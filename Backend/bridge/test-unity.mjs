import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
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
    const bounds=ok(await call('scene_bounds',{entityId:primitive.entityId})).bounds;
    assert.equal(bounds.space,'world');assert.equal(bounds.kind,'aabb');
    const reference=ok(await call('create_primitive',{primitiveType:'cube',name:'PlacementReference',parentId:'Generated',position:vector(-2,1,-3),rotation:vector(10,20,30),scale:vector(1,2,3)}));
    const placed=ok(await call('place_relative',{entityId:primitive.entityId,referenceId:reference.entityId,direction:'above',gap:.2}));
    assert.ok(Math.abs(placed.bounds.min.y-placed.referenceBounds.max.y-.2)<.0001);
    assert.ok(Math.abs(placed.bounds.center.x-placed.referenceBounds.center.x)<.0001);
    ok(await call('undo',{scope:'operation',expectedOperationId:placed.operationId}));
    assert.deepEqual(ok(await call('scene_get',{entityId:primitive.entityId})).transform,read.transform);
    const foundPrimitive=ok(await call('scene_find',{rootId:parent.entityId,name:'Primitive_'+primitiveType,match:'exact',componentType:'MeshRenderer'}));assert.equal(foundPrimitive.total,1);
  }
  const beforeInvalid=ok(await call('scene_hierarchy',{rootId:parent.entityId})).total;
  for(const invalid of [{primitiveType:'torus'},{parentId:null},{parentId:first.scenes[0].sceneId},{position:vector(Number.MAX_VALUE,0,0)}]){
    const result=await client.callTool({name:'create_primitive',arguments:{primitiveType:'cube',name:'InvalidPrimitive',parentId:parent.entityId,position:vector(),rotation:vector(),scale:vector(1,1,1),...invalid}});
    assert.ok(result.isError||!JSON.parse(result.content[0].text).success);
  }
  assert.equal(ok(await call('scene_hierarchy',{rootId:parent.entityId})).total,beforeInvalid);
  const panelData={version:1,kind:'panel',title:'MCP panel',text:'  <b>Literal text</b>\nSecond line  ',width:.8,height:.6,fontSize:24};
  const panel=ok(await call('create_panel',{name:'IntegrationPanel',parentId:parent.entityId,position:vector(0,1,2),rotation:vector(),scale:vector(1,1,1),panel:panelData}));
  assert.deepEqual(panel.panel,panelData);assert.equal(panel.panelIntact,true);
  const updatedPanel=ok(await call('update_panel',{entityId:panel.entityId,panel:{...panelData,text:'Updated panel',width:1,fontSize:28}}));
  assert.equal(updatedPanel.panel.text,'Updated panel');assert.equal(updatedPanel.entityId,panel.entityId);
  ok(await call('undo',{scope:'operation',expectedOperationId:updatedPanel.operationId}));
  assert.deepEqual(ok(await call('scene_get',{entityId:panel.entityId})).panel,panelData);
  const labelData={...panelData,kind:'label',title:'',height:.08};
  const label=ok(await call('create_panel',{name:'IntegrationLabel',parentId:parent.entityId,position:vector(1,1,2),rotation:vector(),scale:vector(1,1,1),panel:labelData}));
  assert.deepEqual(ok(await call('scene_get',{entityId:label.entityId})).panel,labelData);
  assert.equal(ok(await call('scene_bounds',{entityId:panel.entityId})).bounds.kind,'aabb');
  const examples=JSON.parse(readFileSync('test/presentation-corpus.json','utf8')).filter(x=>x.valid);
  for(const example of examples){
    const shown=ok(await call('ui_present',{name:example.name,parentId:parent.entityId,position:vector(0,1,2),rotation:vector(),scale:vector(1,1,1),width:.8,height:.6,presentation:example.content}));
    assert.equal(shown.panelIntact,true);
    assert.deepEqual(ok(await call('ui_get',{entityId:shown.entityId})).presentation,shown.presentation);
    ok(await call('ui_close',{entityId:shown.entityId}));
    assert.equal(ok(await call('ui_get',{entityId:shown.entityId})).interaction.visible,false);
    ok(await call('ui_open',{entityId:shown.entityId}));
  }
  assert.equal(ok(await call('ui_list')).presentations.length,12);
  const request=ok(await call('request_begin',{name:'GroupedRequest'}));
  const grouped=ok(await call('create_primitive',{primitiveType:'sphere',name:'GroupedSphere',parentId:'Generated',position:vector(1,2,3),rotation:vector(),scale:vector(1,1,1)}));
  assert.equal(grouped.parentId,request.entityId);assert.equal(grouped.requestGroupId,request.entityId);
  const componentIds=grouped.components;
  const appearance={style:'colored_translucent',color:{r:0.2,g:0.4,b:0.8,a:0.35}};
  const styled=ok(await call('set_appearance',{entityId:grouped.entityId,...appearance}));
  assert.equal(styled.appearance.style,appearance.style);
  assert.ok(Math.abs(ok(await call('scene_get',{entityId:grouped.entityId})).appearance.color.a-0.35)<1e-6);
  const rejected=await client.callTool({name:'set_appearance',arguments:{entityId:grouped.entityId,style:'plastic',color:{r:1,g:1,b:1,a:0.4}}});
  assert.equal(rejected.isError,true);
  const edited=ok(await call('set_transform',{entityId:grouped.entityId,space:'local',position:vector(3,2,1)}));
  ok(await call('request_end',{requestGroupId:request.entityId}));
  ok(await call('content_save',{slot:'integration'}));
  assert.equal(ok(await call('content_list')).slots.length,1);
  const deleted=ok(await call('delete_object',{entityId:request.entityId}));assert.equal(deleted.deleted,true);
  assert.equal(ok(await call('scene_find',{rootId:'Generated',name:'GroupedSphere',match:'exact'})).total,0);
  assert.equal(ok(await call('scene_get',{entityId:grouped.entityId})).deleted,true);
  ok(await call('restore_object',{entityId:request.entityId}));
  assert.deepEqual(ok(await call('scene_get',{entityId:grouped.entityId})).components,componentIds);
  const loaded=ok(await call('content_load',{slot:'integration'}));assert.ok(loaded.nodeCount>6);
  assert.equal(ok(await call('scene_get',{entityId:grouped.entityId})).parentId,request.entityId);
  assert.deepEqual(ok(await call('scene_get',{entityId:grouped.entityId})).appearance,styled.appearance);
  assert.deepEqual(ok(await call('scene_get',{entityId:panel.entityId})).panel,panelData);
  assert.deepEqual(ok(await call('scene_get',{entityId:label.entityId})).panel,labelData);
  ok(await call('undo',{scope:'operation',expectedOperationId:loaded.operationId}));
  assert.deepEqual(ok(await call('scene_get',{entityId:grouped.entityId})).components,componentIds);
  ok(await call('undo',{scope:'operation'})); // undo restore: back to trash
  ok(await call('undo',{scope:'operation'})); // undo delete: live again
  ok(await call('undo',{scope:'operation',expectedOperationId:edited.operationId}));
  assert.deepEqual(ok(await call('scene_get',{entityId:grouped.entityId})).transform.localPosition,vector(1,2,3));
  ok(await call('undo',{scope:'request'}));
  assert.equal(ok(await call('scene_find',{rootId:'Generated',name:'GroupedSphere',match:'exact'})).total,0);
  console.log(JSON.stringify({success:true,checks:['paged hierarchy','runtime GUIDs','component GUIDs','create parent/child','duplicate names','local partial transform','world transform','reparent preserve world','reject cycle','reject stale revision','reject null and scene parents','six native primitives','native mesh dimensions and colliders','shared IDs/queries/transform','invalid primitive creates nothing','request containers','soft delete and restore','persistent IDs and hierarchy','undo load with original component IDs','operation and request undo','appearance through MCP, RGBA rejection and persistence','atomic panels and labels through MCP, update/undo and persistence v4','twelve presentations through MCP and visibility'],runtimeId:first.runtimeId,createdParent:parent.entityId,createdChild:child.entityId},null,2));
} finally {await client.close();await transport.close();}
