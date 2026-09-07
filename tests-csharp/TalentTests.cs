using InfiAir.Core.Talent;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>天赋缓存系统纯逻辑单测：缓存池 LIFO 衰减/花费、递增消耗、收益递减、
/// 互斥锁/专注惩罚/路线契约上限、风险加点、树结构与扇形布局。</summary>
public sealed class TalentTests
{
    private static TalentConfig Config() => new();

    // ---------------- TalentCache：溢出衰减 + LIFO 花费 ----------------

    [Fact]
    public void Cache_UnderThreshold_NoDecay()
    {
        var cache = new TalentCache(Config());
        cache.Grant(20);
        Assert.Equal(20, cache.Raw);
        Assert.Equal(20.0, cache.Effective, 9);
    }

    [Fact]
    public void Cache_Overflow_DecaysByPosition()
    {
        var cache = new TalentCache(Config());
        cache.Grant(25); // 超出 5 点：0.9+0.8+0.7+0.6+0.5
        Assert.Equal(25, cache.Raw);
        Assert.Equal(20.0 + 0.9 + 0.8 + 0.7 + 0.6 + 0.5, cache.Effective, 9);
    }

    [Fact]
    public void Cache_Overflow_DecayCapsAtFloor()
    {
        var cache = new TalentCache(Config());
        cache.Grant(40); // 超出 20 点：第 10 超点起触底 0.1
        var expected = 20.0;
        for (var depth = 1; depth <= 20; depth++)
        {
            expected += Math.Max(0.1, 1.0 - 0.1 * depth);
        }

        Assert.Equal(expected, cache.Effective, 9);
    }

    [Fact]
    public void Cache_Spend_LifoFromTail()
    {
        var cache = new TalentCache(Config());
        cache.Grant(25); // 尾部点值 [..., 0.9, 0.8, 0.7, 0.6, 0.5]
        Assert.True(cache.Spend(2.0)); // 贪心：整点移除 0.5 + 0.6 + 0.7（合计 1.8），尾点 0.8 再扣 0.2 → 残 0.6
        Assert.Equal(22, cache.Raw);
        Assert.Equal(20.0 + 0.9 + 0.6, cache.Effective, 9);
    }

    [Fact]
    public void Cache_Spend_InsufficientFailsWithoutChange()
    {
        var cache = new TalentCache(Config());
        cache.Grant(3);
        Assert.False(cache.Spend(10.0));
        Assert.Equal(3, cache.Raw);
        Assert.Equal(3.0, cache.Effective, 9);
    }

    [Fact]
    public void Cache_DecayDoesNotRecoverAfterSpend()
    {
        var cache = new TalentCache(Config());
        cache.Grant(25);
        cache.Spend(5.0); // 依次整点移除 0.5/0.6/0.7/0.8/0.9（=3.5）+ 2 个整 1.0 点，尾点再扣 0.5 → 残 0.5
        Assert.Equal(19, cache.Raw);
        Assert.Equal(18.5, cache.Effective, 9);
        // 关键：残点位置 18 已低于安全阈值，但历史衰减/花费不可回满（min 钳制）——溢出压力真实存在
        cache.Grant(1); // 新点落在位置 19（阈值内）→ 全值 1.0
        Assert.Equal(19.5, cache.Effective, 9);
    }

    [Fact]
    public void Cache_Restore_ClampsAndReappliesDecay()
    {
        var cache = new TalentCache(Config());
        cache.Restore(new[] { 1.0, 1.0, 5.0, -1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 });
        Assert.Equal(21, cache.Raw); // 负值点丢弃
        Assert.Equal(20.9, cache.Effective, 9); // 超界值钳 1.0 后按位衰减
    }

    // ---------------- TalentEconomy：消耗 / 上限 / 有效层级 ----------------

    [Fact]
    public void Cost_EscalatesPerLevel()
    {
        var cfg = Config();
        Assert.Equal(2, TalentEconomy.CostForLevel(cfg, 0));
        Assert.Equal(3, TalentEconomy.CostForLevel(cfg, 1));
        Assert.Equal(5, TalentEconomy.CostForLevel(cfg, 3));
    }

    [Fact]
    public void Cap_MutexReductionThenRouteHalving()
    {
        var cfg = Config();
        Assert.Equal(5, TalentEconomy.EffectiveCap(cfg, 5, 0, false));
        Assert.Equal(3, TalentEconomy.EffectiveCap(cfg, 5, 2, false)); // 互斥 -2
        Assert.Equal(2, TalentEconomy.EffectiveCap(cfg, 5, 0, true)); // 路线减半
        Assert.Equal(1, TalentEconomy.EffectiveCap(cfg, 5, 2, true)); // 叠加：3/2 → 1
        Assert.Equal(1, TalentEconomy.EffectiveCap(cfg, 1, 2, true)); // 下限钳 1
        Assert.Equal(1, TalentEconomy.EffectiveCap(cfg, 5, 9, true)); // 不超过结构上限语义的下限
    }

