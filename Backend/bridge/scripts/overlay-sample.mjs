import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';
import { segmentObject } from '../dist/segmentation.js';
import { chooseOriginal, chooseEdited, composeOverlay } from '../dist/camera-overlay.js';
import { editedOverlayPrompt, validateEditInstruction, IMAGE_MODEL } from '../dist/image-policy.js';

const [photo, phrase, instruction] = process.argv.slice(2);
try {
  if (!photo || !phrase || !instruction || !process.env.META_API_KEY)
    throw new Error('Use META_API_KEY and arguments: photo-path object-phrase instruction.');
  validateEditInstruction(instruction);
  const source = await sharp(await readFile(photo)).rotate().resize(1280, 1280, { fit: 'inside', withoutEnlargement: true }).jpeg().toBuffer({ resolveWithObject: true });
  const { width, height } = source.info;
  const jpegBase64 = source.data.toString('base64');
  const original = chooseOriginal(await segmentObject(jpegBase64, phrase, AbortSignal.timeout(15000)), width, height);
  if (!original) throw new Error('No original object found.');
  const response = await fetch('https://api.meta.ai/v1/images/edits', {
    method: 'POST', signal: AbortSignal.timeout(60000),
    headers: { Authorization: `Bearer ${process.env.META_API_KEY}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ model: IMAGE_MODEL, prompt: editedOverlayPrompt(instruction), n: 1,
      images: [{ image_url: 'data:image/jpeg;base64,' + jpegBase64 }], size: width + 'x' + height,
      response_format: 'b64_json', output_format: 'jpeg', reasoning_strength: 'low',
      tool_enablement: { enable_image_search: false, enable_web_search: false, enable_shell: false } }),
  });
  if (!response.ok) { await response.body?.cancel(); throw new Error('Image request failed.'); }
  const body = await response.json();
  if (typeof body?.data?.[0]?.b64_json !== 'string') throw new Error('Missing image data.');
  const edited = Buffer.from(body.data[0].b64_json, 'base64');
  const metadata = await sharp(edited).metadata();
  const detections = await segmentObject(edited.toString('base64'), phrase, AbortSignal.timeout(15000)).catch(() => []);
  const selected = chooseEdited(original, detections, width, height, metadata.width, metadata.height);
  const { png, rect } = await composeOverlay(edited, width, height, original, selected);
  const directory = fileURLToPath(new URL('../../../Progress/Evidence/', import.meta.url));
  await mkdir(directory, { recursive: true });
  const prefix = directory + '/38-' + Date.now();
  await writeFile(prefix + '-overlay.png', png);
  const crop = await sharp(png).resize(rect.x2 - rect.x1, rect.y2 - rect.y1).toBuffer();
  await sharp(source.data).composite([{ input: crop, left: rect.x1, top: rect.y1 }]).png().toFile(prefix + '-composite.png');
  console.log('Overlay evidence written. Visual evaluation is still required.');
} catch {
  console.error('Overlay sample failed. Check arguments, configuration and provider availability. No retry was attempted.');
  process.exitCode = 1;
}
