using Godot;

using InfiAir.Core;

namespace InfiAir;

/// <summary>
/// 左缘交互轮盘：圆心锚在屏幕外（调用方将节点 Position 设为轮盘圆心，如 (-160, 540)），
/// 仅右侧 ~1/4 弧面伸入画面，越界部分由视口剪裁。同心圆环承载层级：最外环始终显示当前层
/// 选项，确认后整轮向圆心收缩（bounce ease-out），父层缩为内环面包屑、外环刷新为子层——
/// 「纵深下钻」。弧面溢出时滚轮/拖拽旋转、松手吸附最近槽，弧端渐隐提示更多内容。
/// 悬停卡片沿径向浮起 5px + 亮边；确认触发圆心脉冲波纹 + 环带闪光。视差跟手以 2D 近似：
/// Skew（绕 X 倾斜 ±3°）+ 非等比缩放（绕 Y ±3°）+ 圆心平移。
/// 输入经 _Input（先于 GUI 相位）：只消费落在轮盘命中区内的事件，区外原样放行。
/// 动画全部 _Process 手动积分（bounce/ease 缓动取自 RadialWheelModel，纯函数可测），
/// 绘制顶点/颜色缓冲静态缓存——热路径零托管分配。
/// 绘制拆三个子层（RadialWheelLayer）按重绘频率独立重铺：钢构（面包屑/环带/轮毂，
/// 仅层深或收缩缩放变化）/ 卡片（悬停/滚动/吸附）/ 特效（波纹/闪光）；
/// 空闲（无动画、鼠标未动、无按压）整体零重绘零变换写入。
/// Node2D 而非 Control：全自绘 + 手动命中，无需 GUI 树语义，且 Skew/ToLocal 在此可用。
/// </summary>
public partial class RadialWheel : Node2D
{
    /// <summary>叶子项确认（无子层可下钻时触发）。</summary>
    public event Action<RadialWheelOption>? Confirmed;

    /// <summary>下钻收缩动画完成、子层已入栈（携带被下钻的父层选项，供面板联动切换右区）。</summary>
    public event Action<RadialWheelOption>? Drilled;

    /// <summary>回退到父层（模型即刻回退，动画随后弹回）。</summary>
    public event Action? Backed;

    /// <summary>弧面中点聚焦项变化（滚轮/键盘步进、拖拽、改层后）；参数为新聚焦索引，供面板联动。
    /// 回调在轮盘 _Process 内同步触发：只读轮盘状态/刷新页面控件，不得回写轮盘导航态。</summary>
    public event Action<int>? FocusChanged;

    // ---------------- 布局常量（1080p 设计值；UI 不走 world_scale） ----------------
    private const float Radius = 640f; // 主动环半径
    private const float BandW = 120f; // 环带宽
    private const float HubR = 150f; // 中心帽半径
    private const float RingQ = 0.72f; // 下钻内收比例（外圈 → 内圈）
    private const float CardW = 240f;
    private const float CardH = 64f;
    private const float CardChamfer = 11f;
    private const float MaxTilt = 0.052f; // 视差倾斜上限（≈±3°）
    private const float ShrinkDur = 0.5f;
    private const float PopDur = 0.18f;
    private const float RippleDur = 0.55f;
    private const float FlashDur = 0.28f;
    private const float SnapDur = 0.14f;
    private const float DragAngleThreshold = 2.5f; // 区分点击/拖拽的累计角阈值（度）
    private const float WedgeSpan = 1.0f; // 确认扫掠扇形的张角（弧度）
    private const float CardTiltFactor = 0.30f; // 卡片随弧角倾斜比例（保可读性的部分切向倾斜，钳 ±20°）
    private const float AliveGrace = 2.5f; // 活性余温（秒）：最后一次交互后呼吸/旋转等活体动效的存续时长
    private const int BandSegs = 64; // 环带渐变分段

    /// <summary>返回芯片文字（深度 > 1 时显示；由调用方传 Tr 后文案，空 = 只画双箭头）。</summary>
    public string BackLabel { get; set; } = string.Empty;

    /// <summary>默认槽距（度）。</summary>
    public const double DefaultSlotAngle = 27.0;

    /// <summary>选项数 → 槽距：端点卡弧角钳在 ±36° 内（1080p 下圆心 y=480 时，
    /// 卡片旋转包络底缘 ≈927 < 左下 HUD 面板顶缘 ≈940；更大的端点角会与 HUD 交叠）。</summary>
    public static double SlotAngleFor(int count) => count <= 1 ? DefaultSlotAngle : Math.Min(DefaultSlotAngle, 72.0 / (count - 1));

    /// <summary>键盘/手柄接管开关（方向键旋转 + Enter 确认聚焦项）。轮盘是全站统一菜单导航，
    /// 纯轮盘页保持开启；GUI 焦点落在页面内按钮时方向键/Enter 在 GUI 相位已被消费，
    /// 自然回落为焦点链导航，无需调用方切换。</summary>
    public bool KeyboardEnabled { get; set; } = true;

    private RadialWheelModel? _model;
    private FontFile? _font;

    // 绘制子层（重绘频率分层：钢构 / 卡片 / 特效）
    private RadialWheelLayer _chrome = null!;
    private RadialWheelLayer _cards = null!;
    private RadialWheelLayer _fx = null!;

    // 动画状态（T = -1 表示未激活）
    private float _contentScale = 1f;
    private float _shrinkT = -1f;
    private bool _shrinkIsDrill; // true = 收缩（下钻），false = 回弹（返回）
    private int _pendingDrill = -1;
    private RadialWheelOption? _pendingDrillOption;
    private float _popT = -1f;
    private float _rippleT = -1f;
    private float _flashT = -1f;
    private float _snapFrom;
    private float _snapTo;
    private float _snapT = -1f;

