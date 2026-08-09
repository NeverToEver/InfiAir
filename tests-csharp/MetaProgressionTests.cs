using InfiAir.Core.Meta;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// MetaProgression / UpgradeDef（2026-08-09 局外成长 M1）单测：
/// 升级费用曲线、累计费用、上限判定、死亡结算公式与全部防御性钳制路径
/// （负值/除零/溢出/NaN——对齐 core 判型防御风格）。
/// </summary>
public sealed class MetaProgressionTests
{
    private static UpgradeDef Def(string id, int maxLevel, double baseCost, double growth)
        => new(id, maxLevel, baseCost, growth);

    // ---------------- UpgradeDef 防御性构造 ----------------

    [Fact]
    public void Def_ClampsInvalidInputs()
    {
        var def = new UpgradeDef("x", 0, -5.0, 0.5);
        Assert.Equal(1, def.MaxLevel);      // ≤0 视为 1
        Assert.Equal(0.0, def.BaseCost);    // 负价钳 0
        Assert.Equal(1.0, def.CostGrowth);  // ≤1 视为 1.0
    }

    [Fact]
    public void Def_NanCostGrowth_FallsBackToOne()
    {
        // Math.Max(NaN, x) 返回 NaN——构造必须显式判 NaN 回退
        var def = new UpgradeDef("x", 2, 10.0, double.NaN);
        Assert.Equal(1.0, def.CostGrowth);
    }

    // ---------------- 费用曲线 ----------------

    [Fact]
    public void CostForLevel_BaseAndGrowth()
    {
        var def = Def("rapid_fire", 3, 10, 1.6);
        Assert.Equal(10, MetaProgression.CostForLevel(def, 1)); // 10 × 1.6^0
        Assert.Equal(16, MetaProgression.CostForLevel(def, 2)); // 10 × 1.6^1
        Assert.Equal(26, MetaProgression.CostForLevel(def, 3)); // 10 × 1.6^2 = 25.6 → 26
    }

    [Fact]
    public void CostForLevel_OutOfRange_ReturnsZero()
    {
        var def = Def("x", 2, 10, 1.5);
        Assert.Equal(0, MetaProgression.CostForLevel(def, 0)); // 0 级不存在
        Assert.Equal(0, MetaProgression.CostForLevel(def, -1));
        Assert.Equal(0, MetaProgression.CostForLevel(def, 3)); // 超出 MaxLevel
        Assert.Equal(0, MetaProgression.CostForLevel(null, 1)); // def 空
    }

    [Fact]
    public void CostForLevel_Overflow_ClampsToMaxValue()
    {
        // 极大底价 × 巨大 growth^level 溢出至 inf → 钳 long.MaxValue
        var def = Def("x", 5, 1e18, 1e18);
        Assert.Equal(long.MaxValue, MetaProgression.CostForLevel(def, 5));
    }

    [Fact]
    public void TotalCostToLevel_Accumulates()
    {
        var def = Def("x", 3, 10, 1.6);
        Assert.Equal(0, MetaProgression.TotalCostToLevel(def, 0));
        Assert.Equal(10, MetaProgression.TotalCostToLevel(def, 1));
        Assert.Equal(26, MetaProgression.TotalCostToLevel(def, 2)); // 10 + 16
        Assert.Equal(52, MetaProgression.TotalCostToLevel(def, 3)); // 10 + 16 + 26
        Assert.Equal(52, MetaProgression.TotalCostToLevel(def, 99)); // 超出按 MaxLevel 封顶
        Assert.Equal(0, MetaProgression.TotalCostToLevel(def, -3));
        Assert.Equal(0, MetaProgression.TotalCostToLevel(null, 2));
    }

    [Fact]
    public void TotalCostToLevel_Overflow_ClampsToMaxValue()
    {
        var def = Def("x", 3, long.MaxValue - 1, 1.0);
        Assert.Equal(long.MaxValue, MetaProgression.TotalCostToLevel(def, 2)); // 累加不溢出回绕
    }

    // ---------------- 升级上限判定 ----------------

    [Fact]
    public void CanUpgrade_Boundaries()
    {
        var def = Def("x", 2, 10, 1.5);
        Assert.True(MetaProgression.CanUpgrade(def, 0));
        Assert.True(MetaProgression.CanUpgrade(def, 1));
        Assert.False(MetaProgression.CanUpgrade(def, 2)); // 已到顶
        Assert.False(MetaProgression.CanUpgrade(def, -1));
        Assert.False(MetaProgression.CanUpgrade(null, 0));
    }

    // ---------------- 死亡结算公式 ----------------

    [Fact]
    public void PointsForRun_Baseline()
    {
        // score 5000 → 5；boss 2×2 → 4；mission 3×1 → 3；合计 12
        Assert.Equal(12, MetaProgression.PointsForRun(5000, 2, 3, 1000, 2, 1));
        // 分数未达除数 → 0 + 0 + 0
        Assert.Equal(0, MetaProgression.PointsForRun(999, 0, 0, 1000, 2, 1));
        // 分数向下取整
        Assert.Equal(2, MetaProgression.PointsForRun(2500, 0, 0, 1000, 2, 1));
    }

    [Fact]
    public void PointsForRun_NegativeInputs_ClampToZero()
    {
        Assert.Equal(0, MetaProgression.PointsForRun(-5000, -2, -3, 1000, -2, -1));
        // 负 score 钳 0 按 0 计分，不影响其余加分项（boss 1×2 → 2）
        Assert.Equal(2, MetaProgression.PointsForRun(-5000, 1, 0, 1000, 2, 1));
    }

    [Fact]
    public void PointsForRun_ZeroDivisor_NoDivideByZero()
    {
        // scoreDivisor ≤0 → 按 1 处理：score 1000 → 1000
        Assert.Equal(1000, MetaProgression.PointsForRun(1000, 0, 0, 0, 2, 1));
        Assert.Equal(1000, MetaProgression.PointsForRun(1000, 0, 0, -5, 2, 1));
    }

    [Fact]
    public void PointsForRun_Overflow_ClampsToMaxValue()
    {
        Assert.Equal(long.MaxValue, MetaProgression.PointsForRun(long.MaxValue, long.MaxValue, long.MaxValue, 1, 2, 1));
    }
}
