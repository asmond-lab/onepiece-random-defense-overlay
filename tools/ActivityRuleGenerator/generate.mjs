import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const root = path.resolve(process.argv[2] ?? path.join(import.meta.dirname, '..', '..'));
const sourceRel = 'artifacts/activity-logging-20260921/source/war3map.j';
const growthRel = 'Data/map-growth-globals-2321.json';
const outputRel = 'Data/map-activity-2321.json';
const evidenceRel = 'artifacts/activity-logging-20260921/rules/source-evidence.json';
const expectedHash = '55d0ffb9921433f45a9244cb946bdd27dcd2552a3550d30c4617c2eaccb94e97';
const sourceBuffer = fs.readFileSync(path.join(root, sourceRel));
const sha256 = value => crypto.createHash('sha256').update(value).digest('hex');
if (sourceBuffer.length !== 3059742 || sha256(sourceBuffer) !== expectedHash) throw new Error('2.321 source pin mismatch');
const source = sourceBuffer.toString('utf8');
const lines = source.split(/\r?\n/);
const growth = JSON.parse(fs.readFileSync(path.join(root, growthRel), 'utf8'));
if (growth.mapVersion !== '2.321' || growth.jassSha256 !== expectedHash) throw new Error('growth registry source pin mismatch');

const decode = hex => Buffer.from(hex.padStart(8, '0'), 'hex').toString('ascii');
const app = rawcode => [...rawcode].reverse().join('');
const lineAt = index => source.slice(0, index).split('\n').length;
const parseJassInteger = token => token.startsWith('$') ? Number.parseInt(token.slice(1), 16) : Number(token);
const unique = values => [...new Set(values)];
const functionByName = name => {
  const start = lines.findIndex(line => line.startsWith(`function ${name} `));
  const end = lines.indexOf('endfunction', start);
  if (start < 0 || end < start) throw new Error(`missing function ${name}`);
  return { name, startLine: start + 1, endLine: end + 1 };
};
const assertLine = (line, text) => { if (!lines[line - 1].includes(text)) throw new Error(`source assertion failed at ${line}: ${text}`); };

// Direct craft registrations are the exact UNIT-output records in eOe.
const recipeHeader = /call SaveBoolean\(Gg,\$([0-9A-Fa-f]+),1,true\)\nset Qg=\$\1\nset rg=0\nset Zg=0\ncall SaveInteger\(kg,\$\1,0,\$554e4954\)\ncall SaveInteger\(Vg,Qg,rg,\$([0-9A-Fa-f]+)\)\ncall SaveInteger\(ng,Qg,rg,([^\n]+)\)/g;
const headers = [...source.matchAll(recipeHeader)];
const activeChoices = new Map();
for (const match of source.matchAll(/call SaveInteger\(Hf,\$([0-9A-Fa-f]+),A4g,\$([0-9A-Fa-f]+)\)/g)) {
  activeChoices.set(decode(match[1]), { recipeId: decode(match[2]), sourceLine: lineAt(match.index) });
}
const recipes = headers.map((match, index) => {
  const id = decode(match[1]);
  const outputSource = decode(match[2]);
  const outputCount = parseJassInteger(match[3]);
  const end = index + 1 < headers.length ? headers[index + 1].index : source.indexOf('endfunction', match.index);
  const block = source.slice(match.index, end);
  const requirements = [...block.matchAll(/call SaveInteger\(Pg,Qg,Zg,\$([0-9A-Fa-f]+)\)\ncall SaveInteger\(ug,Qg,Zg,\$([0-9A-Fa-f]+)\)\ncall SaveInteger\(Tg,Qg,Zg,([^\n]+)\)/g)].map(row => ({
    kind: decode(row[1]), sourceRawcode: decode(row[2]), rawcode: app(decode(row[2])), count: parseJassInteger(row[3])
  }));
  if (!Number.isInteger(outputCount) || outputCount <= 0 || requirements.some(r => !Number.isInteger(r.count) || r.count <= 0)) throw new Error(`non-integer recipe count ${id}`);
  const ingredients = {};
  for (const requirement of requirements.filter(r => r.kind === 'UNIT')) ingredients[requirement.rawcode] = (ingredients[requirement.rawcode] ?? 0) + requirement.count;
  const choice = activeChoices.get(outputSource);
  if (!choice) throw new Error(`no active choice for output ${outputSource}`);
  return {
    id, outputRawcode: app(outputSource), outputCount, ingredients,
    otherRequirements: requirements.filter(r => r.kind !== 'UNIT'), sourceLine: lineAt(match.index),
    sourceOutputRawcode: outputSource, activeRecipeId: choice.recipeId,
    isActiveChoice: choice.recipeId === id, activeChoiceSourceLine: choice.sourceLine
  };
});

