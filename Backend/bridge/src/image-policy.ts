import { createHash } from 'node:crypto';
import { z } from 'zod';

export const IMAGE_MODEL = 'muse-image-1.0';
export const IMAGE_CALL_LIMIT = 12;
export const IMAGE_SUBJECT_REJECTED = 'IMAGE_SUBJECT_REJECTED';
export const IMAGE_LIMIT_EXCEEDED = 'IMAGE_LIMIT_EXCEEDED';
const rejectedMessage = 'This subject cannot have a generated image. Use a text panel or a card without an image.';

export class ImagePolicyError extends Error {
  constructor(public readonly code: typeof IMAGE_SUBJECT_REJECTED | typeof IMAGE_LIMIT_EXCEEDED | 'EDIT_REJECTED') {
    super(code === 'EDIT_REJECTED' ? 'This change cannot be generated. Ask for it without brands, people or copyrighted works.' : code === IMAGE_SUBJECT_REJECTED ? rejectedMessage : 'At most 12 generated images per call.');
    this.name = 'ImagePolicyError';
  }
}

export const imageCategories = [
  'ingredient', 'dish', 'tool', 'mechanical_part', 'electrical_component',
  'material', 'plant', 'animal', 'household_object',
] as const;

export const blockedImageTerms = [
  'marvel', 'disney', 'pixar', 'star wars', 'harry potter', 'pokemon', 'nintendo',
  'playstation', 'xbox', 'lego', 'barbie', 'apple', 'iphone', 'samsung', 'tesla',
  'ferrari', 'bmw', 'nike', 'adidas', 'coca-cola', 'ikea', 'poster', 'movie',
  'film', 'book cover', 'album cover', 'logo', 'celebrity', 'portrait', 'selfie', 'photo of',
] as const;
const blockedPatterns = blockedImageTerms.map(term => ({ term, pattern: new RegExp(`\\b${term}\\b`, 'i') }));
const normalizeSubject = (subject: string) => subject.trim().replace(/\s+/g, ' ').toLowerCase();

// MCP must let subject policy failures reach the handler's structured error path.
export const generatedImageInputSchema = z.object({
  subject: z.string().describe("English subject, 2..80 characters, matching ^[A-Za-z0-9][A-Za-z0-9 ,'-]*$. Subject policy is validated by the bridge handler."),
  category: z.enum(imageCategories),
  alt: z.string().min(1).max(200),
}).strict();

export const generatedImageSchema = z.object({
  subject: z.string().min(2).max(80).regex(/^[A-Za-z0-9][A-Za-z0-9 ,'-]*$/),
  category: z.enum(imageCategories),
  alt: z.string().min(1).max(200),
}).strict().superRefine((image, ctx) => {
  const subject = normalizeSubject(image.subject);
  const allowApple = ['ingredient', 'dish', 'plant'].includes(image.category);
  if (blockedPatterns.some(({ term, pattern }) => !(term === 'apple' && allowApple) && pattern.test(subject)))
    ctx.addIssue({ code: 'custom', message: rejectedMessage, params: { code: IMAGE_SUBJECT_REJECTED } });
});

export type GeneratedImage = z.infer<typeof generatedImageSchema>;

export function validateGeneratedImage(value: unknown): GeneratedImage {
  const parsed = generatedImageSchema.safeParse(value);
  if (!parsed.success) throw new ImagePolicyError(IMAGE_SUBJECT_REJECTED);
  return parsed.data;
}

/** Validate the complete call before any Unity invocation or image generation. */
export function validateGeneratedImages(values: readonly unknown[]): GeneratedImage[] {
  if (values.length > IMAGE_CALL_LIMIT) throw new ImagePolicyError(IMAGE_LIMIT_EXCEEDED);
  return values.map(validateGeneratedImage);
}

export function generatedImageId(value: GeneratedImage): string {
  const image = validateGeneratedImage(value);
  return 'gen:' + createHash('sha256').update(`${IMAGE_MODEL}|${image.category}|${normalizeSubject(image.subject)}`).digest('hex').slice(0, 32);
}

export function generatedImagePrompt(value: GeneratedImage): string {
  const image = validateGeneratedImage(value);
  return `A plain generic example of ${image.subject}. Clean, simple illustration, clearly not a photograph. One centered subject on a plain light background. No text, letters, numbers, logos, brand marks, labels, packaging designs, watermarks, people or fictional characters.`;
}

export function validateEditInstruction(instruction: string): void {
  if (blockedPatterns.some(({ pattern }) => pattern.test(normalizeSubject(instruction))))
    throw new ImagePolicyError('EDIT_REJECTED');
}
export function editedImageId(source: string, instruction: string): string {
  return 'gen:' + createHash('sha256').update(IMAGE_MODEL + '|edit|' + source + '|' + normalizeSubject(instruction)).digest('hex').slice(0, 32);
}
export function editedImagePrompt(instruction: string): string {
  return `Edit this photo of a real place. Apply only this change: ${instruction}. Keep everything else exactly as it is: same viewpoint, framing, room, objects and lighting. The result must look like a realistic photo of the same place. Do not add text, letters, logos, brand marks, people or fictional characters. Do not change any person in the photo.`;
}

export function editedOverlayPrompt(instruction: string): string {
  return editedImagePrompt(instruction) + " Keep this object in exactly the same position and at the same size, so that the new version covers the old one. Keep the same perspective.";
}
