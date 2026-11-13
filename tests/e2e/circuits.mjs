// Load test with real browser tabs (Blazor circuits), each showing the four views with live mounts, against the
// stack from start-stack.sh. Measures the app's CPU and memory (read from /proc in WSL), the requests the mounts
// get, the SignalR traffic per tab, and whether everything stops when tabs close normally or the browser dies.
//
//   node circuits.mjs [tabs=1,5,10,20] [appUrl] [fakeUrl]
import { chromium } from 'playwright';
import { execFileSync } from 'node:child_process';
import { writeFileSync, mkdirSync } from 'node:fs';

const steps = (process.argv[2] ?? '1,5,10,20').split(',').map(Number);
const app = process.argv[3] ?? 'http://localhost:5300';
const fake = process.argv[4] ?? 'http://localhost:5400';
const outDir = new URL('./out/', import.meta.url);
mkdirSync(outDir, { recursive: true });
const sleep = ms => new Promise(r => setTimeout(r, ms));

const wsl = cmd => execFileSync('wsl', ['-e', 'bash', '-c', cmd], { encoding: 'utf8' }).trim();
const appPid = wsl('cat ~/gsp3w-run/app.pid');
const ticks = Number(wsl('getconf CLK_TCK'));
function appSample() {
    const out = wsl(`cat /proc/${appPid}/stat; grep VmRSS /proc/${appPid}/status`);
    const stat = out.split(/\r?\n/)[0];
    const f = stat.slice(stat.lastIndexOf(')') + 2).split(' ');
    const rss = /VmRSS:\s+(\d+)/.exec(out)?.[1];
    return { t: Date.now(), cpu: (Number(f[11]) + Number(f[12])) / ticks, rssMb: Number(rss) / 1024 };
}
async function deviceStateCalls() {
    const stats = await (await fetch(`${fake}/fake/stats`)).json();
    return stats.reduce((sum, m) => sum + (m.Calls.devicestate ?? 0), 0);
}
async function totalCalls() {
    const stats = await (await fetch(`${fake}/fake/stats`)).json();
    return stats.reduce((sum, m) => sum + Object.values(m.Calls).reduce((a, b) => a + b, 0), 0);
}
async function rate(seconds = 5) {
    const a = await totalCalls(), d0 = await deviceStateCalls();
    await sleep(seconds * 1000);
    return { all: (await totalCalls() - a) / seconds, devicestate: (await deviceStateCalls() - d0) / seconds };
}

async function waitInteractive(page) {
    await page.waitForFunction(() => {
        const s = [...document.querySelectorAll('.quadrant .status-text')].map(e => e.textContent.trim());
        return s.length === 4 && s.every(t => t && t !== 'Ready' && !t.startsWith('Initializing') && !t.startsWith('Loading'));
    }, null, { timeout: 60000 });
}

/** A tab with SignalR frame counting and a frame-rate probe. */
async function openTab(context) {
    const page = await context.newPage();
    const ws = { frames: 0, bytes: 0 };
    page.on('websocket', socket => socket.on('framereceived', f => { ws.frames++; ws.bytes += (f.payload?.length ?? 0); }));
    await page.addInitScript(() => {
        window.__frames = 0;
        const tick = () => { window.__frames++; requestAnimationFrame(tick); };
        requestAnimationFrame(tick);
        // Count the frames Babylon really draws: each view calls scene.render() via engine.runRenderLoop.
        window.__draws = 0;
        const original = WebGL2RenderingContext.prototype.drawElements;
        WebGL2RenderingContext.prototype.drawElements = function (...a) { window.__draws++; return original.apply(this, a); };
    });
    await page.goto(app);
    await waitInteractive(page);
    return { page, ws };
}

const browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--enable-gpu', '--ignore-gpu-blocklist'] });
const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
const report = { app, when: new Date().toISOString(), idle: {}, steps: [], cleanup: {} };

// Baseline with no tab open.
let s0 = appSample();
await sleep(5000);
let s1 = appSample();
report.idle = { cpuPct: +((s1.cpu - s0.cpu) / ((s1.t - s0.t) / 1000) * 100).toFixed(1), rssMb: +s1.rssMb.toFixed(0), mountRequests: await rate(3) };
console.log('idle', report.idle);

// The first tab connects the four mounts; every view in every tab then polls (connections are shared).
const tabs = [await openTab(context)];
for (let i = 0; i < 4; i++) {
    const q = tabs[0].page.locator('.quadrant').nth(i);
    if (!(await q.locator('.connect-btn').getAttribute('class')).includes('connected')) await q.locator('.connect-btn').click();
}
await tabs[0].page.waitForFunction(() => [...document.querySelectorAll('.status-text')].every(e => e.textContent.startsWith('Every')), null, { timeout: 15000 });

