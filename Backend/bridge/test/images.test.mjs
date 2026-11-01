import assert from 'node:assert/strict';
import test from 'node:test';
import sharp from 'sharp';
import { createImageService } from '../dist/images.js';
import { presentationSchema } from '../dist/presentations.js';

const card = (subject = 'carrot', id = 'carrot') => ({ type: 'mediaCard', id, title: 'Carrot', body: 'One carrot', generatedImage: { subject, category: 'ingredient', alt: 'A carrot' } });
const result = data => ({ content: [{ type: 'text', text: JSON.stringify({ success: true, data }) }] });
const decode = value => JSON.parse(value.content[0].text);
async function fixture() {
  const jpeg = await sharp({ create: { width: 8, height: 8, channels: 3, background: 'white' } }).jpeg().toBuffer();
  const requests = [], fetched = [];
  const service = createImageService({ env: { META_API_KEY: 'mock-key' },
    fetch: async (url, options) => { fetched.push({ url, options }); return new Response(JSON.stringify({ data: [{ b64_json: jpeg.toString('base64') }] })); },
    invoke: async (name, args) => { requests.push({ name, args }); return result(name === 'media_status' ? { existing: [] } : name === 'media_deliver' ? { refreshed: 1 } : { entityId: 'go_mock' }); },
  });
  return { service, requests, fetched };
}

test('rewrites generated images and disables all Muse tools', async () => {
  const { service, requests, fetched } = await fixture();
  const response = decode(await service.execute('ui_present', { presentation: card() }));
  await service.whenIdle();
  const args = requests.find(r => r.name === 'ui_present').args;
  assert.equal(args.presentation.generatedImage, undefined);
  assert.match(args.presentation.image.imageRef, /^gen:[0-9a-f]{32}$/);
  assert.equal(args.presentation.image.source, 'AI-generated illustration');
  assert.deepEqual(args.pendingMedia, [args.presentation.image.imageRef]);
  assert.equal(response.success, true);
  assert.equal(response.data.images.queued, 1);
  assert.equal(fetched.length, 1);
  assert.deepEqual(JSON.parse(fetched[0].options.body), {
    model: 'muse-image-1.0', prompt: 'A plain generic example of carrot. Clean, simple illustration, clearly not a photograph. One centered subject on a plain light background. No text, letters, numbers, logos, brand marks, labels, packaging designs, watermarks, people or fictional characters.',
    n: 1, size: '1024x1024', response_format: 'b64_json', output_format: 'jpeg', reasoning_strength: 'low',
    tool_enablement: { enable_image_search: false, enable_web_search: false, enable_shell: false },
  });
  assert.equal(requests.find(r => r.name === 'media_deliver').args.media.length, 1);
});

test('blocked subject returns the policy error without invoke or fetch', async () => {
  let invokes = 0, fetches = 0;
  const service = createImageService({ env: { META_API_KEY: 'mock-key' }, invoke: async () => { invokes++; return result({}); }, fetch: async () => { fetches++; throw Error('Unexpected fetch'); } });
  const presentation = presentationSchema.parse(card('Disney carrot'));
  const response = decode(await service.execute('ui_present', { presentation }));
  assert.deepEqual(response, { success: false, error: { code: 'IMAGE_SUBJECT_REJECTED', message: 'This subject cannot have a generated image. Use a text panel or a card without an image.' } });
  assert.equal(invokes, 0); assert.equal(fetches, 0);
});

test('Unity rejection never starts generation', async () => {
  let fetches = 0;
  const rejected = { isError: true, content: [{ type: 'text', text: JSON.stringify({ success: false, error: { code: 'INVALID_ARGUMENT', message: 'Rejected' } }) }] };
  const service = createImageService({ env: { META_API_KEY: 'mock-key' }, invoke: async name => name === 'media_status' ? result({ existing: [] }) : rejected, fetch: async () => { fetches++; throw Error('Unexpected fetch'); } });
  assert.equal(await service.execute('ui_present', { presentation: card() }), rejected);
  await service.whenIdle(); assert.equal(fetches, 0);
});

test('two cards with the same subject use one fetch', async () => {
  const { service, fetched } = await fixture();
  await service.execute('layout_create', { items: [{ presentation: card() }, { presentation: card('carrot', 'second') }] });
  await service.whenIdle(); assert.equal(fetched.length, 1);
});
