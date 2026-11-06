using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using GigaTodo;
using Xunit;
using Xunit.Sdk;

namespace GigaTodo.Tests;

[CollectionDefinition("RealGigacode", DisableParallelization = true)]
public sealed class RealGigacodeCollection;

/// <summary>Real CLI acceptance checks: no Mock, fixture, skip, database or application server.</summary>
[Collection("RealGigacode")]
[Trait("Category", "RealGigacode")]
public class RealGigacodeTests
{
    [Fact]
    public async Task CliIsAvailableAndSupportsApplicationFlags()
    {
        var result = await Run(NewWorkspace(), ["--help"]);
        foreach (var flag in new[] { "--prompt", "--output-format", "--resume", "--approval-mode", "--max-wall-time", "--max-session-turns", "--max-tool-calls" })
            Assert.Contains(flag, result.Stdout + result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HeadlessTextActuallyReturnsModelAnswer()
    {
        var marker = "GIGA_CONNECTION_" + Guid.NewGuid().ToString("N");
        var result = await Run(NewWorkspace(), PromptArguments(Instruction(marker), "text"));
        Assert.Contains(marker, result.Stdout.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task StreamJsonContainsSessionAndAssistantResult()
    {
        var marker = "GIGA_STREAM_" + Guid.NewGuid().ToString("N");
        var result = await Run(NewWorkspace(), PromptArguments(Instruction(marker), "stream-json"));
        var stream = ParseStream(result);
        Assert.False(string.IsNullOrWhiteSpace(stream.SessionId), "stream-json не содержит верхнеуровневый session_id, ожидаемый приложением.");
        Assert.Contains(marker, stream.AssistantText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResumeByExactIdRestoresPreviousConversation()
    {
        var cwd = NewWorkspace();
        var secret = "GIGA_MEMORY_" + Guid.NewGuid().ToString("N");
        var first = ParseStream(await Run(cwd, PromptArguments(Instruction(secret) + " Запомни эту контрольную строку для следующего сообщения в этой сессии.", "stream-json")));
        Assert.False(string.IsNullOrWhiteSpace(first.SessionId), "Нет session_id для проверки --resume.");
        Assert.Contains(secret, first.AssistantText, StringComparison.Ordinal);
        var arguments = PromptArguments("Это продолжение проверки связи. Верни только контрольную строку GIGA_MEMORY_ из предыдущего сообщения этой сессии. Не придумывай новую. Не вызывай инструменты, не читай файлы и не меняй их.", "stream-json");
        arguments.AddRange(["--resume", first.SessionId!]);
        var resumed = ParseStream(await Run(cwd, arguments));
        Assert.False(string.IsNullOrWhiteSpace(resumed.SessionId), "Продолжение не вернуло session_id.");
        Assert.Contains(secret, resumed.AssistantText, StringComparison.Ordinal);
    }

    private static string Instruction(string marker) => "Это минимальная проверка подключения к модели, не задача разработки. Не вызывай инструменты, MCP, shell или субагентов. Не читай и не меняй файлы. Ответь одной строкой, без Markdown: " + marker;

    private static string NewWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "giga-live-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int TimeoutSeconds()
    {
        var value = Environment.GetEnvironmentVariable("GIGA_TEST_TIMEOUT_SECONDS");
        if (value is null) return 180;
        if (!int.TryParse(value, out var seconds) || seconds is < 10 or > 600)
            throw new XunitException("GIGA_TEST_TIMEOUT_SECONDS должен быть целым числом от 10 до 600.");
        return seconds;
    }

    private static List<string> PromptArguments(string prompt, string format)
    {
        var arguments = new List<string> { "-p", prompt, "-o", format, "--approval-mode", "default", "--max-wall-time", TimeoutSeconds() + "s", "--max-session-turns", "4", "--max-tool-calls", "20" };
        var model = Environment.GetEnvironmentVariable("GIGA_TEST_MODEL");
        if (!string.IsNullOrWhiteSpace(model)) arguments.AddRange(["--model", model]);
        return arguments;
    }

    private static async Task<CliOutput> Run(string cwd, IEnumerable<string> arguments)
    {
        var executable = Environment.GetEnvironmentVariable("GIGA_CLI") ?? "gigacode";
        if (Path.GetFileName(executable).Equals("gigacode-contract.py", StringComparison.OrdinalIgnoreCase))
            throw new XunitException("RealGigacode требует реальный CLI. Тестовая contract fixture здесь запрещена.");
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = cwd, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        Process process;
        try { process = Process.Start(info) ?? throw new XunitException("Gigacode не запущен."); }
        catch (Win32Exception exception)
        {
            throw new XunitException($"Gigacode недоступен: не удалось запустить '{executable}'. Установите CLI, проверьте PATH или задайте GIGA_CLI полным путём к нему. Причина: {exception.Message}");
        }
        using (process)
        {
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds() + 15));
            try { await process.WaitForExitAsync(budget.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw new XunitException("Gigacode не завершился в пределах таймаута. Проверьте авторизацию, доступ к модели, MCP discovery и интерактивные подтверждения.\n" + Diagnostic(await error));
            }
            var result = new CliOutput(await output, await error);
            Assert.True(process.ExitCode == 0, $"Gigacode завершился с кодом {process.ExitCode}. Проверьте доступ к модели и совместимость CLI.\n{Diagnostic(result.Stderr)}\n{Diagnostic(result.Stdout)}");
            return result;
        }
    }

    private static string Diagnostic(string text)
    {
        var clean = string.Join('\n', text.Split('\n').Select(line => LogRedactor.Redact(line, "__unused_test_token__")));
        return clean[..Math.Min(clean.Length, 6000)];
    }

    private static StreamResult ParseStream(CliOutput result)
    {
        string? session = null;
        var assistant = new List<string>();
        var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            JsonDocument json;
            try { json = JsonDocument.Parse(line); }
            catch (JsonException) { throw new XunitException("stream-json содержит строку не в формате JSON: " + Diagnostic(line)); }
            using (json)
            {
                var root = json.RootElement;
                Assert.Equal(JsonValueKind.Object, root.ValueKind);
                if (root.TryGetProperty("session_id", out var id) && id.ValueKind == JsonValueKind.String)
                    session ??= id.GetString();
                if (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() is "assistant" or "result")
                {
                    if (root.TryGetProperty("is_error", out var failure) && failure.ValueKind == JsonValueKind.True)
                        throw new XunitException("Gigacode сообщил ошибку: " + Diagnostic(line));
                    ExtractText(root, assistant);
                }
            }
        }
        Assert.NotEmpty(assistant); // User prompts and tool output cannot satisfy the answer check.
        return new(session, string.Join('\n', assistant));
    }

    private static void ExtractText(JsonElement value, List<string> text)
    {
        if (value.ValueKind == JsonValueKind.String) { text.Add(value.GetString()!); return; }
        if (value.ValueKind == JsonValueKind.Array) { foreach (var child in value.EnumerateArray()) ExtractText(child, text); return; }
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var field in new[] { "text", "result", "message", "content" })
            if (value.TryGetProperty(field, out var child)) ExtractText(child, text);
    }

    private sealed record CliOutput(string Stdout, string Stderr);
    private sealed record StreamResult(string? SessionId, string AssistantText);
}
