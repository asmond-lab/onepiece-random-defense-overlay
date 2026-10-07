import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

const root = path.resolve(process.argv[2] ?? path.join(import.meta.dirname, '..', '..'));
const generator = path.join(root, 'Tools/ActivityRuleGenerator/generate.mjs');
const bundlePath = path.join(root, 'Data/map-activity-2321.json');
const evidencePath = path.join(root, 'artifacts/activity-logging-20260921/rules/source-evidence.json');
const reportPath = path.join(root, 'artifacts/activity-logging-20260921/rules/monitor-report.json');
const hash = value => crypto.createHash('sha256').update(value).digest('hex');
const run = () => execFileSync(process.execPath, [generator, root], { encoding: 'utf8' }).trim();

const firstRun = run();
const firstBundle = fs.readFileSync(bundlePath);
const firstEvidence = fs.readFileSync(evidencePath);
const secondRun = run();
const secondBundle = fs.readFileSync(bundlePath);
const secondEvidence = fs.readFileSync(evidencePath);
if (!firstBundle.equals(secondBundle) || !firstEvidence.equals(secondEvidence)) throw new Error('generator is not deterministic');

const data = JSON.parse(secondBundle);
const evidence = JSON.parse(secondEvidence);
if (data.schemaVersion !== 1 || data.mapVersion !== '2.321' || data.jassSha256 !== evidence.jassSha256) throw new Error('bundle identity mismatch');
if (data.recipes.length !== 265 || data.wisps.length !== 6 || data.gambles.length !== 10 || data.integerArrays.length !== 19) throw new Error('bundle count mismatch');
if (data.recipes.filter(recipe => recipe.isActiveChoice).length !== 264) throw new Error('active choice mismatch');
const globalNames = new Set(data.integerArrays.map(row => row.name));
if (globalNames.size !== data.integerArrays.length || data.integerArrays.some(row => row.type !== 9 || row.sourceLines.length < 2)) throw new Error('integer-array evidence mismatch');
for (const wisp of data.wisps) if (wisp.consumptionCounterGlobal && !globalNames.has(wisp.consumptionCounterGlobal)) throw new Error(`unexported wisp global ${wisp.consumptionCounterGlobal}`);
for (const gamble of data.gambles) {
  for (const name of [gamble.attemptsGlobal, gamble.successesGlobal, gamble.failuresGlobal, ...gamble.branchGlobals].filter(Boolean)) {
    if (!globalNames.has(name)) throw new Error(`unexported gamble global ${name}`);
  }
}
for (const recipe of data.recipes) {
  if (!Number.isInteger(recipe.outputCount) || recipe.outputCount < 1 || Object.values(recipe.ingredients).some(count => !Number.isInteger(count) || count < 1) || recipe.otherRequirements.some(row => !Number.isInteger(row.count) || row.count < 1)) throw new Error(`invalid recipe count ${recipe.id}`);
}
const report = {
  schemaVersion: 1,
  status: 'passed',
  firstRun: JSON.parse(firstRun),
  secondRun: JSON.parse(secondRun),
  bundleSha256: hash(secondBundle),
  evidenceSha256: hash(secondEvidence),
  checks: ['source-pin-and-size','deterministic-regeneration','schema-and-counts','active-choice-resolution','native-type-9-global-registry','global-source-evidence','rawcode-and-positive-count-validation']
};
fs.writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
