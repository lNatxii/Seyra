import { lookup } from 'node:dns/promises';
import { isIP } from 'node:net';
import * as http from 'node:http';
import * as https from 'node:https';
import { createHash } from 'node:crypto';
import sharp from 'sharp';

const USER_AGENT = 'SeyraBridge/1.0 (personal assistant)';
const MAX_BYTES = 4 * 1024 * 1024;
export class MediaError extends Error {
  constructor(public readonly code: string, message = code) { super(message); }
}
export interface MediaEntry { id: string; mime: 'image/jpeg'; width: number; height: number; data: string }
export type MediaFailure = { search: string; code: string } | { url: string; code: string };
export type HostChecker = (hostname: string) => Promise<{ address: string; family: number }>;
export interface DownloadResponse { body: Buffer; contentType: string; url: string }
export type MediaDownloader = (url: URL, options: { signal: AbortSignal; maxBytes: number; checkHost: HostChecker }) => Promise<DownloadResponse>;

function blocked(address: string): boolean {
  if (isIP(address) === 4) {
    const [a, b] = address.split('.').map(Number);
    return a === 0 || a === 10 || a === 127 || a === 169 && b === 254 || a === 172 && b >= 16 && b <= 31 ||
      a === 192 && (b === 168 || b === 0) || a === 100 && b >= 64 && b <= 127 || a >= 224 || a === 198 && (b === 18 || b === 19);
  }
  if (isIP(address) !== 6) return true;
  // URL normalizes compressed and IPv4-mapped literals before inspecting words.
  const normalized = new URL(`http://[${address}]/`).hostname.slice(1, -1);
  const halves = normalized.split('::');
  const left = halves[0] ? halves[0].split(':') : [];
  const right = halves[1] ? halves[1].split(':') : [];
  const words = (halves.length === 2 ? [...left, ...Array(8 - left.length - right.length).fill('0'), ...right] : left).map(v => parseInt(v, 16));
  if (words.slice(0, 5).every(v => v === 0) && words[5] === 0xffff)
    return blocked(`${words[6] >> 8}.${words[6] & 255}.${words[7] >> 8}.${words[7] & 255}`);
  // Only globally routed unicast; exclude local, multicast and transition ranges.
  return (words[0] & 0xe000) !== 0x2000 || words[0] === 0x2002 || words[0] === 0x2001 && words[1] < 0x200;
}

export const checkPublicHost: HostChecker = async hostname => {
  const host = hostname.replace(/^\[|\]$/g, '');
  const addresses = isIP(host) ? [{ address: host, family: isIP(host) }] : await lookup(host, { all: true });
  if (!addresses.length || addresses.some(a => blocked(a.address))) throw new MediaError('MEDIA_BLOCKED_HOST');
  return addresses[0];
};

function safeUrl(value: string, base?: string): URL {
  let url: URL;
  try { url = new URL(value, base); } catch { throw new MediaError('MEDIA_UNSUPPORTED'); }
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) throw new MediaError('MEDIA_BLOCKED_HOST');
  return url;
}

export const downloadMedia: MediaDownloader = async (initial, options) => {
  const signal = AbortSignal.any([options.signal, AbortSignal.timeout(4000)]);
  let url = initial;
  for (let redirects = 0; ; redirects++) {
    safeUrl(url.href);
    const host = await bounded(options.checkHost(url.hostname), signal);
    signal.throwIfAborted();
    const response = await new Promise<{ body: Buffer; contentType: string; location?: string; status: number }>((resolve, reject) => {
      const requestOptions: http.RequestOptions & { autoSelectFamily: boolean } = {
        method: 'GET', signal, agent: false, autoSelectFamily: false, headers: { 'User-Agent': USER_AGENT, 'Accept-Encoding': 'identity' },
        lookup: (_hostname, _options, callback) => callback(null, host.address, host.family),
      };
      const request = (url.protocol === 'https:' ? https : http).request(url, requestOptions, res => {
        const status = res.statusCode ?? 0;
        const contentType = String(res.headers['content-type'] ?? '');
        if (status >= 300 && status < 400) {
          res.destroy(); resolve({ body: Buffer.alloc(0), contentType, location: res.headers.location, status }); return;
        }
        if (status < 200 || status >= 300) { res.destroy(); reject(new MediaError(`MEDIA_HTTP_${status}`)); return; }
        const limit = /text\/html/i.test(contentType) ? Math.min(options.maxBytes, 1024 * 1024) : options.maxBytes;
        if (Number(res.headers['content-length']) > limit) { res.destroy(); reject(new MediaError('MEDIA_TOO_LARGE')); return; }
        const chunks: Buffer[] = []; let length = 0;
        res.on('data', (chunk: Buffer) => {
          length += chunk.length;
          if (length > limit) { reject(new MediaError('MEDIA_TOO_LARGE')); res.destroy(); }
          else chunks.push(chunk);
        });
        res.on('end', () => resolve({ body: Buffer.concat(chunks), contentType, status }));
        res.on('error', reject);
        res.on('aborted', () => reject(new MediaError('MEDIA_UNSUPPORTED')));
      });
      request.on('error', reject); request.end();
    });
    if (response.status >= 300 && response.status < 400) {
      if (!response.location || redirects >= 3) throw new MediaError('MEDIA_UNSUPPORTED');
      url = safeUrl(response.location, url.href); continue;
    }
    return { body: response.body, contentType: response.contentType, url: url.href };
  }
};

