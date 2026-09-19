namespace InfiAir.Core.Visual;

/// <summary>玩家机挂点几何（单源）：贴图像素坐标，中心 (0,0)、机头朝 -Y，与
/// <c>scripts/tools/generate_player_sprite.py</c> 头部注释的锚点同一坐标系。
/// size 是该挂点的**特征视觉尺寸**（光点＝直径，炮管与拖尾＝全长）。</summary>
public readonly record struct HullAnchor(string Name, double X, double Y, double Size);

/// <summary>
/// 玩家机挂点布局（纯逻辑，零 Godot 依赖）：坐标单源 + 可读性护栏。
///
/// 存在理由是一条静默错误：挂在贴图节点下的挂点，局部单位**已经**含了世界缩放
/// （贴图缩放 = 设计系数 × world_scale），再乘一次就把世界缩放叠了两遍——世界缩放 0.4 时
/// 机动喷口只剩 1.14px、两盏舷灯相距 1.7px，「左红右绿」这个读数等于不存在，而引擎侧
/// 零报错、零警告。把坐标与尺寸收在这里，引擎层只做换算与写节点，
/// <see cref="IsReadable"/> / <see cref="Separation"/> 就能把这条口径钉在测试里。
///
/// 设计约束（<c>DESIGN_BASELINE</c> §2.17 挂点单位口径 / §2.18）：本类只描述**表现层挂点**，
/// 不参与任何判定几何——碰撞圆、擦弹环、受击点与弹道的坐标不在本表内，改写本表不动玩法。
/// </summary>
public static class PlayerHullLayout
{
    /// <summary>机体设计系数：贴图 254px 在设计尺寸下读作约 165px 翼展。引擎侧写
    /// `Sprite2D.Scale = DesignScale × world_scale`；贴图子节点的局部单位因此等于贴图像素。</summary>
    public const double DesignScale = 0.65;

    /// <summary>挂点最小可读直径（世界像素）：低于此值的加色小点读作噪点或干脆看不见，
    /// 挂点尺寸必须让它在默认世界缩放下越过这条线。</summary>
    public const double MinReadablePx = 2.0;

    /// <summary>左右舷灯的最小世界间距（像素）：两盏灯挤在一起时「左红 / 右绿」这对读数
    /// 退化成「机体中线附近有个橙点」，等于没有。</summary>
    public const double MinNavSeparationPx = 20.0;

    // ---- 机身挂点（§2.17）----

    /// <summary>左舷航行灯：主翼外段，红。</summary>
    public static readonly HullAnchor NavPort = new("nav_port", -78.0, 52.0, 10.0);

    /// <summary>右舷航行灯：主翼外段，绿。</summary>
    public static readonly HullAnchor NavStarboard = new("nav_starboard", 78.0, 52.0, 10.0);

    /// <summary>尾部频闪灯：两台引擎喷口之间的机尾端，白。</summary>
    public static readonly HullAnchor NavStrobe = new("nav_strobe", 0.0, 100.0, 9.0);

    /// <summary>左侧机动喷口（RCS）：贴机身侧缘，按加速度反向点火。</summary>
    public static readonly HullAnchor RcsLeft = new("rcs_left", -20.0, 12.0, 13.0);

    /// <summary>右侧机动喷口（RCS）。</summary>
    public static readonly HullAnchor RcsRight = new("rcs_right", 20.0, 12.0, 13.0);

    /// <summary>机首反推喷口：制动时朝前点火。</summary>
    public static readonly HullAnchor RcsRetro = new("rcs_retro", 0.0, -104.0, 13.0);

    /// <summary>损伤烟发射点：机身后段（不遮机头）。</summary>
    public static readonly HullAnchor DamageSmoke = new("damage_smoke", 0.0, 62.0, 34.0);

