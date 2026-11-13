import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const root = fileURLToPath(new URL('../../', import.meta.url));
const reference = path.join(root, '.references/lan-laser-pen');
const commit = '1e643ed5cb9c93056ed9a9b2dce4ecd3765ad67b';
if (execFileSync('git', ['-C', reference, 'rev-parse', 'HEAD'], {encoding:'utf8'}).trim() !== commit ||
    execFileSync('git', ['-C', reference, 'status', '--porcelain'], {encoding:'utf8'}).trim())
  throw new Error('Reference checkout must be clean and pinned to ' + commit);
if (JSON.parse(readFileSync(path.join(root,'.references/node_modules/bezier-js/package.json'),'utf8')).version !== '5.1.0')
  throw new Error('Reference fixtures require bezier-js 5.1.0');
const generated = path.join(root, '.references/generated');
mkdirSync(generated, { recursive: true });
for (const [source, name] of [['src/index.ts', 'drawing'], ['example/orientation.ts', 'orientation']]) {
  writeFileSync(path.join(generated, `${name}.mjs`), stripTypeScriptTypes(readFileSync(path.join(reference, source), 'utf8')));
}
const drawing = await import(pathToFileURL(path.join(generated, 'drawing.mjs')));
const motion = await import(pathToFileURL(path.join(generated, 'orientation.mjs')));
drawing.setDelay(500); drawing.setMaxWidth(8);
const paths = [
  { name:'curve', points:[[40,220,700],[120,80,760],[210,250,820],[300,80,880],[400,230,940],[540,150,1000]] },
  { name:'straight', points:[[60,180,700],[180,180,800],[300,180,900],[540,180,1000]] },
  { name:'return', points:[[50,100,600],[50,100,650],[100,120,700],[50,100,750],[130,180,850],[300,140,1000]] },
  { name:'two', points:[[100,100,900],[500,220,1000]] },
];
const trails = [];
for (const item of paths) for (const now of [1000, 1150, 1550]) {
  const points = item.points.map(([x,y,time]) => ({x,y,time}));
  const realNow = Date.now; Date.now = () => now;
  const kept = drawing.drainPoints(points.map(p => ({...p})));
  Date.now = realNow;
  const strokes = [];
  let current;
  const context = {
    save(){}, restore(){}, beginPath(){}, closePath(){},
    moveTo(x,y){current=[x,y];}, bezierCurveTo(...p){current.push(...p);},
    stroke(){strokes.push({p:current, width:this.lineWidth, opacity:Number(this.strokeStyle.match(/,([^,]+)\)$/)[1])});},
  };
  if (kept.length >= 3) drawing.drawLaserPen(context, kept);
  trails.push({ name:`${item.name}-${now}`, now, points:item.points, kept:kept.map(p=>[p.x,p.y,p.time]),
    fade:kept.length ? Math.max(0,1-(now-kept.at(-1).time)/500) : 0, strokes });
}

const q = (axis, radians) => [0,0,0,Math.cos(radians/2)].map((v,i)=>Math.fround(i===axis ? Math.sin(radians/2) : v));
const times = [0,16,32,48,80,160,320,640];
const air = [];
for (const rotation of [0,1,2,3]) for (const axis of [0,1,2]) {
  const samples = times.map((time,i)=>({time, vector:q(axis, i===0?0:Math.min(i*.12,.6))}));
  let reference;
  const filter = new motion.LowPassFilter();
  for (const sample of samples) {
    const orientation = motion.quaternionOrientation(sample.vector);
    reference ??= motion.createReference(orientation, rotation*90);
    const angles = motion.pointerAngles(orientation, reference, rotation*90);
    sample.expected = motion.normalizedPoint(filter.update(angles,sample.time),1,false,false).map(v=>(v+1)/2);
  }
  air.push({name:`rotation-${rotation}-axis-${axis}`, rotation, gravity:false, samples});
}
for (const rotation of [0,1,2,3]) {
  let reference;
  const filter = new motion.LowPassFilter();
  const samples = times.map((time,i)=>({time, vector:[-i*.3,i*.5,9.81].map(Math.fround)}));
  for (const sample of samples) {
    const [x,y,z] = sample.vector;
    const orientation = motion.gravityOrientation({x,y,z});
    reference ??= orientation;
    const angles = motion.tiltAngles(orientation,reference,rotation*90);
    sample.expected = motion.normalizedPoint(filter.update(angles,sample.time),1,false,false).map(v=>(v+1)/2);
  }
  air.push({name:`gravity-${rotation}`,rotation,gravity:true,samples});
}
const windowsFixture = path.join(root,'pointa-windows/src/Pointa.Windows/Native/ReferenceTrails.json');
const androidFixture = path.join(root,'pointa-android/app/src/test/resources/air-reference.json');
mkdirSync(path.dirname(androidFixture),{recursive:true});
writeFileSync(windowsFixture,JSON.stringify({commit,bezier:'5.1.0',cases:trails},null,2));
writeFileSync(androidFixture,JSON.stringify(air,null,2));
writeFileSync(androidFixture.replace('.json','.csv'), 'name,rotation,gravity,time,x,y,z,w,expectedX,expectedY\n' +
  air.flatMap(test => test.samples.map(sample => [test.name,test.rotation,test.gravity,sample.time,
    sample.vector[0],sample.vector[1],sample.vector[2],sample.vector[3]??0,...sample.expected].join(','))).join('\n')+'\n');
console.log(`${trails.length} trail fixtures and ${air.length} motion streams generated from the unmodified upstream functions.`);
