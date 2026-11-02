import sharp from 'sharp';
import { type Detection } from './segmentation.js';
import type { ToolResult } from './camera.js';

type Box = Detection['box'];
export function chooseOriginal(detections: Detection[], width: number, height: number): Detection | undefined {
  const x = width / 2, y = height / 2;
  const contains = (d: Detection) => d.box.x1 <= x && d.box.x2 > x && d.box.y1 <= y && d.box.y2 > y;
  const distance = (d: Detection) => Math.hypot(Math.max(d.box.x1 - x, 0, x - d.box.x2), Math.max(d.box.y1 - y, 0, y - d.box.y2));
  return [...detections].sort((a, b) => Number(contains(b)) - Number(contains(a)) ||
    (contains(a) ? b.confidence - a.confidence : distance(a) - distance(b)))[0];
}
export function chooseEdited(original: Detection, detections: Detection[], width: number, height: number,
  editedWidth: number, editedHeight: number): Detection | undefined {
  const a = original.box;
  const iou = (d: Detection) => {
    const b = { x1: d.box.x1 * width / editedWidth, x2: d.box.x2 * width / editedWidth,
      y1: d.box.y1 * height / editedHeight, y2: d.box.y2 * height / editedHeight };
    const intersection = Math.max(0, Math.min(a.x2, b.x2) - Math.max(a.x1, b.x1)) *
      Math.max(0, Math.min(a.y2, b.y2) - Math.max(a.y1, b.y1));
    return intersection / ((a.x2 - a.x1) * (a.y2 - a.y1) + (b.x2 - b.x1) * (b.y2 - b.y1) - intersection);
  };
  const selected = [...detections].sort((a, b) => iou(b) - iou(a))[0];
  return selected && iou(selected) >= 0.2 ? selected : undefined;
}
function inside(d: Detection, x: number, y: number): boolean {
  const { box: b, raster, rasterWidth: w, rasterHeight: h } = d;
  if (!raster || x < b.x1 || x >= b.x2 || y < b.y1 || y >= b.y2) return false;
  return raster[Math.floor((y - b.y1) / (b.y2 - b.y1) * h) * w + Math.floor((x - b.x1) / (b.x2 - b.x1) * w)] === 1;
}
export function erodedSamplePoints(d: Detection, width: number, height: number): { x: number; y: number }[] {
  const points: { x: number; y: number }[] = [];
  const dx = width / 40, dy = height / 30;
  for (let y = d.box.y1 + dy; y < d.box.y2 - dy; y += dy)
    for (let x = d.box.x1 + dx; x < d.box.x2 - dx; x += dx) {
      if ([-1, 0, 1].every(i => [-1, 0, 1].every(j => inside(d, x + i * dx, y + j * dy)))) points.push({ x, y });
    }
  return points.length <= 24 ? points : Array.from({ length: 24 }, (_, i) => points[Math.floor((i + 0.5) * points.length / 24)]!);
}
export async function composeOverlay(edited: Buffer, width: number, height: number, original: Detection,
  editedDetection?: Detection, tint = false): Promise<{ png: Buffer; rect: Box }> {
  const decoded = await sharp(edited, { limitInputPixels: 40_000_000 }).rotate().removeAlpha().toBuffer({ resolveWithObject: true });
  const ew = decoded.info.width, eh = decoded.info.height;
  if (Math.abs((ew / eh) / (width / height) - 1) > 0.02) throw new Error('ratio_mismatch');
  if (!original.raster) throw new Error('no_mask');
  const b = original.box, e = editedDetection?.box;
  const x1 = Math.min(b.x1, e ? e.x1 * width / ew : b.x1), y1 = Math.min(b.y1, e ? e.y1 * height / eh : b.y1);
  const x2 = Math.max(b.x2, e ? e.x2 * width / ew : b.x2), y2 = Math.max(b.y2, e ? e.y2 * height / eh : b.y2);
  const rect = { x1: Math.max(0, Math.floor(x1 - (x2 - x1) * 0.06)), y1: Math.max(0, Math.floor(y1 - (y2 - y1) * 0.06)),
    x2: Math.min(width, Math.ceil(x2 + (x2 - x1) * 0.06)), y2: Math.min(height, Math.ceil(y2 + (y2 - y1) * 0.06)) };
  const rw = rect.x2 - rect.x1, rh = rect.y2 - rect.y1;
  for (const maximum of [1024, 768]) {
    const scale = Math.min(1, maximum / Math.max(rw, rh));
    const w = Math.max(1, Math.round(rw * scale)), h = Math.max(1, Math.round(rh * scale));
    const mask = Buffer.alloc(w * h);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const u = rect.x1 + (x + 0.5) * rw / w, v = rect.y1 + (y + 0.5) * rh / h;
      if (inside(original, u, v) || editedDetection && inside(editedDetection, u * ew / width, v * eh / height)) mask[y * w + x] = 255;
    }
    const alpha = await sharp(mask, { raw: { width: w, height: h, channels: 1 } })
      .blur(Math.max(2, Math.max(w, h) * 0.008)).linear(1.8, 0).toColourspace('b-w').raw().toBuffer();
    const full = await sharp(decoded.data).resize(width, height).toBuffer();
    const cropped = await sharp(full).extract({ left: rect.x1, top: rect.y1, width: rw, height: rh }).toBuffer();
    let color = sharp(cropped).resize(w, h);
    if (tint) color = color.composite([{ input: { create: { width: w, height: h, channels: 4, background: { r: 255, g: 0, b: 255, alpha: 0.35 } } } }]);
    const rgb = await color.removeAlpha().toBuffer();
    const png = await sharp(rgb).joinChannel(alpha, { raw: { width: w, height: h, channels: 1 } }).png().toBuffer();
    if (png.length <= 3 * 1024 * 1024) return { png, rect };
  }
  throw new Error('too_large');
}
export async function showOverlay(invoke: (name: string, args: unknown) => Promise<ToolResult>,
  args: { overlayId: string; captureId: string; cardId: string; rect: Box; points: { x: number; y: number }[]; png: Buffer }): Promise<void> {
  const { png, ...fields } = args;
  await invoke('camera_overlay_show', { ...fields, pngBase64: png.toString('base64') });
}
