import http from 'node:http';
let round=0, decision=null, replies=[];
const server=http.createServer(async(req,res)=>{
 let body='';for await(const chunk of req)body+=chunk;
 const url=new URL(req.url,'http://localhost');let data;
 if(url.pathname==='/test/state'){data={round,decision,replies};}
 else if(req.method==='POST'&&url.pathname==='/api/session'){data={data:{id:'permission-ui-test'}};}
 else if(url.pathname.endsWith('/prompt')){round++;decision=null;data={data:{id:'user-'+round}};}
 else if(url.pathname.endsWith('/reply')){const value=JSON.parse(body).decision;replies.push(value);decision=value;data={data:true};}
 else if(url.pathname.endsWith('/permission')){data={data:round&&!decision?[{id:'permission-'+round,sessionID:'permission-ui-test',action:'test_action',resources:['test-resource'],metadata:{description:'Test request <b>literal</b>',command:'No actual tool is executed.'}}]:[]};}
 else if(url.pathname.endsWith('/message')){data={data:decision?[{id:'idle-'+round,type:'idle',outcome:'succeeded'},{id:'answer-'+round,type:'assistant',content:[{type:'text',text:'Decision received: '+decision}]},{id:'user-'+round,type:'user'}]:[{id:'user-'+round,type:'user'}]};}
 else data={data:{id:'permission-ui-test'}};
 res.writeHead(200,{'Content-Type':'application/json'});res.end(JSON.stringify(data));
});server.listen(4199,'127.0.0.1');
