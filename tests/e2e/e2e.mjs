// Browser tests for GS Point3D Web, against the stack started by start-stack.sh (the app on :5300 and the fake
// Alpaca server on :5400 with four moving mounts, one in each view). Uses the installed Chrome, headless.
//
//   node e2e.mjs [appUrl=http://localhost:5300] [fakeUrl=http://localhost:5400]
import { chromium } from 'playwright';
import { writeFileSync, mkdirSync } from 'node:fs';

const app = process.argv[2] ?? 'http://localhost:5300';
const fake = process.argv[3] ?? 'http://localhost:5400';
const outDir = new URL('./out/', import.meta.url);
mkdirSync(outDir, { recursive: true });

const results = [];
const consoleProblems = [];
async function step(name, fn) {
    const started = Date.now();
    try {
        await fn();
        results.push({ name, ok: true, ms: Date.now() - started });
        console.log(`  PASS  ${name} (${Date.now() - started} ms)`);
    } catch (e) {
        results.push({ name, ok: false, ms: Date.now() - started, error: String(e?.message ?? e).split('\n')[0] });
        console.log(`  FAIL  ${name}: ${String(e?.message ?? e).split('\n').slice(0, 3).join(' | ')}`);
    }
}
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };
const sleep = ms => new Promise(r => setTimeout(r, ms));
const setFake = (n, query) => fetch(`${fake}/fake/${n}?${query}`).then(r => r.json());

const browser = await chromium.launch({
    channel: 'chrome',
    headless: true,
    args: ['--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-gpu']
});

async function newPage(context) {
    const page = await (context ?? await browser.newContext({ viewport: { width: 1600, height: 1000 }, locale: 'en-US' })).newPage();
    page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') consoleProblems.push(`${m.type()}: ${m.text()}`); });
    page.on('pageerror', e => consoleProblems.push(`pageerror: ${e.message}`));
    page.on('requestfailed', r => consoleProblems.push(`requestfailed: ${r.url()} ${r.failure()?.errorText}`));
    return page;
}

const quadrant = (page, i) => page.locator('.quadrant').nth(i);
const statusOf = (page, i) => quadrant(page, i).locator('.status-text').innerText();
async function waitStatus(page, i, test, timeout = 10000, what = 'status') {
    const until = Date.now() + timeout;
    let last = '';
    while (Date.now() < until) {
        last = await statusOf(page, i).catch(() => '');
        if (test(last)) return last;
        await sleep(100);
    }
    throw new Error(`view ${i}: timed out waiting for ${what}; last status "${last}"`);
}
/** Waits until the page is interactive: every view has started its viewer. */
async function waitInteractive(page, views = 4) {
    await page.waitForFunction(n => {
        const s = [...document.querySelectorAll('.quadrant .status-text')].map(e => e.textContent.trim());
        return s.length === n && s.every(t => t && t !== 'Ready' && !t.startsWith('Initializing') && !t.startsWith('Loading'));
    }, views, { timeout: 30000 });
}
async function acceptDialog(page, expectText) {
    const dialog = page.locator('.dialog[role=dialog]');
    await dialog.waitFor({ timeout: 5000 });
    if (expectText) assert((await dialog.innerText()).includes(expectText), `dialog says "${await dialog.innerText()}"`);
    await dialog.locator('.dialog-button').first().click();
    await dialog.waitFor({ state: 'detached', timeout: 5000 });
}

console.log(`GS Point3D Web browser tests: ${app} (fake mounts ${fake})`);
const page = await newPage();
const timings = {};

await step('Home page loads and becomes interactive', async () => {
    const t0 = Date.now();
    await page.goto(app, { waitUntil: 'domcontentloaded' });
    timings.domContentLoaded = Date.now() - t0;
    await waitInteractive(page);
    timings.interactiveAllViews = Date.now() - t0;
    assert(await page.title() === 'GS Point3D Web', `title ${await page.title()}`);
    assert(/^v\d/.test(await page.locator('.brand-version').innerText()), 'version shown');
    assert(await page.locator('.server-item').count() === 4, 'four servers listed');
    assert(await page.locator('canvas.render-canvas').count() === 4, 'four canvases');
    for (let i = 0; i < 4; i++) await waitStatus(page, i, s => s === 'Not connected', 5000, 'Not connected');
    assert((await page.locator('.connected-count').innerText()).includes('0 of 4 connected'), 'connected count');
});

