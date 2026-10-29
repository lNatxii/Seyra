import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import test from 'node:test';
import sharp from 'sharp';
import { checkPublicHost, createMediaResolver, MediaError } from '../dist/media.js';
import { tools } from '../dist/tools.js';

const imageUrl = 'https://images.example.test/poster.jpg';
const pageUrl = 'https://images.example.test/film';
const searchText = 'Iron Man 2008 film';
const articleTitle = 'Iron Man (2008 film)';
const summaryUrl = `https://en.wikipedia.org/api/rest_v1/page/summary/${encodeURIComponent(articleTitle)}`;
// Offline snapshots retain the generator=search and REST summary response shapes.
const searchResponse = {
  batchcomplete: '',
  query: { pages: { '1168906': { pageid: 1168906, ns: 0, title: articleTitle, index: 1,
    contentmodel: 'wikitext', pagelanguage: 'en', pagelanguagehtmlcode: 'en', pagelanguagedir: 'ltr' } } },
};
const summaryResponse = {
  type: 'standard', title: articleTitle, displaytitle: '<i>Iron Man</i> (2008 film)',
  namespace: { id: 0, text: '' }, wikibase_item: 'Q192724', titles: { canonical: articleTitle, normalized: articleTitle, display: articleTitle },
  thumbnail: { source: 'https://images.example.test/thumbnail.jpg', width: 203, height: 300 },
  originalimage: { source: imageUrl, width: 800, height: 1200 }, lang: 'en', dir: 'ltr',
};
const checkTestHost = async () => ({ address: '127.0.0.1', family: 4 });
const json = value => ({ body: Buffer.from(JSON.stringify(value)), contentType: 'application/json' });
const makeImage = (width = 800, height = 1200) => sharp({
  create: { width, height, channels: 3, background: { r: 40, g: 90, b: 150 } },
}).jpeg().toBuffer();

function offlineResolver(routes, requests = []) {
  return createMediaResolver({
    checkHost: checkTestHost,
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

test('media imageSearch resolves offline Wikipedia originalimage to a bounded JPEG with matching hash', async () => {
  const query = new URL('https://en.wikipedia.org/w/api.php');
  query.search = new URLSearchParams({ action: 'query', format: 'json', generator: 'search', gsrsearch: searchText,
    gsrlimit: '1', gsrnamespace: '0', prop: 'info' }).toString();
  const requests = [];
  const resolve = offlineResolver(new Map([
    [query.href, json(searchResponse)], [summaryUrl, json(summaryResponse)],
    [imageUrl, { body: await makeImage(), contentType: 'image/jpeg' }],
  ]), requests);
  const result = await resolve('ui_present', { image: { imageSearch: searchText, title: 'Iron Man' } });
  assert.deepEqual(requests, [query.href, summaryUrl, imageUrl]);
  assert.deepEqual(result.failures, []);
  assert.equal(result.media.length, 1);
  const media = result.media[0];
  assert.match(media.id, /^web:[0-9a-f]{32}$/);
  assert.equal(result.args.image.imageRef, media.id);
  assert.equal('imageSearch' in result.args.image, false);
  assert.equal(media.mime, 'image/jpeg');
  const bytes = Buffer.from(media.data, 'base64');
  const metadata = await sharp(bytes).metadata();
  assert.equal(metadata.format, 'jpeg');
  assert.ok(metadata.width > 0 && metadata.width <= 512);
  assert.ok(metadata.height > 0 && metadata.height <= 512);
  assert.equal(media.width, metadata.width);
  assert.equal(media.height, metadata.height);
  assert.equal(media.id, `web:${createHash('sha256').update(bytes).digest('hex').slice(0, 32)}`);
});

test('media imageSearch without Wikipedia results reports MEDIA_NOT_FOUND', async () => {
  const resolve = createMediaResolver({ checkHost: checkTestHost, download: async (url, { checkHost }) => {
    await checkHost(url.hostname);
    assert.equal(url.searchParams.get('generator'), 'search');
    return { ...json({ batchcomplete: '' }), url: url.href };
  } });
  assertFailure(await resolve('ui_present', { image: { imageSearch: searchText } }), { search: searchText }, 'MEDIA_NOT_FOUND');
});

test('media HTML og:image resolves to the same id as the direct image', async () => {
  const resolve = offlineResolver(new Map([
    [pageUrl, { body: Buffer.from('<!doctype html><html><head><meta property="og:image" content="/poster.jpg"></head></html>'), contentType: 'text/html' }],
    [imageUrl, { body: await makeImage(), contentType: 'image/jpeg' }],
  ]));
  // Resolve HTML first so the test exercises its image download before caching.
  const html = await resolve('ui_present', { image: { imageUrl: pageUrl } });
  const direct = await resolve('ui_present', { image: { imageUrl } });
  assert.deepEqual(html.failures, []);
  assert.deepEqual(direct.failures, []);
  assert.equal(html.media.length, 1);
  assert.equal(direct.media.length, 1);
  assert.equal(html.args.image.imageRef, direct.args.image.imageRef);
  assert.deepEqual(html.media, direct.media);
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

test('ui_present accepts mediaCard and rejects photos with multiple sources or reserved web references', () => {
  const schema = tools.find(tool => tool.name === 'ui_present').schema;
  const args = {
    name: 'Movie card', parentId: 'Generated', position: { x: 0, y: 1.4, z: 1.5 },
    rotation: { x: 0, y: 0, z: 0 }, scale: { x: 1, y: 1, z: 1 }, width: .42, height: .62,
    presentation: { id: 'iron-man', type: 'mediaCard', title: 'Iron Man',
      image: { title: 'Poster', imageSearch: searchText, alt: 'Iron Man film poster' }, body: 'A superhero film.',
      facts: [{ label: 'Year', value: '2008' }], tags: ['Marvel'] },
  };
  assert.equal(schema.safeParse(args).success, true);
  const withImage = image => ({ ...args, presentation: { ...args.presentation, image: { title: 'Poster', ...image } } });
  for (const image of [
    { imageRef: 'poster', imageSearch: searchText },
    { imageRef: 'poster', imageUrl },
    { imageSearch: searchText, imageUrl },
  ]) assert.equal(schema.safeParse(withImage(image)).success, false);
  assert.equal(schema.safeParse(withImage({ imageRef: `web:${'a'.repeat(32)}` })).success, false);
});
