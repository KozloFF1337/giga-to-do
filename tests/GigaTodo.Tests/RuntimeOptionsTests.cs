using GigaTodo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GigaTodo.Tests;

public class RuntimeOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidLimitsAreRejected(int limit)
    {
        var services = new ServiceCollection();
        services.AddOptions<RuntimeOptions>().Configure(o => o.MaxConcurrentAgents = limit)
            .Validate(RuntimeOptions.ValidConcurrency, "Limit must be 1–100");
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RuntimeOptions>>().Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(100)]
    public void ConfigurationBindsCustomLimit(int limit)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Runtime:MaxConcurrentAgents"] = limit.ToString() }).Build();
        var options = configuration.GetSection("Runtime").Get<RuntimeOptions>()!;
        Assert.Equal(limit, options.MaxConcurrentAgents);
        Assert.True(RuntimeOptions.ValidConcurrency(options));
    }

    [Fact]
    public void DefaultLimitIsTen() => Assert.Equal(10, new RuntimeOptions().MaxConcurrentAgents);
}
