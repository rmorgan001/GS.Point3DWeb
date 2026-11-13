// Does the 3D view stop drawing when nothing moves? Counts WebGL draw calls and frames with the mounts slewing,
// still, and tracking (tiny moves), with one tab of four connected views. Uses the stack from start-stack.sh.
import { chromium } from 'playwright';

const app = process.argv[2] ?? 'http://localhost:5300';
const fake = process.argv[3] ?? 'http://localhost:5400';
const sleep = ms => new Promise(r => setTimeout(r, ms));
const setAll = q => Promise.all([0, 1, 2, 3].map(n => fetch(`${fake}/fake/${n}?${q}`)));

const browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--enable-gpu', '--ignore-gpu-blocklist'] });
const page = await (await browser.newContext({ viewport: { width: 1600, height: 1000 } })).newPage();
await page.addInitScript(() => {
    window.__draws = 0;
    for (const proto of [WebGL2RenderingContext.prototype, WebGLRenderingContext.prototype]) {
        const original = proto.drawElements;
        proto.drawElements = function (...a) { window.__draws++; return original.apply(this, a); };
    }
});
await page.goto(app);
await page.waitForFunction(() => [...document.querySelectorAll('.status-text')].length === 4
    && [...document.querySelectorAll('.status-text')].every(e => !/^(Ready|Initializing|Loading)/.test(e.textContent.trim())), null, { timeout: 30000 });
for (let i = 0; i < 4; i++) {
    const button = page.locator('.quadrant').nth(i).locator('.connect-btn');
    if (!(await button.getAttribute('class')).includes('connected')) await button.click();
}
await page.waitForFunction(() => [...document.querySelectorAll('.status-text')].every(e => e.textContent.startsWith('Every')), null, { timeout: 15000 });

async function measure(label) {
    await sleep(3000);
    const a = await page.evaluate(() => window.__draws);
    await sleep(5000);
    const b = await page.evaluate(() => window.__draws);
    console.log(`${label.padEnd(44)} ${((b - a) / 5).toFixed(0).padStart(6)} draw calls/s`);
}
await setAll('Moving=true');
await measure('mounts slewing (large moves every poll)');
await setAll('Moving=false');
await measure('mounts still');
// Tracking: RA fixed, LST advances, so the hour angle grows ~0.002° per 0.5 s poll.
await setAll('Moving=false&SiderealTime=12.00001');
for (let i = 0; i < 12; i++) { await setAll(`SiderealTime=${(12 + i * 0.5 / 3600 * 1.0027).toFixed(7)}`); await sleep(500); }
await measure('mounts tracking (moves too small to see)');
await page.locator('.quadrant').first().locator('canvas').hover();
await page.mouse.down();
await page.mouse.move(900, 400, { steps: 20 });
await page.mouse.up();
await measure('3 s after dragging the camera');
const hidden = await browser.newPage();
await hidden.goto('about:blank');
await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
await setAll('Moving=true');
await measure('slewing, tab in the background');

await setAll('Moving=false');
await browser.close();
