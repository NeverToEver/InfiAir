using System;
using InfiAir.Core;
using InfiAir.Core.Visual;
using Xunit;

namespace InfiAir.Core.Tests.Visual;

/// <summary>节奏算式契约测试（`DESIGN_BASELINE` §2.12 的准入判据 1 与 5 的算式面）：拍相位、
/// 拍点包络、呼吸频率映射与全屏振幅硬线是后续各批动效共用的取值口。
/// 守的静默错误＝护栏缺失：相位算成 NaN 时整屏着色器输出跟着 NaN（整块黑或白，引擎不报错）；
/// 振幅越线（≥10% 亮度差的慢呼吸变成「闪」）纯属观感退化，无头门禁里没有任何信号。</summary>
public sealed class RhythmTests
{
    /// <summary>攻段占比（生产取值：balance `effects.motion.beat_attack`）。</summary>
    private const double Attack = 0.08;

    /// <summary>放段占比（生产取值：balance `effects.motion.beat_release`）。</summary>
    private const double Release = 0.20;

    [Fact]
    public void BeatPhase_WrapsAtBeatBoundary_AndHandlesNegativePosition()
    {
        // 120BPM ＝ 0.5s 一拍
        Assert.Equal(0.0, Rhythm.BeatPhase(0.0, 120.0), 6);
        Assert.Equal(0.5, Rhythm.BeatPhase(0.25, 120.0), 6);
        Assert.Equal(0.0, Rhythm.BeatPhase(0.5, 120.0), 6); // 整拍回绕到拍首，不回 1.0
        Assert.Equal(0.4, Rhythm.BeatPhase(1.2, 120.0), 6);
        // 负位置是真实场景（音乐位置在曲目起始前 / 播放位置回退）：按同一算式取拍内小数部分，
        // -0.1s 落在上一拍的 0.8 处，整拍负位置落拍首——相位永远不出 [0,1)
        Assert.Equal(0.8, Rhythm.BeatPhase(-0.1, 120.0), 6);
        Assert.Equal(0.0, Rhythm.BeatPhase(-0.5, 120.0), 6);
        // 偏移只是把相位基准整体平移（音乐前奏空拍、动画对拍）
        Assert.Equal(0.0, Rhythm.BeatPhase(0.25, 120.0, offsetSeconds: 0.25), 6);
        Assert.Equal(0.7, Rhythm.BeatPhase(0.6, 120.0, offsetSeconds: 0.25), 6);
    }

    [Fact]
    public void BeatPhase_StaysInUnitRange_AcrossBpmAndPosition()
    {
        // 相位是乘算除算的结果，越界只表现为「相位恒 1 或负」——消费方按它取包络时会整段无脉冲
        foreach (var bpm in new[] { 84.0, 120.0, 150.0, 233.0 })
        {
            for (var i = -200; i <= 200; i++)
            {
                var phase = Rhythm.BeatPhase(i * 0.137, bpm);
                Assert.InRange(phase, 0.0, 1.0);
                Assert.True(phase < 1.0, $"{bpm}BPM 下相位 {phase} 未回绕到 [0,1)");
            }
        }
    }

