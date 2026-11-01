import { z } from 'zod';

export function lineSchema(id: z.ZodType<string>, vector: z.ZodType, key?: z.ZodType<string>) {
  const endpoint = z.union([
    z.object({ entityId: id }).strict(),
    z.object({ position: vector }).strict(),
    ...(key ? [z.object({ key }).strict()] : []),
  ] as [z.ZodType, z.ZodType, ...z.ZodType[]]);
  const channel = z.number().finite().min(0).max(1);
  return z.object({ points: z.array(endpoint).min(2).max(16), width: z.number().finite().min(.001).max(.05).optional(),
    color: z.object({ r: channel, g: channel, b: channel, a: z.literal(1) }).strict().optional() }).strict();
}
export const pointRadius = z.number().finite().min(.002).max(.1);
export function pointDirection(vector: z.ZodType) {
  return vector.refine(v => { const p = v as { x: number; y: number; z: number }; return p.x !== 0 || p.y !== 0 || p.z !== 0; }, 'direction must be non-zero.');
}
export function lineTools(id: z.ZodType<string>, vector: z.ZodType, creation: { name: z.ZodString }, appearanceWithoutEntityId: z.ZodType) {
  const line = lineSchema(id, vector);
  return [
    { name: 'create_point', readOnly: false, schema: z.object({ name: creation.name, targetId: id, direction: pointDirection(vector).optional(), radius: pointRadius.optional(), appearance: appearanceWithoutEntityId.optional() }).strict(),
      description: 'Creates a managed sphere centered on a live primitive collider surface, as its child. World direction defaults up; radius .002..0.1 m defaults .01. Default appearance unlit white. Ancestors require positive uniform scales. Cylinder uses its capsule collider; plane/quad are one-sided. Supports undo, trash and persistence as a normal primitive. Returns targetId and confirmed sphere state. Never retry ambiguous mutations.' },
    { name: 'create_line', readOnly: false, schema: z.object({ name: creation.name, parentId: id, line }).strict(),
      description: 'Creates an opaque managed polyline under a confirmed Generated parent (Generated routes to the open request). line.points has 2..16 endpoints, each exactly entityId or position local to its parent. Live managed targets cannot be lines. Follows targets and terminates at panel borders. Width .001..0.05 m defaults .01; color defaults white, alpha must be 1. Maximum 128 live lines. Lines do not contribute bounds; missing targets hide the line until restored. Undo/trash/persistence v7 supported. Never retry ambiguous mutations.' },
    { name: 'update_line', readOnly: false, schema: z.object({ entityId: id, line }).strict(),
      description: 'Replaces the complete managed line content: points, width and color, using create_line contracts and defaults. Preserves identity, parent and request. Undoable and persistible. Never retry ambiguous mutations.' },
  ];
}
