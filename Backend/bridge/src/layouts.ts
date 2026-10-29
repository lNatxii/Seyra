import { z } from 'zod';
import { panelSchema } from './panels.js';
import { presentationSchema } from './presentations.js';

const id = z.string().trim().min(1).max(128).refine(v => v.toLowerCase() !== 'null');
const size = z.object({ width: z.number().finite().min(.16).max(2), height: z.number().finite().min(.04).max(2) }).strict();
const layout = z.enum(['arc', 'plane', 'hub', 'surround', 'anchor', 'wall', 'surface']);
const behavior = z.enum(['fixed_facing', 'fixed_aligned', 'billboard']);
const params = z.object({ radius: z.number().finite().optional(), rows: z.number().int().min(1).max(3).optional(),
  distance: z.number().finite().min(.8).max(3).optional(), arrangement: z.enum(['row', 'column', 'grid']).optional(),
  columns: z.number().int().min(1).max(6).optional(), physicalId: id.optional(), allowSpill: z.boolean().optional(), pose: z.enum(['standing', 'lying']).optional() }).strict();
const common = { layout, behavior: behavior.optional(), itemSize: size, centerSize: size.optional(), gap: z.number().finite().min(0).max(.5).optional(), params: params.optional() };
const target = { targetId: id, side: z.enum(['above', 'front', 'left', 'right']).optional() };
const { width: _width, height: _height, ...panelFields } = panelSchema.shape;
const card = z.object(panelFields).strict();
const item = z.union([
  z.object({ name: z.string().trim().min(1).max(256), panel: card, targetId: id.optional(), side: target.side }).strict(),
  z.object({ name: z.string().trim().min(1).max(256), presentation: presentationSchema, targetId: id.optional(), side: target.side }).strict(),
]);
type Request = { layout?: string; behavior?: string; centerSize?: unknown; itemSize?: z.infer<typeof size>; params?: z.infer<typeof params> };
function validate(a: Request, count: number | undefined, ctx: z.RefinementCtx) {
  const fail = (message: string) => ctx.addIssue({ code: 'custom', message });
  const allowed: Record<string, string[]> = { arc: ['fixed_facing', 'billboard'], plane: ['fixed_facing'], hub: ['fixed_facing', 'billboard'], surround: ['fixed_facing'], anchor: ['fixed_facing', 'billboard'], wall: ['fixed_aligned'], surface: ['fixed_aligned'] };
  if (a.layout && a.behavior && !allowed[a.layout]?.includes(a.behavior)) fail('LAYOUT_BEHAVIOR_INVALID');
  if (a.layout === 'hub' && count !== undefined && (count < 3 || count > 9)) fail('hub requires 3..9 items.');
  if (a.layout && a.centerSize && a.layout !== 'hub') fail('centerSize requires hub.');
  if (a.layout === 'surface' && a.itemSize && (a.itemSize.height > (a.params?.pose === 'lying' ? .3 : .35) || a.params?.pose === 'lying' && a.itemSize.width > .3)) fail('LAYOUT_SIZE_INVALID');
  if (!a.params || !a.layout) return;
  const keys: Record<string, string[]> = { arc: ['radius', 'rows'], surround: ['radius', 'rows'], plane: ['distance', 'arrangement', 'columns'], hub: ['distance'], anchor: [], wall: ['physicalId', 'allowSpill'], surface: ['physicalId', 'pose'] };
  for (const key of Object.keys(a.params)) if (!keys[a.layout].includes(key)) fail(`Unexpected ${key} for ${a.layout}.`);
  if (a.params.radius !== undefined && (a.params.radius < (a.layout === 'arc' ? 1.2 : 1.4) || a.params.radius > (a.layout === 'arc' ? 2.6 : 3))) fail('Invalid radius.');
  if (a.layout === 'surround' && a.params.rows === 3) fail('surround allows at most two rows.');
  if (a.params.columns && a.params.arrangement && a.params.arrangement !== 'grid') fail('columns requires grid.');
}
const guidance = 'Preferred way to create two or more related cards with one shared size. Use layout_plan before generating content. On LAYOUT_DOES_NOT_FIT group or select items; do not collapse rich items into a long list. Wall/surface require a ready room and use fixed_aligned by default. Wall cards retain the 12 degree reading minimum; use taller cards on distant walls. Surface supports standing (height <= .35m) or lying (<= .30 x .30m). ';
export const layoutTools = [
  { name: 'layout_plan', readOnly: true, description: guidance + 'Read-only world poses and capacity, using the working head yaw. Anchor requires one managed entityId or captured physicalId target per card. Does not create or reserve objects.',
    schema: z.object({ ...common, count: z.number().int().min(1).max(24), targets: z.array(z.object(target).strict()).min(1).max(24).optional() }).strict().superRefine((a, ctx) => {
      validate(a, a.count, ctx);
      if (a.layout === 'anchor' ? a.targets?.length !== a.count : a.targets !== undefined) ctx.addIssue({ code: 'custom', message: 'Only anchor requires targets, one per item.' });
    }) },
  { name: 'layout_create', readOnly: false, description: guidance + 'Creates an atomic layout group using existing panel/presentation contracts, with automatic request grouping. Photos accept only a registered imageRef; web images (imageSearch, imageUrl) are disabled. Never supply web: imageRef values. Supports request undo, format 5 persistence, billboard and fixed_facing planes. Never retry ambiguous mutations.',
    schema: z.object({ ...common, name: z.string().trim().min(1).max(256), parentId: id.optional(), items: z.array(item).min(1).max(24) }).strict().superRefine((a, ctx) => {
      validate(a, a.items.length, ctx);
      a.items.forEach((i, index) => {
        if (a.layout === 'anchor' ? !i.targetId : i.targetId !== undefined || i.side !== undefined) ctx.addIssue({ code: 'custom', message: 'Only anchor requires item targets.' });
        const s = a.layout === 'hub' && index === 0 ? a.centerSize ?? a.itemSize : a.itemSize;
        if ('panel' in i) { const parsed = panelSchema.safeParse({ ...i.panel, ...s }); if (!parsed.success) ctx.addIssue({ code: 'custom', message: 'LAYOUT_SIZE_INVALID: invalid panel size/content.' }); }
        else if (s.width < .4 || s.height < .4) ctx.addIssue({ code: 'custom', message: 'LAYOUT_SIZE_INVALID: presentations require .4..2 meters.' });
      });
    }) },
  { name: 'layout_apply', readOnly: false, description: guidance + 'Replans a managed layout or a container of equally sized direct cards. Optional order is a permutation of all current child entityIds. Preserves card content and identities, records one undoable group operation and restores behavior on undo. Existing specifications supply omitted fields. Never retry ambiguous mutations.',
    schema: z.object({ rootId: id, layout: layout.optional(), behavior: behavior.optional(), gap: common.gap, params: params.optional(), order: z.array(id).min(1).max(24).optional() }).strict().superRefine((a, ctx) => validate(a, undefined, ctx)) },
] as const;
