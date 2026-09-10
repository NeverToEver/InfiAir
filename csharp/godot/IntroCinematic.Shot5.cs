using Godot;

namespace InfiAir;

/// <summary>
/// 开场过场 镜头 5 构建器（文件粒度拆分自 IntroCinematic.cs，2026-08-12）。
/// 2026-09-10 重构：原 ~340 行单方法拆为「编排 + 四个分层构建器」——
/// BuildLaunchCorridor（走廊结构）/ BuildShipRig（机体与尾焰）/ BuildSpeedField（速度场）/ PlayIgnition（点火时序）。
/// 同时收掉叠加过量的层：尾焰每喷口原 5 层橙光（主焰粒子 + 白芯粒子 + 柔光柱 + 独立喷口热辉 + 壁面投光）
/// 收敛为 3 层（主焰粒子 + 柔光柱含喷口炽芯 + 壁面投光），走廊去掉与轨枕灯重复的独立铆钉列、
/// 6 条任意位置接缝收敛为每壁 1 条。
/// 硬约束：机身挂点（喷口）一律由贴图锚点推导，禁止硬编码像素坐标
/// （原 `(960±46, y640)` 与真位 `(960±26.6, y704)` 错位 ~60px，是「尾焰悬在机身上」的根因）。
/// </summary>
public partial class IntroCinematic : CanvasLayer
{
    // ---------------- 镜头 5：弹射尾追视角（2.8s） ----------------

    // 机体与喷口锚点（锚点坐标同 scripts/tools/generate_player_sprite.py 头注：喷口 (108/146, 230)）
    private const float LaunchShipScale = 1.4f;
    private const float ShipTexHalf = 127.0f;              // 254/2
    private static readonly Vector2 LaunchShipPos = new(960.0f, 560.0f);
    private static readonly Vector2 NozzleAnchorL = new(108.0f, 230.0f);
    private static readonly Vector2 NozzleAnchorR = new(146.0f, 230.0f);

    // 走廊几何（左右壁内外缘 / 导轨 / 上下端），多处共用故提常量
    private const float WallOuterL = 430.0f;
    private const float WallInnerL = 700.0f;
    private const float WallInnerR = 1220.0f;
    private const float WallOuterR = 1490.0f;
    private const float RailL = 748.0f;
    private const float RailR = 1172.0f;
    private const float TrackTop = -50.0f;
    private const float TrackBottom = 1130.0f;

    // 色板（Color 为 struct，静态持有安全）
    private static readonly Color CorridorWall = new(0.09f, 0.11f, 0.15f);
    private static readonly Color CorridorEdge = new(0.35f, 0.42f, 0.55f);
    private static readonly Color CorridorSeam = new(0.16f, 0.20f, 0.28f);
    private static readonly Color CorridorRib = new(0.18f, 0.22f, 0.30f);
    private static readonly Color CorridorRibLit = new(0.34f, 0.40f, 0.52f, 0.5f);
    private static readonly Color CorridorAccent = new(0.0f, 0.70f, 0.90f);
    private static readonly Color RailFace = new(0.40f, 0.46f, 0.58f);
    private static readonly Color RailShade = new(0.14f, 0.18f, 0.26f);
    private static readonly Color RailLit = new(0.78f, 0.86f, 0.98f, 0.55f);
    private static readonly Color HazardAmber = new(1.0f, 0.75f, 0.20f, 0.5f);
    private static readonly Color WallLampWarm = new(1.0f, 0.35f, 0.20f, 0.85f);
    private static readonly Color WallLampCool = new(0.0f, 0.83f, 1.0f, 0.85f);
    private static readonly Color FlameOuter = new(1.0f, 0.45f, 0.10f, 0.95f);
    private static readonly Color FlameLick = new(1.0f, 0.50f, 0.12f, 0.80f);
    private static readonly Color SpeedLine = new(0.70f, 0.85f, 1.0f);
    private static readonly Color SparkColor = new(1.0f, 0.75f, 0.35f, 0.90f);

