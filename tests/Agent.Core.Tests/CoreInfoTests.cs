using Xunit;

namespace Agent.Core.Tests;

public class CoreInfoTests
{
    [Fact]
    public void VersionIsSetForBootstrap()
    {
        Assert.Equal("0.0.0", CoreInfo.Version);
    }
}