    /// <summary>左引擎喷口：贴图骨架的双发喷口 (108, 230)，尾焰 / 喷口辉光的取位锚点。
    /// 尺寸取喷管环外径 12（≈两倍喷口半径），可读性判据按点状挂点口径。</summary>
    public static readonly HullAnchor EngineLeft = new("engine_left", -19.0, 103.0, 12.0);

    /// <summary>右引擎喷口（贴图 (146, 230)）。</summary>
    public static readonly HullAnchor EngineRight = new("engine_right", 19.0, 103.0, 12.0);

    // ---- 机体形态层挂点（§2.18）----

    /// <summary>左侧机炮荚舱：炮管收放的原点（炮管自原点向机头方向伸出）。</summary>
    public static readonly HullAnchor GunLeft = new("gun_left", -30.0, 0.0, 20.0);

    /// <summary>右侧机炮荚舱。</summary>
    public static readonly HullAnchor GunRight = new("gun_right", 30.0, 0.0, 20.0);

    /// <summary>左侧散热排气口：机背脊线中后段（生成器注释的脊线带 y≈118–200 上）。
    /// 不放在引擎喷口旁——那里已有常驻尾焰光晕，再点一盏读不出「正在放热」；
    /// 也不放在机体中线上——会被受击点光圈盖住。</summary>
    public static readonly HullAnchor VentLeft = new("vent_left", -10.0, 46.0, 12.0);

    /// <summary>右侧散热排气口。</summary>
    public static readonly HullAnchor VentRight = new("vent_right", 10.0, 46.0, 12.0);

    /// <summary>左翼尖：机动涡流的起始点（拖尾向机尾方向延伸）。
    /// 全长 40：约 10 世界像素，机体长的六分之一——细到 30（7.8px）时实测读不出「划出蒸气」。</summary>
    public static readonly HullAnchor WingtipLeft = new("wingtip_left", -115.0, 79.0, 40.0);

    /// <summary>右翼尖。</summary>
    public static readonly HullAnchor WingtipRight = new("wingtip_right", 115.0, 79.0, 40.0);

    /// <summary>细长挂点（炮管 / 拖尾）的最小可读**全长**（世界像素）：比点状挂点的可读线高，
    /// 因为细长件靠长度而非面积被读到——低于此值读作机身上的一个凸点。</summary>
    public const double MinFeaturePx = 4.0;

    /// <summary>贴图坐标 → 世界偏移：设计系数与世界缩放各乘一次（此处正是引擎侧不该重复的两次）。</summary>
    public static double WorldOffsetX(HullAnchor a, double worldScale) => a.X * DesignScale * worldScale;

    /// <summary>贴图坐标 → 世界偏移（纵轴）。</summary>
    public static double WorldOffsetY(HullAnchor a, double worldScale) => a.Y * DesignScale * worldScale;

    /// <summary>挂点在给定世界缩放下的视觉直径（世界像素）。</summary>
    public static double WorldSize(HullAnchor a, double worldScale) =>
        double.IsFinite(worldScale) && worldScale > 0.0 ? a.Size * DesignScale * worldScale : 0.0;

    /// <summary>两挂点的世界间距（像素）：用于钉住「舷灯分居两侧」这类成对读数。</summary>
    public static double Separation(HullAnchor a, HullAnchor b, double worldScale)
    {
        if (!double.IsFinite(worldScale) || worldScale <= 0.0)
        {
            return 0.0;
        }

        var dx = (a.X - b.X) * DesignScale * worldScale;
        var dy = (a.Y - b.Y) * DesignScale * worldScale;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>挂点在给定世界缩放下是否越过可读线（尺寸非有限或世界缩放非法即不可读）。</summary>
    public static bool IsReadable(HullAnchor a, double worldScale) => WorldSize(a, worldScale) >= MinReadablePx;

    /// <summary>细长挂点在给定世界缩放下是否越过可读线（判据是全长，见 <see cref="MinFeaturePx"/>）。</summary>
    public static bool IsFeatureReadable(HullAnchor a, double worldScale) => WorldSize(a, worldScale) >= MinFeaturePx;
}
