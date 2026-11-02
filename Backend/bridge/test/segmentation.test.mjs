import assert from 'node:assert/strict';
import test from 'node:test';
import sharp from 'sharp';
import { createCameraService } from '../dist/camera.js';
import { renderSegmentation } from '../dist/segmentation-image.js';
import { samplePoints } from '../dist/segmentation.js';
import { tools } from '../dist/tools.js';

const answer = 'A mug and a notebook are visible.';
const toolResult = data => ({ content: [{ type: 'text', text: JSON.stringify({ success: true, data }) }] });
const responseText = text => new Response(JSON.stringify({ status: 'completed', output: [
  { type: 'message', content: [{ type: 'output_text', text }] },
] }));
const decode = result => JSON.parse(result.content[0].text);
const makeJpeg = () => sharp({ create: { width: 800, height: 600, channels: 3, background: 'white' } }).jpeg().toBuffer();

// Fixed lossless mask from @meta-sam/parser's public README quick start.
const samText = "<0f>0<|box;x1=211;y1=228;x2=270;y2=254;w=800;h=600;c=0.9|><|mask;x=0;y=0;c=0.9;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7`e.ioeAf6-k/#Xd8%dX9x(|>";

async function setup({ objects = ['ceramic mug', 'open notebook'], invalidMuse = false, samFails = false,
  locate = { state: 'no_depth', regions: [] } } = {}) {
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
        case 'camera_locate': return toolResult(locate);
        case 'scene_build': return toolResult({ rootId: 'object-labels', nodes: Object.fromEntries(args.nodes.map(node => [node.key, `entity-${node.key}`])) });
        case 'camera_marks_selectable': return toolResult({ state: 'bound', objects: args.objects.length });
        case 'camera_marks_bind': return toolResult({ state: 'bound' });
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
    'camera_capture_start', 'camera_capture_result', 'media_deliver', 'camera_locate', 'ui_present', 'spatial_place',
  ]);
  const delivered = calls.find(item => item.name === 'media_deliver').args.media[0];
  assert.ok(delivered.data.length > 0);
  assert.equal(Object.hasOwn(calls.find(item => item.name === 'ui_present').args, 'media'), false);
  const serialized = JSON.stringify(result);
  assert.equal(serialized.includes(jpegBase64), false);
  assert.equal(serialized.includes(delivered.data), false);
  assert.equal(/base64|raster/i.test(serialized), false);
});

test('samplePoints stays within a known mask and box with at most 24 samples', () => {
  const raster = Uint8Array.from({ length: 400 }, (_, index) => index % 20 >= 4 && index % 20 < 16 ? 1 : 0);
  const detection = { label: 'mug', confidence: 0.9, box: { x1: 100, y1: 50, x2: 300, y2: 250 },
    raster, rasterWidth: 20, rasterHeight: 20 };
  const points = samplePoints(detection);
  assert.ok(points.length > 0 && points.length <= 24);
  for (const point of points) {
    assert.ok(point.x >= 100 && point.x < 300 && point.y >= 50 && point.y < 250);
    const column = Math.floor((point.x - 100) / 10);
    const row = Math.floor((point.y - 50) / 10);
    assert.equal(raster[row * 20 + column], 1);
  }
});

