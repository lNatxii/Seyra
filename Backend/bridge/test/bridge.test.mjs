import assert from 'node:assert/strict';
import test from 'node:test';
import { once } from 'node:events';
import { createServer } from 'node:net';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
import { WebSocket } from 'ws';
import { tools } from '../dist/tools.js';
import { readFile } from 'node:fs/promises';

async function freePort() {
  const server=createServer();server.listen(0,'127.0.0.1');await once(server,'listening');
  const port=server.address().port;await new Promise(r=>server.close(r));return port;
}
const outcome=result=>JSON.parse(result.content[0].text);
test('schema, agent permissions and parent contract', async()=>{
  const config=JSON.parse(await readFile(new URL('../../opencode.json',import.meta.url),'utf8'));
  assert.deepEqual(Object.keys(config.agents),['seyra','seyra_glm','seyra_deepseek','seyra_zai']);
  assert.equal(config.model,'meta/muse-spark-1.3');
  const {model:glmModel,...glm}=config.agents.seyra_glm;
  assert.equal(glmModel,'helmcode/glm5.3-flash');assert.deepEqual(glm,config.agents.seyra);
  const {model,...deepseek}=config.agents.seyra_deepseek;
  assert.equal(model,'opencode/deepseek-v4.1-flash-free');assert.deepEqual(deepseek,config.agents.seyra);
  const {model:zaiModel,...zai}=config.agents.seyra_zai;
  assert.equal(zaiModel,'zai-coding-plan/glm-5.3-flash');assert.deepEqual(zai,config.agents.seyra);
  const permissions=config.agents.seyra.permissions;
  assert.deepEqual(permissions,[
    {action:'*',resource:'*',effect:'deny'},
    {action:'seyra_*',resource:'*',effect:'allow'},
    {action:'websearch',resource:'*',effect:'allow'},
    {action:'webfetch',resource:'*',effect:'allow'},
    {action:'read',resource:'*',effect:'allow'},
    {action:'grep',resource:'*',effect:'allow'},
    {action:'read',resource:'*.env',effect:'deny'},
    {action:'read',resource:'*.env.*',effect:'deny'},
    {action:'grep',resource:'*.env',effect:'deny'},
    {action:'grep',resource:'*.env.*',effect:'deny'},
    {action:'external_directory',resource:'{env:USERPROFILE}/.local/share/opencode/tool-output/*',effect:'allow'}
  ]);
  assert.deepEqual(permissions[0],{action:'*',resource:'*',effect:'deny'});
  assert.equal(permissions.some(rule=>rule.effect==='ask'),false);
  const schema=tools.find(t=>t.name==='create_gameobject').schema;
  const args={name:'Box',parentId:'Generated',position:{x:0,y:0,z:0},rotation:{x:0,y:0,z:0},scale:{x:1,y:1,z:1}};
  for(const invalid of [null,'null','NULL','', '   '])assert.equal(schema.safeParse({...args,parentId:invalid}).success,false);
  assert.equal(schema.safeParse(args).success,true);
  const rename=tools.find(t=>t.name==='rename_object').schema;
  for(const name of ['', '   ', null, 'x'.repeat(257)])assert.equal(rename.safeParse({entityId:'go_test',name}).success,false);
  assert.equal(rename.parse({entityId:'go_test',name:' Renamed '}).name,'Renamed');
  assert.equal(tools.find(t=>t.name==='set_transform').schema.safeParse({entityId:'Generated',space:'local'}).success,false);
  assert.equal(schema.safeParse({...args,position:{x:Infinity,y:0,z:0}}).success,false);
  const primitive=tools.find(t=>t.name==='create_primitive').schema;
  for(const primitiveType of ['cube','sphere','capsule','cylinder','plane','quad'])
    assert.equal(primitive.safeParse({...args,primitiveType}).success,true);
  for(const primitiveType of ['Cube','torus',0,null])
    assert.equal(primitive.safeParse({...args,primitiveType}).success,false);
  for(const parentId of [null,'null',''])assert.equal(primitive.safeParse({...args,primitiveType:'cube',parentId}).success,false);
  assert.equal(primitive.safeParse({...args,primitiveType:'cube',size:2}).success,false);
  for(const name of ['content_save','content_load']){
    const slot=tools.find(t=>t.name===name).schema;
    for(const invalid of ['../escape','C:/outside','a/b','',null])assert.equal(slot.safeParse({slot:invalid}).success,false);
    assert.equal(slot.safeParse({slot:'test_01'}).success,true);
  }
  assert.equal(tools.find(t=>t.name==='undo').schema.safeParse({scope:'all'}).success,false);
  assert.equal(tools.find(t=>t.name==='undo').schema.safeParse({scope:'request',expectedOperationId:'op_'+'a'.repeat(32)}).success,true);
});