const arrayValues = (name, first, last) => {
  const values = [];
  for (let i = first; i <= last; i++) {
    const pattern = new RegExp(`set ${name}\\[${i}\\]=\\$([0-9A-Fa-f]+)`);
    const row = lines.findIndex(line => pattern.test(line));
    if (row < 0) throw new Error(`missing ${name}[${i}]`);
    values.push(decode(lines[row].match(pattern)[1]));
  }
  return values;
};
const poolValues = name => [...source.matchAll(new RegExp(`call UnitPoolAddUnitType\\(${name},\\$([0-9A-Fa-f]+),1\\.\\)`, 'g'))].map(m => decode(m[1]));
const outputs = raws => unique(raws.map(app)).sort();
const JT = arrayValues('JT', 1, 9), Zt = arrayValues('Zt', 0, 8);
const pools = Object.fromEntries(['Eu','Nu','Su','Bu','Ki','Oi','hi','mi'].map(name => [name, poolValues(name)]));
for (const [name, values] of Object.entries(pools)) if (values.length === 0) throw new Error(`empty pool ${name}`);

const wisps = [
  { id:'e016', rawcode:app('e016'), kind:'special', outputRawcodes:outputs(pools.Eu), poolNames:['Eu'], sourceFunctions:[functionByName('v_t')] },
  { id:'e017', rawcode:app('e017'), kind:'uncommon', outputRawcodes:outputs(pools.Nu), poolNames:['Nu'], sourceFunctions:[functionByName('ret')] },
  { id:'e019', rawcode:app('e019'), kind:'rare', outputRawcodes:outputs([...pools.Su,...pools.Bu]), poolNames:['Su','Bu'], sourceFunctions:[functionByName('gPt')], observationLimitations:['No dedicated cumulative consumption counter is mutated by gPt.'] },
  { id:'e0IX', rawcode:app('e0IX'), kind:'random-or-context-token', outputRawcodes:outputs([...Zt,'h060']), poolNames:['Zt[0..8]'], sourceFunctions:[functionByName('FPe'),functionByName('Hmg'),functionByName('vOt')], consumptionCounterGlobal:'f', counterSemantics:'FPe increments f[player] once for normal common allocation, but the 0.24% direct h060 branch does not increment f; Hmg and vOt do not increment f.', contextDependentUses:['random common unit','lumber gamble','helper mana recovery'] },
  { id:'e018', rawcode:app('e018'), kind:'selection', outputRawcodes:outputs(JT), poolNames:['JT[1..9]'], sourceFunctions:['Dig','EQt','G_t','Hwg','UMt','fGt','qNg','sXg','vQe'].map(functionByName), consumptionCounterGlobal:'NU', counterSemantics:'Every listed selection handler increments NU[player] by exactly one before killing one e018.' },
  { id:'e01A', rawcode:app('e01A'), kind:'transcendent', outputRawcodes:outputs(['h04S','h05X','h060','h0BR']), poolNames:[], sourceFunctions:['Ppt','Woe','pBe'].map(functionByName), observationLimitations:['No dedicated cumulative consumption counter is mutated by these handlers.','Woe branches to h05X or h060; the other handlers are direct outputs.'] }
];

const gamble = (id,name,fn,attempts,successes,failures,branches,raws,extra={}) => ({
  id,name,attemptsGlobal:attempts,successesGlobal:successes,failuresGlobal:failures,branchGlobals:branches,
  outputRawcodes:outputs(raws),sourceFunctions:[functionByName(fn)],...extra
});
const gambles = [
  gamble('low','Low unit gamble','WHe','Ie',null,'xe',['tU','gU'],['h060',...JT,...pools.Ki],{counterEquation:'Ie = xe + tU + gU; successes = tU + gU.'}),
  gamble('middle','Middle unit gamble','nKe','eU','UU',null,[],['h060',...pools.Oi],{counterEquation:'failures = eU - UU.'}),
  gamble('high','High unit gamble','PXe','pU',null,'CU',['JU','fU'],['h06G','h05X',...pools.hi,...pools.mi],{counterEquation:'pU = CU + JU + fU; successes = JU + fU.'}),
  gamble('world','Other-world unit gamble','Bte','LU','aU',null,[],['h06G',...arrayValues('cT',0,41),...arrayValues('sT',0,13)],{counterEquation:'failures = LU - aU.',nonCumulativeStateGlobals:['dU']}),
  gamble('absalom','Absalom gamble','Set','cU','bU',null,[],['h00H','h010'],{counterEquation:'failures = cU - bU.'}),
  gamble('lumber','Lumber gamble','Hmg','vU','oU',null,[],[],{counterEquation:'failures = vU - oU.',nonUnitOutputs:['1 lumber on success','75 * current round gold on failure']}),
  gamble('money-high','Money gamble - high','VRt',null,null,null,['Se'],[],{counterSemantics:'Se is cumulative gold awarded by this handler, not an attempt/success counter.',nonUnitOutputs:['500..4500 gold success','300..400 gold failure refund']}),
  gamble('money-low','Money gamble - low','nme',null,null,null,[],[],{nonUnitOutputs:['10..50 gold success','3..5 gold failure refund'],observationLimitations:['No cumulative integer-array counter is mutated by nme.']}),
  gamble('ship-7-lumber','Ancient ship gamble - 7 lumber','Hdt',null,null,null,[],['h05X'],{nonUnitRequirements:['7 lumber'],observationLimitations:['No cumulative integer-array counter is mutated; failure has no unit output.']}),
  gamble('ship-8-lumber','Ancient ship purchase/gamble - 8 lumber','lUt',null,null,null,[],['h060'],{nonUnitRequirements:['8 lumber'],observationLimitations:['This handler has no random failure branch and no cumulative integer-array counter.']})
];