    // 指针状态
    private int _hoverIdx = -1;
    private int _externalHighlight = -1; // 外部联动高亮（右侧平铺区悬停 → 轮盘对应卡亮起）
    private bool _pressed;
    private bool _dragging;
    private bool _pressInHub;
    private bool _hubHot; // 指针悬在轮毂/返回芯片域（深度 > 1 时亮起返回芯片）
    private float _lastDragAngle;
    private float _dragAccum;
    private Vector2 _basePos;
    private Vector2 _lastLocal; // 空闲快路径：本地鼠标位去重（漂移 > 0.5px 才扫描）
    private bool _rescan; // 动画结束/外部改模型后强制重扫悬停（鼠标未动也要命中新内容）

    private float _tiltX;
    private float _tiltY;

    // 活体动效状态：呼吸/活性环只在「活性态」（最近 AliveGrace 内有指针移动/点击/键盘交互）演进，
    // 空闲（输入静默超 AliveGrace）冻结回中性值且不重铺——菜单常开但零持续开销
    private float[] _h = Array.Empty<float>(); // 卡片悬停/聚焦过渡因子（0..1，逐卡平滑）
    private float _breath = 0.5f; // 聚焦呼吸相位（0..1）
    private float _time;
    private float _idleT = 100f; // 距上次交互秒数（初值视为已超时；指针移动也算交互）
    private bool _engaged;
    private int _lastFocusIdx = -1;
    private float _spinA; // 轮毂活性环透明度（随活性态淡入淡出）

    // 静态几何/颜色缓冲（DrawPolygon/DrawPolyline 零分配）
    private static readonly Vector2[] CardPts = ChamferRect(CardW, CardH, CardChamfer);
    private static readonly Vector2[] CardLoop = ClosedLoop(CardPts);
    private static readonly Vector2[] SocketPts = ChamferRect(40f, 40f, 8f);
    private static readonly Vector2[] SocketLoop = ClosedLoop(SocketPts);
    private static readonly Color[] CardFill = new Color[CardPts.Length];
    private static readonly Color[] SocketFill = new Color[SocketPts.Length];
    private static readonly Color[] Poly4 = new Color[4];
    private static readonly Color[] Poly3 = new Color[3];
    private static readonly Color[] Poly6 = new Color[6];
    private static readonly Vector2[] GlyphDiamond = { new(0f, -1f), new(1f, 0f), new(0f, 1f), new(-1f, 0f) };
    private static readonly Vector2[] GlyphTriangle = { new(0f, -1f), new(0.9f, 0.7f), new(-0.9f, 0.7f) };
    private static readonly Vector2[] GlyphBolt =
    {
        new(-0.30f, -1f), new(0.60f, -0.20f), new(0.05f, -0.10f),
        new(0.40f, 1f), new(-0.55f, 0.10f), new(0f, -0.05f),
    };
    private static readonly Vector2[] RailPts =
    {
        new(-CardW * 0.5f, -CardH * 0.30f), new(-CardW * 0.5f + 3.5f, -CardH * 0.30f),
        new(-CardW * 0.5f + 3.5f, CardH * 0.30f), new(-CardW * 0.5f, CardH * 0.30f),
    };
    private static readonly Vector2[] MarkPts = new Vector2[3];
    private static readonly Vector2[] WedgePts = new Vector2[12]; // 中心 + 11 弧点
    private static readonly Color[] RailCols = new Color[4];
    private static readonly Color[] MarkCols = new Color[3];
    private static readonly Color[] WedgeCols = new Color[WedgePts.Length];
    private static readonly Vector2[][] BandQuad = new Vector2[BandSegs][];
    private static readonly Color[][] BandQuadCols = new Color[BandSegs][];

    // 卡片/环带基色（rgb；alpha 绘制时按透明度因子重写）
    private static readonly Color CardFillIdle = new(0.058f, 0.090f, 0.135f);
    private static readonly Color CardFillHot = new(0.088f, 0.128f, 0.188f);
    private static readonly Color CardFillFocus = new(0.105f, 0.152f, 0.220f);
    private static readonly Color BandColInn = new(0.028f, 0.045f, 0.072f);
    private static readonly Color BandColOut = new(0.058f, 0.090f, 0.138f);

    static RadialWheel()
    {
        for (var i = 0; i < BandSegs; i++)
        {
            BandQuad[i] = new Vector2[4];
            BandQuadCols[i] = new Color[4];
        }
    }

    private static Vector2[] ChamferRect(float w, float h, float c)
    {
        var hw = w * 0.5f;
        var hh = h * 0.5f;
        return new Vector2[]
        {
            new(-hw + c, -hh), new(hw - c, -hh), new(hw, -hh + c), new(hw, hh - c),
            new(hw - c, hh), new(-hw + c, hh), new(-hw, hh - c), new(-hw, -hh + c),
        };
    }

    private static Vector2[] ClosedLoop(Vector2[] pts)
    {
        var loop = new Vector2[pts.Length + 1];
        for (var i = 0; i < pts.Length; i++)
        {
            loop[i] = pts[i];
        }

        loop[^1] = pts[0];
        return loop;
    }

    private static Color[] Fill(Color[] buf, Color c, int n)
    {
        for (var i = 0; i < n; i++)
        {
            buf[i] = c;
        }

        return buf;
    }

    public override void _Ready()
    {
        _font = UITheme.Font;
        _basePos = Position;
        _chrome = new RadialWheelLayer { Painter = DrawChrome };
        _cards = new RadialWheelLayer { Painter = DrawCards };
        _fx = new RadialWheelLayer { Painter = DrawFx };
        AddChild(_chrome);
        AddChild(_cards);
        AddChild(_fx);
    }

