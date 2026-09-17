using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>dash_strike 增幅的触及判定：半径闭区间、平方比较、欧氏距离。
/// 算式原先内联在 Player.TickDashStrike 的逐帧扫描里，坏了只表现为「冲刺打不到贴脸的敌机」。</summary>
public sealed class DashStrikeReachTests
{
    [Fact]
    public void Hits_RadiusBoundaryIsClosed()
    {
        // balance augments.dash_strike.radius = 80；贴沿算触及（严格小于会让边缘目标漏掉）
        Assert.True(DashStrikeReach.Hits(80.0f, 0.0f, 80.0f));
        Assert.False(DashStrikeReach.Hits(80.5f, 0.0f, 80.0f));
    }

    [Fact]
    public void Hits_ComparesSquaredDistanceToSquaredRadius()
    {
        // 3-4-5：距离 5 在半径 8 内。若把半径漏掉平方（dx²+dy² ≤ radius）此例判否
        Assert.True(DashStrikeReach.Hits(3.0f, 4.0f, 8.0f));

        // 欧氏距离而非轴向距离：双轴各 6（距离 8.49）在半径 8 外
        Assert.False(DashStrikeReach.Hits(6.0f, 6.0f, 8.0f));
    }
}
