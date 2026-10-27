import http from 'node:http';

const sessions = new Map();
const events = [];
let sequence = 0, blockReads = false, missing = false;
const stamp = () => Date.now();
const send = (res, data, status = 200) => { res.writeHead(status, {'Content-Type':'application/json'}); res.end(JSON.stringify(data)); };
function finish(session, outcome = 'succeeded') {
  if (session.stopped) return;
  session.stopped = true;
  session.permission = null;
  session.outcome = outcome;
  session.idle = stamp();
  const assistant = session.messages.findLast(m => m.type === 'assistant');
  if (outcome === 'succeeded') {
    session.mutations++;
    const tool = assistant.content.find(p => p.type === 'tool');
    tool.state = { status:'completed', content:[{type:'text',text:JSON.stringify(session.mode === 'unknown_tool'
      ? {success:false,outcomeUnknown:true,error:{code:'UNITY_TIMEOUT',message:'Fixture unknown outcome'}}
      : {success:true,data:{name:'FixtureCube',deleted:false}})}] };
    assistant.content.push({type:'text',text:'Fixture final answer'});
    session.messages.push({id:'idle-'+(++sequence),type:'idle',outcome});
  } else if (outcome === 'failed') assistant.error = {type:'provider',message:'Fixture provider failure'};
  else assistant.content.find(p => p.type === 'tool').state = {status:'error',error:{type:'aborted',message:'Tool execution interrupted'}};
  events.push({type:'terminal',session:session.id,outcome});
}
function admit(session, text) {
  session.mode = text;
  session.stopped = false;
  session.admitted = stamp();
  const user = {id:'user-'+(++sequence),type:'user',text,time:{created:session.admitted}};
  session.messages.push(user, {id:'assistant-'+(++sequence),type:'assistant',content:[
    {type:'reasoning',text:'INTERNAL_REASONING_MUST_NOT_RENDER'},
    {type:'tool',id:'tool-'+sequence,name:'seyra_create_primitive',state:{status:'running',input:{private:'RAW_INPUT_MUST_NOT_RENDER'}}}
  ]});
  if (text.includes('permission')) session.permission = {id:'permission-'+sequence,sessionID:session.id,action:'test_action',resources:['fixture'],metadata:{description:'Literal <b>permission</b>'}};
  if (['complete','drop_prompt','unknown_tool','fail'].includes(text)) setTimeout(() => finish(session,text === 'fail'?'failed':'succeeded'),600);
  events.push({type:'prompt',session:session.id,text});
  return user;
}
const server = http.createServer(async (req,res) => {
  let body='';for await(const chunk of req)body+=chunk;
  const url=new URL(req.url,'http://localhost');
  const data=body?JSON.parse(body):{};
  if(url.pathname==='/test/state') return send(res,{sessions:[...sessions.values()].map(s=>({id:s.id,prompts:s.messages.filter(m=>m.type==='user').length,interrupts:s.interrupts,mutations:s.mutations,outcome:s.outcome})),events});
  if(url.pathname==='/test/control') {
    if('blockReads' in data)blockReads=data.blockReads;
    if('missing' in data)missing=data.missing;
    if(data.finish)finish(sessions.get(data.session));
    return send(res,{ok:true});
  }
  if(url.pathname==='/api/agent')return send(res,{data:[{id:'seyra'},{id:'seyra_deepseek',model:{id:'fixture',providerID:'test'}}]});
  if(url.pathname==='/api/session'&&req.method==='POST') {
    const id='fixture-'+(++sequence);
    sessions.set(id,{id,messages:[],interrupts:0,mutations:0,stopped:true});
    events.push({type:'session',id,model:data.model});
    return send(res,{data:{id}});
  }
  const match=url.pathname.match(/^\/api\/session\/([^/]+)(.*)$/);
  const session=match&&sessions.get(match[1]);
  if(!session||missing)return send(res,{error:'Fixture missing session'},404);
  const tail=match[2];
  if(req.method==='GET'&&blockReads)return send(res,{error:'Fixture read outage'},503);
  if(!tail)return send(res,{data:{id:session.id,outcome:session.outcome,time:{created:1,updated:session.admitted,idle:session.idle}}});
  if(tail==='/message')return send(res,{data:[...session.messages].reverse(),cursor:{next:null}});
  if(tail==='/permission')return send(res,{data:session.permission?[session.permission]:[]});
  if(tail.endsWith('/reply')) {
    events.push({type:'decision',session:session.id,decision:data.decision});
    if(session.mode==='lost_permission')return res.destroy();
    if(data.decision==='once')finish(session);else finish(session,'interrupted');
    return send(res,{data:true});
  }
  if(tail==='/prompt') {
    if(data.text==='no_admission') {events.push({type:'unconfirmed-prompt',session:session.id});return res.destroy();}
    if(data.text==='slow_no_admission') {events.push({type:'unconfirmed-prompt',session:session.id});return setTimeout(()=>res.destroy(),900);}
    const user=admit(session,data.text);
    if(data.text==='drop_prompt')return res.destroy();
    if(data.text==='cancel_during_admission')return setTimeout(()=>send(res,{data:user}),900);
    return send(res,{data:user});
  }
  if(tail==='/interrupt') {
    session.interrupts++;
    if(session.mode==='interrupt_not_received'&&!session.stopFailed) { session.stopFailed=true;return send(res,{error:'Fixture interrupt unavailable'},503); }
    if(session.mode==='cancel_race_complete')finish(session);
    const interrupted=!session.stopped;
    if(interrupted)finish(session,'interrupted');
    events.push({type:'interrupt',session:session.id,interrupted});
    if(session.mode==='drop_interrupt')return res.destroy();
    return send(res,{interrupted});
  }
  return send(res,{error:'Unknown fixture route'},404);
});
server.listen(4202,'127.0.0.1',()=>console.log('Chat execution fixture listening on 4202'));
