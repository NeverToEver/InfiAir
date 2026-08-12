using Godot;

namespace InfiAir;

/// <summary>
/// 返航过场 镜头 4 构建器（文件粒度拆分自 ReturnCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class ReturnCinematic : CanvasLayer
{

    // ---------------- 镜头 4：虚影站全貌 + 捕获轨道（2.2s） ----------------

    /// <summary>虚影站「曙光·残响」全貌首次完整亮相（§1.1 四层虚影全开）；
    /// 半透明能量捕获轨道（CinematicFx.beam 分层能量束：辉光层 + 亮芯层 + 3 个循环流光软点）牵引战机滑向停机坪入口；
    /// 站体环缘 8 盏航行灯慢速追逐明灭；镜头缓慢侧跟（正弦平移 60px + scale 1.0→1.12 缓推）。</summary>
    private ReturnCinematicCaptureShot BuildShot4()
    {
        var dur = _shotDurations[3];
        var root = new ReturnCinematicCaptureShot { Name = "Shot4" };
        root.AddChild(new Starfield()); // M1 起 Starfield 为 C#，typed 实例化
        var cam = new Node2D(); // 侧跟推镜容器
        root.AddChild(cam);
        var camTween = root.CreateTween().SetParallel(true);
        camTween.TweenProperty(cam, "scale", Vector2.One * 1.12f, dur).SetTrans(Tween.TransitionType.Sine);
        camTween.TweenProperty(cam, "position", new Vector2(-60.0f, 0.0f), dur).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        // 虚影站全貌（与开场镜头 1 同位同构）
        var station = DawnStation.Build(1); // DawnStation.Mode.PHANTOM
        station.Position = new Vector2(960.0f, 470.0f);
        cam.AddChild(station);
        // 站体环缘航行灯 ×8（慢速追逐明灭，预建后由 _CaptureShot 相位驱动 alpha）
        var ringRadius = DawnStation.RingRadius;
        for (var i = 0; i < 8; i++)
        {
            var a = Mathf.Tau * i / 8.0f;
            var lamp = SoftGlow(5.0f, new Color(0.5f, 0.95f, 1.0f, 1.0f));
            lamp.Position = new Vector2(960.0f, 470.0f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (ringRadius + 4.0f);
            var lampMod = lamp.Modulate;
            lampMod.A = 0.12f;
            lamp.Modulate = lampMod;
            cam.AddChild(lamp);
            root._lights.Add(lamp);
        }

        // 捕获轨道：站体边缘 → 战机的二次贝塞尔弧（构建期采样 24 点，_process 零分配）
        var p0 = new Vector2(960.0f, 470.0f) + new Vector2(Mathf.Cos(-0.2f), Mathf.Sin(-0.2f)) * ringRadius;
        var p2 = new Vector2(1530.0f, 660.0f);
        var p1 = new Vector2(1420.0f, 470.0f);
        var samples = new List<Vector2>(24);
        for (var i = 0; i < 24; i++)
        {
            var t = i / 23.0f;
            samples.Add(p0.Lerp(p1, t).Lerp(p1.Lerp(p2, t), t));
        }

        root._samples = samples.ToArray();
        // 分层能量束（辉光层 + 亮芯层 + 3 个循环流光软点，内部零分配 _process）
        var beam = CinematicFx.Beam(root._samples, new Godot.Collections.Dictionary
        {
            ["color"] = new Color(0.0f, 0.83f, 1.0f),
            ["width"] = 14.0f,
            ["dot_count"] = 3,
            ["dot_speed"] = 0.5f,
            ["dot_radius"] = 8.0f,
            ["dot_color"] = new Color(0.6f, 0.98f, 1.0f),
        });
        cam.AddChild(beam);
        // 战机沿轨道弧线缓速滑向停机坪入口（TRANS_SINE 吸附感）
        var ship = new Sprite2D
        {
            Texture = _playerShip,
            Scale = Vector2.One * 0.9f,
        };
        cam.AddChild(ship);
        root._ship = ship;
        // 机尾短距拖尾（镜头 3 飞出段同款配方，减速滑行弱一档）
        var shipTrail = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 24,
            ["lifetime"] = 0.35f,
            ["direction"] = new Vector3(-1.0f, 0.4f, 0.0f),
            ["spread"] = 20.0f,
            ["vel_min"] = 100.0f,
            ["vel_max"] = 180.0f,
            ["scale_min"] = 2.0f,
            ["scale_max"] = 3.5f,
            ["color"] = new Color(0.6f, 0.9f, 1.0f, 0.55f),
        });
        shipTrail.Position = new Vector2(-26.0f, 0.0f);
        ship.AddChild(shipTrail);
        var pull = root.CreateTween();
        pull.TweenProperty(root, "_ship_u", 0.06f, dur).SetTrans(Tween.TransitionType.Sine); // _ship_u 保持原名：tween 按 ClassDB 属性名驱动
        GameState.Instance.PlaySfx(GameState.Instance.SFX_RESUPPLY, -8.0f); // 对接感
        return root;
    }
}
