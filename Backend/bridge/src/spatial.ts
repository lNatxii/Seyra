import { z } from 'zod';

export const spatialFrame = z.enum(['world', 'viewer', 'reference']);
export const spatialOrientation = z.enum(['keep', 'face_viewer', 'match_reference']);
export const placement = {
  entityId: z.string().trim().min(1).max(128).refine(v => v.toLowerCase() !== 'null', 'Use an actual ID; never null.'), destination: z.enum(['wall', 'tabletop', 'air']),
  physicalId: z.string().regex(/^physical:[0-9a-fA-F-]{36}$/).optional(),
  allowAirFallback: z.boolean().default(true), minScale: z.number().finite().min(.1).max(1).default(1),
};
export const roomTools = [
  { name: 'spatial_candidates', readOnly: true,
    description: 'Plans placement of existing managed Generated content without moving or reserving it (applied=false). destination wall/tabletop/air, optional physicalId selects a captured support, allowAirFallback defaults true, minScale .1..1 defaults 1 and must be 1 for any reading panel/presentation. Uses fixed task yaw, current viewer position, captured room geometry and current reservations. Returns best candidate pose, relative scale, score, roomRevision, physicalValidation and up to 40 rejection reasons. Candidates do not guarantee a later commit; use expectedRoomRevision on spatial_place. Captured geometry cannot detect moved or new furniture. Without a room, only air fallback can be returned and physicalValidation=false.',
    schema: z.object(placement).strict().refine(a => a.destination !== 'air' || a.physicalId === undefined, 'Air has no physical support.') },
  { name: 'spatial_place', readOnly: false,
    description: 'Plans and commits a checked world pose for existing managed Generated content. Same arguments as spatial_candidates plus optional expectedRoomRevision. Revalidates captured geometry, current viewer and reservations before applying. Rejects held objects, non-uniform/negative ancestor scales and stale room revision. success=true with applied=false and PLACEMENT_REJECTED means no compatible site, no mutation and no history entry; explain the reason and offer an alternative. A committed translation/rotation/relative scale is one undoable Transform operation with existing HISTORY_CONFLICT and save/load. Does not create content or scan; use existing create_panel/ui_present/create_primitive first. Bounds and ray checks cover only captured geometry, not newly moved furniture. Never retry an ambiguous mutation.',
    schema: z.object({ ...placement, expectedRoomRevision: z.number().int().min(0).optional() }).strict().refine(a => a.destination !== 'air' || a.physicalId === undefined, 'Air has no physical support.') },
  { name: 'room_get', readOnly: true,
    description: 'Queries captured room state, roomRevision, simulated, viewerReady, counts and reason. States: AirOnly, WaitingForPermission, Loading, Ready, PermissionDenied, Unavailable, Suspended, Error. Does not request permission, scan or load a test room. Without Ready no physical reference is usable. Geometry describes the captured scan only; moved furniture and new objects are unknown.',
    schema: z.object({}).strict() },
  { name: 'room_objects', readOnly: true,
    description: 'Lists captured surfaces and furniture with read-only physicalId, kind, original labels, bounds, surface polygon/pose or oriented volume box, known facing and viewer-relative position/side when tracking is available. Filter kind and label (exact case-insensitive); paginated up to 64. A tabletop and its supporting volume share physicalId and have distinct kinds. IDs apply only to the current runtime and room; query again after roomRevision changes. Physical IDs are valid references for scene_bounds/place_relative, never mutation targets or parents. This does not discover moved furniture.',
    schema: z.object({ offset: z.number().int().min(0).max(1000000).default(0), limit: z.number().int().min(1).max(64).default(32), kind: z.enum(['wall', 'floor', 'ceiling', 'opening', 'tabletop', 'object']).optional(), label: z.string().trim().min(1).max(128).optional() }).strict() },
] as const;
