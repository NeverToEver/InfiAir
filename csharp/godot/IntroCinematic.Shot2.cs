using Godot;

namespace InfiAir;

/// <summary>
/// 开场过场 镜头 2 构建器（文件粒度拆分自 IntroCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class IntroCinematic : CanvasLayer
{

    // ---------------- 镜头 2：X光链式爆炸（2.5s） ----------------
    /// <summary>冷蓝底剖面：甲板为半透明填充带 + 亮边（有厚度层次），舱室隔断分区清晰；
    /// 橙红能量沿预设折线 0.2s/节点链式点亮（叠加态外发光双线），节点处爆开橙色圆闪，
    /// 爆炸音 ×3 音量递减。</summary>
    private Node2D BuildShot2()
    {
        var root = new Node2D { Name = "Shot2" };
        root.AddChild(BgRect(new Color(0.02f, 0.05f, 0.12f)));
        var wire = new Color(0.3f, 0.6f, 1.0f, 0.4f);
        // 4 层甲板：半透明蓝填充带（厚度）+ 上下亮边
        var deckYs = new[] { 340.0f, 520.0f, 700.0f, 880.0f };
        for (var i = 0; i < deckYs.Length; i++)
        {
            var y = deckYs[i];
            var band = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(150.0f, y),
                    new Vector2(1770.0f, y),
                    new Vector2(1770.0f, y + 16.0f),
                    new Vector2(150.0f, y + 16.0f),
                },
                Color = new Color(0.2f, 0.45f, 0.9f, 0.12f),
            };
            root.AddChild(band);
            root.AddChild(Line(new[] { new Vector2(150.0f, y), new Vector2(1770.0f, y) }, new Color(0.45f, 0.7f, 1.0f, 0.7f), 2.5f));
            root.AddChild(Line(new[] { new Vector2(150.0f, y + 16.0f), new Vector2(1770.0f, y + 16.0f) }, new Color(0.2f, 0.4f, 0.8f, 0.4f)));
        }

        // 舱室分区：相邻舱室交替冷蓝微填充，层级更清
        for (var deck = 0; deck < deckYs.Length - 1; deck++)
        {
            for (var i = 0; i < 6; i++)
            {
                if ((i + deck) % 2 == 0)
                {
                    continue;
                }

                var room = new Polygon2D();
                var rx = 150.0f + 270.0f * i;
                room.Polygon = new[]
                {
                    new Vector2(rx, deckYs[deck] + 16.0f),
                    new Vector2(rx + 270.0f, deckYs[deck] + 16.0f),
                    new Vector2(rx + 270.0f, deckYs[deck + 1]),
                    new Vector2(rx, deckYs[deck + 1]),
                };
                room.Color = new Color(0.15f, 0.35f, 0.8f, 0.05f);
                root.AddChild(room);
            }
        }

        // 骨架竖线 + 舱室隔断（隔断更粗，分区感）
        for (var i = 0; i < 7; i++)
        {
            var x = 150.0f + 270.0f * i;
            root.AddChild(Line(new[] { new Vector2(x, 300.0f), new Vector2(x, 920.0f) }, wire));
        }

        for (var i = 0; i < 6; i++)
        {
            var x = 285.0f + 270.0f * i;
            root.AddChild(Line(new[] { new Vector2(x, 520.0f), new Vector2(x, 700.0f) }, new Color(0.4f, 0.65f, 1.0f, 0.55f), 3.5f));
        }

        // 外框
        var frame = Line(
            new[]
            {
                new Vector2(150.0f, 300.0f),
                new Vector2(1770.0f, 300.0f),
                new Vector2(1770.0f, 920.0f),
                new Vector2(150.0f, 920.0f),
            },
            new Color(0.4f, 0.7f, 1.0f, 0.6f),
            3.0f);
        frame.Closed = true;
        root.AddChild(frame);
        // 蛇形扫描线网格底（单条 Line2D 铺满剖面区）+ 循环往复的亮色扫描带
        var scanPoints = new List<Vector2>();
        var sy = 304.0f;
        var scanLeft = true;
        while (sy <= 916.0f)
        {
            scanPoints.Add(new Vector2(scanLeft ? 150.0f : 1770.0f, sy));
            scanPoints.Add(new Vector2(scanLeft ? 1770.0f : 150.0f, sy));
            sy += 6.0f;
            scanLeft = !scanLeft;
        }

        root.AddChild(Line(scanPoints.ToArray(), new Color(0.4f, 0.7f, 1.0f, 0.05f), 1.0f));
        var scanBand = BgRect(new Color(0.4f, 0.8f, 1.0f, 0.06f));
        scanBand.Size = new Vector2(1620.0f, 46.0f);
        scanBand.Position = new Vector2(150.0f, 300.0f);
        root.AddChild(scanBand);
        var bandSweep = root.CreateTween().SetLoops();
        bandSweep.TweenProperty(scanBand, "position:y", 874.0f, 2.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        bandSweep.TweenProperty(scanBand, "position:y", 300.0f, 2.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        // 顶层甲板状态灯带：一排小舷灯（初始青色），链爆经过时逐点转红（重着色在 Timer 步进内完成）
        var deckLights = new List<GlowDot>();
        for (var dI = 0; dI < 12; dI++)
        {
            var dl = Glow(3.5f, new Color(0.0f, 0.83f, 1.0f, 0.8f));
            dl.Position = new Vector2(220.0f + 134.0f * dI, 348.0f);
            root.AddChild(dl);
            deckLights.Add(dl);
        }

        // 链式能量路径：逐节点点亮（折线穿过各层甲板）；外发光 = 更宽更淡的叠加态底线
        var path = new[]
        {
            new Vector2(200.0f, 700.0f),
            new Vector2(500.0f, 700.0f),
            new Vector2(500.0f, 520.0f),
            new Vector2(900.0f, 520.0f),
            new Vector2(900.0f, 340.0f),
            new Vector2(1300.0f, 340.0f),
            new Vector2(1300.0f, 700.0f),
            new Vector2(1700.0f, 700.0f),
        };
        var energyGlow = Line(System.Array.Empty<Vector2>(), new Color(1.0f, 0.4f, 0.1f, 0.3f), 16.0f);
        energyGlow.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        root.AddChild(energyGlow);
        var energy = Line(System.Array.Empty<Vector2>(), new Color(1.0f, 0.45f, 0.15f), 6.0f);
        root.AddChild(energy);
        var step = new int[] { 0 };
        var shakeState = new Godot.Collections.Array { default(Variant) };  // 每次引爆刷新一次颤动峰值（连锁震感）
        var timer = new Godot.Timer { WaitTime = 0.2f, Autostart = true };  // 构建期未入树，用 autostart（入树后自动启动）
        root.AddChild(timer);
        timer.Timeout += () =>
        {
            if (step[0] >= path.Length)
            {
                timer.Stop();
                return;
            }

            var pos = path[step[0]];
            KickShake(root, 4.0f, shakeState);  // 节点引爆：轻微画面颤动脉冲
            energy.AddPoint(pos);
            energy.Width = 6.0f + step[0] * 1.5f;
            energyGlow.AddPoint(pos);
            energyGlow.Width = 16.0f + step[0] * 3.0f;
            // 节点爆闪（软光晕）+ 一次性火花溅射（textured 软点粒子，播完自毁）
            var flash = CinematicFx.SoftGlow(36.0f, new Color(1.0f, 0.55f, 0.15f, 0.9f));
            flash.Position = pos;
            var flashBase = flash.Scale;
            flash.Scale = Vector2.Zero;
            root.AddChild(flash);
            var ft = root.CreateTween();
            ft.TweenProperty(flash, "scale", flashBase * 1.4f, 0.12);
            ft.TweenProperty(flash, "modulate:a", 0.0f, 0.35);
            ft.TweenCallback(Callable.From(flash.QueueFree));
            var sparks = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 24,
                ["lifetime"] = 0.4f,
                ["one_shot"] = true,
                ["explosiveness"] = 0.9f,
                ["spread"] = 180.0f,
                ["vel_min"] = 120.0f,
                ["vel_max"] = 300.0f,
                ["damping_min"] = 80.0f,
                ["damping_max"] = 160.0f,
                ["scale_min"] = 2.0f,
                ["scale_max"] = 4.0f,
                ["color"] = new Color(1.0f, 0.6f, 0.2f, 0.95f),
            });
            sparks.Position = pos;
            root.AddChild(sparks);
            sparks.Finished += sparks.QueueFree;
            // 顶层甲板状态灯带：链爆波前经过处由青转红（步进内重着色，零逐帧开销）
            foreach (var dl in deckLights)
            {
                if (dl.Position.X <= pos.X)
                {
                    dl.DotColor = new Color(1.0f, 0.3f, 0.2f, 0.9f);
                    dl.QueueRedraw();
                }
            }

            // 冲击波纹：节点爆闪处薄环扩大淡出（仿镜头 1 冲击波）
            var ripplePoints = new Vector2[16];
            for (var rI = 0; rI < 16; rI++)
            {
                var rA = Mathf.Tau * rI / 16.0f;
                ripplePoints[rI] = new Vector2(Mathf.Cos(rA), Mathf.Sin(rA)) * 14.0f;
            }

            var ripple = Line(ripplePoints, new Color(1.0f, 0.6f, 0.2f, 0.6f), 3.0f);
            ripple.Closed = true;
            ripple.Position = pos;
            ripple.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            root.AddChild(ripple);
            var rt = root.CreateTween().SetParallel(true);
            rt.TweenProperty(ripple, "scale", Vector2.One * 3.2f, 0.5).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            rt.TweenProperty(ripple, "modulate:a", 0.0f, 0.45);
            rt.Chain().TweenCallback(Callable.From(ripple.QueueFree));
            if (step[0] == 1 || step[0] == 3 || step[0] == 5)
            {
                // 链式三连发：音量逐发递减
                GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION, -2.0f - 3.0f * (step[0] / 2) + AudioVolOffset, AudioPitch);
            }

            step[0] += 1;
        };
        return root;
    }
}
