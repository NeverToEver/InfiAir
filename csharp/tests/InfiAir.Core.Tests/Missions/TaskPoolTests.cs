using InfiAir.Core.Missions;
using Xunit;

namespace InfiAir.Core.Tests.Missions;

/// <summary>TaskPool 契约测试：无放回抽取/排除刷新/重复 id 防挂死。任务轮换的经济漏洞
/// （重复任务、刷新重号）都从池语义漏出去。</summary>
public sealed class TaskPoolTests
{
    private static TaskDef[] Defs(params string[] ids) => ids.Select(id => new TaskDef(id)).ToArray();

    [Fact]
    public void Draw_SingleBatch_HasNoDuplicates()
    {
        var pool = new TaskPool(Defs("a", "b", "c", "d", "e"), seed: 7);
        var drawn = pool.Draw(5, new HashSet<string>());

        Assert.Equal(5, drawn.Count);
        Assert.Equal(5, drawn.Select(d => d.Id).Distinct().Count());
    }

    [Fact]
    public void Draw_ExcludingEverything_ReturnsEmpty()
    {
        var pool = new TaskPool(Defs("a", "b"), seed: 1);
        Assert.Empty(pool.Draw(2, new HashSet<string> { "a", "b" }));
    }

    [Fact]
    public void Draw_ExcludingSome_OnlyDrawsFromRemainder()
    {
        var pool = new TaskPool(Defs("a", "b", "c"), seed: 3);
        var drawn = pool.Draw(2, new HashSet<string> { "a" });

        Assert.Equal(2, drawn.Count);
        Assert.All(drawn, d => Assert.NotEqual("a", d.Id));
    }

    [Fact]
    public void Draw_DuplicateDefinitions_DoNotHangAndStayDistinct()
    {
        // 按 id 去重计数：重复定义不得让名额追不上可用数死循环
        var pool = new TaskPool(Defs("a", "a", "b"), seed: 5);
        var drawn = pool.Draw(2, new HashSet<string>());

        Assert.Equal(2, drawn.Count);
        Assert.Equal(2, drawn.Select(d => d.Id).Distinct().Count());
    }

    [Fact]
    public void Draw_ZeroCount_ReturnsEmpty()
    {
        var pool = new TaskPool(Defs("a"), seed: 1);
        Assert.Empty(pool.Draw(0, new HashSet<string>()));
    }

    [Fact]
    public void Draw_SameSeed_ReproducesSequence()
    {
        var first = new TaskPool(Defs("a", "b", "c", "d"), seed: 42).Draw(4, new HashSet<string>());
        var second = new TaskPool(Defs("a", "b", "c", "d"), seed: 42).Draw(4, new HashSet<string>());

        Assert.Equal(first.Select(d => d.Id), second.Select(d => d.Id));
    }

    [Fact]
    public void Draw_QuotaClampedToUsableIds_AfterExclusions()
    {
        // 排除导致批次提前耗尽：名额按去重后的可用 id 收口，不截断也不挂死
        var pool = new TaskPool(Defs("a", "b", "c"), seed: 9);
        var drawn = pool.Draw(3, new HashSet<string> { "a" });

        Assert.Equal(2, drawn.Count);
        Assert.Equal(2, drawn.Select(d => d.Id).Distinct().Count());
    }
}