    /// <summary>镜头 5 编排：走廊 → 机体与尾焰 → 速度场 → 点火时序 → 音效。</summary>
    private IntroChaseShot BuildShot5()
    {
        var root = new IntroChaseShot { Name = "Shot5" };
        var shakeRoot = new Node2D();
        root.AddChild(shakeRoot);
        root.ShakeRoot = shakeRoot;
        shakeRoot.AddChild(BgRect(new Color(0.02f, 0.02f, 0.05f)));

        BuildLaunchCorridor(shakeRoot, root);
        var (nozzleL, nozzleR) = LaunchNozzlePositions();
        var (engines, plumes) = BuildShipRig(shakeRoot, nozzleL, nozzleR);
        BuildSpeedField(shakeRoot, root);
        PlayIgnition(root, _shotDurations[4], engines, plumes);
        PlayLaunchAudio(root);
        return root;
    }

    /// <summary>喷口真位 = 机位 + (贴图锚点 − 贴图中心) × 缩放。</summary>
    private static (Vector2 L, Vector2 R) LaunchNozzlePositions()
    {
        Vector2 At(Vector2 anchor) => new(
            LaunchShipPos.X + (anchor.X - ShipTexHalf) * LaunchShipScale,
            LaunchShipPos.Y + (anchor.Y - ShipTexHalf) * LaunchShipScale);
        return (At(NozzleAnchorL), At(NozzleAnchorR));
    }

    /// <summary>发射走廊：两侧透视壁板 + 主棱线 + 内缘青色舷灯带 + 横向加强肋 + 金属导轨与轨枕灯 + 疏散斜纹
    /// + 透视滚动结构线与防撞灯点流（由 IntroChaseShot._Process 驱动）。各结构族各一，不做重复叠加。</summary>
    private void BuildLaunchCorridor(Node2D shake, IntroChaseShot root)
    {
        // 壁板（透视向顶部收缩）+ 内外主棱线（内缘亮、外缘暗）
        shake.AddChild(Poly(new[]
        {
            new Vector2(WallOuterL, TrackTop), new Vector2(WallInnerL, TrackTop),
            new Vector2(WallInnerL - 80.0f, TrackBottom), new Vector2(250.0f, TrackBottom),
        }, CorridorWall));
        shake.AddChild(Poly(new[]
        {
            new Vector2(WallInnerR, TrackTop), new Vector2(WallOuterR, TrackTop),
            new Vector2(1670.0f, TrackBottom), new Vector2(WallInnerR + 80.0f, TrackBottom),
        }, CorridorWall));
        shake.AddChild(Line(new[] { new Vector2(WallInnerL, TrackTop), new Vector2(WallInnerL - 80.0f, TrackBottom) }, CorridorEdge, 4.0f));
        shake.AddChild(Line(new[] { new Vector2(WallOuterL, TrackTop), new Vector2(250.0f, TrackBottom) }, new Color(CorridorEdge, 0.5f), 3.0f));
        shake.AddChild(Line(new[] { new Vector2(WallInnerR, TrackTop), new Vector2(WallInnerR + 80.0f, TrackBottom) }, CorridorEdge, 4.0f));
        shake.AddChild(Line(new[] { new Vector2(WallOuterR, TrackTop), new Vector2(1670.0f, TrackBottom) }, new Color(CorridorEdge, 0.5f), 3.0f));

        // 内缘青色舷灯带（走廊唯一冷色引导，每壁一条）+ 壁中缝（每壁一条，结构分格）
        foreach (var x in new[] { WallInnerL - 8.0f, WallInnerR + 8.0f })
        {
            shake.AddChild(Line(
                new[] { new Vector2(x, TrackTop), new Vector2(x + (x < 960.0f ? -72.0f : 72.0f), TrackBottom) },
                new Color(CorridorAccent, 0.35f),
                2.5f));
        }

        foreach (var (x, skew) in new[] { (560.0f, -60.0f), (1360.0f, 60.0f) })
        {
            shake.AddChild(Line(new[] { new Vector2(x, TrackTop), new Vector2(x + skew, TrackBottom) }, CorridorSeam, 2.0f));
        }

        // 横向加强肋（结构分格，上缘受光棱）
        foreach (var (x0, x1) in new[] { (WallOuterL, WallInnerL), (WallInnerR, WallOuterR) })
        {
            for (var y = 40.0f; y < 1080.0f; y += 180.0f)
            {
                shake.AddChild(Line(new[] { new Vector2(x0, y), new Vector2(x1, y + 12.0f) }, CorridorRib, 3.0f));
                shake.AddChild(Line(new[] { new Vector2(x0, y + 1.6f), new Vector2(x1, y + 13.6f) }, CorridorRibLit, 1.0f));
            }
        }

        // 金属导轨：轨面 + 轨侧暗边 + 受光棱（每轨 3 线，走廊机械主体）
        foreach (var railX in new[] { RailL, RailR })
        {
            shake.AddChild(Line(new[] { new Vector2(railX, TrackTop), new Vector2(railX, TrackBottom) }, RailFace, 6.0f));
            shake.AddChild(Line(new[] { new Vector2(railX - 4.0f, TrackTop), new Vector2(railX - 4.0f, TrackBottom) }, RailShade, 2.5f));
            shake.AddChild(Line(new[] { new Vector2(railX + 4.0f, TrackTop), new Vector2(railX + 4.0f, TrackBottom) }, RailLit, 1.4f));
        }

        // 轨枕横梁 + 轨端铆灯（等距节奏，兼作导轨刻度）
        for (var y = -30.0f; y < TrackBottom; y += 96.0f)
        {
            shake.AddChild(Line(new[] { new Vector2(RailL - 12.0f, y), new Vector2(RailL - 2.0f, y) }, RailShade, 3.0f));
            shake.AddChild(Line(new[] { new Vector2(RailR + 2.0f, y), new Vector2(RailR + 12.0f, y) }, RailShade, 3.0f));
            shake.AddChild(new GlowDot { Radius = 2.0f, DotColor = HazardAmber, Position = new Vector2(RailL, y) });
            shake.AddChild(new GlowDot { Radius = 2.0f, DotColor = HazardAmber, Position = new Vector2(RailR, y) });
        }

        // 疏散斜纹（指向 +x 逃生方向，每壁一列）
        foreach (var (x0, _) in new[] { (WallOuterL, 0.0f), (WallInnerR, 0.0f) })
        {
            for (var y = 500.0f; y < 900.0f; y += 130.0f)
            {
                shake.AddChild(Line(
                    new[] { new Vector2(x0 + 18.0f, y), new Vector2(x0 + 34.0f, y + 12.0f), new Vector2(x0 + 18.0f, y + 24.0f) },
                    HazardAmber,
                    3.0f));
            }
        }

        // 壁面斜向结构线 + 防撞灯点流（透视收缩向后流动；_Process 原地写点/位置，零分配）
        foreach (var side in new[] { 0, 1 })
        {
            for (var i = 0; i < 7; i++)
            {
                var y = -320.0f + 210.0f * i;
                var strut = Line(new[] { new Vector2(0.0f, y), new Vector2(1.0f, y) }, new Color(0.25f, 0.31f, 0.42f), 5.0f);
                shake.AddChild(strut);
                root.Struts.Add((strut, side));
            }

            for (var i = 0; i < 6; i++)
            {
                var lamp = new GlowDot
                {
                    Radius = 5.0f,
                    DotColor = i % 2 == 0 ? WallLampWarm : WallLampCool,
                    Position = new Vector2(0.0f, -320.0f + 210.0f * i),
                    Material = CinematicFx.AdditiveMaterial(),
                };
                shake.AddChild(lamp);
                root.WallLights.Add((lamp, side));
            }
        }
    }

