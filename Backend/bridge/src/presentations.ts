import { z } from 'zod';


const text = z.string().max(4000);
const required = text.refine(s => s.trim().length > 0, 'Non-empty text is required.');
const id = z.string().min(1).max(128).refine(s => s.trim().length > 0);
const number = z.number().finite().min(-1e12).max(1e12);
const photo = z.object({ imageRef: required, title: required, description: text.optional(), alt: text.optional() }).strict();
const base = { id, title: z.string().min(1).max(256).refine(s => s.trim().length > 0), description: text.optional() };
const property = z.object({ label: required, value: required, unit: text.optional() }).strict();
const section = z.object({ title: required, description: required }).strict();
const step = z.object({ id, title: required, description: required, notice: text.optional(), images: z.array(photo).max(10).optional() }).strict();
const document = z.object({ imageRef: required, title: required, description: text.optional(), alt: text.optional(), legend: text.optional(), annotations: z.array(text).max(30).optional() }).strict();
const alternative = z.object({ id, title: required, description: text.optional(), values: z.array(text).min(1).max(20), advantages: z.array(text).max(10).optional(), disadvantages: z.array(text).max(10).optional() }).strict();
const check = z.object({ id, title: required, description: text.optional(), group: text.optional(), completed: z.boolean().optional() }).strict();
const answer = z.object({ id, label: required, nextNodeId: id }).strict();
const node = z.object({ id, title: required, description: required, answers: z.array(answer).max(8).optional() }).strict();
const choice = z.object({ id, title: required, description: text.optional(), image: photo.optional() }).strict();
const field = z.object({ id, label: required, type: z.enum(['text', 'number', 'toggle', 'choice']), help: text.optional(), unit: text.optional(), value: text.optional(), required: z.boolean().optional(), min: number.optional(), max: number.optional(), choices: z.array(text).max(50).optional() }).strict();
const task = z.object({ id, title: required, description: text.optional(), phase: text.optional(), status: z.enum(['pending', 'active', 'done', 'blocked']).optional(), dependsOn: z.array(text).max(30).optional() }).strict();

export const presentationSchema = z.discriminatedUnion('type', [
  z.object({ ...base, type: z.literal('infoPanel'), image: photo.optional(), highlights: z.array(text).max(20).optional() }).strict(),
  z.object({ ...base, type: z.literal('technicalSheet'), properties: z.array(property).min(1).max(50), sections: z.array(section).max(20).optional(), images: z.array(photo).max(20).optional() }).strict(),
  z.object({ ...base, type: z.literal('gallery'), images: z.array(photo).min(1).max(50) }).strict(),
  z.object({ ...base, type: z.literal('stepGuide'), prerequisites: z.array(text).max(30).optional(), steps: z.array(step).min(1).max(50) }).strict(),
  z.object({ ...base, type: z.literal('documentViewer'), documents: z.array(document).min(1).max(30) }).strict(),
  z.object({ ...base, type: z.literal('comparison'), criteria: z.array(text).min(1).max(20), alternatives: z.array(alternative).min(2).max(8), recommendation: text.optional() }).strict(),
  z.object({ ...base, type: z.literal('checklist'), items: z.array(check).min(1).max(100) }).strict(),
  z.object({ ...base, type: z.literal('diagnostic'), startNodeId: id, nodes: z.array(node).min(1).max(50) }).strict(),
  z.object({ ...base, type: z.literal('choiceSelector'), multiple: z.boolean().optional(), options: z.array(choice).min(1).max(30), submitLabel: text.optional() }).strict(),
  z.object({ ...base, type: z.literal('parameterForm'), fields: z.array(field).min(1).max(30), submitLabel: text.optional() }).strict(),
  z.object({ ...base, type: z.literal('dataView'), mode: z.enum(['table', 'bars']), columns: z.array(text).max(12).optional(), rows: z.array(z.object({ cells: z.array(text).min(1).max(12) }).strict()).max(100).optional(), bars: z.array(z.object({ label: required, value: number, unit: text.optional() }).strict()).max(50).optional() }).strict(),
  z.object({ ...base, type: z.literal('workPlan'), tasks: z.array(task).min(1).max(100) }).strict(),
]).superRefine((d, ctx) => {
  const invalid = (message: string) => ctx.addIssue({ code: 'custom', message });
  const unique = (ids: string[]) => { if (new Set(ids).size !== ids.length) invalid('Duplicate IDs.'); };
  if (Buffer.byteLength(JSON.stringify(d), 'utf8') > 131072) invalid('Maximum 128 KiB of content.');
  switch (d.type) {
    case 'stepGuide': unique(d.steps.map(s => s.id)); break;
    case 'comparison': unique(d.alternatives.map(a => a.id)); if (d.alternatives.some(a => a.values.length !== d.criteria.length)) invalid('Each alternative requires one value per criterion.'); break;
    case 'checklist': unique(d.items.map(i => i.id)); break;
    case 'choiceSelector': unique(d.options.map(o => o.id)); break;
    case 'diagnostic': {
      unique(d.nodes.map(n => n.id)); const ids = new Set(d.nodes.map(n => n.id));
      if (!ids.has(d.startNodeId)) invalid('Unknown initial node.');
      for (const n of d.nodes) { unique((n.answers ?? []).map(a => a.id)); if (n.answers?.some(a => !ids.has(a.nextNodeId))) invalid('Unknown answer destination.'); }
      break;
    }
    case 'parameterForm':
      unique(d.fields.map(f => f.id));
      for (const f of d.fields) {
        const min = f.min ?? -1e12, max = f.max ?? 1e12;
        if (min > max) invalid('Field limits are inverted.');
        if (f.type === 'choice' && (!f.choices?.length || f.choices.some(s => !s.trim()) || new Set(f.choices).size !== f.choices.length)) invalid('Field choices are empty or duplicated.');
        if (f.value?.length) {
          if (f.type === 'number' && (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(f.value.trim()) || !Number.isFinite(Number(f.value)) || Number(f.value) < min || Number(f.value) > max)) invalid('Invalid initial numeric value.');
          if (f.type === 'toggle' && !['true', 'false'].includes(f.value)) invalid('Initial toggle value must be true or false.');
          if (f.type === 'choice' && !f.choices?.includes(f.value)) invalid('Unknown initial selection.');
        }
      }
      break;
    case 'dataView':
      if (d.mode === 'table' && (!d.columns?.length || !d.rows?.length || d.bars?.length || d.rows.some(r => r.cells.length !== d.columns!.length))) invalid('Table requires complete columns and rows, without bars.');
      if (d.mode === 'bars' && (!d.bars?.length || d.columns?.length || d.rows?.length)) invalid('Bars must not include table data.');
      break;
    case 'workPlan': {
      unique(d.tasks.map(t => t.id)); const tasks = new Map(d.tasks.map(t => [t.id, t])); const visiting = new Set<string>(), done = new Set<string>();
      const visit = (id: string): boolean => {
        if (done.has(id)) return true;
        if (visiting.has(id) || !tasks.has(id)) return false;
        visiting.add(id);
        for (const dep of tasks.get(id)!.dependsOn ?? []) if (!visit(dep)) return false;
        visiting.delete(id); done.add(id); return true;
      };
      if (d.tasks.some(t => !visit(t.id))) invalid('Task dependency is unknown or cyclic.'); break;
    }
  }
});

