using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 初代环带空间站「曙光」共享构件（docs/RETURN_HOME_CINEMATIC.md §0.2 几何 / §1.1 虚影变换）。
/// 纯静态工厂：build() 返回中心在原点的 Node2D，调用方负责 position/scale 与入树。
/// 三处复用：开场镜头 1（DESTROYED 实体毁灭态）、返航镜头 2/3/4（PHANTOM 全息虚影态）、
/// 基地 UI 背景层（PHANTOM，自行压 modulate.a）。粒子发射器 ≤96/个，与过场性能预算一致。
/// M6 全量迁移（2026-08-08 自 scripts/dawn_station.gd）。
/// 迁移注：内嵌类 _Dot 迁为同文件顶层类 DawnStationDot（C# 源生成器不支持内嵌类，BaseConsole 先例）；
/// PackedVector2Array → Vector2[]（批次规则 9，互操作语义一致）；静态方法/常量经 UPPER_SNAKE
/// 属性与 Get* 访问器兼容过渡期 GDScript 调用方（intro/return 过场接线后改 typed）。
/// </summary>
public partial class DawnStation : RefCounted
{
    /// <summary>实体毁灭态：冷钢蓝灰 + 破口残骸（开场镜头 1 现状配色，提取自 intro_cinematic._build_shot1）</summary>
    public enum Mode
    {
        /// <summary>全息虚影态：ADD 青蓝 + 慢呼吸 + 扫描带 + 数据流粒子 + 破口能量网格（§1.1 四层变换）</summary>
        Destroyed,

        Phantom,
    }

    public const float RingRadius = 260.0f; // 环体主弧半径
    public const float BreachStart = 0.5f; // 破口起角（rad，右下象限，识别锚点不可移动）
    public const float BreachEnd = 1.2f; // 破口止角

    // UPPER_SNAKE 兼容（过渡期接线/旧名引用；M7 后清理）
    public const float RING_RADIUS = RingRadius;
    public const float BREACH_START = BreachStart;
    public const float BREACH_END = BreachEnd;

    public static Node2D Build() => Build(Mode.Destroyed);

    public static Node2D Build(int mode) => Build((Mode)mode);

    /// <summary>构建站体（中心在原点，未定位）。DESTROYED = 开场镜头 1 现状视觉（纯提取，行为不变）；
    /// PHANTOM = §1.1 四层虚影变换全开（全息基底/扫描带/数据流/破口能量网格）。</summary>
    public static Node2D Build(Mode mode)
    {
        var station = new Node2D { Name = "DawnStation" };
        if (mode == Mode.Destroyed)
        {
            BuildDestroyed(station);
        }
        else
        {
            BuildPhantom(station);
        }

        return station;
    }

    /// <summary>纯色圆点构件（环心毂/辉光垫共用）。</summary>
    private static Node2D Dot(float radius, Color color, bool additive = true)
    {
        var dot = new DawnStationDot { Radius = radius, DotColor = color };
        if (additive)
        {
            var mat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            dot.Material = mat;
        }

        return dot;
    }

    private static Line2D Line(Vector2[] points, Color color, float width = 2.0f)
    {
        var l = new Line2D { Points = points, DefaultColor = color, Width = width };
        return l;
    }

