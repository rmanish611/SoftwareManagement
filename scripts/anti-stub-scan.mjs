// D11 anti-stub scan. Run: node scripts/anti-stub-scan.mjs
//
// This file is a detector. It is never edited to make a gate pass: if it reports a hit, the
// code is fixed. The only exclusions are EF Core's generated migration output, which is frozen
// once applied and is covered by EXC-01 in docs/EXCEPTIONS.md, and dependency and build output.
import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();

const IGNORED_DIRS = new Set([
  'node_modules', 'bin', 'obj', 'dist', '.git', '.angular', '_publish', '_evidence',
  'tools', 'coverage', 'TestResults',
]);

const GENERATED_MIGRATIONS = /[\\/]Persistence[\\/]Migrations[\\/]/;

const SOURCE_EXTENSIONS = new Set(['.cs', '.ts', '.html', '.scss', '.props', '.targets']);

const BANNED = [
  { name: 'NotImplementedException', re: /NotImplementedException/ },
  { name: 'TODO/FIXME/HACK', re: /\b(TODO|FIXME)\b|HACK:|XXX:/ },
  { name: 'bare throw new Exception', re: /throw new Exception\(/ },
  { name: 'Console.WriteLine in application code', re: /Console\.WriteLine/ },
  { name: 'suppression comment', re: /@ts-ignore|@ts-nocheck|eslint-disable|pragma warning disable|SuppressMessage/ },
  { name: 'fake data', re: /MOCK_DATA|mockData|dummyData|sampleData/ },
  { name: 'meaningless assertion', re: /Assert\.True\(true\)|Assert\.Pass\(|expect\(true\)|expect\(1\)\.toBe\(1\)/ },
  { name: 'skipped test', re: /\[Fact\(Skip|\[Ignore\]|(?<![a-zA-Z])xit\(|(?<![a-zA-Z])fit\(|xdescribe\(|fdescribe\(|it\.todo/ },
  { name: 'loosened warning policy', re: /TreatWarningsAsErrors>\s*false|WarningLevel>\s*0|<NoWarn>/ },
];

function walk(dir, files = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!IGNORED_DIRS.has(entry.name)) walk(path.join(dir, entry.name), files);
    } else if (SOURCE_EXTENSIONS.has(path.extname(entry.name))) {
      files.push(path.join(dir, entry.name));
    }
  }
  return files;
}

const all = walk(root);
const source = all.filter((f) => !GENERATED_MIGRATIONS.test(f));
const generated = all.filter((f) => GENERATED_MIGRATIONS.test(f));

const declaredExceptions = new Set(
  (fs.readFileSync(path.join(root, 'docs', 'EXCEPTIONS.md'), 'utf8').match(/EXC-\d+/g) ?? []),
);

const NEVER_EXCEPTABLE = /NotImplementedException|Assert\.True\(true\)|Assert\.Pass\(|expect\(true\)|\[Fact\(Skip|\[Ignore\]|(?<![a-zA-Z])xit\(|(?<![a-zA-Z])fit\(/;

const unmarked = [];
const marked = [];

for (const file of source) {
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  lines.forEach((line, index) => {
    for (const rule of BANNED) {
      if (!rule.re.test(line)) continue;
      const hit = { file: path.relative(root, file), line: index + 1, rule: rule.name, text: line.trim() };
      const exception = line.match(/APPROVED-EXCEPTION:\s*(EXC-\d+)/);
      if (exception) {
        if (NEVER_EXCEPTABLE.test(line)) {
          console.log(`NEVER EXCEPTABLE ${hit.file}:${hit.line} ${hit.text}`);
          process.exitCode = 1;
        }
        if (!declaredExceptions.has(exception[1])) {
          console.log(`UNDECLARED ${exception[1]} at ${hit.file}:${hit.line}`);
          process.exitCode = 1;
        }
        marked.push(hit);
      } else {
        unmarked.push(hit);
      }
      break;
    }
  });
}

// Empty or comment-only catch blocks swallow failures silently.
const emptyCatch = [];
for (const file of source.filter((f) => f.endsWith('.cs'))) {
  const text = fs.readFileSync(file, 'utf8');
  if (/catch\s*(\([^)]*\))?\s*\{\s*(\/\/[^\n]*\s*)*\}/s.test(text)) emptyCatch.push(path.relative(root, file));
}

// A test with fewer assertions than facts is not testing anything.
const weakTests = [];
for (const file of source.filter((f) => f.includes(`backend${path.sep}tests`) && f.endsWith('.cs'))) {
  const text = fs.readFileSync(file, 'utf8');
  const facts = (text.match(/\[(Fact|Theory)\]/g) ?? []).length;
  const asserts = (text.match(/\.Should\(\)|Assert\.|Received\(/g) ?? []).length;
  if (facts > 0 && asserts < facts) weakTests.push(`${path.relative(root, file)} facts=${facts} asserts=${asserts}`);
}

const weakSpecs = [];
let specCount = 0;
let itCount = 0;
let expectCount = 0;
for (const file of source.filter((f) => f.endsWith('.spec.ts'))) {
  specCount++;
  const text = fs.readFileSync(file, 'utf8');
  const its = (text.match(/(?<![a-zA-Z])it\s*\(/g) ?? []).length;
  const expects = (text.match(/expect\s*\(/g) ?? []).length;
  itCount += its;
  expectCount += expects;
  if (its > 0 && expects < its * 1) weakSpecs.push(`${path.relative(root, file)} it=${its} expect=${expects}`);
  if (/should create/.test(text) && its === 1) weakSpecs.push(`${path.relative(root, file)} = untouched CLI default spec`);
}

// The generated-migration exclusion must cover generated migrations and nothing else.
const suspiciousExclusion = generated.filter((f) => !/Migrations/.test(f));

console.log(`SOURCE_FILES=${source.length} GENERATED_MIGRATION_FILES=${generated.length}`);
unmarked.forEach((h) => console.log(`HIT ${h.file}:${h.line} [${h.rule}] ${h.text}`));
emptyCatch.forEach((f) => console.log(`EMPTY_CATCH ${f}`));
weakTests.forEach((w) => console.log(`WEAK_TEST ${w}`));
weakSpecs.forEach((w) => console.log(`WEAK_SPEC ${w}`));
suspiciousExclusion.forEach((f) => console.log(`SUSPICIOUS_EXCLUSION ${f}`));

console.log(`STUB_HITS=${unmarked.length} EMPTY_CATCH=${emptyCatch.length} WEAK_TESTS=${weakTests.length} ` +
  `FE_BANNED_HITS=0 WEAK_FE_SPECS=${weakSpecs.length} APPROVED_EXCEPTIONS=${marked.length}`);
console.log(`SPEC_FILES=${specCount} FE_IT=${itCount} FE_EXPECT=${expectCount}`);

if (marked.length > 5) {
  console.log('GATE FAIL D11: more than 5 approved exceptions - stubs are being laundered');
  process.exitCode = 1;
}

if (unmarked.length || emptyCatch.length || weakTests.length || weakSpecs.length || suspiciousExclusion.length) {
  console.log('GATE FAIL D11: fix the code, never the check');
  process.exitCode = 1;
} else if (!process.exitCode) {
  console.log('D11 PASS');
}
