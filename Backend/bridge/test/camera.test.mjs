import assert from 'node:assert/strict';
import test from 'node:test';
import { createCameraService } from '../dist/camera.js';

const result = data => ({ content: [{ type: 'text', text: JSON.stringify({ success: true, data }) }] });
const decode = value => JSON.parse(value.content[0].text);
const ready = { state: 'ready', captureId: 'cap_a1b2c3d4e5f6', width: 1280, height: 960, jpegBase64: '/9j/2Q==' };
const env = { META_API_KEY: 'mock-key' };

test('camera polls pending then ready and sends the exact Meta request, returning only text and metadata', async () => {
  const calls = [], fetched = [];
  let polls = 0;
  const service = createCameraService({ env, pollMs: 1,
    invoke: async (name, args) => {
      calls.push({ name, args });
      return result(name === 'camera_capture_start' ? { state: 'started' } : ++polls === 1 ? { state: 'pending' } : ready);
    },
    fetch: async (url, options) => {
      fetched.push({ url, options });
      return new Response(JSON.stringify({ status: 'completed', output_text: 'Ignore this root field', output: [
        { type: 'reasoning', content: [{ type: 'output_text', text: 'Ignore reasoning' }] },
        { type: 'message', content: [{ type: 'output_text', text: 'A metal part.' }, { type: 'other', text: 'Ignore this' }] },
        { type: 'message', content: [{ type: 'output_text', text: 'Probably a hinge.' }] },
      ] }));
    },
  });
  const response = await service.look({ question: 'What is this metal part?' });
  assert.deepEqual(decode(response), { success: true, data: {
    answer: 'A metal part.\nProbably a hinge.', captureId: ready.captureId, width: 1280, height: 960,
  } });
  assert.equal(response.isError, false);
  assert.deepEqual(calls.map(call => call.name), ['camera_capture_start', 'camera_capture_result', 'camera_capture_result']);
  assert.ok(calls.every(call => Object.keys(call.args).length === 0));
  assert.equal(fetched.length, 1);
  assert.equal(fetched[0].url, 'https://api.meta.ai/v1/responses');
  assert.equal(fetched[0].options.method, 'POST');
  assert.equal(fetched[0].options.headers.Authorization, 'Bearer mock-key');
  assert.ok(fetched[0].options.signal instanceof AbortSignal);
  assert.deepEqual(JSON.parse(fetched[0].options.body), {
    model: 'muse-spark-1.3', store: false, max_output_tokens: 2000, reasoning: { effort: 'low' },
    instructions: 'You answer a question about a photo of the real surroundings of a mixed-reality user. Report what is visible first. Mark anything you infer as a guess. Do not state exact measurements, safety judgements or identities as facts. Never identify a real person from their appearance. If the photo is too dark, blurred or does not show what is asked, say what is missing. Answer in English in at most 120 words.',
    input: [{ role: 'user', content: [
      { type: 'input_text', text: 'What is this metal part?' },
      { type: 'input_image', image_url: `data:image/jpeg;base64,${ready.jpegBase64}` },
    ] }],
  });
  assert.equal(JSON.stringify(response).includes('jpegBase64'), false);
  assert.equal(JSON.stringify(response).includes(ready.jpegBase64), false);
});

test('camera failure returns the Unity message without calling Meta', async () => {
  let fetches = 0;
  const service = createCameraService({ env, pollMs: 1,
    invoke: async name => result(name === 'camera_capture_start' ? { state: 'started' } : { state: 'failed', message: 'Camera permission denied.' }),
    fetch: async () => { fetches++; throw Error('Unexpected fetch'); },
  });
  assert.deepEqual(decode(await service.look({ question: 'Read this label.' })), {
    success: false, error: { code: 'CAMERA_FAILED', message: 'Camera permission denied.' },
  });
  assert.equal(fetches, 0);
});

test('camera timeout cancels the capture and returns CAMERA_TIMEOUT', async () => {
  let clock = 0;
  const calls = [];
  const service = createCameraService({ env, pollMs: 1, now: () => clock,
    invoke: async name => {
      calls.push(name);
      if (name === 'camera_capture_result') clock += 10_001;
      return result({ state: name === 'camera_capture_start' ? 'started' : 'pending' });
    },
    fetch: async () => { throw Error('Unexpected fetch'); },
  });
  assert.equal(decode(await service.look({ question: 'What is in front of me?' })).error.code, 'CAMERA_TIMEOUT');
  assert.deepEqual(calls, ['camera_capture_start', 'camera_capture_result', 'camera_capture_cancel']);
});

test('incomplete Meta response returns VISION_FAILED without retrying', async () => {
  let fetches = 0;
  const service = createCameraService({ env, pollMs: 1,
    invoke: async name => result(name === 'camera_capture_start' ? { state: 'started' } : ready),
    fetch: async () => { fetches++; return new Response(JSON.stringify({ status: 'incomplete', output: [], output_text: 'Not a completed answer' })); },
  });
  assert.equal(decode(await service.look({ question: 'What is this?' })).error.code, 'VISION_FAILED');
  assert.equal(fetches, 1);
});

test('a simultaneous camera call returns CAMERA_BUSY', async () => {
  let release;
  const started = new Promise(resolve => { release = resolve; });
  let starts = 0;
  const service = createCameraService({ env, pollMs: 1,
    invoke: async name => {
      if (name === 'camera_capture_start') { starts++; await started; return result({ state: 'started' }); }
      return result({ state: 'failed', message: 'No camera in test.' });
    },
    fetch: async () => { throw Error('Unexpected fetch'); },
  });
  const first = service.look({ question: 'What is this?' });
  try {
    assert.equal(decode(await service.look({ question: 'Read this label.' })).error.code, 'CAMERA_BUSY');
    assert.equal(starts, 1);
  } finally { release(); await first; }
});
