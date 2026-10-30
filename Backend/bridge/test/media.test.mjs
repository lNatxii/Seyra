import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import test from 'node:test';
import sharp from 'sharp';
import { checkPublicHost, createMediaResolver, MediaError } from '../dist/media.js';
import { tools } from '../dist/tools.js';

const imageUrl = 'https://images.example.test/poster.jpg';
const pageUrl = 'https://images.example.test/film';
const searchText = 'Iron Man 2008 film';
const checkTestHost = async () => ({ address: '127.0.0.1', family: 4 });
const metaResponse = results => new Response(JSON.stringify({ output: [{ type: 'web_search_call', results }] }), { status: 200 });
const makeImage = (width = 800, height = 1200) => sharp({
  create: { width, height, channels: 3, background: { r: 40, g: 90, b: 150 } },
}).jpeg().toBuffer();

function offlineResolver(routes, requests = [], extra = {}) {
  return createMediaResolver({
    ...extra, checkHost: checkTestHost,
    download: async (url, { checkHost, signal }) => {
      signal.throwIfAborted();
      await checkHost(url.hostname);
      requests.push(url.href);
      const route = routes.get(url.href);
      assert.ok(route, `Unexpected offline media request: ${url.href}`);
      return { ...route, url: url.href };
    },
  });
}

function assertFailure(result, source, code) {
  assert.deepEqual(result.media, []);
  assert.deepEqual(result.failures, [{ ...source, code }]);
  assert.equal(result.args.image.imageRef, 'web:unavailable');
  assert.equal('imageSearch' in result.args.image, false);
  assert.equal('imageUrl' in result.args.image, false);
}

test('Meta search skips an imageless page and cites the next valid result in search order', async () => {
  const empty = 'https://www.example.test/empty';
  const requests = [];
  const resolve = offlineResolver(new Map([
    [empty, { body: Buffer.from('<html><head><title>No image</title></head></html>'), contentType: 'text/html' }],
    [pageUrl, { body: await makeImage(), contentType: 'image/jpeg' }],
  ]), requests, { env: { META_API_KEY: 'fixture-key', SEYRA_MEDIA_SEARCH_MODEL: 'fixture-model' }, metaRequest: async (url, options) => {
    assert.equal(url, 'https://api.meta.ai/v1/responses');
    assert.equal(options.method, 'POST');
    assert.equal(options.headers.Authorization, 'Bearer fixture-key');
    assert.deepEqual(JSON.parse(options.body), { model: 'fixture-model', tools: [{ type: 'web_search', search_context_size: 'low' }],
      include: ['web_search_call.results'], reasoning: { effort: 'low' }, max_output_tokens: 2048,
      input: `Use web search now to find: ${searchText}. Then reply with the single word done.` });
    return metaResponse([{ type: 'text_result', title: 'Empty', url: empty }, { type: 'text_result', title: 'Film poster', url: pageUrl }]);
  } });
  const result = await resolve('ui_present', { image: { imageSearch: searchText, title: 'Iron Man' } });
  assert.deepEqual(result.failures, []);
  assert.equal(result.args.image.source, 'images.example.test · Film poster');
  assert.equal(result.args.image.sourceUrl, pageUrl);
  assert.equal(result.media.length, 1);
  const media = result.media[0], bytes = Buffer.from(media.data, 'base64');
  const metadata = await sharp(bytes).metadata();
  assert.equal(metadata.format, 'jpeg');
  assert.ok(metadata.width <= 512 && metadata.height <= 512);
  assert.equal(media.width, metadata.width); assert.equal(media.height, metadata.height);
  assert.equal(media.id, `web:${createHash('sha256').update(bytes).digest('hex').slice(0, 32)}`);
  assert.equal(result.args.image.imageRef, media.id);
  assert.ok(requests.includes(empty) && requests.includes(pageUrl));
});

test('Meta search uses YouTube hqdefault without downloading the consent page', async () => {
  const video = 'https://www.youtube.com/watch?v=Abc123_-xyz';
  const thumbnail = 'https://img.youtube.com/vi/Abc123_-xyz/hqdefault.jpg';
  const requests = [];
  const resolve = offlineResolver(new Map([[thumbnail, { body: await makeImage(), contentType: 'image/jpeg' }]]), requests,
    { env: { META_API_KEY: 'fixture-key' }, metaRequest: async () => metaResponse([{ type: 'text_result', title: 'Recipe', url: video }]) });
  const result = await resolve('ui_present', { image: { imageSearch: 'recipe' } });
  assert.deepEqual(result.failures, []); assert.deepEqual(requests, [thumbnail]);
  assert.equal(result.args.image.sourceUrl, video); assert.equal(result.args.image.source, 'youtube.com · Recipe');
});

test('Meta search without a key reports MEDIA_NOT_CONFIGURED without a request', async () => {
  const resolve = createMediaResolver({ env: {}, metaRequest: async () => { throw new Error('Must not request Meta without a key.'); } });
  assertFailure(await resolve('ui_present', { image: { imageSearch: searchText } }), { search: searchText }, 'MEDIA_NOT_CONFIGURED');
});

