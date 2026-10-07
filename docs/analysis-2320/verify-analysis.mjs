import fs from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import { createHash } from 'node:crypto';
import path from 'node:path';
import assert from 'node:assert/strict';
const root=process.argv[2];
if(!root)throw new Error('Usage: node verify-analysis.mjs <analysis-root>');
const tests=[];
function test(name,fn){fn();tests.push({name,status:'pass'});}
async function sha(file){const h=createHash('sha256');for await(const b of createReadStream(file))h.update(b);return h.digest('hex').toUpperCase();}
const manifest=JSON.parse(await fs.readFile(path.join(root,'modern-reader','confirmed-source-manifest.json'),'utf8'));
for(const a of manifest.archives){
 const got=await sha(a.archivePath);test(`archive ${a.version} unchanged`,()=>assert.equal(got,a.sha256Before.toUpperCase()));
 test(`archive ${a.version} previous after hash`,()=>assert.equal(got,a.sha256After.toUpperCase()));
 for(const m of a.members){const s=await fs.stat(m.extractionPath);const h=await sha(m.extractionPath);test(`${a.version}/${m.name} bytes and hash`,()=>{assert.equal(s.size,m.lengthBytes);assert.equal(h,m.sha256.toUpperCase());});}
}
const source=await fs.readFile(path.join(root,'modern-reader','members-ko-2.320','war3map.j'),'utf8');
const registry=JSON.parse(await fs.readFile(path.join(root,'utility-board-formulas.json'),'utf8')).registry;
const body=/function UtilityObjectData__Init takes nothing returns nothing\r?\n([\s\S]*?)\r?\nendfunction/.exec(source)?.[1];
assert.ok(body,'Exact registry function exists');
const independentlyParsed=[];let value;let row={};
function fourcc(hex){return Buffer.from(hex,'hex').toString('ascii');}
for(const line of body.split(/\r?\n/)){
 let m=/^(?:local real|set) value=(-?(?:\d+(?:\.\d*)?|\.\d+))$/.exec(line);if(m){value=Number(m[1]);continue;}
 m=/^set (vT|QT)\[MT\]=\$([0-9a-fA-F]{8})$/.exec(line);if(m){row[m[1]==='vT'?'ability':'family']=fourcc(m[2]);continue;}
 m=/^set (OT|rT)\[MT\]=(\d+)$/.exec(line);if(m){row[m[1]==='OT'?'category':'level']=Number(m[2]);continue;}
 if(line==='set hT[MT]=value'){assert.ok(Number.isFinite(value));row.value=value;continue;}
 m=/^set hT\[MT\]=(-?(?:\d+(?:\.\d*)?|\.\d+))$/.exec(line);if(m){row.value=Number(m[1]);continue;}
 if(line==='set MT=MT+1'){assert.deepEqual(Object.keys(row).sort(),['ability','category','family','level','value']);independentlyParsed.push(row);row={};}
}
const published=registry.map(({ability,category,family,level,value})=>({ability,category,family,level,value}));
test('all 221 registry rows independently reparsed from pinned JASS',()=>{assert.equal(independentlyParsed.length,221);assert.deepEqual(independentlyParsed,published);});
test('registry tampering negative control',()=>{const altered=structuredClone(published);altered[0].value++;assert.throws(()=>assert.deepEqual(independentlyParsed,altered));});
function board(states){
 for(const s of states){if(typeof s.eligible!=='boolean'||!Number.isInteger(s.level)||s.level<0||typeof s.ability!=='string')throw new Error('Unknown runtime state cannot be coerced');}
 const minimum=new Map(),families=new Map();
 const present=published.map(r=>states.some(s=>s.eligible&&s.ability===r.ability&&s.level===r.level));
 for(let i=0;i<published.length;i++){if(!present[i])continue;const r=published[i],k=r.category+':'+r.ability;minimum.set(k,Math.min(minimum.get(k)??Infinity,r.level));}
 for(let i=0;i<published.length;i++){const r=published[i];if(!present[i]||r.level!==minimum.get(r.category+':'+r.ability))continue;const k=r.category+':'+r.family;const prior=families.get(k);if(prior===undefined||Math.abs(r.value)<Math.abs(published[prior].value))families.set(k,i);}
 const totals=[0,0,0,0];for(const i of families.values()){const r=published[i];totals[r.category-1]+=(r.category===1||r.category===4?-1:1)*r.value;}
 return totals;
}
const s=(ability,level=1)=>({ability,level,eligible:true});
test('empty explicit roster',()=>assert.deepEqual(board([]),[0,0,0,0]));
test('duplicate instances do not multiply',()=>assert.deepEqual(board([s('A15G'),s('A15G')]),[35,0,25,0]));
test('weaker same-family armor wins in board algorithm',()=>assert.deepEqual(board([s('A15G'),s('A0GJ')]),[25,0,20,0]));
test('lower registered ability level suppresses higher',()=>assert.deepEqual(board([s('A173',1),s('A173',2)]),[0,45,0,0]));
test('Bullet level1 registered',()=>assert.deepEqual(board([s('A0WN',1)]),[5,0,0,0]));
for(const level of [2,3,4])test(`Bullet level${level} absent from board registry`,()=>assert.deepEqual(board([s('A0WN',level)]),[0,0,0,0]));
test('Bullet slow is separate row',()=>assert.deepEqual(board([s('A0WV')]),[0,20,0,0]));
test('Leo normal board metadata remains7',()=>assert.deepEqual(board([s('A0II',1)]),[0,7,0,0]));
test('Leo MartialLaw level2 missing registry',()=>assert.deepEqual(board([s('A0II',2)]),[0,0,0,0]));
test('proc armor not included in first armor category',()=>assert.deepEqual(board([s('A0GG')]),[0,0,0,15]));
test('explicitly ineligible ability contributes nothing',()=>assert.deepEqual(board([{...s('A15G'),eligible:false}]),[0,0,0,0]));
test('unknown eligibility rejected',()=>assert.throws(()=>board([{ability:'A15G',level:1}])));
const result={scope:'Offline extraction integrity, independent registry parsing and synthetic board-model fixtures; not live game or production regression.',passed:tests.length,failed:0,tests};
await fs.writeFile(path.join(root,'analysis-verification-results.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
