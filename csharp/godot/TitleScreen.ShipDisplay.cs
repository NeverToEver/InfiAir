using Godot;

namespace InfiAir;

/// <summary>
/// 标题屏·玩家机悬挂展示部分：自跃迁远点飞入（RadialStreaks 光线场 + 位置/尺寸减速缓动）→
/// 右侧停驻（轮廓背光 + 幽灵拖影退场 + 尾焰怠速点火 + 悬浮浮动）+ 机体铭牌卡。
/// 时轴：0.2s 起飞 1.4s → 1.6s 落位收尾 → 1.8s 铭牌淡入；全程 Tween/Timer，无 await。
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    private const float ShipScale = 2.0f; // 254px 原生贴图 ×2：首次大尺寸细节展示
    private const float FlyDuration = 1.4f;

    private Node2D _shipAnchor = null!;
    private Node2D _shipBobber = null!;
    private Node2D _warpStreaks = null!;
    private readonly System.Collections.Generic.List<GpuParticles2D> _engines = new();
    private readonly System.Collections.Generic.List<Sprite2D> _nozzleGlows = new();
    private readonly System.Collections.Generic.List<Sprite2D> _ghosts = new();

    private Texture2D? _playerTex;

    /// <summary>实例级缓存 GD.Load（命中引擎资源缓存）；不做 static 持有——引擎退出 finalize segfault 规则（同 ChamferedPanel.StreakTex）。</summary>
    private Texture2D PlayerTex => _playerTex ??= GD.Load<Texture2D>("res://assets/sprites/player_ship.png");

    private void BuildShipDisplay()
    {
        _shipAnchor = new Node2D { Position = ShipFarPos, Scale = Vector2.One * 0.05f };
        AddChild(_shipAnchor);

        // 轮廓背光：大范围低强度青辉光 + 内圈亮芯（光源不随机体浮动，落位后托出金属体积感）
        var rim = CinematicFx.SoftGlow(190.0f, new Color(0.0f, 0.83f, 1.0f, 0.14f));
        rim.Position = new Vector2(0.0f, 10.0f);
        _shipAnchor.AddChild(rim);
        var rimCore = CinematicFx.SoftGlow(90.0f, new Color(0.5f, 0.95f, 1.0f, 0.16f));
        rimCore.Position = new Vector2(0.0f, -6.0f);
        _shipAnchor.AddChild(rimCore);

        // 跃迁光线场：飞入期间机体后方爆发，落位收尾时淡出自毁
        _warpStreaks = CinematicFx.RadialStreaks(new Godot.Collections.Dictionary
        {
            ["count"] = 26,
            ["max_radius"] = 640.0f,
            ["cycle"] = 0.9f,
            ["color"] = new Color(0.6f, 0.85f, 1.0f, 0.32f),
        });
        _warpStreaks.Position = (ShipFarPos + ShipAnchorPos) * 0.5f;
        _warpStreaks.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        AddChild(_warpStreaks);
        var streakIn = _warpStreaks.CreateTween();
        streakIn.TweenProperty(_warpStreaks, "modulate:a", 1.0f, 0.3).SetDelay(0.1).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        // bobber：机体 + 拖影 + 尾焰（落位后整体浮动，背光不动 → 悬挂感）
        _shipBobber = new Node2D();
        _shipAnchor.AddChild(_shipBobber);

        // 幽灵拖影：飞入期间沿运动反方向的动态模糊残影，落位收尾时淡出
        for (var k = 1; k <= 2; k++)
        {
            var ghost = new Sprite2D
            {
                Texture = PlayerTex,
                Scale = Vector2.One * ShipScale,
                Position = new Vector2(22.0f * k, -25.0f * k),
                Modulate = new Color(0.6f, 0.9f, 1.0f, 0.10f - 0.04f * (k - 1)),
            };
            _shipBobber.AddChild(ghost);
            _ghosts.Add(ghost);
        }

        // 机体本体（微亮 modulate：星空背景下机身读得清）
        var ship = new Sprite2D
        {
            Texture = PlayerTex,
            Scale = Vector2.One * ShipScale,
            Modulate = new Color(0.94f, 0.97f, 1.02f),
        };
        _shipBobber.AddChild(ship);

        // 尾焰怠速（青色对齐主色板；喷口按贴图 254px × scale 2 折算，比例同 IntroCinematic.Shot5）
        foreach (var side in new[] { -66.0f, 66.0f })
        {
            var flame = CinematicFx.Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 36,
                ["lifetime"] = 0.4f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 14.0f,
                ["vel_min"] = 220.0f,
                ["vel_max"] = 330.0f,
                ["scale_min"] = 5.0f,
                ["scale_max"] = 9.0f,
                ["color"] = new Color(0.0f, 0.83f, 1.0f, 0.8f),
            });
            flame.Position = new Vector2(side, 114.0f);
            flame.AmountRatio = 0.0f; // 点火前无粒子
            _shipBobber.AddChild(flame);
            _engines.Add(flame);

            var coreFlame = CinematicFx.Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 20,
                ["lifetime"] = 0.26f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 8.0f,
                ["vel_min"] = 150.0f,
                ["vel_max"] = 230.0f,
                ["scale_min"] = 2.5f,
                ["scale_max"] = 5.0f,
                ["color"] = new Color(0.85f, 0.98f, 1.0f, 1.0f),
            });
            coreFlame.Position = new Vector2(side, 110.0f);
            coreFlame.AmountRatio = 0.0f;
            _shipBobber.AddChild(coreFlame);
            _engines.Add(coreFlame);

            // 喷口辉光（SoftGlow(24) 基准 scale = 24/32 = 0.75）
            var nozzle = CinematicFx.SoftGlow(24.0f, new Color(0.0f, 0.83f, 1.0f, 0.6f));
            nozzle.Position = new Vector2(side, 108.0f);
            nozzle.Scale = Vector2.Zero;
            _shipBobber.AddChild(nozzle);
            _nozzleGlows.Add(nozzle);
        }

        // 飞入时轴：0.2s 起飞，1.4s 减速接近（Cubic Out 远→近），1.6s 落位收尾
        var fly = CreateTween().SetParallel(true);
        fly.TweenProperty(_shipAnchor, "position", ShipAnchorPos, FlyDuration).SetDelay(0.2).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        fly.TweenProperty(_shipAnchor, "scale", Vector2.One, FlyDuration).SetDelay(0.2).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        fly.Chain().TweenCallback(Callable.From(OnShipSettled));

        // 点火：飞入末段尾焰升起 + 喷口辉光弹起
        var ignite = CreateTween().SetParallel(true);
        foreach (var engine in _engines)
        {
            ignite.TweenProperty(engine, "amount_ratio", 1.0f, 0.35).SetDelay(1.15).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }

        foreach (var nozzle in _nozzleGlows)
        {
            ignite.TweenProperty(nozzle, "scale", Vector2.One * 0.75f, 0.3).SetDelay(1.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }

        BuildNameplate();
    }

    /// <summary>落位收尾（飞入 Tween 结束回调）：残影/光线退场 + 制动冲击环 + 启动悬浮浮动。</summary>
    private void OnShipSettled()
    {
        var outTween = CreateTween().SetParallel(true);
        foreach (var ghost in _ghosts)
        {
            outTween.TweenProperty(ghost, "modulate:a", 0.0f, 0.45);
        }

        outTween.TweenProperty(_warpStreaks, "modulate:a", 0.0f, 0.5);
        outTween.Chain().TweenCallback(Callable.From(_warpStreaks.QueueFree));

        var brake = CinematicFx.Shockwave(new Godot.Collections.Dictionary
        {
            ["radius"] = 130.0f,
            ["time"] = 0.5f,
            ["color"] = new Color(0.0f, 0.83f, 1.0f, 0.3f),
            ["core_color"] = new Color(0.8f, 0.97f, 1.0f, 0.5f),
            ["width"] = 8.0f,
        });
        brake.Position = ShipAnchorPos;
        AddChild(brake);

        // 悬浮浮动：纵向漂移 + 微摆（机库悬挂/怠速悬停的呼吸感）
        var bob = _shipBobber.CreateTween().SetLoops();
        bob.TweenProperty(_shipBobber, "position:y", 6.0f, 1.2).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        bob.TweenProperty(_shipBobber, "position:y", -6.0f, 1.2).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        var sway = _shipBobber.CreateTween().SetLoops();
        sway.TweenProperty(_shipBobber, "rotation", 0.018f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        sway.TweenProperty(_shipBobber, "rotation", -0.018f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>机体铭牌卡（切角面板 + 档案文案 + 引擎输出装饰条）：纯装饰 FUI，不绑真实数值。</summary>
    private void BuildNameplate()
    {
        var plate = new ChamferedPanel
        {
            Brackets = true,
            Position = new Vector2(975.0f, 742.0f),
            Padding = 22.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f),
        };
        // Padding 只参与面板自适应尺寸，内容需自行内缩半个 Padding 才不贴边
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(11.0f, 11.0f) };
        box.AddThemeConstantOverride("separation", 6);
        plate.AddChild(box);

        box.AddChild(UITheme.MakeLabel((string)Tr("TITLE_PLATE_TITLE"), UITheme.FontSmall, UITheme.Accent, HorizontalAlignment.Left));
        box.AddChild(UITheme.MakeLabel((string)Tr("TITLE_PLATE_STATUS"), UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left));
        box.AddChild(UITheme.MakeLabel((string)Tr("TITLE_PLATE_SYSTEMS"), UITheme.FontSmall, UITheme.TextDim, HorizontalAlignment.Left));

        var outRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        outRow.AddThemeConstantOverride("separation", 12);
        outRow.AddChild(UITheme.MakeLabel((string)Tr("TITLE_PLATE_OUTPUT"), UITheme.FontSmall, UITheme.TextDim, HorizontalAlignment.Left));
        var outputBar = new SegmentedBar
        {
            Segments = 12,
            MaxValue = 12.0f,
            Value = 2.0f,
            CustomMinimumSize = new Vector2(220.0f, 12.0f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        outRow.AddChild(outputBar);
        box.AddChild(outRow);

        AddChild(plate);
        var plateIn = plate.CreateTween();
        plateIn.TweenProperty(plate, "modulate:a", 1.0f, 0.45).SetDelay(1.8).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        // 引擎输出条随铭牌上线缓升（TweenMethod 直写属性，规避导出名/别名路径歧义）
        var barUp = outputBar.CreateTween();
        barUp.TweenMethod(Callable.From<float>(v => outputBar.Value = v), 2.0f, 9.0f, 0.9).SetDelay(2.0).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }
}
