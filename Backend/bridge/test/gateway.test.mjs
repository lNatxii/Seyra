import assert from 'node:assert/strict';
import test from 'node:test';
import { once } from 'node:events';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import http from 'node:http';
import https from 'node:https';
import { WebSocketServer } from 'ws';
import { createGateway } from '../dist/gateway.js';

test('gateway authenticates, restricts proxy routes and relays polling results', async () => {
  const root = mkdtempSync(join(tmpdir(), 'seyra-gateway-'));
  execFileSync('pwsh', ['-NoProfile', '-Command', `
    $key=[Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
    $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=seyra-test',$key,[Security.Cryptography.HashAlgorithmName]::SHA256)
    $cert=$request.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1),[DateTimeOffset]::Now.AddDays(1))
    [IO.File]::WriteAllBytes((Join-Path $env:SEYRA_TEST_CERT_ROOT 'test.pfx'),$cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,'fixture'))
    $cert.Dispose();$key.Dispose()
  `], { env:{...process.env,SEYRA_TEST_CERT_ROOT:root} });
  const tokenHash = value => createHash('sha256').update(value).digest('hex');
  const devicesFile = join(root,'devices.json');
  writeFileSync(devicesFile,JSON.stringify([{id:'valid',tokenSha256:tokenHash('device-test'),revoked:false},{id:'revoked',tokenSha256:tokenHash('revoked-test'),revoked:true}]));
  const upstream = http.createServer((request,response) => {
    assert.equal(request.headers.authorization,`Basic ${Buffer.from('opencode:password-test').toString('base64')}`);
    assert.equal(request.headers.cookie,undefined);assert.equal(request.headers.origin,undefined);
    response.setHeader('Content-Type','application/json');response.end('{"data":[]}');
  });
  upstream.listen(0,'127.0.0.1');await once(upstream,'listening');
  const bridge = new WebSocketServer({host:'127.0.0.1',port:0});await once(bridge,'listening');
  let runtime;
  bridge.on('connection',(socket,request) => { assert.equal(request.headers.authorization,'Bearer internal-test');runtime=socket; });
  const gateway = createGateway({pfx:readFileSync(join(root,'test.pfx')),passphrase:'fixture',devicesFile,password:'password-test',internalToken:'internal-test',openCodePort:upstream.address().port,bridgePort:bridge.address().port});
  gateway.listen(0,'127.0.0.1');await once(gateway,'listening');
  function request(path, token = 'device-test', method = 'GET', body) {
    return new Promise((resolve,reject) => {
      const req=https.request({host:'127.0.0.1',port:gateway.address().port,path,method,rejectUnauthorized:false,headers:{...(token ? {Authorization:`Bearer ${token}`} : {}),Cookie:'discard',Origin:'discard',Connection:'close'}},response=>{
        let text='';response.on('data',part=>text+=part);response.on('end',()=>resolve({status:response.statusCode,body:text ? JSON.parse(text) : null}));
      });req.on('error',reject);req.end(body ? JSON.stringify(body) : undefined);
    });
  }
  try {
    assert.equal((await request('/api/agent',null)).status,401);
    assert.equal((await request('/api/agent','revoked-test')).status,401);
    assert.equal((await request('/api/config')).status,404);
    assert.equal((await request('/api/agent')).status,200);
    const hello=await request('/_seyra/bridge/hello','device-test','POST',{});assert.equal(hello.status,200);
    const id=hello.body.sessionId;const requestId=randomUUID();
    const poll=request(`/_seyra/bridge/next?sessionId=${id}&wait=25`);
    runtime.send(JSON.stringify({version:1,type:'tool_request',requestId,tool:'scene_hierarchy',arguments:{}}));
    const command=await poll;assert.equal(command.body.requestId,requestId);
    const received=once(runtime,'message');
    assert.equal((await request(`/_seyra/bridge/result?sessionId=${id}`,'device-test','POST',{version:1,type:'tool_result',requestId,success:true,data:{nodes:[]}})).status,200);
    assert.equal(JSON.parse((await received)[0]).requestId,requestId);
  } finally {
    gateway.closeAllConnections();await new Promise(resolve=>gateway.close(resolve));
    for(const socket of bridge.clients)socket.terminate();await new Promise(resolve=>bridge.close(resolve));
    await new Promise(resolve=>upstream.close(resolve));rmSync(root,{recursive:true,force:true});
  }
});