    /// <summary>装载根级选项（重开轮盘时重复调用即可重置全部状态）。
    /// slotAngle 覆盖默认槽距：选项多的菜单页（5-7 项）收紧间距，避免弧面两端卡片
    /// 超出可视半幅被剔除/溢出屏幕下缘（1080p 下 |弧角| ≳ 50° 的卡片出屏）。</summary>
    public void Load(IReadOnlyList<RadialWheelOption> roots, double slotAngle = DefaultSlotAngle)
    {
        _model = new RadialWheelModel(roots) { SlotAngle = slotAngle };
        _contentScale = 1f;
        _shrinkT = _popT = _rippleT = _flashT = _snapT = -1f;
        _hoverIdx = -1;
        _externalHighlight = -1;
        _pressed = _dragging = false;
        _rescan = true;
        Array.Clear(_h);
        _idleT = 0f;
        _lastFocusIdx = _model.FocusedIndex; // 不发合成 FocusChanged（开页联动由 _rescan 与页面自身刷新负责）
        RepaintAll();
    }

    // ---------------- A7：测试/诊断公开接口 ----------------

    public int TestDepth => _model?.Depth ?? 0;

    // ---------------- 面板联动 API（层级/选项读取 + 外部高亮） ----------------

    /// <summary>当前层深（1 = 根层）。</summary>
    public int Depth => _model?.Depth ?? 0;

    /// <summary>当前层选项数。</summary>
    public int CurrentCount => _model?.OptionCount ?? 0;

    /// <summary>当前层第 i 项（越界返回 null）。</summary>
    public RadialWheelOption? CurrentOption(int i) =>
        _model != null && i >= 0 && i < _model.Current.Count ? _model.Current[i] : null;

    /// <summary>弧面中点聚焦项（用于面板联动选中态）。</summary>
    public RadialWheelOption? FocusedOption => _model?.FocusedOption;

    /// <summary>外部联动高亮（右侧平铺区悬停 → 对应卡亮起；-1 清除）。</summary>
    public void HighlightOption(int index)
    {
        if (_externalHighlight != index)
        {
            _externalHighlight = index;
            _cards?.Repaint();
        }
    }

    public void TestScrollBy(double slots)
    {
        if (_model == null)
        {
            return;
        }

        _model.ScrollBy(slots);
        _rescan = true;
        _cards?.Repaint();
    }

    public bool TestDrill(int index)
    {
        if (_model == null || !_model.CanDrill(index))
        {
            return false;
        }

        StartConfirmFx();
        _pendingDrill = index;
        _pendingDrillOption = _model.Current[index];
        _shrinkIsDrill = true;
        _shrinkT = 0f;
        return true;
    }

    public void TestBack() => DoBack();

    /// <summary>收缩/回弹动画进行中（忙态）。外部联动绘制（如内容引线）可据此暂停跟随。</summary>
    public bool IsBusy => _shrinkT >= 0f;

    /// <summary>聚焦卡径向外缘尖端（含浮起/缩放/倾斜）的轮盘本地坐标；无可见聚焦卡返回 null。供引线类联动锚定。</summary>
    public Vector2? FocusedTipLocal()
    {
        if (_model == null)
        {
            return null;
        }

        var i = _model.FocusedIndex;
        if (i < 0 || i >= _model.OptionCount)
        {
            return null;
        }

        var a = (float)_model.AngleOf(i);
        if (Mathf.Abs(a) > (float)_model.HalfSpan || (float)_model.AlphaAt(a) <= 0.05f)
        {
            return null;
        }

        // 聚焦卡口径（同 DrawCard）：h 恒满 + pop 缩放；纯查询不改绘制状态
        var pop = _popT >= 0f ? Mathf.Lerp(1.12f, 1f, (float)RadialWheelModel.EaseOutCubic(_popT)) : 1f;
        var aRad = Mathf.DegToRad(a);
        var cardScale = 1.09f * pop;
        var pos = new Vector2(Mathf.Cos(aRad), Mathf.Sin(aRad)) * (Radius * _contentScale);
        var rot = Mathf.Clamp(a * CardTiltFactor, -20f, 20f) * Mathf.DegToRad(1f);
        return pos + (new Vector2((CardW * 0.5f + 6f) * cardScale, 0f)).Rotated(rot);
    }

    // ---------------- 帧驱动：动画积分 / 视差 / 悬停 / 拖拽 ----------------

