// Coverage gate (D15). Reads the merged Cobertura report and enforces the NFR-MAINT-02 floor.
//
// Only the four production assemblies are measured. Test projects are excluded because measuring
// how thoroughly the tests test themselves says nothing about the product, and including them
// would inflate the figure. Generated EF migrations are excluded too: they are verified by the
// database gate, which applies them to a real server (ASM-6).
//
// Usage: node scripts/coverage.mjs <report.xml|evidence-dir> [floor] [previousFloor]
import fs from 'node:fs';
import path from 'node:path';

const PRODUCTION_PACKAGES = [
  'SoftwareManagement.Domain',
  'SoftwareManagement.Application',
  'SoftwareManagement.Infrastructure',
  'SoftwareManagement.Api',
];

const target = process.argv[2];
const floor = Number(process.argv[3] ?? 70);
const previous = process.argv[4] === undefined ? null : Number(process.argv[4]);

const reportPath = fs.statSync(target).isDirectory()
  ? path.join(target, 'coverage.cobertura.xml')
  : target;

if (!fs.existsSync(reportPath)) {
  console.log(`GATE FAIL D15: no coverage report at ${reportPath}`);
  process.exit(1);
}

const xml = fs.readFileSync(reportPath, 'utf8');

let totalCovered = 0;
let totalValid = 0;
const rows = [];

for (const chunk of xml.split('<package ').slice(1)) {
  const name = (chunk.match(/name="([^"]+)"/) || [])[1];
  if (!name || !PRODUCTION_PACKAGES.includes(name)) continue;

  const body = chunk.split('</package>')[0];

  // Skip generated migration files: verified by the D4 database gate, not by unit tests.
  let covered = 0;
  let valid = 0;
  for (const cls of body.split('<class ').slice(1)) {
    const filename = (cls.match(/filename="([^"]*)"/) || [])[1] ?? '';
    if (/[\\/]Migrations[\\/]/.test(filename)) continue;

    for (const line of cls.matchAll(/<line number="\d+" hits="(\d+)"/g)) {
      valid++;
      if (Number(line[1]) > 0) covered++;
    }
  }

  totalCovered += covered;
  totalValid += valid;
  rows.push({ name, covered, valid, pct: valid ? Math.round((1000 * covered) / valid) / 10 : 0 });
}

rows.sort((a, b) => a.pct - b.pct);
for (const row of rows) {
  console.log(`PKG ${row.name} = ${row.pct}% (${row.covered}/${row.valid})`);
}

const rate = totalValid ? Math.round((1000 * totalCovered) / totalValid) / 10 : 0;
console.log(`LINE_COVERAGE=${rate}% (${totalCovered}/${totalValid}) FLOOR=${floor}%`);

let failed = false;

if (!rows.length) {
  console.log('GATE FAIL D15: no production assembly appeared in the report');
  failed = true;
}

if (rate < floor) {
  console.log('GATE FAIL D15: below the floor - write tests, never lower the floor');
  failed = true;
}

if (previous !== null && rate < previous - 1) {
  console.log(`GATE FAIL D15: coverage fell from ${previous}% to ${rate}%`);
  failed = true;
}

if (failed) process.exit(1);
console.log('D15 PASS');
