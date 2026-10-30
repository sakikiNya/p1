import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';
import {dirname,resolve} from 'node:path';
import {spawn,execFileSync} from 'node:child_process';
import {once} from 'node:events';
import {createServer} from 'node:net';
import {mkdir,mkdtemp} from 'node:fs/promises';
import assert from 'node:assert/strict';

const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const require=createRequire(root+'/frontend/package.json');
const {default:puppeteer}=await import(require.resolve('puppeteer-core'));
const apiPort=process.env.SMOKE_API_PORT||'18080',webPort=process.env.SMOKE_WEB_PORT||'15175';
const apiBase=`http://127.0.0.1:${apiPort}`,webBase=`http://127.0.0.1:${webPort}`;
if(!process.env.CHROME_PATH)throw new Error('设置 CHROME_PATH 为 Chrome/Chromium 完整路径');
for(const port of [apiPort,webPort])await new Promise((resolve,reject)=>{const s=createServer();s.once('error',reject);s.listen(Number(port),'127.0.0.1',()=>s.close(resolve));});
await mkdir(root+'/.cache/browser',{recursive:true});
const data=await mkdtemp(root+'/.cache/browser/run-');
const password='Browser-Test-Password-2026!';
const env={...process.env,GOCACHE:root+'/.cache/go-build',GOMODCACHE:root+'/.cache/gomod',GOPATH:root+'/.cache/gopath',LISTEN_ADDR:`127.0.0.1:${apiPort}`,DB_FILE:data+'/inspection.db',LEGACY_JSON_FILE:'',ADMIN_PASSWORD:password,COOKIE_SECURE:'false',CORS_ORIGINS:webBase,PORT:webPort,BIND_HOST:'127.0.0.1',API_UPSTREAM:`127.0.0.1:${apiPort}`,FRONTEND_DIR:root+'/frontend/dist',XDG_CONFIG_HOME:root+'/.cache/browser-config',XDG_CACHE_HOME:root+'/.cache/browser-cache',XDG_DATA_HOME:data};
execFileSync('go',['build','-o',root+'/.cache/inspection','.'],{cwd:root+'/backend',env,stdio:'inherit'});
execFileSync('npm',['run','build'],{cwd:root+'/frontend',env,stdio:'inherit'});
let backend,proxy,browser;
async function ready(url,child){for(let i=0;i<100;i++){if(child.exitCode!==null)throw new Error('process exited');try{if((await fetch(url)).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw new Error(`not ready: ${url}`);}
async function start(){backend=spawn(root+'/.cache/inspection',[],{cwd:root,env:{...env,FRONTEND_DIR:''},stdio:'ignore'});await ready(apiBase+'/health',backend);}
async function stop(child){if(child&&child.exitCode===null&&!child.killed){child.kill('SIGTERM');await once(child,'exit');}}
try{
 await start();proxy=spawn('caddy',['run','--config',root+'/deploy/Caddyfile'],{cwd:root,env,stdio:'ignore'});await ready(webBase+'/api/health',proxy);
 browser=await puppeteer.launch({executablePath:process.env.CHROME_PATH,headless:true,args:['--no-sandbox','--disable-dev-shm-usage'],env});
 const page=await browser.newPage();await page.setViewport({width:1440,height:1100});const errors=[];page.on('pageerror',e=>errors.push(e.message));
 const click=async text=>page.evaluate(text=>{const b=[...document.querySelectorAll('button')].find(b=>b.textContent===text);if(!b)throw new Error('Missing button: '+text);b.click();},text);
 const login=async(username,pw)=>{await page.waitForSelector('input[autocomplete=username]');await page.type('input[autocomplete=username]',username);await page.type('input[autocomplete=current-password]',pw);await click('登录');await page.waitForSelector('.tabs');};
 await page.goto(webBase);await page.waitForSelector('.login-form');await page.screenshot({path:root+'/.cache/browser/login.png',fullPage:true});
 await login('Admin',password);await page.waitForFunction(()=>document.body.innerText.includes('还没有上报的问题'));console.log('PASS Caddy HTTP + Admin login + empty list');
 const created=await fetch(webBase+'/api/issues',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({title:'入口墙面破损',description:'左侧墙体存在裂缝，需要检查修补。',priority:'high',position:{x:.42,y:.03,z:1.26}})});assert.equal(created.status,201);const issue=await created.json();
 await click('刷新列表');await page.waitForSelector('.issue');
 await page.select('.issue select','in_progress');await click('保存状态');await page.waitForFunction(()=>document.querySelector('.issue .status')?.textContent==='处理中');
 await page.select('.issue select','resolved');await click('保存状态');await page.waitForFunction(()=>document.querySelector('.issue .status')?.textContent==='已解决');console.log('PASS open Unity API + status transitions');
 await page.type('input[type=search]','不存在的问题');assert.equal(await page.$('.issue'),null);await page.$eval('input[type=search]',el=>{const setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;setter.call(el,'');el.dispatchEvent(new Event('input',{bubbles:true}));});await page.waitForSelector('.issue');console.log('PASS search filters');
 await stop(backend);await start();await page.reload();await page.waitForSelector('.issue');assert.match(await page.$eval('.issue .status',el=>el.textContent),/已解决/);assert.equal((await(await fetch(apiBase+'/issues')).json())[0].id,issue.id);console.log('PASS SQLite restart + persisted login session');
 await page.screenshot({path:root+'/.cache/browser/desktop.png',fullPage:true});
 await click('用户管理');await click('加载用户列表');await page.waitForSelector('tbody tr');
 for(const role of ['user','admin']){
  await page.type('.user-form input[autocomplete=off]','Test_'+role);await page.type('.user-form input[type=password]','User-Testing-Password');await page.select('.user-form select',role);await click('添加用户');await page.waitForFunction(name=>document.querySelector('.success')?.textContent?.includes(name),{},'Test_'+role);
 }
 await page.screenshot({path:root+'/.cache/browser/users.png',fullPage:true});console.log('PASS super admin creates users and administrators');
 assert.equal((await fetch(webBase+'/api/users')).status,401);
 await click('退出登录');await login('Test_user','User-Testing-Password');await page.waitForSelector('.issue');assert.equal(await page.$('.issue button'),null);assert.equal(await page.evaluate(()=>[...document.querySelectorAll('.tabs button')].some(b=>b.textContent==='用户管理')),false);assert.equal(await page.evaluate(async()=> (await fetch('/api/users')).status),403);console.log('PASS ordinary user read-only UI + backend account permissions');
 await click('退出登录');await login('Test_admin','User-Testing-Password');await page.waitForSelector('.issue');assert.ok(await page.$('.issue button'));console.log('PASS administrator edit UI');
 await stop(backend);await page.select('.issue select','open');await click('保存状态');await page.waitForSelector('.issue .error');assert.equal(await page.$eval('.issue select',el=>el.value),'resolved');await click('刷新列表');await page.waitForSelector('.notice');assert.ok(await page.$('.issue'));await page.screenshot({path:root+'/.cache/browser/error.png',fullPage:true});await start();await click('重试');await page.waitForFunction(()=>!document.querySelector('.notice'));console.log('PASS backend outage + retained state + recovery');
 await click('修改密码');await page.type('input[autocomplete=current-password]','User-Testing-Password');await page.type('input[autocomplete=new-password]','New-Testing-Password');await click('保存新密码');await login('Test_admin','New-Testing-Password');await page.waitForSelector('.issue');console.log('PASS password change and re-login');
 await page.setViewport({width:390,height:844});await page.screenshot({path:root+'/.cache/browser/mobile.png',fullPage:true});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));assert.deepEqual(errors,[]);
 const cors=await page.evaluate(async api=>{const r=await fetch(api+'/issues');const issues=await r.json();return(await fetch(`${api}/issues/${issues[0].id}/status`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({status:'open'})})).status;},apiBase);assert.equal(cors,200);console.log('PASS responsive UI + no page errors + unauthenticated CORS PATCH');
 assert.equal((await fetch(webBase+'/api/not-a-route')).status,404);assert.equal((await fetch(webBase+'/some-page')).status,200);console.log('PASS Caddy API routing and SPA fallback');
}finally{if(browser)await browser.close();await stop(proxy);await stop(backend);}
