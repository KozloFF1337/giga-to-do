import { useRef, useState } from 'react';
import { api, date, days, formRequest, localDate, parse } from './api.js';
import { Dependencies, Field, Select } from './ui.jsx';
import { AttachmentPicker } from './Attachments.jsx';
import { AuthenticationSettings } from './AuthenticationSettings.jsx';

export function TaskForm({ board, boards, options, schedule, recurring, close, saved, notify }) {
  const ref = useRef(null);
  const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState([]);
  const s = schedule || {};
  const [attachmentIds, setAttachmentIds] = useState(() => s.attachmentIds || []);
  const [uploading, setUploading] = useState(false);
  async function submit(e) {
    e.preventDefault();
    if (busy || uploading) return;
    setBusy(true);
    try {
      const request = formRequest(ref.current, board, s);
      request.attachmentIds = attachmentIds;
      const path = recurring ? `schedules${s.id ? `/${s.id}?version=${s.version}` : ''}` : 'tasks';
      const result = await api(path, s.id ? 'PUT' : 'POST', request);
      saved(); close(); notify(result?.created === false ? 'Такая задача уже существует' : 'Сохранено');
    } catch (error) { notify(error.message); } finally { setBusy(false); }
  }
  async function showPreview() {
    try { setPreview(await api('schedules/preview', 'POST', formRequest(ref.current, board, s))); }
    catch (error) { notify(error.message); }
  }
  return <form id="task-form" ref={ref} data-schedule={recurring} onSubmit={submit}>
    <Field label="Описание задачи · промпт" name="prompt" value={s.prompt} area required maxLength={100000} />
    <Field label="Название · необязательно" name="title" value={s.title} />
    <Field label="Критерии готовности" name="criteria" value={s.criteria} area />
    <section className="attachment-form"><h3>Вложения</h3><AttachmentPicker ids={attachmentIds} onChange={setAttachmentIds} disabled={busy} onBusy={setUploading} notify={notify} /></section>
    <div className="form-grid"><Field label="Источник · необязательно" name="source" value={s.source || ''} />
      <Select label="Кто проверяет результат" name="humanReview" value={String(s.humanReview || false)}><option value="false">Агент-критик</option><option value="true">Человек</option></Select></div>
    <Dependencies options={options} boards={boards} selected={s.dependencyIds} />
    <Field label="Ссылки · по одному URL на строку" name="links" value={parse(s.linksJson).map(l => l.url).join('\n')} area />
    <label className="check"><input type="checkbox" name="overrideRepos" defaultChecked={!!s.id} /> Задать репозитории вместо настроек доски</label>
    <Field label="Пути репозиториев · по одному на строку; пусто — без репозитория" name="repositories" value={parse(s.repositoriesJson).map(r => r.path).join('\n')} area />
    {recurring && <>
      <div className="form-grid"><Field label="Интервал: дни" name="daysInterval" value={Math.floor((s.intervalHours || 24) / 24)} type="number" min="0" max="3650" />
        <Field label="Интервал: часы" name="hoursInterval" value={(s.intervalHours || 24) % 24} type="number" min="0" max="87600" />
        <Field label="Первый слот" name="startAt" value={localDate(s.startAt || new Date(Date.now() + 3600000))} type="datetime-local" required />
        <Field label="Часовой пояс" name="timeZone" value={s.timeZone || 'Europe/Moscow'} /></div>
      <p className="muted">Дата вводится в часовом поясе браузера. Фильтр дней применяется в указанном часовом поясе.</p>
      <div className="days">{[1, 2, 3, 4, 5, 6, 0].map(d => <label key={d}><input type="checkbox" name="day" value={d} defaultChecked={(s.days || [1, 2, 3, 4, 5]).includes(d)} />{days[d]}</label>)}</div>
      <p className="actions start"><button type="button" className="secondary small" onClick={() => ref.current.querySelectorAll('input[name=day]').forEach(i => { i.checked = [1, 2, 3, 4, 5].includes(Number(i.value)); })}>Будни</button>
        <button type="button" className="secondary small" onClick={showPreview}>Ближайшие запуски</button></p>
      <div id="preview" className="preview">{preview.map(s => <div key={s}>{date(s)}</div>)}</div>
    </>}
    <div className="actions"><button type="button" className="secondary" disabled={busy || uploading} onClick={close}>Отмена</button><button type="submit" disabled={busy || uploading}>{busy ? 'Сохранение…' : s.id ? 'Сохранить' : 'Создать'}</button></div>
  </form>;
}