test('Meta search without results reports MEDIA_NOT_FOUND', async () => {
  const resolve = createMediaResolver({ env: { META_API_KEY: 'fixture-key' }, metaRequest: async () => metaResponse([]) });
  assertFailure(await resolve('ui_present', { image: { imageSearch: searchText } }), { search: searchText }, 'MEDIA_NOT_FOUND');
});

test('media retries one 429 with Retry-After and resolves the following 200', async () => {
  let attempts = 0;
  const body = await makeImage();
  const resolve = createMediaResolver({ checkHost: checkTestHost, download: async (url, options) => {
    assert.equal(options.userAgent, 'SeyraBridge/1.0 (https://github.com/lNatxii/Seyra; personal assistant)');
    if (++attempts === 1) throw new MediaError('MEDIA_HTTP_429', undefined, '0');
    return { body, url: url.href, contentType: 'image/jpeg' };
  } });
  const result = await resolve('ui_present', { image: { imageUrl } });
  assert.deepEqual(result.failures, []); assert.equal(attempts, 2); assert.equal(result.media.length, 1);
});

test('media never downloads more than three images concurrently per host', async () => {
  let active = 0, peak = 0;
  const body = await makeImage();
  const resolve = createMediaResolver({ checkHost: checkTestHost, download: async url => {
    active++; peak = Math.max(peak, active);
    try { await new Promise(resolve => setTimeout(resolve, 15)); return { body, url: url.href, contentType: 'image/jpeg' }; }
    finally { active--; }
  } });
  const result = await resolve('layout_create', { images: Array.from({ length: 6 }, (_, i) => ({ imageUrl: `https://images.example.test/${i}.jpg` })) });
  assert.deepEqual(result.failures, []); assert.equal(peak, 3); assert.equal(active, 0);
});

test('media real host checker blocks a private IP before any connection', async () => {
  let connections = 0;
  const url = 'https://192.168.1.10/x.jpg';
  const resolve = createMediaResolver({ checkHost: checkPublicHost, download: async (target, { checkHost }) => {
    await checkHost(target.hostname);
    connections++;
    throw new Error('A private host must never reach the connection stage.');
  } });
  assertFailure(await resolve('ui_present', { image: { imageUrl: url } }), { url }, 'MEDIA_BLOCKED_HOST');
  assert.equal(connections, 0);
});

test('media rejects plain text, a 5 MiB body and a 68 by 100 thumbnail', async () => {
  const cases = [
    ['html', Buffer.from('<html><meta property="og:image" content="/poster.jpg"></html>'), 'text/html', 'MEDIA_UNSUPPORTED'],
    ['text', Buffer.from('This is plain text.'), 'text/plain', 'MEDIA_UNSUPPORTED'],
    ['large', Buffer.alloc(5 * 1024 * 1024), 'image/jpeg', 'MEDIA_TOO_LARGE'],
    ['small', await makeImage(68, 100), 'image/jpeg', 'MEDIA_TOO_SMALL'],
  ];
  for (const [name, body, contentType, code] of cases) {
    const url = `https://images.example.test/${name}`;
    const resolve = offlineResolver(new Map([[url, { body, contentType }]]));
    assertFailure(await resolve('ui_present', { image: { imageUrl: url } }), { url }, code);
  }
});

test('media rejects 13 distinct images with INVALID_ARGUMENT before downloads', async () => {
  let downloads = 0;
  const resolve = createMediaResolver({ checkHost: checkTestHost, download: async () => {
    downloads++;
    throw new Error('Too many images must be rejected before downloading.');
  } });
  await assert.rejects(resolve('layout_create', {
    images: Array.from({ length: 13 }, (_, index) => ({ imageUrl: `https://images.example.test/${index}.jpg` })),
  }), error => error instanceof MediaError && error.code === 'INVALID_ARGUMENT');
  assert.equal(downloads, 0);
});

test('ui_present accepts catalog photos and rejects web image fields', () => {
  const schema = tools.find(tool => tool.name === 'ui_present').schema;
  const args = {
    name: 'Movie card', parentId: 'Generated', position: { x: 0, y: 1.4, z: 1.5 },
    rotation: { x: 0, y: 0, z: 0 }, scale: { x: 1, y: 1, z: 1 }, width: .42, height: .62,
    presentation: { id: 'iron-man', type: 'mediaCard', title: 'Iron Man',
      image: { title: 'Poster', imageRef: 'poster', alt: 'Iron Man film poster' }, body: 'A superhero film.',
      facts: [{ label: 'Year', value: '2008' }], tags: ['Marvel'] },
  };
  assert.equal(schema.safeParse(args).success, true);
  const withImage = image => ({ ...args, presentation: { ...args.presentation, image: { title: 'Poster', ...image } } });
  for (const image of [
    { imageSearch: searchText },
    { imageUrl },
    { imageRef: 'poster', imageSearch: searchText },
    { imageRef: 'poster', imageUrl },
    { imageSearch: searchText, imageUrl },
  ]) assert.equal(schema.safeParse(withImage(image)).success, false);
  assert.equal(schema.safeParse(withImage({ imageRef: `web:${'a'.repeat(32)}` })).success, false);
  assert.equal(schema.safeParse(withImage({ imageSearch: searchText, source: 'Injected', sourceUrl: pageUrl })).success, false);
});
