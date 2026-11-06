import { useEffect, useMemo, useRef, useState } from 'react';
import { agentStatus, allocateSeats, roles, SEATS_PER_FLOOR } from './office-model.js';
import { loadPixelAssets, mountOffice } from './pixel-engine.ts';

function OfficeCanvas({ agents, connected, openTask, retry }) {
  const canvas = useRef(null), engine = useRef(null), latest = useRef({ agents, connected });
  const [error, setError] = useState(''), [ready, setReady] = useState(false);
  latest.current = { agents, connected };
  useEffect(() => {
    let disposed = false;
    loadPixelAssets().then(() => {
      if (disposed) return;
      engine.current = mountOffice(canvas.current, openTask); engine.current.sync(latest.current.agents, latest.current.connected); setReady(true);
    }).catch(e => { if (!disposed) setError(e.message); });
    return () => { disposed = true; engine.current?.dispose(); engine.current = null; };
  }, [openTask]);
  useEffect(() => { engine.current?.sync(agents, connected); }, [agents, connected]);
  return <div className="office-scene"><canvas id="office-canvas" ref={canvas} aria-label="Pixel Agents: офис исполнителей и критиков. Для клавиатуры используйте список запусков рядом." role="img" />
    {!ready && <div className="office-loading">{error ? <><p>{error}</p><button onClick={retry}>Повторить загрузку</button></> : 'Загрузка Pixel Agents…'}</div>}</div>;
}
export default function Office({ agents, boards, runtime, connected, openTask }) {
  const [floor, setFloor] = useState(0), [retry, setRetry] = useState(0);
  const allocation = useRef(new Map());
  const slotted = useMemo(() => {
    allocation.current = allocateSeats(allocation.current, agents);
    return agents.map(a => ({ ...a, slot: allocation.current.get(a.id) }));
  }, [agents]);
  const floorCount = Math.max(1, ...slotted.map(a => Math.floor(a.slot / SEATS_PER_FLOOR) + 1));
  const selected = Math.min(floor, floorCount - 1);
  const visible = slotted.filter(a => Math.floor(a.slot / SEATS_PER_FLOOR) === selected);
  return <section className="office-page">
    <div className="board-heading"><div><p className="eyebrow">PIXEL AGENTS · ЖИВАЯ КОМАНДА</p><h1>Команда в работе</h1><p className="muted">Исполнители и критики всех досок в одном пространстве.</p></div>
      <div className="office-live"><span className={`live-dot ${connected ? '' : 'offline'}`} /><strong>{runtime.activeAgents}</strong><span>из {runtime.agentLimit ?? '—'} работают</span></div></div>
    <div className="department-strip">{boards.map((b, i) => <div key={b.id}><span className="department-number">0{i + 1}</span><span>{b.name}</span><strong>{agents.filter(a => a.boardId === b.id && a.state === 'Running').length}</strong></div>)}</div>
    {!connected && <div className="notice error" role="alert">Связь потеряна. Показано последнее полученное состояние; анимация приостановлена.</div>}
    <div className="office-layout"><section className="office-stage"><div className="office-stage-head"><span><span className="live-dot" /> {connected ? 'Состояние запусков' : 'Последний снимок'}</span>
      <div className="floor-picker" aria-label="Этажи">{Array.from({ length: floorCount }, (_, i) => <button key={i} className={i === selected ? 'active' : ''} aria-pressed={i === selected} onClick={() => setFloor(i)}>Этаж {i + 1}</button>)}</div></div>
      <OfficeCanvas key={selected + ':' + retry} agents={visible} connected={connected} openTask={openTask} retry={() => setRetry(n => n + 1)} />
      <div className="office-caption"><span>Нажмите на персонажа, чтобы открыть задачу</span><a href="/pixel-agents/LICENSE.txt" target="_blank" rel="noreferrer">Pixel Agents · MIT ↗</a></div>
    </section>
      <aside className="office-roster"><div className="section-heading"><div><p className="eyebrow">ЭТАЖ {selected + 1}</p><h2>Запуски <span className="count">{visible.length}</span></h2></div></div>
        <div className="roster-list">{visible.map(agent => { const status = agentStatus(agent); return <button className="agent-row" key={agent.id} onClick={() => openTask(agent.taskId)}><span className={`agent-avatar ${agent.role === 'Critic' ? 'critic' : ''}`}>{agent.role === 'Critic' ? 'К' : 'И'}</span><span className="agent-info"><strong>{roles[agent.role]}</strong><small>{boards.find(b => b.id === agent.boardId)?.name} · {agent.taskId.slice(0, 8)}</small><span className="agent-task">{agent.title}</span><span className={`badge ${status.tone}`}>{status.label}{agent.runner === 'Mock' ? ' · Mock' : ''}</span></span><span className="agent-open">↗</span></button>; })}
          {!visible.length && <div className="office-empty"><span>☕</span><h3>Пока тихо</h3><p>Когда сервис запустит задачу, её агент появится здесь и займёт компьютер.</p></div>}</div>
        <p className="roster-note">Завершённые запуски видны ещё 45 секунд. Анимация отражает статус процесса; чтение файлов и вызовы инструментов Gigacode пока не отслеживаются.</p>
      </aside></div>
  </section>;
}