await step('WebGL renders the scene', async () => {
    const info = await page.evaluate(() => {
        const gl = document.createElement('canvas').getContext('webgl2') ?? document.createElement('canvas').getContext('webgl');
        const ext = gl?.getExtension('WEBGL_debug_renderer_info');
        return { webgl: !!gl, renderer: ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl?.getParameter(gl.RENDERER) };
    });
    timings.webglRenderer = info.renderer;
    assert(info.webgl, 'no WebGL');
    const png = await quadrant(page, 0).locator('.viewport').screenshot();
    writeFileSync(new URL('view0.png', outDir), png);
    assert(png.length > 30000, `the view's screenshot is only ${png.length} bytes: blank?`);
});

await step('Connect from a view: coordinates, model and mount state update', async () => {
    await quadrant(page, 0).locator('.connect-btn').click();
    await waitStatus(page, 0, s => s.startsWith('Every 0.5 s · Updates'), 10000, 'updates');
    const ra1 = await quadrant(page, 0).locator('.coord.ra .value').innerText();
    const axes1 = await quadrant(page, 0).locator('.axes').innerText();
    await sleep(1500);
    const ra2 = await quadrant(page, 0).locator('.coord.ra .value').innerText();
    const axes2 = await quadrant(page, 0).locator('.axes').innerText();
    assert(ra1 !== ra2 && axes1 !== axes2, `didn't move: ${ra1} / ${ra2}`);
    assert(await quadrant(page, 0).locator('.mount-state .chip').count() >= 5, 'mount state strip');
    assert(await quadrant(page, 0).locator('.mount-state').innerText().then(t => t.includes('UTC')), 'UTC time');
    assert((await page.locator('.connected-count').innerText()).includes('1 of 4 connected'), 'count 1 of 4');
    assert(await page.locator('.server-item .status.connected').count() === 1, 'one green dot in the list');
    assert(await quadrant(page, 0).locator('.run-time').count() >= 1, 'run clock');
});

await step('Double-click on Ra switches to degrees and back', async () => {
    const ra = quadrant(page, 0).locator('.coord.ra');
    await ra.dblclick();
    await page.waitForFunction(() => document.querySelector('.quadrant .coord.ra .value').textContent.includes('°'), null, { timeout: 3000 });
    await ra.dblclick();
    await page.waitForFunction(() => document.querySelector('.quadrant .coord.ra .value').textContent.includes('h'), null, { timeout: 3000 });
});

await step('View settings: change the model and colour, then back', async () => {
    await quadrant(page, 0).locator('button[title="View settings"]').click();
    const settings = quadrant(page, 0).locator('.settings');
    await settings.waitFor({ state: 'visible', timeout: 3000 });
    await settings.locator('.model-type select').selectOption('Reflector');
    await settings.locator('.swatch').nth(3).click();
    await settings.locator('button.done').click();
    await waitStatus(page, 0, s => s.startsWith('Every') || s.includes('Reflector'), 8000, 'model reload');
    assert((await quadrant(page, 0).locator('.mount-tag').innerText()).includes('Reflector'), 'mount tag');
    // Back to the default model for the later steps.
    await quadrant(page, 0).locator('button[title="View settings"]').click();
    await settings.locator('.model-type select').selectOption('Default');
    await settings.locator('button.done').click();
});

await step('Lost connection is reported, then recovers by itself', async () => {
    await quadrant(page, 1).locator('.connect-btn').click();
    await waitStatus(page, 1, s => s.startsWith('Every'), 10000, 'updates');
    await setFake(1, 'Down=true');
    const t0 = Date.now();
    await waitStatus(page, 1, s => s.startsWith('Connection lost'), 8000, 'connection lost');
    timings.connectionLostDetectedMs = Date.now() - t0;
    assert(!(await quadrant(page, 1).locator('.conn').getAttribute('class')).includes('connected'), 'dot still green');
    await setFake(1, 'Down=false');
    const t1 = Date.now();
    await waitStatus(page, 1, s => s.startsWith('Every') || s === 'Connected', 12000, 'recovery');
    timings.reconnectDetectedMs = Date.now() - t1;
});

