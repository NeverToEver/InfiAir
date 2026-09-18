using Godot;
using InfiAir.Core;

namespace InfiAir;

/// <summary>
/// 分段条（Sci-Fi FUI）：N 段小切角块，填充主强调色，空段暗色。
/// 兼容旧 ProgressBar 用法：value / max_value（0-100）。
/// 分段血条：SegWeights 非空时按权重分格（段序 = Boss 阶段顺序，
/// P1→P2→ENRAGE 从左到右），每段对应一段 HP 区间、消耗从左端开始（P1 段先暗化），
/// 段色按 SegColors 逐段着色；未设置时保持等分语义（HP/燃料/dash 条）.
/// Control 子类，[Export] PascalCase 属性（tscn 以同名访问）。
/// </summary>
public partial class SegmentedBar : Control
{
    private int _segments = 10;

    [Export]
    public int Segments
    {
        get => _segments;
        set
        {
            _segments = value;
            QueueRedraw();
        }
    }

    private Color _fillColor = UITheme.Accent;

    [Export]
    public Color FillColor
    {
        get => _fillColor;
        set
        {
            if (value == _fillColor)
            {
                return; // 值未变不重绘（HUD 0.1s 轮询直写/回血信号残差路径）
            }

            _fillColor = value;
            QueueRedraw();
        }
    }

    private Color _emptyColor = new(UITheme.SlotDark, 0.8f);

    [Export]
    public Color EmptyColor
    {
        get => _emptyColor;
        set
        {
            _emptyColor = value;
            QueueRedraw();
        }
    }

    private Color _frameColor = UITheme.PanelBorder;

    [Export]
    public Color FrameColor
    {
        get => _frameColor;
        set
        {
            _frameColor = value;
            QueueRedraw();
        }
    }

    private float _maxValue = 100.0f;

    public float MaxValue
    {
        get => _maxValue;
        set
        {
            _maxValue = value;
            QueueRedraw();
        }
    }

    private float _value = 100.0f;

    public float Value
    {
        get => _value;
        set
        {
            if (value == _value)
            {
                return; // 值未变不重绘（调用侧赋同一计算值；精确比较足够）
            }

            var previous = _value;
            _value = value;
            QueueRedraw();
            if (!_enableGhost)
            {
                return;
            }

            if (value >= previous)
            {
                _ghostValue = value; // 回血不留残影：残影只表示「刚失去的血」
                _ghostHold = 0.0f;
            }
            else
            {
                // 受击沿用短延迟：连击掉血期间反复重置延迟，停手后残影才开始下坠
                _ghostHold = GhostHoldSeconds;
            }

            SetProcess(true);
        }
    }

    // ---------------- 受伤残影（血量下落拖尾） ----------------
    // 残影只是显示层：它不改变 Value、不影响任何判定，仅让「刚失去的血」多留一瞬。
    // 只在残影/闪段活跃时开 _process，静止后 SetProcess(false)——无永久逐帧开销。

    private bool _enableGhost;
    private float _ghostValue = 100.0f;
    private float _ghostHold;
    private int _flashSeg = -1;
    private float _flashT;

    /// <summary>残影开关（仅 HpBar 开启；Boss/燃料/dash/弹反/事件条不受影响）。</summary>
    [Export]
    public bool EnableGhost
    {
        get => _enableGhost;
        set
        {
            _enableGhost = value;
            _ghostValue = _value;
            _ghostHold = 0.0f;
            QueueRedraw();
        }
    }

    /// <summary>残影归位：读档续局/场景重载后把残影对齐当前值，避免满血基线被当作一次掉血演出。</summary>
    public void SnapGhost()
    {
        _ghostValue = _value;
        _ghostHold = 0.0f;
        if (_flashT <= 0.0f)
        {
            SetProcess(false);
        }
    }

    /// <summary>残影延迟（秒）：掉血后先停住再追赶，读作「损失量」而非即时抖动。</summary>
    private const float GhostHoldSeconds = 0.22f;

    /// <summary>残影追赶速率：比例收敛（每帧按剩余差值的比例推进，越近越慢）。</summary>
    private const float GhostEaseRate = 9.0f;

    /// <summary>残影追赶下限：比例项过小时仍以固定步进收尾，避免无限逼近。</summary>
    private const float GhostMinStep = 20.0f;

    /// <summary>残影/闪段收敛阈值（Value 量纲，0.05 ≈ 无感）。</summary>
    private const float AnimEpsilon = 0.05f;

    /// <summary>闪段衰减时长（秒）。</summary>
    private const float DamageFlashSeconds = 0.28f;

    /// <summary>残影填充色（红）：与填充色叠加后读作「已扣掉的那截」。</summary>
    private static readonly Color GhostColor = new(UITheme.Danger, 0.5f);