    public override void _Process(double delta)
    {
        if (!Visible || _model == null)
        {
            return;
        }

        var d = (float)delta;
        _time += d;
        _idleT += d;
        var anim = false;

        if (_shrinkT >= 0f)
        {
            _shrinkT += d / ShrinkDur;
            if (_shrinkT >= 1f)
            {
                FinishShrink(_model);
                _rescan = true;
            }
            else
            {
                var p = (float)RadialWheelModel.BounceOut(_shrinkT);
                _contentScale = _shrinkIsDrill ? 1f - (1f - RingQ) * p : RingQ + (1f - RingQ) * p;
            }

            _chrome.Repaint();
            _cards.Repaint();
            anim = true;
        }

        if (_popT >= 0f)
        {
            _popT += d / PopDur;
            if (_popT >= 1f)
            {
                _popT = -1f;
                _rescan = true;
            }

            _cards.Repaint();
            anim = true;
        }

        if (_rippleT >= 0f)
        {
            _rippleT += d / RippleDur;
            if (_rippleT >= 1f)
            {
                _rippleT = -1f;
            }

            _fx.Repaint();
            anim = true;
        }

        if (_flashT >= 0f)
        {
            _flashT += d / FlashDur;
            if (_flashT >= 1f)
            {
                _flashT = -1f;
            }

            _fx.Repaint();
            anim = true;
        }

        if (_snapT >= 0f)
        {
            _snapT += d / SnapDur;
            var t = (float)RadialWheelModel.EaseOutCubic(Math.Min(_snapT, 1f));
            _model.ScrollTo(Mathf.Lerp(_snapFrom, _snapTo, t));
            if (_snapT >= 1f)
            {
                _snapT = -1f;
                _rescan = true;
            }

            _cards.Repaint();
            anim = true;
        }

        // 活性态判定与退出定格（呼吸值回中性 + 补一次重铺收尾）；活性环透明度衰减放在
        // 扫描 gate 之外——否则脱离活性后的快路径拦截帧让渐隐永不发生，留下冻结的幽灵弧
        var engaged = _idleT < AliveGrace;
        if (engaged != _engaged)
        {
            _engaged = engaged;
            if (!engaged)
            {
                _breath = 0.5f;
                _cards.Repaint();
                _fx.Repaint();
            }
        }

        var prevSpin = _spinA;
        _spinA = Mathf.MoveToward(_spinA, _engaged ? 1f : 0f, d * 4f);
        if (_spinA != prevSpin)
        {
            _fx.Repaint();
        }

        // 空闲快路径：无动画、无活性、无按压且鼠标未动时不做视差积分/悬停扫描（零变换写入零重绘）；
        // _rescan 兜底「内容变了但鼠标没动」：吸附/收缩/装载/聚焦步进完成后强制重扫一次
        var local = ToLocal(GetGlobalMousePosition());
        var moved = local.DistanceSquaredTo(_lastLocal) > 0.25f;
        if (!(_engaged || anim || _rescan || _pressed || moved))
        {
            return;
        }

        _rescan = false;
        _lastLocal = local;
        if (moved)
        {
            _idleT = 0f; // 指针移动即交互（活性续期），静止驻留域内同样转入空闲
        }

        // 悬停视差（2D 近似 3D 倾斜 ±3°）：Skew = 绕 X，非等比缩放 = 绕 Y，圆心随鼠标平移；
        // 变化低于阈值不写（每帧写 Skew/Scale/Position 会无谓脏化 CanvasItem 变换）
        var tx = Mathf.Clamp(local.X / Radius, -1f, 1f) * MaxTilt;
        var ty = Mathf.Clamp(local.Y / Radius, -1f, 1f) * MaxTilt;
        var k = 1f - Mathf.Exp(-10f * d);
        _tiltX = Mathf.Lerp(_tiltX, tx, k);
        _tiltY = Mathf.Lerp(_tiltY, ty, k);
        var targetPos = _basePos + new Vector2(_tiltX, _tiltY) * 140f;
        if (Mathf.Abs(_tiltY - Skew) > 0.0004f
            || Mathf.Abs((1f - Mathf.Abs(_tiltX) * 0.5f) - Scale.X) > 0.0004f
            || targetPos.DistanceSquaredTo(Position) > 0.01f)
        {
            Skew = _tiltY;
            Scale = new Vector2(1f - Mathf.Abs(_tiltX) * 0.5f, 1f - Mathf.Abs(_tiltY) * 0.5f);
            Position = targetPos;
        }

        var a = Mathf.RadToDeg(Mathf.Atan2(local.Y, local.X));

        // 点击/拖拽判定：按下后累计角漂移超阈值即进入拖拽
        if (_pressed && !_dragging)
        {
            _dragAccum += Mathf.Wrap(a - _lastDragAngle, -180f, 180f);
            _lastDragAngle = a;
            if (Mathf.Abs(_dragAccum) > DragAngleThreshold)
            {
                _dragging = true;
                _hoverIdx = -1;
                _cards.Repaint();
            }
        }

        // 拖拽旋转：角度差驱动皮带（光标向下转 = 皮带向下），越界由 EffectiveScroll 钳制
        if (_dragging)
        {
            var dTheta = Mathf.Wrap(a - _lastDragAngle, -180f, 180f);
            if (Mathf.Abs(dTheta) > 0.001f)
            {
                _model.ScrollTo(_model.Scroll - dTheta / _model.SlotAngle);
                _lastDragAngle = a;
                _cards.Repaint();
            }
        }

        // 悬停命中（卡片域）+ 返回芯片域（轮毂）
        var hov = _dragging ? -1 : HoverIndexAt(_model, local);
        if (hov != _hoverIdx)
        {
            _hoverIdx = hov;
            _idleT = 0f;
            _cards.Repaint();
        }

        var hubHot = _model.Depth > 1 && IsInHubZone(local);
        if (hubHot != _hubHot)
        {
            _hubHot = hubHot;
            _idleT = 0f;
            _chrome.Repaint();
        }

        IntegrateCardStates(_model, d);
    }

    /// <summary>逐卡悬停/聚焦过渡因子与聚焦呼吸积分；活性态下卡片层逐帧重铺（呼吸演进），
    /// 活性环透明度随之淡入淡出。仅在扫描帧调用。</summary>
    private void IntegrateCardStates(RadialWheelModel model, float d)
    {
        var n = model.OptionCount;
        if (_h.Length != n)
        {
            Array.Resize(ref _h, n);
        }

        _breath = 0.5f + (0.5f * Mathf.Sin(_time * Mathf.Tau * 0.8f));
        var fi = model.FocusedIndex;
        if (fi != _lastFocusIdx)
        {
            _lastFocusIdx = fi;
            FocusChanged?.Invoke(fi);
        }

        for (var i = 0; i < n; i++)
        {
            var target = i == fi || i == _hoverIdx || i == _externalHighlight ? 1f : 0f;
            var nh = Mathf.MoveToward(_h[i], target, d * 7f);
            if (nh != _h[i])
            {
                _h[i] = nh;
                _cards.Repaint();
            }
        }

        if (_engaged)
        {
            _cards.Repaint();
        }
    }

    // ---------------- 输入（_Input：轮盘命中区内消费，区外放行） ----------------

