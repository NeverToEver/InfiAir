using System;
using System.Collections.Generic;
using Godot;
using InfiAir.Core;
using InfiAir.Core.Machines;
using InfiAir.Core.Text;
using InfiAir.Core.Visual;

namespace InfiAir;

/// <summary>
/// 初始机型轮盘（标题屏 M 与底部「机型」入口共用）：左缘圆盘上排六型，右区是**被选中的那一架**——
/// 转动轮盘时旧机向上右加速飞出、新机自远处跃迁飞入（光线场 + 拖影 + 制动冲击环），
/// 落位后播该型的**特性演出**（<see cref="MachineShowcaseKind"/>：横移冲刺 / 重炮出膛 /
/// 三连点射 / 护盾弹开 / 装甲环带），铭牌同步写机型名与加成 / 代价两行。
///
/// **浏览不写入**：转动轮盘只换焦点与展示，写机型只有一条路——<see cref="Confirm"/>（回车 / 手柄 A /
/// 点机体），它走生产单口 <c>GameState.SetMachine</c>（归一 id → 落偏好 → 结算血上限 → 广播
/// <c>MachineChanged</c>，标题屏悬挂展示据此换机）。确认不关面板：玩家可以逐型点过去比一比，
/// 铭牌标签随之在「现役机型 / 候选机型」间切换，Esc 退。
///
/// 为什么是轮盘而不是列表：全站菜单导航面统一为左缘圆盘（天赋 / 暂停 / 结算 / 基地 / 设置），
/// 机型选择此前是唯一一页列表——同一件事两套操作语汇，玩家每次都要重学一遍怎么移动焦点。
/// 骨架复用 <see cref="RadialMenuLayer"/>（dim + 轮盘 + 引线 + 四角括弧），不另造一套。
/// </summary>
public partial class MachineWheelPanel : RadialMenuLayer
{
    /// <summary>面板画层：取值理由同 <see cref="PracticePanel"/>（高于全部页面 1–25、低于退出确认 40）。</summary>
    private const int PanelLayer = 30;

    /// <summary>展示机缩放：254px 原生贴图 ×2（与标题屏悬挂展示同档，看的就是细节）。</summary>
    private const float ShipScale = 2.0f;

    /// <summary>尾焰起点相对喷口锚点的下沉量：同 <c>TitleScreen.ShipDisplay</c> 的口径（自喷管口起燃）。</summary>
    private const float FlameDrop = 8.0f;

    // ---- 布局（1920×1080 设计坐标；轮盘圆心由 RadialMenuLayer.WheelRest 给）----
    private static readonly Vector2 ShipRest = new(1160.0f, 420.0f);
    /// <summary>铭牌左上角：x 随板宽加宽而左移 50，使板心恒在 x=1160（＝ <see cref="ShipRest"/> 的 x，
    /// 铭牌始终正对机体；只加宽不挪位会让板心右偏 50px，与机体错开）。</summary>
    private static readonly Vector2 PlatePos = new(880.0f, 686.0f);

    /// <summary>铭牌尺寸。**宽 460→560**（高 168 不动）：能力项并入加成 / 代价两行后，
    /// 英文最长串（壁垒「Damage taken -15% · Parry window 0.70s」在 FontBody 下实测约 449px）
    /// 已贴死旧内容框的 424px——自动换行一开就会折成两行，而加高会让下方 note 与提示行被挤掉。
    /// 加宽到 560 后内容框 524px，最长串留出约 75px 余量，两行仍各占一行。</summary>
    private static readonly Vector2 PlateSize = new(560.0f, 168.0f);

    private static readonly Vector2 FlyFar = new(430.0f, -300.0f);    // 飞入起点（相对停驻位，右上远处）
    private static readonly Vector2 FlyOutPos = new(380.0f, -260.0f); // 飞出终点（同向加速离场）

    private const float FlyInTime = 0.42f;
    private const float FlyOutTime = 0.24f;

    /// <summary>面板退场（取消后自关）；打开方据此清掉自己对面板的引用。</summary>
    public event Action? Closed;

    private Node2D _shipRest = null!;
    private Node2D _flyer = null!;
    private Sprite2D _body = null!;
    private readonly List<Sprite2D> _ghosts = new();
    private ChamferedPanel _plate = null!;
    private Label _role = null!;
    private Label _name = null!;
    private Label _tag = null!;
    private Label _trait = null!;
    private Label _penalty = null!;

    /// <summary>换机序号：旧动画的回调据此作废——连点方向键时不留半截动画（飞入收尾回调里
    /// 还要播特性演出与制动环，作废判据少一个就会在旧机上炸出新机的演出）。</summary>
    private int _swapSeq;

