using InfiAir.Core.Talent;
using Xunit;

namespace InfiAir.Core.Tests.Talent;

/// <summary>天赋经济测试：缓存池衰减/花费与四机制纯规则。天赋点数是玩家唯一成长货币，
/// 衰减或花费口径错了直接变经济漏洞。</summary>
public sealed class TalentEconomyTests
{
    /// <summary>小阈值配置：3 点内不衰减，之后每点 -0.5，下限 0.1。
    /// 默认关掉正向回补（RecoveryStep = 0）——回补单独用 PositiveConfig 测，
    /// 否则「衰减不可逆」的既有断言会被回补静默改写成另一套语义。</summary>
    private static TalentConfig CacheConfig() => new()
    {
        SafeThreshold = 3,
        DecayStep = 0.5,
        DecayFloor = 0.1,
        RecoveryStep = 0.0,
    };

    /// <summary>开启正向回补的配置（同阈值，每次花费后已衰减点朝 1.0 抬 25%）。</summary>
    private static TalentConfig PositiveConfig() => new()
    {
        SafeThreshold = 3,
        DecayStep = 0.5,
        DecayFloor = 0.1,
        RecoveryStep = 0.25,
    };

    [Fact]
    public void Cache_WithinSafeThreshold_NoDecay()
    {
        var cache = new TalentCache(CacheConfig());
        cache.Grant(3);

        Assert.Equal(3, cache.Raw);
        Assert.Equal(3.0, cache.Effective, 12);
    }

    [Fact]
    public void Cache_OverflowPoints_DecayByDepth()
    {
        var cache = new TalentCache(CacheConfig());
        cache.Grant(5); // 第 4 点衰减到 0.5，第 5 点衰减到 0（钳下限 0.1）

        Assert.Equal(5, cache.Raw);
        Assert.Equal(3.0 + 0.5 + 0.1, cache.Effective, 12);
    }

    [Fact]
    public void Cache_DecayIsIrreversible_AfterSpending()
    {
        // 花费使总点数回落，已衰减点不回满——若衰减可逆，回落后第 4 点会回到 0.5
        var cache = new TalentCache(CacheConfig());
        cache.Grant(5);
        cache.Spend(0.3); // 吃掉 0.1 尾点 + 0.2，留 0.3
        cache.ApplyDecay();

        Assert.Equal(4, cache.Raw);
        Assert.Equal(3.3, cache.Effective, 12); // 1+1+1+0.3：第 4 点保持历史衰减后的 0.3
    }

    [Fact]
    public void Cache_SpendTriggersRecovery_WhenEnabled()
    {
        // 回补语义：花掉点数即缓解溢出压力（StS 稀有度保底的正向补偿思路）
        var cache = new TalentCache(PositiveConfig());
        cache.Grant(5); // 值 [1,1,1,0.5,0.1]

        Assert.True(cache.Spend(1.0)); // 扣掉尾点 0.1 + 次尾 0.5 + 第三点 0.4，第三点留 0.6
        // 回补：剩下那点 0.6 朝 1.0 抬 25% → 0.6 + 0.4×0.25 = 0.7；合计 1 + 1 + 0.7 = 2.7
        Assert.Equal(2.7, cache.Effective, 12);
    }

    [Fact]
    public void Cache_RecoveryNeverExceedsFullValue()
    {
        var cache = new TalentCache(PositiveConfig());
        cache.Grant(4); // 值 [1,1,1,0.5]
        Assert.True(cache.Spend(0.2));

        // 多次回补不得把任何点抬过 1.0（回补只作用于曾被衰减的点）
        for (var i = 0; i < 20; i++)
        {
            cache.Grant(1);
            if (!cache.Spend(0.05))
            {
                break;
            }
        }

        Assert.True(cache.Effective <= cache.Raw + 1e-9,
            $"回补越界：有效 {cache.Effective} > 原始 {cache.Raw}");
    }

    [Fact]
    public void Cache_SpendTailValue_PreservesPartialPoint()
    {
        var cache = new TalentCache(CacheConfig());
        cache.Grant(4); // 值 [1,1,1,0.5]

        Assert.True(cache.Spend(1.2)); // 吃掉尾点 0.5 + 次尾 0.7，留 0.3

        Assert.Equal(3, cache.Raw);
        Assert.Equal(2.3, cache.Effective, 12);
    }