    public override void _Input(InputEvent @event)
    {
        if (!Visible || _model == null || _shrinkT >= 0f)
        {
            return; // 收缩/回弹动画中为忙态，不接输入
        }

        if (@event is not InputEventMouseButton mb)
        {
            return;
        }

        var local = ToLocal(GetGlobalMousePosition());
        if (mb.Pressed)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left:
                    if (IsInWheelZone(_model, local))
                    {
                        _pressed = true;
                        _dragging = false;
                        _dragAccum = 0f;
                        _pressInHub = local.Length() < (Radius - BandW * 0.5f) * _contentScale;
                        _lastDragAngle = Mathf.RadToDeg(Mathf.Atan2(local.Y, local.X));
                        _idleT = 0f;
                        GetViewport().SetInputAsHandled();
                    }

                    break;
                case MouseButton.WheelUp:
                    if (IsInWheelZone(_model, local))
                    {
                        MoveFocusOrScroll(_model, -1);
                        _idleT = 0f;
                        GetViewport().SetInputAsHandled();
                    }

                    break;
                case MouseButton.WheelDown:
                    if (IsInWheelZone(_model, local))
                    {
                        MoveFocusOrScroll(_model, +1);
                        _idleT = 0f;
                        GetViewport().SetInputAsHandled();
                    }

                    break;
            }
        }
        else if (mb.ButtonIndex == MouseButton.Left && _pressed)
        {
            _pressed = false;
            if (_dragging)
            {
                _dragging = false;
                ScrollTowards(_model, Math.Round(_model.Scroll, MidpointRounding.AwayFromZero)); // 松手吸附最近槽
            }
            else if (_pressInHub)
            {
                DoBack();
            }
            else
            {
                var hit = CardIndexAt(_model, local);
                if (hit >= 0)
                {
                    Confirm(_model, hit);
                }
            }
        }
    }

    // ---------------- 交互动作 ----------------

    private void Confirm(RadialWheelModel model, int index)
    {
        StartConfirmFx();
        if (model.CanDrill(index))
        {
            _pendingDrill = index;
            _pendingDrillOption = model.Current[index];
            _shrinkIsDrill = true;
            _shrinkT = 0f; // 收缩到内收位，完成时提交下钻并弹出新层
        }
        else
        {
            Confirmed?.Invoke(model.Current[index]);
        }
    }

    private void DoBack()
    {
        if (_model == null || !_model.Back())
        {
            return;
        }

        _externalHighlight = -1;
        _contentScale = RingQ; // 从内收位弹回满幅（bounce-out）
        _shrinkIsDrill = false;
        _shrinkT = 0f;
        Array.Resize(ref _h, _model.OptionCount);
        Array.Clear(_h); // 改层后清残留高亮，弹回首帧不带旧态
        Backed?.Invoke();
        _chrome.Repaint();
        _cards.Repaint();
    }

    private void StartConfirmFx()
    {
        _rippleT = 0f;
        _flashT = 0f;
        _fx?.Repaint();
    }

    private void FinishShrink(RadialWheelModel model)
    {
        _shrinkT = -1f;
        _contentScale = 1f;
        if (_shrinkIsDrill)
        {
            model.Drill(_pendingDrill); // CanDrill 已在 Confirm 时校验
            _pendingDrill = -1;
            _externalHighlight = -1;
            _popT = 0f; // 新层在满幅环上弹出
            Array.Resize(ref _h, model.OptionCount);
            Array.Clear(_h); // 改层后清残留高亮，新层首帧不带旧态
            _cards.Repaint();
            var drilled = _pendingDrillOption;
            _pendingDrillOption = null;
            if (drilled != null)
            {
                Drilled?.Invoke(drilled);
            }
        }

        _hoverIdx = -1;
    }

    private void ScrollTowards(RadialWheelModel model, double target)
    {
        _snapFrom = (float)model.Scroll;
        _snapTo = (float)Math.Clamp(target, 0, model.OptionCount - 1);
        _snapT = 0f;
    }

    /// <summary>聚焦步进：溢出弧面走滚动吸附（原语义）；全容弧面滚动位被钳居中，
    /// 改走模型焦点偏移（否则键盘/滚轮在整容菜单上永远无法移动聚焦项）。</summary>
    private void MoveFocusOrScroll(RadialWheelModel model, int delta)
    {
        if (model.FitsSpan(model.OptionCount))
        {
            model.MoveFocus(delta);
            _rescan = true;
            _cards.Repaint();
            return;
        }

        ScrollTowards(model, Math.Round(model.EffectiveScroll, MidpointRounding.AwayFromZero) + delta);
    }

    // ---------------- 键盘/手柄（_UnhandledInput：GUI 相位无人消费时接管） ----------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || _model == null || _shrinkT >= 0f || !KeyboardEnabled)
        {
            return; // 忙态/关闭态不接；KeyboardEnabled=false 时方向键留给页面焦点链
        }

        if (@event.IsActionPressed("ui_down"))
        {
            MoveFocusOrScroll(_model, +1);
            _idleT = 0f;
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_up"))
        {
            MoveFocusOrScroll(_model, -1);
            _idleT = 0f;
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_accept"))
        {
            _idleT = 0f;
            var focused = _model.FocusedIndex;
            if (focused >= 0)
            {
                Confirm(_model, focused);
            }

            GetViewport().SetInputAsHandled();
        }
    }

    // ---------------- 命中 ----------------

    private bool IsInHubZone(Vector2 local) => local.Length() < (Radius - BandW * 0.5f) * _contentScale;

    private bool IsInWheelZone(RadialWheelModel model, Vector2 local)
    {
        var r = local.Length();
        if (r <= (Radius - BandW * 0.5f) * _contentScale)
        {
            return true; // 内域（返回芯片/轮毂）
        }

        var ang = Mathf.RadToDeg(Mathf.Atan2(local.Y, local.X));
        return r <= (Radius + BandW * 0.6f) * _contentScale && Mathf.Abs(ang) <= (float)model.HalfSpan + 6f;
    }

    private int CardIndexAt(RadialWheelModel model, Vector2 local)
    {
        if (Mathf.Abs(local.Length() - Radius * _contentScale) > BandW * 0.75f)
        {
            return -1;
        }

        var hit = model.IndexAtAngle(Mathf.RadToDeg(Mathf.Atan2(local.Y, local.X)));
        return hit ?? -1;
    }

    private int HoverIndexAt(RadialWheelModel model, Vector2 local) =>
        IsInWheelZone(model, local) && !IsInHubZone(local) ? CardIndexAt(model, local) : -1;

    // ---------------- 绘制：钢构层（面包屑/轮毂/环带） ----------------

    private void DrawChrome(RadialWheelLayer c)
    {
        if (_model == null || _font == null)
        {
            return;
        }

        var cs = _contentScale;
        var rInn = (Radius - BandW * 0.5f) * cs;
        var rOut = (Radius + BandW * 0.5f) * cs;
        var depth = _model.Depth;

        // 面包屑内环（已下钻的旧层级：暗钢细环 + 受光/背光缘线，随收缩一起缩放）
        for (var j = 0; j < depth - 1; j++)
        {
            var rj = Radius * Mathf.Pow(RingQ, depth - 1 - j) * cs;
            c.DrawArc(Vector2.Zero, rj, 0f, Mathf.Tau, 64, new Color(0.10f, 0.14f, 0.20f, 0.42f), 14f, true);
            c.DrawArc(Vector2.Zero, rj - 8f, 0f, Mathf.Tau, 64, new Color(0f, 0f, 0f, 0.35f), 1f, true);
            c.DrawArc(Vector2.Zero, rj + 8f, 0f, Mathf.Tau, 64, new Color(UITheme.PanelBorder, 0.16f), 1f, true);
        }

        // 内域暗面 + 装饰导引弧 + 轮毂刻度环 + 返回芯片（回上一层的常驻入口）
        c.DrawCircle(Vector2.Zero, (HubR + 40f) * cs, new Color(0.016f, 0.03f, 0.055f, 0.85f));
        c.DrawArc(Vector2.Zero, (HubR + 40f) * cs, 0f, Mathf.Tau, 48, new Color(UITheme.PanelBorder, 0.25f), 1.5f, true);
        c.DrawArc(Vector2.Zero, (HubR + 40f + (Radius - BandW * 0.5f - HubR - 40f) * 0.45f) * cs, 0f, Mathf.Tau, 64,
            new Color(UITheme.PanelBorder, 0.10f), 1f, true);
        c.DrawArc(Vector2.Zero, (HubR + 40f + (Radius - BandW * 0.5f - HubR - 40f) * 0.75f) * cs, 0f, Mathf.Tau, 64,
            new Color(UITheme.PanelBorder, 0.07f), 1f, true);
        for (var sTick = 0; sTick < 24; sTick++)
        {
            var ta = Mathf.Tau * sTick / 24f;
            var tu = new Vector2(Mathf.Cos(ta), Mathf.Sin(ta));
            c.DrawLine(tu * ((HubR + 18f) * cs), tu * ((HubR + 30f) * cs), new Color(UITheme.PanelBorder, 0.10f), 1.5f, true);
        }

        if (depth > 1)
        {
            DrawBackChip(c, (HubR + rInn) * 0.5f);
        }

        // 主动环钢带：径向明暗渐变（内缘背光 → 外缘受光，全站「上偏左受光」的径向版）+ 受光/背光缘线
        for (var sQ = 0; sQ < BandSegs; sQ++)
        {
            var a0 = Mathf.Tau * sQ / BandSegs;
            var a1 = Mathf.Tau * (sQ + 1) / BandSegs;
            var u0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            var u1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            var q = BandQuad[sQ];
            q[0] = u0 * rInn;
            q[1] = u1 * rInn;
            q[2] = u1 * rOut;
            q[3] = u0 * rOut;
            var bc = BandQuadCols[sQ];
            bc[0] = BandColInn;
            bc[1] = BandColInn;
            bc[2] = BandColOut;
            bc[3] = BandColOut;
            c.DrawPolygon(q, bc);
        }

        c.DrawArc(Vector2.Zero, rOut, 0f, Mathf.Tau, 96, new Color(UITheme.PanelBorder, 0.65f), 2f, true);
        c.DrawArc(Vector2.Zero, rInn, 0f, Mathf.Tau, 96, new Color(0f, 0f, 0f, 0.55f), 2f, true);
    }

    // ---------------- 绘制：卡片层 ----------------

    private void DrawCards(RadialWheelLayer c)
    {
        if (_model == null || _font == null)
        {
            return;
        }

        var pop = _popT >= 0f ? (float)RadialWheelModel.EaseOutCubic(_popT) : 1f;
        var n = _model.OptionCount;
        if (_h.Length != n)
        {
            Array.Resize(ref _h, n); // 绘制先于积分帧时兜底（下钻/回退/装载改层后首帧）
        }

        var rOut = (Radius + BandW * 0.5f) * _contentScale;

        // 卡片
        for (var i = 0; i < n; i++)
        {
            var a = (float)_model.AngleOf(i);
            if (Mathf.Abs(a) > (float)_model.HalfSpan)
            {
                continue; // 视口剪裁之外的弧段本就不可见，弧端外不再绘制
            }

            DrawCard(c, _model, i, a, pop);
        }

        // 间隙刻度（相邻卡之间的弧中点，不与卡片重叠）：仪表感 + 槽位可数性，弧端渐隐
        var gapA = _model.SlotAngle * 0.5;
        for (var i = 0; i + 1 < n; i++)
        {
            var a = (float)(_model.AngleOf(i) + gapA);
            var vis = (float)_model.AlphaAt(a) * pop;
            if (Mathf.Abs(a) > (float)_model.HalfSpan || vis <= 0.01f)
            {
                continue;
            }

            var au = new Vector2(Mathf.Cos(Mathf.DegToRad(a)), Mathf.Sin(Mathf.DegToRad(a)));
            c.DrawLine(au * (rOut + 3f), au * (rOut + 11f), new Color(UITheme.PanelBorder, 0.4f * vis), 2f, true);
        }
    }

    // ---------------- 绘制：特效层（确认波纹/环带闪光） ----------------

    private void DrawFx(RadialWheelLayer c)
    {
        if (_model == null)
        {
            return;
        }

        // 轮毂活性环：双弧慢旋 + 微呼吸（活性态限定，透明度随活性淡入淡出）
        if (_spinA > 0.001f)
        {
            var rot = _time * Mathf.Tau * 0.22f;
            var hr = (HubR + 52f) * _contentScale;
            var scol = new Color(UITheme.Accent, (0.14f + 0.08f * _breath) * _spinA);
            c.DrawArc(Vector2.Zero, hr, rot, rot + 1.1f, 20, scol, 2f, true);
            c.DrawArc(Vector2.Zero, hr, rot + Mathf.Pi, rot + Mathf.Pi + 1.1f, 20, scol, 2f, true);
        }

        // 确认反馈：圆心三级脉冲波纹（宽度衰减）+ 环带扫掠闪光
        if (_rippleT >= 0f)
        {
            var e = (float)RadialWheelModel.EaseOutCubic(_rippleT);
            var rr = Mathf.Lerp(HubR * 0.5f, Radius * 1.06f, e) * _contentScale;
            var al = (1f - _rippleT) * 0.55f;
            c.DrawArc(Vector2.Zero, rr, 0f, Mathf.Tau, 64, new Color(UITheme.Accent, al), Mathf.Lerp(4f, 1.5f, _rippleT), true);
            c.DrawArc(Vector2.Zero, rr * 0.82f, 0f, Mathf.Tau, 64, new Color(UITheme.Accent, al * 0.5f), 2f, true);
            c.DrawArc(Vector2.Zero, rr * 0.6f, 0f, Mathf.Tau, 64, new Color(UITheme.Accent, al * 0.25f), 1.5f, true);
        }

        if (_flashT >= 0f)
        {
            // 旋转扇形（圆心透明 → 弧端 accent）替代整带白闪；随附一道快速外扩亮环
            var e = (float)RadialWheelModel.EaseOutCubic(_flashT);
            var rot = e * Mathf.Tau;
            var wr = Radius * _contentScale;
            WedgePts[0] = Vector2.Zero;
            WedgeCols[0] = new Color(UITheme.Accent, 0f);
            for (var sW = 1; sW < WedgePts.Length; sW++)
            {
                var a = rot + (WedgeSpan * (sW - 1f) / (WedgePts.Length - 2));
                WedgePts[sW] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * wr;
                WedgeCols[sW] = new Color(UITheme.Accent, (1f - _flashT) * 0.20f);
            }

            c.DrawPolygon(WedgePts, WedgeCols);
            c.DrawArc(Vector2.Zero, Mathf.Lerp(Radius * 0.9f, Radius * 1.05f, e) * _contentScale, 0f, Mathf.Tau, 96,
                new Color(1f, 1f, 1f, (1f - _flashT) * 0.35f), 3f, true);
        }
    }

    private void RepaintAll()
    {
        _chrome?.Repaint();
        _cards?.Repaint();
        _fx?.Repaint();
    }

    private void DrawBackChip(RadialWheelLayer c, float midR)
    {
        // 位置：内域中线上（可视弧中点），双箭头 ‹‹ + 可选文字；指针在轮毂域时亮起
        var p = new Vector2(midR, 0f);
        c.DrawSetTransform(p, 0f, Vector2.One);
        var col = _hubHot ? UITheme.Accent : new Color(UITheme.PanelBorder, 0.85f);
        c.DrawLine(new Vector2(6f, -9f), new Vector2(-6f, 0f), col, 2.5f, true);
        c.DrawLine(new Vector2(-6f, 0f), new Vector2(6f, 9f), col, 2.5f, true);
        c.DrawLine(new Vector2(16f, -9f), new Vector2(4f, 0f), col, 2.5f, true);
        c.DrawLine(new Vector2(4f, 0f), new Vector2(16f, 9f), col, 2.5f, true);
        if (BackLabel.Length > 0 && _font != null)
        {
            c.DrawString(_font, new Vector2(26f, 6f), BackLabel, HorizontalAlignment.Left, -1, 16,
                _hubHot ? new Color(UITheme.Text, 1f) : new Color(UITheme.TextDim, 0.9f));
        }

        c.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawCard(RadialWheelLayer c, RadialWheelModel model, int i, float angleDeg, float pop)
    {
        var focused = i == model.FocusedIndex;
        var alpha = (float)model.AlphaAt(angleDeg) * pop;
        if (alpha <= 0.01f)
        {
            return;
        }

        var h = Mathf.Max(_h[i], focused ? 1f : 0f); // 悬停/聚焦共享的平滑过渡因子（聚焦恒满）
        var aRad = Mathf.DegToRad(angleDeg);
        var cardScale = (1f + 0.09f * h) * Mathf.Lerp(1.12f, 1f, pop);
        var pos = new Vector2(Mathf.Cos(aRad), Mathf.Sin(aRad)) * ((Radius + 6f * h) * _contentScale); // 径向浮起 = 悬停的 Z 表达
        var rot = Mathf.Clamp(angleDeg * CardTiltFactor, -20f, 20f) * Mathf.DegToRad(1f);

        c.DrawSetTransform(pos, rot, new Vector2(cardScale, cardScale));

        // 底板：暗钢随过渡因子渐亮（聚焦再提一档）+ 切角
        var fill = CardFillIdle.Lerp(CardFillHot, h);
        if (focused)
        {
            fill = fill.Lerp(CardFillFocus, 0.6f);
        }

        c.DrawPolygon(CardPts, Fill(CardFill, new Color(fill, (0.93f + 0.05f * h) * alpha), CardFill.Length));

        // 描边：钢线 → ACCENT 亮边随过渡渐变；聚焦边呼吸 + 外圈泛光
        var border = UITheme.PanelBorder.Lerp(UITheme.Accent, h);
        var borderA = focused ? (0.85f + 0.15f * _breath) * alpha : (0.62f + 0.28f * h) * alpha;
        c.DrawPolyline(CardLoop, new Color(border, borderA), 1.5f + (0.5f * h), true);
        if (focused)
        {
            c.DrawPolyline(CardLoop, new Color(UITheme.Accent, (0.14f + 0.10f * _breath) * alpha), 6f, true);
        }

        // 图标槽 + 字形：必须同变换绘制在槽中心（分开变换会让槽框错位到卡片原点）
        var socketC = new Vector2(-CardW * 0.5f + 50f, 0f);
        var socketWorld = pos + (socketC * cardScale).Rotated(rot);
        c.DrawSetTransform(socketWorld, rot, new Vector2(cardScale, cardScale));
        c.DrawPolygon(SocketPts, Fill(SocketFill, new Color(0.024f, 0.040f, 0.066f, 0.92f * alpha), SocketFill.Length));
        c.DrawPolyline(SocketLoop, new Color(UITheme.PanelBorder, (0.35f + 0.25f * h) * alpha), 1f, true);
        c.DrawSetTransform(socketWorld, rot, new Vector2(15f * cardScale, 15f * cardScale));
        DrawGlyph(c, model.Current[i].Glyph, new Color(UITheme.Accent, (0.62f + 0.38f * h) * alpha));

        // 标签（恢复卡片变换）：随过渡由次文字亮到主文字
        c.DrawSetTransform(pos, rot, new Vector2(cardScale, cardScale));
        var labelCol = UITheme.TextDim.Lerp(UITheme.Text, h);
        // 宽度钳制：超长标签省略号截断（不裁字到描边外）
        c.DrawString(_font, new Vector2(-CardW * 0.5f + 78f, 8f), model.Current[i].Label, HorizontalAlignment.Left,
            CardW - 90f, 22, new Color(labelCol, (0.8f + 0.2f * h) * alpha));

        // 左缘选择轨：选中态的恒定 accent 锚点（呼吸只作用于聚焦项）
        var railA = (focused ? 0.85f + 0.15f * _breath : 0.6f * h) * alpha;
        if (railA > 0.01f)
        {
            c.DrawPolygon(RailPts, Fill(RailCols, new Color(UITheme.Accent, railA), 4));
        }

        // 聚焦标记：卡片径向外缘的 accent 三角（呼吸）。挂在卡片局部右缘随卡倾斜，
        // 不出卡片旋转包络（端点角 ±36° 的卡底缘 ≈927 不压 HUD 顶缘 ≈940）
        if (focused)
        {
            var tip = CardW * 0.5f + 4f;
            MarkPts[0] = new Vector2(tip + 9f, 0f);
            MarkPts[1] = new Vector2(tip, -5f);
            MarkPts[2] = new Vector2(tip, 5f);
            c.DrawPolygon(MarkPts, Fill(MarkCols, new Color(UITheme.Accent, (0.55f + 0.4f * _breath) * alpha), 3));
        }

        c.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>单位字形（半径 1 的多边形/线段），经 DrawSetTransform 缩放到图标尺寸。</summary>
    private void DrawGlyph(RadialWheelLayer c, RadialGlyph glyph, Color col)
    {
        switch (glyph)
        {
            case RadialGlyph.Diamond:
                c.DrawPolygon(GlyphDiamond, Fill(Poly4, col, 4));
                break;
            case RadialGlyph.Triangle:
                c.DrawPolygon(GlyphTriangle, Fill(Poly3, col, 3));
                break;
            case RadialGlyph.Hex:
                c.DrawPolygon(HexPts, Fill(Poly6, col, 6));
                break;
            case RadialGlyph.Bolt:
                c.DrawPolygon(GlyphBolt, Fill(Poly6, col, 6));
                break;
            case RadialGlyph.Ring:
                c.DrawArc(Vector2.Zero, 0.85f, 0f, Mathf.Tau, 24, col, 0.22f, true);
                break;
            case RadialGlyph.Cross:
                c.DrawLine(new Vector2(-0.8f, 0f), new Vector2(0.8f, 0f), col, 0.22f, true);
                c.DrawLine(new Vector2(0f, -0.8f), new Vector2(0f, 0.8f), col, 0.22f, true);
                break;
            case RadialGlyph.Chevron:
                c.DrawPolyline(ChevronPts, col, 0.22f, true);
                break;
            case RadialGlyph.Star:
                c.DrawLine(new Vector2(0f, -1f), new Vector2(0f, 1f), col, 0.16f, true);
                c.DrawLine(new Vector2(-1f, 0f), new Vector2(1f, 0f), col, 0.16f, true);
                c.DrawLine(new Vector2(-0.6f, -0.6f), new Vector2(0.6f, 0.6f), col, 0.12f, true);
                c.DrawLine(new Vector2(-0.6f, 0.6f), new Vector2(0.6f, -0.6f), col, 0.12f, true);
                break;
        }
    }

    private static readonly Vector2[] HexPts = CreateHex();
    private static readonly Vector2[] ChevronPts = { new(-0.5f, -0.8f), new(0.5f, 0f), new(-0.5f, 0.8f) };

    private static Vector2[] CreateHex()
    {
        var pts = new Vector2[6];
        for (var i = 0; i < 6; i++)
        {
            var a = Mathf.DegToRad(60f * i - 90f);
            pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        return pts;
    }
}
