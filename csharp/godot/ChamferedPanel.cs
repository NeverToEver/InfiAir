using Godot;

namespace InfiAir;

/// <summary>
/// 切角面板（Sci-Fi FUI）：四角斜切的矩形 + 1px 青色细边框，
/// 可选四角 L 形括号标记（brackets=true，重要面板开启）。
/// 直接作为容器使用：子节点绘制在面板底/边框之上。
/// M5 全量迁移（2026-08-08 自 scripts/ui_chamfered_panel.gd）：Control 子类，
/// [Export] PascalCase 属性（tscn/GDScript 以同名访问）；snake_case 别名供
/// 未迁移 GDScript 调用方（M6 过场）过渡，M7 删除。
/// </summary>
public partial class ChamferedPanel : Control
{
    private float _chamfer = 12.0f;

    [Export]
    public float Chamfer
    {
        get => _chamfer;
        set
        {
            _chamfer = value;
            QueueRedraw();
        }
    }

    private bool _brackets;

    [Export]
    public bool Brackets
    {
        get => _brackets;
        set
        {
            _brackets = value;
            QueueRedraw();
        }
    }

    private Color _bgColor = UITheme.PanelBg;

    [Export]
    public Color BgColor
    {
        get => _bgColor;
        set
        {
            _bgColor = value;
            QueueRedraw();
        }
    }

    private Color _borderColor = UITheme.PanelBorder;