console.log('tabs  views  mount req/s (devicestate)  app CPU %core  RSS MB  ws frames/s/tab  ws KB/s/tab  rAF fps  GL draws/s  JS heap MB');
for (const n of steps) {
    while (tabs.length < n) tabs.push(await openTab(context));
    await sleep(4000);
    const probe = tabs[0];
    const f0 = { frames: probe.ws.frames, bytes: probe.ws.bytes };
    const p0 = await probe.page.evaluate(() => ({ raf: window.__frames, draws: window.__draws }));
    const a = appSample();
    const r = await rate(10);
    const b = appSample();
    const p1 = await probe.page.evaluate(() => ({ raf: window.__frames, draws: window.__draws, heap: performance.memory?.usedJSHeapSize ?? 0 }));
    const row = {
        tabs: n, views: n * 4, mountRequestsPerSec: r.all, devicestatePerSec: r.devicestate,
        cpuPctOfCore: +((b.cpu - a.cpu) / ((b.t - a.t) / 1000) * 100).toFixed(1), rssMb: +b.rssMb.toFixed(0),
        wsFramesPerSecPerTab: +((probe.ws.frames - f0.frames) / 10).toFixed(1), wsKBPerSecPerTab: +((probe.ws.bytes - f0.bytes) / 10 / 1024).toFixed(2),
        rafFps: +((p1.raf - p0.raf) / 10).toFixed(1), glDrawsPerSec: +((p1.draws - p0.draws) / 10).toFixed(0), jsHeapMb: +(p1.heap / 1048576).toFixed(1)
    };
    report.steps.push(row);
    console.log(`${String(n).padStart(4)}  ${String(n * 4).padStart(5)}  ${(r.all.toFixed(0) + ' (' + r.devicestate.toFixed(0) + ')').padStart(24)}  ${String(row.cpuPctOfCore).padStart(13)}  ${String(row.rssMb).padStart(6)}  ${String(row.wsFramesPerSecPerTab).padStart(15)}  ${String(row.wsKBPerSecPerTab).padStart(11)}  ${String(row.rafFps).padStart(7)}  ${String(row.glDrawsPerSec).padStart(10)}  ${String(row.jsHeapMb).padStart(10)}`);
}

// Closing tabs normally: their views must stop polling straight away.
while (tabs.length > 1) await tabs.pop().page.close({ runBeforeUnload: true });
await sleep(3000);
report.cleanup.afterClosingTabs = await rate(5);
console.log('after closing all but one tab (expect ~8 devicestate/s):', report.cleanup.afterClosingTabs);

// Open and close a tab 30 times: nothing should be left polling or growing.
const before = appSample();
for (let i = 0; i < 30; i++) {
    const t = await openTab(context);
    await t.page.close({ runBeforeUnload: true });
}
await sleep(3000);
report.cleanup.after30OpenClose = { ...(await rate(5)), rssMbBefore: +before.rssMb.toFixed(0), rssMbAfter: +appSample().rssMb.toFixed(0) };
console.log('after opening and closing 30 tabs:', report.cleanup.after30OpenClose);

// A browser that dies (no goodbye from its tabs): how long do its views keep polling?
const server = await chromium.launchServer({ channel: 'chrome', headless: true });
const doomed = await chromium.connect(server.wsEndpoint());
const doomedContext = await doomed.newContext();
for (let i = 0; i < 5; i++) await openTab(doomedContext);
await sleep(2000);
const withDoomed = await rate(3);
server.process().kill('SIGKILL');
const killedAt = Date.now();
console.log(`5 tabs in a second browser: ${withDoomed.devicestate} devicestate/s; browser killed`);
const timeline = [];
for (let t = 0; t < 240; t += 15) {
    const r = await rate(5);
    timeline.push({ secondsAfterKill: Math.round((Date.now() - killedAt) / 1000), devicestatePerSec: r.devicestate });
    console.log(`  ${timeline.at(-1).secondsAfterKill} s after the kill: ${r.devicestate} devicestate/s`);
    if (r.devicestate <= 9) break;
    await sleep(10000);
}
report.cleanup.afterBrowserKilled = timeline;

await tabs[0].page.close({ runBeforeUnload: true });
await browser.close();
await sleep(3000);
s0 = appSample();
await sleep(5000);
s1 = appSample();
report.cleanup.idleAtEnd = { cpuPct: +((s1.cpu - s0.cpu) / ((s1.t - s0.t) / 1000) * 100).toFixed(1), rssMb: +s1.rssMb.toFixed(0), mountRequests: await rate(5) };
console.log('idle at the end (no tabs; the mounts are still connected, so nothing should poll):', report.cleanup.idleAtEnd);
writeFileSync(new URL('circuits-report.json', outDir), JSON.stringify(report, null, 2));
