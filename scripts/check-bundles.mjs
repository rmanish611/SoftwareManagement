// Enforces the frozen bundle ceilings from NFR-PERF-03.
//
// angular.json owns the "initial" ceiling (500 kB) because the CLI can fail the build on it.
// The per-lazy-chunk ceiling (250 kB) cannot be expressed as an Angular budget without naming
// every chunk, so it is enforced here instead, over the real build output, and run in both the
// phase gate and CI.
//
// Raising either number is an R-13 violation. The only legal path to a bigger bundle is an ADR
// written before the change, and the new figure must still sit under the approved ceiling.
import fs from 'node:fs';
import path from 'node:path';

const distDir = process.argv[2];
const initialCeilingKb = Number(process.argv[3] ?? 500);
const lazyCeilingKb = Number(process.argv[4] ?? 250);

// With server-side rendering there is no prerendered index.html: the server produces the HTML per
// request and the build emits index.csr.html, the client-side shell used when hydration takes over.
// Either file tells us which assets load on first paint, which is what the ceiling is about.
const indexCandidates = ['index.html', 'index.csr.html']
  .map((name) => path.join(distDir, name))
  .filter((file) => fs.existsSync(file));

if (indexCandidates.length === 0) {
  console.log(`GATE FAIL D10: no index.html or index.csr.html under ${distDir}`);
  process.exit(1);
}

const indexHtml = fs.readFileSync(indexCandidates[0], 'utf8');
console.log(`INDEX_FILE=${path.basename(indexCandidates[0])}`);
const jsFiles = fs.readdirSync(distDir).filter((f) => f.endsWith('.js'));
const cssFiles = fs.readdirSync(distDir).filter((f) => f.endsWith('.css'));

const sizeKb = (file) => Math.round((fs.statSync(path.join(distDir, file)).size / 1024) * 10) / 10;

// A file referenced by index.html loads on first paint; everything else is lazy.
const isInitial = (file) => indexHtml.includes(file);

const initial = [...jsFiles, ...cssFiles].filter(isInitial);
const lazy = jsFiles.filter((f) => !isInitial(f));

const initialTotal = Math.round(initial.reduce((sum, f) => sum + sizeKb(f), 0) * 10) / 10;

console.log(`INITIAL_FILES=${initial.length} INITIAL_KB=${initialTotal} CEILING=${initialCeilingKb}`);
for (const file of initial) console.log(`  initial ${file} = ${sizeKb(file)} kB`);
console.log(`LAZY_CHUNKS=${lazy.length} CEILING_EACH=${lazyCeilingKb}`);
for (const file of lazy) console.log(`  lazy ${file} = ${sizeKb(file)} kB`);

const failures = [];
if (initialTotal > initialCeilingKb) failures.push(`initial bundle ${initialTotal} kB exceeds ${initialCeilingKb} kB`);
for (const file of lazy) {
  const kb = sizeKb(file);
  if (kb > lazyCeilingKb) failures.push(`lazy chunk ${file} is ${kb} kB, ceiling ${lazyCeilingKb} kB`);
}

if (failures.length) {
  console.log('GATE FAIL D10: bundle ceiling exceeded - split the code, never raise the ceiling');
  failures.forEach((f) => console.log('  ' + f));
  process.exit(1);
}

console.log('BUNDLE_BUDGETS=PASS');
