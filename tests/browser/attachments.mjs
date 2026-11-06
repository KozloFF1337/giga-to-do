import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
const { chromium } = await import(process.env.GIGA_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.GIGA_TEST_URL || 'http://localhost:5180';
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
const errors = []; page.on('pageerror', e => errors.push(e.message));
let taskPosts = 0;
page.on('request', request => { if (request.method() === 'POST' && new URL(request.url()).pathname === '/api/tasks') taskPosts++; });
const api = async (path, method = 'GET', data) => {
  const response = await fetch(base + '/api/' + path, { method, headers: data ? { 'Content-Type': 'application/json' } : {}, body: data ? JSON.stringify(data) : undefined });
  assert.ok(response.ok, `${method} ${path}: ${response.status} ${response.ok ? '' : await response.text()}`);
  return response.status === 204 ? null : response.json();
};
const zip = Buffer.from('UEsDBBQAAAAIAGpUZl20ei22FQAAABYAAAAVAAAAZG9jcy9yZXF1aXJlbWVudHMudHh0SywpSUzOyE3NK1EoSi0szSxKBbEBUEsBAhQDFAAAAAgAalRmXbR6LbYVAAAAFgAAABUAAAAAAAAAAAAAAIABAAAAAGRvY3MvcmVxdWlyZW1lbnRzLnR4dFBLBQYAAAAAAQABAEMAAABIAAAAAAA=', 'base64');
const stamp = Date.now();
try {
  await api('runtime', 'PUT', { mode: 'Disabled' });
  // This file exceeds the application's normal 2 MiB JSON request limit.
  const large = new FormData(); large.append('files', new Blob([new Uint8Array(3 * 1024 * 1024)]), 'arbitrary.bin');
  const largeResponse = await fetch(base + '/api/attachments', { method: 'POST', body: large });
  assert.equal(largeResponse.status, 200);
  assert.equal((await largeResponse.json()).attachments[0].size, 3 * 1024 * 1024);
  await page.goto(base);
  await page.getByRole('button', { name: '＋ Новая задача', exact: true }).click();
  await page.getByLabel('Описание задачи · промпт').fill(`Attachment UI ${stamp}`);
  await page.route('**/api/attachments', async route => { await new Promise(resolve => setTimeout(resolve, 700)); await route.continue(); });
  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: '📎 Прикрепить файлы', exact: true }).click();
  await (await chooser).setFiles([
    { name: 'requirements.zip', mimeType: 'application/zip', buffer: zip },
    { name: 'image.bin', mimeType: 'application/octet-stream', buffer: Buffer.from([0, 255, 17, 34]) }
  ]);
  await page.getByRole('button', { name: 'Загружаем и распаковываем…', exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Создать', exact: true }).isDisabled(), true);
  await page.locator('#task-form').evaluate(form => form.requestSubmit());
  assert.equal(taskPosts, 0, 'No task can enter the queue before attachment processing completes');
  await page.getByText('Распаковано: 1 файл', { exact: false }).waitFor();
  await page.unroute('**/api/attachments');
  await page.getByRole('button', { name: 'Создать', exact: true }).click();
  await page.locator('.card').filter({ hasText: `Attachment UI ${stamp}` }).click();
  await page.locator('#task-attachments').getByRole('link', { name: 'requirements.zip', exact: true }).waitFor();
  let task = (await api('tasks?boardId=analytics')).find(t => t.prompt === `Attachment UI ${stamp}`);
  let detail = await api('tasks/' + task.id);
  assert.equal(detail.attachments.length, 2);
  const archive = detail.attachments.find(a => a.fileName === 'requirements.zip');
  assert.equal(archive.state, 'Extracted');
  assert.ok(archive.originalPath && archive.contentPath);
  assert.deepEqual(Buffer.from(await (await fetch(base + `/api/attachments/${archive.id}/download`)).arrayBuffer()), zip);
  await page.getByText('Содержимое · 1 файл', { exact: true }).click();
  await page.getByRole('link', { name: 'docs/requirements.txt', exact: true }).waitFor();
  assert.equal(await (await fetch(base + `/api/attachments/${archive.id}/download?path=docs%2Frequirements.txt`)).text(), 'attachment requirement');
  assert.equal((await fetch(base + `/api/attachments/${archive.id}/download?path=..%2Foriginal%2Ffile-requirements.zip`)).status, 400);
  await page.locator('#task-attachments input[type=file]').setInputFiles({ name: 'broken.rar', mimeType: 'application/octet-stream', buffer: Buffer.from('invalid archive') });
  await page.getByText('Не удалось распаковать', { exact: false }).first().waitFor();
  await page.getByRole('button', { name: 'Сохранить вложения', exact: true }).click();
  await page.getByRole('button', { name: 'Сохранить вложения', exact: true }).waitFor({ state: 'hidden' });
  detail = await api('tasks/' + task.id); assert.equal(detail.attachments.length, 3);
  assert.equal(detail.attachments.find(a => a.fileName === 'broken.rar').state, 'Failed');
  await page.getByRole('button', { name: 'Убрать вложение image.bin', exact: true }).click();
  await page.getByRole('button', { name: 'Сохранить вложения', exact: true }).click();
  await page.getByRole('button', { name: 'Сохранить вложения', exact: true }).waitFor({ state: 'hidden' });
  detail = await api('tasks/' + task.id); assert.equal(detail.attachments.length, 2);
  const template = await api('schedules', 'POST', { boardId: 'analytics', prompt: `Attached schedule ${stamp}`, daysInterval: 1, hoursInterval: 0, days: [0, 1, 2, 3, 4, 5, 6], startAt: new Date(Date.now() + 86400000).toISOString(), timeZone: 'Europe/Moscow', attachmentIds: [archive.id] });
  await page.getByRole('button', { name: 'Закрыть', exact: true }).click();
  const row = page.locator('#schedules tr').filter({ hasText: `Attached schedule ${stamp}` });
  await row.getByRole('button', { name: 'Изменить', exact: true }).click();
  await page.getByRole('link', { name: 'requirements.zip', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await page.getByRole('dialog').waitFor({ state: 'hidden' });
  assert.deepEqual((await api('schedules?boardId=analytics')).find(s => s.id === template.id).attachmentIds, [archive.id]);
  await page.locator('.card').filter({ hasText: `Attachment UI ${stamp}` }).click();
  await mkdir('artifacts', { recursive: true });
  await page.screenshot({ path: 'artifacts/attachments-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), 390);
  await page.screenshot({ path: 'artifacts/attachments-mobile.png', fullPage: true });
  assert.deepEqual(errors, []);
  console.log('PASS: file chooser, arbitrary binary >2 MiB, ZIP extraction/download, task create/add/remove, failed archive/original, schedule edit preserves files, desktop/mobile.');
} catch (error) {
  console.error('Browser errors:', errors);
  await mkdir('artifacts', { recursive: true });
  await page.screenshot({ path: 'artifacts/attachments-failure.png', fullPage: true });
  throw error;
} finally { await api('runtime', 'PUT', { mode: 'Disabled' }).catch(() => {}); await browser.close(); }