test('segment locates objects before card creation and builds labels in detection colours', async () => {
  const position = { x: 0.1, y: 0.8, z: 1.2 };
  const rotation = { x: 10, y: 180, z: 0 };
  const { service, calls } = await setup({ locate: { state: 'ready', regions: [null, { position, rotation, points: [position] }] } });
  const result = decode(await service.segment({ question: 'What am I looking at?' }));
  assert.deepEqual(calls.slice(3).map(item => item.name), ['camera_locate', 'ui_present', 'spatial_place', 'scene_build', 'camera_marks_selectable']);
  assert.equal(result.data.objects[0].position, null);
  assert.deepEqual(result.data.objects[1].position, position);
  assert.deepEqual(result.data.labels, { rootId: 'object-labels', located: 1, total: 2 });
  assert.equal(JSON.stringify(result).includes('points'), false);
  const build = calls.find(item => item.name === 'scene_build').args;
  assert.equal(Object.hasOwn(build, 'placement'), false);
  assert.equal(build.nodes.length, 3);
  assert.equal(build.nodes[0].primitiveType, 'sphere');
  assert.equal(build.nodes[0].key, 'dot_1');
  assert.deepEqual(build.nodes[0].appearance.color, { r: 45 / 255, g: 212 / 255, b: 191 / 255, a: 1 });
  assert.deepEqual(build.nodes[2].line.color, build.nodes[0].appearance.color);
  assert.deepEqual(build.nodes[1].transform.rotation, rotation);
  assert.equal(build.nodes[1].panel.chip, true);
  assert.equal(build.nodes[1].panel.fontSize, 22);
  assert.equal(build.nodes[1].panel.width, 0.05 + 'open notebook'.length * 0.013);
  tools.find(item => item.name === 'scene_build').schema.parse(build);
});

test('segment builds a translucent box and falls back to a point while counting located detections', async () => {
  const position = { x: 0.1, y: 0.8, z: 1.2 };
  const rotation = { x: 10, y: 180, z: 0 };
  const box = { center: { x: 0.1, y: 0.85, z: 1.3 }, rotation: { x: 0, y: 180, z: 0 },
    size: { x: 0.09, y: 0.11, z: 0.09 } };
  const { service, calls } = await setup({ locate: { state: 'ready', regions: [
    { position, rotation, box }, { position, rotation },
  ] } });
  const result = decode(await service.segment({ question: 'What am I looking at?' }));
  const regions = calls.find(item => item.name === 'camera_locate').args.regions;
  assert.deepEqual(regions.map(region => region.box), result.data.objects.map(item => item.box));
  assert.ok(regions.every(region => region.points.length > 0));
  assert.deepEqual(result.data.labels, { rootId: 'object-labels', located: 2, total: 2 });
  assert.ok(result.data.objects.every(item => JSON.stringify(item.position) === JSON.stringify(position)));
  const build = calls.find(item => item.name === 'scene_build').args;
  const node = key => build.nodes.find(item => item.key === key);
  assert.equal(build.nodes.length, 10);
  assert.deepEqual(node('box_0').transform, { position: box.center, rotation: box.rotation, scale: box.size });
  assert.equal(node('box_0').appearance.color.a, 0.25);
  assert.equal(node('dot_1').primitiveType, 'sphere');
  assert.equal(node('label_0').panel.chip, true);
  assert.equal(node('label_0').panel.fontSize, 22);
  assert.equal(node('label_0').panel.width, 0.05 + 'ceramic mug'.length * 0.013);
  assert.deepEqual(node('label_0').transform, { position: { ...box.center, y: box.center.y + box.size.y / 2 + 0.1 }, rotation });
  assert.deepEqual(node('boxline_0').line.points, [{ position: { ...box.center, y: box.center.y + box.size.y / 2 } }, { key: 'label_0' }]);
  assert.deepEqual(node('line_0').line.color, node('boxline_0').line.color);
  tools.find(item => item.name === 'scene_build').schema.parse(build);
});

test('segment preserves the card without labels when camera_locate has no depth', async () => {
  const { service, calls } = await setup();
  const result = decode(await service.segment({ question: 'What am I looking at?' }));
  assert.equal(result.data.segmentation, 'ok');
  assert.deepEqual(result.data.panel, { entityId: 'segmentation-card', placed: true });
  assert.equal(result.data.labels, null);
  assert.ok(result.data.objects.every(item => item.position === null));
  assert.equal(calls.some(item => item.name === 'scene_build'), false);
});

