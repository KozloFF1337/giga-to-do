export async function api(path, method = 'GET', data, signal) {
  const response = await fetch('/api/' + path, {
    method, signal, headers: data === undefined || data instanceof FormData ? {} : { 'Content-Type': 'application/json' },
    body: data === undefined ? undefined : data instanceof FormData ? data : JSON.stringify(data)
  });
  if (!response.ok) {
    let message;
    try { message = (await response.json()).error; } catch { /* HTTP fallback */ }
    throw new Error(message || `Ошибка ${response.status}`);
  }
  return response.status === 204 ? null : response.json();
}
export const parse = (text, fallback = []) => { try { return JSON.parse(text); } catch { return fallback; } };
export const date = value => new Date(value).toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'short' });
export const stages = ['ToDo', 'InWork', 'InReview', 'Done'];
export const names = { ToDo: 'To Do', InWork: 'In Work', InReview: 'In Review', Done: 'Done' };
export const days = ['Вс', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];
export function safeUrl(value) {
  try { const url = new URL(value); return ['http:', 'https:'].includes(url.protocol) ? url.href : null; }
  catch { return null; }
}
export function localDate(value) {
  const d = new Date(value);
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
}
export function formRequest(form, board, schedule) {
  const f = new FormData(form);
  const lines = name => String(f.get(name) || '').split('\n').map(v => v.trim()).filter(Boolean);
  const defaults = [...parse(schedule?.repositoriesJson), ...parse(board.defaultRepositoriesJson)];
  const result = {
    boardId: board.id, prompt: f.get('prompt'), title: f.get('title') || null,
    criteria: f.get('criteria') || '', source: f.get('source') || null,
    dependencyIds: f.getAll('dependencyIds'), humanReview: f.get('humanReview') === 'true',
    links: lines('links').map(url => ({ title: url, url }))
  };
  if (f.has('overrideRepos')) result.repositories = lines('repositories').map(path => defaults.find(r => r.path === path) || { path, baseBranch: 'HEAD' });
  if (form.dataset.schedule === 'true') Object.assign(result, {
    daysInterval: Number(f.get('daysInterval')), hoursInterval: Number(f.get('hoursInterval')),
    days: f.getAll('day').map(Number), startAt: new Date(f.get('startAt')).toISOString(), timeZone: f.get('timeZone')
  });
  return result;
}
