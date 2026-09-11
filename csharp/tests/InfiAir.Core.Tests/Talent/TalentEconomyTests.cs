using InfiAir.Core.Talent;
using Xunit;

namespace InfiAir.Core.Tests.Talent;

/// <summary>天赋经济测试：缓存池衰减/花费与四机制纯规则。天赋点数是玩家唯一成长货币，
/// 衰减或花费口径错了直接变经济漏洞。</summary>
public sealed class TalentEconomyTests
{
    /// <summary>小阈值配置：3 点内不衰减，之后每点 -0.5，下限 0.1。</summary>
    private static TalentConfig CacheConfig() => new()
    {
        SafeThreshold = 3,
        DecayStep = 0.5,
        DecayFloor = 0.1,
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
}
