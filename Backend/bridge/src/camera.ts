import { setTimeout as delay } from 'node:timers/promises';
import { z } from 'zod';
import { createHash } from 'node:crypto';
import { askObjects, samplePoints, segmentAll, type Detection } from './segmentation.js';
import { palette, renderSegmentation } from './segmentation-image.js';

export type ToolResult = { isError?: boolean; content: { type: 'text'; text: string }[] };
export interface CameraServiceDependencies {
  invoke: (name: string, args: unknown) => Promise<ToolResult>;
  fetch?: typeof globalThis.fetch;
  env?: NodeJS.ProcessEnv;
  now?: () => number;
  pollMs?: number;
}

export const cameraTool = {
  name: 'camera_look', readOnly: true,
  description: 'Takes one photo of what the user is looking at, with their headset camera, and answers a question about it. The user hears a shutter and sees the photo. Call it only when the user\'s latest message needs you to see their real surroundings. question must be complete and self-contained: the vision model sees only the photo and this text, not the conversation. Returns text. Shows the real world only, never virtual content. Do not retry automatically after a failure.',
  schema: z.object({ question: z.string().trim().min(1).max(1000) }).strict(),
};

export const cameraSegmentTool = {
  name: 'camera_segment', readOnly: false,
  description: "Takes one photo of what the user is looking at, answers a question about it, finds the objects in it and places a panel showing the photo with each object coloured, boxed and labelled. The user hears a shutter and sees the photo. Call it only when the user's latest message asks what they are looking at or what is around them. question must be complete and self-contained: the vision model sees only the photo and this text, not the conversation. Returns text. Shows the real world only, never virtual content. Do not retry automatically after a failure.",
  schema: cameraTool.schema,
};
type Capture = { jpegBase64: string; captureId: unknown; width: unknown; height: unknown };

const instructions = 'You answer a question about a photo of the real surroundings of a mixed-reality user. Report what is visible first. Mark anything you infer as a guess. Do not state exact measurements, safety judgements or identities as facts. Never identify a real person from their appearance. If the photo is too dark, blurred or does not show what is asked, say what is missing. Answer in English in at most 120 words.';
const respond = (outcome: object, isError = false): ToolResult => ({ isError, content: [{ type: 'text', text: JSON.stringify(outcome) }] });
const failure = (code: string, message: string) => respond({ success: false, error: { code, message } }, true);
type Outcome = { success: true; data: Record<string, unknown> } | { success: false };
function outcome(result: ToolResult): Outcome | undefined {
  try {
    const parsed = JSON.parse(result.content.find(item => item.type === 'text')?.text ?? '');
    if (parsed?.success === false) return parsed;
    if (!result.isError && parsed?.success === true && parsed.data && typeof parsed.data === 'object') return parsed;
  } catch { /* Invalid runtime results cannot be used as camera frames. */ }
  return undefined;
}

