// Merges the per-test-project Cobertura files into one honest figure.
// Each test run instruments every referenced assembly, so summing the run totals would count
// the same source lines several times. The merge keeps, per assembly, the run that actually
// exercised it (highest covered line count) and sums those.
import fs from 'node:fs';
import path from 'node:path';

const evidenceDir = process.argv[2];
const floor = Number(process.argv[3] ?? 70);

const files = [];
(function walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full);
    else if (entry.name === 'coverage.cobertura.xml') files.push(full);
  }
})(evidenceDir);

const best = new Map();
for (const file of files) {
  const xml = fs.readFileSync(file, 'utf8');
  for (const pkg of xml.split('<package ').slice(1)) {
    const name = (pkg.match(/name="([^"]+)"/) || [])[1];
    if (!name) continue;
    let valid = 0;
    let covered = 0;
    for (const line of pkg.matchAll(/<line number="\d+" hits="(\d+)"/g)) {
      valid++;
      if (Number(line[1]) > 0) covered++;
    }
    const current = best.get(name);
    if (!current || covered > current.covered) best.set(name, { covered, valid });
  }
}

let totalCovered = 0;
let totalValid = 0;
const rows = [...best.entries()].sort((a, b) => a[0].localeCompare(b[0]));
for (const [name, { covered, valid }] of rows) {
  totalCovered += covered;
  totalValid += valid;
  const pct = valid ? Math.round((1000 * covered) / valid) / 10 : 0;
  console.log(`PKG ${name} = ${pct}% (${covered}/${valid})`);
}

const rate = totalValid ? Math.round((1000 * totalCovered) / totalValid) / 10 : 0;
console.log(`LINE_COVERAGE=${rate}% (${totalCovered}/${totalValid}) FLOOR=${floor}%`);
if (rate < floor) {
  console.log('GATE FAIL D15: coverage below the floor - write tests, never lower the floor');
  process.exit(1);
}
console.log('D15 PASS');