export function presentationTools(creation: z.ZodRawShape, entityId: z.ZodType<string>) {
  const dimensions = { width: z.number().finite().min(0.4).max(2), height: z.number().finite().min(0.4).max(2) };
  const reference = z.object({ entityId }).strict();
  return [
    { name: 'ui_catalog', readOnly: true, description: 'Lists the twelve complete semantic presentations and limits. Use ui_present to supply content once; the runtime builds layouts and controls. Full type schemas are in ui_present; images use registered imageRef values only.', schema: z.object({}).strict() },
    { name: 'ui_media_list', readOnly: true, description: 'Lists registered imageRef IDs and descriptions with pagination. No arbitrary file reads or URL downloads. Missing references render alternate text and appear in missingMedia.', schema: z.object({ offset: z.number().int().min(0).max(1000000), limit: z.number().int().min(1).max(100) }).strict() },
    { name: 'ui_present', readOnly: false, description: 'Creates one complete spatial presentation in Generated: infoPanel,technicalSheet,gallery,stepGuide,documentViewer,comparison,checklist,diagnostic,choiceSelector,parameterForm,dataView,workPlan. Requires normal creation name,parentId,local pose, width/height in local meters (0.4..2), and complete presentation content. presentation.id is a unique semantic content ID; returned entityId is the scene identity used by all subsequent tools. Max 16 live presentations. Paging, toggles, editing, selection, validation, zoom and submit run locally. No child/component calls. Uses existing requests, bounds, placement, trash, undo and persistence v4. Documents are registered images with annotations, not PDF/CAD parsing. Plans display states without executing tasks. Never retry a mutation automatically after a timeout.', schema: z.object({ ...creation, ...dimensions, presentation: presentationSchema }).strict() },
    { name: 'ui_update', readOnly: false, description: 'Replaces the complete presentation and dimensions by scene entityId, preserving presentation.id, scene identity, pose, parent and visibility. May change type. Validates/prepares before replacing; failure retains old content. Resets local state; undo restores the previous content and interaction snapshot. Do not send partial widget edits. Never retry automatically after a timeout.', schema: z.object({ entityId, ...dimensions, presentation: presentationSchema }).strict() },
    { name: 'ui_list', readOnly: true, description: 'Lists live managed presentations, including hidden views: entityId,semantic id,type,title,interaction. Excludes trash; saved entityIds survive load while component IDs change.', schema: z.object({}).strict() },
    { name: 'ui_get', readOnly: true, description: 'Reads confirmed content and local interaction by scene entityId: revision,index,nodeId,values,submitted,typed submission,lastAction,scroll,zoom,visible. missingMedia lists unresolved registered image references at the result root. Query after the user interacts, never by-frame polling. A later edit invalidates submission. Local controls do not execute tools or automatically send a chat turn.', schema: reference },
    { name: 'ui_close', readOnly: false, description: 'Hides the presentation window, retaining content and local interaction. A local display operation, outside scene undo history; visibility is saved. Does not delete or cancel work.', schema: reference },
    { name: 'ui_open', readOnly: false, description: 'Reopens a hidden presentation, retaining interaction. Independent windows can be visible simultaneously; no room-placement service or implicit camera lookup. Local display operation outside scene undo history.', schema: reference },
    { name: 'ui_remove', readOnly: false, description: 'Recoverably deletes the presentation through existing trash and history. restore_object or undo recovers the same entity and controls. Protects unrelated objects; no permanent asset/file deletion.', schema: reference },
  ] as const;
}