const meanings = {
  f:'Random-wisp normal common allocations (not every e0IX consumption)', NU:'Selection-wisp consumptions',
  Ie:'Low gamble attempts', xe:'Low gamble failures', tU:'Low gamble common-result branch count', gU:'Low gamble uncommon-result branch count',
  eU:'Middle gamble attempts', UU:'Middle gamble successes', pU:'High gamble attempts', CU:'High gamble failures',
  JU:'High gamble rare-result branch count', fU:'High gamble special-result branch count', LU:'Other-world gamble attempts', aU:'Other-world gamble successes',
  cU:'Absalom gamble attempts', bU:'Absalom gamble successes', vU:'Lumber gamble attempts', oU:'Lumber gamble successes',
  Se:'Cumulative gold awarded by high money gamble'
};
const integerArrays = Object.entries(meanings).map(([name,meaning]) => {
  if (growth.globals[name] !== 9) throw new Error(`${name} is not native type 9 in growth registry`);
  const sourceLines = [];
  lines.forEach((line,index) => { if (line === `integer array ${name}` || line.startsWith(`set ${name}[`)) sourceLines.push(index + 1); });
  if (!sourceLines.length || lines[sourceLines[0]-1] !== `integer array ${name}`) throw new Error(`missing declaration evidence ${name}`);
  return {name,type:9,meaning,sourceLines};
});

for (const [line,text] of [[70630,'Ie[BTe]'],[70631,'eU[BTe]'],[70632,'pU[BTe]'],[70633,'LU[BTe]'],[70634,'cU[BTe]'],[70635,'vU[BTe]'],[64856,'f[hyt]']]) assertLine(line,text);
if (recipes.length !== 265 || new Set(recipes.map(r=>r.id)).size !== 265 || activeChoices.size !== 264 || recipes.filter(r=>r.isActiveChoice).length !== 264) throw new Error('recipe/choice count mismatch');
if (wisps.length !== 6 || gambles.length !== 10 || integerArrays.length !== 19) throw new Error('bundle count mismatch');
const validAppRawcode = value => typeof value === 'string' && value.length === 4 && /^[\x20-\x7e]{4}$/.test(value);
for (const recipe of recipes) {
  const rawcodes = [recipe.id, recipe.outputRawcode, recipe.sourceOutputRawcode, ...Object.keys(recipe.ingredients), ...recipe.otherRequirements.flatMap(row => [row.sourceRawcode, row.rawcode])];
  if (rawcodes.some(raw=>!validAppRawcode(raw))) throw new Error(`bad recipe rawcode ${recipe.id}`);
}
for (const wisp of wisps) if (![wisp.id,wisp.rawcode,...wisp.outputRawcodes].every(validAppRawcode)) throw new Error(`bad wisp rawcode ${wisp.id}`);
for (const row of gambles) if (row.outputRawcodes.some(raw=>!validAppRawcode(raw))) throw new Error(`bad output rawcode ${row.id}`);

const data = {schemaVersion:1,mapVersion:'2.321',jassSha256:expectedHash,sourcePath:sourceRel,recipes,wisps,gambles,integerArrays};
const evidenceFunctions = unique([...wisps,...gambles].flatMap(row=>row.sourceFunctions.map(fn=>fn.name))).map(functionByName);
const evidence = {
  schemaVersion:1,mapVersion:'2.321',jassSha256:expectedHash,sourcePath:sourceRel,
  recipeRegistrationFunction:functionByName('eOe'), activeChoiceLines:[33294,34569],
  poolInitializationLines:[16977,17236,82678,82686,84693,84828,85218,85315],
  scoreboardLines:[70630,70631,70632,70633,70634,70635,70636,64856], functions:evidenceFunctions,
  note:'Line ranges point into the pinned source; no source text is duplicated here.'
};
const json = JSON.stringify(data,null,2)+'\n';
const evidenceJson = JSON.stringify(evidence,null,2)+'\n';
fs.writeFileSync(path.join(root,outputRel),json);
fs.writeFileSync(path.join(root,evidenceRel),evidenceJson);
console.log(JSON.stringify({output:outputRel,sha256:sha256(json),bytes:Buffer.byteLength(json),recipes:recipes.length,activeRecipes:recipes.filter(r=>r.isActiveChoice).length,wisps:wisps.length,gambles:gambles.length,integerArrays:integerArrays.length,evidence:evidenceRel}));