    [Theory]
    [InlineData(double.NaN, 120.0)]
    [InlineData(double.PositiveInfinity, 120.0)]
    [InlineData(double.NegativeInfinity, 120.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(1.0, -120.0)]
    [InlineData(1.0, double.NaN)]
    [InlineData(1.0, double.PositiveInfinity)]
    [InlineData(double.MaxValue, double.MaxValue)] // 无限长曲目：中间量溢出成 ∞ 也要落回确定值
    public void BeatPhase_BadInput_FallsBackToZero(double position, double bpm)
    {
        var phase = Rhythm.BeatPhase(position, bpm);
        Assert.Equal(0.0, phase);
        Assert.False(double.IsNaN(phase));
    }

    [Fact]
    public void BeatPulse_ZeroOutsideWindow_PeaksAtAttackEnd()
    {
        // 拍点包络：拍首起振、攻段末到峰、放段衰减，窗口之外（拍内其余部分）为 0——
        // 「窗口外非零」会把拍点读成常驻亮起，正是「静止几乎不动、变化时才动」的反面
        Assert.Equal(0.0, Rhythm.BeatPulse(0.0, Attack, Release), 6);
        Assert.Equal(1.0, Rhythm.BeatPulse(Attack, Attack, Release), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(Attack + Release, Attack, Release), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(0.9, Attack, Release), 6);
        // 拍内相位域是 [0,1)：负相位与下一拍相位都不继承本拍窗口
        Assert.Equal(0.0, Rhythm.BeatPulse(-0.1, Attack, Release), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(1.0, Attack, Release), 6);
    }

    [Fact]
    public void BeatPulse_RisesFast_FallsSlow_AndStaysContinuous()
    {
        var prev = Rhythm.BeatPulse(0.0, Attack, Release);
        for (var i = 1; i <= 1000; i++)
        {
            var phase = i / 1000.0;
            var value = Rhythm.BeatPulse(phase, Attack, Release);
            Assert.InRange(value, 0.0, 1.0);
            // 逐点连续性：包络若在窗口边界跳变（例如放段直接截断到 0），逐帧观感就是高频闪烁
            Assert.True(
                Math.Abs(value - prev) < 0.05,
                $"相位 {phase:0.###} 处包络跳变 {Math.Abs(value - prev):0.###}");
            prev = value;
        }

        Assert.True(Rhythm.BeatPulse(0.02, Attack, Release) < Rhythm.BeatPulse(0.06, Attack, Release), "攻段未上升");
        Assert.True(Rhythm.BeatPulse(0.10, Attack, Release) > Rhythm.BeatPulse(0.20, Attack, Release), "放段未下降");
        Assert.True(Rhythm.BeatPulse(0.20, Attack, Release) > Rhythm.BeatPulse(0.27, Attack, Release), "放段尾部未收敛到 0");
    }

    [Fact]
    public void BeatPulse_DegenerateParameters_FallBackToZero()
    {
        // 窗口为零＝关掉拍点：返回 0 而不是「常亮 1」——「一律返回 1」的实现会让
        // 动效强度 0 与减少闪光双双失效，而画面只表现为「更亮一点」，没有判据能发现
        Assert.Equal(0.0, Rhythm.BeatPulse(0.0, 0.0, 0.0), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(0.5, 0.0, 0.0), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(0.0, -0.1, Release), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(0.0, Attack, -0.2), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(double.NaN, Attack, Release), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(0.0, double.NaN, Release), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(0.0, Attack, double.PositiveInfinity), 6);
        // 只有一侧为零：零攻段＝拍首即峰，零放段＝到达峰值即归零，都不产出 NaN
        Assert.Equal(1.0, Rhythm.BeatPulse(0.0, 0.0, Release), 6);
        Assert.Equal(0.5, Rhythm.BeatPulse(0.1, 0.0, Release), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(0.2, 0.0, Release), 6);
        Assert.Equal(0.5, Rhythm.BeatPulse(0.05, 0.1, 0.0), 6);
        Assert.Equal(0.0, Rhythm.BeatPulse(0.1, 0.1, 0.0), 6);
        // 窗口长于一拍：相位域仍截在 [0,1)，不夸口「下一拍还有余量」
        Assert.Equal(0.5, Rhythm.BeatPulse(0.9, 0.6, 0.6), 6);
    }

    [Fact]
    public void BeatHz_IsBpmOverSixty_WithBadInputGuard()
    {
        Assert.Equal(2.0, Rhythm.BeatHz(120.0), 6); // 战斗曲
        Assert.Equal(2.5, Rhythm.BeatHz(150.0), 6); // Boss 曲：全屏判据里最接近阈值的一档
        Assert.Equal(1.4, Rhythm.BeatHz(84.0), 6);  // 基地曲
        Assert.Equal(0.0, Rhythm.BeatHz(0.0), 6);
        Assert.Equal(0.0, Rhythm.BeatHz(-120.0), 6);
        Assert.Equal(0.0, Rhythm.BeatHz(double.NaN), 6);
        Assert.Equal(0.0, Rhythm.BeatHz(double.PositiveInfinity), 6);
    }

    [Fact]
    public void BreathHz_MapsDangerBetweenBounds_AndClamps()
    {
        // 危险度越界按端点处理；映射单调不降（越危险呼吸越快）
        Assert.Equal(0.5, Rhythm.BreathHz(0.0, 0.5, 1.2), 6);
        Assert.Equal(1.2, Rhythm.BreathHz(1.0, 0.5, 1.2), 6);
        Assert.Equal(0.85, Rhythm.BreathHz(0.5, 0.5, 1.2), 6);
        Assert.Equal(0.5, Rhythm.BreathHz(-3.0, 0.5, 1.2), 6);
        Assert.Equal(1.2, Rhythm.BreathHz(4.0, 0.5, 1.2), 6);
        var prev = 0.0;
        for (var i = 0; i <= 20; i++)
        {
            var hz = Rhythm.BreathHz(i / 20.0, 0.5, 1.2);
            Assert.True(hz >= prev, $"危险度 {i / 20.0:0.##} 处呼吸频率回落（{prev} → {hz}）");
            prev = hz;
        }

        // 上下限写反：结果仍落在两者之间（不会冒出负频率）
        Assert.Equal(1.2, Rhythm.BreathHz(1.0, 1.2, 0.5), 6);
        Assert.Equal(0.5, Rhythm.BreathHz(0.0, 1.2, 0.5), 6);
        // 全负区间：频率钳到 0（负频率＝反相振荡，消费方按它取相位只会读到反了的一拍）
        Assert.Equal(0.0, Rhythm.BreathHz(0.5, -1.0, -0.5), 6);
        Assert.Equal(0.0, Rhythm.BreathHz(double.NaN, 0.5, 1.2), 6);
        Assert.Equal(0.0, Rhythm.BreathHz(0.5, double.NaN, 1.2), 6);
        Assert.Equal(0.0, Rhythm.BreathHz(0.5, 0.5, double.PositiveInfinity), 6);
    }

    [Fact]
    public void BreathValue_OscillatesInUnitRange_FromTroughAtZero()
    {
        // 起振点固定在谷（t=0 值为 0）：全屏呼吸的默认态即「亮度不动」，接上音乐不会先亮一下
        Assert.Equal(0.0, Rhythm.BreathValue(0.0, 1.0), 6);
        Assert.Equal(1.0, Rhythm.BreathValue(0.5, 1.0), 6);
        Assert.Equal(0.0, Rhythm.BreathValue(1.0, 1.0), 6); // 整周期回谷
        foreach (var hz in new[] { 0.5, 0.85, 1.2, 2.5 })
        {
            for (var i = 0; i <= 120; i++)
            {
                Assert.InRange(Rhythm.BreathValue(i / 60.0, hz), 0.0, 1.0);
            }
        }

        // 无频率＝无呼吸（返回 0 而非 0.5：消费方乘振幅后应是「零偏移」，不是「偏移一半」）
        Assert.Equal(0.0, Rhythm.BreathValue(0.3, 0.0), 6);
        Assert.Equal(0.0, Rhythm.BreathValue(0.3, -1.0), 6);
        Assert.Equal(0.0, Rhythm.BreathValue(double.NaN, 1.0), 6);
        Assert.Equal(0.0, Rhythm.BreathValue(double.PositiveInfinity, 1.0), 6);
        Assert.Equal(0.0, Rhythm.BreathValue(0.3, double.PositiveInfinity), 6);
    }

    [Fact]
    public void FullScreenAmplitude_ClampsAboveHardLine_AndReturnsBelowUnchanged()
    {
        // 硬线本身必须留在 XAG 118「闪」的定义线（≥10% 亮度变化）之下——把 cap 抬到定义线以上，
        // 下面的钳制就形同虚设（钳制正确、判据失效）
        Assert.Equal(0.09, Rhythm.FullScreenPeakToTroughCap, 6);
        Assert.True(
            Rhythm.FullScreenPeakToTroughCap < 0.10,
            $"全屏峰谷硬线 {Rhythm.FullScreenPeakToTroughCap} 不低于 XAG 118 的 10% 定义线");
        var halfCap = Rhythm.FullScreenPeakToTroughCap / 2.0;

        // 上半：超过上限被钳回（20% 屏上 9% 峰谷差就是「闪」；半振幅按峰谷差的一半算）
        Assert.Equal(halfCap, Rhythm.FullScreenHalfAmplitude(0.5), 6);
        Assert.Equal(halfCap, Rhythm.FullScreenHalfAmplitude(Rhythm.FullScreenPeakToTroughCap), 6);
        Assert.True(2.0 * Rhythm.FullScreenHalfAmplitude(0.5) <= Rhythm.FullScreenPeakToTroughCap);
        // 下半：未超上限原样返回（一律返回上限的实现会让「调小振幅」与动效强度缩放在这里失效）
        Assert.Equal(0.025, Rhythm.FullScreenHalfAmplitude(0.025), 6); // 生产默认 effects.motion.breath_amp
        Assert.Equal(halfCap, Rhythm.FullScreenHalfAmplitude(halfCap), 6);
        Assert.Equal(0.0, Rhythm.FullScreenHalfAmplitude(0.0), 6);
        // 非法入参落回 0：NaN 振幅会让整屏输出跟着 NaN（黑屏/白屏，引擎侧零报错）
        Assert.Equal(0.0, Rhythm.FullScreenHalfAmplitude(-0.02), 6);
        Assert.Equal(0.0, Rhythm.FullScreenHalfAmplitude(double.NaN), 6);
        Assert.Equal(0.0, Rhythm.FullScreenHalfAmplitude(double.PositiveInfinity), 6);
    }
}