    private Tween? _swap;
    private Tween? _traitMotion;

    /// <summary>此刻展示的机型（轮盘焦点；不等于生效机型——生效的那个只在 <see cref="Confirm"/> 里改）。</summary>
    private MachineSpec _focused = MachineRoster.Default;

    public override void _Ready()
    {
        Layer = PanelLayer;
        // 可挂载节点一律自己 Always：挂到暂停的树上时默认继承的暂停态会让面板一个输入都收不到，
        // 而它看上去只是「按了没反应」（同 MachinePanel / PracticePanel 口径）
        ProcessMode = ProcessModeEnum.Always;

        BuildChrome();
        BuildShipDisplay();
        BuildPlate();
        SetContentAnchor(() => _plate);
        BuildMenu();
        RaiseWheel();

        Wheel.FocusChanged += OnWheelFocusChanged;
        Wheel.Confirmed += OnWheelConfirmed;
        // 开页聚焦生效机型（FocusOption 不触发 FocusChanged，故展示要自己起一次）
        var start = MachineRoster.IndexOf(GameState.Instance.MachineId);
        Wheel.FocusOption(start);
        _focused = MachineRoster.At(start);
        RefreshPlate(punch: false);
        FlyIn(_focused, ++_swapSeq);

        PlayWheelEntrance();
    }

    /// <summary>喷口锚点（贴图坐标，中心原点）→ 面板局部坐标：锚点 × 展示缩放。</summary>
    private static Vector2 NozzlePos(HullAnchor anchor) =>
        new((float)(anchor.X * ShipScale), (float)(anchor.Y * ShipScale));

