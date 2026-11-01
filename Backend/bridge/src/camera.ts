import { setTimeout as delay } from 'node:timers/promises';
import { z } from 'zod';

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
} {
  const { invoke } = dependencies;
  const fetchVision = dependencies.fetch ?? globalThis.fetch;
  const env = dependencies.env ?? process.env;
  const now = dependencies.now ?? Date.now;
  const pollMs = dependencies.pollMs ?? 250;
  let busy = false;
  let recent: number[] = [];

  async function look(args: { question: string }): Promise<ToolResult> {
    if (!env.META_API_KEY) return failure('CAMERA_NOT_CONFIGURED', 'Vision is not configured.');
    recent = recent.filter(time => time > now() - 600_000);
    if (busy) return failure('CAMERA_BUSY', 'A photo is already being taken.');
    if (recent.length >= 20) return failure('CAMERA_LIMIT', 'Too many photos. Ask the user to wait.');
    busy = true;
    recent.push(now());
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
        try {
          const response = await fetchVision('https://api.meta.ai/v1/responses', {
            method: 'POST', signal: AbortSignal.timeout(13_000),
            headers: { Authorization: `Bearer ${env.META_API_KEY}`, 'Content-Type': 'application/json' },
            body: JSON.stringify({
              model: 'muse-spark-1.3', store: false, max_output_tokens: 2000, reasoning: { effort: 'low' },
              instructions,
              input: [{ role: 'user', content: [
                { type: 'input_text', text: args.question },
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
    } catch (error) {
      if (error !== captureTimeout) throw error;
      // Cancellation gets the remaining margin, never a second full Unity timeout.
      try { await boundedInvoke('camera_capture_cancel', 1000); } catch { /* Do not retry cancellation. */ }
      return failure('CAMERA_TIMEOUT', 'The camera did not answer in time. If a permission prompt appeared, ask the user to accept it and ask again.');
    } finally { busy = false; }
  }
  return { look };
}