    /// <summary>机体与尾焰：暖色轮廓背光 + 加性加速拖影 + 机身 + 主焰粒子 + 机身火舌 + 尾焰柔光柱 + 引擎光洒壁。
    /// 尾焰三层结构（主焰粒子承「屑」、柔光柱承「焰」含喷口炽芯、洒壁承「光」）；返回点火清单。</summary>
    private (List<GpuParticles2D> Engines, List<Node2D> Plumes) BuildShipRig(Node2D shake, Vector2 nozzleL, Vector2 nozzleR)
    {
        var nozzles = new[] { nozzleL, nozzleR };

        // 暖色轮廓背光（垫底，把剪影从暗舱托出）
        var rim = CinematicFx.SoftGlow(150.0f, new Color(1.0f, 0.5f, 0.2f, 0.22f));
        rim.Position = LaunchShipPos + new Vector2(0.0f, 55.0f);
        shake.AddChild(rim);

        // 加速拖影：加性暖色、微放大 + 小位移（辉光拖尾，非重影复制）
        foreach (var k in new[] { 2, 1 })
        {
            shake.AddChild(new Sprite2D
            {
                Texture = _playerShip,
                Scale = Vector2.One * (LaunchShipScale * (1.0f + 0.05f * k)),
                Position = LaunchShipPos + new Vector2(0.0f, 14.0f * k),
                Modulate = new Color(1.0f, 0.52f, 0.24f, 0.05f + 0.045f * (2 - k)),
                Material = CinematicFx.AdditiveMaterial(),
            });
        }

        // 战机（尾部视角：机头朝远方/画面上方，喷口朝向镜头）
        shake.AddChild(new Sprite2D
        {
            Texture = _playerShip,
            Scale = Vector2.One * LaunchShipScale,
            Position = LaunchShipPos,
            Modulate = new Color(0.93f, 0.91f, 0.95f),
        });

        var engines = new List<GpuParticles2D>();
        var plumes = new List<Node2D>();

        // 主焰（橙红软点，向下拖尾；点火预热期由 amount_ratio 0→1 升起）
        foreach (var nozzle in nozzles)
        {
            var flame = ExhaustParticles(nozzle, 44, 0.4f, new Vector3(0.0f, 1.0f, 0.0f), 16.0f, 380.0f, 560.0f, 6.0f, 13.0f, FlameOuter);
            shake.AddChild(flame);
            engines.Add(flame);
        }

        // 机身两侧火舌（斜外下、更短寿命，包住机身）
        foreach (var side in new[] { -1.0f, 1.0f })
        {
            var lick = ExhaustParticles(
                LaunchShipPos + new Vector2(38.0f * side, 102.0f), 32, 0.28f,
                new Vector3(0.35f * side, 1.0f, 0.0f), 25.0f, 200.0f, 380.0f, 4.5f, 9.0f, FlameLick);
            shake.AddChild(lick);
            engines.Add(lick);
        }

        // 尾焰柔光柱：喷口炽芯 → 半径/亮度沿轴递减 → 尾端透明（无硬边，自然收束；柱基即喷口热辉）
        foreach (var nozzle in nozzles)
        {
            var plume = new Node2D { Position = nozzle, Scale = new Vector2(0.15f, 0.35f) };
            shake.AddChild(plume);
            const int Steps = 6;
            const float Len = 190.0f;
            for (var s = 0; s < Steps; s++)
            {
                var t = (float)s / (Steps - 1);
                var glow = CinematicFx.SoftGlow(
                    Mathf.Lerp(30.0f, 9.0f, t),
                    new Color(1.0f, Mathf.Lerp(0.90f, 0.34f, t), Mathf.Lerp(0.62f, 0.06f, t), Mathf.Lerp(0.85f, 0.0f, Mathf.Pow(t, 0.75f))));
                glow.Position = new Vector2(0.0f, t * Len);
                glow.Scale *= new Vector2(0.78f, 1.55f);
                plume.AddChild(glow);
            }

            var core = CinematicFx.SoftGlow(17.0f, new Color(1.0f, 0.97f, 0.88f, 0.95f));
            core.Position = new Vector2(0.0f, 7.0f);
            core.Scale *= new Vector2(0.85f, 1.3f);
            plume.AddChild(core);
            plumes.Add(plume);
            // 呼吸只动 alpha（缩放归点火 tween，避免同属性竞争）
            var breathe = plume.CreateTween().SetLoops();
            breathe.TweenProperty(plume, "modulate:a", 0.72f, 0.1).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            breathe.TweenProperty(plume, "modulate:a", 1.0f, 0.1).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        // 引擎光洒壁：尾焰把两侧轨壁内缘照亮（每侧一，随尾焰同步呼吸）
        foreach (var x in new[] { WallInnerL, WallInnerR })
        {
            var spill = CinematicFx.SoftGlow(260.0f, new Color(1.0f, 0.5f, 0.18f, 0.11f));
            spill.Position = new Vector2(x, LaunchShipPos.Y + 130.0f);
            shake.AddChild(spill);
            var tween = spill.CreateTween().SetLoops();
            tween.TweenProperty(spill, "modulate:a", 0.6f, 0.18).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            tween.TweenProperty(spill, "modulate:a", 1.0f, 0.18).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        return (engines, plumes);
    }

    /// <summary>尾焰粒子统一工厂（软点贴图向下拖尾；参数显式，避免内联字典重复）。</summary>
    private GpuParticles2D ExhaustParticles(
        Vector2 pos, int amount, float lifetime, Vector3 direction, float spread,
        float velMin, float velMax, float scaleMin, float scaleMax, Color color)
    {
        var p = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = amount,
            ["lifetime"] = lifetime,
            ["direction"] = direction,
            ["spread"] = spread,
            ["vel_min"] = velMin,
            ["vel_max"] = velMax,
            ["scale_min"] = scaleMin,
            ["scale_max"] = scaleMax,
            ["color"] = color,
        });
        p.Position = pos;
        return p;
    }

