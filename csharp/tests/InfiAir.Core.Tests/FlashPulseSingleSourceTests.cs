using System;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>全屏尺度脉冲的单源判据（读源码文本的结构性判定，不改运行态）：
/// 频率与「减少闪光」处置的单源在 core FlashBudget，引擎侧必须引用它、不得留本地副本。
/// 副本形态正是这条债务的原始形态——`private const float FuelPulseHz = 2.4f` 写在 HUD 里时，
/// 改一行即超阈值而任何门禁都无信号；而两侧取值相等时运行期分辨不出谁是副本，
/// 故只能判「不留副本」这一结构事实。</summary>
public sealed class FlashPulseSingleSourceTests
{
    [Fact]
    public void LowFuelPulse_ReadsCoreFrequencyAndAmplitudeBudget()
    {
        var src = RepoFiles.Read("csharp/godot/Hud.cs");
        Assert.Contains("FlashBudget.LowFuelHz", src, StringComparison.Ordinal);
        Assert.Contains("FlashBudget.Amplitude", src, StringComparison.Ordinal);
        // 低血晕影（全屏回退路径）也走同一处预算——它的 id 必须出现在 HUD 里，
        // 否则「登记了却没接线」这一形态无人判（登记行只证明表里有这一项）
        Assert.Contains("PulseId.HudLowHpVignette", src, StringComparison.Ordinal);
        Assert.DoesNotContain("FuelPulseHz", src, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfusionOverlay_ReadsCoreFrequencyAndAmplitudeBudget()
    {
        var src = RepoFiles.Read("csharp/godot/ConfusionEvent.cs");
        Assert.Contains("FlashBudget.ConfusionHz", src, StringComparison.Ordinal);
        Assert.Contains("FlashBudget.Amplitude", src, StringComparison.Ordinal);
        Assert.Contains("PulseId.FogConfusion", src, StringComparison.Ordinal);
        Assert.DoesNotContain("PulsePeriod", src, StringComparison.Ordinal);
    }

    [Fact]
    public void StarfieldTwinkle_ReadsCoreFrequencyAndAmplitudeBudget()
    {
        var src = RepoFiles.Read("csharp/godot/Starfield.cs");
        Assert.Contains("FlashBudget.StarfieldTwinkleHz", src, StringComparison.Ordinal);
        Assert.Contains("FlashBudget.Amplitude", src, StringComparison.Ordinal);
        Assert.Contains("PulseId.StarfieldTwinkle", src, StringComparison.Ordinal);
    }

    [Fact]
    public void OneShotFlashes_ConsultCoreGateAtEverySite()
    {
        // 一次性闪光（就绪脉冲 / 血条掉段闪 / Boss 阶段闪）不进频率表，但门控同样只有一处：
        // 三处站点各自留一份布尔副本正是本条债务的原始形态（改坏只表现为「开减闪后仍闪一下」，
        // 引擎侧零报错）。探针采样只在它跑得到的那条路径上成立，这里钉住「谁都别再自己写一遍」。
        var socket = RepoFiles.Read("csharp/godot/AbilitySocket.cs");
        Assert.Contains("FlashBudget.AllowsOneShot", socket, StringComparison.Ordinal);
        Assert.Contains("OneShotFlashId.AbilityReadyPulse", socket, StringComparison.Ordinal);

        var bar = RepoFiles.Read("csharp/godot/SegmentedBar.cs");
        Assert.Contains("FlashBudget.AllowsOneShot", bar, StringComparison.Ordinal);
        Assert.Contains("OneShotFlashId.HealthBarSegment", bar, StringComparison.Ordinal);

        var hud = RepoFiles.Read("csharp/godot/Hud.cs");
        Assert.Contains("FlashBudget.AllowsOneShot", hud, StringComparison.Ordinal);
        Assert.Contains("OneShotFlashId.BossPhase", hud, StringComparison.Ordinal);
        // 能力槽两个实例都必须在开关上接线：冲刺槽在无增幅的局里锁着（探针采样不到它），
        // 漏接这一行时它会在减少闪光下照闪，而没有别的判据能发现
        Assert.Contains("_dashSocket.SetReduceFlash", hud, StringComparison.Ordinal);
        Assert.Contains("_parrySocket.SetReduceFlash", hud, StringComparison.Ordinal);
    }
}