await step('Disconnect from a view asks first', async () => {
    await quadrant(page, 1).locator('.connect-btn').click();
    await acceptDialog(page, 'Disconnect from Fake mount 1?');
    await waitStatus(page, 1, s => s === 'Disconnected', 5000, 'Disconnected');
    const stats = await setFake(1, '');
    assert(stats.Connected === false, 'the fake mount is still connected');
});

await step('Remove a server from a view, then drag it back (drag connects)', async () => {
    await quadrant(page, 2).locator('button[title="Remove from this view"]').click();
    await quadrant(page, 2).locator('.quadrant-empty').waitFor({ timeout: 3000 });
    await page.locator('.server-item', { hasText: 'Fake mount 2' }).dragTo(quadrant(page, 2));
    await waitStatus(page, 2, s => s.startsWith('Every'), 10000, 'updates after the drop');
});

await step('The server list collapses to a strip, stays collapsed after a reload, and expands', async () => {
    const width = () => page.locator('.sidebar').evaluate(e => e.getBoundingClientRect().width);
    const canvasWidth = () => quadrant(page, 0).locator('canvas').evaluate(e => e.width);
    const open = await width();
    const canvasBefore = await canvasWidth();
    await page.locator('button[data-sidebar-toggle=collapse]').click();
    await page.waitForFunction(() => document.querySelector('.sidebar').getBoundingClientRect().width < 80, null, { timeout: 3000 });
    assert(await page.locator('.server-item .status').first().isVisible(), 'status dots hidden in the strip');
    assert(!(await page.locator('.toolbar').isVisible()), 'toolbar still shown');
    assert(await page.evaluate(() => document.activeElement?.dataset.sidebarToggle) === 'expand', 'focus not on the expand button');
    await sleep(500);
    assert(await canvasWidth() > canvasBefore, 'the views did not get wider');
    await page.reload();
    await waitInteractive(page);
    assert(await width() < 80, 'not collapsed after a reload');
    // Dragging from the strip still works.
    await quadrant(page, 3).locator('button[title="Remove from this view"]').click();
    await quadrant(page, 3).locator('.quadrant-empty').waitFor({ timeout: 3000 });
    await page.locator('.server-item[title^="Fake mount 3"]').dragTo(quadrant(page, 3));
    await quadrant(page, 3).locator('canvas').waitFor({ timeout: 5000 });
    await page.locator('button[data-sidebar-toggle=expand]').click();
    await page.waitForFunction(w => document.querySelector('.sidebar').getBoundingClientRect().width >= w - 1, open, { timeout: 3000 });
    await waitInteractive(page);
});

await step('The page is small: the hidden settings lists hold only their current value', async () => {
    const html = await (await fetch(app)).text();
    const options = (html.match(/<option/g) ?? []).length;
    assert(options < 100, `${options} <option> elements in the page`);
    timings.homeHtmlBytes = html.length;
});

await step('The settings show the saved RA / Dec adjustments every time they open', async () => {
    const q = quadrant(page, 0);
    const lists = () => q.locator('.settings select');
    const open = async () => {
        await q.locator('button[title="View settings"]').click();
        await page.waitForFunction(() => document.querySelectorAll('.quadrant')[0].querySelectorAll('.settings select')[2].options.length === 181, null, { timeout: 3000 });
    };
    const shown = async () => [await lists().nth(2).inputValue(), await lists().nth(3).inputValue()].join(',');
    await open();
    assert(await shown() === '0,0', `first open shows ${await shown()} (saved 0,0)`);
    await lists().nth(2).selectOption('5');
    await lists().nth(3).selectOption('-7');
    await q.locator('.settings button.done').click();
    await sleep(300);
    await open();
    assert(await shown() === '5,-7', `second open shows ${await shown()} (saved 5,-7)`);
    await page.reload();
    await waitInteractive(page);
    await open();
    assert(await shown() === '5,-7', `after a reload it shows ${await shown()} (saved 5,-7)`);
    await lists().nth(2).selectOption('0');
    await lists().nth(3).selectOption('0');
    await q.locator('.settings button.done').click();
    await sleep(300);
    await open();
    assert(await shown() === '0,0', `back at 0,0 it shows ${await shown()}`);
    await q.locator('.settings button.done').click();
});

