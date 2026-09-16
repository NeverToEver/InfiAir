using System;
using InfiAir.Core;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>Boss 头部版式契约：逃跑倒计时读数盒必须落在血条背板下缘之外。
/// 原实现把倒计时写死在 y=78，而背板是 y 4..92——数字下半截压在背板下边框上；
/// 这类布局错在无头探针里既不崩也不报错（结算页 dim 只压暗、不清除），
/// 故判据落在这里：背板与倒计时的顶位同出一源，单测断两盒不相交。</summary>
public sealed class HudLayoutTests
{
    [Fact]
    public void BossCountdown_DoesNotOverlapPlate()
    {
        Assert.False(HudLayout.BoxesOverlap(
            HudLayout.BossCountdownTop,
            HudLayout.BossCountdownLineHeight,
            HudLayout.BossPlateTop,
            HudLayout.BossPlateHeight));
        // 顶位必须在背板下缘之外（相切也不许：读数贴框读成背板的一部分）
        Assert.True(
            HudLayout.BossCountdownTop >= HudLayout.BossPlateBottom,
            $"倒计时顶 {HudLayout.BossCountdownTop} 未越出背板下缘 {HudLayout.BossPlateBottom}");
    }

    [Fact]
    public void BoxesOverlap_DetectsIntersectionAndIgnoresTouching()
    {
        // 判据自身要能失败：部分相交/包含判相交，相切与分离判不相交
        Assert.True(HudLayout.BoxesOverlap(0.0f, 10.0f, 5.0f, 10.0f));
        Assert.True(HudLayout.BoxesOverlap(5.0f, 10.0f, 0.0f, 10.0f));
        Assert.True(HudLayout.BoxesOverlap(0.0f, 100.0f, 40.0f, 10.0f));
        Assert.False(HudLayout.BoxesOverlap(0.0f, 10.0f, 10.0f, 10.0f));
        Assert.False(HudLayout.BoxesOverlap(10.0f, 10.0f, 0.0f, 10.0f));
        // 回归形态（原实现的裸值：倒计时顶 78、行盒 30、背板 4..92）必须判相交——
        // 判据抓不到它要抓的那一版就等于没判
        Assert.True(HudLayout.BoxesOverlap(78.0f, 30.0f, 4.0f, 88.0f));
    }

    [Fact]
    public void Hud_TakesBossHeaderPositionsFromCore()
    {
        // 结构性判定：Hud 的落位必须引用 core 算式，不得再写一份字面量
        // （两处各写一份时改背板高不会带动倒计时位，正是要防的形态）
        var src = RepoFiles.Read("csharp/godot/Hud.cs");
        Assert.Contains("new Vector2(-100.0f, HudLayout.BossCountdownTop)", src, StringComparison.Ordinal);
        Assert.Contains("new Vector2(-320.0f, HudLayout.BossPlateTop)", src, StringComparison.Ordinal);
        Assert.Contains("new Vector2(640.0f, HudLayout.BossPlateHeight)", src, StringComparison.Ordinal);
    }
}
