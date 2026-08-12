using Godot;

namespace InfiAir;

/// <summary>
/// 开场过场 镜头 5 构建器（文件粒度拆分自 IntroCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class IntroCinematic : CanvasLayer
{

    // ---------------- 镜头 5：弹射尾追视角（2.8s） ----------------

    /// <summary>尾部视角：玩家机贴图置于画面中心偏下（调暗 + 暖色软轮廓光，对齐镜头 6 剪影光比）+ 双层橙色拖影（动态模糊）；
    /// 点火预热 ~0.3s（尾焰/喷口辉光从 0 升起 + 白闪脉冲，时轴随镜头时长缩放）；
    /// 轨道 = 两侧透视收缩的壁面 + 斜向结构线加速向后流动（取代横档「梯子」）；
    /// 尾部双火焰 + 亮白内芯 + 机身两侧舔舐火焰舌 + 双壁轨道电火花；全屏速度线 + 容器 ±6px 震动；引擎音持续。</summary>
    private IntroChaseShot BuildShot5()
    {
        var dur = _shotDurations[4];
        var root = new IntroChaseShot { Name = "Shot5" };
        var shakeRoot = new Node2D();
        root.AddChild(shakeRoot);
        root.ShakeRoot = shakeRoot;
        shakeRoot.AddChild(BgRect(new Color(0.02f, 0.02f, 0.05f)));
        // 轨道壁面：两侧梯形壁板（透视向顶部收缩）+ 内外棱线
        var wallColor = new Color(0.09f, 0.11f, 0.15f);
        var leftWall = new Polygon2D
        {
            Polygon = new[] { new Vector2(430.0f, -50.0f), new Vector2(700.0f, -50.0f), new Vector2(620.0f, 1130.0f), new Vector2(250.0f, 1130.0f) },
            Color = wallColor,
        };
        shakeRoot.AddChild(leftWall);
        var rightWall = new Polygon2D
        {
            Polygon = new[] { new Vector2(1220.0f, -50.0f), new Vector2(1490.0f, -50.0f), new Vector2(1670.0f, 1130.0f), new Vector2(1300.0f, 1130.0f) },
            Color = wallColor,
        };
        shakeRoot.AddChild(rightWall);
        var edgeColor = new Color(0.35f, 0.42f, 0.55f);
        shakeRoot.AddChild(Line(new[] { new Vector2(700.0f, -50.0f), new Vector2(620.0f, 1130.0f) }, edgeColor, 4.0f));
        shakeRoot.AddChild(Line(new[] { new Vector2(430.0f, -50.0f), new Vector2(250.0f, 1130.0f) }, new Color(edgeColor, 0.5f), 3.0f));
        shakeRoot.AddChild(Line(new[] { new Vector2(1220.0f, -50.0f), new Vector2(1300.0f, 1130.0f) }, edgeColor, 4.0f));
        shakeRoot.AddChild(Line(new[] { new Vector2(1490.0f, -50.0f), new Vector2(1670.0f, 1130.0f) }, new Color(edgeColor, 0.5f), 3.0f));
        // 壁面斜向结构线：加速度向画面下方流动（初始等距铺满）
        foreach (var side in new[] { 0, 1 })
        {
            for (var i = 0; i < 7; i++)
            {
                var strut = Line(new[] { Vector2.Zero, Vector2.One }, new Color(0.25f, 0.31f, 0.42f), 5.0f);
                var y = -320.0f + 210.0f * i;
                strut.Points = new[] { new Vector2(0.0f, y), new Vector2(1.0f, y) };
                shakeRoot.AddChild(strut);
                root.Struts.Add((strut, side));
            }
        }

        // 壁面防撞灯点流：红/青交替，随结构线同款透视向后流动（数组预建，_process 零分配）
        foreach (var side in new[] { 0, 1 })
        {
            for (var i = 0; i < 6; i++)
            {
                var lamp = new IntroGlowDot
                {
                    Radius = 5.0f,
                    DotColor = i % 2 == 0 ? new Color(1.0f, 0.35f, 0.2f, 0.85f) : new Color(0.0f, 0.83f, 1.0f, 0.85f),
                    Position = new Vector2(0.0f, -320.0f + 210.0f * i),
                    Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
                };
                shakeRoot.AddChild(lamp);
                root.WallLights.Add((lamp, side));
            }
        }

        // 机身暖色软轮廓光（托底背光，对齐镜头 6 剪影光比；在拖影/机身之下）
        var shipRim = CinematicFx.SoftGlow(120.0f, new Color(1.0f, 0.5f, 0.2f, 0.2f));
        shipRim.Position = new Vector2(960.0f, 600.0f);
        shakeRoot.AddChild(shipRim);
        // 战机双层拖影（动态模糊：橙色淡影拖在机尾方向）
        foreach (var k in new[] { 2, 1 })
        {
            var ghostShip = new Sprite2D
            {
                Texture = _playerShip,
                Scale = Vector2.One * 1.4f,
                Position = new Vector2(960.0f, 560.0f + 30.0f * k),
                Modulate = new Color(1.0f, 0.6f, 0.3f, 0.05f + 0.05f * (2 - k)),
            };
            shakeRoot.AddChild(ghostShip);
        }

        // 战机（尾部视角：机头朝远方/画面上方，发动机喷口朝向镜头；整体调暗对齐镜头 6 剪影调性）
        var ship = new Sprite2D
        {
            Texture = _playerShip,
            Scale = Vector2.One * 1.4f,
            Position = new Vector2(960.0f, 560.0f),
            Modulate = new Color(0.85f, 0.85f, 0.92f),
        };
        shakeRoot.AddChild(ship);
        // 尾部主火焰（橙红、短寿命、向下拖尾；软点贴图边缘衰减，尺寸加大一档补偿）；点火预热期由 amount_ratio 0→1 升起
        var engines = new List<GpuParticles2D>();
        foreach (var side in new[] { -46.0f, 46.0f })
        {
            var flame = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 40,
                ["lifetime"] = 0.35f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 18.0f,
                ["vel_min"] = 380.0f,
                ["vel_max"] = 560.0f,
                ["scale_min"] = 6.0f,
                ["scale_max"] = 12.0f,
                ["color"] = new Color(1.0f, 0.45f, 0.1f, 0.95f),
            });
            flame.Position = new Vector2(960.0f + side, 640.0f);
            shakeRoot.AddChild(flame);
            engines.Add(flame);
        }

        // 亮白内芯：叠在橙红外焰内，更短射程、更高温度感
        foreach (var side in new[] { -46.0f, 46.0f })
        {
            var coreFlame = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 24,
                ["lifetime"] = 0.22f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 10.0f,
                ["vel_min"] = 300.0f,
                ["vel_max"] = 420.0f,
                ["scale_min"] = 3.0f,
                ["scale_max"] = 6.0f,
                ["color"] = new Color(1.0f, 0.95f, 0.85f, 1.0f),
            });
            coreFlame.Position = new Vector2(960.0f + side, 636.0f);
            shakeRoot.AddChild(coreFlame);
            engines.Add(coreFlame);
        }

        // 机身两侧舔舐火焰舌（斜外下方向、更短寿命，包住机身两侧）
        foreach (var side in new[] { -1.0f, 1.0f })
        {
            var lick = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 32,
                ["lifetime"] = 0.28f,
                ["direction"] = new Vector3(0.35f * side, 1.0f, 0.0f),
                ["spread"] = 25.0f,
                ["vel_min"] = 200.0f,
                ["vel_max"] = 380.0f,
                ["scale_min"] = 4.5f,
                ["scale_max"] = 9.0f,
                ["color"] = new Color(1.0f, 0.5f, 0.12f, 0.8f),
            });
            lick.Position = new Vector2(960.0f + 62.0f * side, 590.0f);
            shakeRoot.AddChild(lick);
            engines.Add(lick);
        }

        // 点火预热：前 ~0.3s（时轴随镜头时长缩放）尾焰 amount_ratio 0→1 + 喷口软辉光从 0 弹起 + 白闪脉冲
        var preRoll = dur * 0.11f;
        var ignite = root.CreateTween().SetParallel(true);
        foreach (var e in engines)
        {
            e.AmountRatio = 0.0f;
            ignite.TweenProperty(e, "amount_ratio", 1.0f, preRoll).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }

        foreach (var side in new[] { -46.0f, 46.0f })
        {
            var nozzle = CinematicFx.SoftGlow(26.0f, new Color(1.0f, 0.55f, 0.15f, 0.75f));
            nozzle.Position = new Vector2(960.0f + side, 632.0f);
            var nozzleBase = nozzle.Scale;
            nozzle.Scale = Vector2.Zero;
            shakeRoot.AddChild(nozzle);
            ignite.TweenProperty(nozzle, "scale", nozzleBase, preRoll).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }

        var igniteFlash = BgRect(new Color(1.0f, 1.0f, 1.0f, 0.55f));
        shakeRoot.AddChild(igniteFlash);
        var igniteFlashT = root.CreateTween();
        igniteFlashT.TweenProperty(igniteFlash, "color:a", 0.0f, dur * 0.07f);
        // 轨道电火花：两侧壁轨各一发射器，火花顺轨向 +y 高速喷洒（textured 软点，≤32/侧）
        foreach (var side in new[] { 0, 1 })
        {
            var railSpark = Particles(new Godot.Collections.Dictionary
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
                ["color"] = new Color(1.0f, 0.75f, 0.35f, 0.9f),
            });
            railSpark.Position = new Vector2(side == 0 ? 645.0f : 1275.0f, 760.0f);
            shakeRoot.AddChild(railSpark);
        }

        // 全屏速度线
        for (var i = 0; i < 10; i++)
        {
            var sl = Line(new[] { Vector2.Zero, new Vector2(0.0f, 180.0f + (float)GD.Randf() * 160.0f) }, new Color(0.7f, 0.85f, 1.0f, 0.18f), 2.0f);
            sl.Position = new Vector2((float)GD.Randf() * 1920.0f, (float)GD.Randf() * 1300.0f - 200.0f);
            shakeRoot.AddChild(sl);
            root.SpeedLines.Add(sl);
        }

        // 左右边缘放射状速度线（斜向，集中在壁外边缘区，回卷保持同侧）
        for (var i = 0; i < 8; i++)
        {
            var sideSign = i % 2 == 0 ? -1.0f : 1.0f;
            var edgeSl = Line(
                new[] { Vector2.Zero, new Vector2((34.0f + (float)GD.Randf() * 40.0f) * sideSign, 220.0f + (float)GD.Randf() * 120.0f) },
                new Color(0.7f, 0.85f, 1.0f, 0.22f),
                2.5f);
            edgeSl.Position = new Vector2(
                sideSign < 0.0f ? (float)GD.Randf() * 230.0f : 1690.0f + (float)GD.Randf() * 230.0f,
                (float)GD.Randf() * 1300.0f - 260.0f);
            shakeRoot.AddChild(edgeSl);
            root.EdgeLines.Add((edgeSl, sideSign));
        }

        GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, AudioVolOffset, AudioPitch);
        // 引擎音持续：1.1s 后压低 6dB 补一发，覆盖镜头后段
        var engine = new Godot.Timer { OneShot = true, WaitTime = 1.1f, Autostart = true };
        root.AddChild(engine);  // 随镜头销毁：跳过/切镜后不残留迟发回调
        engine.Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
            {
                GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -6.0f + AudioVolOffset, AudioPitch);
            }
        };
        return root;
    }
}
