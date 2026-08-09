using Godot;

namespace InfiAir;

/// <summary>
/// 分段条（Sci-Fi FUI）：N 段小切角块，填充主强调色，空段暗色。
/// 兼容旧 ProgressBar 用法：value / max_value（0-100）。
/// 分段血条（2026-08-03 机制三）：seg_weights 非空时按权重分格（段序 = 文档阶段顺序，
/// P1→P2→ENRAGE 从左到右），每段对应一段 HP 区间、消耗从左端开始（P1 段先暗化），
/// 段色按 seg_colors 逐段着色；未设置时保持既有等分语义（HP/燃料/dash 条零改动）。
/// M5 全量迁移（2026-08-08 自 scripts/ui_segmented_bar.gd）：Control 子类，
/// [Export] PascalCase 属性；snake_case 别名供未迁移 GDScript 调用方过渡。
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
            _fillColor = value;
            QueueRedraw();
        }
    }

    private Color _emptyColor = new(0.05f, 0.09f, 0.14f, 0.8f);

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
            _value = value;
            QueueRedraw();
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

    /// <summary>分段模式下第 index 段的消耗度（0..1，纯函数供绘制与测试共用）：
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

    public override void _Draw()
    {
        if (Segments <= 0 || Size.Y <= 0.0f)
        {
            return;
        }

        var gap = 2.0f;
        if (SegWeights.Count == 0)
        {
            DrawWeighted(gap);
            return;
        }

        var segW = (Size.X - gap * (Segments + 1)) / Segments;
        // 平滑填充：满格数取 floor，最后一段按小数部分宽度部分填充
        var exact = Mathf.Clamp(Value / MaxValue, 0.0f, 1.0f) * Segments;
        var filled = (int)Mathf.Floor(exact);
        var partial = exact - filled;
        for (var i = 0; i < Segments; i++)
        {
            var x = gap + i * (segW + gap);
            var rect = new Rect2(x, gap, segW, Size.Y - gap * 2.0f);
            if (i < filled)
            {
                DrawRect(rect, FillColor);
            }
            else if (i == filled && partial > 0.0f)
            {
                DrawRect(rect, EmptyColor);
                DrawRect(new Rect2(rect.Position, new Vector2(segW * partial, rect.Size.Y)), FillColor);
            }
            else
            {
                DrawRect(rect, EmptyColor);
            }
        }

        DrawRect(new Rect2(Vector2.Zero, Size), FrameColor, false, 1.0f, true);
    }

    /// <summary>分段血条绘制：按权重分格，逐段按消耗度填充（未消耗全亮、部分消耗暗底+右侧亮区、
    /// 已消耗全暗）。段色按 seg_colors 逐段取色（当前消耗段的高亮 = 段内亮区）。</summary>
    private void DrawWeighted(float gap)
    {
        var ratio = Mathf.Clamp(Value / MaxValue, 0.0f, 1.0f);
        var x = gap;
        var total = WeightsTotal(); // 2026-08-03 审计：循环外缓存（段循环内每段重复全量累加）
        for (var i = 0; i < SegWeights.Count; i++)
        {
            var w = (float)SegWeights[i].AsDouble() / total * (Size.X - gap * (SegWeights.Count + 1));
            var consumed = SegmentFill(ratio, SegWeights, i);
            var rect = new Rect2(x, gap, w, Size.Y - gap * 2.0f);
            var col = i < SegColors.Count ? SegColors[i].AsColor() : FillColor;
            if (consumed <= 0.0f)
            {
                DrawRect(rect, col);
            }
            else if (consumed >= 1.0f)
            {
                DrawRect(rect, EmptyColor);
            }
            else
            {
                DrawRect(rect, EmptyColor);
                var fillW = rect.Size.X * (1.0f - consumed);
                DrawRect(new Rect2(new Vector2(rect.Position.X + rect.Size.X - fillW, rect.Position.Y), new Vector2(fillW, rect.Size.Y)), col);
            }

            x += w + gap;
        }

        DrawRect(new Rect2(Vector2.Zero, Size), FrameColor, false, 1.0f, true);
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

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public int segments { get => Segments; set => Segments = value; }

    public Color fill_color { get => FillColor; set => FillColor = value; }

    public float max_value { get => MaxValue; set => MaxValue = value; }

    public float value { get => Value; set => Value = value; }
}