function bounded<T>(promise: Promise<T>, signal: AbortSignal): Promise<T> {
  return new Promise((resolve, reject) => {
    const abort = () => reject(new MediaError('MEDIA_TIMEOUT'));
    if (signal.aborted) { abort(); return; }
    signal.addEventListener('abort', abort, { once: true });
    promise.then(resolve, reject).finally(() => signal.removeEventListener('abort', abort));
  });
}

class Cache<T> {
  private entries = new Map<string, { value: T; expires: number }>();
  get(key: string): T | undefined {
    const entry = this.entries.get(key);
    if (!entry) return undefined;
    this.entries.delete(key);
    if (entry.expires <= Date.now()) return undefined;
    this.entries.set(key, entry); return entry.value;
  }
  set(key: string, value: T): void {
    this.entries.delete(key); this.entries.set(key, { value, expires: Date.now() + 15 * 60_000 });
    if (this.entries.size > 64) this.entries.delete(this.entries.keys().next().value!);
  }
}

function imageMagic(b: Buffer): boolean {
  return b.length >= 12 && (b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff ||
    b.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])) ||
    /^GIF8[79]a$/.test(b.toString('ascii', 0, 6)) || b.toString('ascii', 0, 4) === 'RIFF' && b.toString('ascii', 8, 12) === 'WEBP');
}
function htmlImage(html: string): string | undefined {
  for (const tag of html.match(/<(?:meta|link)\b[^>]*>/gi) ?? []) {
    const attrs: Record<string, string> = {};
    for (const a of tag.matchAll(/([\w:-]+)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/g)) attrs[a[1].toLowerCase()] = a[2] ?? a[3] ?? a[4];
    const key = (attrs.property ?? attrs.name ?? '').toLowerCase();
    const value = ['og:image', 'twitter:image'].includes(key) ? attrs.content : attrs.rel?.toLowerCase() === 'image_src' ? attrs.href : undefined;
    if (value) return value.replace(/&(?:amp|quot|apos|lt|gt|#(\d+)|#x([\da-f]+));/gi, (entity, decimal, hex) => {
      if (decimal || hex) { const n = parseInt(decimal ?? hex, decimal ? 10 : 16); return n <= 0x10ffff ? String.fromCodePoint(n) : ''; }
      return ({ '&amp;': '&', '&quot;': '"', '&apos;': "'", '&lt;': '<', '&gt;': '>' } as Record<string, string>)[entity.toLowerCase()];
    });
  }
  return undefined;
}

