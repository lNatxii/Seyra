import sharp, { type OverlayOptions } from 'sharp';
import type { Detection } from './segmentation.js';

export const palette = ['#FF4D4D', '#2DD4BF', '#FACC15', '#60A5FA', '#F472B6', '#A3E635', '#FB923C', '#C084FC'];
const maxBytes = 1024 * 1024;
const escapeXml = (text: string): string => text.replace(/[&<>"']/g, character => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;',
})[character]!);

export async function renderSegmentation(
  jpeg: Buffer, detections: Detection[],
): Promise<{ jpeg: Buffer; width: number; height: number }> {
  const image = sharp(jpeg, { limitInputPixels: 40_000_000 }).rotate();
  // metadata() describes the input, before rotate() applies EXIF orientation.
  const metadata = await image.metadata();
  if (!metadata.width || !metadata.height) throw new Error('The photo dimensions are unavailable.');
  const swapped = metadata.orientation !== undefined && metadata.orientation >= 5 && metadata.orientation <= 8;
  const sourceWidth = swapped ? metadata.height : metadata.width;
  const sourceHeight = swapped ? metadata.width : metadata.height;
  const { data: background, info } = await image.resize(512, 512, { fit: 'inside', withoutEnlargement: true })
    .toBuffer({ resolveWithObject: true });
  const { width, height } = info;
  const scaleX = width / sourceWidth;
  const scaleY = height / sourceHeight;
  const layers: OverlayOptions[] = [];
  const annotations: string[] = [];
  // Keep the original index for colour assignment while painting low confidence first.
  const ordered = detections.map((detection, index) => ({ detection, index }))
    .sort((a, b) => a.detection.confidence - b.detection.confidence);
  for (const { detection, index } of ordered) {
    const color = palette[index % palette.length];
    const { box, raster, rasterWidth, rasterHeight } = detection;
    const left = box.x1 * scaleX;
    const top = box.y1 * scaleY;
    const boxWidth = (box.x2 - box.x1) * scaleX;
    const boxHeight = (box.y2 - box.y1) * scaleY;
    if (boxWidth <= 0 || boxHeight <= 0) continue;
    if (raster !== null) {
      const rgba = Buffer.alloc(width * height * 4);
      const rgb = [1, 3, 5].map(offset => parseInt(color.slice(offset, offset + 2), 16));
      for (let y = Math.max(0, Math.ceil(top - 0.5)); y < Math.min(height, Math.ceil(top + boxHeight - 0.5)); y++) {
        const ry = Math.min(rasterHeight - 1, Math.floor(((y + 0.5 - top) / boxHeight) * rasterHeight));
        for (let x = Math.max(0, Math.ceil(left - 0.5)); x < Math.min(width, Math.ceil(left + boxWidth - 0.5)); x++) {
          const rx = Math.min(rasterWidth - 1, Math.floor(((x + 0.5 - left) / boxWidth) * rasterWidth));
          if (raster[ry * rasterWidth + rx] !== 1) continue;
          const offset = (y * width + x) * 4;
          rgba[offset] = rgb[0];
          rgba[offset + 1] = rgb[1];
          rgba[offset + 2] = rgb[2];
          rgba[offset + 3] = Math.round(255 * 0.45);
        }
      }
      layers.push({ input: rgba, raw: { width, height, channels: 4 }, blend: 'over' });
    }
    const labelWidth = Math.min(width, Math.max(12, detection.label.length * 8 + 8));
    const labelHeight = Math.min(20, height);
    const labelX = Math.max(0, Math.min(left, width - labelWidth));
    const labelY = Math.max(0, Math.min(top >= labelHeight ? top - labelHeight : top, height - labelHeight));
    const fittedText = detection.label.length * 8 + 8 > width
      ? ` textLength="${Math.max(1, labelWidth - 8)}" lengthAdjust="spacingAndGlyphs"` : '';
    annotations.push(
      `<rect x="${left}" y="${top}" width="${boxWidth}" height="${boxHeight}" fill="none" stroke="${color}" stroke-width="2"/>` +
      `<rect x="${labelX}" y="${labelY}" width="${labelWidth}" height="${labelHeight}" fill="${color}"/>` +
      `<text x="${labelX + 4}" y="${labelY + 15}" fill="black" font-size="14" font-family="sans-serif"${fittedText}>${escapeXml(detection.label)}</text>`,
    );
  }
  layers.push({ input: Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}">${annotations.join('')}</svg>`) });
  const composite = sharp(background).composite(layers);
  let output = await composite.clone().jpeg({ quality: 82 }).toBuffer();
  if (output.length > maxBytes) output = await composite.clone().jpeg({ quality: 65 }).toBuffer();
  if (output.length > maxBytes) throw new Error('The segmented photo exceeds the image size limit.');
  return { jpeg: output, width, height };
}
