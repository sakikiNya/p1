import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';
import {dirname,resolve} from 'node:path';
import {spawn,execFileSync} from 'node:child_process';
import {once} from 'node:events';
import {createServer} from 'node:net';
import {mkdir,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
const root=resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require=createRequire(root+'/frontend/package.json');
const {default:puppeteer}=await import(require.resolve('puppeteer-core'));
const apiPort=process.env.SMOKE_API_PORT || '18080';
const webPort=process.env.SMOKE_WEB_PORT || '15175';
const apiBase=`http://127.0.0.1:${apiPort}`;
const webBase=`http://127.0.0.1:${webPort}`;
for (const port of [apiPort,webPort]) {
 await new Promise((resolve,reject)=>{const server=createServer();server.once('error',()=>reject(new Error(`测试端口 ${port} 已占用，请调整 SMOKE_API_PORT/SMOKE_WEB_PORT`)));server.listen(Number(port),'127.0.0.1',()=>server.close(resolve))});
}
if(!process.env.CHROME_PATH)throw new Error('请设置 CHROME_PATH 为本机 Chrome/Chromium 完整路径');
execFileSync('go',['build','-o',root+'/.cache/inspection','.'],{cwd:root+'/backend',env:{...process.env,GOCACHE:root+'/.cache/go-build'},stdio:'inherit'});
const vite=spawn(process.execPath,[root+'/frontend/node_modules/vite/bin/vite.js','--host','127.0.0.1','--port',webPort,'--strictPort'],{cwd:root+'/frontend',env:{...process.env,DEV_API_TARGET:apiBase},stdio:'pipe'});
let viteExited=false;vite.on('exit',()=>viteExited=true);
for(let i=0;i<60;i++){if(viteExited)throw new Error('Vite 启动失败，检查测试网页端口是否已占用');try{if((await fetch(webBase)).ok)break}catch{}await new Promise(r=>setTimeout(r,100))}
const env={...process.env,LISTEN_ADDR:`127.0.0.1:${apiPort}`,DATA_FILE:root+'/.cache/browser/issues.json',CORS_ORIGINS:webBase};
await mkdir(root+'/.cache/browser',{recursive:true});await writeFile(env.DATA_FILE,'[]');
let backend;
async function start(){backend=spawn(root+'/.cache/inspection',[],{env,stdio:'pipe'});for(let i=0;i<40;i++){try{if((await fetch(apiBase+'/health')).ok)return}catch{}await new Promise(r=>setTimeout(r,100))}throw new Error('backend not started')}
async function stop(){const child=backend;if(child&&!child.killed){child.kill('SIGTERM');await new Promise(r=>child.once('exit',r))}}
let browser;
try{
 await start();
 browser=await puppeteer.launch({executablePath:process.env.CHROME_PATH,headless:true,args:['--no-sandbox','--disable-dev-shm-usage'],env:{...process.env,XDG_CONFIG_HOME:root+'/.cache/browser-config',XDG_CACHE_HOME:root+'/.cache/browser-cache'}});
 const page=await browser.newPage();await page.setViewport({width:1440,height:1100});const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(webBase);await page.waitForFunction(()=>document.body.innerText.includes('还没有上报的问题'));console.log('PASS empty list');
 const response=await fetch(apiBase+'/issues',{method:'POST',body:JSON.stringify({title:'入口墙面破损',description:'左侧墙体存在裂缝，需要检查修补。',priority:'high',position:{x:.42,y:.03,z:1.26}})});assert.equal(response.status,201);const issue=await response.json();
 const clickText=async text=>{await page.evaluate(text=>{const b=[...document.querySelectorAll('button')].find(b=>b.textContent===text);if(!b)throw new Error(text);b.click()},text)};
 await clickText('刷新列表');await page.waitForSelector('.issue');assert.match(await page.$eval('.issue',el=>el.innerText),/入口墙面破损/);console.log('PASS list loads real issue');
 await page.select('select','in_progress');await clickText('保存状态');await page.waitForFunction(()=>document.querySelector('.issue .status')?.textContent==='处理中');
 await page.select('select','resolved');await clickText('保存状态');await page.waitForFunction(()=>document.querySelector('.issue .status')?.textContent==='已解决');console.log('PASS status transitions');
 await stop();await start();await page.reload();await page.waitForFunction(()=>document.querySelector('.issue .status')?.textContent==='已解决');
 assert.equal((await (await fetch(apiBase+'/issues')).json())[0].id,issue.id);console.log('PASS process restart persistence');
 await page.screenshot({path:root+'/.cache/browser/desktop.png',fullPage:true});
 await stop();await page.select('select','open');await clickText('保存状态');await page.waitForSelector('.issue .error');assert.equal(await page.$eval('select',el=>el.value),'resolved');console.log('PASS failed save retains saved status');
 await clickText('刷新列表');await page.waitForSelector('.notice');assert.ok(await page.$('.issue'));console.log('PASS outage error and retained list');
 await page.screenshot({path:root+'/.cache/browser/error.png',fullPage:true});
 await start();await clickText('重试');await page.waitForFunction(()=>!document.querySelector('.notice'));console.log('PASS recovery');
 await page.setViewport({width:390,height:844});await page.screenshot({path:root+'/.cache/browser/mobile.png',fullPage:true});
 assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));assert.deepEqual(errors,[]);console.log('PASS mobile layout and no uncaught errors');
 // Direct browser CORS, rather than the same-origin Vite proxy.
 const cors=await page.evaluate(async(apiBase)=>{const r=await fetch(apiBase+'/issues');const issues=await r.json();const p=await fetch(`${apiBase}/issues/${issues[0].id}/status`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({status:'open'})});return p.status},apiBase);assert.equal(cors,200);console.log('PASS browser CORS PATCH preflight');
}finally{if(browser)await browser.close();await stop();if(!viteExited){vite.kill('SIGTERM');await once(vite,'exit')}}
