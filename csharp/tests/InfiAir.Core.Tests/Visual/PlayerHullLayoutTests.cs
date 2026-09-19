using InfiAir.Core.Visual;
using Xunit;

namespace InfiAir.Core.Tests.Visual;

/// <summary>玩家机挂点布局契约测试（<c>DESIGN_BASELINE</c> §2.17 挂点单位口径）：尺寸越过可读线、
/// 成对挂点分居两侧。守的静默错误＝世界缩放被叠加两遍——挂点挂在贴图节点下（局部单位已含
/// `world_scale`），再乘一次时全部挂点等比缩到亚像素，引擎零报错、画面就是「没有这些细节」。</summary>
public sealed class PlayerHullLayoutTests
{
    /// <summary>出厂默认世界缩放（`balance.json world_scale`）：护栏必须在这一档成立。</summary>
    private const double ShippedWorldScale = 0.4;

    [Fact]
    public void AllHullLights_AreReadableAtShippedWorldScale()
    {
        foreach (var a in new[]
        {
            PlayerHullLayout.NavPort, PlayerHullLayout.NavStarboard, PlayerHullLayout.NavStrobe,
            PlayerHullLayout.RcsLeft, PlayerHullLayout.RcsRight, PlayerHullLayout.RcsRetro,
        })
        {
            Assert.True(
                PlayerHullLayout.IsReadable(a, ShippedWorldScale),
                $"{a.Name} 在 world_scale={ShippedWorldScale} 下只有 {PlayerHullLayout.WorldSize(a, ShippedWorldScale):F2}px，低于可读线 {PlayerHullLayout.MinReadablePx}px");
        }
    }

    [Fact]
    public void NavLights_AreSeparatedEnoughToReadPortFromStarboard()
    {
        var sep = PlayerHullLayout.Separation(PlayerHullLayout.NavPort, PlayerHullLayout.NavStarboard, ShippedWorldScale);
        Assert.True(
            sep >= PlayerHullLayout.MinNavSeparationPx,
            $"两盏舷灯相距仅 {sep:F2}px（下限 {PlayerHullLayout.MinNavSeparationPx}px）——低于此值读不出「左红右绿」");
        // 左红右绿：横坐标一负一正，且等高（等高才读作一对）
        Assert.True(PlayerHullLayout.NavPort.X < 0.0 && PlayerHullLayout.NavStarboard.X > 0.0);
        Assert.Equal(PlayerHullLayout.NavPort.Y, PlayerHullLayout.NavStarboard.Y, 9);
    }

    [Fact]
    public void RcsNozzles_AreSymmetricAndOffCentreLine()
    {
        Assert.Equal(-PlayerHullLayout.RcsLeft.X, PlayerHullLayout.RcsRight.X, 9);
        Assert.Equal(PlayerHullLayout.RcsLeft.Y, PlayerHullLayout.RcsRight.Y, 9);
        // 侧向喷口必须离开机体中线：贴在中线上就分不出「向左还是向右点火」
        var sep = PlayerHullLayout.Separation(PlayerHullLayout.RcsLeft, PlayerHullLayout.RcsRight, ShippedWorldScale);
        Assert.True(sep > 0.0);
        Assert.True(PlayerHullLayout.RcsLeft.X < 0.0);
    }

    /// <summary>贴图骨架的机尾端：254×254 画布的 (127,236) 换到中心原点坐标系。</summary>
    private const double TailEndY = 109.0;

    [Fact]
    public void EngineNozzles_SitInTheTailBand_NotMidHull()
    {
        // 尾焰从喷口出：喷口锚点必须落在主翼尖之后、机尾端之前的**尾段**。
        // 守的静默错误＝锚点写进机身中部——尾焰照样喷、粒子照样亮，但看起来是从机腹/翼根
        // 冒出来的（标题屏悬挂展示就曾把尾焰写在 (±66,114)：X 外挂到机身之外、Y 落在机身中段）。
        Assert.Equal(-PlayerHullLayout.EngineLeft.X, PlayerHullLayout.EngineRight.X, 9);
        Assert.Equal(PlayerHullLayout.EngineLeft.Y, PlayerHullLayout.EngineRight.Y, 9);
        Assert.True(
            PlayerHullLayout.EngineLeft.Y > PlayerHullLayout.WingtipLeft.Y,
            $"喷口 Y={PlayerHullLayout.EngineLeft.Y} 未越过主翼尖 Y={PlayerHullLayout.WingtipLeft.Y}——落在机身中段");
        Assert.True(
            PlayerHullLayout.EngineLeft.Y < TailEndY,
            $"喷口 Y={PlayerHullLayout.EngineLeft.Y} 越出机尾端 Y={TailEndY}");
        // 双发在机身内（不外挂）：喷口比机炮挂点更靠中线
        Assert.True(
            System.Math.Abs(PlayerHullLayout.EngineLeft.X) < System.Math.Abs(PlayerHullLayout.GunLeft.X),
            "喷口比机炮挂点还靠外——那不是机身内的双发");
        Assert.True(
            PlayerHullLayout.IsReadable(PlayerHullLayout.EngineLeft, ShippedWorldScale),
            $"喷管环在 world_scale={ShippedWorldScale} 下只有 {PlayerHullLayout.WorldSize(PlayerHullLayout.EngineLeft, ShippedWorldScale):F2}px，低于可读线");
    }

