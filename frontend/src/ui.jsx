import { useEffect, useId, useRef, useState } from 'react';
import { names, safeUrl } from './api.js';

export function Field({ label, name, value = '', area, ...props }) {
  const id = useId();
  return <div className="field"><label htmlFor={id}>{label}</label>{area
    ? <textarea id={id} name={name} defaultValue={value} {...props} />
    : <input id={id} name={name} defaultValue={value} {...props} />}</div>;
}
export function Select({ label, name, value, children, ...props }) {
  const id = useId();
  return <div className="field"><label htmlFor={id}>{label}</label><select id={id} name={name} defaultValue={value} {...props}>{children}</select></div>;
}
export function Links({ links = [] }) {
  return (links || []).map((link, index) => { const url = safeUrl(link.url); return url ? <p key={index}><a href={url} target="_blank" rel="noopener noreferrer">{link.title || link.url} ↗</a> {link.description}</p> : null; });
}
export function Modal({ title, close, children }) {
  const ref = useRef(null);
  useEffect(() => { ref.current.showModal(); }, []);
  return <dialog id="dialog" ref={ref} onCancel={e => { e.preventDefault(); close(); }} onClose={close}>
    <div className="modal-head"><h2>{title}</h2><button type="button" className="close" onClick={close} aria-label="Закрыть">×</button></div>
    <div className="modal-body">{children}</div>
  </dialog>;
}
export function Dependencies({ options, boards, selected = [], exclude }) {
  const [search, setSearch] = useState('');
  return <fieldset className="dependency-picker"><legend>Начать после завершения задач</legend>
    <p className="muted">Задачи любой доски должны перейти в Done. Пока они не готовы, очередь продолжает работу.</p>
    <input type="search" value={search} onChange={e => setSearch(e.target.value)} placeholder="Найти задачу…" aria-label="Поиск задач-зависимостей" />
    <div className="dependency-options">{options.filter(t => t.id !== exclude).map(t => <label key={t.id} className="check" hidden={!t.title.toLocaleLowerCase().includes(search.toLocaleLowerCase())}>
      <input type="checkbox" name="dependencyIds" value={t.id} defaultChecked={selected.includes(t.id)} />
      <span>{t.title}<small>{boards.find(b => b.id === t.boardId)?.name || t.boardId} · {names[t.stage]}</small></span>
    </label>)}{!options.some(t => t.id !== exclude) && <p className="muted">Пока нет других задач</p>}</div>
  </fieldset>;
}
export function DependencyBadge({ task, options }) {
  const ids = task.dependencyIds || [];
  if (!ids.length) return null;
  const waiting = ids.filter(id => options.find(t => t.id === id)?.stage !== 'Done').length;
  return <span className={`badge ${waiting ? 'amber' : 'green'}`}>{waiting ? `Ждёт ${waiting} задач` : 'Зависимости выполнены'}</span>;
}
