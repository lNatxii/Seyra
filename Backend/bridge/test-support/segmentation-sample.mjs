import { readFile, writeFile } from 'node:fs/promises';
import { parseEnv } from 'node:util';
import { askObjects, segmentAll } from '../dist/segmentation.js';
import { renderSegmentation } from '../dist/segmentation-image.js';

// Resolve all paths from this script, independently of the working directory.
let stage = 'configuration';
const start = performance.now();
try {
  const env = parseEnv(await readFile(new URL('../../.env.local', import.meta.url), 'utf8'));
  if (!env.META_API_KEY) throw new Error('Missing API key.');
  const jpeg = await readFile(new URL('../../../Temp/sam_sample.jpg', import.meta.url));
  const jpegBase64 = jpeg.toString('base64');
  const dependencies = { env };
  stage = 'Muse';
  let phaseStart = performance.now();
  const { objects } = await askObjects(jpegBase64, 'What am I looking at?', dependencies);
  console.log(`Muse: ${Math.round(performance.now() - phaseStart)} ms; labels: ${objects.join(', ') || '(none)'}`);
  stage = 'SAM';
  phaseStart = performance.now();
  const result = await segmentAll(jpegBase64, objects, dependencies);
  console.log(`SAM: ${Math.round(performance.now() - phaseStart)} ms; status: ${result.segmentation}`);
  console.log(`Detections: ${result.detections.map(item => `${item.label} (${Math.round(item.confidence * 100)}%)`).join(', ') || '(none)'}`);
  stage = 'render';
  phaseStart = performance.now();
  const output = await renderSegmentation(jpeg, result.detections);
  await writeFile(new URL('../../../Temp/sam_sample_result.jpg', import.meta.url), output.jpeg);
  console.log(`Render and write: ${Math.round(performance.now() - phaseStart)} ms; ${output.width}x${output.height}; ${output.jpeg.length} bytes`);
  console.log(`Saved Temp/sam_sample_result.jpg; total: ${Math.round(performance.now() - start)} ms`);
  if (result.segmentation === 'failed') process.exitCode = 1;
} catch {
  // Never print provider errors, response bodies, image data, or credentials.
  console.error(`Segmentation sample failed during ${stage}; total: ${Math.round(performance.now() - start)} ms.`);
  process.exitCode = 1;
}
