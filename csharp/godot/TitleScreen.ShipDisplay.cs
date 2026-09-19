using System;
using Godot;
using InfiAir.Core.Machines;
using InfiAir.Core.Text;
using InfiAir.Core.Visual;

namespace InfiAir;

/// <summary>
/// 标题屏·玩家机悬挂展示部分：自跃迁远点飞入（RadialStreaks 光线场 + 位置/尺寸减速缓动）→
/// 右侧停驻（轮廓背光 + 幽灵拖影退场 + 尾焰怠速点火 + 悬浮浮动）+ 铭牌。
/// 时轴：0.2s 起飞 1.4s → 1.6s 落位收尾；全程 Tween/Timer，无 await。
///
/// **展示哪一型由轮展袋掷（<see cref="MachineBag"/>）**：每次进标题屏从六型里换一架，
/// 一袋之内不重复、跨袋也不连出同型——同一个顺序反复出现会被读成「只有这一型」，
/// 而独立掷签在六型里连出同型的概率是 1/6，玩家一晚上必然撞见。
/// 铭牌写明此刻挂着的是哪一型、强在哪；机型面板选定后立刻换成刚选的那一型（原有反馈保留），
/// 此时铭牌标签由「机库展示」变为「现役机型」——玩家一眼能分清「挂着看的」与「我要飞的」。
/// 尾焰与喷口辉光的取位走 core `PlayerHullLayout` 的双发喷口锚点（六型共用同一套骨架坐标）。
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    private const float ShipScale = 2.0f; // 254px 原生贴图 ×2：首次大尺寸细节展示
    private const float FlyDuration = 1.4f;

    /// <summary>尾焰起点相对喷口锚点的下沉量（局部像素）：喷管口在锚点下方约 4.5 贴图像素（＝9 局部像素），
    /// 粒子自口部稍下起燃，读作「从喷口喷出来」而不是「罩在喷口上」。</summary>
    private const float FlameDrop = 8.0f;

    /// <summary>喷口锚点（贴图坐标，中心原点）→ 悬挂展示的局部坐标：锚点 × 展示缩放。</summary>
    private static Vector2 NozzlePos(HullAnchor anchor) =>
        new((float)(anchor.X * ShipScale), (float)(anchor.Y * ShipScale));

    // 铭牌：机体停驻位正下方（机体中心 y=470、半高 254 → 下缘 724；背光外圈到 670），
    // 与底部入口行（y≈1008）之间留出整段空白，故铭牌落在 y=748 起
    private static readonly Vector2 ShipPlatePos = new(1190.0f, 748.0f);
    private static readonly Vector2 ShipPlateSize = new(340.0f, 116.0f);

    /// <summary>轮展袋（**跨标题屏实例保留**：每进一次标题屏换一架，而不是每开一次机掷一次）。
    /// 静态字段持有的是纯 C# 对象，不碰 Godot 资源的退出 finalize 规则（同 ChamferedPanel 的教训）；
    /// 不给种子 → 时间种子，每次开机顺序不同，同一次运行内六型各出一次。</summary>
    private static readonly MachineBag ShowcaseBag = new(MachineRoster.All);

    private Node2D _shipAnchor = null!;
    private Node2D _shipBobber = null!;
    private Node2D _warpStreaks = null!;
    private Sprite2D _shipBody = null!;
    private readonly System.Collections.Generic.List<GpuParticles2D> _engines = new();
    private readonly System.Collections.Generic.List<Sprite2D> _nozzleGlows = new();
    private readonly System.Collections.Generic.List<Sprite2D> _ghosts = new();

    private Texture2D? _playerTex;

    /// <summary>此刻挂着的机型（轮展袋抽出，或机型面板刚选定的那一型）。</summary>
    private MachineSpec _showcase = MachineRoster.Default;

    // 铭牌三段（落位后淡入；换机时刷新）
    private Label? _plateRole;
    private Label? _plateName;
    private Label? _plateTrait;

    /// <summary>机型变更回调：构造期建 Callable，_ExitTree 按同一实例断开。</summary>
    private readonly Callable _onMachineChanged;

    public TitleScreen()
    {
        _onMachineChanged = Callable.From<string>(OnMachineChanged);
    }

    /// <summary>当前展示机型的机体贴图（实例级缓存 GD.Load，命中引擎资源缓存；不做 static 持有——
    /// 引擎退出 finalize segfault 规则，同 ChamferedPanel.StreakTex）。</summary>
    private Texture2D? PlayerTex => _playerTex ??= GD.Load<Texture2D>(MachineRoster.SpritePath(_showcase));

    private void BuildShipDisplay()
    {
        _showcase = ShowcaseBag.Next();
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

        // 尾焰怠速：两处喷口取位＝core 喷口锚点（贴图 108/146, 230）× 展示缩放，
        // 与机体贴图同一坐标系——尾焰从机尾双发喷出，不再从机身中段冒出来。
        // 焰长压在铭牌上缘（局部 y≈278）之内：怠速焰是短焰，长了会穿到牌子后面。
        foreach (var nozzlePos in new[] { NozzlePos(PlayerHullLayout.EngineLeft), NozzlePos(PlayerHullLayout.EngineRight) })
        {
            var flame = CinematicFx.Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 26,
                ["lifetime"] = 0.26f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 14.0f,
                ["vel_min"] = 130.0f,
                ["vel_max"] = 200.0f,
                ["scale_min"] = 5.0f,
                ["scale_max"] = 9.0f,
                ["color"] = new Color(1.0f, 0.68f, 0.26f, 0.8f),
            });
            flame.Position = nozzlePos + new Vector2(0.0f, FlameDrop);
            flame.AmountRatio = 0.0f; // 点火前无粒子
            _shipBobber.AddChild(flame);
            _engines.Add(flame);

            var coreFlame = CinematicFx.Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 16,
                ["lifetime"] = 0.2f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 8.0f,
                ["vel_min"] = 90.0f,
                ["vel_max"] = 150.0f,
                ["scale_min"] = 2.5f,
                ["scale_max"] = 5.0f,
                ["color"] = new Color(1.0f, 0.92f, 0.72f, 1.0f),
            });
            coreFlame.Position = nozzlePos + new Vector2(0.0f, FlameDrop - 2.0f);
            coreFlame.AmountRatio = 0.0f;
            _shipBobber.AddChild(coreFlame);
            _engines.Add(coreFlame);

            // 喷口辉光（SoftGlow(24) 基准 scale = 24/32 = 0.75）：压在喷管口上，罩住装甲环
            var nozzle = CinematicFx.SoftGlow(24.0f, new Color(1.0f, 0.72f, 0.30f, 0.6f));
            nozzle.Position = nozzlePos + new Vector2(0.0f, 2.0f);
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

        BuildShipPlate();

        // 机型变更订阅：标题屏的机型面板就地选定后（不切场景），悬挂展示必须跟着换外形——
        // 只按 _Ready 读一次的话，玩家选完看不到任何变化，会以为没选上。
        // IsConnected 守卫：重入树路径不重复连接（同 Player / GameOverUi 口径）。
        var gs = GameState.Instance;
        if (!gs.IsConnected(GameState.SignalName.MachineChanged, _onMachineChanged))
        {
            gs.Connect(GameState.SignalName.MachineChanged, _onMachineChanged);
        }
    }

    /// <summary>
    /// 悬挂铭牌：机体停驻位正下方的一块切角面板——标签行（现役机型 / 机库展示）+ 机型名 +
    /// 该型的加成一行。
    /// 为什么要有这块牌子：展示机改成轮展之后，「挂着的」不再等于「要飞的」，没有牌子时
    /// 玩家看到别的机型只会以为自己的选择丢了；牌子上写明机型与其加成，轮展就从「随机」
    /// 变成了「机库巡礼」——顺手把六型各强在哪讲了一遍，而玩家不必先打开机型面板。
    /// 加成百分比与实际乘区同出一条求值路径（<see cref="MachineTraitText"/> 反算），
    /// 与机型面板上的数字不可能分叉。
    /// </summary>
    private void BuildShipPlate()
    {
        var plate = new ChamferedPanel
        {
            Chamfer = 10.0f,
            BgColor = new Color(UITheme.PanelBg, 0.62f),
            CustomMinimumSize = ShipPlateSize,
            Position = ShipPlatePos,
            Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f), // 落位后（1.9s）淡入，不与飞入抢视线
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(plate);

        // 顶缘一道琥珀细线：全站面板的页头语汇，把「这块牌子属于上面那架机体」连起来
        var rule = new ColorRect
        {
            Color = new Color(UITheme.Accent, 0.55f),
            CustomMinimumSize = new Vector2(ShipPlateSize.X - 56.0f, 2.0f),
            Position = new Vector2(28.0f, 16.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        plate.AddChild(rule);

        var vbox = new VBoxContainer
        {
            Position = new Vector2(16.0f, 26.0f),
            Size = new Vector2(ShipPlateSize.X - 32.0f, ShipPlateSize.Y - 38.0f),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        vbox.AddThemeConstantOverride("separation", 2);
        plate.AddChild(vbox);

        _plateRole = UITheme.MakeLabel(string.Empty, UITheme.FontSmall, UITheme.TextDim, HorizontalAlignment.Center);
        vbox.AddChild(_plateRole);
        _plateName = UITheme.MakeLabel(string.Empty, UITheme.FontHeader, UITheme.AccentHot, HorizontalAlignment.Center);
        vbox.AddChild(_plateName);
        _plateTrait = UITheme.MakeLabel(string.Empty, UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Center);
        vbox.AddChild(_plateTrait);

        RefreshPlate(punch: false);
        var plateIn = plate.CreateTween();
        plateIn.TweenProperty(plate, "modulate:a", 1.0f, 0.5).SetDelay(1.9).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>铭牌三段按当前展示机型重写；<paramref name="punch"/> 时名字弹一下（换机反馈）。</summary>
    private void RefreshPlate(bool punch)
    {
        if (_plateName == null || _plateRole == null || _plateTrait == null || !GodotObject.IsInstanceValid(_plateName))
        {
            return;
        }

        var active = string.Equals(_showcase.Id, GameState.Instance.MachineId, StringComparison.Ordinal);
        // 两个键都写成字面量 Tr 调用（条件表达式里放键名时文案门禁扫不到，缺键会静默显示键名本身）
        _plateRole.Text = active ? (string)Tr("TITLE_SHOWCASE_ACTIVE") : (string)Tr("TITLE_SHOWCASE_ROTATION");
        _plateName.Text = (string)Tr(_showcase.NameKey);
        _plateTrait.Text = GdFormat.Format(
            Tr(MachineTraitText.Key(_showcase.Trait)),
            MachineTraitText.SignedPercent(_showcase.Trait, GameState.Instance.MachineModsFor(_showcase.Id)));
        _plateRole.AddThemeColorOverride("font_color", active ? UITheme.AccentGold : UITheme.TextDim);
        _plateName.AddThemeColorOverride("font_color", active ? UITheme.AccentHot : UITheme.Text);
        if (punch)
        {
            UITheme.PunchScale(_plateName, 1.08f, 0.22f);
        }
    }

    /// <summary>机型变更（标题屏面板选定 / 「继续上次出击」同步存档机型）：机体本体与两枚幽灵拖影
    /// 一起换贴图，铭牌跟着刷新。拖影通常已淡出退场，但玩家可能在飞入途中就选完机型——
    /// 只换本体的话，旧的残影会叠在新型上多留半秒。</summary>
    private void OnMachineChanged(string id)
    {
        _showcase = MachineRoster.ById(id);
        var tex = GD.Load<Texture2D>(MachineRoster.SpritePath(_showcase));
        _playerTex = tex;
        SetShipTexture(_shipBody, tex);
        foreach (var ghost in _ghosts)
        {
            SetShipTexture(ghost, tex);
        }

        RefreshPlate(punch: true);
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
