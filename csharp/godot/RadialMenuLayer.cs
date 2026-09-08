using Godot;

using InfiAir.Core;

namespace InfiAir;

/// <summary>
/// 左缘轮盘菜单页骨架（2026-09-08 圆盘 UI 全覆盖）：dim 遮罩 + RadialWheel（左缘 1/4 弧）
/// + 右侧内容区的统一开合编排，与 TalentPanel 同一视觉语言——全站菜单/条目目录的导航
/// 面统一收敛为圆盘。入场：dim 淡入 + 轮盘过冲滑入 + 开机物化（Wheel.PlayBoot：环带扫掠
/// 成形/卡片交错部署/全息闪烁）+ 屏幕四角 HUD 括弧错峰绘入；退场反序加速（内容层由子类
/// 自编排）。子类契约：_Ready 内先 BuildChrome() 再自建内容区；打开前 LoadMenu() 装配根级
/// 选项；轮盘叶子确认经 Wheel.Confirmed 订阅自处理。宿主页隐藏时轮盘/引线经 VisibilityChanged
/// 自动断供（Node2D._Input 不随 CanvasLayer 隐藏失效，漏同步会隐形吞命中区输入）。
/// 空间链路：子类经 SetContentAnchor() 注册右区内容面板后，聚焦引线把轮盘聚焦卡与
/// 内容区连成一条 HUD 引线（聚焦变化时脉冲提示）；锚点空间不足时守卫放弃该页引线。
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
    private static readonly Vector2[] MidTickPts = new Vector2[6]; // 三缘中点刻度端点（重绘零分配）

    protected RadialWheel Wheel = null!;

    private Node2D _wheelHolder = null!;
    private RadialWheelLayer _tether = null!;
    private RadialWheelLayer _frame = null!;
    private float _frameT; // 四角括弧绘制因子 0..1（错峰绘入/收起）
    private float _frameTarget;
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
        // 屏幕四角 HUD 括弧：整个弹出面板的「系统层」取景框，最上、纯几何零输入
        _frame = new RadialWheelLayer { Painter = DrawFrame };
        AddChild(_frame);

        Wheel.FocusChanged += OnFocusChangedForTether;
        Wheel.Drilled += _ => _tetherDirty = true;
        Wheel.Backed += () => _tetherDirty = true;
        VisibilityChanged += OnVisibilityChangedForWheel;
    }

    /// <summary>页面隐藏时强制断供轮盘/引线（防隐形轮盘继续吞命中区输入）；恢复由宿主页 Open 流程的 SetWheelActive 负责。</summary>
    private void OnVisibilityChangedForWheel()
    {
        if (!Visible)
        {
            Wheel.Visible = false;
            _tether.Visible = false;
        }
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
        _frame.MoveToFront(); // 取景括弧保持最上（轮盘环带是整圆，左下角会盖住括弧）
    }

    /// <summary>入场编排：dim 淡入 + 轮盘过冲滑入 + 开机物化 + 四角括弧错峰绘入 +
    /// 引线延迟淡入（等轮盘/内容就位后再建立空间关联）。</summary>
    protected void PlayWheelEntrance()
    {
        Dim.Modulate = new Color(Dim.Modulate, 0f);
        _tether.Modulate = new Color(1f, 1f, 1f, 0f);
        _wheelHolder.Position = new Vector2(WheelRest.X - 620f, WheelRest.Y);
        Wheel.PlayBoot();
        _frameT = 0f;
        _frameTarget = 1f;
        var tw = CreateTween();
        tw.Parallel().TweenProperty(Dim, "modulate:a", 1.0f, 0.2);
        tw.Parallel().TweenProperty(_wheelHolder, "position", WheelRest, 0.5)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(_tether, "modulate:a", 1.0f, 0.3).SetDelay(0.42);
    }

    /// <summary>退场编排：轮盘加速滑出 + dim 收尾 + 引线与括弧先行淡出/收起，完成后回调
    /// （回调里再置 Visible=false）。</summary>
    protected void PlayWheelExit(Action finished, float total = 0.36f)
    {
        _frameTarget = 0f;
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
        _frameTarget = active ? 1f : 0f;
        _tetherDirty = true;
    }

    public override void _Process(double delta)
    {
        // 四角括弧绘制因子：开合渐次绘入/收起，仅在变化帧重绘
        if (_frameT != _frameTarget)
        {
            _frameT = Mathf.MoveToward(_frameT, _frameTarget, (float)delta * (_frameTarget > _frameT ? 2.6f : 3.8f));
            _frame.Repaint();
        }

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

    /// <summary>屏幕四角 HUD 括弧 + 四缘中点刻度：把整屏取景成一块「系统面板」。
    /// 四角按 0.12s 步进错峰绘入（臂长随局部因子生长），缘中刻度最后收尾；纯几何零分配。</summary>
    private void DrawFrame(RadialWheelLayer c)
    {
        if (_frameT <= 0.001f)
        {
            return;
        }

        var vp = GetViewport().GetVisibleRect().Size;
        const float margin = 24f;
        const float leg = 64f;
        for (var k = 0; k < 4; k++)
        {
            var s = Mathf.Clamp((_frameT - k * 0.12f) * 2.2f, 0f, 1f);
            if (s <= 0f)
            {
                continue;
            }

            var e = (float)RadialWheelModel.EaseOutCubic(s);
            var cx = k % 2 == 0 ? margin : vp.X - margin;
            var cy = k < 2 ? margin : vp.Y - margin;
            var sx = k % 2 == 0 ? 1f : -1f;
            var sy = k < 2 ? 1f : -1f;
            // 暗托底 + 亮线双描：括弧要压在宿主页/HUD 面板之上仍可辨
            var under = new Color(0f, 0f, 0f, 0.55f * e);
            var col = new Color(UITheme.Accent, 0.6f * e);
            var x2 = cx + (sx * leg * e);
            var y2 = cy + (sy * leg * e);
            c.DrawLine(new Vector2(cx, cy), new Vector2(x2, cy), under, 5.5f, true);
            c.DrawLine(new Vector2(cx, cy), new Vector2(cx, y2), under, 5.5f, true);
            c.DrawLine(new Vector2(cx, cy), new Vector2(x2, cy), col, 2.5f, true);
            c.DrawLine(new Vector2(cx, cy), new Vector2(cx, y2), col, 2.5f, true);
            // 臂外延伸刻度：括弧到位后浮现的短续线
            var tA = Mathf.Clamp((e - 0.85f) / 0.15f, 0f, 1f);
            if (tA > 0f)
            {
                var lx = cx + (sx * (leg + 7f));
                var ly = cy + (sy * (leg + 7f));
                var eCol = new Color(UITheme.Accent, 0.28f * tA);
                c.DrawLine(new Vector2(lx, cy), new Vector2(lx + (sx * 9f * tA), cy), eCol, 1.5f, true);
                c.DrawLine(new Vector2(cx, ly), new Vector2(cx, ly + (sy * 9f * tA)), eCol, 1.5f, true);
            }
        }

        // 三缘中点刻度（最后收尾）：上/下竖刻、右横刻。左缘不设——轮盘整圆在本骨架
        // 所有页面上都叠压左缘中点，刻度放那里永不可辨
        var sE = Mathf.Clamp((_frameT - 0.55f) * 2.3f, 0f, 1f);
        if (sE <= 0f)
        {
            return;
        }

        var eE = (float)RadialWheelModel.EaseOutCubic(sE);
        var midU = new Color(0f, 0f, 0f, 0.45f * eE);
        var colE = new Color(UITheme.Accent, 0.45f * eE);
        var len = 12f * eE;
        MidTickPts[0] = new Vector2(vp.X * 0.5f, margin);
        MidTickPts[1] = new Vector2(vp.X * 0.5f, margin + len);
        MidTickPts[2] = new Vector2(vp.X * 0.5f, vp.Y - margin);
        MidTickPts[3] = new Vector2(vp.X * 0.5f, vp.Y - margin - len);
        MidTickPts[4] = new Vector2(vp.X - margin, vp.Y * 0.5f);
        MidTickPts[5] = new Vector2(vp.X - margin - len, vp.Y * 0.5f);
        for (var m = 0; m < 6; m += 2)
        {
            c.DrawLine(MidTickPts[m], MidTickPts[m + 1], midU, 4.5f, true);
            c.DrawLine(MidTickPts[m], MidTickPts[m + 1], colE, 2f, true);
        }
    }
}
