using Agent.Core.Shell;
using Xunit;

namespace Agent.Core.Tests.Shell;

public sealed class CommandPolicyTests
{
    [Theory]
    [InlineData("Remove-Item -Recurse -Force C:\\Users")]
    [InlineData("rm -rf /")]
    [InlineData("shutdown /s")]
    [InlineData("git push origin main")]
    [InlineData("Invoke-WebRequest https://example.com")]
    [InlineData("git reset --hard HEAD")]
    public void FR34DeniedPatternsAreRefused(string command)
    {
        var decision = CommandPolicy.Evaluate(command);

        Assert.Equal(CommandOutcome.Denied, decision.Outcome);
    }

    [Theory]
    [InlineData("dotnet build && rm x")]
    [InlineData("dotnet build; whoami")]
    [InlineData("dir | findstr secret")]
    [InlineData("echo $(whoami)")]
    [InlineData("type a > b")]
    [InlineData("echo `whoami`")]
    public void FR34ChainingPipingAndSubstitutionAreRefused(string command)
    {
        var decision = CommandPolicy.Evaluate(command);

        Assert.Equal(CommandOutcome.Denied, decision.Outcome);
    }

    [Theory]
    [InlineData("dotnet build")]
    [InlineData("git status")]
    [InlineData("Get-ChildItem src")]
    public void FR34AllowlistedCommandsAreAllowed(string command)
    {
        var decision = CommandPolicy.Evaluate(command);

        Assert.Equal(CommandOutcome.Allowed, decision.Outcome);
    }

    [Theory]
    [InlineData("curl-like-tool --fetch")]
    [InlineData("python script.py")]
    public void FR34UnknownProgramsNeedApprovalAndDoNotRun(string command)
    {
        var decision = CommandPolicy.Evaluate(command);

        Assert.Equal(CommandOutcome.RequiresApproval, decision.Outcome);
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\cmd.exe /c dir")]
    [InlineData("./tool dotnet")]
    public void FR34ProgramsGivenByPathAreDenied(string command)
    {
        var decision = CommandPolicy.Evaluate(command);

        Assert.Equal(CommandOutcome.Denied, decision.Outcome);
    }

    [Fact]
    public void FR34EmptyCommandIsDenied()
    {
        Assert.Equal(CommandOutcome.Denied, CommandPolicy.Evaluate("   ").Outcome);
    }
}
