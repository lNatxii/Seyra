import { z } from 'zod';
import { panelSchema } from './panels.js';
import { presentationSchema } from './presentations.js';

export const interactionDetailSchema = z.union([
  z.object({ panel: panelSchema.refine(panel => panel.kind === 'panel', 'Interaction details require a panel, not a label.') }).strict(),
  z.object({ presentation: presentationSchema, width: z.number().finite().min(.4).max(2), height: z.number().finite().min(.4).max(2) }).strict(),
]);

export const interactionFields = {
  grabbable: z.boolean().optional(),
  returnOnRelease: z.boolean().optional(),
  exclusive: z.boolean().optional(),
  detail: interactionDetailSchema.optional(),
  detailSide: z.enum(['above', 'right', 'left']).optional(),
  detailGap: z.number().finite().min(0).max(.5).optional(),
  detailOpen: z.boolean().optional(),
};
export const interactionFieldsSchema = z.object(interactionFields).strict();
export function interactionSetSchema(id: z.ZodType<string>) {
  return z.discriminatedUnion('interactive', [
    z.object({ entityId: id, interactive: z.literal(false) }).strict(),
    z.object({ entityId: id, interactive: z.literal(true), ...interactionFields }).strict(),
  ]);
}
export const interactionDescription = 'Makes a live Generated primitive or container interactive by ray. Hover highlights it; a short selection toggles its optional detail panel or presentation. grabbable defaults false, returnOnRelease and exclusive default true, detailSide defaults above, detailGap defaults 0.05 m (0..0.5), detailOpen defaults false. Replaces configuration; omitted detail preserves the current detail. interactive=false accepts no other interaction fields and removes interaction. Returns confirmed object state and operationId; undoable. Never retry ambiguous mutations.';
