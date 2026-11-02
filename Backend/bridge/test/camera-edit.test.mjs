import assert from 'node:assert/strict';
import test from 'node:test';
import sharp from 'sharp';
import { createImageService } from '../dist/images.js';
import { createCameraService } from '../dist/camera.js';
import { editedImageId } from '../dist/image-policy.js';

const result = data => ({ content: [{ type: 'text', text: JSON.stringify({ success: true, data }) }] });
const decode = value => JSON.parse(value.content[0].text);
const instruction = 'Repaint the walls of this living room in light blue';
const title = 'Blue living room';
async function fixture(rejectCard = false) {
  const jpeg = (await sharp({ create: { width: 16, height: 12, channels: 3, background: 'white' } }).jpeg().toBuffer()).toString('base64');
  const calls = [], fetched = [];
  const invoke = async (name, args) => {
    calls.push({ name, args });
    if (name === 'camera_capture_start') return result({ state: 'started' });
    if (name === 'camera_capture_result') return result({ state: 'ready', captureId: 'cap_a1b2c3d4e5f6', jpegBase64: jpeg, width: 1280, height: 960 });
    if (name === 'ui_present' && rejectCard) return { isError: true, content: [{ type: 'text', text: JSON.stringify({ success: false, error: { code: 'INVALID_ARGUMENT', message: 'Rejected' } }) }] };
    return result(name === 'ui_present' ? { entityId: 'go_edit' } : { applied: true });
  };
  const env = { META_API_KEY: 'mock-key' };
  const fetch = async (url, options) => {
    calls.push({ name: 'fetch' }); fetched.push({ url, body: JSON.parse(options.body) });
    return new Response(JSON.stringify({ data: [{ b64_json: jpeg }] }));
  };
  const images = createImageService({ invoke, fetch, env });
  const camera = createCameraService({ invoke, fetch, env, images, pollMs: 0 });
  return { camera, images, calls, fetched, jpeg };
}

test('camera edit confirms and places the pending card before the exact edit request and delivery', async () => {
  const { camera, images, calls, fetched, jpeg } = await fixture();
  const response = decode(await camera.edit({ instruction, title }));
  await images.whenIdle();
  assert.equal(response.success, true);
  assert.deepEqual(response.data.panel, { entityId: 'go_edit', placed: true });
  assert.equal(response.data.image, 'queued');
  assert.deepEqual(calls.map(c => c.name), ['camera_capture_start', 'camera_capture_result', 'ui_present', 'spatial_place', 'fetch', 'media_deliver']);
  const imageRef = editedImageId('cap_a1b2c3d4e5f6', instruction);
  assert.equal(response.data.editId, 'edit_' + imageRef.slice(4, 16));
  assert.deepEqual(calls[2].args, {
    name: 'Image edit', parentId: 'Generated',
    position: { x: 0, y: 1.4, z: 1 }, rotation: { x: 0, y: 0, z: 0 }, scale: { x: 1, y: 1, z: 1 },
    width: 0.7, height: 1, pendingMedia: [imageRef],
    presentation: { id: 'edit-' + response.data.editId, type: 'mediaCard', title,
      image: { imageRef, title, alt: instruction, source: 'AI edit of your photo' },
      body: 'AI-generated picture of the change. It is not a real view, and sizes may not be accurate.' },
  });
  assert.deepEqual(calls[3].args, { entityId: 'go_edit', destination: 'air', allowAirFallback: true, minScale: 1 });
  assert.equal(fetched[0].url, 'https://api.meta.ai/v1/images/edits');
  assert.deepEqual(fetched[0].body, {
    model: 'muse-image-1.0',
    prompt: 'Edit this photo of a real place. Apply only this change: ' + instruction + '. Keep everything else exactly as it is: same viewpoint, framing, room, objects and lighting. The result must look like a realistic photo of the same place. Do not add text, letters, logos, brand marks, people or fictional characters. Do not change any person in the photo.',
    images: [{ image_url: 'data:image/jpeg;base64,' + jpeg }], n: 1, size: '1280x960',
    response_format: 'b64_json', output_format: 'jpeg', reasoning_strength: 'low',
    tool_enablement: { enable_image_search: false, enable_web_search: false, enable_shell: false },
  });
  assert.equal(Object.hasOwn(fetched[0].body, 'store'), false);
  assert.equal(calls.at(-1).args.media[0].id, imageRef);
});

test('blocked edit returns EDIT_REJECTED without capture or fetch', async () => {
  const { camera, images, calls, fetched } = await fixture();
  assert.deepEqual(decode(await camera.edit({ instruction: 'Redesign it in IKEA style', title })), {
    success: false, error: { code: 'EDIT_REJECTED', message: 'This change cannot be generated. Ask for it without brands, people or copyrighted works.' },
  });
  await images.whenIdle();
  assert.equal(calls.length, 0); assert.equal(fetched.length, 0);
});

test('unconfirmed card returns EDIT_CARD_FAILED without fetch', async () => {
  const { camera, images, calls, fetched } = await fixture(true);
  assert.equal(decode(await camera.edit({ instruction, title })).error.code, 'EDIT_CARD_FAILED');
  await images.whenIdle();
  assert.equal(calls.filter(c => c.name === 'camera_capture_start').length, 1);
  assert.equal(fetched.length, 0);
});

test('previousEditId sends the latest result without capture and rejects a different ID', async () => {
  const { camera, images, calls, fetched } = await fixture();
  const first = decode(await camera.edit({ instruction, title }));
  await images.whenIdle();
  const previous = images.latestEdit(first.data.editId);
  assert.ok(previous);
  calls.length = 0;
  const refined = decode(await camera.edit({ instruction: 'Make the walls darker', title, previousEditId: first.data.editId }));
  await images.whenIdle();
  assert.equal(refined.data.captureId, null);
  assert.equal(calls.some(c => c.name.startsWith('camera_capture')), false);
  assert.equal(fetched[1].body.images[0].image_url, 'data:image/jpeg;base64,' + previous.jpegBase64);
  assert.equal(fetched[1].body.size, previous.width + 'x' + previous.height);
  calls.length = 0;
  assert.equal(decode(await camera.edit({ instruction, title, previousEditId: 'edit_000000000000' })).error.code, 'EDIT_EXPIRED');
  assert.equal(calls.length, 0); assert.equal(fetched.length, 2);
});
