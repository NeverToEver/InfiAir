using Godot;

using InfiAir.Core;

namespace InfiAir;

/// <summary>
/// 左缘轮盘菜单页骨架（2026-09-08 圆盘 UI 全覆盖）：dim 遮罩 + RadialWheel（左缘 1/4 弧）
/// + 右侧内容区的统一开合编排，与 TalentPanel 同一视觉语言——全站菜单/条目目录的导航
/// 面统一收敛为圆盘。入场：dim 淡入 + 轮盘过冲滑入；退场反序加速（内容层由子类自编排）。
/// 子类契约：_Ready 内先 BuildChrome() 再自建内容区；打开前 LoadMenu() 装配根级选项；
/// 轮盘叶子确认经 Wheel.Confirmed 订阅自处理。宿主页 Visible=false 期间必须同步
/// Wheel.Visible=false（Node2D._Input 只看自身可见性，不随 CanvasLayer 隐藏失效）。
/// 空间链路（2026-09-08）：子类经 SetContentAnchor() 注册右区内容面板后，聚焦引线把
/// 轮盘聚焦卡与内容区连成一条 HUD 引线（聚焦变化时脉冲提示），轮盘收缩/回弹期间逐帧跟随。
/// </summary>
public abstract partial class RadialMenuLayer : CanvasLayer
{
    /// <summary>轮盘圆心静止位（1080p 设计坐标；UI 不走 world_scale）。
    /// y=480 与端点角钳 36°（RadialWheel.SlotAngleFor）配合：卡片旋转包络底缘 ≈927，
    /// 不压左下生命/状态 HUD 区（顶缘 ≈940）。</summary>
    protected static readonly Vector2 WheelRest = new(-160f, 480f);

    /// <summary>引线菱形端点/线宽的静态缓冲（重绘零分配）。</summary>
    private static readonly Vector2[] TipDiamond = { new(6f, 0f), new(0f, 5f), new(-6f, 0f), new(0f, -5f) };
    private static readonly Color[] TipDiamondCols = new Color[4];

    protected RadialWheel Wheel = null!;

    private Node2D _wheelHolder = null!;
    private RadialWheelLayer _tether = null!;
    private bool _tetherDirty = true;
    private Tween? _tetherPulse;
    private Vector2 _lastWheelG;
    private Vector2 _lastHolderP;

    /// <summary>右区内容锚点：返回当前应被引线指向的面板（可随页内切换变化）；null 或不可见 = 不画引线。
    /// 子类在 BuildChrome 后设置（TalentPanel 自管骨架不经此处）。</summary>
    protected Func<Control?>? ContentAnchor { get; set; }

    protected ColorRect Dim = null!;

    /// <summary>构建骨架（dim + 轮盘 holder + 聚焦引线）。子类 _Ready 先调本方法再挂内容区
    /// （绘制序：dim → 轮盘 → 引线 → 内容；RaiseWheel 的宿主页引线随轮盘一并提前）。</summary>
    protected void BuildChrome()
    {
        Dim = new ColorRect { Color = UITheme.DimBg };
        Dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(Dim);

        // holder 承载入场/退场位移：轮盘自身 _Process 视差会写自己的 Position，两者解耦（TalentPanel 先例）
        _wheelHolder = new Node2D { Position = WheelRest };
        Wheel = new RadialWheel();
        _wheelHolder.AddChild(Wheel);
        AddChild(_wheelHolder);
        // 引线挂本层（画布坐标直绘），不随 holder 入场滑动——否则入场途中换算的锚点端
        // 会被冻结在已画几何里、跟着 holder 漂移离面板；端点跟随由 _Process 运动门控重绘保证
        _tether = new RadialWheelLayer { Painter = DrawTether };
        AddChild(_tether);

        Wheel.FocusChanged += OnFocusChangedForTether;
        Wheel.Drilled += _ => _tetherDirty = true;
        Wheel.Backed += () => _tetherDirty = true;
    }

    /// <summary>注册右区内容锚点（引线指向面板左缘中点）。</summary>
    protected void SetContentAnchor(Func<Control?>? anchor) => ContentAnchor = anchor;

    /// <summary>装载根级选项（每次打开重装即可复位导航态）。槽距按选项数自适应：
    /// 端点卡 |弧角| 钳在 ≈42° 内（更大的卡片会出屏/被可视半幅剔除）。</summary>
    protected void LoadMenu(IReadOnlyList<RadialWheelOption> roots, string backLabel)
    {
        Wheel.BackLabel = backLabel;
        Wheel.Load(roots, RadialWheel.SlotAngleFor(roots.Count));
        _tetherDirty = true;
    }

    /// <summary>把轮盘提到本层最上（宿主页在 chrome 之后还挂了带遮罩的 shell 时必须调用，
    /// 否则 shell 遮罩盖住轮盘卡片——结算/设置页实测）。</summary>
    protected void RaiseWheel()
    {
        _wheelHolder.MoveToFront();
        _tether.MoveToFront(); // 引线保持紧贴轮盘之上（否则被后入树的背景/遮罩盖住）
    }

