import { decodeMaskToRaster, parseImageStream, recordsOfKind, type ResponsesEvent } from '@meta-sam/parser';
import { z } from 'zod';

export interface SegmentationDependencies {
  fetch?: typeof globalThis.fetch;
  env?: NodeJS.ProcessEnv;
}

export interface Detection {
  label: string;
  confidence: number;
  // Source pixels, with exclusive x2 and y2.
  box: { x1: number; y1: number; x2: number; y2: number };
  raster: Uint8Array | null;
  rasterWidth: number;
  rasterHeight: number;
}

export interface ObjectsAnswer {
  answer: string;
  objects: string[];
}

export interface SegmentationResult {
  segmentation: 'ok' | 'empty' | 'failed';
  detections: Detection[];
}

const instructions = 'You answer a question about a photo of the real surroundings of a mixed-reality user, and list what can be segmented in it. Reply with only one JSON object: {"answer": string, "objects": string[]}. answer: report what is visible first, mark anything you infer as a guess, do not state exact measurements, safety judgements or identities as facts, never identify a real person, say what is missing if the photo is too dark or blurred, English, at most 120 words. objects: at most 6 distinct separate objects clearly visible that a person could point at or pick up, smallest and most interesting first, never surfaces or structures such as tables, desks, floors, walls, ceilings or windows, each a short English noun phrase naming one kind of object, such as "ceramic mug", with no commas. Never list people or body parts. Use an empty array if nothing is clear.';
const objectsSchema = z.object({ answer: z.string().min(1).max(2000), objects: z.array(z.unknown()) });
const responseSchema = z.object({
  status: z.literal('completed'),
  output: z.array(z.unknown()),
});
const messageSchema = z.object({ type: z.literal('message'), content: z.array(z.unknown()) });
const textSchema = z.object({ type: z.literal('output_text'), text: z.string() });

async function requestText(
  request: object, signal: AbortSignal, dependencies: SegmentationDependencies,
): Promise<string> {
  const apiKey = (dependencies.env ?? process.env).META_API_KEY;
  if (!apiKey) throw new Error('The model API is not configured.');
  const response = await (dependencies.fetch ?? globalThis.fetch)('https://api.meta.ai/v1/responses', {
    method: 'POST', signal,
    headers: { Authorization: `Bearer ${apiKey}`, 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });
  if (!response.ok) {
    await response.body?.cancel().catch(() => {});
    throw new Error('The model request failed.');
  }
  const body = responseSchema.parse(await response.json());
  return body.output.flatMap(item => {
    const message = messageSchema.safeParse(item);
    return message.success ? message.data.content : [];
  }).flatMap(item => {
    const text = textSchema.safeParse(item);
    return text.success ? [text.data.text] : [];
  }).join('\n');
}

export async function askObjects(
  jpegBase64: string, question: string, dependencies: SegmentationDependencies = {},
): Promise<ObjectsAnswer> {
  try {
    const text = await requestText({
      model: 'muse-spark-1.3', store: false, max_output_tokens: 2000, reasoning: { effort: 'low' },
      instructions,
      input: [{ role: 'user', content: [
        { type: 'input_text', text: question },
        { type: 'input_image', image_url: `data:image/jpeg;base64,${jpegBase64}` },
      ] }],
    }, AbortSignal.timeout(7000), dependencies);
    const parsed = objectsSchema.parse(JSON.parse(text.match(/\{[\s\S]*\}/)?.[0] ?? ''));
    const seen = new Set<string>();
    const objects = parsed.objects.filter((value): value is string => {
      if (typeof value !== 'string' || !/^[A-Za-z][A-Za-z0-9 '-]{1,59}$/.test(value)) return false;
      const key = value.toLowerCase();
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    }).slice(0, 6);
    return { answer: parsed.answer, objects };
  } catch {
    // Do not expose provider errors, response bodies, or request credentials.
    throw new Error('The photo was taken but could not be analysed.');
  }
}

async function* responseEvents(text: string): AsyncIterable<ResponsesEvent> {
  const lane = { item_id: 'sam-output', output_index: 0, content_index: 0 };
  if (text.length > 0) yield { type: 'response.output_text.delta', ...lane, delta: text };
  yield { type: 'response.output_text.done', ...lane, text };
  yield { type: 'response.completed' };
}

export async function segmentObject(
  jpegBase64: string, label: string, signal: AbortSignal, dependencies: SegmentationDependencies = {},
): Promise<Detection[]> {
  try {
    const text = await requestText({
      model: 'sam-3.1', store: false, metadata: { include_confidence: 'true' },
      input: [{ type: 'message', role: 'user', content: [
        { type: 'input_text', text: label },
        { type: 'input_image', image_url: `data:image/jpeg;base64,${jpegBase64}` },
      ] }],
    }, signal, dependencies);
    const result = await parseImageStream(responseEvents(text)).finalResult;
    if (result.outcome.status !== 'completed' || result.diagnostics.some(item => item.severity === 'error'))
      throw new Error('Invalid segmentation output.');
    return recordsOfKind(result.records, 'mask').flatMap(record => {
      // Missing confidence is unknown, not zero; do not invent a score.
      if (record.confidence === undefined || record.confidence < 0.5) return [];
      let raster: Uint8Array | null = null;
      try { raster = decodeMaskToRaster(record.mask); } catch { /* Keep the box when decoding fails. */ }
      return [{
        label, confidence: record.confidence,
        box: { x1: record.bounds.left, y1: record.bounds.top, x2: record.bounds.right, y2: record.bounds.bottom },
        raster, rasterWidth: record.mask.width, rasterHeight: record.mask.height,
      }];
    });
  } catch {
    throw new Error('The photo could not be segmented.');
  }
}

export async function segmentAll(
  jpegBase64: string, objects: readonly string[], dependencies: SegmentationDependencies = {}, timeoutMs = 4000,
): Promise<SegmentationResult> {
  if (objects.length === 0) return { segmentation: 'empty', detections: [] };
  const signal = AbortSignal.timeout(timeoutMs);
  const results = await Promise.allSettled(objects.map(label => segmentObject(jpegBase64, label, signal, dependencies)));
  if (results.every(result => result.status === 'rejected')) return { segmentation: 'failed', detections: [] };
  const detections = results.flatMap(result => result.status === 'fulfilled' ? result.value : [])
    .sort((a, b) => b.confidence - a.confidence).slice(0, 8);
  return { segmentation: detections.length > 0 ? 'ok' : 'empty', detections };
}
