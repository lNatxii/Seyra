import sharp from 'sharp';
import { setTimeout as delay } from 'node:timers/promises';
import { IMAGE_MODEL, ImagePolicyError, generatedImageId, generatedImagePrompt, validateGeneratedImages, type GeneratedImage } from './image-policy.js';
import type { MediaEntry } from './media.js';

type Outcome = { success: true; data: Record<string, unknown> } |
  { success: false; error: { code: string; message: string }; outcomeUnknown?: boolean };
export type ImageToolResult = { isError?: boolean; content: { type: 'text'; text: string }[] };
export interface ImageServiceDependencies {
  fetch?: typeof globalThis.fetch;
  invoke: (name: string, args: unknown) => Promise<ImageToolResult>;
  env?: NodeJS.ProcessEnv;
  now?: () => number;
}
type Job = { id: string; image: GeneratedImage; releaseReservation: () => void };
const respond = (outcome: Outcome): ImageToolResult => ({ isError: !outcome.success, content: [{ type: 'text', text: JSON.stringify(outcome) }] });
const failure = (code: string, message: string): ImageToolResult => respond({ success: false, error: { code, message } });
function outcome(result: ImageToolResult): Outcome | undefined {
  try {
    const parsed = JSON.parse(result.content.find(item => item.type === 'text')?.text ?? '');
    if (parsed?.success === true && !result.isError && parsed.data && typeof parsed.data === 'object') return parsed;
    if (parsed?.success === false) return parsed;
  } catch { /* Unconfirmed Unity results must never trigger generation. */ }
  return undefined;
}

