using System.IO;
using System.Linq;
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
    public void Cache_Recovery_IsExactAndNeverOvershoots()
    {
        // 精确值断言（原版只判 Effective ≤ Raw，对任何「每点值 ≤1」的实现都恒真 → 守不住不变量）：
        // [1,1,1,0.5,0.1] 花 1.0 → 扣 0.1 + 0.5 + 0.4，第三点余 0.6；回补 25% → 0.6 + 0.4×0.25 = 0.7
        var cache = new TalentCache(PositiveConfig());
        cache.Grant(5);
        Assert.True(cache.Spend(1.0));
        Assert.Equal(2.7, cache.Effective, 12);

        // 坏配置（RecoveryStep > 1）不得过冲：步进必须在实现内钳到 1.0。
        // 逐点判而不看 Effective：回补预算（＝本次实际扣减量）会盖住某一点的过冲——
        // 0.6 这一点按 step=2.0 会被抬到 1.4，总和却仍是「扣 1.0 补 ≤1.0」。放开 Math.Min(step,1.0) 即红。
        var overshootCfg = new TalentConfig
        {
            SafeThreshold = 3,
            DecayStep = 0.5,
            DecayFloor = 0.1,
            RecoveryStep = 2.0,
        };
        var overshoot = new TalentCache(overshootCfg);
        overshoot.Grant(5); // 值 [1,1,1,0.5,0.1]
        Assert.True(overshoot.Spend(1.0)); // 扣 0.1+0.5+0.4，第三点余 0.6；回补预算 1.0
        foreach (var value in overshoot.Snapshot())
        {
            Assert.True(value <= 1.0 + 1e-9, $"回补把点值抬过满值：{value}");
        }
    }

    [Fact]
    public void Cache_RecoveryGain_IsErasedByNextGrantDecay()
    {
        // 现状钉住（回补「名不副实」是设计意图与实现不符，属人决策项，见交付报告）：
        // 回补当场生效，但下一次 Grant 末尾的 ApplyDecay 按位置把已衰减点重新压回深度上限——
        // 收益被逐位抹平，RecoveryStep=0.25 与 RecoveryStep=0 走同一序列得到完全相同的点值序列。
        var recoveryConfig = new TalentConfig
        {
            SafeThreshold = 2,
            DecayStep = 0.25,
            DecayFloor = 0.25,
            RecoveryStep = 0.25,
        };
        var offConfig = new TalentConfig
        {
            SafeThreshold = 2,
            DecayStep = 0.25,
            DecayFloor = 0.25,
            RecoveryStep = 0.0,
        };

        var withRecovery = new TalentCache(recoveryConfig);
        var without = new TalentCache(offConfig);
        foreach (var spent in new[] { withRecovery, without })
        {
            // Grant(5) → 第 2/3/4 点衰减到 0.75/0.5/0.25，有效 3.5
            spent.Grant(5);
            Assert.True(spent.Spend(0.25)); // 吃掉 0.25 尾点
        }

        // 机制确实在跑：回补当场把 0.75→0.8125、0.5→0.625（+0.1875）
        Assert.Equal(3.4375, withRecovery.Effective, 12);
        Assert.True(withRecovery.Effective > without.Effective);

        foreach (var spent in new[] { withRecovery, without })
        {
            spent.Grant(1); // 回补收益到此处被 ApplyDecay 抹平
        }

        Assert.Equal(without.Effective, withRecovery.Effective, 12);
        Assert.Equal(without.Snapshot(), withRecovery.Snapshot());
    }

    [Fact]
    public void Cache_RecoveryGain_IsPartlyErasedByNextGrantDecay_AtProductionDefaults()
    {
        // 现状钉住（「回补名不副实」是设计意图与实现不符，属人决策项，见交付报告）。
        // 生产参数（TalentConfig 默认值＝balance.json talent.cache：阈值 30 / 衰减 0.1 / 回补 0.25）：
        // Grant(35) 后第 30..34 点衰减为 0.9/0.8/0.7/0.6/0.5（Effective 31.5）；
        // Spend(2.0) 扣掉尾三点共 1.8 + 从 0.8 点扣 0.2（余 0.6）→ 31.5，回补预算 2.0 把
        // 0.9→0.925、0.6→0.7（当场 31.625，比关闭回补的 31.5 多 0.125）；
        // 下一次 Grant(1) 触发 ApplyDecay：0.925 越过后点上限 0.9 被压回，0.7 未越上限 0.8 得以保留
        // → 回补路径 32.3 vs 关闭回补 32.2，回补收益只剩 0.1（0.125 中的 0.1）。
        // 该收益为「跨位置上限的透支」，会被后续入账按上限差额逐步回收。
        var recovery = new TalentCache(new TalentConfig());
        var baseline = new TalentCache(new TalentConfig { RecoveryStep = 0.0 });
        foreach (var cache in new[] { recovery, baseline })
        {
            cache.Grant(35);
            Assert.True(cache.Spend(2.0));
        }

        Assert.Equal(31.625, recovery.Effective, 12);
        Assert.Equal(31.5, baseline.Effective, 12);

        foreach (var cache in new[] { recovery, baseline })
        {
            cache.Grant(1);
        }

        Assert.Equal(32.3, recovery.Effective, 12);
        Assert.Equal(32.2, baseline.Effective, 12);
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
    public void Cache_RestoreValues_AboveFullValue_ClampsToOne()
    {
        // 点值不变量是「每点 ≤1」：Grant 只追加 1.0、ApplyDecay 只下调、ApplyRecovery 只在 <1 时朝 1 抬。
        // 读档是唯一外部入口，手改存档塞入 5.0 会让 Effective 虚高——与 NaN 同一条威胁模型，
        // 原先只堵了非有限/非正一路，超值原样入列即白拿天赋。
        var cache = new TalentCache(CacheConfig());
        cache.RestoreValues(new[] { 5.0, 0.5 });

        Assert.Equal(2, cache.Raw);
        Assert.Equal(1.5, cache.Effective, 12);
        Assert.False(cache.Spend(2.0));    // 兑现不了被钳掉的 3.5
        Assert.True(cache.Spend(1.5));
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
    public void Cache_SpendZeroOrNegative_IsRejectedWithoutRecovery()
    {
        // cost<=0 是空转花费：旧实现走完扣减循环（零次）后仍触发正向回补，Effective 白涨——
        // 花 0 点就能抬高有效点数，破坏「Spend 不得抬高 Effective」。
        var cache = new TalentCache(PositiveConfig());
        cache.Grant(5); // 值 [1,1,1,0.5,0.1]，Effective 3.6
        var before = cache.Effective;

        Assert.False(cache.Spend(0.0));
        Assert.False(cache.Spend(-1.0));
        Assert.Equal(before, cache.Effective, 12);
    }

    [Fact]
    public void Cache_SpendNeverRaisesEffective()
    {
        // 不变量：任何 cost 下花费后有效点数不得上升（回补只能把「已扣剩的」点朝 1 抬，
        // 且被扣掉的量必须不小于回补量）。旧实现下 Spend(0.0) 即红。
        var cache = new TalentCache(PositiveConfig());
        cache.Grant(5);
        foreach (var cost in new[] { 0.0, 0.1, 0.5, 1.0, 2.0, 10.0 })
        {
            var before = cache.Effective;
            cache.Spend(cost);
            Assert.True(
                cache.Effective <= before + 1e-9,
                $"Spend({cost}) 抬高了有效点数：{before} → {cache.Effective}");
        }
    }

    [Fact]
    public void Cache_NegativeSafeThreshold_TreatedAsZeroStartAndDoesNotThrow()
    {
        // 手改配置/坏档可能给出负安全阈值：直接拿它当数组下标会抛 IndexOutOfRangeException
        // 击穿入账路径。负值视作 0 起点（缓存第 i 点按衰减深度 i+1 处理）。
        var config = CacheConfig();
        config.SafeThreshold = -5;
        var cache = new TalentCache(config);

        cache.Grant(3);

        Assert.Equal(3, cache.Raw);
        // 深度 1/2/3 → cap 0.5 / max(0.1, 0) / max(0.1, -0.5) = 0.5 + 0.1 + 0.1
        Assert.Equal(0.7, cache.Effective, 12);
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
    public void FocusPenalty_MatchesEffectiveLevelDiscount_AndCaps()
    {
        // 面板显示与生效层级必须用同一式子：折扣率从本函数出，EffectiveLevel 内部也调它
        // （同式两写会在改参数时分叉——面板说 -12%、实际按 -6% 扣）。
        var config = new TalentConfig { FocusPenaltyPerLevel = 0.06, FocusPenaltyCap = 0.4 };
        Assert.Equal(0.0, TalentEconomy.FocusPenalty(config, 0), 12);
        Assert.Equal(0.06, TalentEconomy.FocusPenalty(config, 1), 12);
        Assert.Equal(0.12, TalentEconomy.FocusPenalty(config, 2), 12);
        Assert.Equal(0.4, TalentEconomy.FocusPenalty(config, 100), 12); // 触顶

        foreach (var over in new[] { 1, 2, 6, 20 })
        {
            var expected = 6.0 * (1.0 - TalentEconomy.FocusPenalty(config, over));
            Assert.Equal(
                expected,
                TalentEconomy.EffectiveLevel(config, 6, softcap: 6, routeCore: false, focusDiscounted: true, focusOver: over),
                12);
        }
    }

    [Fact]
    public void FocusOver_PinsThresholdTriggerSemantics()
    {
        // 达到阈值即算一档（threshold → over=1），不是「超阈值 1 级起算」：
        // 注释与实现曾不一致，这里把实现的真实口径钉住，改口径必须显式改本用例。
        var config = new TalentConfig { FocusThreshold = 7 };
        Assert.Equal(0, TalentEconomy.FocusOver(6, config));
        Assert.Equal(1, TalentEconomy.FocusOver(7, config));
        Assert.Equal(2, TalentEconomy.FocusOver(8, config));
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
    public void EffectiveCap_NeverExceedsStructuralMaxLevel()
    {
        // RouteCapFloor 是「减半后不得归零」的下限，不是上限来源：下限高于结构上限时旧实现的
        // Math.Max 会把返回值抬到结构上限之上——配置 route_cap_floor=3 时，max_stacks=1 的节点
        // 可买到 3 级（消费点判的是 level >= cap）。
        var config = new TalentConfig { RouteCapFloor = 3 };
        Assert.Equal(1, TalentEconomy.EffectiveCap(config, maxLevel: 1, mutexReduction: 0, routeHalved: false));
        Assert.Equal(1, TalentEconomy.EffectiveCap(config, maxLevel: 1, mutexReduction: 2, routeHalved: false));
        Assert.Equal(2, TalentEconomy.EffectiveCap(config, maxLevel: 2, mutexReduction: 0, routeHalved: true));

        // 结构上限 0（不可升级节点）不得被下限抬到 1
        Assert.Equal(0, TalentEconomy.EffectiveCap(config, maxLevel: 0, mutexReduction: 0, routeHalved: false));

        // 上限足够时下限仍生效（原意保留：互斥扣减/路线减半不得把节点削到买不了）
        Assert.Equal(3, TalentEconomy.EffectiveCap(config, maxLevel: 5, mutexReduction: 4, routeHalved: false));
    }

    [Fact]
    public void EffectiveCap_IsAlwaysWithinStructuralBound()
    {
        // 「上限不越界」是消费点语义的前提（level >= cap 即已满）：全网格扫一遍，
        // 任何组合下返回值都必须落在 [0, maxLevel]。
        foreach (var maxLevel in new[] { 0, 1, 2, 5, 10 })
        {
            foreach (var mutex in new[] { 0, 1, 2, 7, 20 })
            {
                foreach (var halved in new[] { false, true })
                {
                    var cap = TalentEconomy.EffectiveCap(new TalentConfig(), maxLevel, mutex, halved);
                    Assert.True(
                        cap >= 0 && cap <= maxLevel,
                        $"上限越界：maxLevel={maxLevel} mutex={mutex} halved={halved} → {cap}");
                }
            }
        }
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

    /// <summary>
    /// 专注惩罚触发面护栏：**读真实 data/balance.json**，断言「只有 extra_life 能触发」。
    ///
    /// 为什么必须读配置而不是手抄一张上限表：手抄的表是配置的快照副本，改 balance.json
    /// 放宽某个节点的 max_stacks 时用例不会红——护栏形同虚设（本条此前正是如此，实测把
    /// power_shot 从 5 改到 7 后 217 条单测全绿）。读真值后，任何让第二个节点够到阈值的
    /// 改动都会立即变红，强制走一次显式决策（阈值 7 属人类已决策保留项）。
    /// </summary>
    [Fact]
    public void FocusTriggerSurface_MatchesRealBalanceConfig()
    {
        var (threshold, caps) = LoadFocusInputs();

        // 前提校验：读到的配置必须是真的（读失败要让本用例红，而不是静默用默认值蒙混）
        Assert.True(threshold > 0, "balance.json talent.focus.threshold 未读到正值");
        Assert.True(caps.Count > 0, "balance.json augments.*.max_stacks 未读到任何节点上限");

        var reachable = caps
            .Where(kv => TalentEconomy.FocusOver(kv.Value + 1, new TalentConfig { FocusThreshold = threshold }) > 0)
            .Select(kv => kv.Key)
            .ToList();

        // 触发面必须恰好是 extra_life 一个节点（其余节点连「结构上限 + 风险加点 1 级」都够不到阈值）
        Assert.Equal(new[] { "extra_life" }, reachable);
        Assert.Equal(10, caps["extra_life"]);
    }

    /// <summary>从仓库的 data/balance.json 读出专注阈值与全部节点结构上限。
    /// 从测试程序集目录向上找仓库根（不依赖 dotnet test 的 cwd）。</summary>
    private static (int Threshold, Dictionary<string, int> Caps) LoadFocusInputs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "data", "balance.json")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new InvalidOperationException("未找到仓库根（data/balance.json）——护栏必须读真实配置");
        }

        using var doc = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(dir.FullName, "data", "balance.json")));
        var root = doc.RootElement;
        var threshold = root.GetProperty("talent").GetProperty("focus").GetProperty("threshold").GetInt32();
        var caps = new Dictionary<string, int>();
        foreach (var node in root.GetProperty("augments").EnumerateObject())
        {
            if (node.Value.TryGetProperty("max_stacks", out var ms))
            {
                caps[node.Name] = ms.GetInt32();
            }
        }

        return (threshold, caps);
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

    // ---- 读档层级还原（风险加点那一级不得在读档时消失） ----

    [Fact]
    public void RestoreLevel_OverchargedNode_KeepsBoughtExtraLevel()
    {
        // 风险加点买的那一级是双倍价换来的：overcharged 节点读档必须保留 MaxLevel+1。
        // 无条件钳回 MaxLevel 会让这一级永久消失，且 _levels 与 Augments 分叉（同一节点两套层级）。
        Assert.Equal(4, TalentEconomy.RestoreLevel(4, 3, isOvercharged: true));
        Assert.Equal(3, TalentEconomy.RestoreLevel(3, 3, isOvercharged: true));
    }

    [Fact]
    public void RestoreLevel_NotOvercharged_ClampsBackToStructuralMax()
    {
        // 未标记风险加点的节点不得借存档抬高一级（手改档案把 level 写成 max+1 也不行）
        Assert.Equal(3, TalentEconomy.RestoreLevel(4, 3, isOvercharged: false));
        Assert.Equal(3, TalentEconomy.RestoreLevel(int.MaxValue, 3, isOvercharged: false));
        // 标记了也只多一级（多出来的不许留）
        Assert.Equal(4, TalentEconomy.RestoreLevel(int.MaxValue, 3, isOvercharged: true));
        // 负层级钳 0（手改档负值不得变成「负投入」影响互斥阈值判定）
        Assert.Equal(0, TalentEconomy.RestoreLevel(-5, 3, isOvercharged: true));
        Assert.Equal(0, TalentEconomy.RestoreLevel(2, 0, isOvercharged: false));
    }

    [Fact]
    public void RestoreLimit_SharesOneExtraLevelRule()
    {
        // 层级还原与增幅表上限必须同源：两处各写一份 +1 会在改规则时分叉
        Assert.Equal(3, TalentEconomy.RestoreLimit(3, isOvercharged: false));
        Assert.Equal(4, TalentEconomy.RestoreLimit(3, isOvercharged: true));
        Assert.Equal(0, TalentEconomy.RestoreLimit(-2, isOvercharged: true)); // 脏配置不得把上限算成负数
    }

    [Fact]
    public void ClampBonusSlots_ClampsToSupplyTierCap()
    {
        // 补给超载档是花 RP 买的档位（base.supply.overcharge_slot_max）：手改档把它写成 999
        // 会让本局每个节点都多一次风险加点名额，且绕过售罄限制——读档侧必须按同一档位钳回
        Assert.Equal(2, TalentEconomy.ClampBonusSlots(999.0, 2));
        Assert.Equal(2, TalentEconomy.ClampBonusSlots(2.0, 2));
        Assert.Equal(1, TalentEconomy.ClampBonusSlots(1.0, 2));
        Assert.Equal(0, TalentEconomy.ClampBonusSlots(0.0, 2));
        // 负值与小数（手改档的两种形态）：负数不得变成「负名额」影响名单截断，小数向零截断
        Assert.Equal(0, TalentEconomy.ClampBonusSlots(-1.0, 2));
        Assert.Equal(1, TalentEconomy.ClampBonusSlots(1.9, 2));
        // 非有限按「无值」归 0：NaN 与任何比较都假，放行会让名额上限静默失效
        Assert.Equal(0, TalentEconomy.ClampBonusSlots(double.NaN, 2));
        Assert.Equal(0, TalentEconomy.ClampBonusSlots(double.PositiveInfinity, 2));
        Assert.Equal(0, TalentEconomy.ClampBonusSlots(double.NegativeInfinity, 2));
        // 坏配置（档位 ≤0）：不得把「不可购置」当成「不设上限」
        Assert.Equal(0, TalentEconomy.ClampBonusSlots(5.0, 0));
        Assert.Equal(0, TalentEconomy.ClampBonusSlots(5.0, -3));
    }

    [Fact]
    public void FilterOvercharged_DropsUnknownDuplicatesAndExcessEntries()
    {
        var known = new[] { "power_shot", "extra_life", "homing" };

        // 未知 id 丢弃：名单是「该节点允许 MaxLevel+1」的唯一许可来源，未知 id 抬高不了任何节点
        Assert.Equal(new[] { "extra_life" }, TalentEconomy.FilterOvercharged(new[] { "ghost", "extra_life" }, known, 3));
        // 重复 id 去重：否则同一节点占掉多个名额（玩家反而少一次风险加点）
        Assert.Equal(new[] { "extra_life" }, TalentEconomy.FilterOvercharged(new[] { "extra_life", "extra_life" }, known, 3));
        // 名额上限截断：手改档塞满 27 个节点 = 每个节点白拿一级
        Assert.Equal(new[] { "power_shot" }, TalentEconomy.FilterOvercharged(new[] { "power_shot", "extra_life" }, known, 1));
        // 名额 ≤0（配置/坏档）不得放行任何条目
        Assert.Empty(TalentEconomy.FilterOvercharged(new[] { "extra_life" }, known, 0));
        Assert.Empty(TalentEconomy.FilterOvercharged(new[] { "extra_life" }, known, -3));
        Assert.Empty(TalentEconomy.FilterOvercharged(Array.Empty<string>(), known, 3));
    }
}
