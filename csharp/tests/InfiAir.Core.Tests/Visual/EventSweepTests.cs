using System.Text.Json;
using InfiAir.Core.Tests;
using InfiAir.Core.Visual;
using Xunit;

namespace InfiAir.Core.Tests.Visual;

/// <summary>扫光算式契约测试（`DESIGN_BASELINE` §1.9.1「静止几乎不动、变化时才动」＋ §2.12 判据 6）：
/// 进度必须能走到 1 并停在那里由生产侧归零，档位必须按最高命中档判定。
/// 守的静默错误＝扫光停在半途常亮（换算写成「永不归 1」或非法入参返回 0 之外的值时，
/// 着色器里的高斯带会永久驻留，画面上像一块多余的亮斑，无头门禁里没有任何信号），
/// 以及档位判反（100 连被 10 连档吃掉，玩家只看到「每 10 连一样强」）。</summary>
public sealed class EventSweepTests
{
    [Fact]
    public void Progress01_ReachesOneAndStaysThere()
    {
        const float Duration = 0.35f;
        Assert.Equal(0.0f, EventSweep.Progress01(0.0f, Duration));
        Assert.Equal(0.5f, EventSweep.Progress01(Duration * 0.5f, Duration), 5);
        Assert.Equal(1.0f, EventSweep.Progress01(Duration, Duration));
        Assert.Equal(1.0f, EventSweep.Progress01(Duration * 3.0f, Duration));
    }

    [Fact]
    public void Progress01_ZeroDurationCompletesInsteadOfStalling()
    {
        // 时长为 0（坏配置）：瞬间走完，不是除零 NaN，也不是停在起点
        Assert.Equal(1.0f, EventSweep.Progress01(0.0f, 0.0f));
        Assert.Equal(1.0f, EventSweep.Progress01(0.1f, -1.0f));
        // 时钟未来值（未推进）：停在起点
        Assert.Equal(0.0f, EventSweep.Progress01(-0.2f, 0.35f));
    }

    [Fact]
    public void Progress01_NonFiniteFallsBackToNoSweep()
    {
        Assert.Equal(0.0f, EventSweep.Progress01(float.NaN, 0.35f));
        Assert.Equal(0.0f, EventSweep.Progress01(0.1f, float.PositiveInfinity));
    }

    [Fact]
    public void MilestoneTier_TakesHighestMatchingTier()
    {
        // 断连（ComboChanged(0)）与负数不是击杀节拍：不扫光
        Assert.Equal(0, EventSweep.MilestoneTier(0));
        Assert.Equal(0, EventSweep.MilestoneTier(-5));
        Assert.Equal(0, EventSweep.MilestoneTier(9));
        Assert.Equal(0, EventSweep.MilestoneTier(49));
        Assert.Equal(1, EventSweep.MilestoneTier(10));
        Assert.Equal(1, EventSweep.MilestoneTier(30));
        Assert.Equal(1, EventSweep.MilestoneTier(110));
        // 50 是 10 的整倍：必须取更高的档，不能被 10 档吃掉
        Assert.Equal(2, EventSweep.MilestoneTier(50));
        Assert.Equal(2, EventSweep.MilestoneTier(150));
        Assert.Equal(3, EventSweep.MilestoneTier(100));
        Assert.Equal(3, EventSweep.MilestoneTier(200));
    }

    [Fact]
    public void AmplitudeFactor_GrowsWithTierAndRejectsBadBoost()
    {
        Assert.Equal(1.0f, EventSweep.AmplitudeFactor(0, 0.35f));
        Assert.Equal(1.35f, EventSweep.AmplitudeFactor(1, 0.35f), 5);
        Assert.Equal(2.05f, EventSweep.AmplitudeFactor(3, 0.35f), 5);
        // 越界档位按上限算（不会算出天文幅度把高斯带糊满整只舰体）
        Assert.Equal(2.05f, EventSweep.AmplitudeFactor(9, 0.35f), 5);
        // 负/非有限的加成退化为「档位无差异」，不产生负幅度（负幅度在着色器里是减光）
        Assert.Equal(1.0f, EventSweep.AmplitudeFactor(3, -0.35f));
        Assert.Equal(1.0f, EventSweep.AmplitudeFactor(3, float.NaN));
    }

    [Fact]
    public void Balance_SweepStaysShortAndVisible()
    {
        using var doc = JsonDocument.Parse(RepoFiles.Read("data/balance.json"));
        var motion = doc.RootElement.GetProperty("effects").GetProperty("motion");

        // 时长型键：>0（0 会让 Progress01 直接跳 1，等于没有扫过过程）且短于 1s
        //（再长就从「一次性事件反馈」变成常驻动效，与 §1.9.1 的定位相悖）
        var sweepTime = motion.GetProperty("sweep_time").GetDouble();
        Assert.True(
            sweepTime > 0.0 && sweepTime <= 1.0,
            $"effects.motion.sweep_time={sweepTime} 越出「一次性扫光」的时长域 (0, 1]");

        // 幅度型键：非负（负幅度在着色器里是减光）
        Assert.True(motion.GetProperty("sweep_amp").GetDouble() >= 0.0, "effects.motion.sweep_amp 不得为负");
        Assert.True(
            motion.GetProperty("sweep_milestone_boost").GetDouble() >= 0.0,
            "effects.motion.sweep_milestone_boost 不得为负");
    }
}
