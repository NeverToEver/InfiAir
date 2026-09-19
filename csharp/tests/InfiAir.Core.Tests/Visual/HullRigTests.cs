using System;
using InfiAir.Core.Visual;
using Xunit;

namespace InfiAir.Core.Tests.Visual;

/// <summary>机体形态算式契约测试（<c>DESIGN_BASELINE</c> §2.18）：机炮收放、炮管热量、散热排气、
/// 翼尖涡流。守的静默错误＝状态读数被无限累积或永不复位：热量不钳上限会让炮管颜色饱和后再无变化
/// （读数消失），热度不衰减会让「刚打完」与「一直没打」看起来一样，`hold_time` 语义写反会让机体
/// 常态张口；非有限入参必须自愈为静息态——NaN 会顺着颜色/变换链污染整棵贴图子树且引擎零报错。</summary>
public sealed class HullRigTests
{
    // ---------------- DeployTarget ----------------

    [Fact]
    public void DeployTarget_HoldsOpenWithinWindow_ThenRetracts()
    {
        // 保持窗 0.55s：窗内展开、窗外收拢，边界取窗末（age == hold 即收）
        Assert.Equal(1.0, HullRig.DeployTarget(0.0, 0.55));
        Assert.Equal(1.0, HullRig.DeployTarget(0.30, 0.55));
        Assert.Equal(1.0, HullRig.DeployTarget(0.549, 0.55));
        Assert.Equal(0.0, HullRig.DeployTarget(0.55, 0.55));
        Assert.Equal(0.0, HullRig.DeployTarget(3.0, 0.55));
    }

    [Fact]
    public void DeployTarget_ZeroHoldTime_MeansNeverDeploy()
    {
        // hold_time = 0 是「关闭该动效」的合法口径：机体永远收拢，不出现开机张口
        Assert.Equal(0.0, HullRig.DeployTarget(0.0, 0.0));
        Assert.Equal(0.0, HullRig.DeployTarget(-1.0, 0.0));
        Assert.Equal(0.0, HullRig.DeployTarget(0.0, -1.0));
    }

    [Fact]
    public void DeployTarget_InvalidInputs_ReturnRetracted()
    {
        Assert.Equal(0.0, HullRig.DeployTarget(double.NaN, 0.55));
        Assert.Equal(0.0, HullRig.DeployTarget(0.1, double.NaN));
        Assert.Equal(0.0, HullRig.DeployTarget(double.PositiveInfinity, 0.55));
    }

    // ---------------- HeatAfterShot / HeatDecay ----------------

    [Fact]
    public void HeatAfterShot_Accumulates_AndClampsAtOne()
    {
        Assert.Equal(0.14, HullRig.HeatAfterShot(0.0, 0.14), 9);
        Assert.Equal(0.28, HullRig.HeatAfterShot(0.14, 0.14), 9);
        // 连发累积到上限即停（超出不溢出——溢出会让着色算式拿到 >1 的插值因子）
        Assert.Equal(1.0, HullRig.HeatAfterShot(0.95, 0.14), 9);
        Assert.Equal(1.0, HullRig.HeatAfterShot(1.0, 0.14), 9);
        // 入参坏值自愈：已坏的热度从 0 起算，非正步进不改现状
        Assert.Equal(0.14, HullRig.HeatAfterShot(double.NaN, 0.14), 9);
        Assert.Equal(0.5, HullRig.HeatAfterShot(0.5, 0.0), 9);
        Assert.Equal(0.5, HullRig.HeatAfterShot(0.5, double.NaN), 9);
    }

    [Fact]
    public void HeatDecay_IsExponentialAndFrameRateIndependent()
    {
        // 一步 1/60s 与两步 1/120s 结果一致（指数衰减的帧率无关性）
        var one = HullRig.HeatDecay(1.0, 1.1, 1.0 / 60.0);
        var two = HullRig.HeatDecay(HullRig.HeatDecay(1.0, 1.1, 1.0 / 120.0), 1.1, 1.0 / 120.0);
        Assert.Equal(Math.Exp(-1.0 / 66.0), one, 9);
        Assert.Equal(one, two, 9);
        // τ 秒整：恰衰减到 1/e
        Assert.Equal(Math.Exp(-1.0), HullRig.HeatDecay(1.0, 1.1, 1.1), 9);
        // 单调递减且恒为正（指数不触零，读数靠阈值截断而非归零事件）
        var cur = 1.0;
        for (var i = 0; i < 240; i++)
        {
            var next = HullRig.HeatDecay(cur, 1.1, 1.0 / 60.0);
            Assert.True(next < cur && next > 0.0);
            cur = next;
        }
    }

