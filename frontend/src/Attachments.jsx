import { useEffect, useRef, useState } from 'react';
import { api } from './api.js';
const size = bytes => bytes < 1024 ? `${bytes} Б` : bytes < 1048576 ? `${(bytes / 1024).toFixed(1)} КБ` : `${(bytes / 1048576).toFixed(1)} МБ`;
const fileCount = count => `${count} ${{ one: 'файл', few: 'файла', many: 'файлов', other: 'файлов' }[new Intl.PluralRules('ru').select(count)]}`;
function ArchiveContents({ attachment, notify }) {
  const [files, setFiles] = useState(null), [loading, setLoading] = useState(false);
  async function load(e) {
    if (!e.currentTarget.open || files || loading) return;
    setLoading(true);
    try { setFiles(await api(`attachments/${attachment.id}/files`)); }
    catch (error) { notify(error.message); } finally { setLoading(false); }
  }
  return <details className="archive-files" onToggle={load}><summary>Содержимое · {fileCount(attachment.extractedFiles)}</summary>
    {loading && <p className="muted">Загрузка…</p>}
    {files && <ul>{files.length ? files.map(file => <li key={file.path}><a href={`/api/attachments/${attachment.id}/download?path=${encodeURIComponent(file.path)}`}>{file.path}</a><small>{size(file.size)}</small></li>) : <li>Архив пуст</li>}</ul>}
  </details>;
}
export function AttachmentPicker({ ids, onChange, disabled = false, initial = [], onBusy, notify }) {
  const input = useRef(null), controller = useRef(null);
  const [items, setItems] = useState(() => new Map(initial.map(a => [a.id, a])));
  const [uploading, setUploading] = useState(false), [loadError, setLoadError] = useState('');
  const signature = ids.join(',');
  useEffect(() => {
    if (!ids.length) return;
    const abort = new AbortController();
    api('attachments?ids=' + encodeURIComponent(signature), 'GET', undefined, abort.signal).then(attachments => {
      setItems(previous => new Map([...previous, ...attachments.map(a => [a.id, a])])); setLoadError('');
    }).catch(error => { if (!abort.signal.aborted) setLoadError(error.message); });
    return () => abort.abort();
  }, [signature]);
  useEffect(() => () => controller.current?.abort(), []);
  async function upload(e) {
    const files = [...e.target.files]; e.target.value = '';
    if (!files.length) return;
    if (files.length > 20 || ids.length + files.length > 50) { notify('До 20 файлов за загрузку и до 50 вложений у задачи'); return; }
    setUploading(true); onBusy?.(true); controller.current = new AbortController();
    const form = new FormData(); files.forEach(file => form.append('files', file));
    try {
      const result = await api('attachments', 'POST', form, controller.current.signal);
      setItems(previous => new Map([...previous, ...result.attachments.map(a => [a.id, a])]));
      onChange([...ids, ...result.attachments.map(a => a.id)]);
      if (result.attachments.some(a => a.state === 'Failed')) notify('Оригиналы загружены. Некоторые архивы не распакованы — см. пояснения у файлов.');
    } catch (error) { if (error.name !== 'AbortError') notify(error.message); }
    finally { setUploading(false); onBusy?.(false); }
  }
  return <div className="attachments-picker">
    <input ref={input} type="file" multiple className="sr-only" tabIndex={-1} aria-label="Файлы задачи" onChange={upload} disabled={disabled || uploading} />
    <button type="button" className="secondary" disabled={disabled || uploading || ids.length >= 50} onClick={() => input.current.click()}>{uploading ? 'Загружаем и распаковываем…' : '📎 Прикрепить файлы'}</button>
    <p className="muted attachment-hint">Любые типы файлов. Архивы распаковываются автоматически; оригиналы сохраняются.</p>
    {loadError && <p className="notice error">Не удалось загрузить список: {loadError}</p>}
    <div className="attachment-list">{ids.map(id => { const a = items.get(id); return <div className="attachment-item" key={id}>
      <div className="attachment-top"><span className="attachment-icon">{a?.isArchive ? '▣' : '↳'}</span><div className="attachment-name">{a ? <a href={`/api/attachments/${id}/download`}>{a.fileName}</a> : <span>Загрузка сведений…</span>}<small>{a && `${size(a.size)} · ${a.state === 'Extracted' ? `Распаковано: ${fileCount(a.extractedFiles)}` : a.state === 'Failed' ? 'Не удалось распаковать' : 'Файл загружен'}`}</small></div>
        <button type="button" className="secondary small" disabled={disabled || uploading} aria-label={`Убрать вложение ${a?.fileName || id}`} onClick={() => onChange(ids.filter(value => value !== id))}>×</button></div>
      {a?.error && <p className="attachment-error" role="status">{a.error} Оригинал доступен для скачивания.</p>}
      {a?.state === 'Extracted' && <ArchiveContents attachment={a} notify={notify} />}
    </div>; })}</div>
  </div>;
}
export function TaskAttachments({ task, attachments, save, notify, busy }) {
  const [ids, setIds] = useState(task.attachmentIds || []), [uploading, setUploading] = useState(false);
  const changed = ids.join(',') !== (task.attachmentIds || []).join(',');
  return <section className="detail-section" id="task-attachments"><h3>Вложения</h3>
    <AttachmentPicker ids={ids} onChange={setIds} initial={attachments} disabled={busy || !!task.activeRunId} onBusy={setUploading} notify={notify} />
    <p className="muted">{task.activeRunId ? 'Изменение вложений доступно после завершения или отмены запуска.' : 'Новые файлы будут доступны следующему запуску агента после сохранения.'}</p>
    {changed && <button className="secondary small" disabled={busy || uploading || !!task.activeRunId} onClick={() => save('attachments', { attachmentIds: ids })}>Сохранить вложения</button>}
  </section>;
}
