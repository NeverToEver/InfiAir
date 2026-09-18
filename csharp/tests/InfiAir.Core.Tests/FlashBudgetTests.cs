using System;
using System.Text.Json;
using InfiAir.Core;
using InfiAir.Core.Visual;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>FlashBudget 契约测试：全屏尺度闪烁的频率与「减少闪光」处置只有一份登记表。
/// 守的静默错误＝`DESIGN_BASELINE` §1.9.1 的「闪烁频率低于 WCAG 2.3.1 阈值」只是散文声明：
/// 改动前改一行 `Hud.FuelPulseHz` 即超阈值，而门禁、单测、探针全绿。
/// 两半互补：只判频率会让「一律照闪」混过，只判归零会让「一律不闪」混过。</summary>
public sealed class FlashBudgetTests
{
    [Fact]
    public void Sources_RowOrderMatchesPulseIdEnum()
    {
        // 行序即枚举序：错位时 Source(id) 会取到别人的频率，而调用点看到的仍是「合法值」
        Assert.Equal(Enum.GetValues<PulseId>().Length, FlashBudget.Sources.Length);
        for (var i = 0; i < FlashBudget.Sources.Length; i++)
        {
            Assert.Equal((PulseId)i, FlashBudget.Sources[i].Id);
            Assert.Equal(FlashBudget.Sources[i].Hz, FlashBudget.Source((PulseId)i).Hz, 5);
        }
    }

    [Fact]
    public void FullScreenPulses_AreWithinFlashLimit()
    {
        // 上半：全屏尺度项一律低于阈值（表内自检，不依赖任何外部来源）
        foreach (var src in FlashBudget.Sources)
        {
            Assert.True(
                src.Hz < FlashBudget.HzLimit,
                $"{src.Id} 的 {src.Hz:0.###}Hz 越过全屏尺度闪烁阈值 {FlashBudget.HzLimit}Hz（XAG 118）");
        }

        Assert.Equal(3.0f, FlashBudget.HzLimit, 5);
    }

    /// <summary>本轮动效（`effects.motion`）的取值面判据（`DESIGN_BASELINE` §2.12 判据 1 与 5）：
    /// 三档 BPM 换算出的**整拍**频率一律 < 3Hz；全屏呼吸的默认半振幅不越 core Rhythm 的硬线。
    /// 守的静默错误＝表里登记的只有呼吸频率，「有人把 BPM 调到 200」（整拍 3.33Hz）时全屏节拍
    /// 越过阈值，而频率表、减闪归零两半都照常全绿。</summary>
    [Fact]
    public void MotionBalance_BeatAndBreathStayUnderHardLines()
    {
        using var doc = JsonDocument.Parse(RepoFiles.Read("data/balance.json"));
        // 段缺失即抛（取不到判据时必须显式失败，不得静默空转）
        var motion = doc.RootElement.GetProperty("effects").GetProperty("motion");

        // 上半：三档曲速的整拍频率（视觉节拍只做整拍脉冲，半拍仅局部元素、不进本判据面）
        foreach (var key in new[] { "bpm_battle", "bpm_boss", "bpm_base" })
        {
            Assert.True(motion.TryGetProperty(key, out var node), $"effects.motion.{key} 不存在（键被改名？）");
            var hz = Rhythm.BeatHz(node.GetDouble());
            Assert.True(
                hz < FlashBudget.HzLimit,
                $"effects.motion.{key} 的整拍频率 {hz:0.###}Hz 越过全屏尺度闪烁阈值 {FlashBudget.HzLimit}Hz（XAG 118）");
        }

        // 下半：全屏呼吸的默认半振幅（峰谷差＝2×半振幅）必须本来就低于定义线——不能靠运行期
        // 钳制兜着：值越线时画面仍合规、但「默认值已合法」这条事实已经没了
        var amp = motion.GetProperty("breath_amp").GetDouble();
        Assert.True(
            2.0 * amp < 0.10,
            $"effects.motion.breath_amp={amp} 的峰谷亮度差达到 XAG 118 的「闪」定义线（≥10%）");
        Assert.Equal(amp, Rhythm.FullScreenHalfAmplitude(amp), 6);
    }

    [Fact]
    public void ReduceFlash_ZeroesEveryRegisteredPulse()
    {
        // 两半互补的另半边：减少闪光下振幅为零，且非减少闪光时原样返回
        // （后半句同样要判——helper 一律返回 0 的实现会让「一律不闪」平凡通过）
        foreach (var src in FlashBudget.Sources)
        {
            Assert.True(src.ZeroUnderReduceFlash, $"{src.Id} 登记为减少闪光下不归零——加例外须同时改本判据与理由");
            Assert.Equal(0.0f, FlashBudget.Amplitude(1.0f, src.Id, reduceFlash: true));
            Assert.Equal(0.5f, FlashBudget.Amplitude(0.5f, src.Id, reduceFlash: false));
        }
    }

    [Fact]
    public void OneShots_RowOrderMatchesEnum_AndGateHasBothHalves()
    {
        // 一次性闪光无频率可言（不进频率表），门控同样单源在这里：行序错位时 AllowsOneShot
        // 会读到别人的处置，而调用点看到的仍是「合法值」
        Assert.Equal(Enum.GetValues<OneShotFlashId>().Length, FlashBudget.OneShots.Length);
        for (var i = 0; i < FlashBudget.OneShots.Length; i++)
        {
            Assert.Equal((OneShotFlashId)i, FlashBudget.OneShots[i].Id);

            var id = (OneShotFlashId)i;
            Assert.True(
                FlashBudget.OneShots[i].SuppressedByReduceFlash,
                $"{id} 登记为减闪下不抑制——加例外须同时改本判据与理由");
            Assert.False(FlashBudget.AllowsOneShot(id, reduceFlash: true));
            // 非减闪那半同样要判：一律返回 false 的实现会让「减少闪光下仍闪一下」退化成
            // 「什么都不闪」——玩家没开减闪时本该看到这些确认反馈
            Assert.True(FlashBudget.AllowsOneShot(id, reduceFlash: false));
        }
    }
}
