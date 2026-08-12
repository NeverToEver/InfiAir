using Godot;

namespace InfiAir;

/// <summary>
/// 返航过场 镜头 5 构建器（文件粒度拆分自 ReturnCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class ReturnCinematic : CanvasLayer
{

    // ---------------- 镜头 5：停机坪降落（1.8s） ----------------
    /// <summary>战机沿引导灯带中线垂直下落 40px 降落（ease_out + 落地下压回弹 + 细尘）；引擎熄火
    /// （喷口辉光 0.3s 缩没）；座舱盖上翻 0.4s；主角跃下落地（弧线 0.5s + 落地微尘）。
    /// 固定机位低角度仰拍（机位元素整体下移 ~60px）。
    /// 舰人比例按现实战机/驾驶员锚定：舰可见长约 420px（scale 2.8）≈ 人物 64px（scale 0.55）的 6.6 倍。</summary>
    private Node2D BuildShot5()
    {
        var dur = _shotDurations[4];
        var u = dur / 1.8f;
        var root = new Node2D { Name = "Shot5" };
        root.AddChild(BgRect(new Color(0.01f, 0.02f, 0.05f)));
        // 虚影站内部线框剖面（半透明青色调，沿用 X 光线框语言）
        for (var i = 0; i < 3; i++)
        {
            var y = 380.0f + 140.0f * i;
            root.AddChild(Line(new[] { new Vector2(150.0f, y), new Vector2(1770.0f, y) }, new Color(0.0f, 0.6f, 1.0f, 0.08f)));
        }

        for (var i = 0; i < 7; i++)
        {
            var x = 150.0f + 270.0f * i;
            root.AddChild(Line(new[] { new Vector2(x, 340.0f), new Vector2(x, 860.0f) }, new Color(0.0f, 0.6f, 1.0f, 0.06f)));
        }

        // 六边形甲板平台：深色实体底 + 青色发光边界 + 中线引导灯带（低角度：整体下移 60px）
        var deck = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(-320.0f, 0.0f),
                new Vector2(-220.0f, -70.0f),
                new Vector2(220.0f, -70.0f),
                new Vector2(320.0f, 0.0f),
                new Vector2(220.0f, 70.0f),
                new Vector2(-220.0f, 70.0f),
            },
            Color = new Color(0.05f, 0.07f, 0.10f),
            Position = new Vector2(960.0f, 780.0f),
        };
        root.AddChild(deck);
        var deckEdge = Line(deck.Polygon, new Color(0.0f, 0.83f, 1.0f, 0.5f), 2.0f);
        deckEdge.Closed = true;
        var deckEdgeMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        deckEdge.Material = deckEdgeMat;
        deck.AddChild(deckEdge);
        // 中线引导灯带 ×8：初始压暗，降落窗口内由两侧向落点依次追逐点亮
        var guides = new List<GlowDot>();
        for (var i = 0; i < 8; i++)
        {
            var guide = Glow(3.0f, new Color(0.0f, 0.83f, 1.0f, 0.7f));
            guide.Position = new Vector2(-210.0f + 60.0f * i, 0.0f);
            var guideMod = guide.Modulate;
            guideMod.A = 0.2f;
            guide.Modulate = guideMod;
            deck.AddChild(guide);
            guides.Add(guide);
        }

        var chaseOrder = new[] { 0, 7, 1, 6, 2, 5, 3, 4 }; // 由远及近指向甲板中心落点
        for (var j = 0; j < 8; j++)
        {
            var g = guides[chaseOrder[j]];
            var gt = root.CreateTween();
            gt.TweenInterval(0.05f * u + 0.075f * u * j);
            gt.TweenProperty(g, "modulate:a", 1.0f, 0.08f * u);
            gt.TweenProperty(g, "modulate:a", 0.7f, 0.3f * u);
        }

        // 甲板尽头通道闸门
        var gate = RectPoly(120.0f, 160.0f, new Color(0.06f, 0.08f, 0.12f));
        gate.Position = new Vector2(1450.0f, 730.0f);
        root.AddChild(gate);
        var gateFrame = Line(
            new[]
            {
                new Vector2(1390.0f, 650.0f),
                new Vector2(1510.0f, 650.0f),
                new Vector2(1510.0f, 810.0f),
                new Vector2(1390.0f, 810.0f),
            },
            new Color(0.0f, 0.83f, 1.0f, 0.35f),
            2.0f
        );
        gateFrame.Closed = true;
        root.AddChild(gateFrame);
        // 战机（载体含机身/喷口/座舱盖）：scale 2.8，尾部落于甲板顶面（y=710）
        var ship = new Node2D { Position = new Vector2(960.0f, 460.0f) };
        root.AddChild(ship);
        var hull = new Sprite2D { Texture = _playerShip, Scale = Vector2.One * 2.8f };
        ship.AddChild(hull);
        var engine = Glow(20.0f, new Color(1.0f, 0.95f, 0.85f, 0.9f));
        engine.Position = new Vector2(0.0f, 195.0f);
        ship.AddChild(engine);
        var canopy = new Polygon2D
        {
            Polygon = new[] { new Vector2(-20.0f, 0.0f), new Vector2(20.0f, 0.0f), new Vector2(12.0f, -36.0f), new Vector2(-12.0f, -36.0f) },
            Color = new Color(0.15f, 0.35f, 0.5f, 0.85f),
            Position = new Vector2(0.0f, -80.0f),
        };
        ship.AddChild(canopy);
        // 主角（scale 0.55 ≈ 64px 高，初始藏于座舱）
        var person = BuildPerson();
        var pnode = (Node2D)person["node"].AsGodotObject();
        pnode.Scale = Vector2.One * 0.55f;
        pnode.Position = new Vector2(960.0f, 425.0f);
        pnode.Visible = false;
        PoseStand(person);
        root.AddChild(pnode);
        // 降落：垂直下落 40px（ease_out）→ 下压回弹 + 细尘 + 熄火 + 开舱 + 跃下
        var land = root.CreateTween();
        land.TweenProperty(ship, "position:y", 500.0f, 0.9f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        land.TweenProperty(ship, "scale:y", 0.94f, 0.1f * u);
        land.TweenProperty(ship, "scale:y", 1.0f, 0.12f * u);
        Once(root, 0.9f * u, Callable.From(() =>
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION, -18.0f); // 落地极轻闷响
            var dust = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 24,
                ["lifetime"] = 0.6f,
                ["one_shot"] = true,
                ["explosiveness"] = 0.9f,
                ["direction"] = new Vector3(0.0f, -1.0f, 0.0f),
                ["spread"] = 140.0f,
                ["vel_min"] = 40.0f,
                ["vel_max"] = 110.0f,
                ["scale_min"] = 2.0f,
                ["scale_max"] = 5.0f,
                ["color"] = new Color(0.5f, 0.7f, 0.9f, 0.3f),
            });
            dust.Position = new Vector2(960.0f, 705.0f);
            root.AddChild(dust);
            var off = root.CreateTween();
            off.TweenProperty(engine, "scale", Vector2.Zero, 0.3f * u); // 引擎熄火
        }));
        Once(root, 1.0f * u, Callable.From(() =>
        {
            var open = root.CreateTween();
            open.TweenProperty(canopy, "rotation", -1.3f, 0.4f * u); // 座舱盖上翻
            // 玻璃高光条随开舱滑过舱面（与上翻同程 0.4u）
            var shine = new Polygon2D
            {
                Polygon = new[] { new Vector2(-2.0f, -4.0f), new Vector2(4.0f, -4.0f), new Vector2(-2.0f, -32.0f), new Vector2(-8.0f, -32.0f) },
                Color = new Color(1.0f, 1.0f, 1.0f, 0.0f),
                Position = new Vector2(-10.0f, 0.0f),
            };
            var shineMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            shine.Material = shineMat;
            canopy.AddChild(shine);
            var slide = root.CreateTween();
            slide.TweenProperty(shine, "position:x", 12.0f, 0.4f * u);
            var fade = root.CreateTween();
            fade.TweenProperty(shine, "color:a", 0.4f, 0.08f * u);
            fade.TweenProperty(shine, "color:a", 0.0f, 0.12f * u).SetDelay(0.26f * u); // 滑到末段同步消隐
        }));
        Once(root, 1.15f * u, Callable.From(() =>
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -14.0f); // 跃下短促音
            pnode.Visible = true;
            var jump = root.CreateTween();
            jump.TweenProperty(pnode, "position", new Vector2(935.0f, 390.0f), 0.25f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            jump.TweenProperty(pnode, "position", new Vector2(905.0f, 686.0f), 0.25f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            jump.TweenCallback(Callable.From(() =>
            {
                // 落地微尘 + 屈膝缓冲后站直
                var landDust = Particles(new Godot.Collections.Dictionary
                {
                    ["amount"] = 16,
                    ["lifetime"] = 0.5f,
                    ["one_shot"] = true,
                    ["explosiveness"] = 0.9f,
                    ["direction"] = new Vector3(0.0f, -1.0f, 0.0f),
                    ["spread"] = 150.0f,
                    ["vel_min"] = 30.0f,
                    ["vel_max"] = 80.0f,
                    ["scale_min"] = 1.5f,
                    ["scale_max"] = 3.5f,
                    ["color"] = new Color(0.5f, 0.7f, 0.9f, 0.25f),
                });
                landDust.Position = new Vector2(905.0f, 706.0f);
                root.AddChild(landDust);
                var knees = person["knees"].AsGodotArray();
                for (var i = 0; i < 2; i++)
                {
                    ((Node2D)knees[i].AsGodotObject()).Rotation = 0.9f;
                }

                var stand = root.CreateTween();
                for (var i = 0; i < 2; i++)
                {
                    stand.Parallel().TweenProperty((Node2D)knees[i].AsGodotObject(), "rotation", 0.05f, 0.3f * u);
                }
            }));
        }));
        return root;
    }
}
