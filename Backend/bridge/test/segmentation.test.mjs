import assert from 'node:assert/strict';
import test from 'node:test';
import sharp from 'sharp';
import { createCameraService } from '../dist/camera.js';
import { renderSegmentation } from '../dist/segmentation-image.js';

const answer = 'A mug and a notebook are visible.';
const toolResult = data => ({ content: [{ type: 'text', text: JSON.stringify({ success: true, data }) }] });
const responseText = text => new Response(JSON.stringify({ status: 'completed', output: [
  { type: 'message', content: [{ type: 'output_text', text }] },
] }));
const decode = result => JSON.parse(result.content[0].text);
const makeJpeg = () => sharp({ create: { width: 800, height: 600, channels: 3, background: 'white' } }).jpeg().toBuffer();

// Fixed lossless mask from @meta-sam/parser's public README quick start.
const samText = "<0f>0<|box;x1=211;y1=228;x2=270;y2=254;w=800;h=600;c=0.9|><|mask;x=0;y=0;c=0.9;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7`e.ioeAf6-k/#Xd8%dX9x(|>";

async function setup({ objects = ['ceramic mug', 'open notebook'], invalidMuse = false, samFails = false } = {}) {
  const jpeg = await makeJpeg();
  const jpegBase64 = jpeg.toString('base64');
  const calls = [];
  const requests = [];
  const service = createCameraService({
    env: { META_API_KEY: 'mock-key' }, pollMs: 1,
    invoke: async (name, args) => {
      calls.push({ name, args });
      switch (name) {
        case 'camera_capture_start': return toolResult({ state: 'started' });
        case 'camera_capture_result': return toolResult({ state: 'ready', captureId: 'cap_a1b2c3d4e5f6', width: 800, height: 600, jpegBase64 });
        case 'media_deliver': return toolResult({ refreshed: 1 });
        case 'ui_present': return toolResult({ entityId: 'segmentation-card' });
        case 'spatial_place': return toolResult({ applied: true });
        default: assert.fail(`Unexpected invocation: ${name}`);
      }
    },
    fetch: async (_url, options) => {
      const request = JSON.parse(options.body);
      requests.push(request);
      if (request.model === 'muse-spark-1.3')
        return responseText(invalidMuse ? 'This is not JSON.' : JSON.stringify({ answer, objects }));
      assert.equal(request.model, 'sam-3.1');
      return samFails ? new Response(null, { status: 503 }) : responseText(samText);
    },
  });
  return { service, calls, requests, jpegBase64 };
}

test('segment creates a card for two names in delivery, creation and placement order without returning base64', async () => {
  const { service, calls, requests, jpegBase64 } = await setup();
  const result = await service.segment({ question: 'What am I looking at?' });
  const parsed = decode(result);
  assert.equal(parsed.success, true);
  assert.equal(parsed.data.segmentation, 'ok');
  assert.equal(parsed.data.answer, answer);
  assert.deepEqual(parsed.data.objects.map(item => item.label), ['ceramic mug', 'open notebook']);
  assert.deepEqual(parsed.data.panel, { entityId: 'segmentation-card', placed: true });
  assert.equal(requests.filter(item => item.model === 'sam-3.1').length, 2);
  assert.deepEqual(calls.map(item => item.name), [
    'camera_capture_start', 'camera_capture_result', 'media_deliver', 'ui_present', 'spatial_place',
  ]);
  const delivered = calls.find(item => item.name === 'media_deliver').args.media[0];
  assert.ok(delivered.data.length > 0);
  assert.equal(Object.hasOwn(calls.find(item => item.name === 'ui_present').args, 'media'), false);
  const serialized = JSON.stringify(result);
  assert.equal(serialized.includes(jpegBase64), false);
  assert.equal(serialized.includes(delivered.data), false);
  assert.equal(/base64|raster/i.test(serialized), false);
});

test('segment returns empty without SAM or a card when Muse finds no objects', async () => {
  const { service, calls, requests } = await setup({ objects: [] });
  const result = decode(await service.segment({ question: 'What am I looking at?' }));
  assert.equal(result.data.segmentation, 'empty');
  assert.deepEqual(result.data.objects, []);
  assert.equal(result.data.panel, null);
  assert.equal(requests.length, 1);
  assert.equal(requests[0].model, 'muse-spark-1.3');
  assert.equal(calls.some(item => item.name === 'ui_present'), false);
});

test('segment retains the answer without a card when every SAM request fails', async () => {
  const { service, calls, requests } = await setup({ samFails: true });
  const result = decode(await service.segment({ question: 'What am I looking at?' }));
  assert.equal(result.success, true);
  assert.equal(result.data.segmentation, 'failed');
  assert.equal(result.data.answer, answer);
  assert.equal(result.data.panel, null);
  assert.equal(requests.filter(item => item.model === 'sam-3.1').length, 2);
  assert.equal(calls.some(item => item.name === 'ui_present'), false);
});

test('segment reports VISION_FAILED for Muse text that is not JSON', async () => {
  const { service } = await setup({ invalidMuse: true });
  const result = decode(await service.segment({ question: 'What am I looking at?' }));
  assert.equal(result.success, false);
  assert.equal(result.error.code, 'VISION_FAILED');
});

test('renderSegmentation produces a bounded JPEG with a box and no mask when raster is null', async () => {
  const output = await renderSegmentation(await makeJpeg(), [{
    label: 'ceramic mug', confidence: 0.9,
    box: { x1: 100, y1: 100, x2: 700, y2: 500 },
    raster: null, rasterWidth: 0, rasterHeight: 0,
  }]);
  const metadata = await sharp(output.jpeg).metadata();
  assert.equal(metadata.format, 'jpeg');
  assert.equal(metadata.width, output.width);
  assert.equal(metadata.height, output.height);
  assert.ok(Math.max(output.width, output.height) <= 512);
  const { data, info } = await sharp(output.jpeg).removeAlpha().raw().toBuffer({ resolveWithObject: true });
  const pixel = (x, y) => [...data.subarray((y * info.width + x) * 3, (y * info.width + x) * 3 + 3)];
  assert.ok(pixel(256, 192).every(value => value > 245), 'The box interior remains white without a mask.');
  const edge = pixel(64, 192);
  assert.ok(edge[0] > edge[1] + 40 && edge[0] > edge[2] + 40, 'The box has a red outline.');
});
