// Blueprint metrics gate - counts structural floors in docs/blueprint and fails below any of them.
// Run: node scripts/blueprint-metrics.mjs
import fs from 'node:fs';
import path from 'node:path';
const B = path.join(process.cwd(), 'docs', 'blueprint');
const read = f => fs.readFileSync(path.join(B, f), 'utf8');
const count = (file, re) => (read(file).match(re) || []).length;

const m = {
  ASSUMPTIONS: count('00-assumptions.md', /^\|\s*A-\d+/gm),
  SOURCES: count('01-research.md', /^\|\s*S-\d+/gm),
  CAPABILITY: count('01-research.md', /^\|\s*C-\d+/gm),
  ACTORS: count('02-domain.md', /^\|\s*ACT-\d+/gm),
  RULES: count('02-domain.md', /^\|\s*BR-[A-Z]+-\d+/gm),
  EXCEPTIONS: count('02-domain.md', /^\|\s*EX-\d+/gm),
  ENTITIES: count('03-data-model.md', /^\|\s*E-\d+/gm),
  REQS: count('04-requirements.md', /^\|\s*REQ-[A-Z]+-\d+/gm),
  NFRS: count('05-nfr.md', /^\|\s*NFR-[A-Z]+-\d+/gm),
  AUTHZ: count('06-authz.md', /^\|\s*AZ-\d+/gm),
  ADRS: count('07-decisions.md', /^##\s*ADR-\d+/gm),
  PHASES: count('09-phase-plan.md', /^##\s*P\d+:/gm),
};
const floor = { ASSUMPTIONS: 20, SOURCES: 14, CAPABILITY: 60, ACTORS: 8, RULES: 40, EXCEPTIONS: 40, ENTITIES: 25, REQS: 120, NFRS: 14, AUTHZ: 30, ADRS: 16, PHASES: 10 };
const fail = [];
for (const k of Object.keys(m)) {
  console.log(`${k.padEnd(12)}=${String(m[k]).padStart(4)}  floor ${floor[k]}`);
  if (m[k] < floor[k]) fail.push(`${k}=${m[k]}<${floor[k]}`);
}
const reqLines = read('04-requirements.md').split(/\r?\n/).filter(l => /^\|\s*REQ-[A-Z]+-\d+\s*\|/.test(l));
const must = reqLines.filter(l => /\|\s*Must\s*\|/.test(l)).length;
const pct = m.REQS ? Math.round(1000 * must / m.REQS) / 10 : 0;
console.log(`MOSCOW: Must=${must} (${pct}%)  (band 55-75%)`);
if (pct < 55 || pct > 75) fail.push(`MOSCOW=${pct}%`);

// Source classes: every class 1-7 must be represented.
const classes = {};
for (const l of read('01-research.md').split(/\r?\n/)) {
  const mm = l.match(/^\|\s*S-\d+\s*\|[^|]*\|\s*(\d)\s*\|/);
  if (mm) classes[mm[1]] = (classes[mm[1]] || 0) + 1;
}
console.log('SOURCE CLASSES: ' + [1, 2, 3, 4, 5, 6, 7].map(c => `CLASS${c}=${classes[c] || 0}`).join(' '));
for (const c of [1, 2, 3, 4, 5, 6, 7]) if (!classes[c]) fail.push(`CLASS${c}=0`);

if (fail.length) { console.log('BLUEPRINT_METRICS=FAIL'); fail.forEach(f => console.log('  ' + f)); process.exit(1); }
console.log('BLUEPRINT_METRICS=PASS');
