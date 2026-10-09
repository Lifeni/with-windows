using WithWindows.Core.Actions;

namespace WithWindows.Core.Tests;

public class DisplayTopologyTests
{
    [Theory]
    [InlineData(1u, "internal")]
    [InlineData(2u, "clone")]
    [InlineData(4u, "extend")]
    [InlineData(8u, "external")]
    [InlineData(0u, null)]
    [InlineData(3u, null)]
    public void TopologyName_映射已知编号(uint id, string? expected)
        => Assert.Equal(expected, DisplayTopology.TopologyName(id));

    [Fact]
    public void GetCurrentMode_返回合法拓扑名或_null()
    {
        string? mode = DisplayTopology.GetCurrentMode();
        Assert.True(mode is null or "internal" or "clone" or "extend" or "external", $"实际值：{mode ?? "<null>"}");
    }
}
