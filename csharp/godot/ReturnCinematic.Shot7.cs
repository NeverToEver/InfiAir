using Godot;

namespace InfiAir;

/// <summary>
/// 返航过场 镜头 7 构建器（文件粒度拆分自 ReturnCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class ReturnCinematic : CanvasLayer
{

    // ---------------- 镜头 7：休息室入睡（2.0s） ----------------

    /// <summary>休眠床 + 床头全息屏微光 + 顶部暖调小灯（全场景唯一暖光源）+ 观察窗外环体缓转 + 漂移星点；
    /// 主角走入 → 坐下 → 躺下（三姿态 0.6s 间隔）→ 镜头推近面部特写（scale→1.6）→
    /// 眼睑 0.8s 闭合；闭眼瞬间画面渐暗（导演 0.9s 淡黑）+ BGM 淡出到 -40dB。</summary>
    private ReturnCinematicRoomShot BuildShot7()
    {
        var dur = _shotDurations[6];
        var u = dur / 3.0f;
        var root = new ReturnCinematicRoomShot { Name = "Shot7" };
        root._time_u = u;
        root.AddChild(BgRect(new Color(0.02f, 0.02f, 0.04f)));
        var room = new Node2D(); // 推近面部特写的运镜容器
        root.AddChild(room);
        // 舱室结构：地面线 + 舱壁
        room.AddChild(Line(new[] { new Vector2(300.0f, 840.0f), new Vector2(1620.0f, 840.0f) }, new Color(0.16f, 0.22f, 0.32f, 0.7f), 3.0f));
        room.AddChild(Line(new[] { new Vector2(300.0f, 300.0f), new Vector2(300.0f, 840.0f) }, new Color(0.10f, 0.14f, 0.22f, 0.5f)));
        room.AddChild(Line(new[] { new Vector2(1620.0f, 300.0f), new Vector2(1620.0f, 840.0f) }, new Color(0.10f, 0.14f, 0.22f, 0.5f)));
        // 观察窗：窗外虚影环体缓慢旋转轮廓（提醒此处仍在虚影站内）+ 远景星点漂移
        root._star_bounds = new Rect2(392.0f, 412.0f, 316.0f, 156.0f); // 窗框内缘留边
        for (var i = 0; i < 3; i++)
        {
            var star = SoftGlow(2.0f, new Color(0.85f, 0.92f, 1.0f, 0.7f));
            star.Position = new Vector2(
                (float)GD.RandRange(root._star_bounds.Position.X, root._star_bounds.End.X),
                (float)GD.RandRange(root._star_bounds.Position.Y, root._star_bounds.End.Y)
            );
            room.AddChild(star);
            root._stars.Add(star);
            root._star_vel.Add(new Vector2((float)GD.RandRange(-14.0f, -6.0f), (float)GD.RandRange(-3.0f, 3.0f)));
        }

        var windowFrame = Line(
            new[]
            {
                new Vector2(380.0f, 400.0f),
                new Vector2(720.0f, 400.0f),
                new Vector2(720.0f, 580.0f),
                new Vector2(380.0f, 580.0f),
            },
            new Color(0.0f, 0.83f, 1.0f, 0.35f),
            2.5f
        );
        windowFrame.Closed = true;
        room.AddChild(windowFrame);
        var ringOutside = new Node2D { Position = new Vector2(550.0f, 490.0f) };
        room.AddChild(ringOutside);
        var outPoints = new Vector2[48];
        for (var i = 0; i < 48; i++)
        {
            var a = Mathf.Tau * i / 48.0f;
            outPoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 150.0f;
        }

        var outRing = Line(outPoints, new Color(0.0f, 0.6f, 1.0f, 0.12f), 14.0f);
        outRing.Closed = true;
        ringOutside.AddChild(outRing);
        for (var i = 0; i < 4; i++)
        {
            var a = Mathf.Tau * i / 4.0f;
            ringOutside.AddChild(Line(
                new[]
                {
                    new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 40.0f,
                    new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 140.0f,
                },
                new Color(0.0f, 0.6f, 1.0f, 0.10f),
                4.0f
            ));
        }

        var spin = root.CreateTween().SetLoops();
        spin.TweenProperty(ringOutside, "rotation", Mathf.Tau, 20.0f).SetTrans(Tween.TransitionType.Linear);
        // 顶部暖调小灯（全场景唯一暖光源，「家」的视觉锚点）
        var lamp = RectPoly(30.0f, 10.0f, new Color(0.2f, 0.16f, 0.1f));
        lamp.Position = new Vector2(1100.0f, 320.0f);
        room.AddChild(lamp);
        var warm = Glow(130.0f, new Color(1.0f, 0.75f, 0.45f, 0.22f));
        warm.Position = new Vector2(1100.0f, 340.0f);
        room.AddChild(warm);
        var cone = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(1060.0f, 330.0f),
                new Vector2(1140.0f, 330.0f),
                new Vector2(1240.0f, 840.0f),
                new Vector2(960.0f, 840.0f),
            },
            Color = new Color(1.0f, 0.8f, 0.5f, 0.05f),
        };
        var coneMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        cone.Material = coneMat;
        room.AddChild(cone);
        // 休眠床：圆角平台（260 长，可躺下 185px 人物）+ 床头全息小屏微光
        var pod = RectPoly(260.0f, 26.0f, new Color(0.08f, 0.10f, 0.14f));
        pod.Position = new Vector2(1080.0f, 786.0f);
        room.AddChild(pod);
        room.AddChild(Line(new[] { new Vector2(950.0f, 773.0f), new Vector2(1210.0f, 773.0f) }, new Color(0.0f, 0.83f, 1.0f, 0.4f), 2.0f));
        var pillow = RectPoly(50.0f, 12.0f, new Color(0.12f, 0.15f, 0.2f));
        pillow.Position = new Vector2(980.0f, 766.0f);
        room.AddChild(pillow);
        var holo = RectPoly(44.0f, 32.0f, new Color(0.0f, 0.83f, 1.0f, 0.15f));
        holo.Position = new Vector2(946.0f, 700.0f);
        var holoMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        holo.Material = holoMat;
        room.AddChild(holo);
        var holoGlow = Glow(40.0f, new Color(0.0f, 0.83f, 1.0f, 0.1f));
        holoGlow.Position = new Vector2(946.0f, 700.0f);
        room.AddChild(holoGlow);
        // 主角：步行循环走入（_RoomShot 驱动肢体，位置仍由 tween 推进）→ 床沿坐下 → 平躺
        var person = BuildPerson();
        var pnode = (Node2D)person["node"].AsGodotObject();
        pnode.Scale = Vector2.One * 1.6f;
        pnode.Position = new Vector2(560.0f, 766.0f);
        PoseStand(person);
        room.AddChild(pnode);
        root._person = person;
        root._bob_base_y = pnode.Position.Y;
        var walkIn = root.CreateTween();
        walkIn.TweenProperty(pnode, "position:x", 990.0f, 0.6f * u).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        Once(root, 0.7f * u, Callable.From(() =>
        {
            // 坐下（床沿）：关节 0.3s 换姿态 + 重心下移
            var sit = root.CreateTween().SetParallel(true);
            var hips = person["hips"].AsGodotArray();
            var knees = person["knees"].AsGodotArray();
            var shoulders = person["shoulders"].AsGodotArray();
            var elbows = person["elbows"].AsGodotArray();
            for (var i = 0; i < 2; i++)
            {
                sit.TweenProperty((Node2D)hips[i].AsGodotObject(), "rotation", -1.5f, 0.3f * u);
                sit.TweenProperty((Node2D)knees[i].AsGodotObject(), "rotation", 1.4f, 0.3f * u);
                sit.TweenProperty((Node2D)shoulders[i].AsGodotObject(), "rotation", 0.2f, 0.3f * u);
                sit.TweenProperty((Node2D)elbows[i].AsGodotObject(), "rotation", -0.8f, 0.3f * u);
            }

            sit.TweenProperty(pnode, "position", new Vector2(1010.0f, 756.0f), 0.3f * u);
        }));
        Once(root, 1.3f * u, Callable.From(() =>
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_RESUPPLY, -16.0f); // 躺下轻柔音
            // 躺下：整体后倒 -90° 卧上休眠床（床面 y≈762）+ 四肢舒展微调
            var lie = root.CreateTween().SetParallel(true);
            lie.TweenProperty(pnode, "rotation", -Mathf.Pi * 0.5f, 0.4f * u).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            lie.TweenProperty(pnode, "position", new Vector2(1080.0f, 762.0f), 0.4f * u).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            var hips = person["hips"].AsGodotArray();
            var knees = person["knees"].AsGodotArray();
            var shoulders = person["shoulders"].AsGodotArray();
            var elbows = person["elbows"].AsGodotArray();
            for (var i = 0; i < 2; i++)
            {
                lie.TweenProperty((Node2D)hips[i].AsGodotObject(), "rotation", 0.1f, 0.5f * u);
                lie.TweenProperty((Node2D)knees[i].AsGodotObject(), "rotation", 0.12f, 0.5f * u);
                lie.TweenProperty((Node2D)shoulders[i].AsGodotObject(), "rotation", 0.15f, 0.5f * u);
                lie.TweenProperty((Node2D)elbows[i].AsGodotObject(), "rotation", -0.2f, 0.5f * u);
            }
        }));
        // 面部特写：镜头推近至头部（scale→1.6，聚焦平躺后的头盔位置 ≈(981,744)）
        // C12 修复：set_parallel 下前置 tween_interval 不延迟并行成员（特写提前完成）；
        // 改顺序 tween + 前置 interval，scale/position 两属性经 parallel() 同时推进
        var pushIn = root.CreateTween();
        pushIn.TweenInterval(1.5f * u);
        pushIn.SetParallel(true);
        pushIn.TweenProperty(room, "scale", Vector2.One * 1.6f, 1.0f * u).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        pushIn.TweenProperty(room, "position", new Vector2(960.0f, 540.0f) - new Vector2(981.0f, 744.0f) * 1.6f, 1.0f * u)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        // 眼睑缓缓闭合（与渐暗重叠：闭眼完成时画面约五成暗）
        var blink = root.CreateTween();
        blink.TweenInterval(1.5f * u);
        blink.TweenProperty((Node2D)person["eyelid"].AsGodotObject(), "scale:y", 1.0f, 0.8f * u);
        // 渐暗期 BGM 同步淡出到 -40dB（skip 时由 skip() kill 并立即置位）
        if (BgmPlayer != null)
        {
            _bgmTween = CreateTween();
            _bgmTween.TweenInterval(dur - FadeOutTime()); // 与画面渐暗同步起淡
            _bgmTween.TweenProperty(BgmPlayer, "volume_db", -40.0, FadeOutTime());
        }

        return root;
    }
}