    /// <summary>入场编排：dim 淡入 + 轮盘过冲滑入 + 引线延迟淡入（等轮盘/内容就位后再建立空间关联）。</summary>
    protected void PlayWheelEntrance()
    {
        Dim.Modulate = new Color(Dim.Modulate, 0f);
        _tether.Modulate = new Color(1f, 1f, 1f, 0f);
        _wheelHolder.Position = new Vector2(WheelRest.X - 620f, WheelRest.Y);
        var tw = CreateTween();
        tw.Parallel().TweenProperty(Dim, "modulate:a", 1.0f, 0.2);
        tw.Parallel().TweenProperty(_wheelHolder, "position", WheelRest, 0.5)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(_tether, "modulate:a", 1.0f, 0.3).SetDelay(0.42);
    }

    /// <summary>退场编排：轮盘加速滑出 + dim 收尾 + 引线先行淡出，完成后回调（回调里再置 Visible=false）。</summary>
    protected void PlayWheelExit(Action finished, float total = 0.36f)
    {
        var tw = CreateTween();
        tw.Parallel().TweenProperty(_tether, "modulate:a", 0.0f, 0.14);
        tw.TweenProperty(_wheelHolder, "position", new Vector2(WheelRest.X - 620f, WheelRest.Y), 0.26)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In).SetDelay(0.04);
        tw.Parallel().TweenProperty(Dim, "modulate:a", 0.0f, 0.24).SetDelay(0.1);
        tw.TweenInterval(Mathf.Max(total - 0.3f, 0.02f));
        tw.TweenCallback(Callable.From(finished));
    }

    /// <summary>轮盘可见性同步（宿主开/关时调用；Node2D._Input 不随 CanvasLayer 隐藏失效）。
    /// dimActive：chrome 遮罩的显隐（默认跟随轮盘）。非模态宿主页（welcome 自带不透明底、
    /// settings 自带 page shell 遮罩）必须显式传 false——chrome Dim 是最上层全屏 84% 黑罩，
    /// 常开会把整页压暗（welcome 实测整页发暗）。</summary>
    protected void SetWheelActive(bool active, bool? dimActive = null)
    {
        Wheel.Visible = active;
        _tether.Visible = active;
        Dim.Visible = dimActive ?? active;
        _tetherDirty = true;
    }

    public override void _Process(double delta)
    {
        // 引线重绘时机：收缩/回弹或入场/退场/视差期间（轮盘或 holder 在动）逐帧跟随，
        // 其余仅在聚焦/层级变化后补一次；静止时两次比较零成本、零重绘
        if (_tether == null || !Wheel.Visible)
        {
            return;
        }

        var wheelG = Wheel.GlobalPosition;
        var holderP = _wheelHolder.Position;
        if (Wheel.IsBusy || _tetherDirty
            || !wheelG.IsEqualApprox(_lastWheelG) || holderP != _lastHolderP)
        {
            _lastWheelG = wheelG;
            _lastHolderP = holderP;
            _tetherDirty = false;
            _tether.Repaint();
        }
    }

    private void OnFocusChangedForTether(int _)
    {
        _tetherDirty = true;
        // 聚焦变化脉冲：引线短暂提亮再回落（同节点互斥 tween，防连打叠加）
        if (_tetherPulse != null && _tetherPulse.IsValid())
        {
            _tetherPulse.Kill();
        }

        _tetherPulse = CreateTween();
        _tetherPulse.TweenProperty(_tether, "modulate:a", 0.55f, 0.05);
        _tetherPulse.TweenProperty(_tether, "modulate:a", 1.0f, 0.3);
    }

    private void DrawTether(RadialWheelLayer c)
    {
        var anchorCtl = ContentAnchor?.Invoke();
        if (anchorCtl == null || !anchorCtl.IsVisibleInTree() || !Wheel.Visible)
        {
            return;
        }

        var tip = Wheel.FocusedTipLocal();
        if (tip == null)
        {
            return;
        }

        var tipG = Wheel.ToGlobal(tip.Value);
        var anchorRect = anchorCtl.GetGlobalRect();
        var anchorG = new Vector2(anchorRect.Position.X, anchorRect.Position.Y + (anchorRect.Size.Y * 0.5f));

        // 面板在尖端左侧（无水平空间走线）时放弃引线
        if (anchorG.X < tipG.X + 40f)
        {
            return;
        }

        var centerG = Wheel.ToGlobal(Vector2.Zero);
        var radial = tipG.DistanceTo(centerG) > 1f ? (tipG - centerG).Normalized() : Vector2.Right;
        var stub = tipG + (radial * 14f);

        // 正交三段走线（stub → 水平 → 垂直 → 水平入锚）：全程走在面板外侧，不切边框
        var gutterX = anchorG.X - 34f;
        c.DrawLine(tipG, stub, new Color(UITheme.Accent, 0.55f), 2f, true);
        c.DrawCircle(stub, 2.5f, new Color(UITheme.Accent, 0.7f));
        var line = new Color(UITheme.Accent, 0.28f);
        c.DrawLine(stub, new Vector2(gutterX, stub.Y), line, 1.5f, true);
        c.DrawLine(new Vector2(gutterX, stub.Y), new Vector2(gutterX, anchorG.Y), line, 1.5f, true);
        c.DrawLine(new Vector2(gutterX, anchorG.Y), anchorG, line, 1.5f, true);

        // 端点菱形（面板左缘中点）
        c.DrawSetTransform(anchorG, 0f, Vector2.One);
        for (var i = 0; i < TipDiamondCols.Length; i++)
        {
            TipDiamondCols[i] = new Color(UITheme.Accent, 0.8f);
        }

        c.DrawPolygon(TipDiamond, TipDiamondCols);
        c.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
