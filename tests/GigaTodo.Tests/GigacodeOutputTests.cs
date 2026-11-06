using GigaTodo;
using Xunit;

namespace GigaTodo.Tests;

public class GigacodeOutputTests
{
    [Theory]
    [InlineData("{\"type\":\"error\",\"error\":{\"status\":401}}", true)]
    [InlineData("{\"type\":\"error\",\"message\":\"Unauthorized\"}", true)]
    [InlineData("{\"type\":\"result\",\"is_error\":true,\"error\":{\"code\":\"TOKEN_EXPIRED\"}}", true)]
    [InlineData("{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"error\":\"Authentication required\"}", true)]
    [InlineData("{\"error\":{\"code\":\"invalid_grant\"}}", false)]
    [InlineData("Error: Your OAuth token has expired. Please log in.", false)]
    public void RecognizesCliAuthenticationDiagnostics(string line, bool stdout)
    {
        var output = new GigacodeOutput(null);
        output.Observe(line, stdout);
        Assert.True(output.AuthenticationFailed);
    }

    [Theory]
    [InlineData("{\"type\":\"assistant\",\"message\":\"Authentication failed; invalid token\"}", true)]
    [InlineData("{\"type\":\"tool_result\",\"error\":{\"status\":401}}", true)]
    [InlineData("{\"type\":\"error\",\"error\":{\"status\":403,\"message\":\"Forbidden\"}}", true)]
    [InlineData("{\"type\":\"result\",\"is_error\":false,\"message\":\"Please log in\"}", true)]
    [InlineData("Authentication failed is the text in this task", true)]
    [InlineData("Error: rate limit exceeded (429)", false)]
    public void DoesNotTreatTaskContentOrOtherFailuresAsAuthentication(string line, bool stdout)
    {
        var output = new GigacodeOutput(null);
        output.Observe(line, stdout);
        Assert.False(output.AuthenticationFailed);
    }

    [Fact]
    public void RetainsNewestSessionAndDetectsActivityBeforeFailure()
    {
        var output = new GigacodeOutput("old-session");
        output.Observe("{\"type\":\"system\",\"session_id\":\"current-session\"}", true);
        Assert.False(output.HasActivity);
        output.Observe("{\"type\":\"assistant\",\"message\":\"Working\"}", true);
        output.Observe("{\"type\":\"error\",\"error\":{\"status\":401}}", true);
        Assert.True(output.HasActivity);
        Assert.True(output.AuthenticationFailed);
        Assert.Equal("current-session", output.Session);
    }

    [Theory]
    [InlineData("unexpected output")]
    [InlineData("{\"type\":\"custom_fork_event\"}")]
    [InlineData("[]")]
    public void UnknownStdoutIsNotProofThatFreshRetryIsSafe(string line)
    {
        var output = new GigacodeOutput(null);
        output.Observe(line, true);
        Assert.True(output.HasActivity);
    }
}
