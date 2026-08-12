using Godot;

namespace InfiAir;

/// <summary>
/// 开场过场 镜头 6 构建器（文件粒度拆分自 IntroCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class IntroCinematic : CanvasLayer
{

    // ---------------- 镜头 6：远景收束（3.0s） ----------------
    /// <summary>星云叠加圆 + 右上恒星背光；画面底部行星弧线 + 青蓝大气辉光带压底；
    /// 左下残骸剪影与余烬闪烁（外加缓慢翻滚的漂浮碎片）；
    /// 战机 = 深色剪影 + 引擎亮斑，向右上加速驶离；补给舰编队两列三行光点同向跟随；
    /// 末尾 0.7s 由导演淡黑衔接标题定格。</summary>
    private Node2D BuildShot6()
    {
        var dur = _shotDurations[5];
        var root = new Node2D { Name = "Shot6" };
        root.AddChild(new Starfield());  // M1 起 Starfield 为 C#，typed 实例化（原经脚本资源，M6 重定型）
        // 星云（大半径低透明度叠加圆，软径向光晕）
        var nebula1 = CinematicFx.SoftGlow(430.0f, new Color(0.35f, 0.15f, 0.5f, 0.13f));
        nebula1.Position = new Vector2(1380.0f, 320.0f);
        root.AddChild(nebula1);
        var nebula2 = CinematicFx.SoftGlow(340.0f, new Color(0.1f, 0.2f, 0.5f, 0.17f));
        nebula2.Position = new Vector2(420.0f, 780.0f);
        root.AddChild(nebula2);
        var nebula3 = CinematicFx.SoftGlow(260.0f, new Color(0.1f, 0.4f, 0.45f, 0.1f));
        nebula3.Position = new Vector2(1050.0f, 850.0f);
        root.AddChild(nebula3);
        // 星云缓慢异向漂移（远景视差呼吸感，往返循环）
        var nebulae = new List<Node2D> { nebula1, nebula2, nebula3 };
        var nebDirs = new[] { new Vector2(60.0f, 24.0f), new Vector2(-70.0f, 30.0f), new Vector2(50.0f, -36.0f) };
        for (var nI = 0; nI < 3; nI++)
        {
            var nebTween = root.CreateTween().SetLoops();
            nebTween.TweenProperty(nebulae[nI], "position", nebulae[nI].Position + nebDirs[nI], 7.0f + 2.0f * nI).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            nebTween.TweenProperty(nebulae[nI], "position", nebulae[nI].Position, 7.0f + 2.0f * nI).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        // 行星弧线（画面底部）：巨大半径圆弧（圆心远在屏下）+ 暗色星体填充 + 青蓝大气辉光带
        var limbPts = new Vector2[64];
        for (var pI = 0; pI < 64; pI++)
        {
            var la = -Mathf.Pi * 0.5f + ((float)pI / 63.0f - 0.5f) * 1.0f;
            limbPts[pI] = new Vector2(960.0f, 3350.0f) + new Vector2(Mathf.Cos(la), Mathf.Sin(la)) * 2480.0f;
        }

        var planetPts = new Vector2[limbPts.Length + 2];
        System.Array.Copy(limbPts, planetPts, limbPts.Length);
        planetPts[limbPts.Length] = new Vector2(2200.0f, 1200.0f);
        planetPts[limbPts.Length + 1] = new Vector2(-280.0f, 1200.0f);
        var planet = new Polygon2D { Polygon = planetPts, Color = new Color(0.02f, 0.04f, 0.09f) };
        root.AddChild(planet);
        root.AddChild(Line(limbPts, new Color(0.25f, 0.5f, 0.75f, 0.55f), 3.0f));
        var atmo = CinematicFx.SoftGlow(90.0f, new Color(0.3f, 0.65f, 0.9f, 0.22f));
        atmo.Position = new Vector2(960.0f, 880.0f);
        atmo.Scale *= new Vector2(16.0f, 0.9f);  // 横向拉扁成大气光带
        root.AddChild(atmo);
        // 恒星背光（右上强辉光，软径向光晕）
        var starHalo = CinematicFx.SoftGlow(230.0f, new Color(1.0f, 0.9f, 0.7f, 0.12f));
        starHalo.Position = new Vector2(1620.0f, 180.0f);
        root.AddChild(starHalo);
        var star = CinematicFx.SoftGlow(80.0f, new Color(1.0f, 0.95f, 0.85f, 0.75f));
        star.Position = new Vector2(1620.0f, 180.0f);
        root.AddChild(star);
        // 恒星横向 anamorphic 光晕：宽蓝白亮条 + 短竖条（叠加态），缓慢脉动
        var flareMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        var flareH = RectPoly(600.0f, 3.0f, new Color(0.6f, 0.8f, 1.0f, 0.35f));
        flareH.Position = star.Position;
        flareH.Material = flareMat;
        root.AddChild(flareH);
        var flareV = RectPoly(4.0f, 90.0f, new Color(0.6f, 0.8f, 1.0f, 0.2f));
        flareV.Position = star.Position;
        flareV.Material = flareMat;
        root.AddChild(flareV);
        var flarePulse = root.CreateTween().SetLoops();
        flarePulse.TweenProperty(flareH, "scale:x", 1.15f, 1.8).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        flarePulse.TweenProperty(flareH, "scale:x", 1.0f, 1.8).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        // 左下燃烧残骸剪影 + 橙色余烬闪烁
        var wreck = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(0.0f, 1080.0f),
                new Vector2(0.0f, 830.0f),
                new Vector2(150.0f, 790.0f),
                new Vector2(280.0f, 850.0f),
                new Vector2(420.0f, 810.0f),
                new Vector2(540.0f, 900.0f),
                new Vector2(560.0f, 1080.0f),
            },
            Color = new Color(0.03f, 0.03f, 0.05f),
        };
        root.AddChild(wreck);
        // 残骸顶缘余烬反光轮廓（否则在暗角下不可读）
        root.AddChild(Line(
            new[]
            {
                new Vector2(0.0f, 830.0f),
                new Vector2(150.0f, 790.0f),
                new Vector2(280.0f, 850.0f),
                new Vector2(420.0f, 810.0f),
                new Vector2(540.0f, 900.0f),
            },
            new Color(1.0f, 0.5f, 0.15f, 0.35f),
            3.0f));
        foreach (var pos in new[] { new Vector2(140.0f, 860.0f), new Vector2(300.0f, 900.0f), new Vector2(430.0f, 870.0f), new Vector2(220.0f, 950.0f) })
        {
            var ember = Glow(6.0f + (float)GD.Randf() * 4.0f, new Color(1.0f, 0.5f, 0.15f, 0.8f));
            ember.Position = pos;
            root.AddChild(ember);
            var et = root.CreateTween().SetLoops();
            et.TweenProperty(ember, "modulate:a", 0.15f, 0.4f + (float)GD.Randf() * 0.3f);
            et.TweenProperty(ember, "modulate:a", 1.0f, 0.4f + (float)GD.Randf() * 0.3f);
        }

        // 残骸上方缓慢翻滚的漂浮碎片（深色剪影 + 边缘微光）
        for (var k = 0; k < 3; k++)
        {
            var shard = new Polygon2D
            {
                Polygon = new[] { new Vector2(-14.0f, -6.0f), new Vector2(12.0f, -10.0f), new Vector2(16.0f, 8.0f), new Vector2(-8.0f, 10.0f) },
                Color = new Color(0.05f, 0.05f, 0.08f),
                Position = new Vector2(520.0f + 130.0f * k, 760.0f - 90.0f * k),
            };
            root.AddChild(shard);
            var shardEdge = Line(new[] { new Vector2(-14.0f, -6.0f), new Vector2(12.0f, -10.0f) }, new Color(1.0f, 0.6f, 0.25f, 0.55f), 2.5f);
            shard.AddChild(shardEdge);
            var st = root.CreateTween().SetParallel(true).SetLoops();
            st.TweenProperty(shard, "rotation", shard.Rotation + Mathf.Tau, 7.0f + 2.0f * k);
            st.TweenProperty(shard, "position", shard.Position + new Vector2(30.0f, -46.0f), 4.5f + k).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        // 战机剪影：深色机身 + 引擎亮斑，向右上加速驶离（ease_in）
        var ship = new Node2D
        {
            Position = new Vector2(760.0f, 640.0f),
            Rotation = new Vector2(720.0f, -360.0f).Angle(),  // 机头对准航向
            Scale = Vector2.One * 1.3f,
        };
        root.AddChild(ship);
        var fuselage = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(26.0f, 0.0f),
                new Vector2(-4.0f, -6.0f),
                new Vector2(-22.0f, -18.0f),
                new Vector2(-14.0f, -4.0f),
                new Vector2(-14.0f, 4.0f),
                new Vector2(-22.0f, 18.0f),
                new Vector2(-4.0f, 6.0f),
            },
            Color = new Color(0.1f, 0.13f, 0.2f),
        };
        ship.AddChild(fuselage);
        // 恒星背光边缘光：机身上缘（朝恒星一侧）暖色描边
        ship.AddChild(Line(
            new[]
            {
                new Vector2(26.0f, 0.0f),
                new Vector2(-4.0f, -6.0f),
                new Vector2(-22.0f, -18.0f),
            },
            new Color(1.0f, 0.85f, 0.6f, 0.6f),
            2.0f));
        var canopy = Glow(3.0f, new Color(0.4f, 0.7f, 1.0f, 0.6f));
        canopy.Position = new Vector2(8.0f, 0.0f);
        ship.AddChild(canopy);
        var engineGlow = Glow(9.0f, new Color(1.0f, 0.6f, 0.2f, 0.9f));
        engineGlow.Position = new Vector2(-16.0f, 0.0f);
        ship.AddChild(engineGlow);
        var engineFlicker = root.CreateTween().SetLoops();
        engineFlicker.TweenProperty(engineGlow, "scale", Vector2.One * 1.5f, 0.12);
        engineFlicker.TweenProperty(engineGlow, "scale", Vector2.One, 0.12);
        var shipTween = root.CreateTween();
        shipTween.TweenProperty(ship, "position", new Vector2(1480.0f, 280.0f), dur).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        // 补给舰编队：两列 × 三行光点阵列同向跟随（各带引擎拖尾短线，作为光点子节点随 tween 同行）
        for (var i = 0; i < 6; i++)
        {
            var dot = Glow(4.0f, new Color(0.6f, 0.8f, 1.0f, 0.8f));
            dot.Position = new Vector2(420.0f + 70.0f * (i % 2), 800.0f + 52.0f * (i / 2));
            dot.AddChild(Line(new[] { Vector2.Zero, new Vector2(-18.0f, 10.0f) }, new Color(0.5f, 0.75f, 1.0f, 0.45f), 2.0f));
            root.AddChild(dot);
            var dt = root.CreateTween();
            dt.TweenProperty(dot, "position", dot.Position + new Vector2(240.0f, -130.0f), dur);
        }

        return root;
    }
}
