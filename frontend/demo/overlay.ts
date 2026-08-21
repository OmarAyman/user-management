import { Page } from '@playwright/test';

/**
 * Caption and detail overlays, drawn onto the page.
 *
 * The recording has no audio, and Playwright records the page rather than the browser window - so anything a
 * presenter would say, point at in the address bar, or open DevTools to show has to appear in frame. Every
 * value shown here is read from the live page at the moment it is displayed; none of it is authored text
 * pretending to be output.
 */
const STYLE_ID = 'demo-overlay-style';

/**
 * Multiplies every hold, so the same walkthrough can be paced for different uses.
 *
 * At 1 it runs about two and a half minutes, which is right for a rehearsal reference. Narrating over it wants
 * longer: `DEMO_PACE=1.7` lands near four minutes, which is roughly what the cue sheet in
 * docs/14-demo-script.md takes to say out loud.
 */
const PACE = Number(process.env['DEMO_PACE'] ?? 1) || 1;

const CSS = `
#demo-caption, #demo-detail {
  position: fixed;
  z-index: 2147483647;
  font-family: "Segoe UI", Roboto, system-ui, sans-serif;
  box-sizing: border-box;
  pointer-events: none;
}
#demo-caption {
  inset-inline: 0;
  bottom: 0;
  padding: 14px 28px 16px;
  background: rgba(10, 12, 16, 0.86);
  color: #f4f6f8;
  font-size: 19px;
  line-height: 1.45;
  text-align: center;
  direction: ltr;
}
#demo-caption b { color: #7fd1ff; font-weight: 600; }
#demo-detail {
  inset-inline-end: 24px;
  top: 24px;
  max-width: 620px;
  padding: 14px 18px;
  background: rgba(10, 12, 16, 0.92);
  color: #d8f5c8;
  font-family: "Cascadia Code", Consolas, monospace;
  font-size: 14px;
  line-height: 1.5;
  white-space: pre-wrap;
  border-inline-start: 4px solid #7fd1ff;
  border-radius: 4px;
  direction: ltr;
  text-align: start;
}
`;

async function ensureStyle(page: Page): Promise<void> {
  await page.evaluate(
    ([id, css]) => {
      if (document.getElementById(id) !== null) {
        return;
      }

      const style = document.createElement('style');
      style.id = id;
      style.textContent = css;
      document.head.appendChild(style);
    },
    [STYLE_ID, CSS] as const,
  );
}

/** Shows a caption at the foot of the frame. `hold` is how long a viewer gets to read it. */
export async function caption(page: Page, html: string, hold = 2600): Promise<void> {
  await ensureStyle(page);

  await page.evaluate((text) => {
    const existing = document.getElementById('demo-caption');
    const element = existing ?? document.createElement('div');
    element.id = 'demo-caption';
    element.innerHTML = text;

    if (existing === null) {
      document.body.appendChild(element);
    }
  }, html);

  await page.waitForTimeout(hold * PACE);
}

/** Shows a monospace panel - a URL, a storage dump, a raw response - top right. */
export async function detail(page: Page, text: string, hold = 2600): Promise<void> {
  await ensureStyle(page);

  await page.evaluate((body) => {
    const existing = document.getElementById('demo-detail');
    const element = existing ?? document.createElement('div');
    element.id = 'demo-detail';
    element.textContent = body;

    if (existing === null) {
      document.body.appendChild(element);
    }
  }, text);

  await page.waitForTimeout(hold * PACE);
}

export async function clearOverlays(page: Page): Promise<void> {
  await page.evaluate(() => {
    document.getElementById('demo-caption')?.remove();
    document.getElementById('demo-detail')?.remove();
  });
}

/** The live URL, which the recording cannot otherwise show: there is no address bar in frame. */
export async function showUrl(page: Page, hold = 2200): Promise<void> {
  const url = page.url();
  await detail(page, `location.href\n${url}`, hold);
}