test('MCP transport confirms, rejects invalid arguments, times out without retry, and marks disconnect unknown',async()=>{
  const port=await freePort();
  const transport=new StdioClientTransport({command:process.execPath,args:['dist/index.js'],cwd:new URL('..',import.meta.url).pathname.replace(/^\/(.:)/,'$1'),env:{...process.env,SEYRA_UNITY_PORT:String(port),SEYRA_UNITY_TIMEOUT_MS:'250'},stderr:'pipe'});
  const client=new Client({name:'bridge-test',version:'1.0.0'});
  let socket;
  try {
    await client.connect(transport);
    const list=await client.listTools();assert.deepEqual(list.tools.map(t=>t.name).sort(),tools.map(t=>t.name).sort());
    assert.equal(outcome(await client.callTool({name:'scene_hierarchy',arguments:{}})).error.code,'UNITY_NOT_CONNECTED');
    socket=new WebSocket(`ws://127.0.0.1:${port}`);await once(socket,'open');
    let requests=0;let mode='confirm';
    socket.on('message',bytes=>{
      requests++;const request=JSON.parse(bytes);assert.ok(Date.parse(request.deadlineUtc)>0);
      if(mode==='confirm')socket.send(JSON.stringify({version:1,type:'tool_result',requestId:request.requestId,success:true,data:{runtimeId:'mock',nodes:[]}}));
      else if(mode==='rollback')socket.send(JSON.stringify({version:1,type:'tool_result',requestId:request.requestId,success:false,error:{code:'ROLLBACK_FAILED',message:'State unknown'}}));
      else if(mode==='disconnect')socket.close();
    });
    const result=outcome(await client.callTool({name:'scene_hierarchy',arguments:{}}));assert.equal(result.success,true);assert.equal(result.data.runtimeId,'mock');
    const before=requests;
    const invalid=await client.callTool({name:'create_gameobject',arguments:{parentId:null}});assert.equal(invalid.isError,true);assert.equal(requests,before);
    mode='timeout';
    const timed=outcome(await client.callTool({name:'set_transform',arguments:{entityId:'Generated',space:'local',position:{x:1,y:2,z:3}}}));
    assert.equal(timed.error.code,'UNITY_TIMEOUT');assert.equal(timed.outcomeUnknown,true);assert.equal(requests,before+1);
    mode='rollback';
    const rollback=outcome(await client.callTool({name:'delete_object',arguments:{entityId:'go_test'}}));
    assert.equal(rollback.error.code,'ROLLBACK_FAILED');assert.equal(rollback.outcomeUnknown,true);
    mode='disconnect';
    const disconnected=outcome(await client.callTool({name:'scene_hierarchy',arguments:{}}));assert.equal(disconnected.error.code,'UNITY_DISCONNECTED');assert.equal(disconnected.outcomeUnknown,true);
  } finally {socket?.terminate();await client.close();await transport.close();}
});

test('internal token rejects unauthorized runtime and replacement settles pending calls', async () => {
  const port = await freePort();
  const transport = new StdioClientTransport({command:process.execPath,args:['dist/index.js'],cwd:new URL('..',import.meta.url).pathname.replace(/^\/(.:)/,'$1'),env:{...process.env,SEYRA_UNITY_PORT:String(port),SEYRA_BRIDGE_INTERNAL_TOKEN:'test-internal-token'},stderr:'pipe'});
  const client = new Client({name:'replacement-test',version:'1.0'});
  let first, second;
  try {
    await client.connect(transport);
    const rejected = new WebSocket(`ws://127.0.0.1:${port}`);
    await once(rejected, 'close');
    assert.equal(outcome(await client.callTool({name:'scene_hierarchy',arguments:{}})).error.code,'UNITY_NOT_CONNECTED');
    first = new WebSocket(`ws://127.0.0.1:${port}`, {headers:{Authorization:'Bearer test-internal-token'}});
    await once(first,'open');
    const received = once(first,'message');
    const pendingCall = client.callTool({name:'scene_hierarchy',arguments:{}});
    await received;
    second = new WebSocket(`ws://127.0.0.1:${port}`, {headers:{Authorization:'Bearer test-internal-token'}});
    await once(second,'open');
    const result = outcome(await pendingCall);
    assert.equal(result.error.code,'UNITY_DISCONNECTED'); assert.equal(result.outcomeUnknown,true);
  } finally {first?.terminate();second?.terminate();await client.close();await transport.close();}
});