    /// <summary>速度场：轨道电火花 + 全屏速度线 + 左右边缘放射线（三族，全部由镜头 _Process 回卷）。</summary>
    private void BuildSpeedField(Node2D shake, IntroChaseShot root)
    {
        foreach (var x in new[] { WallInnerL - 55.0f, WallInnerR + 55.0f })
        {
            var spark = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 28,
                ["lifetime"] = 0.35f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 22.0f,
                ["vel_min"] = 500.0f,
                ["vel_max"] = 850.0f,
                ["damping_min"] = 100.0f,
                ["damping_max"] = 220.0f,
                ["scale_min"] = 2.5f,
                ["scale_max"] = 4.5f,
                ["color"] = SparkColor,
            });
            spark.Position = new Vector2(x, 760.0f);
            shake.AddChild(spark);
        }

        for (var i = 0; i < 10; i++)
        {
            var sl = Line(new[] { Vector2.Zero, new Vector2(0.0f, 180.0f + GD.Randf() * 160.0f) }, new Color(SpeedLine, 0.18f), 2.0f);
            sl.Position = new Vector2(GD.Randf() * 1920.0f, GD.Randf() * 1300.0f - 200.0f);
            shake.AddChild(sl);
            root.SpeedLines.Add(sl);
        }

        for (var i = 0; i < 8; i++)
        {
            var sign = i % 2 == 0 ? -1.0f : 1.0f;
            var edge = Line(
                new[] { Vector2.Zero, new Vector2((34.0f + GD.Randf() * 40.0f) * sign, 220.0f + GD.Randf() * 120.0f) },
                new Color(SpeedLine, 0.22f),
                2.5f);
            edge.Position = new Vector2(sign < 0.0f ? GD.Randf() * 230.0f : 1690.0f + GD.Randf() * 230.0f, GD.Randf() * 1300.0f - 260.0f);
            shake.AddChild(edge);
            root.EdgeLines.Add((edge, sign));
        }
    }

    /// <summary>点火时序：前 ~11% 镜头时长内尾焰 amount 0→1、柔光柱从收束态展开；同步过曝纱淡出。
    /// （原独立喷口辉光弹起已并入柔光柱基座炽芯，不再单列。）</summary>
    private void PlayIgnition(IntroChaseShot root, float dur, List<GpuParticles2D> engines, List<Node2D> plumes)
    {
        var preRoll = dur * 0.11f;
        var ignite = root.CreateTween().SetParallel(true);
        foreach (var e in engines)
        {
            e.AmountRatio = 0.0f;
            ignite.TweenProperty(e, "amount_ratio", 1.0f, preRoll).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }

        foreach (var plume in plumes)
        {
            ignite.TweenProperty(plume, "scale", Vector2.One, preRoll).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }

        // 点火过曝纱：低峰值暖白，只做氛围（「被点燃」靠尾焰与柱基炽芯承载，防闪感）
        var flash = BgRect(new Color(1.0f, 0.94f, 0.86f, 0.22f));
        root.ShakeRoot.AddChild(flash);
        root.CreateTween().TweenProperty(flash, "color:a", 0.0f, dur * 0.16f);
    }

    /// <summary>弹射音效：点火一声 + 1.1s 后压低 6dB 补一发覆盖后段（Timer 随镜头销毁，跳过/切镜不残留迟发回调）。</summary>
    private void PlayLaunchAudio(Node2D root)
    {
        GameState.Instance.PlaySfx(SfxId.Dash, AudioVolOffset, AudioPitch);
        var engine = new Godot.Timer { OneShot = true, WaitTime = 1.1f, Autostart = true };
        root.AddChild(engine);
        engine.Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
            {
                GameState.Instance.PlaySfx(SfxId.Dash, -6.0f + AudioVolOffset, AudioPitch);
            }
        };
    }

    /// <summary>多边形快捷工厂（Polygon2D 直构，减少内联 object initializer 噪声）。</summary>
    private static Polygon2D Poly(Vector2[] pts, Color color) => new() { Polygon = pts, Color = color };
}