    /// <summary>展示机：轮廓背光（不动）+ 可动层（位置/缩放/旋转归动画写）+ 机体 + 拖影 + 尾焰怠速。
    /// 背光挂在 <see cref="Node2D"/> 停驻位上、机体挂在可动层里：换机只动可动层，背光不跟着飞。</summary>
    private void BuildShipDisplay()
    {
        _shipRest = new Node2D { Position = ShipRest };
        AddChild(_shipRest);

        var rim = CinematicFx.SoftGlow(190.0f, new Color(1.0f, 0.66f, 0.24f, 0.16f));
        rim.Position = new Vector2(0.0f, 10.0f);
        _shipRest.AddChild(rim);
        var rimCore = CinematicFx.SoftGlow(90.0f, new Color(1.0f, 0.86f, 0.52f, 0.18f));
        rimCore.Position = new Vector2(0.0f, -6.0f);
        _shipRest.AddChild(rimCore);

        _flyer = new Node2D();
        _shipRest.AddChild(_flyer);

        for (var k = 1; k <= 2; k++)
        {
            var ghost = new Sprite2D
            {
                Scale = Vector2.One * ShipScale,
                Position = new Vector2(26.0f * k, -30.0f * k),
                Modulate = new Color(0.6f, 0.9f, 1.0f, 0.0f),
                ZIndex = -1,
            };
            _flyer.AddChild(ghost);
            _ghosts.Add(ghost);
        }

        _body = new Sprite2D
        {
            Scale = Vector2.One * ShipScale,
            Modulate = new Color(0.94f, 0.97f, 1.02f, 0.0f),
        };
        _flyer.AddChild(_body);

        // 尾焰怠速：取位同标题屏（core 喷口锚点 × 展示缩放）——机体停驻时是「点火待发」而不是一张贴图
        foreach (var nozzle in new[] { NozzlePos(PlayerHullLayout.EngineLeft), NozzlePos(PlayerHullLayout.EngineRight) })
        {
            var flame = CinematicFx.Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 22,
                ["lifetime"] = 0.24f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 14.0f,
                ["vel_min"] = 120.0f,
                ["vel_max"] = 190.0f,
                ["scale_min"] = 4.0f,
                ["scale_max"] = 8.0f,
                ["color"] = new Color(1.0f, 0.68f, 0.26f, 0.8f),
            });
            flame.Position = nozzle + new Vector2(0.0f, FlameDrop);
            _flyer.AddChild(flame);
        }
    }

    /// <summary>铭牌：标签行（现役 / 候选）+ 机型名 + 加成 + 代价，下方接一行机型语义说明
    /// （选定即保存、代价压暗一行），底部是轮盘操作提示。三段与标题屏悬挂铭牌同一版式语汇。</summary>
    private void BuildPlate()
    {
        // 页题压在右区顶带（y≈96）：机体画布自 166 起，题字与机体之间留得开——
        // 落在铭牌上方那一带会横穿机体（展示位比铭牌高一整个机位）
        var title = UITheme.MakeLabel((string)Tr("MACHINE_TITLE"), UITheme.FontHeader, UITheme.AccentGold);
        title.Position = new Vector2(PlatePos.X, 96.0f);
        title.CustomMinimumSize = new Vector2(PlateSize.X, 0.0f);
        AddChild(title);

        _plate = new ChamferedPanel
        {
            Chamfer = 10.0f,
            BgColor = new Color(UITheme.PanelBg, 0.72f),
            CustomMinimumSize = PlateSize,
            Position = PlatePos,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_plate);

        var rule = new ColorRect
        {
            Color = new Color(UITheme.Accent, 0.55f),
            CustomMinimumSize = new Vector2(PlateSize.X - 56.0f, 2.0f),
            Position = new Vector2(28.0f, 16.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _plate.AddChild(rule);

        var vbox = new VBoxContainer
        {
            Position = new Vector2(18.0f, 26.0f),
            Size = new Vector2(PlateSize.X - 36.0f, PlateSize.Y - 36.0f),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        vbox.AddThemeConstantOverride("separation", 2);
        _plate.AddChild(vbox);

        _role = UITheme.MakeLabel(string.Empty, UITheme.FontSmall, UITheme.TextDim, HorizontalAlignment.Center);
        _name = UITheme.MakeLabel(string.Empty, UITheme.FontHeader, UITheme.AccentHot, HorizontalAlignment.Center);
        _tag = UITheme.MakeLabel(string.Empty, UITheme.FontSmall, UITheme.AccentGold, HorizontalAlignment.Left);
        _trait = UITheme.MakeLabel(string.Empty, UITheme.FontBody, UITheme.AccentGold, HorizontalAlignment.Center);
        _penalty = UITheme.MakeLabel(string.Empty, UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Center);
        // 自动换行：能力项并入两行后英文最长串已接近内容框宽度（余量约 75px），换行是溢出前
        // 最后一道防线——不开时超宽串会被 Label 静默裁掉/顶出板外，不报错也看不出来。
        // WordSmart：中英混排按词边界折，中文无空格时按字折。
        _trait.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _penalty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_role);
        vbox.AddChild(BuildNameRow());
        vbox.AddChild(_trait);
        vbox.AddChild(_penalty);

        var note = UITheme.MakeLabel((string)Tr("MACHINE_NOTE"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Center);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.Position = new Vector2(PlatePos.X - 40.0f, PlatePos.Y + PlateSize.Y + 12.0f);
        note.CustomMinimumSize = new Vector2(PlateSize.X + 80.0f, 0.0f);
        AddChild(note);

        var hint = UITheme.MakeLabel((string)Tr("MACHINE_WHEEL_HINT"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Center);
        hint.Position = new Vector2(560.0f, 986.0f);
        hint.CustomMinimumSize = new Vector2(1040.0f, 0.0f);
        AddChild(hint);
    }

    /// <summary>机型名 + 性格标签同一行：标签紧邻名字右侧，字号与配色从既有层级取
    /// （FontSmall / AccentGold），不新造层级。名字居中、标签靠左紧随其右——标签跟名字走，
    /// 换机型时整行一起读，比另起一行更省板高（板高不动的前提之一）。</summary>
    private Control BuildNameRow()
    {
        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(_name);
        _tag.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_tag);
        return row;
    }

    /// <summary>轮盘选项：六个叶子（无子层），顺序 ＝ 名册顺序（与机型面板、存档 id 同一份名册）。
    /// 图标按演出类型取——卡片上的符号与选中后播的那段演出同源，不是两套语汇。</summary>
    private void BuildMenu()
    {
        var options = new List<RadialWheelOption>(MachineRoster.Count);
        foreach (var spec in MachineRoster.All)
        {
            options.Add(new RadialWheelOption
            {
                Id = spec.Id,
                Label = (string)Tr(spec.NameKey),
                Glyph = GlyphOf(MachineShowcase.KindOf(spec.Id)),
            });
        }

        LoadMenu(options, string.Empty);
    }

    private static RadialGlyph GlyphOf(MachineShowcaseKind kind) => kind switch
    {
        MachineShowcaseKind.Speed => RadialGlyph.Chevron,
        MachineShowcaseKind.Impact => RadialGlyph.Hex,
        MachineShowcaseKind.Burst => RadialGlyph.Bolt,
        MachineShowcaseKind.Shield => RadialGlyph.Ring,
        MachineShowcaseKind.Armor => RadialGlyph.Star,
        _ => RadialGlyph.Diamond,
    };

    /// <summary>焦点变化（轮盘转动 / 鼠标划过卡片）：**只换展示**——写机型不在这里，见 <see cref="Confirm"/>。</summary>
    private void OnWheelFocusChanged(int index)
    {
        var spec = MachineRoster.At(index);
        if (string.Equals(spec.Id, _focused.Id, StringComparison.Ordinal))
        {
            return;
        }

        _focused = spec;
        RefreshPlate(punch: true);
        SwapTo(spec);
    }

    /// <summary>确认（轮盘叶子确认 / 点机体）：走 GameState 唯一写入口落定这一型。
    /// 确认后不关面板——玩家可以逐型点过去比一比；铭牌标签随之在「现役 / 候选」间切换。</summary>
    private void OnWheelConfirmed(RadialWheelOption option) => Confirm(option.Id);

    /// <summary>确认某一型（点机体与轮盘确认同一出口）。</summary>
    public void Confirm(string id)
    {
        GameState.Instance.SetMachine(id);
        RefreshPlate(punch: true);
        BodyPulse(1.08f, 0.35f);
        // 落定拍：一圈琥珀环自机位张开，读作「就是它了」
        var ring = CinematicFx.Shockwave(new Godot.Collections.Dictionary
        {
            ["radius"] = 170.0f,
            ["time"] = 0.5f,
            ["width"] = 6.0f,
            ["color"] = new Color(UITheme.Accent, 0.55f),
            ["core_color"] = new Color(1.0f, 0.95f, 0.8f, 0.85f),
        });
        ring.Position = ShipRest;
        AddChild(ring);
    }

    /// <summary>换机：旧机飞出 → 新机飞入 → 落位播特性演出。连点方向键时上一段动画作废
    /// （<see cref="_swapSeq"/>），故这里先杀旧 tween 再把可动层摆到起飞位，不留半截姿态。</summary>
    private void SwapTo(MachineSpec spec)
    {
        _swap?.Kill();
        _traitMotion?.Kill();
        var seq = ++_swapSeq;

        var outTween = CreateTween().SetParallel(true);
        outTween.TweenProperty(_flyer, "position", FlyOutPos, FlyOutTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        outTween.TweenProperty(_flyer, "scale", Vector2.One * 0.4f, FlyOutTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        outTween.TweenProperty(_body, "modulate:a", 0.0f, FlyOutTime);
        foreach (var ghost in _ghosts)
        {
            outTween.TweenProperty(ghost, "modulate:a", 0.0f, FlyOutTime * 0.7f);
        }

        outTween.Chain().TweenCallback(Callable.From(() =>
        {
            if (seq == _swapSeq)
            {
                FlyIn(spec, seq);
            }
        }));
        _swap = outTween;
    }

    /// <summary>新机自右上远处跃迁飞入：光线场爆发 + 减速接近 + 拖影，落位时制动冲击环 + 特性演出。</summary>
    private void FlyIn(MachineSpec spec, int seq)
    {
        var tex = GD.Load<Texture2D>(MachineRoster.SpritePath(spec));
        _body.Texture = tex;
        foreach (var ghost in _ghosts)
        {
            ghost.Texture = tex;
        }

        _flyer.Position = FlyFar;
        _flyer.Scale = Vector2.One * 0.42f;
        _flyer.Rotation = 0.0f;
        _body.Modulate = new Color(0.94f, 0.97f, 1.02f, 0.0f);

        var streaks = CinematicFx.RadialStreaks(new Godot.Collections.Dictionary
        {
            ["count"] = 22,
            ["max_radius"] = 520.0f,
            ["cycle"] = 0.7f,
            ["color"] = new Color(UITheme.HoloPale, 0.30f),
        });
        streaks.Position = FlyFar * 0.5f;
        streaks.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        _shipRest.AddChild(streaks);
        var streakFade = CreateTween();
        streakFade.TweenProperty(streaks, "modulate:a", 1.0f, 0.12);
        streakFade.TweenProperty(streaks, "modulate:a", 0.0f, 0.5);
        streakFade.TweenCallback(Callable.From(streaks.QueueFree));

        var inTween = CreateTween().SetParallel(true);
        inTween.TweenProperty(_flyer, "position", Vector2.Zero, FlyInTime)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        inTween.TweenProperty(_flyer, "scale", Vector2.One, FlyInTime)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        inTween.TweenProperty(_body, "modulate:a", 1.0f, FlyInTime * 0.7f);
        for (var k = 0; k < _ghosts.Count; k++)
        {
            inTween.TweenProperty(_ghosts[k], "modulate:a", 0.10f - 0.04f * k, FlyInTime * 0.5f);
        }

        inTween.Chain().TweenCallback(Callable.From(() =>
        {
            if (seq != _swapSeq)
            {
                return;
            }

            foreach (var ghost in _ghosts)
            {
                var fade = CreateTween();
                fade.TweenProperty(ghost, "modulate:a", 0.0f, 0.4);
            }

            var brake = CinematicFx.Shockwave(new Godot.Collections.Dictionary
            {
                ["radius"] = 130.0f,
                ["time"] = 0.45f,
                ["width"] = 8.0f,
                ["color"] = new Color(1.0f, 0.70f, 0.28f, 0.32f),
                ["core_color"] = new Color(1.0f, 0.92f, 0.72f, 0.5f),
            });
            brake.Position = ShipRest;
            AddChild(brake);

            PlayTraitShowcase(MachineShowcase.KindOf(spec.Id));
        }));
        _swap = inTween;
    }

    /// <summary>铭牌重写：标签行按「焦点是否等于生效机型」在现役 / 候选间切；数值层百分比
    /// 与实际乘区同出一条求值路径（<see cref="MachineTraitText"/> 反算）、能力层自然量与实际
    /// 能力档案同出一条求值路径（<see cref="MachineKitTable"/>），与起飞时的取值不可能分叉。
    /// 两层并入同一行：加成行＝我拿到什么（数值加成 ＋ 能力强化），代价行＝我付出什么
    /// （数值代价 ＋ 能力代价）。</summary>
    private void RefreshPlate(bool punch)
    {
        if (_name == null || !GodotObject.IsInstanceValid(_name))
        {
            return;
        }

        var active = string.Equals(_focused.Id, GameState.Instance.MachineId, StringComparison.Ordinal);
        // 两个键都写成字面量 Tr 调用（条件表达式里放键名时文案门禁扫不到，缺键会静默显示键名本身）
        _role.Text = active ? (string)Tr("TITLE_SHOWCASE_ACTIVE") : (string)Tr("MACHINE_WHEEL_CANDIDATE");
        _name.Text = (string)Tr(_focused.NameKey);
        _tag.Text = (string)Tr(_focused.TagKey);
        _trait.Text = RowText(_focused.Trait, _focused.KitBonus);
        // 标准型没有代价：留空而不是把「无加成 · 基准配置」印两遍（同一句话连写两行像排版事故）
        _penalty.Text = _focused.Penalty == MachineTrait.None && _focused.KitPenalty == MachineAxis.None
            ? string.Empty
            : RowText(_focused.Penalty, _focused.KitPenalty);
        _role.AddThemeColorOverride("font_color", active ? UITheme.AccentGold : UITheme.TextDim);
        _name.AddThemeColorOverride("font_color", active ? UITheme.AccentHot : UITheme.Text);
        if (punch)
        {
            UITheme.PunchScale(_name, 1.08f, 0.22f);
        }
    }

    /// <summary>一行的合成：数值层（百分比，带符号）＋ 能力层（自然量秒数，不带符号），
    /// 以「 · 」相接。一层缺席就只印另一层——缺一层时不留悬空分隔符。
    /// **标准型两层都缺席，此时原样印它那条「无加成 · 基准配置」**：那行是它的身份读数，
    /// 不是空行（两条轴都是 None 时把它吃掉会让标准型的加成行变成空白）。
    /// 能力项写自然量而非百分比，是因为四条轴的自然量各不相同（窗＝窗口秒数、循环＝流程＋冷却、
    /// 冷却＝冷却秒数、耗油＝满箱续航），印乘区百分比会与玩家实感对不上；且加成行里出现负号
    /// 会被读成「这型更差」。</summary>
    private string RowText(MachineTrait trait, MachineAxis axis)
    {
        if (trait == MachineTrait.None && axis == MachineAxis.None)
        {
            return TraitText(trait);
        }

        var numeric = trait == MachineTrait.None ? string.Empty : TraitText(trait);
        var ability = axis == MachineAxis.None ? string.Empty : KitText(axis);
        if (numeric.Length == 0)
        {
            return ability;
        }

        return ability.Length == 0 ? numeric : numeric + " · " + ability;
    }

    /// <summary>某一轴（加成或代价）的文案：百分比与正负号都由实际生效的乘区反算
    /// （数值被调过、甚至被调成反向，面板上的数字与符号都跟着走）。</summary>
    private string TraitText(MachineTrait trait)
        => GdFormat.Format(
            Tr(MachineTraitText.Key(trait)),
            MachineTraitText.SignedPercent(trait, GameState.Instance.MachineModsFor(_focused.Id)));

    /// <summary>某条能力轴的文案：自然量（秒，两位小数）由实际生效的能力档案经
    /// <see cref="MachineKitTable.NaturalQuantity"/> 反算——与四条轴在 <c>Player</c> 里的消费
    /// 读同一份档案，改 <c>balance.json</c> 一处面板自动跟随。**不带正负号、不写百分比**：
    /// 四条轴的自然量各不相同（见 <see cref="RowText"/>），百分比会在加成行里印出负号。
    /// 文案键取 <see cref="MachineKitTable.TextKey"/>（<c>MACHINE_KIT_*</c>，%s 占位）。</summary>
    private string KitText(MachineAxis axis)
        => GdFormat.Format(
            Tr(MachineKitTable.TextKey(axis)),
            MachineKitTable.NaturalQuantity(axis, GameState.Instance.KitFor(_focused.Id)).ToString("0.00"));

    /// <summary>机体提亮一瞬（确认落定 / 校准环 / 装甲演出共用）：**只写 RGB，不碰 alpha**——
    /// alpha 归换机过渡独占（写 `modulate` 整份会把正在淡出的机体一把点亮，换机中途确认就穿帮）。</summary>
    private void BodyPulse(float peak, float time)
    {
        var t = CreateTween();
        t.TweenProperty(_body, "modulate:r", 0.94f * peak, time * 0.35f);
        t.Parallel().TweenProperty(_body, "modulate:g", 0.97f * peak, time * 0.35f);
        t.Parallel().TweenProperty(_body, "modulate:b", 1.02f * peak, time * 0.35f);
        t.TweenProperty(_body, "modulate:r", 0.94f, time * 0.65f);
        t.Parallel().TweenProperty(_body, "modulate:g", 0.97f, time * 0.65f);
        t.Parallel().TweenProperty(_body, "modulate:b", 1.02f, time * 0.65f);
    }

    // ---- 特性演出（选中某型时播一段，把「这型强在哪」演出来）----
    // 语汇与全站特效一致：软点辉光 / 加色 / 冲击环；时长都在 1s 上下——展台演出，不拖住玩家。
    // 位置取贴图骨架：机首 (127,16) → 局部 (0,-222)，重锤肩炮 (94,50) → 局部 (±66,-154)，
    // 连弩机首炮 (116,10) → 局部 (±22,-234)。

    private void PlayTraitShowcase(MachineShowcaseKind kind)
    {
        switch (kind)
        {
            case MachineShowcaseKind.Speed:
                ShowSpeed();
                break;
            case MachineShowcaseKind.Impact:
                ShowImpact();
                break;
            case MachineShowcaseKind.Burst:
                ShowBurst();
                break;
            case MachineShowcaseKind.Shield:
                ShowShield();
                break;
            case MachineShowcaseKind.Armor:
                ShowArmor();
                break;
            default:
                ShowBaseline();
                break;
        }
    }

    /// <summary>标准型：一圈缓慢外扩的校准环——没有特性本身也是一种读数（基准点）。</summary>
    private void ShowBaseline()
    {
        var ring = CinematicFx.Shockwave(new Godot.Collections.Dictionary
        {
            ["radius"] = 210.0f,
            ["time"] = 1.1f,
            ["width"] = 3.0f,
            ["color"] = new Color(UITheme.HoloPale, 0.40f),
            ["core_color"] = new Color(UITheme.HoloPale, 0.65f),
        });
        _shipRest.AddChild(ring);
        BodyPulse(1.05f, 0.6f);
    }

    /// <summary>游隼：两次短促横移 + 速度线场 + 拖影——读作「快」（不是「有个光效」）。</summary>
    private void ShowSpeed()
    {
        var streaks = CinematicFx.RadialStreaks(new Godot.Collections.Dictionary
        {
            ["count"] = 16,
            ["max_radius"] = 380.0f,
            ["cycle"] = 0.45f,
            ["color"] = new Color(UITheme.HoloPale, 0.34f),
        });
        streaks.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        _shipRest.AddChild(streaks);
        var sf = CreateTween();
        sf.TweenProperty(streaks, "modulate:a", 1.0f, 0.1);
        sf.TweenProperty(streaks, "modulate:a", 0.0f, 0.55);
        sf.TweenCallback(Callable.From(streaks.QueueFree));

        SpawnTrail(3);

        var dart = CreateTween();
        dart.TweenProperty(_flyer, "position:x", -84.0f, 0.15).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        dart.Parallel().TweenProperty(_flyer, "rotation", -0.06f, 0.15).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        dart.TweenProperty(_flyer, "position:x", 84.0f, 0.22).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        dart.Parallel().TweenProperty(_flyer, "rotation", 0.06f, 0.22).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        dart.TweenProperty(_flyer, "position:x", 0.0f, 0.2).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        dart.Parallel().TweenProperty(_flyer, "rotation", 0.0f, 0.2).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _traitMotion = dart;
    }

    /// <summary>重锤：肩部双炮出膛 + 机体后坐 + 前方冲击环——读作「火力」。</summary>
    private void ShowImpact()
    {
        foreach (var x in new[] { -66.0f, 66.0f })
        {
            MuzzleFlash(new Vector2(x, -154.0f), 78.0f, 28);
        }

        var ring = CinematicFx.Shockwave(new Godot.Collections.Dictionary
        {
            ["radius"] = 180.0f,
            ["time"] = 0.45f,
            ["width"] = 7.0f,
            ["color"] = new Color(1.0f, 0.72f, 0.30f, 0.50f),
            ["core_color"] = new Color(1.0f, 0.95f, 0.80f, 0.85f),
            ["start_scale"] = 0.35f,
        });
        ring.Position = new Vector2(0.0f, -170.0f);
        _shipRest.AddChild(ring);

        var recoil = CreateTween();
        recoil.TweenProperty(_flyer, "position:y", 42.0f, 0.1).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        recoil.TweenProperty(_flyer, "position:y", 0.0f, 0.42).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _traitMotion = recoil;
    }

    /// <summary>连弩：机首双管三连点射（每发一束短曳光 + 一次枪口闪）——读作「射速」。</summary>
    private void ShowBurst()
    {
        for (var i = 0; i < 3; i++)
        {
            var delay = i * 0.15f;
            foreach (var x in new[] { -22.0f, 22.0f })
            {
                var muzzle = new Vector2(x, -234.0f);
                var flash = CinematicFx.SoftGlow(46.0f, new Color(1.0f, 0.88f, 0.55f, 0.9f));
                flash.Position = muzzle;
                flash.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
                _shipRest.AddChild(flash);
                var ft = CreateTween();
                ft.TweenInterval(delay);
                ft.TweenProperty(flash, "modulate:a", 1.0f, 0.04);
                ft.TweenProperty(flash, "modulate:a", 0.0f, 0.14);
                ft.TweenCallback(Callable.From(flash.QueueFree));

                var beam = CinematicFx.Beam(new[] { muzzle, muzzle + new Vector2(0.0f, -320.0f) }, new Godot.Collections.Dictionary
                {
                    ["color"] = new Color(1.0f, 0.78f, 0.36f, 0.55f),
                    ["width"] = 5.0f,
                    ["dot_speed"] = 2.4f,
                    ["dot_count"] = 3,
                    ["dot_radius"] = 4.0f,
                    ["dot_color"] = new Color(1.0f, 0.95f, 0.8f, 0.9f),
                });
                beam.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
                _shipRest.AddChild(beam);
                var bt = CreateTween();
                bt.TweenInterval(delay);
                bt.TweenProperty(beam, "modulate:a", 1.0f, 0.05);
                bt.TweenProperty(beam, "modulate:a", 0.0f, 0.2);
                bt.TweenCallback(Callable.From(beam.QueueFree));
            }
        }

        var shake = CreateTween();
        shake.TweenInterval(0.15);
        shake.TweenProperty(_flyer, "position:x", 5.0f, 0.05);
        shake.TweenProperty(_flyer, "position:x", -5.0f, 0.05);
        shake.TweenProperty(_flyer, "position:x", 0.0f, 0.12);
        _traitMotion = shake;
    }

    /// <summary>壁垒：护盾环张开 + 两发来袭弹被弹开——读作「防御」（挨打的那一侧演的是防住）。</summary>
    private void ShowShield()
    {
        const float ringR = 210.0f;
        var ring = CinematicFx.Line(CinematicFx.RingPoints(40, ringR), new Color(UITheme.HoloPale, 0.75f), 4.0f);
        ring.Material = CinematicFx.AdditiveMaterial();
        ring.Scale = Vector2.One * 0.55f;
        ring.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        _shipRest.AddChild(ring);
        var rt = CreateTween();
        rt.TweenProperty(ring, "modulate:a", 1.0f, 0.1);
        rt.Parallel().TweenProperty(ring, "scale", Vector2.One, 0.35).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        rt.TweenInterval(0.45);
        rt.TweenProperty(ring, "modulate:a", 0.0f, 0.25);
        rt.TweenCallback(Callable.From(ring.QueueFree));

        // 来袭弹：自两侧飞向机体，撞在护盾环上弹开并淡出（与护盾环同一半径，故读作「打在盾上」）
        foreach (var from in new[] { new Vector2(-1.0f, -0.45f), new Vector2(1.0f, 0.35f) })
        {
            var start = from.Normalized() * (ringR + 220.0f);
            var hit = from.Normalized() * ringR;
            var shot = CinematicFx.SoftGlow(26.0f, new Color(1.0f, 0.55f, 0.35f, 0.9f));
            shot.Position = start;
            _shipRest.AddChild(shot);
            var st = CreateTween();
            st.TweenProperty(shot, "position", hit, 0.34).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            st.TweenProperty(shot, "position", hit + from.Normalized() * 150.0f, 0.3).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            st.Parallel().TweenProperty(shot, "modulate:a", 0.0f, 0.3);
            st.TweenCallback(Callable.From(shot.QueueFree));
        }
    }

    /// <summary>巨像：装甲环带六段依次点亮（血条语汇）+ 机体提亮——读作「皮厚」。</summary>
    private void ShowArmor()
    {
        const float segR = 196.0f;
        const int segs = 6;
        for (var i = 0; i < segs; i++)
        {
            var a0 = -Mathf.Pi * 0.5f + Mathf.Tau * (i / (float)segs) * 0.92f - Mathf.Pi * 0.16f;
            var a1 = a0 + Mathf.Tau / segs * 0.62f;
            var seg = CinematicFx.Line(new[]
            {
                new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * segR,
                new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * segR,
            }, new Color(1.0f, 0.78f, 0.34f, 0.9f), 9.0f);
            seg.Material = CinematicFx.AdditiveMaterial();
            seg.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
            _shipRest.AddChild(seg);
            var t = CreateTween();
            t.TweenInterval(i * 0.07f);
            t.TweenProperty(seg, "modulate:a", 1.0f, 0.12);
            t.TweenInterval(0.5);
            t.TweenProperty(seg, "modulate:a", 0.0f, 0.3);
            t.TweenCallback(Callable.From(seg.QueueFree));
        }

        BodyPulse(1.1f, 0.7f);
    }

    /// <summary>枪口闪（重锤 / 连弩共用）：一发软点 + 一圈压扁冲击环 + 前向火花。</summary>
    private void MuzzleFlash(Vector2 muzzle, float radius, int sparks)
    {
        var flash = CinematicFx.SoftGlow(radius, new Color(1.0f, 0.86f, 0.55f, 0.95f));
        flash.Position = muzzle;
        _shipRest.AddChild(flash);
        var ft = CreateTween();
        ft.TweenProperty(flash, "scale", flash.Scale * 1.5f, 0.22).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        ft.Parallel().TweenProperty(flash, "modulate:a", 0.0f, 0.22);
        ft.TweenCallback(Callable.From(flash.QueueFree));

        var spray = CinematicFx.Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = sparks,
            ["lifetime"] = 0.26f,
            ["one_shot"] = true,
            ["explosiveness"] = 0.9f,
            ["direction"] = new Vector3(0.0f, -1.0f, 0.0f),
            ["spread"] = 22.0f,
            ["vel_min"] = 420.0f,
            ["vel_max"] = 760.0f,
            ["scale_min"] = 3.0f,
            ["scale_max"] = 7.0f,
            ["color"] = new Color(1.0f, 0.82f, 0.45f, 0.95f),
        });
        spray.Position = muzzle;
        _shipRest.AddChild(spray);
        var st = CreateTween();
        st.TweenInterval(0.5);
        st.TweenCallback(Callable.From(spray.QueueFree));
    }

    /// <summary>拖影（游隼横移用）：机体后方三枚残影随横移拉开并淡出，读作速度而不是「多了三架」。</summary>
    private void SpawnTrail(int count)
    {
        var tex = _body.Texture;
        for (var i = 0; i < count; i++)
        {
            var ghost = new Sprite2D
            {
                Texture = tex,
                Scale = Vector2.One * ShipScale,
                Position = new Vector2(38.0f * (i + 1), 0.0f),
                Modulate = new Color(0.70f, 0.92f, 1.0f, 0.20f - 0.05f * i),
                ZIndex = -1,
            };
            _shipRest.AddChild(ghost);
            var t = CreateTween();
            t.TweenProperty(ghost, "position:x", 150.0f + 46.0f * i, 0.5)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            t.Parallel().TweenProperty(ghost, "modulate:a", 0.0f, 0.5);
            t.TweenCallback(Callable.From(ghost.QueueFree));
        }
    }

    /// <summary>退场回标题屏（Esc / 手柄 B）。</summary>
    public void Close()
    {
        var fired = false;
        UITheme.AnimateModalClose(this, Dim, _plate, () =>
        {
            if (fired)
            {
                return;
            }

            fired = true;
            SetWheelActive(false);
            Closed?.Invoke();
            QueueFree();
        });
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 轮盘自己收方向键 / 摇杆 / 滚轮 / 回车（KeyboardEnabled 默认开），这里只接退出。
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }
}
