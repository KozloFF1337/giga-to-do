using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace GigaTodo;

public sealed record AuthenticationSettings(string Command, int TimeoutSeconds);

// This is machine-local configuration, never part of a task, profile snapshot or agent prompt.
public sealed class GigacodeAuthentication
{
    private readonly string settingsPath;
    private readonly string workingDirectory;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly SemaphoreSlim executionGate = new(1, 1);
    private AuthenticationSettings settings;

    public GigacodeAuthentication(Workspace workspace, IHostEnvironment environment, IOptions<RuntimeOptions> options)
    {
        settingsPath = Path.Combine(workspace.Root, "gigacode-authentication.json");
        workingDirectory = environment.ContentRootPath;
        settings = Validate(File.Exists(settingsPath)
            ? Data.Unpack<AuthenticationSettings>(File.ReadAllText(settingsPath))
            : new(options.Value.AuthenticationCommand, options.Value.AuthenticationTimeoutSeconds));
    }

    public AuthenticationSettings Current => Volatile.Read(ref settings);

    private static AuthenticationSettings Validate(AuthenticationSettings? value)
    {
        if (value is null || value.Command is null || value.Command.Length > 8192 || value.Command.Contains('\0'))
            throw new RuleException("Команда авторизации: не более 8192 символов, без нулевого символа");
        if (value.TimeoutSeconds is < 5 or > 600) throw new RuleException("Таймаут авторизации должен быть от 5 до 600 секунд");
        return value with { Command = value.Command.Trim() };
    }

    public async Task<AuthenticationSettings> Save(AuthenticationSettings value, CancellationToken ct)
    {
        value = Validate(value);
        await saveGate.WaitAsync(ct);
        var temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, fileOptions))
                await System.Text.Json.JsonSerializer.SerializeAsync(stream, value, Data.Json, ct);
            File.Move(temporary, settingsPath, overwrite: true);
            Volatile.Write(ref settings, value);
            return value;
        }
        finally
        {
            try { File.Delete(temporary); }
            finally { saveGate.Release(); }
        }
    }

    public async Task BeforeLaunch(CancellationToken ct)
    {
        await executionGate.WaitAsync(ct);
        try
        {
            var current = Current;
            if (current.Command.Length == 0) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(current.TimeoutSeconds));
            var token = timeout.Token;
            var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh")
            {
                WorkingDirectory = workingDirectory, UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (OperatingSystem.IsWindows())
            {
                // cmd parses a shell command, not CRT argv: preserve its quotes verbatim.
                info.Arguments = "/d /s /c \"" + current.Command + "\"";
            }
            else
            {
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add(current.Command);
            }
            try
            {
                using var process = Process.Start(info) ?? throw new RuleException("Не удалось запустить скрипт авторизации Gigacode");
                using var registration = token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
                process.StandardInput.Close();
                // Auth output can contain credentials. Drain it without retaining or logging it.
                try
                {
                    await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, token),
                        process.StandardError.BaseStream.CopyToAsync(Stream.Null, token), process.WaitForExitAsync(token));
                }
                finally
                {
                    if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
                }
                if (process.ExitCode != 0)
                    throw new RuleException($"Скрипт авторизации Gigacode завершился с кодом {process.ExitCode}. Агент не запущен. Проверьте скрипт в настройках и продолжите задачу.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new RuleException($"Скрипт авторизации Gigacode превысил таймаут {current.TimeoutSeconds} с. Агент не запущен.");
            }
            catch (Win32Exception)
            {
                throw new RuleException("Не удалось запустить оболочку для скрипта авторизации Gigacode. Проверьте настройки сервера.");
            }
        }
        finally { executionGate.Release(); }
    }
}