    [Fact]
    public void HeatDecay_ZeroOrNegativeTau_MeansInstantCooling()
    {
        // tau ≤ 0 是「关掉热度层」的口径：热度立刻归零 ＝ 炮管回到冷态，不残留读数
        Assert.Equal(0.0, HullRig.HeatDecay(1.0, 0.0, 1.0 / 60.0));
        Assert.Equal(0.0, HullRig.HeatDecay(1.0, -2.0, 1.0 / 60.0));
        // 停帧不动（delta ≤ 0）：不是「冷却到 0」
        Assert.Equal(0.8, HullRig.HeatDecay(0.8, 1.1, 0.0), 9);
        Assert.Equal(0.8, HullRig.HeatDecay(0.8, 1.1, double.NaN), 9);
        // 坏热度自愈为冷态
        Assert.Equal(0.0, HullRig.HeatDecay(double.NaN, 1.1, 1.0 / 60.0));
        // 超界热度先钳到满再衰减（不放大）：与从 1 起算逐位一致
        Assert.Equal(HullRig.HeatDecay(1.0, 1.1, 1.1), HullRig.HeatDecay(2.0, 1.1, 1.1), 9);
    }

    // ---------------- VentStrength ----------------

    [Fact]
    public void VentStrength_ZeroBelowThreshold_ThenRampsToFull()
    {
        Assert.Equal(0.0, HullRig.VentStrength(0.0, 0.25));
        Assert.Equal(0.0, HullRig.VentStrength(0.25, 0.25));
        // 阈值到 1 之间线性，中点 = 0.5
        Assert.Equal(0.5, HullRig.VentStrength(0.625, 0.25), 9);
        Assert.Equal(1.0, HullRig.VentStrength(1.0, 0.25), 9);
        // 阈值 1：永不排气（关闭该读数）
        Assert.Equal(0.0, HullRig.VentStrength(1.0, 1.0));
        Assert.Equal(0.0, HullRig.VentStrength(1.0, 2.0));
        // 坏入参不排气
        Assert.Equal(0.0, HullRig.VentStrength(double.NaN, 0.25));
        Assert.Equal(0.0, HullRig.VentStrength(0.9, double.NaN));
    }

    // ---------------- VortexStrength ----------------

    [Fact]
    public void VortexStrength_UsesMagnitude_SoBothTurnDirectionsRead()
    {
        // 左右横向加速度都要出涡流（取绝对值；写成有符号会让一个方向永远不出现）
        Assert.Equal(0.0, HullRig.VortexStrength(0.35, 0.35));
        Assert.True(HullRig.VortexStrength(0.9, 0.35) > 0.0);
        Assert.Equal(HullRig.VortexStrength(0.9, 0.35), HullRig.VortexStrength(-0.9, 0.35), 9);
        // 满偏达上限且不越界（超速机动不放大）
        Assert.Equal(1.0, HullRig.VortexStrength(1.0, 0.35), 9);
        Assert.Equal(1.0, HullRig.VortexStrength(-1.5, 0.35), 9);
        // 阈值 1：永不出现
        Assert.Equal(0.0, HullRig.VortexStrength(1.0, 1.0));
        // 坏入参不出涡流
        Assert.Equal(0.0, HullRig.VortexStrength(double.NaN, 0.35));
        Assert.Equal(0.0, HullRig.VortexStrength(0.9, double.NaN));
    }

    // ---------------- HeatTintMix ----------------

    [Fact]
    public void HeatTintMix_TwoSegments_SteelToAmberToWhite()
    {
        var (a0, w0) = HullRig.HeatTintMix(0.0);
        Assert.Equal(0.0, a0, 9);
        Assert.Equal(0.0, w0, 9);
        // 前半段：钢灰→琥珀
        var (a1, w1) = HullRig.HeatTintMix(0.5);
        Assert.Equal(1.0, a1, 9);
        Assert.Equal(0.0, w1, 9);
        var (ah, wh) = HullRig.HeatTintMix(0.25);
        Assert.Equal(0.5, ah, 9);
        Assert.Equal(0.0, wh, 9);
        // 后半段：琥珀→白热
        var (a2, w2) = HullRig.HeatTintMix(0.75);
        Assert.Equal(1.0, a2, 9);
        Assert.Equal(0.5, w2, 9);
        var (a3, w3) = HullRig.HeatTintMix(1.0);
        Assert.Equal(1.0, a3, 9);
        Assert.Equal(1.0, w3, 9);
        // 坏入参＝冷态
        var (an, wn) = HullRig.HeatTintMix(double.NaN);
        Assert.Equal(0.0, an, 9);
        Assert.Equal(0.0, wn, 9);
    }
}