test('segment binds box and point groups while keeping unboxed marks at the root', async () => {
  const position = { x: 0.1, y: 0.8, z: 1.2 };
  const rotation = { x: 0, y: 180, z: 0 };
  const box = { center: position, rotation, size: { x: 0.1, y: 0.2, z: 0.1 } };
  const { service, calls } = await setup({ locate: { state: 'ready', regions: [{ position, rotation, box }, { position, rotation }] } });
  const result = decode(await service.segment({ question: 'What am I looking at?' }));
  assert.equal(result.data.segmentation, 'ok');
  assert.deepEqual(calls.slice(3).map(call => call.name), ['camera_locate', 'ui_present', 'spatial_place', 'scene_build', 'camera_marks_bind', 'camera_marks_selectable']);
  assert.deepEqual(calls.find(call => call.name === 'ui_present').args.presentation.check, { label: 'Show boxes', completed: false });
  const build = calls.find(call => call.name === 'scene_build').args;
  const node = key => build.nodes.find(item => item.key === key);
  for (const key of ['boxes', 'points']) {
    assert.equal(node(key).type, 'empty');
    assert.equal(Object.hasOwn(node(key), 'parentKey'), false);
    assert.deepEqual(node(key).transform.position, { x: 0, y: 0, z: 0 });
  }
  for (const key of ['box_0', 'boxline_0']) assert.equal(node(key).parentKey, 'boxes');
  for (const key of ['dot_0', 'line_0']) assert.equal(node(key).parentKey, 'points');
  for (const key of ['label_0', 'dot_1', 'label_1', 'line_1']) assert.equal(Object.hasOwn(node(key), 'parentKey'), false);
  assert.equal(node('label_1').panel.chip, true);
  assert.equal(Object.hasOwn(build, 'placement'), false);
  tools.find(item => item.name === 'scene_build').schema.parse(build);
  assert.deepEqual(calls.find(call => call.name === 'camera_marks_bind').args, {
    cardId: 'segmentation-card', rootId: 'object-labels', boxesId: 'entity-boxes', pointsId: 'entity-points',
  });
});

test('segment omits the toggle and mark operations with no_depth', async () => {
  const { service, calls } = await setup({ locate: { state: 'no_depth', regions: [] } });
  const result = decode(await service.segment({ question: 'What am I looking at?' }));
  assert.equal(result.data.segmentation, 'ok');
  assert.deepEqual(result.data.panel, { entityId: 'segmentation-card', placed: true });
  assert.equal(Object.hasOwn(calls.find(call => call.name === 'ui_present').args.presentation, 'check'), false);
  assert.equal(calls.some(call => call.name === 'scene_build'), false);
  assert.equal(calls.some(call => call.name === 'camera_marks_bind'), false);
  assert.equal(calls.some(call => call.name === 'camera_marks_selectable'), false);
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

test('segment binds exact selectable contexts for boxed and unboxed detections', async () => {
  const position = { x: 0.1, y: 0.8, z: 1.2 };
  const rotation = { x: 0, y: 180, z: 0 };
  const box = { center: position, rotation, size: { x: 0.1, y: 0.2, z: 0.1 } };
  const { service, calls } = await setup({ locate: { state: 'ready', regions: [
    { position, rotation, box }, { position, rotation },
  ] } });
  const result = decode(await service.segment({ question: 'What am I looking at?' }));
  assert.deepEqual(calls.slice(3).map(call => call.name), [
    'camera_locate', 'ui_present', 'spatial_place', 'scene_build', 'camera_marks_bind', 'camera_marks_selectable',
  ]);
  const args = calls.find(call => call.name === 'camera_marks_selectable').args;
  assert.deepEqual(args, {
    rootId: 'object-labels', captureId: 'cap_a1b2c3d4e5f6', width: 800, height: 600,
    objects: [
      { label: 'ceramic mug', labelId: 'entity-label_0', boxId: 'entity-box_0', position, box: result.data.objects[0].box },
      { label: 'open notebook', labelId: 'entity-label_1', position, box: result.data.objects[1].box },
    ],
  });
  assert.equal(Object.hasOwn(args.objects[1], 'boxId'), false);
});
