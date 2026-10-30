import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import test from 'node:test';
import sharp from 'sharp';
import { checkPublicHost, createImageLoader, MediaError } from '../dist/media.js';
import { tools } from '../dist/tools.js';

const directUrl = 'https://images.example.test/poster.jpg';
const pageUrl = 'https://images.example.test/film';
const searchText = 'Iron Man 2008 film';
const checkTestHost = async () => ({ address: '127.0.0.1', family: 4 });
const makeImage = (width = 800, height = 1200) => sharp({
  create: { width, height, channels: 3, background: { r: 40, g: 90, b: 150 } },
}).jpeg().toBuffer();

function offlineLoader(routes, requests = [], extra = {}) {
  return createImageLoader({
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

test('media retries one 429 with Retry-After and resolves the following 200', async () => {
  let attempts = 0;
  const body = await makeImage();
  const load = createImageLoader({ checkHost: checkTestHost, download: async (url, options) => {
    assert.equal(options.userAgent, 'SeyraBridge/1.0 (https://github.com/lNatxii/Seyra; personal assistant)');
    if (++attempts === 1) throw new MediaError('MEDIA_HTTP_429', undefined, '0');
    return { body, url: url.href, contentType: 'image/jpeg' };
  } });
  const result = await load(directUrl);
  assert.equal(attempts, 2);
  const media = result.entry, bytes = Buffer.from(media.data, 'base64');
  const metadata = await sharp(bytes).metadata();
  assert.equal(metadata.format, 'jpeg');
  assert.ok(metadata.width <= 512 && metadata.height <= 512);
  assert.equal(media.width, metadata.width); assert.equal(media.height, metadata.height);
  assert.equal(media.id, `web:${createHash('sha256').update(bytes).digest('hex').slice(0, 32)}`);
  assert.equal(result.sourceUrl, directUrl);
});

test('media never downloads more than three images concurrently per host', async () => {
  let active = 0, peak = 0;
  const body = await makeImage();
  const load = createImageLoader({ checkHost: checkTestHost, download: async url => {
    active++; peak = Math.max(peak, active);
    try { await new Promise(resolve => setTimeout(resolve, 15)); return { body, url: url.href, contentType: 'image/jpeg' }; }
    finally { active--; }
  } });
  await Promise.all(Array.from({ length: 6 }, (_, i) => load(`https://images.example.test/${i}.jpg`))); assert.equal(peak, 3); assert.equal(active, 0);
});

test('media real host checker blocks a private IP before any connection', async () => {
  let connections = 0;
  const url = 'https://192.168.1.10/x.jpg';
  const load = createImageLoader({ checkHost: checkPublicHost, download: async (target, { checkHost }) => {
    await checkHost(target.hostname);
    connections++;
    throw new Error('A private host must never reach the connection stage.');
  } });
  await assert.rejects(load(url), error => error instanceof MediaError && error.code === 'MEDIA_BLOCKED_HOST');
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
    const load = offlineLoader(new Map([[url, { body, contentType }]]));
    await assert.rejects(load(url), error => error instanceof MediaError && error.code === code);
  }
});

test('ui_present accepts catalog photos and rejects web image fields', () => {
  const imageUrl = directUrl;
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