function Repository({ value, remove }) {
  return <div className="repo"><Field label="Локальный путь к репозиторию" name="repoPath" value={value.path} placeholder="Абсолютный путь на компьютере бэкенда" required />
    <Field label="Базовая ветка" name="repoBase" value={value.baseBranch || 'HEAD'} />
    <div className="form-grid"><Field label="Команда frontend" name="repoCommand" value={value.frontendCommand} /><Field label="Локальный URL" name="repoLocal" value={value.localUrl} /></div>
    <Field label="API стенда" name="repoApi" value={value.apiUrl} /><Field label="Ссылка на дизайн" name="repoDesign" value={value.designUrl} />
    <button type="button" className="danger small" onClick={remove}>Убрать</button></div>;
}
export function Settings({ board, close, saved, notify }) {
  const [repos, setRepos] = useState(() => parse(board.defaultRepositoriesJson).map(r => ({ ...r, key: crypto.randomUUID() })));
  const [catalog, setCatalog] = useState(null);
  const [busy, setBusy] = useState(false);
  async function submit(e) {
    e.preventDefault(); setBusy(true);
    const form = e.currentTarget, f = new FormData(form);
    const defaultRepositories = [...form.querySelectorAll('.repo-fields .repo')].map(el => {
      const get = name => el.querySelector(`[name=${name}]`).value;
      return { path: get('repoPath'), baseBranch: get('repoBase'), frontendCommand: get('repoCommand'), localUrl: get('repoLocal'), apiUrl: get('repoApi'), designUrl: get('repoDesign') };
    });
    try {
      await api('boards/' + board.id, 'PUT', { version: board.version, executorLimit: Number(f.get('executorLimit')), criticLimit: Number(f.get('criticLimit')), reworkLimit: Number(f.get('reworkLimit')), model: f.get('model'), approvalMode: f.get('approvalMode'), executorInstructions: f.get('executorInstructions'), criticInstructions: f.get('criticInstructions'), defaultRepositories });
      saved(); close(); notify('Настройки сохранены');
    } catch (error) { notify(error.message); } finally { setBusy(false); }
  }
  return <><AuthenticationSettings notify={notify} /><form id="settings-form" onSubmit={submit}>
    <h3>Настройки доски</h3>
    <p><button type="button" className="secondary small" onClick={async () => { try { setCatalog(await api('agent-catalog')); } catch (error) { notify(error.message); } }}>Навыки и сценарии агентов</button></p>
    {catalog && <div id="agent-matrix"><p className="muted">Каталог {catalog.catalog.version} · {catalog.hash.slice(0, 12)}. Внешние навыки требуют настройки в Gigacode.</p>
      {catalog.catalog.profiles.filter(p => p.boardId === board.id).map(p => <details key={p.id} className="repo"><summary>{p.title} · {p.id}</summary>{p.scenarioIds.map(id => {
        const scenario = catalog.catalog.scenarios.find(s => s.id === id);
        return <div className="detail-section" key={id}><strong>{id}</strong><p>{scenario.when}</p><p>Порядок: {scenario.required.join(' → ')}</p><p className="muted">По необходимости: {scenario.optional.join(', ') || '—'}</p><p className="muted">{scenario.gate}</p></div>;
      })}</details>)}</div>}
    <div className="form-grid"><Field label="Исполнители" name="executorLimit" value={board.executorLimit} type="number" min="1" max="100" required />
      <Field label="Критики" name="criticLimit" value={board.criticLimit} type="number" min="1" max="100" required />
      <Field label="Лимит возвратов" name="reworkLimit" value={board.reworkLimit} type="number" min="1" max="20" required />
      <Field label="Модель · пусто — настройки Gigacode" name="model" value={board.model} /></div>
    <Select label="Режим подтверждений Gigacode" name="approvalMode" value={board.approvalMode}>{['default', 'auto-edit', 'auto', 'plan'].map(m => <option key={m}>{m}</option>)}</Select>
    <p className="muted">Headless требует заранее настроенных разрешений. AUTO доступен не во всех окружениях.</p>
    <Field label="Инструкции исполнителя" name="executorInstructions" value={board.executorInstructions} area />
    <Field label="Инструкции критика" name="criticInstructions" value={board.criticInstructions} area />
    <h3>Репозитории по умолчанию</h3><div className="repo-fields">{repos.map(r => <Repository key={r.key} value={r} remove={() => setRepos(list => list.filter(item => item.key !== r.key))} />)}</div>
    <button type="button" className="secondary" onClick={() => setRepos(list => [...list, { key: crypto.randomUUID() }])}>＋ Репозиторий</button>
    <div className="actions"><button type="button" className="secondary" onClick={close}>Отмена</button><button type="submit" disabled={busy}>Сохранить</button></div>
  </form></>;
}
export function ScheduleHistory({ slots, openTask }) {
  return slots.length ? slots.map(s => <p key={s.id}>{date(s.at)} · {s.state === 'created' ? 'Создан экземпляр' : 'Пропущено во время простоя'} {s.taskId && <button className="secondary small" onClick={() => openTask(s.taskId)}>Открыть</button>}</p>) : <p className="muted">Слотов пока нет</p>;
}
