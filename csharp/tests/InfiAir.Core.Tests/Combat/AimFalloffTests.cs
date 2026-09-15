using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>AimFalloff 契约测试：辅助瞄准共用的距离衰减分段曲线（生产值 peak 400 / end 1400 /
/// min 0.3，balance.json player.aim_assist.falloff）。
/// 开火弱追踪与准星磁吸共用同一条曲线，原先藏在 Player 节点里；两种坏法都静默——
/// 峰值内提前衰减会让近距离追踪变钝（玩家只觉得「手感差」），末端地板写丢则远距离追踪直接归零。</summary>
public sealed class AimFalloffTests
{
    private const float Peak = 400.0f;
    private const float End = 1400.0f;
    private const float Min = 0.3f;

    [Theory]
    [InlineData(0.0f)]
    [InlineData(120.0f)]
    [InlineData(399.9f)]
    [InlineData(400.0f)]
    public void FullStrengthInsidePeak(float distance)
    {
        // 端点闭合：<= peak 全额（含恰好 400）
        Assert.Equal(1.0f, AimFalloff.Evaluate(distance, Peak, End, Min));
    }

    [Theory]
    [InlineData(1400.0f)]
    [InlineData(2000.0f)]
    public void FloorsAtMinFromEndOutward(float distance)
    {
        // 端点闭合：>= end 取地板（恰好 1400 也在地板上）
        Assert.Equal(Min, AimFalloff.Evaluate(distance, Peak, End, Min));
    }

    [Theory]
    [InlineData(500.0f, 0.93f)]
    [InlineData(650.0f, 0.825f)]
    [InlineData(900.0f, 0.65f)]
    [InlineData(1150.0f, 0.47500002f)] // 四分位与四分之三位的浮点定值
    [InlineData(1399.0f, 0.3007f)]
    public void LinearInBetweenWithFullStrengthFloor(float distance, float expected)
    {
        Assert.Equal(expected, AimFalloff.Evaluate(distance, Peak, End, Min));
    }

    [Fact]
    public void EndpointValueIsContinuousWithFloor()
    {
        // 末端左极限收敛到地板：分段点两侧不许出现台阶
        Assert.Equal(Min, AimFalloff.Evaluate(1399.9999f, Peak, End, Min), 4);
    }

    [Fact]
    public void EndPointReturnsMinVerbatimWithoutInterpolation()
    {
        // 端点上取地板本身而不是插值到地板：min=0.1 时 Lerp(1, 0.1, 1) 会差 1 ulp 变成 0.100000024，
        // 端点走分支、不进除法是「地板是精确值」的契约（磁吸强度按 min 直接读表的场景依赖它）。
        Assert.Equal(0.1f, AimFalloff.Evaluate(End, Peak, End, 0.1f));
        Assert.Equal(0.1f, AimFalloff.Evaluate(2000.0f, Peak, End, 0.1f));
    }

    [Fact]
    public void MinScalesWholeRamp()
    {
        Assert.Equal(0.75f, AimFalloff.Evaluate(900.0f, Peak, End, 0.5f), 4); // 中点 = (1 + min) / 2
        Assert.Equal(0.0f, AimFalloff.Evaluate(2000.0f, Peak, End, 0.0f)); // 地板可为 0（磁吸关）
        Assert.Equal(0.2f, AimFalloff.Evaluate(1200.0f, Peak, End, 0.0f), 4);
    }

    [Fact]
    public void DegeneratePeakAtOrBeyondEndDoesNotDivide()
    {
        // 非法配置（peak >= end）只由分支守卫兜住，不进除法：两端点各自返回，不产生 NaN
        Assert.Equal(1.0f, AimFalloff.Evaluate(100.0f, 100.0f, 100.0f, Min));
        Assert.Equal(Min, AimFalloff.Evaluate(200.0f, 100.0f, 100.0f, Min));
        Assert.Equal(1.0f, AimFalloff.Evaluate(75.0f, 100.0f, 50.0f, Min)); // peak 先命中
        Assert.False(float.IsNaN(AimFalloff.Evaluate(500.0f, 100.0f, 50.0f, Min)));
    }
}