    /// <summary>分段模式下让第 index 段短闪（Boss 掉血时指向刚被消耗的那段）。
    /// 减少闪光下直接跳过（判据单源在 core FlashBudget）——无障碍下不做亮度泵动，
    /// 掉的是哪一段仍由残影段表达。</summary>
    public void FlashSegment(int index)
    {
        if (index < 0 || index >= Segments
            || !FlashBudget.AllowsOneShot(OneShotFlashId.HealthBarSegment, GameState.Instance.ReduceFlash))
        {
            return;
        }

        _flashSeg = index;
        _flashT = 1.0f;
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Ready()
    {
        // 未动画时彻底关闭逐帧回调（全部分段条默认零开销）
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        var busy = false;
        if (_enableGhost && _ghostValue > _value + AnimEpsilon)
        {
            busy = true;
            if (_ghostHold > 0.0f)
            {
                _ghostHold -= d;
            }
            else
            {
                var gap = _ghostValue - _value;
                _ghostValue -= Mathf.Max(gap * GhostEaseRate, GhostMinStep) * d;
                if (_ghostValue <= _value + AnimEpsilon)
                {
                    _ghostValue = _value;
                }
            }
        }
        else if (_ghostValue != _value)
        {
            _ghostValue = _value; // 残影已追平（含回血快照）
        }

        if (_flashT > 0.0f)
        {
            busy = true;
            _flashT -= d / DamageFlashSeconds;
            if (_flashT < 0.0f)
            {
                _flashT = 0.0f;
                _flashSeg = -1;
            }
        }

        if (busy)
        {
            QueueRedraw();
        }
        else
        {
            SetProcess(false);
        }
    }

    /// <summary>分段血条段权（非空启用分段模式；空 = 既有等分）。值变化才 queue_redraw。</summary>
    private Godot.Collections.Array _segWeights = new();

    public Godot.Collections.Array SegWeights
    {
        get => _segWeights;
        set
        {
            _segWeights = value;
            QueueRedraw();
        }
    }

    /// <summary>分段血条段色（缺省回退 fill_color；按段序一一对应）。</summary>
    private Godot.Collections.Array _segColors = new();

    public Godot.Collections.Array SegColors
    {
        get => _segColors;
        set
        {
            _segColors = value;
            QueueRedraw();
        }
    }

    /// <summary>分段模式下第 index 段的消耗度（0..1，纯函数供绘制与外部查询共用）：
    /// 段 i 对应 HP 区间 [hi, lo]（首段 hi=1.0 满血，段宽 = 权占比），ratio 低于段上界越多
    /// 消耗越多——消耗从血条左端（P1 段）开始，与既有「值高左端亮」的整体语义方向一致。</summary>
    public static float SegmentFill(float ratio, Godot.Collections.Array weights, int index)
    {
        if (index < 0 || index >= weights.Count)
        {
            return 1.0f;
        }

        var total = 0.0f;
        foreach (var w in weights)
        {
            total += (float)w.AsDouble();
        }

        if (total <= 0.0f)
        {
            return 1.0f;
        }

        var hi = 1.0f;
        var lo = 0.0f;
        for (var i = 0; i < index + 1; i++)
        {
            lo = hi - (float)weights[i].AsDouble() / total;
            if (i < index)
            {
                hi = lo;
            }
        }

        return Mathf.Clamp((hi - ratio) / Mathf.Max(hi - lo, 0.0001f), 0.0f, 1.0f);
    }

    /// <summary>ratio 落在哪一段（段序 = 权重数组从左到右；与 SegmentFill 同一区间划分）。
    /// Boss 掉血闪段用：找到首个尚有余量的段即「当前段」。空权重返回 -1。</summary>
    public static int SegmentIndexAt(float ratio, Godot.Collections.Array weights)
    {
        if (weights.Count == 0)
        {
            return -1;
        }

        var total = 0.0f;
        foreach (var w in weights)
        {
            total += (float)w.AsDouble();
        }

        if (total <= 0.0f)
        {
            return 0;
        }

        // 消耗从左端起算：累计段宽首次覆盖「已消耗比例」的段即当前段
        var consumed = 1.0f - Mathf.Clamp(ratio, 0.0f, 1.0f);
        var cumulative = 0.0f;
        for (var i = 0; i < weights.Count; i++)
        {
            cumulative += (float)weights[i].AsDouble() / total;
            if (cumulative >= consumed)
            {
                return i;
            }
        }

        return weights.Count - 1; // ratio 恰为 0：收尾闪最后一段
    }

    public override void _Draw()
    {
        if (Segments <= 0 || Size.Y <= 0.0f)
        {
            return;
        }

        var gap = 2.0f;
        if (SegWeights.Count > 0)
        {
            DrawWeighted(gap);
            return;
        }

        // 绘制序：空槽底盘 → 残影 → 实填充（残影在填充之下，只露出刚失去的那截）
        DrawEqualBase(gap);
        if (_enableGhost && _ghostValue > _value + AnimEpsilon && MaxValue > 0.0f)
        {
            DrawEqualFill(gap, _ghostValue / MaxValue, GhostColor, false);
        }

        DrawEqualFill(gap, MaxValue > 0.0f ? Value / MaxValue : 0.0f, FillColor, true);
        DrawMetalFrame();
    }

    /// <summary>等分模式空槽底盘（全段 EmptyColor；与旧实现逐段空槽同一渲染结果）。</summary>
    private void DrawEqualBase(float gap)
    {
        var segW = (Size.X - gap * (Segments + 1)) / Segments;
        for (var i = 0; i < Segments; i++)
        {
            var x = gap + i * (segW + gap);
            DrawRect(new Rect2(x, gap, segW, Size.Y - gap * 2.0f), EmptyColor);
        }
    }

    /// <summary>等分模式填充（只画填充段与末段小数部分，不画空槽——底盘已铺）。
    /// 平滑填充：满格数取 floor，最后一段按小数部分宽度部分填充。</summary>
    private void DrawEqualFill(float gap, float ratio, Color color, bool sheen)
    {
        var segW = (Size.X - gap * (Segments + 1)) / Segments;
        var exact = Mathf.Clamp(ratio, 0.0f, 1.0f) * Segments;
        var filled = (int)Mathf.Floor(exact);
        var partial = exact - filled;
        for (var i = 0; i < Segments; i++)
        {
            var x = gap + i * (segW + gap);
            var rect = new Rect2(x, gap, segW, Size.Y - gap * 2.0f);
            if (i < filled)
            {
                DrawRect(rect, color);
                if (sheen)
                {
                    DrawSheen(rect);
                }
            }
            else if (i == filled && partial > 0.0f)
            {
                var fillRect = new Rect2(rect.Position, new Vector2(segW * partial, rect.Size.Y));
                DrawRect(fillRect, color);
                if (sheen)
                {
                    DrawSheen(fillRect);
                }
            }
        }
    }

    /// <summary>分段血条绘制：按权重分格，逐段按消耗度填充（未消耗全亮、部分消耗暗底+右侧亮区、
    /// 已消耗全暗）。段色按 SegColors 逐段取色（当前消耗段的高亮 = 段内亮区）。</summary>
    private void DrawWeighted(float gap)
    {
        var ratio = Mathf.Clamp(Value / MaxValue, 0.0f, 1.0f);
        var x = gap;
        var total = WeightsTotal(); // 循环外缓存（段循环内每段重复全量累加）
        for (var i = 0; i < SegWeights.Count; i++)
        {
            var w = (float)SegWeights[i].AsDouble() / total * (Size.X - gap * (SegWeights.Count + 1));
            var consumed = SegmentFill(ratio, SegWeights, i);
            var rect = new Rect2(x, gap, w, Size.Y - gap * 2.0f);
            var col = i < SegColors.Count ? SegColors[i].AsColor() : FillColor;
            if (consumed <= 0.0f)
            {
                DrawRect(rect, col);
                DrawSheen(rect);
            }
            else if (consumed >= 1.0f)
            {
                DrawRect(rect, EmptyColor);
            }
            else
            {
                DrawRect(rect, EmptyColor);
                var fillW = rect.Size.X * (1.0f - consumed);
                var fillRect = new Rect2(new Vector2(rect.Position.X + rect.Size.X - fillW, rect.Position.Y), new Vector2(fillW, rect.Size.Y));
                DrawRect(fillRect, col);
                DrawSheen(fillRect);
            }

            // 段闪：Boss 掉血时叠一层白，指向刚被吃掉的那段（衰减由 _process 驱动）
            if (i == _flashSeg && _flashT > 0.0f)
            {
                DrawRect(rect, new Color(1.0f, 1.0f, 1.0f, _flashT * 0.7f));
            }

            x += w + gap;
        }

        DrawMetalFrame();
    }

    /// <summary>受光钢框：顶亮 / 底暗 / 侧翼弱化 + 框内上缘阴影（槽口深度感）；光向与全站面板一致（上偏左）。</summary>
    private void DrawMetalFrame()
    {
        var s = Size;
        DrawLine(new Vector2(0.5f, 0.5f), new Vector2(s.X - 0.5f, 0.5f), FrameColor, 1.0f, true);
        DrawLine(new Vector2(0.5f, s.Y - 0.5f), new Vector2(s.X - 0.5f, s.Y - 0.5f), new Color(FrameColor, FrameColor.A * 0.4f), 1.0f, true);
        var sideCol = new Color(FrameColor, FrameColor.A * 0.75f);
        DrawLine(new Vector2(0.5f, 0.5f), new Vector2(0.5f, s.Y - 0.5f), sideCol, 1.0f, true);
        DrawLine(new Vector2(s.X - 0.5f, 0.5f), new Vector2(s.X - 0.5f, s.Y - 0.5f), sideCol, 1.0f, true);
        DrawLine(new Vector2(1.5f, 1.5f), new Vector2(s.X - 1.5f, 1.5f), UITheme.ShadowBlack, 1.0f, true);
    }

    /// <summary>填充段顶部 1px 镜面高光（段太矮时省略，避免吃掉填充色）。</summary>
    private void DrawSheen(Rect2 rect)
    {
        if (rect.Size.Y < 5.0f)
        {
            return;
        }

        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 1.5f)), UITheme.SheenWhite);
    }

    private float WeightsTotal()
    {
        var total = 0.0f;
        foreach (var w in SegWeights)
        {
            total += (float)w.AsDouble();
        }

        return total > 0.0f ? total : 1.0f;
    }
}