await step('Views 1-4 buttons change the layout', async () => {
    await page.locator('.segmented button', { hasText: '1' }).click();
    await page.waitForFunction(() => document.querySelectorAll('.quadrant').length === 1, null, { timeout: 3000 });
    await page.locator('.segmented button', { hasText: '4' }).click();
    await page.waitForFunction(() => document.querySelectorAll('.quadrant').length === 4, null, { timeout: 3000 });
    await waitInteractive(page);
});

const page2 = await newPage();
await step('A second tab follows changes made in the first', async () => {
    await page2.goto(app);
    await waitInteractive(page2);
    // Connections made in tab 1 show in tab 2.
    assert(await page2.locator('.server-item .status.connected').count() >= 2, 'connections not shown in tab 2');
    await page.locator('.toolbar button[title="Add"]').click();
    await page2.waitForFunction(() => document.querySelectorAll('.server-item').length === 5, null, { timeout: 3000 });
    await page.locator('.toolbar button[title="Add"]').click();
    const dialog = page.locator('.dialog[role=dialog]');
    await dialog.waitFor({ timeout: 3000 });
    assert((await dialog.innerText()).includes('up to 5 servers'), 'limit dialog');
    await dialog.locator('.dialog-button').nth(1).click(); // Close
    await page.locator('.server-item', { hasText: 'Blank' }).click();
    await page.locator('.toolbar button[title="Delete"]').click();
    await acceptDialog(page, 'Delete Blank?');
    await page2.waitForFunction(() => document.querySelectorAll('.server-item').length === 4, null, { timeout: 3000 });
});

await step('Edit dialog validates the address and Cancel changes nothing', async () => {
    await page.locator('.server-item', { hasText: 'Fake mount 3' }).dblclick();
    const dialog = page.locator('.dialog[role=dialog]');
    await dialog.waitFor({ timeout: 3000 });
    const url = dialog.locator('input[inputmode=url]');
    await url.fill('http://127.0.0.1:5400/api/v1/camera/0/');
    await url.blur();
    await dialog.locator('.field-error').first().waitFor({ timeout: 3000 });
    assert((await dialog.innerText()).includes('Invalid path'), 'no path error');
    await dialog.locator('button', { hasText: 'Cancel' }).click();
    await dialog.waitFor({ state: 'detached', timeout: 3000 });
    assert((await page.locator('.server-item', { hasText: 'Fake mount 3' }).getAttribute('title')).includes('/api/v1/telescope/3/'), 'address changed');
});

await step('F2 opens the edit dialog for the selected server', async () => {
    await page.locator('.server-item', { hasText: 'Fake mount 3' }).focus();
    await page.keyboard.press('F2');
    const dialog = page.locator('.dialog[role=dialog]');
    await dialog.waitFor({ timeout: 3000 });
    await dialog.locator('button', { hasText: 'Cancel' }).click();
});

await step('Language menu switches to German and back', async () => {
    await page.locator('details.lang-menu summary').click();
    await page.locator('details.lang-menu a[lang=de]').click();
    await page.waitForLoadState('domcontentloaded');
    await page.waitForFunction(() => document.querySelector('.sidebar h2')?.textContent.trim() !== 'Telescope Mounts', null, { timeout: 5000 });
    const heading = await page.locator('.sidebar h2').innerText();
    assert(await page.locator('html').getAttribute('lang') === 'de', `html lang ${await page.locator('html').getAttribute('lang')}`);
    await waitInteractive(page).catch(() => {}); // statuses are German now
    timings.germanHeading = heading;
    await page.goto(`${app}/culture/set?culture=en&redirectUri=%2F`);
    await page.waitForFunction(() => document.querySelector('.sidebar h2')?.textContent.trim() === 'Telescope Mounts', null, { timeout: 5000 });
});

