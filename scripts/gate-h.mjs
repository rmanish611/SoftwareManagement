// Gate H - deterministic cross-file consistency reconciliation over docs/blueprint.
import fs from 'node:fs';
const B = 'G:/software-management/docs/blueprint';
const rd = f => fs.existsSync(`${B}/${f}`) ? fs.readFileSync(`${B}/${f}`, 'utf8') : '';
const dom = rd('02-domain.md'), dm = rd('03-data-model.md'), req = rd('04-requirements.md'), nfr = rd('05-nfr.md'), az = rd('06-authz.md'), adr = rd('07-decisions.md'), pp = rd('09-phase-plan.md');
const ids = (t, re) => new Set([...t.matchAll(re)].map(m => m[1]));
const BR = ids(dom, /^\|\s*(BR-[A-Z]+-\d+)/gm), EX = ids(dom, /^\|\s*(EX-\d+)/gm), ACT = ids(dom, /^\|\s*(ACT-\d+)/gm);
const NFR = ids(nfr, /^\|\s*(NFR-[A-Z]+-\d+)/gm), ADR = ids(adr, /^##\s*(ADR-\d+)/gm), AZ = ids(az, /^\|\s*(AZ-\d+)/gm);
const entRe = /^\|\s*E-\d+\s*\|\s*\**`?([A-Za-z][A-Za-z0-9_]*)`?\**\s*\|/gm;
const ENT = new Set([...dm.matchAll(entRe)].map(m => m[1]));
const domEnt = new Set([...dom.matchAll(entRe)].map(m => m[1]));
const reqRows = req.split(/\r?\n/).filter(l => /^\|\s*REQ-[A-Z]+-\d+\s*\|/.test(l)).map(l => {
  const c = l.split(/(?<!\\)\|/).map(x => x.trim());
  return { id: c[1], module: c[2], actor: c[3], story: c[4], ac: c[5], br: c[6], ent: c[7], moscow: c[8], phase: c[9], dep: c[10], ver: c[11], cols: c.length - 2 };
});
const REQ = new Set(reqRows.map(r => r.id));
const fail = [], warn = [];
const usedEnt = new Set();
reqRows.map(r => r.id).filter((x, i, a) => a.indexOf(x) !== i).forEach(d => fail.push(`duplicate REQ id ${d}`));
for (const r of reqRows) {
  if (r.cols !== 11) fail.push(`${r.id}: ${r.cols} columns, expected 11`);
  for (const b of (r.br.match(/BR-[A-Z]+-\d+/g) || [])) if (!BR.has(b)) fail.push(`${r.id}: BR ${b} not defined in 02-domain.md`);
  for (const e of ((r.ent || '').match(/[A-Za-z][A-Za-z0-9_]+/g) || [])) {
    if (/^(none|n\/a|na)$/i.test(e)) continue;
    usedEnt.add(e);
    if (!ENT.has(e)) fail.push(`${r.id}: entity ${e} not in 03-data-model.md dictionary`);
  }
  for (const d of ((r.dep || '').match(/REQ-[A-Z]+-\d+/g) || [])) if (!REQ.has(d)) fail.push(`${r.id}: depends on unknown ${d}`);
  if (!/^(Must|Should|Could|Won't|Wont|Withdrawn)$/.test(r.moscow)) fail.push(`${r.id}: bad MoSCoW '${r.moscow}'`);
  if (r.moscow === 'Must' && !/P\d{2}/.test(r.phase)) fail.push(`${r.id}: Must without phase`);
  if (!/given/i.test(r.ac) || !/when/i.test(r.ac) || !/then/i.test(r.ac)) fail.push(`${r.id}: acceptance criteria not Given/When/Then`);
  if (r.moscow === 'Must' && !/(reject|4\d\d|error|denied|invalid|fail|refus)/i.test(r.ac)) warn.push(`${r.id}: no visible rejection path in acceptance criteria`);
}
for (const e of ENT) if (!usedEnt.has(e)) warn.push(`entity ${e} in data model is not touched by any REQ`);
for (const e of domEnt) if (!ENT.has(e)) warn.push(`domain entity ${e} missing from data model dictionary`);
const phases = [...pp.matchAll(/^##\s*(P\d{2}[ab]?):/gm)].map(m => m[1]);
const PH = new Set(phases);
for (const r of reqRows) { const m = (r.phase || '').match(/P\d{2}[ab]?/); if (m && !PH.has(m[0])) fail.push(`${r.id}: phase ${m[0]} not in 09-phase-plan.md`); }
const blocks = pp.split(/^##\s*(?=P\d{2})/m).slice(1);
const delivered = new Map();
let uiNone = 0;
for (const b of blocks) {
  const name = (b.match(/^(P\d{2}[ab]?)/) || [])[1] || '?';
  const line = (b.match(/REQ IDs delivered:([^\n]*)/) || [])[1] || '';
  for (const id of (line.match(/REQ-[A-Z]+-\d+/g) || [])) {
    if (!REQ.has(id)) fail.push(`${name}: delivers unknown ${id}`);
    if (delivered.has(id)) warn.push(`${id} delivered by both ${delivered.get(id)} and ${name}`);
    delivered.set(id, name);
  }
  for (const id of (b.match(/NFR-[A-Z]+-\d+/g) || [])) if (!NFR.has(id)) fail.push(`${name}: references unknown ${id}`);
  for (const id of (b.match(/REQ-[A-Z]+-\d+/g) || [])) if (!REQ.has(id)) fail.push(`${name}: references unknown ${id}`);
  for (const id of (b.match(/BR-[A-Z]+-\d+/g) || [])) if (!BR.has(id)) fail.push(`${name}: references unknown ${id}`);
  if (/UI:\s*none/i.test(b)) uiNone++;
  const mt = Number((b.match(/minTests:\s*(\d+)/) || [])[1] || 0);
  const nreq = (line.match(/REQ-[A-Z]+-\d+/g) || []).length;
  if (nreq && mt < 2 * nreq) fail.push(`${name}: minTests ${mt} < 2 x ${nreq} REQ IDs`);
  if (name !== 'P01' && name !== 'P02') {
    for (const tag of ['[BR]', '[REJECT]', '[PERSIST]']) if (!b.includes(tag)) fail.push(`${name}: acceptance criteria missing ${tag}`);
    if (!/UI:\s*none/i.test(b) && !b.includes('[UI]')) fail.push(`${name}: acceptance criteria missing [UI]`);
  }
}
if (uiNone > 2) fail.push(`UI: none phases = ${uiNone} (cap 2)`);
for (const r of reqRows) {
  if (r.moscow === 'Must' && !delivered.has(r.id)) fail.push(`${r.id}: Must not delivered by any phase block`);
  const m = (r.phase || '').match(/P\d{2}[ab]?/);
  if (m && delivered.has(r.id) && delivered.get(r.id) !== m[0]) fail.push(`${r.id}: phase column ${m[0]} but delivered in ${delivered.get(r.id)}`);
}
const n = reqRows.length, must = reqRows.filter(r => r.moscow === 'Must').length;
const mods = {};
reqRows.forEach(r => { (mods[r.module] = mods[r.module] || { n: 0, must: 0 }); mods[r.module].n++; if (r.moscow === 'Must') mods[r.module].must++; });
for (const [m, s] of Object.entries(mods)) {
  if (s.n < 3) fail.push(`module ${m} has ${s.n} REQ rows (<3)`);
  if (s.must < 2) fail.push(`module ${m} has ${s.must} Musts (<2)`);
  if (s.n > 0.25 * n) fail.push(`module ${m} holds ${(100 * s.n / n).toFixed(1)}% of rows (>25%)`);
}
const pct = n ? 100 * must / n : 0;
if (pct < 55 || pct > 75) fail.push(`MoSCoW Must ${pct.toFixed(1)}% outside 55-75%`);
if (phases.length < 10 || phases.length > 15) fail.push(`phase count ${phases.length} outside 10-15`);
console.log(`REQ=${n} MUST=${must} (${pct.toFixed(1)}%) BR=${BR.size} EX=${EX.size} ACT=${ACT.size} ENT=${ENT.size} NFR=${NFR.size} AZ=${AZ.size} ADR=${ADR.size} PHASES=${phases.length} UI_NONE=${uiNone}`);
console.log('MODULES: ' + Object.entries(mods).map(([m, s]) => `${m}=${s.n}/${s.must}M`).join(' '));
warn.forEach(w => console.log('WARN ' + w));
fail.forEach(f => console.log('FAIL ' + f));
console.log(`GATE_H ${fail.length ? 'FAIL' : 'PASS'} failures=${fail.length} warnings=${warn.length}`);
process.exit(fail.length ? 1 : 0);
