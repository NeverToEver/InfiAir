using InfiAir.Core;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>TankLiquid 契约测试：量槽液面的两条几何护栏——波谷不越底、最薄可三角化液层。
/// 这两条守的是「低油量时填充整块消失」的静默错误：多边形一自交/退化，Godot 三角化失败并
/// 整块不画，既不崩也不报可读错，且无头门禁走 dummy 渲染不执行 _Draw，抓不到。</summary>
public sealed class TankLiquidTests
{
    /// <summary>HUD 里燃料量槽的内腔高度（控件 52 − Pad 3×2）。</summary>
    private const float InnerHeight = 46.0f;

    /// <summary>常态液面幅度与晃动叠加后的幅度（占内腔高度比例，对齐 FuelTank 常量）。</summary>
    private const float BaseAmpRatio = 0.040f;
    private const float SloshAmpRatio = 0.150f;

    [Fact]
    public void Amplitude_NeverReachesFloor_AcrossLevelSweep()
    {
        // 液面最高点（液位 + 波幅）必须落在内腔底之上，否则填充多边形与底边自交。
        for (var i = 0; i <= 100; i++)
        {
            var level = i / 100.0f;
            var levelY = InnerHeight * (1.0f - level); // 内腔顶为 0 时的液位
            foreach (var ratio in new[] { BaseAmpRatio, SloshAmpRatio })
            {
                var amp = TankLiquid.WaveAmplitude(InnerHeight, level, ratio);
                Assert.True(amp >= 0.0f, $"波幅为负：level={level} amp={amp}");
                Assert.True(
                    levelY + amp <= InnerHeight + 1e-4f,
                    $"波谷越过内腔底：level={level} ratio={ratio} levelY={levelY} amp={amp}");
            }
        }
    }

    [Fact]
    public void Amplitude_IsCappedByFillHeight_NotByRequestedRatio()
    {
        // 低液位时上限由液层厚度决定：请求 15% 波幅、液位只剩 5%，波幅必被压到 5% 以下。
        var amp = TankLiquid.WaveAmplitude(InnerHeight, 0.05f, SloshAmpRatio);
        Assert.True(amp <= InnerHeight * 0.05f + 1e-4f, $"未按液层厚度收窄：{amp}");
        Assert.True(amp < InnerHeight * SloshAmpRatio, "低液位仍用了满幅");
    }

    [Fact]
    public void Amplitude_KeepsRequestedRatio_WhenTankIsMid()
    {
        // 中位液量两侧余量都足，波幅保持请求值（护栏不压低正常液面起伏）。
        var amp = TankLiquid.WaveAmplitude(InnerHeight, 0.5f, BaseAmpRatio);
        Assert.Equal(InnerHeight * BaseAmpRatio, amp, 4);
    }

    [Fact]
    public void Amplitude_NeverReachesCeiling_AcrossLevelSweep()
    {
        // 对称护栏：液面最低点（液位 − 波幅）必须落在内腔顶之下。满油时液位即内腔顶，
        // 任何正波幅都会把液面与弯月线画到内腔之外（压过外框上缘）——与「波谷越底」同族，
        // 此前只堵了底侧。该越顶不触发三角化失败，但绘制溢出内腔。
        for (var i = 0; i <= 100; i++)
        {
            var level = i / 100.0f;
            var levelY = InnerHeight * (1.0f - level);
            foreach (var ratio in new[] { BaseAmpRatio, SloshAmpRatio })
            {
                var amp = TankLiquid.WaveAmplitude(InnerHeight, level, ratio);
                Assert.True(
                    levelY - amp >= -1e-4f,
                    $"液面越过内腔顶：level={level} ratio={ratio} levelY={levelY} amp={amp}");
            }
        }
    }

    [Fact]
    public void Amplitude_IsZeroAtFullTank_NoOverdrawAboveCavity()
    {
        // 满油：液位即内腔顶，上方余量为 0 → 波幅归零（液面静止，不探出内腔）。
        Assert.Equal(0.0f, TankLiquid.WaveAmplitude(InnerHeight, 1.0f, BaseAmpRatio));
    }

    [Fact]
    public void Amplitude_NonPositiveHeightOrRatio_IsZero()
    {
        Assert.Equal(0.0f, TankLiquid.WaveAmplitude(0.0f, 0.5f, BaseAmpRatio));
        Assert.Equal(0.0f, TankLiquid.WaveAmplitude(-10.0f, 0.5f, BaseAmpRatio));
        Assert.Equal(0.0f, TankLiquid.WaveAmplitude(InnerHeight, 0.0f, BaseAmpRatio));
        Assert.Equal(0.0f, TankLiquid.WaveAmplitude(InnerHeight, 0.5f, -1.0f));
    }

    [Fact]
    public void Amplitude_NonFiniteLevelOrRatio_StaysFinite()
    {
        // 液位来自燃料读数（可被 NaN 污染）：Clamp01 对 NaN 原样放行 → 波幅 NaN →
        // 填充多边形顶点 NaN/自交，Godot 三角化整块失败且静默不画。非有限一律按 0 液位处理。
        Assert.Equal(0.0f, TankLiquid.WaveAmplitude(InnerHeight, float.NaN, BaseAmpRatio));
        Assert.Equal(0.0f, TankLiquid.WaveAmplitude(InnerHeight, float.NaN, SloshAmpRatio));
        Assert.True(float.IsFinite(TankLiquid.WaveAmplitude(InnerHeight, 0.5f, float.NaN)));
        Assert.True(float.IsFinite(TankLiquid.WaveAmplitude(InnerHeight, 0.5f, float.PositiveInfinity)));
        Assert.True(float.IsFinite(TankLiquid.WaveAmplitude(float.NaN, 0.5f, BaseAmpRatio)));
    }

    [Fact]
    public void HasDrawableFill_RejectsDegenerateLayer()
    {
        // 见底附近：液层薄到不能安全三角化，调用方只画液面线。
        Assert.False(TankLiquid.HasDrawableFill(InnerHeight, 0.0f));
        Assert.False(TankLiquid.HasDrawableFill(InnerHeight, 0.002f));
        Assert.False(TankLiquid.HasDrawableFill(InnerHeight, 0.02f)); // 46 × 0.02 = 0.92px
        // 正常低量与满油：都要画填充。
        Assert.True(TankLiquid.HasDrawableFill(InnerHeight, 0.05f)); // 2.3px
        Assert.True(TankLiquid.HasDrawableFill(InnerHeight, 1.0f));
        // 非正内腔高不得误判为可画。
        Assert.False(TankLiquid.HasDrawableFill(0.0f, 1.0f));
    }
}
