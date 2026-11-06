using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GigaTodo;

public static partial class LogRedactor
{
    public static string Redact(string text, string runToken)
    {
        text = text.Replace(runToken, "[REDACTED]", StringComparison.Ordinal);
        try
        {
            var node = JsonNode.Parse(text);
            Clean(node);
            text = node?.ToJsonString() ?? text;
        }
        catch (System.Text.Json.JsonException) { }
        return Secrets().Replace(text, "$1[REDACTED]");
    }

    private static void Clean(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(k => k.Key).ToArray())
            {
                var normalized = key.Replace("_", "").Replace("-", "").ToLowerInvariant();
                if (normalized is "authorization" or "password" or "apikey" or "accesstoken" or "refreshtoken" or "token" or "secret" or "cookie" or "setcookie") obj[key] = "[REDACTED]";
                else Clean(obj[key]);
            }
        }
        else if (node is JsonArray array) foreach (var value in array) Clean(value);
    }

    [GeneratedRegex("((?:Bearer\\s+)|(?:(?:password|api[_-]?key|access[_-]?token|refresh[_-]?token|secret)\\s*[:=]\\s*[\"']?))[^\\s\"',;]+", RegexOptions.IgnoreCase)]
    private static partial Regex Secrets();
}