export function createCameraService(dependencies: CameraServiceDependencies): {
  look(args: { question: string }): Promise<ToolResult>;
  segment(args: { question: string }): Promise<ToolResult>;
} {
  const { invoke } = dependencies;
  const fetchVision = dependencies.fetch ?? globalThis.fetch;
  const env = dependencies.env ?? process.env;
  const now = dependencies.now ?? Date.now;
  const pollMs = dependencies.pollMs ?? 250;
  let busy = false;
  let recent: number[] = [];

  async function run(args: { question: string }, segmented: boolean): Promise<ToolResult> {
    const started = now();
    const wallStarted = Date.now();
    if (!env.META_API_KEY) return failure('CAMERA_NOT_CONFIGURED', 'Vision is not configured.');
    recent = recent.filter(time => time > now() - 600_000);
    if (busy) return failure('CAMERA_BUSY', 'A photo is already being taken.');
    if (recent.length >= 20) return failure('CAMERA_LIMIT', 'Too many photos. Ask the user to wait.');
    busy = true;
    recent.push(now());
    try {
      const captured = await capture();
      if ('content' in captured) return captured;
      return segmented ? await segmentCaptured(captured, args.question, started, wallStarted) : await lookCaptured(captured, args.question);
    } finally { busy = false; }
  }

  async function capture(): Promise<Capture | ToolResult> {
    const deadline = now() + 10_000;
    const wallDeadline = Date.now() + 10_000;
    const remaining = () => Math.min(deadline - now(), wallDeadline - Date.now());
    const captureTimeout = new Error('Camera capture timed out.');
    async function boundedInvoke(name: string, ms: number): Promise<ToolResult> {
      let timer: NodeJS.Timeout | undefined;
      try {
        return await Promise.race([invoke(name, {}), new Promise<never>((_, reject) => {
          timer = setTimeout(() => reject(captureTimeout), Math.max(0, ms));
        })]);
      } finally { clearTimeout(timer); }
    }
    try {
      let result = await boundedInvoke('camera_capture_start', remaining());
      let parsed = outcome(result);
      if (!parsed || !parsed.success) return result;
      if (parsed.data.state === 'unavailable') return failure('CAMERA_UNAVAILABLE', 'The camera is not available.');
      if (parsed.data.state === 'busy') return failure('CAMERA_BUSY', 'A photo is already being taken.');
      for (;;) {
        if (remaining() <= 0) throw captureTimeout;
        await delay(Math.min(pollMs, remaining()));
        if (remaining() <= 0) throw captureTimeout;
        result = await boundedInvoke('camera_capture_result', remaining());
        parsed = outcome(result);
        if (!parsed || !parsed.success) return result;
        const data = parsed.data;
        if (data.state === 'unavailable') return failure('CAMERA_UNAVAILABLE', 'The camera is not available.');
        if (data.state === 'busy') return failure('CAMERA_BUSY', 'A photo is already being taken.');
        if (data.state === 'failed') return failure('CAMERA_FAILED', typeof data.message === 'string' ? data.message : 'The camera could not take a photo.');
        if (data.state !== 'ready') continue;
        if (typeof data.jpegBase64 !== 'string' || !data.jpegBase64.length)
          return failure('VISION_FAILED', 'The photo was taken but could not be analysed.');
        return { jpegBase64: data.jpegBase64, captureId: data.captureId, width: data.width, height: data.height };
      }
    } catch (error) {
      if (error !== captureTimeout) throw error;
      // Cancellation gets the remaining margin, never a second full Unity timeout.
      try { await boundedInvoke('camera_capture_cancel', 1000); } catch { /* Do not retry cancellation. */ }
      return failure('CAMERA_TIMEOUT', 'The camera did not answer in time. If a permission prompt appeared, ask the user to accept it and ask again.');
    }
  }
  async function lookCaptured(data: Capture, question: string): Promise<ToolResult> {
    try {
      const response = await fetchVision('https://api.meta.ai/v1/responses', {
        method: 'POST', signal: AbortSignal.timeout(13_000),
        headers: { Authorization: `Bearer ${env.META_API_KEY}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          model: 'muse-spark-1.3', store: false, max_output_tokens: 2000, reasoning: { effort: 'low' },
          instructions,
          input: [{ role: 'user', content: [
            { type: 'input_text', text: question },
            { type: 'input_image', image_url: `data:image/jpeg;base64,${data.jpegBase64}` },
          ] }],
        }),
      });
      if (!response.ok) {
        await response.body?.cancel().catch(() => {});
        return failure('VISION_FAILED', 'The photo was taken but could not be analysed.');
      }
      const body = await response.json();
      const answer = body.status === 'completed' && Array.isArray(body.output)
        ? body.output.filter((item: any) => item?.type === 'message' && Array.isArray(item.content))
          .flatMap((item: any) => item.content)
          .filter((item: any) => item?.type === 'output_text' && typeof item.text === 'string')
          .map((item: any) => item.text).join('\n').trim()
        : '';
      if (!answer) return failure('VISION_FAILED', 'The photo was taken but could not be analysed.');
      return respond({ success: true, data: { answer, captureId: data.captureId, width: data.width, height: data.height } });
    } catch {
      return failure('VISION_FAILED', 'The photo was taken but could not be analysed.');
    }
  }
  async function segmentCaptured(data: Capture, question: string, started: number, wallStarted: number): Promise<ToolResult> {
    let answer: string;
    let objects: string[];
    try { ({ answer, objects } = await askObjects(data.jpegBase64, question, { fetch: fetchVision, env })); }
    catch { return failure('VISION_FAILED', 'The photo was taken but could not be analysed.'); }
    let detections: Detection[] = [];
    const vector = z.object({ x: z.number().finite(), y: z.number().finite(), z: z.number().finite() });
    const regionSchema = z.object({ position: vector, rotation: vector });
    const positions: ({ x: number; y: number; z: number } | null)[] = [];
    let labels: { rootId: string; located: number; total: number } | null = null;
    const paletteColor = (index: number) => {
      const hex = palette[index % palette.length]!;
      return { r: parseInt(hex.slice(1, 3), 16) / 255, g: parseInt(hex.slice(3, 5), 16) / 255,
        b: parseInt(hex.slice(5, 7), 16) / 255, a: 1 };
    };
    const respondSegment = (segmentation: 'ok' | 'empty' | 'failed', panel: { entityId: string; placed: boolean } | null = null) =>
      respond({ success: true, data: {
        answer, captureId: data.captureId, width: data.width, height: data.height, segmentation,
        objects: detections.map(({ label, confidence, box }, index) => ({ label, confidence, box, position: positions[index] ?? null })), panel, labels,
      } });
    if (objects.length === 0) return respondSegment('empty');
    const elapsed = Math.max(now() - started, Date.now() - wallStarted);
    const timeoutMs = Math.min(8000, 21000 - elapsed);
    if (timeoutMs <= 0) return respondSegment('failed');
    try {
      const result = await segmentAll(data.jpegBase64, objects, { fetch: fetchVision, env }, timeoutMs);
      detections = result.detections;
      if (result.segmentation !== 'ok') return respondSegment(result.segmentation);
      const output = await renderSegmentation(Buffer.from(data.jpegBase64, 'base64'), detections);
      const imageRef = `gen:${createHash('sha256').update(output.jpeg).digest('hex').slice(0, 32)}`;
      const delivered = outcome(await invoke('media_deliver', { media: [{
        id: imageRef, mime: 'image/jpeg', width: output.width, height: output.height, data: output.jpeg.toString('base64'),
      }] }));
      if (!delivered?.success) return respondSegment('failed');
      const created = outcome(await invoke('ui_present', {
        name: 'Segmentation', parentId: 'Generated',
        position: { x: 0, y: 1.4, z: 1 }, rotation: { x: 0, y: 0, z: 0 }, scale: { x: 1, y: 1, z: 1 },
        width: 0.7, height: 1,
        presentation: {
          id: `segmentation-${data.captureId}`, type: 'mediaCard', title: 'What I can see',
          image: { imageRef, title: 'Segmented photo',
            alt: `Photo of the surroundings with labelled objects: ${detections.map(item => item.label).join(', ')}`.slice(0, 200),
            source: 'Camera photo, segmented by SAM 3.1' },
          body: answer,
        },
      }));
      if (!created?.success || typeof created.data.entityId !== 'string') return respondSegment('failed');
      const entityId = created.data.entityId;
      let placed = false;
      try {
        const placement = outcome(await invoke('spatial_place', { entityId, destination: 'air', allowAirFallback: true, minScale: 1 }));
        placed = placement?.success === true && placement.data.applied === true;
      } catch { /* Do not retry an unconfirmed placement. */ }
      try {
        const located = outcome(await invoke('camera_locate', {
          captureId: data.captureId, regions: detections.map(detection => ({ points: samplePoints(detection) })),
        }));
        if (located?.success && located.data.state === 'ready' && Array.isArray(located.data.regions)) {
          const regions = located.data.regions;
          const nodes = detections.flatMap((detection, index) => {
            const parsed = regionSchema.safeParse(regions[index]);
            if (!parsed.success) return [];
            const { position, rotation } = parsed.data;
            positions[index] = position;
            const color = paletteColor(index);
            return [
              { key: `dot_${index}`, name: `${detection.label} point`, type: 'primitive', primitiveType: 'sphere',
                transform: { position, scale: { x: 0.03, y: 0.03, z: 0.03 } },
                appearance: { style: 'unlit', color } },
              { key: `label_${index}`, name: `${detection.label} label`, type: 'panel',
                transform: { position: { ...position, y: position.y + 0.1 }, rotation },
                panel: { version: 1, kind: 'label', title: '', text: detection.label, width: 0.2, height: 0.05, fontSize: 24 } },
              { key: `line_${index}`, name: `${detection.label} line`, type: 'line',
                line: { points: [{ key: `dot_${index}` }, { key: `label_${index}` }], color, width: 0.003 } },
            ];
          });
          if (nodes.length > 0) {
            const built = outcome(await invoke('scene_build', { name: 'Object labels', nodes }));
            if (built?.success && typeof built.data.rootId === 'string')
              labels = { rootId: built.data.rootId, located: nodes.length / 3, total: detections.length };
          }
        }
      } catch { /* Localization and labels must never invalidate the photo card. Do not retry. */ }
      return respondSegment('ok', { entityId, placed });
    } catch { return respondSegment('failed'); }
  }
  return { look: args => run(args, false), segment: args => run(args, true) };
}
