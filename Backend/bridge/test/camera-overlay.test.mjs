import assert from 'node:assert/strict';
import test from 'node:test';
import sharp from 'sharp';
import { chooseOriginal, chooseEdited, composeOverlay } from '../dist/camera-overlay.js';
import { createCameraService } from '../dist/camera.js';
import { createImageService } from '../dist/images.js';

const detection = (box, confidence = 0.9) => ({ label: 'cabinet', confidence, box,
  raster: new Uint8Array(100).fill(1), rasterWidth: 10, rasterHeight: 10 });
const box = (x1, y1, x2, y2) => ({ x1, y1, x2, y2 });
const photo = () => sharp({ create: { width: 800, height: 600, channels: 3, background: 'white' } }).jpeg().toBuffer();

test('overlay selects by photo center and normalized IoU', () => {
  const center = detection(box(300, 200, 500, 400), 0.8);
  const outside = detection(box(10, 10, 80, 80));
  assert.equal(chooseOriginal([outside, center], 800, 600), center);
  const confident = detection(center.box, 0.95);
  assert.equal(chooseOriginal([center, confident], 800, 600), confident);
  const near = detection(box(500, 200, 600, 400));
  assert.equal(chooseOriginal([outside, near], 800, 600), near);
  const edited = detection(box(600, 400, 1000, 800));
  assert.equal(chooseEdited(center, [outside, edited], 800, 600, 1600, 1200), edited);
  assert.equal(chooseEdited(center, [outside], 800, 600, 800, 600), undefined);
});

test('overlay unions masks, bounds the rect and preserves transparent and opaque pixels', async () => {
  const jpeg = await photo();
  const original = detection(box(200, 150, 400, 450));
  const edited = detection(box(350, 150, 650, 450));
  const single = await composeOverlay(jpeg, 800, 600, original);
  const union = await composeOverlay(jpeg, 800, 600, original, edited);
  assert.ok(union.rect.x2 > single.rect.x2);
  const { data, info } = await sharp(union.png).raw().toBuffer({ resolveWithObject: true });
  assert.equal(info.channels, 4);
  assert.equal(data[3], 0);
  assert.equal(data[(Math.floor(info.height / 2) * info.width + Math.floor(info.width / 2)) * 4 + 3], 255);
  const bounded = await composeOverlay(jpeg, 800, 600, detection(box(0, 0, 800, 600)));
  assert.deepEqual(bounded.rect, box(0, 0, 800, 600));
});

test('overlay rejects a changed image aspect ratio', async () => {
  const jpeg = await photo();
  await assert.rejects(composeOverlay(jpeg, 800, 800, detection(box(200, 200, 500, 500))), /ratio_mismatch/);
});

test('SAM and Unity overlay failures preserve the delivered card without retry', async () => {
  // Fixed lossless parser fixture, enlarged in photo coordinates for depth sampling.
  const sam = "<0f>0<|box;x1=200;y1=150;x2=600;y2=450;w=800;h=600;c=0.9|><|mask;x=0;y=0;c=0.9;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7`e.ioeAf6-k/#Xd8%dX9x(|>";
  const jpeg = (await photo()).toString('base64');
  for (const samFails of [true, false]) {
    const calls = [];
    const result = data => ({ content: [{ type: 'text', text: JSON.stringify({ success: true, data }) }] });
    const invoke = async (name, args) => {
      calls.push({ name, args });
      if (name === 'camera_capture_start') return result({ state: 'started' });
      if (name === 'camera_capture_result') return result({ state: 'ready', captureId: 'cap_a1b2c3d4e5f6', width: 800, height: 600, jpegBase64: jpeg });
      if (name === 'ui_present') return result({ entityId: 'card' });
      if (name === 'camera_overlay_show') throw new Error('Unity unavailable.');
      return result({ applied: true });
    };
    const fetch = async (url) => url.endsWith('/edits')
      ? new Response(JSON.stringify({ data: [{ b64_json: jpeg }] }))
      : samFails ? new Response(null, { status: 503 }) : new Response(JSON.stringify({ status: 'completed', output: [
        { type: 'message', content: [{ type: 'output_text', text: sam }] },
      ] }));
    const env = { META_API_KEY: 'mock-key' };
    const images = createImageService({ invoke, fetch, env });
    const camera = createCameraService({ invoke, fetch, env, images, pollMs: 0 });
    const response = JSON.parse((await camera.edit({ instruction: 'Make the cabinet darker', title: 'Cabinet', overlayObject: 'cabinet' })).content[0].text);
    await images.whenIdle();
    assert.equal(response.success, true);
    assert.equal(response.data.overlay, 'queued');
    assert.deepEqual(calls.find(c => c.name === 'ui_present').args.presentation.check, { label: 'Show original', completed: false });
    assert.equal(calls.filter(c => c.name === 'media_deliver').length, 1);
    assert.ok(calls.find(c => c.name === 'media_deliver').args.media);
    assert.equal(calls.filter(c => c.name === 'camera_overlay_show').length, samFails ? 0 : 1);
  }
});
