using Godot;

namespace InfiAir;

/// <summary>
/// 返航过场 镜头 3 构建器（文件粒度拆分自 ReturnCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class ReturnCinematic : CanvasLayer
{

    // ---------------- 镜头 3：跃迁匹配剪辑（1.4s） ----------------
    /// <summary>前半原星域：战机加速冲入端口（scale 1→0.2 入环心），端口急缩成一点 → 白闪 0.10s
    /// → 后半虚影站星域：同一位置端口再度张开，战机减速飞出（scale 0.2→1，ease_out），
    /// 端口闭合消散。飞行方向两段保持一致（向右上）；远景先露远处虚影站剪影为镜头 4 铺垫。</summary>
    private Node2D BuildShot3()
    {
        var dur = _shotDurations[2];
        var u = dur / 2.0f; // 内部关键帧按基准时长等比缩放（测试短时长表兼容）
        var root = new Node2D { Name = "Shot3" };
        var portalPos = new Vector2(1180.0f, 400.0f);
        // ---- 前半：原星域冲入 ----
        var partA = new Node2D();
        root.AddChild(partA);
        partA.AddChild(new Starfield()); // M1 起 Starfield 为 C#，typed 实例化
        // 跃迁隧道放射条纹（以端口为中心，白闪切镜时随 part_a 隐藏）
        var streaks = CinematicFx.RadialStreaks(new Godot.Collections.Dictionary
        {
            ["count"] = 26,
            ["max_radius"] = 1000.0f,
            ["color"] = new Color(0.5f, 0.85f, 1.0f, 0.45f),
            ["cycle"] = 1.0f,
        });
        streaks.Position = portalPos;
        var streaksMod = streaks.Modulate;
        streaksMod.A = 0.0f;
        streaks.Modulate = streaksMod;
        partA.AddChild(streaks);
        var streaksIn = root.CreateTween();
        streaksIn.TweenProperty(streaks, "modulate:a", 1.0f, 0.3f * u);
        var ringAPoints = new Vector2[48];
        for (var i = 0; i < 48; i++)
        {
            var a = Mathf.Tau * i / 48.0f;
            ringAPoints[i] = new Vector2(Mathf.Cos(a) * 90.0f, Mathf.Sin(a) * 150.0f);
        }

        var ringA = Line(ringAPoints, new Color(0.0f, 0.9f, 1.0f, 0.9f), 4.0f);
        ringA.Closed = true;
        ringA.Position = portalPos;
        partA.AddChild(ringA);
        var shipA = new Sprite2D { Texture = _playerShip };
        shipA.Position = new Vector2(700.0f, 720.0f);
        shipA.Rotation = (portalPos - shipA.Position).Angle() + Mathf.Pi * 0.5f; // 贴图机头朝上，+PI/2 对准航向
        partA.AddChild(shipA);
        var flameA = Glow(18.0f, new Color(1.0f, 0.95f, 0.85f, 0.9f));
        flameA.Position = new Vector2(-30.0f, 0.0f); // 机尾（局部 -x）
        shipA.AddChild(flameA);
        var dive = root.CreateTween().SetParallel(true);
        dive.TweenProperty(shipA, "position", portalPos, 0.8f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        dive.TweenProperty(shipA, "scale", Vector2.One * 0.2f, 0.8f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        dive.TweenProperty(flameA, "scale", Vector2.One * 2.0f, 0.5f * u); // 尾焰骤亮
        // 端口急缩成一点（白闪前）
        var closeA = root.CreateTween();
        closeA.TweenInterval(0.82f * u);
        closeA.TweenProperty(ringA, "scale", Vector2.One * 0.02f, 0.08f * u);
        // ---- 后半：虚影站星域飞出（初始隐藏，白闪后揭示） ----
        var partB = new Node2D { Visible = false };
        root.AddChild(partB);
        partB.AddChild(new Starfield()); // M1 起 Starfield 为 C#，typed 实例化
        var neb = Glow(420.0f, new Color(0.08f, 0.2f, 0.45f, 0.06f));
        neb.Position = new Vector2(420.0f, 780.0f);
        partB.AddChild(neb);
        // 远处虚影站剪影（α0.15，为镜头 4 铺垫）
        var farStation = DawnStation.Build(1); // DawnStation.Mode.PHANTOM
        farStation.Scale = Vector2.One * 0.3f;
        farStation.Position = new Vector2(1560.0f, 300.0f);
        var farMod = farStation.Modulate;
        farMod.A = 0.5f;
        farStation.Modulate = farMod;
        partB.AddChild(farStation);
        var ringB = Line(ringAPoints, new Color(0.0f, 0.9f, 1.0f, 0.9f), 4.0f);
        ringB.Closed = true;
        ringB.Position = portalPos;
        ringB.Scale = Vector2.One * 0.02f;
        partB.AddChild(ringB);
        var shipB = new Sprite2D
        {
            Texture = _playerShip,
            Scale = Vector2.One * 0.2f,
            Position = portalPos,
        };
        shipB.Rotation = (new Vector2(1520.0f, 230.0f) - portalPos).Angle() + Mathf.Pi * 0.5f;
        partB.AddChild(shipB);
        // 减速拖短粒子尾迹（挂在机尾随 tween 同行）
        var trail = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 40,
            ["lifetime"] = 0.35f,
            ["direction"] = new Vector3(-1.0f, 0.4f, 0.0f),
            ["spread"] = 20.0f,
            ["vel_min"] = 120.0f,
            ["vel_max"] = 220.0f,
            ["scale_min"] = 2.0f,
            ["scale_max"] = 4.0f,
            ["color"] = new Color(0.6f, 0.9f, 1.0f, 0.7f),
        });
        trail.Position = new Vector2(-26.0f, 0.0f);
        shipB.AddChild(trail);
        // 白闪转场件（镜头内部，复用开场 1→2 差异化白闪）
        var flash = BgRect(new Color(1.0f, 1.0f, 1.0f, 0.0f));
        root.AddChild(flash);
        Once(root, 0.9f * u, Callable.From(() =>
        {
            partA.Visible = false;
            partB.Visible = true;
            // 白闪瞬间端口中心扩散一道冲击环（跃迁能量释放）
            var wave = CinematicFx.Shockwave(new Godot.Collections.Dictionary
            {
                ["radius"] = 520.0f,
                ["time"] = 0.4f,
                ["ry_ratio"] = 0.7f,
                ["color"] = new Color(0.6f, 0.95f, 1.0f, 0.5f),
                ["core_color"] = new Color(1.0f, 1.0f, 1.0f, 0.85f),
                ["width"] = 14.0f,
            });
            wave.Position = portalPos;
            root.AddChild(wave);
            var ft = root.CreateTween();
            ft.TweenProperty(flash, "color:a", 1.0f, 0.05f);
            ft.TweenProperty(flash, "color:a", 0.0f, 0.25f);
            GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH); // 白闪瞬间正常速
            var emerge = root.CreateTween().SetParallel(true);
            emerge.TweenProperty(ringB, "scale", Vector2.One, 0.2f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            emerge.TweenProperty(shipB, "scale", Vector2.One, 0.7f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            emerge.TweenProperty(shipB, "position", new Vector2(1520.0f, 230.0f), 0.7f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            var dissolve = root.CreateTween();
            dissolve.TweenInterval(0.7f * u);
            dissolve.TweenProperty(ringB, "modulate:a", 0.0f, 0.2f * u); // 端口闭合消散
        }));
        Once(root, 1.2f * u, Callable.From(() => GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -10.0f))); // 飞出段尾音
        return root;
    }
}
