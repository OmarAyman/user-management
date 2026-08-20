/**
 * Keeps the stylesheets direction-agnostic.
 *
 * The RTL approach in docs/09-localization-plan.md rests on one rule: application styles use CSS **logical**
 * properties (`margin-inline-start`, `inset-inline-end`, `text-align: start`) so a mirrored layout needs no
 * mirrored stylesheet. A single `margin-left` survives the direction switch and quietly breaks the Arabic
 * layout in a way no unit test notices and only a careful look at a screenshot catches.
 *
 * This is the check that enforces it, across both `.scss` files and inline component styles. A physical
 * property is allowed only with an explicit `/* physical: <reason> *\/` marker on the same line, so an
 * exception is a decision someone wrote down rather than an oversight.
 *
 * Run with `npm run lint:styles`.
 */

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sourceRoot = path.join(root, 'src');

/** Physical equivalents of the logical properties this project uses, and what to use instead. */
const forbidden = [
  { pattern: /(?<![-\w])margin-(left|right)\s*:/, advice: 'margin-inline-start / margin-inline-end' },
  { pattern: /(?<![-\w])padding-(left|right)\s*:/, advice: 'padding-inline-start / padding-inline-end' },
  { pattern: /(?<![-\w])border-(left|right)(-\w+)?\s*:/, advice: 'border-inline-start / border-inline-end' },
  { pattern: /(?<![-\w])(left|right)\s*:/, advice: 'inset-inline-start / inset-inline-end' },
  { pattern: /text-align\s*:\s*(left|right)/, advice: 'text-align: start / end' },
  { pattern: /float\s*:\s*(left|right)/, advice: 'flexbox or grid, which have no direction of their own' },
];

/** An acknowledged exception, e.g. `transform: scaleX(-1); /* physical: mirrors a directional icon *\/`. */
const acknowledged = /\/\*\s*physical:/;

function sourceFiles(directory) {
  return readdirSync(directory).flatMap((entry) => {
    const full = path.join(directory, entry);

    if (statSync(full).isDirectory()) {
      return sourceFiles(full);
    }

    return /\.(scss|css|ts|html)$/.test(entry) && !entry.endsWith('.spec.ts') ? [full] : [];
  });
}

const findings = [];

for (const file of sourceFiles(sourceRoot)) {
  const lines = readFileSync(file, 'utf8').split(/\r?\n/);

  lines.forEach((line, index) => {
    if (acknowledged.test(line)) {
      return;
    }

    for (const { pattern, advice } of forbidden) {
      if (pattern.test(line)) {
        findings.push({
          location: `${path.relative(root, file)}:${index + 1}`,
          text: line.trim(),
          advice,
        });
        return;
      }
    }
  });
}

if (findings.length > 0) {
  console.error(`${findings.length} physical direction propert${findings.length === 1 ? 'y' : 'ies'} found:\n`);

  for (const finding of findings) {
    console.error(`  ${finding.location}\n    ${finding.text}\n    use ${finding.advice}\n`);
  }

  console.error('Logical properties keep the Arabic layout correct without a mirrored stylesheet.');
  process.exit(1);
}

console.log('No physical direction properties in application styles.');
