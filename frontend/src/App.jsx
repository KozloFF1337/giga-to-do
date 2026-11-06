import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { api, date, days, names, stages } from './api.js';
import { DependencyBadge, Modal } from './ui.jsx';
import { ScheduleHistory, Settings, TaskForm } from './Forms.jsx';
import { TaskDetail } from './TaskDetail.jsx';
const Office = lazy(() => import('./Office.jsx'));
const empty = { boards: [], tasks: [], schedules: [], options: [], office: [], runtime: { mode: 'Disabled', activeAgents: 0, agentLimit: null } };
function remembered(key, fallback) { try { return localStorage.getItem(key) || fallback; } catch { return fallback; } }
function remember(key, value) { try { localStorage.setItem(key, value); } catch { /* storage is optional */ } }

export default function App() {
  const [boardId, setBoardId] = useState(() => remembered('board', 'analytics'));
  const [view, setView] = useState(() => remembered('view', 'board') === 'office' ? 'office' : 'board');
  const [data, setData] = useState(empty);
  const [connection, setConnection] = useState('Подключение…');
  const [modal, setModal] = useState(null);
  const [toast, setToast] = useState('');
  const [drag, setDrag] = useState(null);
  const [refreshTick, setRefreshTick] = useState(0);
  const [changingMode, setChangingMode] = useState(false);
  const dragging = useRef(false);
  const generation = useRef(0);
  const notify = useCallback(text => setToast(String(text)), []);
  const refresh = useCallback(() => setRefreshTick(n => n + 1), []);
  useEffect(() => { if (!toast) return; const timer = setTimeout(() => setToast(''), 6000); return () => clearTimeout(timer); }, [toast]);
  useEffect(() => {
    const controller = new AbortController(), current = ++generation.current;
    let pending = false;
    const load = async () => {
      if (pending || dragging.current) return; pending = true;
      try {
        const [boards, tasks, schedules, runtime, options, office] = await Promise.all([
          api('boards', 'GET', undefined, controller.signal), api('tasks?boardId=' + encodeURIComponent(boardId), 'GET', undefined, controller.signal),
          api('schedules?boardId=' + encodeURIComponent(boardId), 'GET', undefined, controller.signal), api('runtime', 'GET', undefined, controller.signal),
          api('task-options', 'GET', undefined, controller.signal), api('office', 'GET', undefined, controller.signal)
        ]);
        if (current !== generation.current || controller.signal.aborted) return;
        if (!boards.some(b => b.id === boardId) && boards.length) { setBoardId(boards[0].id); remember('board', boards[0].id); return; }
        setData({ boards, tasks, schedules, runtime, options, office: office.agents }); setConnection('Подключено');
      } catch (error) { if (!controller.signal.aborted) setConnection('Нет связи'); }
      finally { pending = false; }
    };
    load();
    const events = new EventSource('/api/events');
    events.addEventListener('refresh', load);
    events.onerror = () => { if (!controller.signal.aborted) setConnection('Переподключение…'); };
    // Polling also recovers when an intermediary buffers SSE.
    const timer = setInterval(load, 5000);
    return () => { controller.abort(); events.close(); clearInterval(timer); };
  }, [boardId, refreshTick]);
  const board = data.boards.find(b => b.id === boardId);
  const openTask = useCallback(id => setModal({ type: 'task', id }), []);
  const close = useCallback(() => setModal(null), []);
  const switchView = value => { setView(value); remember('view', value); };
  function switchBoard(id) { dragging.current = false; setDrag(null); setBoardId(id); setData(d => ({ ...d, tasks: [], schedules: [] })); remember('board', id); }
  async function modeChanged(mode) {
    if (mode !== 'Disabled' && !confirm(mode === 'Mock' ? 'Включить тестовый режим? Он автоматически принимает задачи без реальной работы.' : 'Запускать Gigacode для доступных задач? Убедитесь, что CLI, навыки и разрешения настроены.')) return;
    setChangingMode(true);
    try { await api('runtime', 'PUT', { mode }); refresh(); }
    catch (error) { notify(error.message); } finally { setChangingMode(false); }
  }
  async function reorder(task, delta) {
    const ids = data.tasks.filter(t => t.stage === task.stage).map(t => t.id), i = ids.indexOf(task.id), j = i + delta;
    if (j < 0 || j >= ids.length) return;
    [ids[i], ids[j]] = [ids[j], ids[i]];
    try { await api('tasks/reorder', 'POST', { boardId, stage: task.stage, ids }); refresh(); } catch (error) { notify(error.message); }
  }
  function startDrag(e, task) {
    dragging.current = true; e.dataTransfer.setData('text/plain', task.id); e.dataTransfer.effectAllowed = 'move';
    setDrag({ id: task.id, stage: task.stage, boardId, ids: data.tasks.filter(t => t.stage === task.stage).map(t => t.id) });
  }
  function over(e, task) {
    if (!drag || task.stage !== drag.stage || task.id === drag.id) return;
    e.preventDefault();
    const ids = drag.ids.filter(id => id !== drag.id), box = e.currentTarget.getBoundingClientRect();
    ids.splice(ids.indexOf(task.id) + (e.clientY >= box.top + box.height / 2 ? 1 : 0), 0, drag.id);
    if (ids.join() !== drag.ids.join()) setDrag({ ...drag, ids });
  }
  async function drop(e, stage) {
    if (!drag || stage !== drag.stage) return; e.preventDefault(); const pending = drag;
    setDrag(null); dragging.current = false;
    try { await api('tasks/reorder', 'POST', { boardId: pending.boardId, stage, ids: pending.ids }); } catch (error) { notify(error.message); }
    refresh();
  }
  async function scheduleAction(action, schedule) {
    try {
      if (action === 'edit') setModal({ type: 'schedule', board, schedule });
      else if (action === 'toggle') { await api(`schedules/${schedule.id}/toggle`, 'POST', { version: schedule.version }); refresh(); }
      else setModal({ type: 'history', slots: await api(`schedules/${schedule.id}/history`) });
    } catch (error) { notify(error.message); }
  }
  const titles = { task: 'Задача', new: 'Новая задача', schedule: modal?.schedule ? 'Изменить повторение' : 'Повторяющаяся задача', settings: 'Настройки · ' + modal?.board?.name, history: 'История повторений' };
  return <>
    <header className="topbar"><a className="brand" href="/"><span className="brand-mark">G<span>·</span></span> Giga To Do</a><span className="subtitle">AGENT WORKSPACE</span>
      <div className="view-switch" aria-label="Представление"><button className={view === 'board' ? 'active' : ''} aria-pressed={view === 'board'} onClick={() => switchView('board')}>▦ Доски</button><button className={view === 'office' ? 'active' : ''} aria-pressed={view === 'office'} onClick={() => switchView('office')}>▧ Офис</button></div>
      <div className="runtime"><span id="connection" className={`connection ${connection === 'Подключено' ? '' : 'offline'}`}>● {connection}</span><span className="agent-meter">Агенты <strong id="agent-count">{data.runtime.activeAgents} / {data.runtime.agentLimit ?? '—'}</strong></span>
        <label htmlFor="runner">Исполнение <select id="runner" value={data.runtime.mode} disabled={changingMode || !board} onChange={e => modeChanged(e.target.value)}><option value="Disabled">Выключено</option><option value="Mock">Тестовый режим</option><option value="Gigacode">Gigacode</option></select></label></div>
    </header>
    <main>
      <div id="mode-warning" className="notice" hidden={data.runtime.mode === 'Gigacode'}>{data.runtime.mode === 'Mock' ? 'Тестовый режим: карточки проходят стадии автоматически. Реальная работа и проверки не выполняются.' : 'Исполнение выключено. Готовьте задачи и расписания — команда начнёт работу после включения.'}</div>
      {view === 'office' ? <Suspense fallback={<div className="empty">Загрузка офиса…</div>}><Office agents={data.office} boards={data.boards} runtime={data.runtime} connected={connection === 'Подключено'} openTask={openTask} /></Suspense> : <>
        <nav id="boards" aria-label="Доски">{data.boards.map((b, i) => <button key={b.id} data-board={b.id} className={b.id === boardId ? 'active' : ''} aria-pressed={b.id === boardId} onClick={() => switchBoard(b.id)}><span className="tab-number" aria-hidden="true">0{i + 1}</span>{b.name}</button>)}</nav>
        <section className="board-heading"><div><p className="eyebrow">ВАША КОМАНДА · ОДНО ПРОСТРАНСТВО</p><h1 id="board-title">{board?.name || 'Задачи'}</h1><p className="muted">Вы задаёте приоритет — агенты делают остальное.</p></div>
          <div className="toolbar"><button id="settings" className="secondary" disabled={!board} onClick={() => setModal({ type: 'settings', board })}>Настройки доски</button><button id="new-task" disabled={!board} onClick={() => setModal({ type: 'new', board })}>＋ Новая задача</button></div></section>
        <section className="stats" aria-label="Состояние доски">{[['Всего задач', data.tasks.length, 'в этой команде', 'stat-total'], ['Агенты работают', data.tasks.filter(t => t.activeRunId).length, 'активных запусков', 'stat-work'], ['На проверке', data.tasks.filter(t => t.stage === 'InReview').length, 'результат ждёт решения', 'stat-review'], ['Готово', data.tasks.filter(t => t.stage === 'Done').length, 'результат принят', 'stat-done']].map(([title, count, caption, id]) => <div key={id}><span>{title}</span><strong id={id}>{count}</strong><small>{caption}</small></div>)}</section>
        <section id="kanban" className="kanban" aria-label="Доска задач">{stages.map(stage => {
          const tasks = drag?.stage === stage ? drag.ids.map(id => data.tasks.find(t => t.id === id)).filter(Boolean) : data.tasks.filter(t => t.stage === stage);
          return <section className="column" key={stage} data-stage={stage}><div className="column-head"><span className="dot" />{names[stage]}<span className="count">{tasks.length}</span></div>
            <div className="cards" data-stage={stage} onDragOver={e => { if (drag?.stage === stage) e.preventDefault(); }} onDrop={e => drop(e, stage)}>
              {tasks.map(t => <article key={t.id} data-task={t.id} data-stage={stage} className={`card ${t.humanReview ? 'human' : ''} ${t.stage === 'ToDo' && t.dependencyIds.some(id => data.options.find(o => o.id === id)?.stage !== 'Done') ? 'waiting' : ''} ${drag?.id === t.id ? 'dragging' : ''}`} draggable tabIndex={0} role="button" aria-label={t.title}
                onClick={e => { if (!dragging.current && !e.target.closest('button')) openTask(t.id); }} onKeyDown={e => { if (e.target === e.currentTarget && ['Enter', ' '].includes(e.key)) { e.preventDefault(); openTask(t.id); } }}
                onDragStart={e => startDrag(e, t)} onDragOver={e => over(e, t)} onDragEnd={() => { dragging.current = false; setDrag(null); refresh(); }}>
                <span className="card-code">{t.id.slice(0, 8).toUpperCase()}</span><h3>{t.title}</h3><div className="card-bottom"><DependencyBadge task={t} options={data.options} />{t.humanReview && <span className="badge red">Ревью человеком</span>}{t.activeRunId && <span className="badge green">Агент работает</span>}{t.paused && <span className="badge amber">Пауза</span>}{t.blocker && <span className="badge red">Требуется внимание</span>}<div className="arrows"><button aria-label="Выше" onClick={() => reorder(t, -1)}>↑</button><button aria-label="Ниже" onClick={() => reorder(t, 1)}>↓</button></div></div>
              </article>)}{!tasks.length && <div className="empty"><span className="empty-symbol">{stage === 'Done' ? '✓' : '·'}</span>Здесь пока нет задач</div>}</div>
            <div className="queue-note">{stage === 'Done' ? 'Результат принят' : '↑ Верхняя доступная — следующая'}</div></section>;
        })}</section>
        <section className="recurring"><div className="section-heading"><div><p className="eyebrow">ПО РАСПИСАНИЮ</p><h2>Повторяющиеся задачи</h2></div><button id="new-schedule" className="secondary" disabled={!board} onClick={() => setModal({ type: 'schedule', board })}>＋ Создать повторение</button></div>
          <div className="table-wrap"><table><thead><tr><th>Задача</th><th>Периодичность</th><th>Дни</th><th>Следующий слот</th><th>Состояние</th><th><span className="sr-only">Действия</span></th></tr></thead><tbody id="schedules">{data.schedules.length ? data.schedules.map(s => <tr key={s.id}><td className="schedule-title">{s.title}</td><td>Каждые {Math.floor(s.intervalHours / 24)} д. {s.intervalHours % 24} ч.</td><td>{s.days.map(d => days[d]).join(', ')}</td><td>{date(s.nextAt)}<br /><span className="muted">{s.timeZone}</span></td><td><span className={`badge ${s.enabled ? 'green' : ''}`}>{s.enabled ? 'Включено' : 'Пауза'}</span></td><td className="table-actions"><button className="secondary small" onClick={() => scheduleAction('edit', s)}>Изменить</button><button className="secondary small" onClick={() => scheduleAction('toggle', s)}>{s.enabled ? 'Пауза' : 'Включить'}</button><button className="secondary small" onClick={() => scheduleAction('history', s)}>История</button></td></tr>) : <tr><td colSpan="6" className="empty">Создайте повторение — задачи будут появляться по расписанию.</td></tr>}</tbody></table></div></section>
      </>}
      <footer>Giga To Do <span>Прогресс и решения сохраняются в карточках</span></footer>
    </main>
    {modal && <Modal key={modal.type + (modal.id || modal.schedule?.id || '')} title={titles[modal.type]} close={close}>
      {['new', 'schedule'].includes(modal.type) && <TaskForm board={modal.board} boards={data.boards} options={data.options} recurring={modal.type === 'schedule'} schedule={modal.schedule} close={close} saved={refresh} notify={notify} />}
      {modal.type === 'settings' && <Settings board={modal.board} close={close} saved={refresh} notify={notify} />}
      {modal.type === 'task' && <TaskDetail id={modal.id} options={data.options} boards={data.boards} openTask={openTask} saved={refresh} notify={notify} />}
      {modal.type === 'history' && <ScheduleHistory slots={modal.slots} openTask={openTask} />}
    </Modal>}
    <div id="toast" role="status" hidden={!toast}>{toast}</div>
  </>;
}
