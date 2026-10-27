import { z } from 'zod';

// Full semantic replacement; no hierarchy paths, markup, asset paths or executable callbacks.
export const panelSchema = z.object({
  version: z.literal(1),
  kind: z.enum(['panel', 'label']),
  title: z.string().max(256),
  text: z.string().max(16000).refine(value => value.trim().length > 0, 'Panel text must not be empty.'),
  width: z.number().finite().min(0.16).max(2),
  height: z.number().finite().min(0.04).max(2),
  fontSize: z.number().finite().min(12).max(72),
}).strict().superRefine((panel, context) => {
  const invalid = (message: string) => context.addIssue({ code: 'custom', message });
  if (panel.kind === 'panel' && panel.height < 0.24) invalid('Panel height must be at least 0.24 meters.');
  if (panel.kind === 'label' && panel.title.length !== 0) invalid('Labels require an empty title.');
  if (panel.kind === 'label' && panel.height * 1000 < panel.fontSize) invalid('Label height must fit at least one text line.');
});