    [Fact]
    public void Anchors_StayWithinTextureCanvas()
    {
        // 锚点越出贴图边界即为坐标口径写错（贴图中心是原点，半幅约 127 贴图像素）
        const double half = 127.0;
        foreach (var a in new[]
        {
            PlayerHullLayout.NavPort, PlayerHullLayout.NavStarboard, PlayerHullLayout.NavStrobe,
            PlayerHullLayout.RcsLeft, PlayerHullLayout.RcsRight, PlayerHullLayout.RcsRetro,
            PlayerHullLayout.DamageSmoke, PlayerHullLayout.EngineLeft, PlayerHullLayout.EngineRight,
        })
        {
            Assert.True(System.Math.Abs(a.X) <= half, $"{a.Name} 的 X 越出贴图半幅");
            Assert.True(System.Math.Abs(a.Y) <= half, $"{a.Name} 的 Y 越出贴图半幅");
        }
    }

    [Fact]
    public void ShapeAnchors_AreReadableAndSymmetric()
    {
        // 形态层（§2.18）的细长件走全长判据：炮管缩到 28% 的收拢态仍要读得出是「一根管子」，
        // 翼尖涡流要长到能读作拖尾而不是一个点
        foreach (var a in new[]
        {
            PlayerHullLayout.GunLeft, PlayerHullLayout.GunRight,
            PlayerHullLayout.WingtipLeft, PlayerHullLayout.WingtipRight,
        })
        {
            Assert.True(
                PlayerHullLayout.IsFeatureReadable(a, ShippedWorldScale),
                $"{a.Name} 全长只有 {PlayerHullLayout.WorldSize(a, ShippedWorldScale):F2}px，低于细长件可读线 {PlayerHullLayout.MinFeaturePx}px");
        }

        // 成对挂点镜像：写反一侧会让机炮只有半边伸出、涡流只有一翼划蒸气
        Assert.Equal(-PlayerHullLayout.GunLeft.X, PlayerHullLayout.GunRight.X, 9);
        Assert.Equal(PlayerHullLayout.GunLeft.Y, PlayerHullLayout.GunRight.Y, 9);
        Assert.Equal(-PlayerHullLayout.WingtipLeft.X, PlayerHullLayout.WingtipRight.X, 9);
        Assert.Equal(PlayerHullLayout.WingtipLeft.Y, PlayerHullLayout.WingtipRight.Y, 9);
        Assert.Equal(-PlayerHullLayout.VentLeft.X, PlayerHullLayout.VentRight.X, 9);

        // 翼尖挂在主翼最外缘：涡流起点离中线不足翼展的七成时会落在翼面上（读作机翼上的污点）
        var halfSpan = 127.0 * PlayerHullLayout.DesignScale * ShippedWorldScale;
        var tipX = PlayerHullLayout.WorldOffsetX(PlayerHullLayout.WingtipRight, ShippedWorldScale);
        Assert.True(tipX >= halfSpan * 0.7, $"翼尖涡流起点离中线仅 {tipX:F1}px（翼展半幅 {halfSpan:F1}px）——太靠内");
    }

    [Fact]
    public void WorldSize_FollowsWorldScaleLinearly_AndRejectsInvalidScale()
    {
        // 与挂点自身尺寸、设计系数、世界缩放的线性关系（写反一处即整层不可读）
        var d = PlayerHullLayout.WorldSize(PlayerHullLayout.NavPort, 0.4);
        Assert.Equal(10.0 * PlayerHullLayout.DesignScale * 0.4, d, 9);
        Assert.Equal(d * 2.0, PlayerHullLayout.WorldSize(PlayerHullLayout.NavPort, 0.8), 9);
        // 非法世界缩放：不可读、间距为 0，不抛
        Assert.Equal(0.0, PlayerHullLayout.WorldSize(PlayerHullLayout.NavPort, 0.0));
        Assert.Equal(0.0, PlayerHullLayout.WorldSize(PlayerHullLayout.NavPort, double.NaN));
        Assert.False(PlayerHullLayout.IsReadable(PlayerHullLayout.NavPort, 0.0));
        Assert.Equal(0.0, PlayerHullLayout.Separation(PlayerHullLayout.NavPort, PlayerHullLayout.NavStarboard, double.NaN));
    }
}
