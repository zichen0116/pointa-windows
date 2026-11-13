import http from 'node:http';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('../../', import.meta.url));
const fixtures = JSON.parse(readFileSync(path.join(root,'pointa-windows/src/Pointa.Windows/Native/ReferenceTrails.json'),'utf8'));
const output = path.join(root,'pointa-windows/artifacts/browser-reference');
mkdirSync(output,{recursive:true});
let complete;
const done = new Promise(resolve => { complete = resolve; });
const received = new Set();
const html = `<!doctype html><meta charset="utf-8"><canvas id="canvas" width="640" height="360"></canvas>
<script type="importmap">{"imports":{"bezier-js":"/bezier/bezier.js"}}</script>
<script type="module">
import {drawLaserPen,drainPoints,setDelay,setMaxWidth} from '/drawing.mjs';
const tests = await (await fetch('/fixtures.json')).json();
const canvas = document.getElementById('canvas'), ctx = canvas.getContext('2d');
setDelay(500); setMaxWidth(8);
for (const test of tests.cases) {
  ctx.clearRect(0,0,640,360);
  const realNow = Date.now; Date.now = () => test.now;
  const points = drainPoints(test.points.map(([x,y,time])=>({x,y,time})));
  Date.now = realNow;
  if (points.length >= 2) {
    ctx.save(); ctx.globalAlpha = Math.max(0,1-(test.now-points.at(-1).time)/500);
    if (points.length >= 3) drawLaserPen(ctx,points);
    else {ctx.strokeStyle='rgb(255,0,0)';ctx.lineWidth=5;ctx.lineCap='round';ctx.beginPath();ctx.moveTo(points[0].x,points[0].y);ctx.lineTo(points[1].x,points[1].y);ctx.stroke();}
    ctx.restore();
  }
  const [x,y] = test.points.at(-1);
  ctx.save();ctx.fillStyle='rgb(255,0,0)';ctx.shadowColor=ctx.fillStyle;ctx.shadowBlur=12;ctx.beginPath();ctx.arc(x,y,6,0,Math.PI*2);ctx.fill();ctx.restore();
  const blob = await new Promise(resolve=>canvas.toBlob(resolve));
  await fetch('/output/'+test.name,{method:'POST',body:blob});
}
document.title='PASS';
</script>`;
const server = http.createServer((req,res) => {
  const route = new URL(req.url,'http://localhost').pathname;
  if (req.method === 'POST' && route.startsWith('/output/')) {
    const name = route.slice('/output/'.length);
    if (!fixtures.cases.some(test=>test.name===name)) {res.writeHead(404).end();return;}
    const chunks=[];let size=0;
    req.on('data',chunk=>{size+=chunk.length;if(size>2_000_000)req.destroy();else chunks.push(chunk);});
    req.on('end',()=>{writeFileSync(path.join(output,`${name}.png`),Buffer.concat(chunks));received.add(name);res.end('OK');if(received.size===fixtures.cases.length)complete();});
    return;
  }
  if (route==='/') {res.setHeader('Content-Type','text/html');res.end(html);return;}
  if (route==='/fixtures.json') {res.setHeader('Content-Type','application/json');res.end(JSON.stringify(fixtures));return;}
  let filename;
  if (route==='/drawing.mjs') filename=path.join(root,'.references/generated/drawing.mjs');
  else if (/^\/bezier\/[a-z-]+\.js$/.test(route)) filename=path.join(root,'.references/node_modules/bezier-js/src',path.basename(route));
  if (filename) {try{res.setHeader('Content-Type','text/javascript');res.end(readFileSync(filename));}catch{res.writeHead(404).end();}return;}
  res.writeHead(404).end();
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const browser = spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  ['--headless=new','--disable-gpu','--no-first-run','--disable-background-networking',
    '--user-data-dir='+path.join(root,'.tools/reference-browser'),`http://127.0.0.1:${server.address().port}/`],{windowsHide:true,stdio:'ignore'});
let timeout;
try {
  await Promise.race([done,new Promise((_,reject)=>{timeout=setTimeout(()=>reject(new Error('Reference browser export timed out')),45000);}),
    new Promise((_,reject)=>browser.on('error',reject))]);
  console.log(`Exported ${received.size} PNGs from the original TypeScript renderer in Edge to ${output}`);
} finally {clearTimeout(timeout);browser.kill();server.closeAllConnections();server.close();}