    private static Polygon2D RectPoly(float w, float h, Color color)
    {
        var p = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(-w * 0.5f, -h * 0.5f),
                new Vector2(w * 0.5f, -h * 0.5f),
                new Vector2(w * 0.5f, h * 0.5f),
                new Vector2(-w * 0.5f, h * 0.5f),
            },
            Color = color,
        };
        return p;
    }

    private static CanvasItem Additive(CanvasItem item)
    {
        var mat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        item.Material = mat;
        return item;
    }

    private static GpuParticles2D Particles(Godot.Collections.Dictionary cfg)
    {
        var p = new GpuParticles2D
        {
            Amount = Mathf.Min(CfgInt(cfg, "amount", 32), 96), // 硬性上限：每发射器 ≤96
            Lifetime = CfgFloat(cfg, "lifetime", 1.0f),
            Explosiveness = CfgFloat(cfg, "explosiveness", 0.0f),
        };
        var mat = new ParticleProcessMaterial
        {
            Direction = CfgVector3(cfg, "direction", new Vector3(0.0f, -1.0f, 0.0f)),
            Spread = CfgFloat(cfg, "spread", 180.0f),
            InitialVelocityMin = CfgFloat(cfg, "vel_min", 100.0f),
            InitialVelocityMax = CfgFloat(cfg, "vel_max", 200.0f),
            Gravity = CfgVector3(cfg, "gravity", Vector3.Zero),
            DampingMin = CfgFloat(cfg, "damping_min", 0.0f),
            DampingMax = CfgFloat(cfg, "damping_max", 0.0f),
            ScaleMin = CfgFloat(cfg, "scale_min", 2.0f),
            ScaleMax = CfgFloat(cfg, "scale_max", 4.0f),
            Color = CfgColor(cfg, "color", new Color(0.0f, 0.83f, 1.0f, 0.35f)),
        };
        if (cfg.ContainsKey("emission_ring_radius"))
        {
            mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring;
            mat.EmissionRingAxis = new Vector3(0.0f, 0.0f, 1.0f);
            mat.EmissionRingRadius = CfgFloat(cfg, "emission_ring_radius", 260.0f);
            mat.EmissionRingInnerRadius = CfgFloat(cfg, "emission_ring_inner_radius", 0.0f);
            mat.EmissionRingHeight = 0.0f;
        }

        p.ProcessMaterial = mat;
        if (CfgBool(cfg, "additive", true))
        {
            Additive(p);
        }

        return p;
    }

    /// <summary>站体共享几何：环体主弧 + 内外廓细节环 + 舱段刻线 + 8 舱段 + 辐条 + 中心毂。
    /// palette 键：ring/detail/tick/seg/seg_edge/spoke/hub/hub_ring；additive=true 时全构件改叠加态。
    /// gaps（[[a0,a1],…]，rad）：主弧分段绘制留出缺口（虚影态破碎感）；空表 = 完整闭合环（毁灭态现状）。
    /// 返回 {"segments":…, "edges":…} 舱段引用（虚影态逐个掉线闪烁用；毁灭态忽略）。</summary>
    private static Godot.Collections.Dictionary BuildBody(
        Node2D station,
        Godot.Collections.Dictionary palette,
        bool additive,
        Godot.Collections.Array gaps)
    {
        var refs = new Godot.Collections.Dictionary
        {
            ["segments"] = new Godot.Collections.Array(),
            ["edges"] = new Godot.Collections.Array(),
        };
        var segments = refs["segments"].AsGodotArray();
        var edges = refs["edges"].AsGodotArray();
        if (gaps.Count == 0)
        {
            var ringPoints = new Vector2[48];
            for (var i = 0; i < 48; i++)
            {
                var a = Mathf.Tau * (float)i / 48.0f;
                ringPoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RING_RADIUS;
            }

            var ring = Line(ringPoints, PaletteColor(palette, "ring"), 26.0f);
            ring.Closed = true;
            if (additive)
            {
                Additive(ring);
            }

            station.AddChild(ring);
        }
        else
        {
            // 按 a0 升序排缺口（GDScript sort_custom 语义：a[0] < b[0]）
            var sortedGaps = new List<Vector2>();
            foreach (var gap in gaps)
            {
                var arr = gap.AsGodotArray();
                sortedGaps.Add(new Vector2(arr[0].AsSingle(), arr[1].AsSingle()));
            }

            sortedGaps.Sort((a, b) => a.X.CompareTo(b.X));
            var cursor = 0.0f;
            foreach (var gap in sortedGaps)
            {
                RingArc(station, PaletteColor(palette, "ring"), cursor, gap.X, additive);
                cursor = gap.Y;
            }

            RingArc(station, PaletteColor(palette, "ring"), cursor, Mathf.Tau, additive);
        }

        foreach (var rDetail in new[] { 232.0f, 288.0f })
        {
            var detailPoints = new Vector2[64];
            for (var i = 0; i < 64; i++)
            {
                var a = Mathf.Tau * (float)i / 64.0f;
                detailPoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rDetail;
            }

            var detail = Line(detailPoints, PaletteColor(palette, "detail"), 2.0f);
            detail.Closed = true;
            if (additive)
            {
                Additive(detail);
            }

            station.AddChild(detail);
        }

        // 舱段分块刻线：16 条径向短刻线跨环体
        for (var i = 0; i < 16; i++)
        {
            var a = Mathf.Tau * (float)i / 16.0f;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var tick = Line(new[] { dir * 244.0f, dir * 276.0f }, PaletteColor(palette, "tick"), 3.0f);
            if (additive)
            {
                Additive(tick);
            }

            station.AddChild(tick);
        }

        // 舱段矩形 ×8 + 描边 + 辐条（破损舱段缺失：0.4–1.4 rad）
        for (var i = 0; i < 8; i++)
        {
            var a = Mathf.Tau * (float)i / 8.0f;
            if (a > 0.4f && a < 1.4f)
            {
                continue;
            }

            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var seg = RectPoly(64.0f, 40.0f, PaletteColor(palette, "seg"));
            seg.Position = dir * RING_RADIUS;
            seg.Rotation = a + Mathf.Pi * 0.5f;
            if (additive)
            {
                Additive(seg);
            }

            station.AddChild(seg);
            segments.Add(seg);
            var segEdge = RectPoly(70.0f, 46.0f, PaletteColor(palette, "seg_edge"));
            segEdge.Position = seg.Position;
            segEdge.Rotation = seg.Rotation;
            if (additive)
            {
                Additive(segEdge);
            }

            station.AddChild(segEdge);
            station.MoveChild(segEdge, seg.GetIndex());
            edges.Add(segEdge);
            var spoke = Line(new[] { dir * 70.0f, dir * 240.0f }, PaletteColor(palette, "spoke"), 8.0f);
            if (additive)
            {
                Additive(spoke);
            }

            station.AddChild(spoke);
        }

        station.AddChild(Dot(66.0f, PaletteColor(palette, "hub"), additive));
        // 中心毂细节环
        var hubRingPoints = new Vector2[24];
        for (var i = 0; i < 24; i++)
        {
            var a = Mathf.Tau * (float)i / 24.0f;
            hubRingPoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 46.0f;
        }

        var hubRing = Line(hubRingPoints, PaletteColor(palette, "hub_ring"), 2.5f);
        hubRing.Closed = true;
        if (additive)
        {
            Additive(hubRing);
        }

        station.AddChild(hubRing);
        return refs;
    }

    /// <summary>主弧分段（gaps 模式）：a0→a1 弧段，段数按弧长等比（闭合整环 48 段基准）</summary>
    private static void RingArc(Node2D station, Color color, float a0, float a1, bool additive)
    {
        if (a1 - a0 < 0.05f)
        {
            return;
        }

        var segs = Mathf.Max(4, (int)((a1 - a0) / Mathf.Tau * 48.0f));
        var points = new Vector2[segs + 1];
        for (var i = 0; i <= segs; i++)
        {
            var a = Mathf.Lerp(a0, a1, (float)i / segs);
            points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RING_RADIUS;
        }

        var arc = Line(points, color, 26.0f);
        if (additive)
        {
            Additive(arc);
        }

        station.AddChild(arc);
    }

    /// <summary>破口锯齿轮廓顶点（两态共用：毁灭态=近黑填充，虚影态=亮线描边）</summary>
    private static Vector2[] JaggedPoints()
    {
        return new[]
        {
            new Vector2(Mathf.Cos(0.45f), Mathf.Sin(0.45f)) * 240.0f,
            new Vector2(Mathf.Cos(0.6f), Mathf.Sin(0.6f)) * 282.0f,
            new Vector2(Mathf.Cos(0.85f), Mathf.Sin(0.85f)) * 236.0f,
            new Vector2(Mathf.Cos(1.1f), Mathf.Sin(1.1f)) * 284.0f,
            new Vector2(Mathf.Cos(1.25f), Mathf.Sin(1.25f)) * 244.0f,
            new Vector2(Mathf.Cos(0.85f), Mathf.Sin(0.85f)) * 262.0f,
        };
    }

    /// <summary>实体毁灭态：现状配色 + 破口暗弧覆盖/锯齿填充/3 块剥落碎片外飘翻滚</summary>
    private static void BuildDestroyed(Node2D station)
    {
        BuildBody(
            station,
            new Godot.Collections.Dictionary
            {
                ["ring"] = new Color(0.38f, 0.45f, 0.58f),
                ["detail"] = new Color(0.55f, 0.65f, 0.8f, 0.5f),
                ["tick"] = new Color(0.18f, 0.22f, 0.3f),
                ["seg"] = new Color(0.48f, 0.56f, 0.68f),
                ["seg_edge"] = new Color(0.6f, 0.7f, 0.85f, 0.35f),
                ["spoke"] = new Color(0.3f, 0.36f, 0.48f),
                ["hub"] = new Color(0.28f, 0.34f, 0.45f),
                ["hub_ring"] = new Color(0.5f, 0.6f, 0.75f, 0.6f),
            },
            false,
            new Godot.Collections.Array());
        // 破损段：暗色弧覆盖出缺口（0.5–1.2 rad）
        var brokenPoints = new Vector2[7];
        for (var i = 0; i < 7; i++)
        {
            var a = BREACH_START + (BREACH_END - BREACH_START) * (float)i / 6.0f;
            brokenPoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RING_RADIUS;
        }

        station.AddChild(Line(brokenPoints, new Color(0.05f, 0.05f, 0.08f), 30.0f));
        // 破口锯齿边缘
        var jagged = new Polygon2D { Polygon = JaggedPoints(), Color = new Color(0.03f, 0.03f, 0.05f) };
        station.AddChild(jagged);
        // 破口剥落碎片：小多边形缓慢外飘 + 翻滚
        for (var k = 0; k < 3; k++)
        {
            var flake = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-8.0f, -5.0f),
                    new Vector2(9.0f, -3.0f),
                    new Vector2(5.0f, 7.0f),
                    new Vector2(-6.0f, 6.0f),
                },
                Color = new Color(0.3f, 0.36f, 0.46f),
            };
            var fa = 0.6f + 0.3f * k;
            flake.Position = new Vector2(Mathf.Cos(fa), Mathf.Sin(fa)) * 265.0f;
            station.AddChild(flake);
            // 往复段：外飘/翻滚到目标后返回起点（对齐虚影态碎片）——仅 set_loops 时循环重放
            // 立即完成，碎片冻结在首圈末位
            var ft = station.CreateTween().SetLoops();
            ft.TweenProperty(flake, "position", flake.Position + new Vector2(Mathf.Cos(fa), Mathf.Sin(fa)) * 60.0f, 2.0);
            ft.Parallel().TweenProperty(flake, "rotation", flake.Rotation + 2.5f, 2.0);
            ft.TweenProperty(flake, "position", flake.Position, 2.0);
            ft.Parallel().TweenProperty(flake, "rotation", flake.Rotation, 2.0);
        }
    }

    /// <summary>全息虚影态（§1.1）：四层变换——全息基底 / 扫描线光晕 / 数据流粒子 / 破口能量网格修补，
    /// 外加破碎感：主弧断弧缺口 ×3、舱段逐个掉线闪烁、破口全息碎片外飘、整站 glitch 瞬闪。
    /// 全部视觉挂在 inner 下：呼吸写 BreatheRoot、glitch 写 inner.modulate，互不打架；
    /// station.modulate 归调用方所有（E04，调用方压站体 alpha 不被呼吸覆盖）。</summary>
    private static void BuildPhantom(Node2D station)
    {
        // E04 修复：全部视觉挂 BreatheRoot 呼吸容器下，4s 慢呼吸写容器 modulate:a 而非站体本身——
        // 调用方压 station.modulate.a（return_cinematic 0.35/0.5、base_console 包装层 0.12）
        // 不再被呼吸 tween 抬高 2.5~3 倍；两种用法统一为「站体 alpha 归调用方，呼吸只动内部容器」。
        var breatheRoot = new Node2D { Name = "BreatheRoot" };
        station.AddChild(breatheRoot);
        var inner = new Node2D { Name = "PhantomBody" };
        breatheRoot.AddChild(inner);
        // 第 1 层：全息基底——全构件 ADD 叠加，亮青 #00d4ff 高饱和（主弧 α0.55/舱段 0.45/细节 0.35）；
        // 主弧分段留 3 处断弧缺口（原破口 0.5–1.2 + 两处较小缺口），站已毁的破碎感
        var refs = BuildBody(
            inner,
            new Godot.Collections.Dictionary
            {
                ["ring"] = new Color(0.0f, 0.83f, 1.0f, 0.55f),
                ["detail"] = new Color(0.0f, 0.83f, 1.0f, 0.35f),
                ["tick"] = new Color(0.0f, 0.83f, 1.0f, 0.35f),
                ["seg"] = new Color(0.0f, 0.83f, 1.0f, 0.45f),
                ["seg_edge"] = new Color(0.0f, 0.83f, 1.0f, 0.40f),
                ["spoke"] = new Color(0.0f, 0.75f, 1.0f, 0.35f),
                ["hub"] = new Color(0.0f, 0.83f, 1.0f, 0.50f),
                ["hub_ring"] = new Color(0.0f, 0.83f, 1.0f, 0.35f),
            },
            true,
            new Godot.Collections.Array
            {
                new Godot.Collections.Array { BREACH_START, BREACH_END },
                new Godot.Collections.Array { 2.4f, 2.62f },
                new Godot.Collections.Array { 4.9f, 5.06f },
            });
        // 舱段模块逐个随机「掉线」闪烁（0.1s 掉线机制，相位错开 0.13s + 周期错档）
        var segments = refs["segments"].AsGodotArray();
        var edges = refs["edges"].AsGodotArray();
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = (Polygon2D)segments[i].AsGodotObject();
            var edge = (Polygon2D)edges[i].AsGodotObject();
            var segFlicker = station.CreateTween().SetLoops();
            segFlicker.TweenInterval(0.13 * i + 1.1 + 0.4 * (i % 3));
            segFlicker.TweenProperty(seg, "modulate:a", 0.08f, 0.05);
            segFlicker.Parallel().TweenProperty(edge, "modulate:a", 0.08f, 0.05);
            segFlicker.TweenInterval(0.1);
            segFlicker.TweenProperty(seg, "modulate:a", 1.0f, 0.08);
            segFlicker.Parallel().TweenProperty(edge, "modulate:a", 1.0f, 0.08);
        }

        // 整体容器 4s 慢呼吸（0.85–1.0，投影不稳定感；下限抬高保证存在感）
        // E04：写 BreatheRoot 容器而非 station（调用方压 station.modulate.a 不被呼吸覆盖）
        var breatheTween = station.CreateTween().SetLoops();
        breatheTween.TweenProperty(breatheRoot, "modulate:a", 0.85f, 2.0).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        breatheTween.TweenProperty(breatheRoot, "modulate:a", 1.0f, 2.0).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        // 偶发整站 glitch 瞬闪：alpha 瞬时下跌 0.08s 内恢复，每 ~3.9s 一次
        var glitch = station.CreateTween().SetLoops();
        glitch.TweenInterval(3.8);
        glitch.TweenProperty(inner, "modulate:a", 0.3f, 0.04);
        glitch.TweenProperty(inner, "modulate:a", 1.0f, 0.04);
        // 第 2 层：扫描线光晕——环体外缘常亮大半径辉光 + 40px 扫描带纵向 3.5s/趟
        inner.AddChild(Dot(360.0f, new Color(0.0f, 0.83f, 1.0f, 0.15f)));
        var scanBand = RectPoly(680.0f, 40.0f, new Color(0.0f, 0.83f, 1.0f, 0.12f));
        scanBand.Position = new Vector2(0.0f, -340.0f);
        Additive(scanBand);
        inner.AddChild(scanBand);
        var scan = station.CreateTween().SetLoops();
        scan.TweenProperty(scanBand, "position:y", 340.0f, 3.5).SetTrans(Tween.TransitionType.Linear);
        scan.TweenProperty(scanBand, "position:y", -340.0f, 0.0);
        // 第 3 层：数据流粒子——①边缘逸散（环轮廓切向慢飘）②内部结构流（环面内侧往返）
        var edgeFlow = Particles(
            new Godot.Collections.Dictionary
            {
                ["amount"] = 48,
                ["lifetime"] = 2.5f,
                ["vel_min"] = 15.0f,
                ["vel_max"] = 35.0f,
                ["scale_min"] = 1.0f,
                ["scale_max"] = 2.0f,
                ["color"] = new Color(0.0f, 0.83f, 1.0f, 0.4f),
                ["emission_ring_radius"] = 260.0f,
                ["emission_ring_inner_radius"] = 250.0f,
            });
        inner.AddChild(edgeFlow);
        var innerFlow = Particles(
            new Godot.Collections.Dictionary
            {
                ["amount"] = 40,
                ["lifetime"] = 3.0f,
                ["vel_min"] = 15.0f,
                ["vel_max"] = 45.0f,
                ["scale_min"] = 1.0f,
                ["scale_max"] = 2.0f,
                ["color"] = new Color(0.0f, 0.75f, 1.0f, 0.3f),
                ["emission_ring_radius"] = 200.0f,
                ["emission_ring_inner_radius"] = 80.0f,
            });
        inner.AddChild(innerFlow);
        // 第 4 层：破口能量网格修补——跨越缺口的经纬格（亮青 α0.75 宽 2.5，加亮加粗），
        // 每格相位错开 0.13s 低频闪烁 + 更频繁的整格掉线 0.1s；锯齿轮廓 α0.8 亮线描出
        var gridLines = new List<Line2D>();
        for (var g = 0; g < 4; g++) // 经线：4 条径向跨 r240→280
        {
            var a = BREACH_START + (BREACH_END - BREACH_START) * (float)g / 3.0f;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var meridian = Line(new[] { dir * 240.0f, dir * 280.0f }, new Color(0.0f, 0.83f, 1.0f, 0.75f), 2.5f);
            Additive(meridian);
            inner.AddChild(meridian);
            gridLines.Add(meridian);
        }

        for (var g = 0; g < 3; g++) // 纬线：3 条弧跨 0.5–1.2 rad
        {
            var rLat = 246.0f + 12.0f * g;
            var latPoints = new Vector2[8];
            for (var s = 0; s < 8; s++)
            {
                var a = BREACH_START + (BREACH_END - BREACH_START) * (float)s / 7.0f;
                latPoints[s] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rLat;
            }

            var latitude = Line(latPoints, new Color(0.0f, 0.83f, 1.0f, 0.75f), 2.5f);
            Additive(latitude);
            inner.AddChild(latitude);
            gridLines.Add(latitude);
        }

        for (var g = 0; g < gridLines.Count; g++)
        {
            var gl = gridLines[g];
            var glModulate = gl.Modulate;
            glModulate.A = 0.75f;
            gl.Modulate = glModulate;
            var flicker = station.CreateTween().SetLoops();
            flicker.TweenInterval(0.13 * g); // 相位错开
            flicker.TweenProperty(gl, "modulate:a", 0.3f, 0.25);
            flicker.TweenProperty(gl, "modulate:a", 0.95f, 0.25);
            flicker.TweenInterval(0.5 + 0.15 * (g % 3));
            // 整格「掉线」0.1s 再亮起（更频繁）
            flicker.TweenProperty(gl, "modulate:a", 0.0f, 0.05);
            flicker.TweenInterval(0.1);
            flicker.TweenProperty(gl, "modulate:a", 0.75f, 0.08);
        }

        var jaggedOutline = Line(JaggedPoints(), new Color(0.0f, 0.83f, 1.0f, 0.8f), 2.5f);
        Additive(jaggedOutline);
        inner.AddChild(jaggedOutline);
        // 破口附近全息碎片：3 块半透明青色多边形缓慢外飘翻滚（往复，不瞬移）
        for (var k = 0; k < 3; k++)
        {
            var flake = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-8.0f, -5.0f),
                    new Vector2(9.0f, -3.0f),
                    new Vector2(5.0f, 7.0f),
                    new Vector2(-6.0f, 6.0f),
                },
                Color = new Color(0.0f, 0.83f, 1.0f, 0.35f),
            };
            Additive(flake);
            var fa = 0.6f + 0.25f * k;
            flake.Position = new Vector2(Mathf.Cos(fa), Mathf.Sin(fa)) * 262.0f;
            inner.AddChild(flake);
            var drift = 3.0f + 0.5f * k;
            var ft = station.CreateTween().SetLoops();
            ft.TweenProperty(flake, "position", flake.Position + new Vector2(Mathf.Cos(fa), Mathf.Sin(fa)) * 55.0f, drift)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.InOut);
            ft.Parallel().TweenProperty(flake, "rotation", flake.Rotation + 2.2f, drift)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.InOut);
            ft.TweenProperty(flake, "position", flake.Position, drift)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.InOut);
            ft.Parallel().TweenProperty(flake, "rotation", flake.Rotation, drift)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.InOut);
        }
    }

    // ---------------- cfg 字典读取辅助（GDScript Dictionary.get(key, default) 语义） ----------------

    private static int CfgInt(Godot.Collections.Dictionary cfg, string key, int def)
    {
        return cfg.TryGetValue(key, out var v) ? v.AsInt32() : def;
    }

    private static float CfgFloat(Godot.Collections.Dictionary cfg, string key, float def)
    {
        return cfg.TryGetValue(key, out var v) ? (float)v.AsDouble() : def;
    }

    private static bool CfgBool(Godot.Collections.Dictionary cfg, string key, bool def)
    {
        return cfg.TryGetValue(key, out var v) ? v.AsBool() : def;
    }

    private static Color CfgColor(Godot.Collections.Dictionary cfg, string key, Color def)
    {
        return cfg.TryGetValue(key, out var v) ? v.AsColor() : def;
    }

    private static Vector3 CfgVector3(Godot.Collections.Dictionary cfg, string key, Vector3 def)
    {
        return cfg.TryGetValue(key, out var v) ? v.AsVector3() : def;
    }

    private static Color PaletteColor(Godot.Collections.Dictionary palette, string key)
    {
        return palette[key].AsColor();
    }

    // ---------------- 鸭子调用兼容桥（M6 过渡，V 系列清理注释：调用方均已 typed） ----------------
    // 调用方：BaseConsole.cs（已 typed 直调）——桥代码随 Boss 链 typed 化批次统一处理。

    public static Node2D build() => Build();

    public static Node2D build(int mode) => Build(mode);

    public static float GetRingRadius() => RING_RADIUS;

    public static float GetBreachStart() => BREACH_START;

    public static float GetBreachEnd() => BREACH_END;

    public static int GetModeDestroyed() => (int)Mode.Destroyed;

    public static int GetModePhantom() => (int)Mode.Phantom;
}

/// <summary>站体构件：纯色圆点（环心毂/辉光垫共用）。
/// 原 GDScript dawn_station.gd 内嵌类 _Dot，迁移为同文件顶层类（C# 源生成器不支持内嵌类）。</summary>
public partial class DawnStationDot : Node2D
{
    public float Radius = 8.0f;

    public Color DotColor = Colors.White;

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, DotColor);
    }
}