    [Export]
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            QueueRedraw();
        }
    }

    private Color _bracketColor = UITheme.Accent;

    [Export]
    public Color BracketColor
    {
        get => _bracketColor;
        set
        {
            _bracketColor = value;
            QueueRedraw();
        }
    }

    /// <summary>内框线（嵌套切角细线，槽位/socket 质感）：默认关，buff 瓦片类开启。</summary>
    private bool _innerFrame;

    [Export]
    public bool InnerFrame
    {
        get => _innerFrame;
        set
        {
            _innerFrame = value;
            QueueRedraw();
        }
    }

    /// <summary>内框颜色；alpha=0 时回退为 border_color 半透明。</summary>
    private Color _innerFrameColor = new(0.0f, 0.0f, 0.0f, 0.0f);

    [Export]
    public Color InnerFrameColor
    {
        get => _innerFrameColor;
        set
        {
            _innerFrameColor = value;
            QueueRedraw();
        }
    }

    /// <summary>内容自适应边距（面板尺寸 = max(custom_minimum_size, 内容最小尺寸 + padding)）。</summary>
    [Export]
    public float Padding { get; set; } = 28.0f;

    /// <summary>内容自适应高度上限（0 = 不限）：内容超出时面板高度被钳制、不再撑超视口。</summary>
    [Export]
    public float MaxContentHeight { get; set; }

    private float _fitCheckTimer;

    // P2：切角几何缓存（尺寸/chamfer 不变即复用，避免布局变化时的重复构建与分配）
    private Vector2[] _cachedPts = System.Array.Empty<Vector2>();
    private float _cachedKeyW = -1.0f;
    private float _cachedKeyH = -1.0f;
    private float _cachedKeyC = -1.0f;

    public override void _Process(double delta)
    {
        // C27：隐藏面板不做内容自适应（消除不可见实例的每帧空转）
        if (!IsVisibleInTree())
        {
            return;
        }

        // 0.1s 节流做内容自适应（只按内容放大，不缩小显式设定的尺寸——
        // 无子节点的纯背板保持原尺寸，否则会被压缩成小菱形）
        _fitCheckTimer -= (float)delta;
        if (_fitCheckTimer > 0.0f)
        {
            return;
        }

        _fitCheckTimer = 0.1f;
        var need = ContentMinSize();
        var target = Size.Max(CustomMinimumSize).Max(need);
        // max_content_height：内容自适应只放大不缩小的前提下钳制高度上限（面板不超视口）
        if (MaxContentHeight > 0.0f)
        {
            target.Y = Mathf.Min(target.Y, MaxContentHeight);
        }

        if (target != CustomMinimumSize)
        {
            CustomMinimumSize = target;
        }

        if (Size != target)
        {
            Size = target;
        }
    }

    private Vector2 ContentMinSize()
    {
        var m = Vector2.Zero;
        foreach (var c in GetChildren())
        {
            if (c is Control control && control.Visible)
            {
                m = m.Max(control.GetCombinedMinimumSize());
            }
        }

        return m + new Vector2(Padding, Padding);
    }

    public override void _Draw()
    {
        var c = Chamfer;
        var w = Size.X;
        var h = Size.Y;
        if (w < c * 2.0f || h < c * 2.0f)
        {
            return;
        }

        // P2：几何缓存——尺寸/chamfer 未变直接复用上次数组
        if (_cachedPts.Length == 0 || w != _cachedKeyW || h != _cachedKeyH || c != _cachedKeyC)
        {
            _cachedPts = new Vector2[]
            {
                new(c, 0.0f),
                new(w - c, 0.0f),
                new(w, c),
                new(w, h - c),
                new(w - c, h),
                new(c, h),
                new(0.0f, h - c),
                new(0.0f, c),
            };
            _cachedKeyW = w;
            _cachedKeyH = h;
            _cachedKeyC = c;
        }

        DrawColoredPolygon(_cachedPts, BgColor);
        for (var i = 0; i < _cachedPts.Length; i++)
        {
            DrawLine(_cachedPts[i], _cachedPts[(i + 1) % _cachedPts.Length], BorderColor, 2.0f, true);
        }

        if (Brackets)
        {
            var b = 10.0f;
            var inset = 3.0f;
            var corners = new Vector2[][]
            {
                new[] { new Vector2(inset + b, inset), new Vector2(inset, inset), new Vector2(inset, inset + b) },
                new[] { new Vector2(w - inset - b, inset), new Vector2(w - inset, inset), new Vector2(w - inset, inset + b) },
                new[] { new Vector2(w - inset - b, h - inset), new Vector2(w - inset, h - inset), new Vector2(w - inset, h - inset - b) },
                new[] { new Vector2(inset + b, h - inset), new Vector2(inset, h - inset), new Vector2(inset, h - inset - b) },
            };
            foreach (var corner in corners)
            {
                DrawPolyline(corner, BracketColor, 1.5f, true);
            }
        }

        if (InnerFrame)
        {
            // 嵌套内框：外轮廓内缩 3px 的同款切角细线（socket 质感），角部随外框收小
            var d = 3.0f;
            var ic = Mathf.Max(c - d, 2.0f);
            if (w >= (d + ic) * 2.0f && h >= (d + ic) * 2.0f)
            {
                var col = InnerFrameColor.A > 0.0f ? InnerFrameColor : new Color(BorderColor, BorderColor.A * 0.5f);
                var ipts = new Vector2[]
                {
                    new(d + ic, d),
                    new(w - d - ic, d),
                    new(w - d, d + ic),
                    new(w - d, h - d - ic),
                    new(w - d - ic, h - d),
                    new(d + ic, h - d),
                    new(d, h - d - ic),
                    new(d, d + ic),
                };
                for (var i = 0; i < ipts.Length; i++)
                {
                    DrawLine(ipts[i], ipts[(i + 1) % ipts.Length], col, 1.0f, true);
                }
            }
        }
    }

    // ---------------- GDScript 鸭子调用兼容桥（M 批次过渡，M7 删除） ----------------

    public float chamfer { get => Chamfer; set => Chamfer = value; }

    public bool brackets { get => Brackets; set => Brackets = value; }

    public Color bg_color { get => BgColor; set => BgColor = value; }

    public Color border_color { get => BorderColor; set => BorderColor = value; }

    public Color bracket_color { get => BracketColor; set => BracketColor = value; }

    public bool inner_frame { get => InnerFrame; set => InnerFrame = value; }

    public Color inner_frame_color { get => InnerFrameColor; set => InnerFrameColor = value; }

    public float padding { get => Padding; set => Padding = value; }

    public float max_content_height { get => MaxContentHeight; set => MaxContentHeight = value; }
}
