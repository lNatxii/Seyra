import assert from 'node:assert/strict';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

// Run with either configured scene in Play Mode and no other bridge owning port 4100.
const client=new Client({name:'seyra-unity-smoke',version:'1.0.0'});
const transport=new StdioClientTransport({command:process.execPath,args:['dist/index.js'],cwd:process.cwd(),env:{...process.env,SEYRA_UNITY_PORT:process.env.SEYRA_UNITY_PORT??'4100'},stderr:'pipe'});
async function call(name,args={}) {
  const response=await client.callTool({name,arguments:args});return JSON.parse(response.content[0].text);
}
function ok(result){assert.equal(result.success,true,JSON.stringify(result));return result.data;}
const vector=(x=0,y=0,z=0)=>({x,y,z});
try {
  await client.connect(transport);
  let hierarchy;
  for(let i=0;i<12;i++){
    hierarchy=await call('scene_hierarchy',{limit:2});
    if(hierarchy.success)break;
    assert.equal(hierarchy.error.code,'UNITY_NOT_CONNECTED',JSON.stringify(hierarchy));
    await new Promise(r=>setTimeout(r,500));
  }
  const first=ok(hierarchy);assert.ok(first.runtimeId);assert.ok(first.generatedRootId);
  let nodes=[...first.nodes],offset=first.nextOffset;
  while(offset!==null){const page=ok(await call('scene_hierarchy',{offset,limit:2,expectedRevision:first.revision}));nodes.push(...page.nodes);offset=page.nextOffset;}
  assert.equal(nodes.length,first.total);assert.equal(new Set(nodes.map(n=>n.entityId)).size,nodes.length);assert.ok(nodes.every(n=>typeof n.parentId==='string'));
  const generated=ok(await call('scene_get',{entityId:'Generated',componentLimit:1}));assert.equal(generated.entityId,first.generatedRootId);assert.equal(generated.components[0].type,'UnityEngine.Transform');assert.ok(generated.components[0].componentId.startsWith('cmp_'));
  const name='MCP_Test_'+Date.now();
  const parent=ok(await call('create_gameobject',{name,parentId:'Generated',position:vector(1,2,3),rotation:vector(),scale:vector(1,1,1)}));
  const child=ok(await call('create_gameobject',{name:'Child',parentId:parent.entityId,position:vector(.25,.5,.75),rotation:vector(0,45,0),scale:vector(1,2,3)}));
  assert.equal(child.parentId,parent.entityId);
  const moved=ok(await call('set_transform',{entityId:child.entityId,space:'local',position:vector(2,3,4)}));
  assert.deepEqual(moved.transform.localPosition,vector(2,3,4));assert.deepEqual(moved.transform.localScale,vector(1,2,3));
  const world=ok(await call('set_transform',{entityId:child.entityId,space:'world',position:vector(10,11,12),rotation:vector(0,90,0)}));assert.deepEqual(world.transform.position,vector(10,11,12));
  const found=ok(await call('scene_find',{name,match:'exact',componentType:'Transform'}));assert.equal(found.total,1);assert.equal(found.nodes[0].entityId,parent.entityId);
  const duplicate=ok(await call('create_gameobject',{name:'Child',parentId:parent.entityId,position:vector(),rotation:vector(),scale:vector(1,1,1)}));
  const duplicates=ok(await call('scene_find',{name:'Child',match:'exact',rootId:parent.entityId}));assert.equal(duplicates.total,2);assert.notEqual(duplicate.entityId,child.entityId);
  assert.equal((await call('set_parent',{entityId:parent.entityId,parentId:child.entityId,worldPositionStays:false})).success,false);
  const reparented=ok(await call('set_parent',{entityId:child.entityId,parentId:'Generated',worldPositionStays:true}));assert.equal(reparented.parentId,first.generatedRootId);assert.deepEqual(reparented.transform.position,vector(10,11,12));
  assert.equal((await call('scene_hierarchy',{offset:2,limit:2,expectedRevision:first.revision})).success,false);
  for(const parentId of [null,'null','']){const response=await client.callTool({name:'create_gameobject',arguments:{name:'Invalid',parentId,position:vector(),rotation:vector(),scale:vector(1,1,1)}});assert.equal(response.isError,true);}
  const invalidParent=await call('create_gameobject',{name:'Invalid',parentId:first.scenes[0].sceneId,position:vector(),rotation:vector(),scale:vector(1,1,1)});assert.equal(invalidParent.success,false);
  const primitives={cube:{size:vector(1,1,1),collider:'UnityEngine.BoxCollider'},sphere:{size:vector(1,1,1),collider:'UnityEngine.SphereCollider'},
    capsule:{size:vector(1,2,1),collider:'UnityEngine.CapsuleCollider'},cylinder:{size:vector(1,2,1),collider:'UnityEngine.CapsuleCollider'},
    plane:{size:vector(10,0,10),collider:'UnityEngine.MeshCollider'},quad:{size:vector(1,1,0),collider:'UnityEngine.MeshCollider'}};
  for(const [primitiveType,expected] of Object.entries(primitives)){
    const primitive=ok(await call('create_primitive',{primitiveType,name:'Primitive_'+primitiveType,parentId:parent.entityId,position:vector(.1,.2,.3),rotation:vector(0,45,0),scale:vector(.5,1,2)}));
    assert.equal(primitive.primitiveType,primitiveType);assert.equal(primitive.parentId,parent.entityId);
    assert.deepEqual(primitive.transform.localPosition,vector(.1,.2,.3));assert.deepEqual(primitive.transform.localScale,vector(.5,1,2));
    for(const axis of ['x','y','z'])assert.ok(Math.abs(primitive.geometry.meshBoundsLocal.size[axis]-expected.size[axis])<.0001,primitiveType+' '+axis);
    assert.ok(primitive.components.some(c=>c.type==='UnityEngine.MeshRenderer'));assert.ok(primitive.components.some(c=>c.type===expected.collider));
    assert.equal(primitive.components.some(c=>c.type==='UnityEngine.Rigidbody'),false);
    const changed=ok(await call('set_transform',{entityId:primitive.entityId,space:'local',scale:vector(2,3,4)}));
    assert.deepEqual(changed.transform.localScale,vector(2,3,4));assert.deepEqual(changed.components,primitive.components);
    const read=ok(await call('scene_get',{entityId:primitive.entityId}));assert.deepEqual(read.geometry,primitive.geometry);
    const foundPrimitive=ok(await call('scene_find',{rootId:parent.entityId,name:'Primitive_'+primitiveType,match:'exact',componentType:'MeshRenderer'}));assert.equal(foundPrimitive.total,1);
  }
  const beforeInvalid=ok(await call('scene_hierarchy',{rootId:parent.entityId})).total;
  for(const invalid of [{primitiveType:'torus'},{parentId:null},{parentId:first.scenes[0].sceneId},{position:vector(Number.MAX_VALUE,0,0)}]){
    const result=await client.callTool({name:'create_primitive',arguments:{primitiveType:'cube',name:'InvalidPrimitive',parentId:parent.entityId,position:vector(),rotation:vector(),scale:vector(1,1,1),...invalid}});
    assert.ok(result.isError||!JSON.parse(result.content[0].text).success);
  }
  assert.equal(ok(await call('scene_hierarchy',{rootId:parent.entityId})).total,beforeInvalid);
  console.log(JSON.stringify({success:true,checks:['paged hierarchy','runtime GUIDs','component GUIDs','create parent/child','duplicate names','local partial transform','world transform','reparent preserve world','reject cycle','reject stale revision','reject null and scene parents','six native primitives','native mesh dimensions and colliders','shared IDs/queries/transform','invalid primitive creates nothing'],runtimeId:first.runtimeId,createdParent:parent.entityId,createdChild:child.entityId},null,2));
} finally {await client.close();await transport.close();}
