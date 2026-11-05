using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Agent.Architecture.Tests;

public class LayerRulesTests
{
    private static readonly Assembly CoreAssembly = typeof(Agent.Core.CoreInfo).Assembly;

    [Fact]
    public void CoreDoesNotDependOnOuterLayers()
    {
        var result = Types.InAssembly(CoreAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Agent.Infrastructure",
                "Agent.Sandbox",
                "Agent.Api",
                "Agent.Worker")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void CoreDoesNotDependOnFrameworkOrDriverNamespaces()
    {
        var result = Types.InAssembly(CoreAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Extensions.Hosting",
                "Npgsql",
                "OpenAI")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }
}
