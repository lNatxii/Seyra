import { z } from 'zod';

export const spatialFrame = z.enum(['world', 'viewer', 'reference']);
export const spatialOrientation = z.enum(['keep', 'face_viewer', 'match_reference']);
export const roomTools = [
  { name: 'room_get', readOnly: true,
    description: 'Queries captured room state, roomRevision, simulated, viewerReady, counts and reason. States: AirOnly, WaitingForPermission, Loading, Ready, PermissionDenied, Unavailable, Suspended, Error. Does not request permission, scan or load a test room. Without Ready no physical reference is usable. Geometry describes the captured scan only; moved furniture and new objects are unknown.',
    schema: z.object({}).strict() },
  { name: 'room_objects', readOnly: true,
    description: 'Lists captured surfaces and furniture with read-only physicalId, kind, original labels, bounds, surface polygon/pose or oriented volume box, known facing and viewer-relative position/side when tracking is available. Filter kind and label (exact case-insensitive); paginated up to 64. A tabletop and its supporting volume share physicalId and have distinct kinds. IDs apply only to the current runtime and room; query again after roomRevision changes. Physical IDs are valid references for scene_bounds/place_relative, never mutation targets or parents. This does not discover moved furniture.',
    schema: z.object({ offset: z.number().int().min(0).max(1000000).default(0), limit: z.number().int().min(1).max(64).default(32), kind: z.enum(['wall', 'floor', 'ceiling', 'opening', 'tabletop', 'object']).optional(), label: z.string().trim().min(1).max(128).optional() }).strict() },
] as const;
