using System.Text.Json;
using System.Text.RegularExpressions;

namespace GigaTodo;

// Inspect CLI diagnostics, never assistant/tool text containing arbitrary task content.
public sealed partial class GigacodeOutput(string? initialSession)
{
    private string? session = initialSession;
    private int authenticationFailed;
    private int activity;
    public string? Session => Volatile.Read(ref session);
    public bool AuthenticationFailed => Volatile.Read(ref authenticationFailed) != 0;
    public bool HasActivity => Volatile.Read(ref activity) != 0;

    public void Observe(string line, bool stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                if (stdout) Interlocked.Exchange(ref activity, 1);
                return;
            }
            if (stdout && root.TryGetProperty("session_id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
                Volatile.Write(ref session, id.GetString());
            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            // Unknown output is treated as possible work, never as proof of a safe fresh retry.
            if (stdout && type is not ("system" or "init" or "status" or "error" or "result"))
                Interlocked.Exchange(ref activity, 1);
            var failed = type == "error" || (type == "result" &&
                ((root.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True) ||
                 (root.TryGetProperty("subtype", out var subtype) && subtype.ValueKind == JsonValueKind.String && subtype.GetString()!.StartsWith("error", StringComparison.OrdinalIgnoreCase))));
            if (stdout && type == "result" && !failed) Interlocked.Exchange(ref activity, 1);
            if ((failed || (!stdout && root.TryGetProperty("error", out _))) && IsAuthenticationError(root)) Interlocked.Exchange(ref authenticationFailed, 1);
        }
        catch (JsonException)
        {
            if (stdout) Interlocked.Exchange(ref activity, 1);
            if (!stdout && AuthenticationMessage().IsMatch(line)) Interlocked.Exchange(ref authenticationFailed, 1);
        }
    }

    private static bool IsAuthenticationError(JsonElement value, int depth = 0)
    {
        if (depth > 4 || value.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is "code" or "status" or "status_code" or "statusCode")
            {
                var code = property.Value.ToString();
                if (code is "401" or "UNAUTHENTICATED" or "AUTHENTICATION_ERROR" or "AUTHENTICATION_FAILED" or "TOKEN_EXPIRED" or "INVALID_TOKEN" or "invalid_grant") return true;
            }
            if (property.Name is "message" or "error" or "reason" or "error_description")
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    (AuthenticationMessage().IsMatch(property.Value.GetString()!) || StructuredAuthenticationMessage().IsMatch(property.Value.GetString()!))) return true;
                if (IsAuthenticationError(property.Value, depth + 1)) return true;
            }
        }
        return false;
    }

    [GeneratedRegex(@"\b(?:unauthenticated|authentication\s+(?:failed|failure|required|expired|error)|error\s+authenticating|not\s+logged\s+in|(?:oauth|access|refresh|auth(?:entication)?)\s+token\s+(?:(?:has|is)\s+)?(?:expired|invalid)|(?:invalid|expired)\s+(?:(?:oauth|access|refresh|auth)\s+)?(?:token|credentials)|invalid_grant|please\s+(?:log\s*in|sign\s*in|authenticate))\b|(?:авторизаци[яи]\s+(?:истекла|требуется)|(?:ист[её]к|недействителен)\s+(?:срок\s+действия\s+)?токен)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthenticationMessage();

    [GeneratedRegex(@"\bunauthori[sz]ed\b|\b(?:HTTP|status(?:\s+code)?)\s*[:=]?\s*401\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StructuredAuthenticationMessage();
}
