import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
const { chromium } = await import(process.env.GIGA_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.GIGA_TEST_URL || 'http://localhost:5180';
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
const errors = [], consoleErrors = [];
page.on('pageerror', e => errors.push(e.message));
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
const api = async (path, method = 'GET', data) => {
  const response = await fetch(base + '/api/' + path, { method, headers: data ? { 'Content-Type': 'application/json' } : {}, body: data ? JSON.stringify(data) : undefined });
  assert.ok(response.ok, `${method} ${path}: ${response.status}`);
  return response.status === 204 ? null : response.json();
};
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
try {
  await api('runtime', 'PUT', { mode: 'Disabled' });
  await page.goto(base);
  await page.getByRole('button', { name: '▧ Офис', exact: true }).click();
  await page.locator('#office-canvas[data-agents]').waitFor();
  assert.equal(await page.locator('.department-strip>div').count(), 4);
  // Actual service + Mock lifecycle, no fabricated Gigacode support.
  const { task } = await api('tasks', 'POST', { boardId: 'frontend', prompt: `Office browser ${Date.now()}`, humanReview: true });
  await api('runtime', 'PUT', { mode: 'Mock' });
  await page.getByRole('button').filter({ hasText: task.title }).waitFor();
  const snapshot = await api('office');
  assert.ok(snapshot.agents.some(a => a.taskId === task.id));
  assert.ok(snapshot.agents.every(a => !('tokenHash' in a) && !('profileJson' in a)));
  await page.getByRole('button').filter({ hasText: task.title }).click();
  await page.getByRole('dialog').getByRole('heading', { name: task.title }).waitFor();
  await page.getByRole('button', { name: 'Закрыть', exact: true }).click();
  for (let i = 0; i < 40; i++) {
    if ((await api('tasks/' + task.id)).task.stage === 'InReview') break;
    if (i === 39) throw new Error('Mock task did not reach human review');
    await wait(250);
  }
  await api('runtime', 'PUT', { mode: 'Disabled' });
  await page.getByRole('button').filter({ hasText: task.title }).getByText('Ждёт ревью человека', { exact: false }).waitFor();
  await mkdir('artifacts', { recursive: true });
  await page.screenshot({ path: 'artifacts/office-live-desktop.png', fullPage: true });
  // Fixture-only UI scale test: verifies renderer and pagination, not real agent concurrency.
  let fixtures = Array.from({ length: 100 }, (_, i) => ({
    id: `fixture-${i}`, taskId: task.id, title: `UI fixture ${i + 1}`,
    boardId: ['analytics', 'frontend', 'backend', 'testing'][i % 4], role: i % 3 === 0 ? 'Critic' : 'Executor',
    state: 'Running', runner: 'Mock', needsAttention: false, waitingForReview: false
  }));
  await page.route('**/api/office', route => route.fulfill({ json: { at: new Date().toISOString(), agents: fixtures } }));
  await page.route('**/api/runtime', route => route.fulfill({ json: { mode: 'Mock', activeAgents: 100, agentLimit: 100 } }));
  await page.reload();
  await page.getByRole('button', { name: 'Этаж 5', exact: true }).waitFor();
  await page.waitForFunction(() => document.querySelector('#office-canvas')?.dataset.active === '20');
  const before = await page.locator('#office-canvas').evaluate(c => c.toDataURL());
  // Poll across frames; comparing at a fixed multiple of the animation period
  // can land on the exact same pose and incorrectly report a frozen office.
  await page.waitForFunction(expected => document.querySelector('#office-canvas')?.toDataURL() !== expected, before, { polling: 71, timeout: 4000 });
  await page.getByRole('button', { name: 'Этаж 5', exact: true }).click();
  await page.getByText('UI fixture 100', { exact: true }).waitFor();
  assert.equal(await page.locator('.agent-row').count(), 20);
  await page.waitForFunction(() => document.querySelector('#office-canvas')?.dataset.agents === '20');
  await page.screenshot({ path: 'artifacts/office-100-fixture.png', fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), 390, 'No mobile horizontal document overflow');
  await page.screenshot({ path: 'artifacts/office-mobile.png', fullPage: true });
  // A disconnected office must visibly mark stale data and stop animation.
  await page.route('**/api/office', route => route.abort());
  await page.getByRole('alert').filter({ hasText: 'Связь потеряна' }).waitFor();
  const frozen = await page.locator('#office-canvas').evaluate(c => c.toDataURL());
  await wait(300);
  assert.equal(await page.locator('#office-canvas').evaluate(c => c.toDataURL()), frozen);
  // Asset failure is recoverable, never a silent empty canvas.
  await page.unroute('**/api/office');
  await page.route('**/api/office', route => route.fulfill({ json: { agents: fixtures } }));
  await page.route('**/pixel-agents/assets.json', route => route.fulfill({ status: 503, body: 'test outage' }));
  await page.reload();
  await page.getByRole('button', { name: 'Повторить загрузку', exact: true }).waitFor();
  await page.unroute('**/pixel-agents/assets.json');
  await page.getByRole('button', { name: 'Повторить загрузку', exact: true }).click();
  await page.locator('#office-canvas[data-agents]').waitFor();
  assert.deepEqual(errors, []);
  assert.ok(consoleErrors.every(text => /503|ERR_FAILED|Failed to load resource/.test(text)), consoleErrors.join('\n'));
  console.log('PASS: real service Mock office, task navigation, role/status display, upstream canvas animation, 100 fixture agents/five floors, mobile, offline freeze, asset error/retry.');
} catch (error) {
  console.error('Page errors:', errors, 'Console:', consoleErrors);
  await mkdir('artifacts', { recursive: true });
  await page.screenshot({ path: 'artifacts/office-failure.png', fullPage: true });
  throw error;
} finally {
  await api('runtime', 'PUT', { mode: 'Disabled' }).catch(() => {});
  await browser.close();
}
