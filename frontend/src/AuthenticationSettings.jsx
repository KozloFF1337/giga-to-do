import { useEffect, useState } from 'react';
import { api } from './api.js';
import { Field } from './ui.jsx';

export function AuthenticationSettings({ notify }) {
  const [settings, setSettings] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [reload, setReload] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setError('');
    api('runtime/authentication', 'GET', undefined, controller.signal).then(setSettings)
      .catch(error => { if (!controller.signal.aborted) setError(error.message); });
    return () => controller.abort();
  }, [reload]);
  async function submit(event) {
    event.preventDefault();
    if (busy) return;
    const form = new FormData(event.currentTarget);
    setBusy(true);
    try {
      setSettings(await api('runtime/authentication', 'PUT', {
        command: form.get('authenticationCommand'), timeoutSeconds: Number(form.get('authenticationTimeoutSeconds'))
      }));
      notify('Авторизация сохранена для всех досок');
    } catch (error) { notify(error.message); } finally { setBusy(false); }
  }
  return <section className="detail-section">
    <h3>Авторизация Gigacode · все доски</h3>
    <p className="muted">При ошибке авторизации Gigacode бэкенд запускает скрипт и один раз возобновляет прежнюю сессию. Пустая команда отключает восстановление. Сохранение настройки само скрипт не запускает.</p>
    {error ? <p role="alert">{error} <button type="button" className="secondary small" onClick={() => setReload(value => value + 1)}>Повторить загрузку</button></p>
      : !settings ? <p className="muted">Загрузка настроек авторизации…</p>
        : <form id="authentication-form" onSubmit={submit}>
          <Field label="Команда запуска скрипта авторизации" name="authenticationCommand" value={settings.command} area maxLength={8192} spellCheck={false} autoComplete="off" disabled={busy} />
          <Field label="Таймаут авторизации, секунды" name="authenticationTimeoutSeconds" value={settings.timeoutSeconds} type="number" min="5" max="600" required disabled={busy} />
          <p className="muted">Скрипт запускает бэкенд на компьютере сервиса: /bin/sh на macOS/Linux, cmd.exe на Windows. Укажите абсолютный путь; скрипт должен работать без ввода и возвращать код 0 при успехе. Одновременные ошибки используют одно успешное обновление авторизации. Вывод скрипта не сохраняется.</p>
          <div className="actions"><button type="submit" disabled={busy}>{busy ? 'Сохранение…' : 'Сохранить авторизацию'}</button></div>
        </form>}
  </section>;
}