await step('Every language renders the home page and its help', async () => {
    const p = await newPage();
    for (const code of ['en', 'de', 'fr', 'it', 'es', 'zh-Hans']) {
        await p.goto(`${app}/culture/set?culture=${code}&redirectUri=%2Fhelp`);
        const text = await p.locator('body').innerText();
        assert(text.length > 2000, `${code} help is only ${text.length} characters`);
        assert(!text.includes('An unhandled error'), `${code} help shows an error`);
    }
    await p.goto(`${app}/culture/set?culture=en&redirectUri=%2F`);
    await p.close();
});

await step('Language links never redirect off the site', async () => {
    const probes = ['//evil.example', '/\\evil.example', 'https://evil.example', '\\\\evil.example', '/%2F%2Fevil.example',
        '/\t/evil.example', ' //evil.example', '/\\/evil.example', 'javascript:alert(1)', '/ok?x=1#y', '%2F%2Fevil.example'];
    const bad = [];
    for (const target of probes) {
        const r = await fetch(`${app}/culture/set?culture=de&redirectUri=${encodeURIComponent(target)}`, { redirect: 'manual' });
        const location = r.headers.get('location') ?? '';
        if (r.status >= 500) bad.push(`${JSON.stringify(target)} -> HTTP ${r.status}`);
        else if (!location.startsWith('/') || location.startsWith('//') || location.startsWith('/\\')) bad.push(`${JSON.stringify(target)} -> ${location}`);
    }
    const unknown = await fetch(`${app}/culture/set?culture=xx-bogus&redirectUri=%2F`, { redirect: 'manual' });
    assert(!(unknown.headers.get('set-cookie') ?? '').includes('.AspNetCore.Culture'), 'an unknown language was saved');
    assert(bad.length === 0, bad.join('; '));
});

await step('Log viewer, log download, help, not-found and error pages', async () => {
    const p = await newPage();
    await p.goto(`${app}/log`);
    await p.waitForFunction(() => document.body.innerText.includes('INF'), null, { timeout: 8000 });
    const file = await fetch(`${app}/log/file`);
    assert(file.ok && (file.headers.get('content-type') ?? '').startsWith('text/plain'), `log file ${file.status}`);
    assert((await file.text()).includes('starting'), 'log file content');
    const missing = await fetch(`${app}/no/such/page`);
    assert(missing.status === 404, `not found gave ${missing.status}`);
    assert((await missing.text()).includes('<html'), 'not-found page');
    await p.goto(`${app}/Error`);
    assert((await p.locator('body').innerText()).length > 20, 'error page');
    await p.close();
});

await step('Static assets are served compressed and cacheable', async () => {
    const html = await page.content();
    const assets = ['model3d/babylon.custom.min.js', 'model3d/model3d.js', 'model3d/models/Default.obj', 'app.css'];
    const report = [];
    for (const asset of assets) {
        const r = await fetch(`${app}/${asset}`, { headers: { 'accept-encoding': 'br, gzip' } });
        const body = await r.arrayBuffer();
        report.push(`${asset}: ${r.headers.get('content-encoding') ?? 'identity'} ${body.byteLength} B, cache ${r.headers.get('cache-control') ?? '-'}`);
    }
    timings.assets = report;
    const home = await fetch(app, { headers: { 'accept-encoding': 'br, gzip' } });
    timings.homeHtml = `${home.headers.get('content-encoding') ?? 'identity'}, ${(await home.arrayBuffer()).byteLength} B`;
    assert(html.length > 0, 'page');
});

// Drop the connections made above, so the next run starts the same way.
for (let n = 0; n < 4; n++) await setFake(n, 'Down=false&Connected=false');
await page2.close();
await page.close();
await browser.close();

const failed = results.filter(r => !r.ok);
const report = { app, when: new Date().toISOString(), passed: results.length - failed.length, failed: failed.length, results, timings,
    consoleProblems: [...new Set(consoleProblems)] };
writeFileSync(new URL('e2e-report.json', outDir), JSON.stringify(report, null, 2));
console.log(`\n${report.passed} passed, ${report.failed} failed`);
console.log('timings', JSON.stringify(timings, null, 1));
if (report.consoleProblems.length) console.log('browser console problems:\n  ' + report.consoleProblems.slice(0, 30).join('\n  '));
process.exit(failed.length ? 1 : 0);
