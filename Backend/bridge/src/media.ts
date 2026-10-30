import { lookup } from 'node:dns/promises';
import { isIP } from 'node:net';
import * as http from 'node:http';
import * as https from 'node:https';
import { createHash } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
import sharp from 'sharp';

const USER_AGENT = 'SeyraBridge/1.0 (https://github.com/lNatxii/Seyra; personal assistant)';
const MAX_BYTES = 4 * 1024 * 1024;
export class MediaError extends Error {
  constructor(public readonly code: string, message = code, public readonly retryAfter?: string) { super(message); }
}
export interface MediaEntry { id: string; mime: 'image/jpeg'; width: number; height: number; data: string }
export type HostChecker = (hostname: string) => Promise<{ address: string; family: number }>;
export interface DownloadResponse { body: Buffer; contentType: string; url: string }
export type MediaDownloader = (url: URL, options: { signal: AbortSignal; maxBytes: number; checkHost: HostChecker; userAgent: string }) => Promise<DownloadResponse>;

class Slots {
  private active = new Map<string, number>();
  private queues = new Map<string, (() => void)[]>();
  async run<T>(key: string, limit: number, signal: AbortSignal, task: () => Promise<T>): Promise<T> {
    signal.throwIfAborted();
    if ((this.active.get(key) ?? 0) >= limit) {
      await new Promise<void>((resolve, reject) => {
        const queue = this.queues.get(key) ?? []; this.queues.set(key, queue);
        const start = () => { signal.removeEventListener('abort', abort); resolve(); };
        const abort = () => { const index = queue.indexOf(start); if (index >= 0) queue.splice(index, 1); reject(new MediaError('MEDIA_TIMEOUT')); };
        queue.push(start); signal.addEventListener('abort', abort, { once: true });
      });
    } else this.active.set(key, (this.active.get(key) ?? 0) + 1);
    try { signal.throwIfAborted(); return await task(); }
    finally {
      const next = this.queues.get(key)?.shift();
      if (next) next(); else { this.active.set(key, (this.active.get(key) ?? 1) - 1); this.queues.delete(key); }
    }
  }
}
const hostSlots = new Slots();
export interface LoadedImage { entry: MediaEntry; sourceUrl: string }

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
    const response = await hostSlots.run(url.hostname, 3, signal, () => new Promise<{ body: Buffer; contentType: string; location?: string; status: number }>((resolve, reject) => {
      const requestOptions: http.RequestOptions & { autoSelectFamily: boolean } = {
        method: 'GET', signal, agent: false, autoSelectFamily: false, headers: { 'User-Agent': options.userAgent, 'Accept-Encoding': 'identity' },
        lookup: (_hostname, _options, callback) => callback(null, host.address, host.family),
      };
      const request = (url.protocol === 'https:' ? https : http).request(url, requestOptions, res => {
        const status = res.statusCode ?? 0;
        const contentType = String(res.headers['content-type'] ?? '');
        if (status >= 300 && status < 400) {
          res.destroy(); resolve({ body: Buffer.alloc(0), contentType, location: res.headers.location, status }); return;
        }
        if (status < 200 || status >= 300) { res.destroy(); reject(new MediaError(`MEDIA_HTTP_${status}`, undefined, res.headers['retry-after'])); return; }
        const limit = options.maxBytes;
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
    }));
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
    if (signal.aborted) { void promise.catch(() => {}); abort(); return; }
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
/** Each factory has an isolated cache; production always uses the real host checker. */
export function createImageLoader(dependencies: { checkHost?: HostChecker; download?: MediaDownloader; env?: NodeJS.ProcessEnv } = {}) {
  const checkHost = dependencies.checkHost ?? checkPublicHost;
  const download = dependencies.download ?? downloadMedia;
  const env = dependencies.env ?? process.env;
  const images = new Cache<LoadedImage>();
  return async function loadImage(url: string): Promise<LoadedImage> {
    const signal = AbortSignal.timeout(10000);
    async function get(url: string, maxBytes: number): Promise<DownloadResponse> {
      const target = safeUrl(url);
      for (let attempt = 0; ; attempt++) {
        try {
          const run = async () => {
            const downloadSignal = AbortSignal.any([signal, AbortSignal.timeout(4000)]);
            return bounded(download(target, { signal: downloadSignal, maxBytes, checkHost, userAgent: env.SEYRA_MEDIA_USER_AGENT || USER_AGENT }), downloadSignal);
          };
          const result = download === downloadMedia ? await run() : await hostSlots.run(target.hostname, 3, signal, run);
          if (result.body.length > maxBytes) throw new MediaError('MEDIA_TOO_LARGE');
          return result;
        } catch (error) {
          if (attempt || !(error instanceof MediaError) || error.code !== 'MEDIA_HTTP_429') throw error;
          const parsed = error.retryAfter === undefined ? 1 : Number(error.retryAfter);
          const seconds = Number.isFinite(parsed) ? Math.max(0, Math.min(2, parsed)) : 1;
          await delay(seconds * 1000, undefined, { signal });
        }
      }
    }
    async function convert(url: string): Promise<LoadedImage> {
      const cached = images.get(url); if (cached) return cached;
      const result = await get(url, MAX_BYTES);
      if (!imageMagic(result.body)) throw new MediaError('MEDIA_UNSUPPORTED');
      const image = sharp(result.body, { limitInputPixels: 40_000_000, animated: false });
      const metadata = await image.metadata();
      if (Math.max(metadata.width ?? 0, metadata.height ?? 0) < 200) throw new MediaError('MEDIA_TOO_SMALL');
      const output = await image.rotate().resize({ width: 512, height: 512, fit: 'inside', withoutEnlargement: true }).jpeg({ quality: 82 }).toBuffer({ resolveWithObject: true });
      signal.throwIfAborted();
      const entry: MediaEntry = { id: `web:${createHash('sha256').update(output.data).digest('hex').slice(0, 32)}`, mime: 'image/jpeg', width: output.info.width, height: output.info.height, data: output.data.toString('base64') };
      const resolved = { entry, sourceUrl: result.url };
      images.set(url, resolved); return resolved;
    }
    try {
      return await bounded(convert(url), signal);
    } catch (error) {
      const code = signal.aborted || error instanceof Error && ['AbortError', 'TimeoutError'].includes(error.name) ? 'MEDIA_TIMEOUT' : error instanceof MediaError ? error.code : 'MEDIA_UNSUPPORTED';
      if (error instanceof MediaError && error.code === code) throw error;
      throw new MediaError(code);
    }
  };
}
