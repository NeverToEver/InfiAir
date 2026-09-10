using Godot;

namespace InfiAir;

/// <summary>
/// 开场过场 镜头 3 构建器（文件粒度拆分自 IntroCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class IntroCinematic : CanvasLayer
{

    // ---------------- 镜头 3：驾驶员冲刺（2.5s） ----------------

    /// <summary>侧视走廊：透视线 + 天花板管道/舱门框结构 + 黄色警示条纹反向滚动；
    /// 多段式飞行服驾驶员（骨盆/胸廓/头盔/维生背包/双关节四肢）+ 暖色边缘光 + 双层残影，两拍奔跑循环；
    /// 五条顶部锥形体积光带，前景近景支杆快速横扫（-1600px/s 强视差），红色应急灯 6Hz 闪烁，
    /// 蒸汽上飘，加密速度线，低频警报脉冲。</summary>
    private IntroRunnerShot BuildShot3()
    {
        var root = new IntroRunnerShot { Name = "Shot3" };
        root.AddChild(BgRect(new Color(0.012f, 0.018f, 0.035f)));
        // 天花板/地面透视带 + 向右侧灭点收敛的走廊线（冷钢蓝主调，暖色只留给应急光与警示条纹）
        root.AddChild(Line(new[] { new Vector2(0.0f, 200.0f), new Vector2(1920.0f, 430.0f) }, new Color(0.2f, 0.28f, 0.42f, 0.8f), 3.0f));
        root.AddChild(Line(new[] { new Vector2(0.0f, 880.0f), new Vector2(1920.0f, 650.0f) }, new Color(0.2f, 0.28f, 0.42f, 0.8f), 3.0f));
        root.AddChild(Line(new[] { new Vector2(0.0f, 540.0f), new Vector2(1920.0f, 540.0f) }, new Color(0.12f, 0.16f, 0.26f, 0.5f)));
        var ceilPoly = new Polygon2D
        {
            Polygon = new[] { new Vector2(0.0f, 0.0f), new Vector2(1920.0f, 0.0f), new Vector2(1920.0f, 430.0f), new Vector2(0.0f, 200.0f) },
            Color = new Color(0.04f, 0.052f, 0.082f),
        };
        root.AddChild(ceilPoly);
        var floorPoly = new Polygon2D
        {
            Polygon = new[] { new Vector2(0.0f, 880.0f), new Vector2(1920.0f, 650.0f), new Vector2(1920.0f, 1080.0f), new Vector2(0.0f, 1080.0f) },
            Color = new Color(0.048f, 0.062f, 0.098f),
        };
        root.AddChild(floorPoly);
        // 天花板管道：双管沿顶棚走向 + 管节环
        var pipes = new[] { new[] { 150.0f, 380.0f, 8.0f }, new[] { 178.0f, 408.0f, 5.0f } };
        foreach (var pipe in pipes)
        {
            root.AddChild(Line(new[] { new Vector2(0.0f, pipe[0]), new Vector2(1920.0f, pipe[1]) }, new Color(0.24f, 0.3f, 0.44f), pipe[2]));
        }

        for (var i = 0; i < 5; i++)
        {
            var joint = new GlowDot { Radius = 7.0f, DotColor = new Color(0.3f, 0.38f, 0.52f) };
            var jx = 200.0f + 400.0f * i;
            joint.Position = new Vector2(jx, 150.0f + jx * 230.0f / 1920.0f);
            root.AddChild(joint);
        }

        // 顶部体积光：五条锥形光带（叠加态，上窄下宽）+ 地面光斑反射（湿冷金属地板的镜面回声）
        for (var i = 0; i < 5; i++)
        {
            var cx = 320.0f + 320.0f * i;
            var cone = new Polygon2D();
            cone.Polygon = new[]
            {
                new Vector2(cx - 50.0f, 60.0f),
                new Vector2(cx + 50.0f, 60.0f),
                new Vector2(cx + 170.0f, 950.0f),
                new Vector2(cx - 170.0f, 950.0f),
            };
            cone.Color = new Color(0.72f, 0.82f, 1.0f, 0.07f);
            cone.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            root.AddChild(cone);
            var reflY = 920.0f - 0.12f * (cx + 120.0f);
            var refl = CinematicFx.SoftGlow(42.0f, new Color(0.6f, 0.72f, 1.0f, 0.10f));
            refl.Position = new Vector2(cx + 120.0f, reflY);
            refl.Scale = new Vector2(6.5f, 1.1f);  // 压扁成地面光斑
            root.AddChild(refl);
        }

        // 顶部旋转警灯光锥：红色叠加态，锚点往复扫掠
        for (var i = 0; i < 2; i++)
        {
            var beacon = new Polygon2D
            {
                Polygon = new[] { new Vector2(0.0f, 0.0f), new Vector2(-70.0f, 760.0f), new Vector2(70.0f, 760.0f) },
                Color = new Color(1.0f, 0.12f, 0.1f, 0.055f),
                Position = new Vector2(640.0f + 640.0f * i, 40.0f),
                Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
            };
            root.AddChild(beacon);
            var beaconSweep = root.CreateTween().SetLoops();
            beaconSweep.TweenProperty(beacon, "rotation", 0.55f, 1.1).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            beaconSweep.TweenProperty(beacon, "rotation", -0.55f, 1.1).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        // 墙面疏散箭头（指向 +x 逃生方向，随墙肋同速滚动讲「往弹射舱跑」的叙事）
        for (var i = 0; i < 3; i++)
        {
            var chevron = Line(
                new[] { new Vector2(0.0f, 0.0f), new Vector2(26.0f, 18.0f), new Vector2(0.0f, 36.0f) },
                new Color(1.0f, 0.72f, 0.2f, 0.85f),
                5.0f);
            chevron.Position = new Vector2(300.0f + 640.0f * i, 462.0f);
            root.AddChild(chevron);
            root.Scrollers.Add(chevron);
        }

        // 黄色警示条纹（地面）+ 深色墙肋 + 舱门框：加入反向滚动列表
        for (var i = 0; i < 14; i++)
        {
            var stripe = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-40.0f, 0.0f),
                    new Vector2(-10.0f, 0.0f),
                    new Vector2(-26.0f, 14.0f),
                    new Vector2(-56.0f, 14.0f),
                },
                Color = new Color(0.9f, 0.75f, 0.1f, 0.75f),
                Position = new Vector2(80.0f + 160.0f * i, 872.0f),
            };
            root.AddChild(stripe);
            root.Scrollers.Add(stripe);
        }

        for (var i = 0; i < 9; i++)
        {
            var rib = RectPoly(26.0f, 420.0f, new Color(0.09f, 0.11f, 0.16f));
            rib.Position = new Vector2(120.0f + 240.0f * i, 400.0f);
            root.AddChild(rib);
            root.Scrollers.Add(rib);
        }

        for (var i = 0; i < 3; i++)
        {
            var doorX = 400.0f + 720.0f * i;
            var door = RectPoly(150.0f, 460.0f, new Color(0.13f, 0.17f, 0.24f));
            door.Position = new Vector2(doorX, 420.0f);
            root.AddChild(door);
            root.Scrollers.Add(door);
            var doorInner = RectPoly(118.0f, 428.0f, new Color(0.05f, 0.06f, 0.09f));
            doorInner.Position = door.Position;
            root.AddChild(doorInner);
            root.Scrollers.Add(doorInner);
        }

        // 蒸汽：墙壁管道泄漏，白色半透明上飘
        var steam = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 32,
            ["lifetime"] = 1.8f,
            ["vel_min"] = 50.0f,
            ["vel_max"] = 110.0f,
            ["spread"] = 30.0f,
            ["scale_min"] = 5.0f,
            ["scale_max"] = 11.0f,
            ["color"] = new Color(0.9f, 0.95f, 1.0f, 0.2f),
            ["additive"] = false,
        });
        steam.Position = new Vector2(1500.0f, 320.0f);
        root.AddChild(steam);
        // 驾驶员背光（叠加态暖光，把剪影从暗舱里托出来）+ 地面接地阴影（人物不再悬浮）
        var backlight = CinematicFx.SoftGlow(210.0f, new Color(1.0f, 0.6f, 0.25f, 0.22f));
        backlight.Position = new Vector2(880.0f, 520.0f);
        root.AddChild(backlight);
        var contactShadow = CinematicFx.SoftGlow(40.0f, new Color(0.0f, 0.0f, 0.02f, 0.55f), additive: false);
        contactShadow.Position = new Vector2(880.0f, 766.0f);
        contactShadow.Scale = new Vector2(2.2f, 0.5f);
        root.AddChild(contactShadow);
        // 驾驶员与双层残影（动态模糊）：细节收敛至共享构件工厂 CrewFigure；
        // 残影复用其胸廓剪影与头盔尺度，拖在奔跑反方向
        var bodyColor = new Color(0.26f, 0.31f, 0.41f);  // 与 CrewFigure.SuitNear 一致（残影用）
        var chestPoints = new[] { new Vector2(-7.0f, -14.0f), new Vector2(13.0f, -16.0f), new Vector2(17.0f, -42.0f), new Vector2(-3.0f, -46.0f) };
        foreach (var k in new[] { 2, 1 })
        {
            var ghost = new Node2D
            {
                Position = new Vector2(880.0f - 40.0f * k, 566.0f + 5.0f * k),
                Scale = Vector2.One * 2.3f,
                Rotation = 0.3f,
                Modulate = new Color(1.0f, 1.0f, 1.0f, 0.06f + 0.05f * (2 - k)),
            };
            root.AddChild(ghost);
            var gTorso = new Polygon2D { Polygon = chestPoints, Color = bodyColor };
            ghost.AddChild(gTorso);
            var gHead = new GlowDot { Radius = 10.5f, DotColor = bodyColor, Position = new Vector2(11.0f, -62.0f) };
            ghost.AddChild(gHead);
        }

        // 乘员（琥珀状态灯，贴合开场暖调+应急红）
        var crew = CrewFigure.Build(new Color(1.0f, 0.72f, 0.28f));
        var pilot = (Node2D)crew["node"].AsGodotObject();
        pilot.Position = new Vector2(880.0f, 566.0f);
        pilot.Scale = Vector2.One * 2.3f;
        root.AddChild(pilot);
        root.BobNode = pilot;
        root.BobBaseY = pilot.Position.Y;
        // 奔跑前倾：躯干组 0.3rad（与旧内联版一致，动画相位公式照旧）
        ((Node2D)crew["torso"].AsGodotObject()).Rotation = 0.3f;
        foreach (var h in crew["hips"].AsGodotArray())
        {
            root.HipPivots.Add((Node2D)h.AsGodotObject());
        }

        foreach (var kn in crew["knees"].AsGodotArray())
        {
            root.KneePivots.Add((Node2D)kn.AsGodotObject());
        }

        foreach (var sh in crew["shoulders"].AsGodotArray())
        {
            root.ShoulderPivots.Add((Node2D)sh.AsGodotObject());
        }

        foreach (var el in crew["elbows"].AsGodotArray())
        {
            root.ElbowPivots.Add((Node2D)el.AsGodotObject());
        }

        // 速度线（加密）
        for (var i = 0; i < 14; i++)
        {
            var sl = Line(new[] { Vector2.Zero, new Vector2(-160.0f - (float)GD.Randf() * 140.0f, 0.0f) }, new Color(0.8f, 0.9f, 1.0f, 0.28f), 2.0f);
            sl.Position = new Vector2((float)GD.Randf() * 2200.0f, (float)GD.Randf() * 1080.0f);
            root.AddChild(sl);
            root.SpeedLines.Add(sl);
        }

        // 前景近景支杆 ×4：宽斜边深色半透明立柱，-1600px/s 反向横扫回卷（近景强视差，在红闪之下受染色）
        for (var fgI = 0; fgI < 4; fgI++)
        {
            var fg = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-90.0f, -620.0f),
                    new Vector2(70.0f, -620.0f),
                    new Vector2(130.0f, 620.0f),
                    new Vector2(-30.0f, 620.0f),
                },
                Color = new Color(0.01f, 0.015f, 0.03f, 0.6f),
                Position = new Vector2(300.0f + 630.0f * fgI, 540.0f),
            };
            root.AddChild(fg);
            root.FgStruts.Add(fg);
        }

        // 红色应急灯全屏闪烁
        var red = BgRect(new Color(1.0f, 0.1f, 0.1f, 0.0f));
        root.AddChild(red);
        root.Red = red;
        // 低频警报脉冲（既有命中音压低至 -14dB 基础，叠加过场音量策略，0.7s 间隔）
        var alarm = new Godot.Timer { WaitTime = 0.7f, Autostart = true };
        root.AddChild(alarm);
        alarm.Timeout += () => GameState.Instance.PlaySfx(SfxId.PlayerHit, -14.0f + AudioVolOffset, AudioPitch);
        return root;
    }
}
