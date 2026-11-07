import { useCallback, useEffect, useRef, useState } from 'react';
import { api, date, names, parse } from './api.js';
import { Dependencies, DependencyBadge, Links } from './ui.jsx';
import { TaskAttachments } from './Attachments.jsx';

export function TaskDetail({ id, options, boards, openTask, saved, notify }) {
  const [detail, setDetail] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [comment, setComment] = useState('');
  const [target, setTarget] = useState('Executor');
  const dependencies = useRef(null);
  const history = useRef(null);
  const load = useCallback(async signal => {
    try { const result = await api('tasks/' + id, 'GET', undefined, signal); if (!signal?.aborted) { setDetail(result); setError(''); } }
    catch (e) { if (!signal?.aborted) setError(e.message); }
  }, [id]);
  useEffect(() => {
    const controller = new AbortController(); let pending = false;
    const refresh = async () => { if (pending) return; pending = true; await load(controller.signal); pending = false; };
    refresh(); const timer = setInterval(refresh, 2000);
    return () => { controller.abort(); clearInterval(timer); };
  }, [load]);
  const lastEvent = detail?.events.at(-1)?.id;
  useEffect(() => { if (history.current) history.current.scrollTop = history.current.scrollHeight; }, [lastEvent]);
  if (!detail) return <p className={error ? 'notice error' : 'muted'}>{error || 'Загрузка задачи…'}</p>;
  const { task: t, events, children, runs } = detail;
  const result = parse(t.resultJson, {});
  async function action(name, data = {}) {
    if (busy) return; setBusy(true);
    try {
      await api(`tasks/${id}/${name}`, 'POST', { version: t.version, ...data });
      if (name === 'review' || name === 'messages') setComment('');
      if (name === 'reset-sessions') notify('Сессии сброшены. Для запуска нажмите «Продолжить».');
      if (name === 'dependencies') notify('Зависимости сохранены');
      await load(); saved();
    } catch (error) { notify(error.message); await load(); } finally { setBusy(false); }
  }
  return <>
    {error && <p className="notice error">Обновление недоступно: {error}</p>}
    <div id="task-meta"><div className="detail-meta"><span className="badge">{names[t.stage]}</span><span className="badge">{boards.find(b => b.id === t.boardId)?.name}</span>
      <DependencyBadge task={t} options={options} />{t.humanReview && <span className="badge red">Ревью человеком</span>}{t.paused && <span className="badge amber">Пауза</span>}</div>
      {t.source && <p className="muted">Источник: {t.source}</p>}{t.blocker && <div className="notice error">{t.blocker}</div>}</div>
    <div className="detail-section"><h2>{t.title}</h2><p className="card-code">{id}</p><div className="prose">{t.prompt}</div>
      {t.criteria && <><h3>Критерии готовности</h3><div className="prose">{t.criteria}</div></>}<Links links={parse(t.linksJson)} /></div>
    <TaskAttachments key={(t.attachmentIds || []).join(',')} task={t} attachments={detail.attachments || []} save={action} notify={notify} busy={busy} />
    <div id="task-dependencies" className="detail-section" ref={dependencies}>
      {t.stage === 'ToDo' && !t.activeRunId ? <><Dependencies key={JSON.stringify(t.dependencyIds)} options={options} boards={boards} selected={t.dependencyIds} exclude={id} />
        <button className="secondary small" disabled={busy} onClick={() => action('dependencies', { dependencyIds: [...dependencies.current.querySelectorAll('input[name=dependencyIds]:checked')].map(i => i.value) })}>Сохранить зависимости</button></>
        : <><h3>Начать после</h3>{t.dependencyIds.length ? t.dependencyIds.map(depId => { const dep = options.find(o => o.id === depId); return <p key={depId}><button className="secondary small" onClick={() => openTask(depId)}>{dep?.title || depId} · {names[dep?.stage] || 'Недоступна'}</button></p>; }) : <p className="muted">Без зависимостей</p>}</>}
    </div>
    {result.summary && <div id="task-result" className="detail-section"><h3>Результат</h3><div className="prose">{result.summary}</div><Links links={result.links} />
      {result.checks?.map((c, i) => <p className="muted" key={i}>• {c}</p>)}{result.artifacts?.map((a, i) => <p key={i}><a href={`/api/tasks/${id}/artifact?path=${encodeURIComponent(a.path)}`}>Скачать: {a.title}</a></p>)}</div>}
    <div id="task-actions" className="actions">
      {t.activeRunId ? <button className="danger" disabled={busy} onClick={() => action('cancel')}>Отменить запуск</button> : t.stage !== 'Done' && <>
        <button className="secondary" disabled={busy} onClick={() => action(t.paused || t.blocker ? 'resume' : 'pause')}>{t.paused || t.blocker ? 'Продолжить / повторить' : 'Приостановить'}</button>
        {(t.paused || t.blocker) && <button className="secondary" disabled={busy} onClick={() => action('reset-sessions')}>Сбросить сессии</button>}</>}
      {t.stage === 'InReview' && t.humanReview && !t.activeRunId && <><button className="danger" disabled={busy} onClick={() => action('review', { accepted: false, comment })}>На доработку</button><button disabled={busy} onClick={() => action('review', { accepted: true, comment })}>Принять</button></>}
    </div>
    {t.stage !== 'Done' && <div className="detail-section"><h3><label htmlFor="comment">Ответ или замечания</label></h3><textarea id="comment" value={comment} onChange={e => setComment(e.target.value)} placeholder="Пояснение исполнителю, вопрос критику или замечания при возврате" />
      <div className="actions"><select id="message-target" aria-label="Получатель сообщения" value={target} onChange={e => setTarget(e.target.value)}><option value="Executor">Исполнителю</option><option value="Critic">Критику</option></select>
        <button className="secondary" disabled={busy || !comment.trim()} onClick={() => action('messages', { text: comment, target })}>Отправить</button></div></div>}
    <div className="detail-section"><h3>Связанные задачи</h3>{children.length ? children.map(c => <p key={c.id}><button className="secondary small" onClick={() => openTask(c.id)}>{c.title} · {names[c.stage]}</button></p>) : <p className="muted">Нет связанных задач</p>}</div>
    <details className="detail-section"><summary>Запуски · {runs.length}</summary>{runs.map(r => <p key={r.id} className="muted">{r.role} · {r.runner} · {r.state} · {date(r.startedAt)}{r.sessionId && ` · ${r.sessionId}`}</p>)}</details>
    <div className="detail-section"><h3>История и журналы</h3><div id="task-events" className="history" ref={history}>{events.map(e => <div className="event" key={e.id}><small>{date(e.at)} · {e.author} · {e.kind}</small><pre>{e.text}</pre></div>)}</div></div>
  </>;
}
