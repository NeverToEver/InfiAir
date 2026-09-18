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
}