    [Fact]
    public void Cache_SpendBeyondEffective_FailsWithoutChange()
    {
        var cache = new TalentCache(CacheConfig());
        cache.Grant(2);

        Assert.False(cache.Spend(2.5));
        Assert.Equal(2.0, cache.Effective, 12);
    }

    [Fact]
    public void Cache_SnapshotRestore_RoundTrips()
    {
        var cache = new TalentCache(CacheConfig());
        cache.Grant(5);
        var snapshot = cache.Snapshot();

        var restored = new TalentCache(CacheConfig());
        restored.RestoreValues(snapshot);

        Assert.Equal(cache.Raw, restored.Raw);
        Assert.Equal(cache.Effective, restored.Effective, 12);
    }

    [Fact]
    public void Cache_RestoreValues_NonFiniteOrNegative_ClampsToZero()
    {
        // 手改存档混入 NaN：Effective 变 NaN 后花费判据恒假、Spend 空转返回 true——白拿天赋。
        var cache = new TalentCache(CacheConfig());
        cache.RestoreValues(new[] { 1.0, double.NaN, double.PositiveInfinity, -5.0, 1.0 });

        Assert.Equal(2.0, cache.Effective, 12);
        Assert.False(cache.Spend(2.5));
    }

    [Fact]
    public void Cache_SpendNaN_IsRejected()
    {
        var cache = new TalentCache(CacheConfig());
        cache.Grant(3);

        Assert.False(cache.Spend(double.NaN));
        Assert.Equal(3.0, cache.Effective, 12);
    }

    [Fact]
    public void CostForLevel_IncrementsLinearly()
    {
        var config = new TalentConfig();
        Assert.Equal(2, TalentEconomy.CostForLevel(config, 0));
        Assert.Equal(5, TalentEconomy.CostForLevel(config, 3));
    }

    [Fact]
    public void EffectiveLevel_ZeroLevelStaysZero_EvenWithRouteBonus()
    {
        // 路线加成只作用于已投入节点：未买 homing 不得凭空获得一级效果；
        // 已投入的路线核心节点在软上限内 1:1 之上再加固定加成
        var config = new TalentConfig();
        Assert.Equal(0.0, TalentEconomy.EffectiveLevel(config, 0, softcap: 2, routeCore: true, focusDiscounted: false, focusOver: 0), 12);
        Assert.Equal(3.0, TalentEconomy.EffectiveLevel(config, 2, softcap: 2, routeCore: true, focusDiscounted: false, focusOver: 0), 12);
        Assert.Equal(2.0, TalentEconomy.EffectiveLevel(config, 2, softcap: 2, routeCore: false, focusDiscounted: false, focusOver: 0), 12);
    }

    [Fact]
    public void EffectiveLevel_DiminishesPastSoftcap()
    {
        var config = new TalentConfig { DiminishingStep = 0.5, DiminishingFloor = 0.25 };
        // 第 3 级效率 0.5：2 + 0.5 = 2.5
        Assert.Equal(2.5, TalentEconomy.EffectiveLevel(config, 3, softcap: 2, routeCore: false, focusDiscounted: false, focusOver: 0), 12);
    }

    [Fact]
    public void EffectiveLevel_FocusDiscount_ScalesWithOver()
    {
        var config = new TalentConfig();
        var discounted = TalentEconomy.EffectiveLevel(config, 2, softcap: 2, routeCore: false, focusDiscounted: true, focusOver: 2);
        Assert.Equal(2.0 * (1.0 - 0.12), discounted, 12);
    }

    [Fact]
    public void EffectiveCap_FloorPreventsZero()
    {
        var config = new TalentConfig { RouteCapFloor = 1 };
        // 互斥 -2 后路线减半：(5-2)/2 = 1；无扣减时上限保持结构值
        Assert.Equal(1, TalentEconomy.EffectiveCap(config, maxLevel: 5, mutexReduction: 2, routeHalved: true));
        Assert.Equal(5, TalentEconomy.EffectiveCap(config, maxLevel: 5, mutexReduction: 0, routeHalved: false));
    }

