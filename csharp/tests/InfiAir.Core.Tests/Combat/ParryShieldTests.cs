using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>ParryShield 契约测试：弧光弹反盾的扇形几何判据（生产值 radius 60 / arc_deg 360，
/// balance.json player.parry）。
/// 判据原先就地写在 `Player.OnParryShieldEntered` 里，引擎侧只能靠探针覆盖，而它的两处边界
/// 「半径上取等号」「弧边取等号」反过来写都不产生任何运行信号——表现为贴脸弹反偶尔不生效，
/// 正是玩家最难归因的一类。机头方向取 <c>Vector2.Up.Rotated(0).Angle()</c>＝-π/2（机身正上方）。</summary>
public sealed class ParryShieldTests
{
    private const float Radius = 60.0f;
    private const float FullArc = 360.0f;

    /// <summary>机头朝上时的方向弧度（引擎侧 Vector2.Up.Rotated(0).Angle()）。</summary>
    private static readonly float NoseUp = -MathF.PI / 2.0f;

    /// <summary>把「距离 + 相对机头的偏角」换算成相对位移（机头朝上：偏角 0 即正上方）。</summary>
    private static (float Dx, float Dy) At(float distance, float offsetDeg)
    {
        var angle = NoseUp + offsetDeg * MathF.PI / 180.0f;
        return (distance * MathF.Cos(angle), distance * MathF.Sin(angle));
    }

    [Fact]
    public void InsideRadius_AlongNose_IsCovered()
    {
        var (dx, dy) = At(30.0f, 0.0f);
        Assert.True(ParryShield.Covers(dx, dy, NoseUp, Radius, FullArc));
    }

    /// <summary>恰在半径上算命中（判据是 dist &gt; radius 才拒）——改写成 ≥ 会把「贴脸擦过」整批判掉。</summary>
    [Fact]
    public void ExactlyOnRadius_IsCovered()
    {
        var (dx, dy) = At(Radius, 0.0f);
        Assert.True(ParryShield.Covers(dx, dy, NoseUp, Radius, FullArc));
    }

    [Fact]
    public void OutsideRadius_IsNotCovered()
    {
        var (dx, dy) = At(Radius + 1.0f, 0.0f);
        Assert.False(ParryShield.Covers(dx, dy, NoseUp, Radius, FullArc));
    }

    /// <summary>整角 360°＝全向盾：机头背后与正侧都要覆盖（半角换算写错会变成只护前半）。
    /// 判据是 |角差| ≤ 半角，取到 ±π 的等号——正后方（180°）落在等号上，必须算命中。</summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(90.0f)]
    [InlineData(180.0f)]
    [InlineData(-90.0f)]
    public void FullArc_CoversEveryDirection(float offsetDeg)
    {
        var (dx, dy) = At(40.0f, offsetDeg);
        Assert.True(ParryShield.Covers(dx, dy, NoseUp, Radius, FullArc));
    }

    /// <summary>整角 120°＝接受域 ±60°：弧边取等号，越界 1° 判掉。</summary>
    [Theory]
    [InlineData(59.0f, true)]
    [InlineData(60.0f, true)]
    [InlineData(61.0f, false)]
    [InlineData(-60.0f, true)]
    [InlineData(-61.0f, false)]
    public void HalfArcBoundary_TakesEquality(float offsetDeg, bool covered)
    {
        var (dx, dy) = At(30.0f, offsetDeg);
        Assert.Equal(covered, ParryShield.Covers(dx, dy, NoseUp, Radius, 120.0f));
    }

    /// <summary>两条判据是「且」：半径内但弧外仍不命中（只判距离会把盾变成全向）
    /// ——这正是弹反盾的边界弹在弧外进入重叠区时的处置。</summary>
    [Fact]
    public void InsideRadiusButOutsideArc_IsNotCovered()
    {
        var (dx, dy) = At(30.0f, 120.0f);
        Assert.False(ParryShield.Covers(dx, dy, NoseUp, Radius, 120.0f));
    }

    /// <summary>机头方向参与判定：同一位移在机身转 90° 后落到弧外。
    /// 写成「固定以屏幕上方为轴」的实现会在此红（机头朝右时，右前方的弹反不生效）。</summary>
    [Fact]
    public void NoseAngle_RotatesWithShip()
    {
        var (dx, dy) = At(30.0f, 0.0f); // 相对「机头朝上」的正前方
        Assert.True(ParryShield.Covers(dx, dy, NoseUp, Radius, 120.0f));
        // 机头转 90°（朝右）后，同一世界位移落在左前方 90° 处——超出 ±60° 接受域
        Assert.False(ParryShield.Covers(dx, dy, NoseUp + MathF.PI / 2.0f, Radius, 120.0f));
    }
}
