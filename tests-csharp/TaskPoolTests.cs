using InfiAir.Core.Missions;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// TaskPool（2026-08-07 迁移）单测：无放回抽取语义逐条对齐原 scripts/task_pool.gd
/// （单批内不重复 / 耗尽重洗续抽 / 排除项跳过 / 全池排除安全空 / Q05 批次耗尽补足），
/// 性质断言同 test/base_task_refresh_test.gd 第 8–9 节。
/// </summary>
public sealed class TaskPoolTests
{
    private static readonly TaskDef[] Defs =
    [
        new("kill_5", 5, "kill"),
        new("kill_15", 15, "kill"),
        new("kill_30", 30, "kill"),
        new("survive_60", 60, "survive"),
        new("survive_180", 180, "survive"),
        new("survive_300", 300, "survive"),
        new("boss_1", 1, "boss"),
        new("boss_2", 2, "boss"),
        new("boss_3", 3, "boss"),
    ];

    private static HashSet<string> Ids(params string[] ids)
    {
        return new HashSet<string>(ids);
    }

    [Fact]
    public void Draw_FullPool_ReturnsAllDistinct()
    {
        var pool = new TaskPool(Defs, seed: 20260805);
        var drawn = pool.Draw(9, new HashSet<string>());
        Assert.Equal(9, drawn.Count);
        var seen = new HashSet<string>();
        foreach (var def in drawn)
        {
            Assert.True(seen.Add(def.Id), $"单批无放回：{def.Id} 重复");
        }
    }

    [Fact]
    public void Draw_AfterExhaustion_RefillsAndContinues()
    {
        var pool = new TaskPool(Defs, seed: 7);
        Assert.Equal(9, pool.Draw(9, new HashSet<string>()).Count);
        var batch2 = pool.Draw(3, new HashSet<string>());
        Assert.Equal(3, batch2.Count);  // 耗尽后自动重洗续抽
    }

    [Fact]
    public void Draw_ExcludedIds_Skipped()
    {
        var pool = new TaskPool([new("a", 1, "kill"), new("b", 1, "kill")], seed: 1);
        var drawn = pool.Draw(2, Ids("a"));
        Assert.Single(drawn);
        Assert.Equal("b", drawn[0].Id);
    }

    [Fact]
    public void Draw_AllExcluded_ReturnsEmpty()
    {
        var pool = new TaskPool([new("a", 1, "kill"), new("b", 1, "kill")], seed: 1);
        Assert.Empty(pool.Draw(2, Ids("a", "b")));  // 排除覆盖全池：安全返回空，不死循环
    }

    [Fact]
    public void Draw_NonPositiveCount_ReturnsEmpty()
    {
        var pool = new TaskPool(Defs, seed: 1);
        Assert.Empty(pool.Draw(0, new HashSet<string>()));
        Assert.Empty(pool.Draw(-3, new HashSet<string>()));
    }

    [Fact]
    public void Draw_CountAboveUsable_CapsAtUsable()
    {
        var pool = new TaskPool(Defs, seed: 3);
        // 排除 7 个 → 可用 2 个；请求 9 个 → 只返回 2 个
        var drawn = pool.Draw(9, Ids("kill_5", "kill_15", "kill_30", "survive_60", "survive_180", "survive_300", "boss_1"));
        Assert.Equal(2, drawn.Count);
    }

    [Fact]
    public void Draw_Q05_ExcludedFields_AlwaysFullSlots()
    {
        // Q05（2026-08-05）：批次耗尽跨批补足——固定种子 20 轮抽取恒满额
        // （原实现「本批已产出即 break」在排除在场任务时提前耗尽）
        var pool = new TaskPool(Defs, seed: 20260805);
        var inField = new HashSet<string>();
        for (int i = 0; i < 20; i++)
        {
            var drawn = pool.Draw(3, inField);
            Assert.Equal(3, drawn.Count);
            inField = new HashSet<string>(drawn.Select(d => d.Id));
        }
    }

    [Fact]
    public void Draw_SeededSequence_IsReproducible()
    {
        var a = new TaskPool(Defs, seed: 42).Draw(9, new HashSet<string>());
        var b = new TaskPool(Defs, seed: 42).Draw(9, new HashSet<string>());
        Assert.Equal(a.Select(d => d.Id), b.Select(d => d.Id));
    }

    [Fact]
    public void Draw_EmptyPool_ReturnsEmpty()
    {
        var pool = new TaskPool([], seed: 1);
        Assert.Empty(pool.Draw(3, new HashSet<string>()));
    }

    [Fact]
    public void Draw_DuplicateIdDefs_DoesNotHangAndDeduplicates()
    {
        // 2026-08-09 审计修复验证：重复 id 定义（数据配置错误）下原实现按条目计 usable，
        // drawnIds 恒跳过重复项 → result.Count 永远追不上 → Refill 无限循环挂死；
        // 修复后 usable 按 id 去重，抽取名额 = 可用 id 数，安全返回去重结果
        var pool = new TaskPool([new("a", 1, "kill"), new("a", 2, "kill")], seed: 1);
        var drawn = pool.Draw(2, new HashSet<string>());
        Assert.Single(drawn);
        Assert.Equal("a", drawn[0].Id);
    }

    [Fact]
    public void Draw_DuplicateIdDefs_ExcludedPartially_StillSafe()
    {
        var pool = new TaskPool([new("a", 1, "kill"), new("a", 2, "kill"), new("b", 1, "kill")], seed: 1);
        var drawn = pool.Draw(5, Ids("a")); // 排除 a → 可用仅 b
        Assert.Single(drawn);
        Assert.Equal("b", drawn[0].Id);
    }
}