    [Fact]
    public void MutexAndFocusAndOvercharge_FollowThresholds()
    {
        var config = new TalentConfig { MutexThreshold = 5, MutexCapReduction = 2, FocusThreshold = 7 };

        Assert.Equal(0, TalentEconomy.MutexReduction(4, config));
        Assert.Equal(2, TalentEconomy.MutexReduction(5, config));
        Assert.Equal(0, TalentEconomy.FocusOver(6, config));
        Assert.Equal(1, TalentEconomy.FocusOver(7, config));
        Assert.Equal(6, TalentEconomy.OverchargeCost(config, 3));
    }

    [Fact]
    public void FocusTriggerSurface_IsPinned()
    {
        // 护栏（对应 TalentEconomy.FocusOver 注释里的显式约束）：阈值 7 时，只有结构上限 ≥7 的节点
        // 能触发专注惩罚。现网 balance.json 的 max_stacks 里只有 extra_life=10 满足，其余最高 5
        // （风险加点 +1 后 6，仍不过线）。此处把「触发面」钉死：日后放宽任一节点上限
        // （使它能到 7 级）本用例即红，强制走一次显式决策而不是让惩罚面悄悄扩大。
        var config = new TalentConfig { FocusThreshold = 7 };
        // 现网全部节点的结构上限（balance.json augments.*.max_stacks）
        var caps = new[]
        {
            5, 4, 2, 10, 1, 2, 1, 1, 1, 1, 3, 1, 2, 2, 2, 3, 2, 3, 1, 2, 2, 2, 2, 2, 3, 3, 2,
        };

        var triggerable = 0;
        var maxCap = 0;
        foreach (var cap in caps)
        {
            maxCap = Math.Max(maxCap, cap);
            if (TalentEconomy.FocusOver(cap, config) > 0)
            {
                triggerable++;
            }
        }

        // 触发面必须是「恰好 extra_life 一个节点」＋「最高上限 10」——两者任一变化都要显式复核
        Assert.Equal(1, triggerable);
        Assert.Equal(10, maxCap);
        // 其余节点即使风险加点 +1 也不得越线（5+1=6 < 7）
        Assert.Equal(0, TalentEconomy.FocusOver(6, config));
    }

    // ---- 可升级提示判定（HUD：点数够点亮任一可选节点） ----

    [Fact]
    public void CheapestAffordable_ReturnsLowestAffordableCost()
    {
        // 三档价格 5/3/9，缓存 4：只有 3 买得起
        Assert.Equal(3, TalentEconomy.CheapestAffordable(new[] { 5, 3, 9 }, 4.0));
    }

    [Fact]
    public void CheapestAffordable_ExactBoundary_IsAffordable()
    {
        // 缓存恰好等于价格即可买（衰减产生小数，须容差而非严格大于）
        Assert.Equal(2, TalentEconomy.CheapestAffordable(new[] { 2 }, 2.0));
        Assert.Equal(2, TalentEconomy.CheapestAffordable(new[] { 2 }, 1.999999999));
        Assert.Equal(0, TalentEconomy.CheapestAffordable(new[] { 2 }, 1.0));
    }

    [Fact]
    public void CheapestAffordable_EmptyOrAllTooExpensive_ReturnsZero()
    {
        // 无可选节点（全被前置/上限/名额挡下）→ 0，提示不亮
        Assert.Equal(0, TalentEconomy.CheapestAffordable(Array.Empty<int>(), 99.0));
        Assert.Equal(0, TalentEconomy.CheapestAffordable(new[] { 7, 9 }, 6.0));
    }

    [Fact]
    public void CheapestAffordable_IgnoresNonPositiveCosts()
    {
        // 0 是 NextCost 的「已锁定」契约值，不参与「买得起」判定（否则提示恒亮）
        Assert.Equal(0, TalentEconomy.CheapestAffordable(new[] { 0, 0 }, 100.0));
        Assert.Equal(3, TalentEconomy.CheapestAffordable(new[] { 0, 3 }, 100.0));
    }
}