export function createImageService(dependencies: ImageServiceDependencies) {
  const fetchImage = dependencies.fetch ?? globalThis.fetch;
  const { invoke } = dependencies;
  const env = dependencies.env ?? process.env;
  const now = dependencies.now ?? Date.now;
  function limit(name: string, fallback: number, maximum: number): number {
    const value = Number(env[name] ?? fallback);
    if (!Number.isSafeInteger(value) || value < 0 || value > maximum) throw new Error(`Invalid ${name}`);
    return value;
  }
  const windowLimit = limit('SEYRA_IMAGE_WINDOW_LIMIT', 30, Number.MAX_SAFE_INTEGER);
  const sessionLimit = limit('SEYRA_IMAGE_SESSION_LIMIT', 100, 100);
  let sent = 0, reserved = 0, active = 0;
  let recent: number[] = [];
  const queue: Job[] = [];
  // Keep IDs deduplicated through generation AND delivery, including failure delivery.
  const inFlight = new Set<string>();
  const background = new Set<Promise<void>>();
  const track = (task: Promise<void>) => {
    background.add(task);
    void task.finally(() => background.delete(task)).catch(() => {});
  };
  function available(): boolean {
    recent = recent.filter(time => time > now() - 600_000);
    return sent + reserved < sessionLimit && recent.length + reserved < windowLimit;
  }
  function countSend(): boolean {
    if (!available()) return false;
    sent++; recent.push(now()); return true;
  }
  async function deliver(args: { media?: MediaEntry[]; failed?: string[] }): Promise<void> {
    const deadline = now() + 120_000;
    // Wall time also bounds retries when tests inject a stationary clock.
    const wallDeadline = Date.now() + 120_000;
    for (;;) {
      try {
        const result = outcome(await invoke('media_deliver', args));
        if (result?.success) return;
        if (result && !result.success && !result.outcomeUnknown &&
          !['UNITY_NOT_CONNECTED', 'UNITY_DISCONNECTED', 'UNITY_TIMEOUT', 'BRIDGE_BUSY', 'BRIDGE_INVALID_RESULT'].includes(result.error.code)) return;
      } catch { /* Delivery is idempotent; retry transport failures within the delivery window. */ }
      const remaining = Math.min(deadline - now(), wallDeadline - Date.now());
      if (remaining < 5000) return;
      await delay(5000);
      if (now() >= deadline || Date.now() >= wallDeadline) return;
    }
  }
  async function readResponse(response: Response, signal: AbortSignal): Promise<Buffer> {
    const maximum = 16 * 1024 * 1024;
    if (Number(response.headers.get('content-length')) > maximum) {
      await response.body?.cancel().catch(() => {});
      throw new Error('Image response too large.');
    }
    if (!response.body) throw new Error('Empty image response.');
    const reader = response.body.getReader();
    const chunks: Buffer[] = []; let length = 0;
    try {
      for (;;) {
        signal.throwIfAborted();
        const { done, value } = await reader.read();
        if (done) break;
        length += value.byteLength;
        if (length > maximum) throw new Error('Image response too large.');
        chunks.push(Buffer.from(value));
      }
      return Buffer.concat(chunks, length);
    } finally { await reader.cancel().catch(() => {}); reader.releaseLock(); }
  }
  async function generate(job: Job, signal: AbortSignal): Promise<MediaEntry> {
    for (let attempt = 0; ; attempt++) {
      signal.throwIfAborted();
      if (attempt === 0) job.releaseReservation();
      if (!countSend()) throw new Error('Image budget exhausted.');
      const response = await fetchImage('https://api.meta.ai/v1/images/generations', {
        method: 'POST', signal,
        headers: { Authorization: `Bearer ${env.META_API_KEY}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          model: IMAGE_MODEL, prompt: generatedImagePrompt(job.image), n: 1, size: '1024x1024',
          response_format: 'b64_json', output_format: 'jpeg', reasoning_strength: 'low',
          tool_enablement: { enable_image_search: false, enable_web_search: false, enable_shell: false },
        }),
      });
      if (response.status === 429 && attempt === 0) {
        const header = response.headers.get('retry-after');
        const seconds = header === null ? 1 : Number(header);
        const wait = Number.isFinite(seconds) ? seconds * 1000 : Date.parse(header!) - now();
        await response.body?.cancel().catch(() => {});
        await delay(Math.max(0, Math.min(2000, Number.isFinite(wait) ? wait : 1000)), undefined, { signal });
        continue;
      }
      if (!response.ok) { await response.body?.cancel().catch(() => {}); throw new Error('Image API failed.'); }
      const parsed = JSON.parse((await readResponse(response, signal)).toString('utf8'));
      const encoded = parsed?.data?.[0]?.b64_json;
      if (typeof encoded !== 'string' || !encoded.length) throw new Error('Missing image data.');
      const converted = await sharp(Buffer.from(encoded, 'base64'), { limitInputPixels: 40_000_000, animated: false })
        .rotate().resize({ width: 512, height: 512, fit: 'inside', withoutEnlargement: true })
        .jpeg({ quality: 82 }).toBuffer({ resolveWithObject: true });
      signal.throwIfAborted();
      if (converted.data.length > 1024 * 1024) throw new Error('Converted image too large.');
      return { id: job.id, mime: 'image/jpeg', width: converted.info.width, height: converted.info.height, data: converted.data.toString('base64') };
    }
  }
  async function run(job: Job): Promise<void> {
    const controller = new AbortController();
    let timer: NodeJS.Timeout | undefined;
    const timeout = new Promise<never>((_, reject) => {
      timer = setTimeout(() => { controller.abort(); reject(new Error('Image generation timed out.')); }, 60_000);
    });
    let args: { media?: MediaEntry[]; failed?: string[] };
    try { args = { media: [await Promise.race([generate(job, controller.signal), timeout])] }; }
    catch { args = { failed: [job.id] }; }
    finally { clearTimeout(timer); job.releaseReservation(); }
    // Deliveries must not hold one of the three generation slots.
    track(deliver(args).finally(() => inFlight.delete(job.id)));
  }
  function pump(): void {
    while (active < 3 && queue.length) {
      const job = queue.shift()!; active++;
      track(run(job).finally(() => { active--; pump(); }));
    }
  }

  async function execute(name: string, args: unknown): Promise<ImageToolResult> {
    if (!['ui_present', 'ui_update', 'layout_create'].includes(name)) return invoke(name, args);
    const rewritten = structuredClone(args) as Record<string, any>;
    const cards: Record<string, any>[] = name === 'layout_create'
      ? (Array.isArray(rewritten.items) ? rewritten.items.map((item: any) => item.presentation).filter(Boolean) : [])
      : [rewritten.presentation].filter(Boolean);
    const generated = cards.filter(card => card.generatedImage !== undefined);
    let images: GeneratedImage[];
    try { images = validateGeneratedImages(generated.map(card => card.generatedImage)); }
    catch (error) {
      if (error instanceof ImagePolicyError) return failure(error.code, error.message);
      return failure('IMAGE_SUBJECT_REJECTED', 'This subject cannot have a generated image. Use a text panel or a card without an image.');
    }
    if (!generated.length) return invoke(name, args);
    if (generated.some(card => card.type !== 'mediaCard' || card.image !== undefined))
      return failure('INVALID_ARGUMENT', 'Only mediaCard accepts generatedImage, with exactly one of image or generatedImage.');
    const unique = new Map<string, GeneratedImage>();
    generated.forEach((card, index) => {
      const image = images[index], id = generatedImageId(image);
      unique.set(id, image);
      delete card.generatedImage;
      card.image = { imageRef: id, title: card.title, alt: image.alt, source: 'AI-generated illustration' };
    });
    let status: Outcome | undefined;
    try { status = outcome(await invoke('media_status', { ids: [...unique.keys()] })); }
    catch { return failure('IMAGE_STATUS_UNAVAILABLE', 'Could not confirm image availability. No content was created.'); }
    if (!status?.success || !Array.isArray(status.data.existing))
      return failure('IMAGE_STATUS_UNAVAILABLE', 'Could not confirm image availability. No content was created.');
    const existing = new Set(status.data.existing.filter((id): id is string => typeof id === 'string'));
    rewritten.pendingMedia = [...unique.keys()].filter(id => !existing.has(id));
    const result = await invoke(name, rewritten);
    const applied = outcome(result);
    if (!applied?.success) return result;
    let queued = 0, reused = 0, skipped = 0;
    let reason: 'IMAGE_BUDGET_EXHAUSTED' | 'IMAGE_NOT_CONFIGURED' | undefined;
    for (const [id, image] of unique) {
      if (existing.has(id) || inFlight.has(id)) { reused++; continue; }
      if (!env.META_API_KEY?.trim() || !available()) {
        skipped++;
        reason = !env.META_API_KEY?.trim() ? 'IMAGE_NOT_CONFIGURED' : 'IMAGE_BUDGET_EXHAUSTED';
        inFlight.add(id);
        track(deliver({ failed: [id] }).finally(() => inFlight.delete(id)));
        continue;
      }
      reserved++; inFlight.add(id); queued++;
      let reservation = true;
      queue.push({ id, image, releaseReservation: () => { if (reservation) { reservation = false; reserved--; } } });
    }
    pump();
    return respond({ success: true, data: { ...applied.data, images: { queued, reused, skipped, ...(reason ? { reason } : {}) } } });
  }
  return {
    execute,
    /** Wait for mocked background work in checks; production returns tool results immediately. */
    async whenIdle(): Promise<void> { while (background.size) await Promise.allSettled([...background]); },
  };
}
