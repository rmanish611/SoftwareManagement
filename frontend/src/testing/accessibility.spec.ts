import { describeViolations, findSeriousAccessibilityViolations } from './accessibility';

/**
 * The accessibility harness, checked against itself.
 *
 * A check that cannot fail is not a check. These two tests prove the engine is actually running
 * over the element it is handed: it finds a real violation in bad markup, and stays quiet on markup
 * that is correct. Without them, a harness that silently did nothing would look exactly like a
 * codebase with no accessibility problems.
 */
describe('the accessibility harness', () => {
  const mount = (html: string): HTMLElement => {
    const host = document.createElement('div');
    host.innerHTML = html;
    document.body.appendChild(host);
    return host;
  };

  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('reports an image with no alternative text', async () => {
    const violations = await findSeriousAccessibilityViolations(mount('<img src="/logo.png">'));

    expect(violations.length).toBeGreaterThan(0);
    expect(describeViolations(violations).toLowerCase()).toContain('alternative text');
  });

  it('reports an input with no label', async () => {
    const violations = await findSeriousAccessibilityViolations(mount('<input type="text">'));

    expect(violations.length).toBeGreaterThan(0);
  });

  it('stays quiet on markup that is correct', async () => {
    const violations = await findSeriousAccessibilityViolations(
      mount('<img src="/logo.png" alt="The company logo"><label for="a">Name</label><input id="a" type="text">'),
    );

    expect(describeViolations(violations)).toBe('');
  });
});
