import axe, { AxeResults, Result } from 'axe-core';

/**
 * Runs the accessibility rules over a rendered component.
 *
 * NFR-ACC-01 asks for zero serious or critical violations. This runs the same engine a browser
 * extension runs, against the real DOM the component produced, inside the ordinary test run. It
 * cannot see contrast against a real stylesheet or anything that only appears after a genuine
 * paint, so it is a floor rather than a certificate: it catches missing labels, unlabelled controls,
 * broken heading order, tables without headers and images without alternative text, which is most of
 * what goes wrong in practice.
 */
export async function findSeriousAccessibilityViolations(element: HTMLElement): Promise<Result[]> {
  // The component under test is mounted inside the test host element, which is not a document. axe
  // needs the fragment attached to a document to resolve colour and layout, and Vitest's jsdom
  // environment provides one.
  const results: AxeResults = await axe.run(element, {
    resultTypes: ['violations'],

    // Colour contrast needs a real rendering engine to measure. jsdom reports every element as
    // transparent on transparent, so the rule would report either nothing or everything; either way
    // its answer here would be noise rather than evidence.
    rules: { 'color-contrast': { enabled: false } },
  });

  return results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
}

/** The violations, formatted so a failing test names the element rather than a rule id. */
export function describeViolations(violations: readonly Result[]): string {
  return violations
    .map((v) => `${v.impact}: ${v.help} (${v.nodes.map((n) => n.target.join(' ')).join(', ')})`)
    .join('\n');
}
