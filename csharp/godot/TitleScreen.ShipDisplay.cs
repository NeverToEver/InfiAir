using Godot;
using InfiAir.Core.Machines;

namespace InfiAir;

/// <summary>
/// 标题屏·玩家机悬挂展示部分：自跃迁远点飞入（RadialStreaks 光线场 + 位置/尺寸减速缓动）→
/// 右侧停驻（轮廓背光 + 幽灵拖影退场 + 尾焰怠速点火 + 悬浮浮动）。
/// 时轴：0.2s 起飞 1.4s → 1.6s 落位收尾；全程 Tween/Timer，无 await。
/// 机体外形跟当前机型走（标题屏机型面板选定后立刻换），尾焰/喷口位置各型共用同一套锚点。
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    private const float ShipScale = 2.0f; // 254px 原生贴图 ×2：首次大尺寸细节展示
    private const float FlyDuration = 1.4f;

    private Node2D _shipAnchor = null!;
    private Node2D _shipBobber = null!;
    private Node2D _warpStreaks = null!;
    private Sprite2D _shipBody = null!;
    private readonly System.Collections.Generic.List<GpuParticles2D> _engines = new();
    private readonly System.Collections.Generic.List<Sprite2D> _nozzleGlows = new();
    private readonly System.Collections.Generic.List<Sprite2D> _ghosts = new();

    private Texture2D? _playerTex;

    /// <summary>机型变更回调：构造期建 Callable，_ExitTree 按同一实例断开。</summary>
    private readonly Callable _onMachineChanged;

    public TitleScreen()
    {
        _onMachineChanged = Callable.From<string>(OnMachineChanged);
    }

    /// <summary>当前机型的机体贴图（实例级缓存 GD.Load，命中引擎资源缓存；不做 static 持有——
    /// 引擎退出 finalize segfault 规则，同 ChamferedPanel.StreakTex）。</summary>
    private Texture2D? PlayerTex => _playerTex ??= GD.Load<Texture2D>(MachineRoster.SpritePath(GameState.Instance.Machine));

    private void BuildShipDisplay()
    {
        _shipAnchor = new Node2D { Position = ShipFarPos, Scale = Vector2.One * 0.05f };
        AddChild(_shipAnchor);

        // 轮廓背光：大范围低强度琥珀辉光 + 内圈亮芯（光源不随机体浮动，落位后托出金属体积感）
        var rim = CinematicFx.SoftGlow(190.0f, new Color(1.0f, 0.66f, 0.24f, 0.16f));
        rim.Position = new Vector2(0.0f, 10.0f);
        _shipAnchor.AddChild(rim);
        var rimCore = CinematicFx.SoftGlow(90.0f, new Color(1.0f, 0.86f, 0.52f, 0.18f));
        rimCore.Position = new Vector2(0.0f, -6.0f);
        _shipAnchor.AddChild(rimCore);

        // 跃迁光线场：飞入期间机体后方爆发，落位收尾时淡出自毁
        _warpStreaks = CinematicFx.RadialStreaks(new Godot.Collections.Dictionary
        {
            ["count"] = 26,
            ["max_radius"] = 640.0f,
            ["cycle"] = 0.9f,
            ["color"] = new Color(UITheme.HoloPale, 0.32f),
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
        _shipBody = new Sprite2D
        {
            Texture = PlayerTex,
            Scale = Vector2.One * ShipScale,
            Modulate = new Color(0.94f, 0.97f, 1.02f),
        };
        _shipBobber.AddChild(_shipBody);

        // 尾焰怠速（青色对齐主色板；喷口按贴图 254px × scale 2 折算）
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
                ["color"] = new Color(1.0f, 0.68f, 0.26f, 0.8f),
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
                ["color"] = new Color(1.0f, 0.92f, 0.72f, 1.0f),
            });
            coreFlame.Position = new Vector2(side, 110.0f);
            coreFlame.AmountRatio = 0.0f;
            _shipBobber.AddChild(coreFlame);
            _engines.Add(coreFlame);

            // 喷口辉光（SoftGlow(24) 基准 scale = 24/32 = 0.75）
            var nozzle = CinematicFx.SoftGlow(24.0f, new Color(1.0f, 0.72f, 0.30f, 0.6f));
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

        // 机型变更订阅：标题屏的机型面板就地选定后（不切场景），悬挂展示必须跟着换外形——
        // 只按 _Ready 读一次的话，玩家选完看不到任何变化，会以为没选上。
        // IsConnected 守卫：重入树路径不重复连接（同 Player / GameOverUi 口径）。
        var gs = GameState.Instance;
        if (!gs.IsConnected(GameState.SignalName.MachineChanged, _onMachineChanged))
        {
            gs.Connect(GameState.SignalName.MachineChanged, _onMachineChanged);
        }
    }

    /// <summary>机型变更（标题屏面板选定 / 「继续上次出击」同步存档机型）：机体本体与两枚幽灵拖影
    /// 一起换贴图。拖影通常已淡出退场，但玩家可能在飞入途中就选完机型——只换本体的话，
    /// 旧的残影会叠在新型上多留半秒。</summary>
    private void OnMachineChanged(string id)
    {
        var tex = GD.Load<Texture2D>(MachineRoster.SpritePath(id));
        _playerTex = tex;
        SetShipTexture(_shipBody, tex);
        foreach (var ghost in _ghosts)
        {
            SetShipTexture(ghost, tex);
        }
    }

    /// <summary>换一张机体贴图（节点已失效时跳过：信号可能在本节点退场的那一帧里到达）。</summary>
    private static void SetShipTexture(Sprite2D sprite, Texture2D? texture)
    {
        if (GodotObject.IsInstanceValid(sprite))
        {
            sprite.Texture = texture;
        }
    }

    public override void _ExitTree()
    {
        // 显式断开 GameState 信号连接（C# Connect 连接不随接收方释放自动断开）
        // autoload 可能先于本节点释放（非常规拆树序），Instance getter 会抛异常，故安全取值
        var gs = GameState.TryGetInstance();
        if (gs == null)
        {
            return;
        }

        if (gs.IsConnected(GameState.SignalName.MachineChanged, _onMachineChanged))
        {
            gs.Disconnect(GameState.SignalName.MachineChanged, _onMachineChanged);
        }
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
            ["color"] = new Color(1.0f, 0.70f, 0.28f, 0.32f),
            ["core_color"] = new Color(1.0f, 0.92f, 0.72f, 0.5f),
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

        // 喷口怠速微闪：点火缩放已结束后再起（各喷口相位错开），避免与落位 tween 抢 scale；
        // ReduceFlash 时保持静止
        if (!GameState.Instance.ReduceFlash)
        {
            for (var i = 0; i < _nozzleGlows.Count; i++)
            {
                var nozzle = _nozzleGlows[i];
                var baseScale = nozzle.Scale; // 点火终态（0.75 基准）
                var flicker = nozzle.CreateTween().SetLoops();
                flicker.TweenInterval(0.12 * i);
                flicker.TweenProperty(nozzle, "scale", baseScale * 1.1f, 0.42).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                flicker.TweenProperty(nozzle, "scale", baseScale * 0.93f, 0.5).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            }
        }
    }
}