    [Fact]
    public void EffectiveLevel_DiminishingReturnsPastSoftcap()
    {
        var cfg = Config();
        Assert.Equal(2.0, TalentEconomy.EffectiveLevel(cfg, 2, 3, false, false, 0), 9);
        // 软上限 3 后每级增益效率：k=4 → 0.75、k=5 → 0.5、k=6 → 0.25（触底）
        Assert.Equal(3.75, TalentEconomy.EffectiveLevel(cfg, 4, 3, false, false, 0), 9);
        Assert.Equal(4.25, TalentEconomy.EffectiveLevel(cfg, 5, 3, false, false, 0), 9);
        // Lv8 = 3 + 0.75 + 0.5 + 0.25×3
        Assert.Equal(5.0, TalentEconomy.EffectiveLevel(cfg, 8, 3, false, false, 0), 9);
    }

    [Fact]
    public void EffectiveLevel_RouteBonusAndFocusPenaltyCompose()
    {
        var cfg = Config();
        // Lv4（eff 3.75）+ 路线核心 +1
        Assert.Equal(4.75, TalentEconomy.EffectiveLevel(cfg, 4, 3, true, false, 0), 9);
        Assert.Equal(3.75 * 0.94, TalentEconomy.EffectiveLevel(cfg, 4, 3, false, true, 1), 9); // 专注 -6%
        Assert.Equal(3.75 * 0.6, TalentEconomy.EffectiveLevel(cfg, 4, 3, false, true, 7), 9); // 惩罚触顶 40%
        Assert.Equal(3.75, TalentEconomy.EffectiveLevel(cfg, 4, 3, false, true, 0), 9); // 未触发不折扣
    }

    [Fact]
    public void FocusOver_TriggersAtThresholdAndCountsBeyond()
    {
        var cfg = Config();
        Assert.Equal(0, TalentEconomy.FocusOver(6, cfg));
        Assert.Equal(1, TalentEconomy.FocusOver(7, cfg));
        Assert.Equal(2, TalentEconomy.FocusOver(8, cfg));
    }

    [Fact]
    public void Mutex_TriggersAtThreshold()
    {
        var cfg = Config();
        Assert.Equal(0, TalentEconomy.MutexReduction(4, cfg));
        Assert.Equal(2, TalentEconomy.MutexReduction(5, cfg));
    }

    [Fact]
    public void OverchargeCost_DoublesNextCost()
    {
        var cfg = Config();
        Assert.Equal(8, TalentEconomy.OverchargeCost(cfg, 4));
    }

    // ---------------- TalentTree：结构完整性 ----------------

    [Fact]
    public void Tree_AllNodeIdsUniqueAndComplete()
    {
        var ids = TalentTree.NodeIds();
        Assert.Equal(19, ids.Count);
        Assert.Equal(ids.Count, new HashSet<string>(ids).Count);
        foreach (var id in ids)
        {
            Assert.NotNull(TalentTree.Find(id));
        }
    }

    [Fact]
    public void Tree_PrerequisiteChainsWithinLine()
    {
        Assert.Null(TalentTree.Prerequisite("power_shot"));
        Assert.Equal("power_shot", TalentTree.Prerequisite("bullet_speed"));
        Assert.Equal("bullet_speed", TalentTree.Prerequisite("piercing"));
        Assert.Equal("explosive", TalentTree.Prerequisite("laser_beam"));
        Assert.Null(TalentTree.Prerequisite("unknown"));
    }

    [Fact]
    public void Tree_RoutesCoverThreeCategories()
    {
        Assert.Equal(3, TalentTree.Routes.Count);
        Assert.Equal("offense", TalentTree.Routes[0].CoreCategoryId);
        Assert.Equal("defense", TalentTree.OpposingCategoryOf("offense"));
        Assert.Equal("offense", TalentTree.OpposingCategoryOf("defense"));
        Assert.Null(TalentTree.OpposingCategoryOf("special"));
    }

    // ---------------- TalentFanLayout：扇形几何 ----------------

    [Fact]
    public void FanLayout_SpreadsLinesSymmetrically()
    {
        var layout = new TalentFanLayout { Width = 1200, Height = 760 };
        var positions = layout.Compute(new[]
        {
            new[] { "a1", "a2" },
            new[] { "b1" },
            new[] { "c1" },
        });
        var (rootX, rootY) = layout.Root;
        Assert.Equal(600.0, rootX, 9);
        var a1 = positions.First(p => p.NodeId == "a1");
        var c1 = positions.First(p => p.NodeId == "c1");
        Assert.True(a1.X < rootX && c1.X > rootX); // 线 0 在左、末线在右
        Assert.True(Math.Abs((rootX - a1.X) - (c1.X - rootX)) < 1e-6); // 对称
        Assert.True(positions.First(p => p.NodeId == "b1").Y < rootY); // 向上展开
    }

    [Fact]
    public void FanLayout_DepthRadiatesOutward()
    {
        var layout = new TalentFanLayout();
        var positions = layout.Compute(new[]
        {
            new[] { "n1", "n2", "n3" },
        });
        var (rx, ry) = layout.Root;
        TalentFanPosition prev = null!;
        foreach (var pos in positions)
        {
            var dRoot = Math.Sqrt(Math.Pow(pos.X - rx, 2) + Math.Pow(pos.Y - ry, 2));
            if (prev != null)
            {
                var dPrev = Math.Sqrt(Math.Pow(prev.X - rx, 2) + Math.Pow(prev.Y - ry, 2));
                Assert.True(dRoot > dPrev);
            }

            prev = pos;
        }
    }
}
