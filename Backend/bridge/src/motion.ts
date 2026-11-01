import { z } from 'zod';

export function motionSchema(id: z.ZodType<string>, vector: z.ZodType) {
  const axis = vector.refine(value => {
    const v = value as { x: number; y: number; z: number };
    return v.x !== 0 || v.y !== 0 || v.z !== 0;
  }, 'axis must be non-zero.').optional();
  const speed = (limit: number) => z.number().finite().min(-limit).max(limit).refine(value => value !== 0, 'speed must be non-zero.');
  return z.discriminatedUnion('kind', [
    z.object({ kind: z.literal('spin'), axis, speed: speed(720) }).strict(),
    z.object({ kind: z.literal('orbit'), axis, speed: speed(360), pivotId: id.optional(), pivotPoint: vector.optional() }).strict(),
    z.object({ kind: z.literal('bob'), axis, amplitude: z.number().finite().min(.005).max(.5), period: z.number().finite().min(.2).max(60) }).strict(),
  ]).superRefine((motion, context) => {
    if (motion.kind === 'orbit' && (motion.pivotId === undefined) === (motion.pivotPoint === undefined))
      context.addIssue({ code: 'custom', message: 'Use exactly one of pivotId or pivotPoint.' });
  });
}

export function motionTools(id: z.ZodType<string>, vector: z.ZodType) {
  return [
    { name: 'motion_set', readOnly: false,
      schema: z.object({ entityId: id, motion: motionSchema(id, vector), paused: z.boolean().optional() }).strict(),
      description: 'Sets or replaces one parametric motion on live Generated content. spin controls local rotation (speed -720..720 deg/s, non-zero); orbit controls parent-local position around exactly one live pivotId or parent-local pivotPoint (speed -360..360 deg/s, non-zero); bob controls parent-local position (amplitude .005..0.5, period .2..60 s). Optional non-zero axis defaults up and is normalized. spin can coexist with orbit or bob; orbit and bob conflict. Captures the current pose as base and resets phase. Paused motion retains its channel. Root, lines and layout objects/children are invalid. Maximum 64 animated objects. Returns confirmed object state with motions and operationId; undoable. Never retry ambiguous mutations.' },
    { name: 'motion_remove', readOnly: false,
      schema: z.object({ entityId: id, kind: z.enum(['spin', 'orbit', 'bob']) }).strict(),
      description: 'Removes one motion kind, keeping the current pose and releasing its channel. Returns confirmed object state with motions and operationId; undoable. MOTION_NOT_FOUND if absent. Never retry ambiguous mutations.' },
    { name: 'motion_control', readOnly: false,
      schema: z.object({ entityId: id.optional(), action: z.enum(['pause', 'resume', 'reset']) }).strict(),
      description: 'Pauses, resumes or resets motion on an object and descendants, or all Generated content when entityId is omitted. Pause/resume preserve phase; reset restores base pose and phase zero. Pausing retains controlled channels. Returns affected count; zero is valid. Does not record history. Never retry ambiguous mutations.' },
  ];
}
