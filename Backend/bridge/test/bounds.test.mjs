import { test } from 'node:test';
import assert from 'node:assert/strict';
import { tools } from '../dist/tools.js';

test('world bounds and placement schemas enforce explicit distance and direction', () => {
  const bounds=tools.find(t=>t.name==='scene_bounds');
  const placement=tools.find(t=>t.name==='place_relative');
  assert.equal(bounds.readOnly,true);assert.equal(placement.readOnly,false);
  assert.equal(bounds.schema.safeParse({entityId:'Generated'}).success,true);
  const args={entityId:'go_a',referenceId:'go_b',direction:'above',gap:0};
  for(const direction of ['above','below','right','left','front','behind'])
    assert.equal(placement.schema.safeParse({...args,direction}).success,true);
  for(const invalid of [{gap:-1},{gap:Infinity},{gap:NaN},{gap:'1'},{gap:undefined},{direction:'up'},{referenceId:null},{space:'local'}])
    assert.equal(placement.schema.safeParse({...args,...invalid}).success,false);
});