/** Each factory has isolated caches; production always uses the real host checker. */
export function createMediaResolver(dependencies: { checkHost?: HostChecker; download?: MediaDownloader } = {}) {
  const checkHost = dependencies.checkHost ?? checkPublicHost;
  const download = dependencies.download ?? downloadMedia;
  const images = new Cache<MediaEntry>();
  const searches = new Cache<string>();
  return async function resolveMedia<T extends Record<string, unknown>>(tool: string, args: T): Promise<{ args: T; media: MediaEntry[]; failures: MediaFailure[] }> {
    if (!['ui_present', 'ui_update', 'layout_create'].includes(tool)) return { args, media: [], failures: [] };
    const copy = structuredClone(args);
    const sources = new Map<string, { kind: 'search' | 'url'; value: string; targets: Record<string, unknown>[] }>();
    function collect(value: unknown): void {
      if (!value || typeof value !== 'object') return;
      if (Array.isArray(value)) { value.forEach(collect); return; }
      const object = value as Record<string, unknown>;
      const kind = typeof object.imageSearch === 'string' ? 'search' : typeof object.imageUrl === 'string' ? 'url' : undefined;
      if (kind) {
        const value = String(kind === 'search' ? object.imageSearch : object.imageUrl);
        const key = `${kind}:${value}`;
        const source = sources.get(key) ?? { kind, value, targets: [] };
        source.targets.push(object); sources.set(key, source);
      }
      Object.values(object).forEach(collect);
    }
    collect(copy);
    if (sources.size > 12) throw new MediaError('INVALID_ARGUMENT', 'At most 12 images per call');
    const signal = AbortSignal.timeout(8000);
    const pending = new Map<string, Promise<MediaEntry>>();
    async function get(url: string, maxBytes: number): Promise<DownloadResponse> {
      const downloadSignal = AbortSignal.any([signal, AbortSignal.timeout(4000)]);
      const result = await bounded(download(safeUrl(url), { signal: downloadSignal, maxBytes, checkHost }), downloadSignal);
      if (result.body.length > maxBytes) throw new MediaError('MEDIA_TOO_LARGE');
      return result;
    }
    async function search(text: string): Promise<string> {
      const cached = searches.get(text); if (cached) return cached;
      const query = new URL('https://en.wikipedia.org/w/api.php');
      query.search = new URLSearchParams({ action: 'query', format: 'json', generator: 'search', gsrsearch: text, gsrlimit: '1', gsrnamespace: '0', prop: 'info' }).toString();
      const found = JSON.parse((await get(query.href, 256 * 1024)).body.toString());
      const pages = Object.values(found.query?.pages ?? {}) as { title?: string; index?: number }[];
      const title = pages.sort((a, b) => (a.index ?? 0) - (b.index ?? 0))[0]?.title;
      if (!title) throw new MediaError('MEDIA_NOT_FOUND');
      const summary = JSON.parse((await get(`https://en.wikipedia.org/api/rest_v1/page/summary/${encodeURIComponent(title)}`, 256 * 1024)).body.toString());
      const url = summary.originalimage?.source ?? summary.thumbnail?.source;
      if (typeof url !== 'string') throw new MediaError('MEDIA_NOT_FOUND');
      safeUrl(url); searches.set(text, url); return url;
    }
    async function convert(url: string): Promise<MediaEntry> {
      const cached = images.get(url); if (cached) return cached;
      let result = await get(url, MAX_BYTES);
      if (!imageMagic(result.body)) {
        if (!/text\/html/i.test(result.contentType) && !/^\s*(?:<!doctype\s+html|<html|<head|<meta)/i.test(result.body.toString('utf8', 0, 512))) throw new MediaError('MEDIA_UNSUPPORTED');
        if (result.body.length > 1024 * 1024) throw new MediaError('MEDIA_TOO_LARGE');
        const source = htmlImage(result.body.toString());
        if (!source) throw new MediaError('MEDIA_NO_IMAGE');
        result = await get(safeUrl(source, result.url).href, MAX_BYTES);
        if (!imageMagic(result.body)) throw new MediaError('MEDIA_UNSUPPORTED');
      }
      const image = sharp(result.body, { limitInputPixels: 40_000_000, animated: false });
      const metadata = await image.metadata();
      if (Math.max(metadata.width ?? 0, metadata.height ?? 0) < 200) throw new MediaError('MEDIA_TOO_SMALL');
      const output = await image.rotate().resize({ width: 512, height: 512, fit: 'inside', withoutEnlargement: true }).jpeg({ quality: 82 }).toBuffer({ resolveWithObject: true });
      signal.throwIfAborted();
      const entry: MediaEntry = { id: `web:${createHash('sha256').update(output.data).digest('hex').slice(0, 32)}`, mime: 'image/jpeg', width: output.info.width, height: output.info.height, data: output.data.toString('base64') };
      images.set(url, entry); return entry;
    }
    const results = await Promise.all([...sources.values()].map(async source => {
      try {
        const url = source.kind === 'search' ? await search(source.value) : source.value;
        let task = pending.get(url);
        if (!task) { task = bounded(convert(url), signal); pending.set(url, task); }
        const entry = await task;
        for (const target of source.targets) { delete target.imageSearch; delete target.imageUrl; target.imageRef = entry.id; }
        return { entry };
      } catch (error) {
        const code = signal.aborted || error instanceof Error && ['AbortError', 'TimeoutError'].includes(error.name) ? 'MEDIA_TIMEOUT' : error instanceof MediaError ? error.code : 'MEDIA_UNSUPPORTED';
        for (const target of source.targets) { delete target.imageSearch; delete target.imageUrl; target.imageRef = 'web:unavailable'; }
        return { failure: { [source.kind]: source.value, code } as MediaFailure };
      }
    }));
    const media = [...new Map(results.flatMap(r => r.entry ? [[r.entry.id, r.entry] as const] : [])).values()];
    return { args: copy, media, failures: results.flatMap(r => r.failure ? [r.failure] : []) };
  };
}

export const resolveMedia = createMediaResolver();
