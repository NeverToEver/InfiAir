using Godot;

namespace InfiAir;

/// <summary>
/// 切角面板（Sci-Fi FUI）：四角斜切的矩形 + 1px 青色细边框，
/// 可选四角 L 形括号标记（brackets=true，重要面板开启）。
/// 直接作为容器使用：子节点绘制在面板底/边框之上。
/// Control 子类，[Export] PascalCase 属性（tscn 以同名访问）。
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

    /// <summary>内框线（嵌套切角细线，槽位/socket 质感）：默认关，增幅 瓦片类开启。</summary>
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

    /// <summary>拉丝钢板填充（近白灰度平铺纹理，只贡献纹路；色相/明暗仍由 BgColor 顶点色派生）：默认开。</summary>
    private bool _metalFill = true;

    [Export]
    public bool MetalFill
    {
        get => _metalFill;
        set
        {
            _metalFill = value;
            QueueRedraw();
        }
    }

    /// <summary>上下缘铆钉（大面板钢壳细节，尺寸足够才绘制）：默认关，页壳/Boss 背板开启。</summary>
    private bool _edgeRivets;

    [Export]
    public bool EdgeRivets
    {
        get => _edgeRivets;
        set
        {
            _edgeRivets = value;
            QueueRedraw();
        }
    }

    /// <summary>全息发光边缘（opt-in，虚影面板族）：切角轮廓多层外晕 + 内缘体积亮线，
    /// 并以「自发光投影」语汇取代钢板受光/阴影线（顶缘受光带、拼板缝、暗色 keyline、
    /// 铆钉都是受光金属的标记，自发光面上是语义冲突的）。默认关——钢板面板外观不变。</summary>
    private bool _holoEdge;

    [Export]
    public bool HoloEdge
    {
        get => _holoEdge;
        set
        {
            _holoEdge = value;
            QueueRedraw();
        }
    }

    /// <summary>四角琥珀短线（"目标锁定"角标）：切角内侧的 L 形短刻度，尺寸足够才绘制；
    /// 默认开——全站面板统一得到受激边缘的精密仪表感（战术琥珀视觉语言）。</summary>
    private bool _cornerTicks = true;

    [Export]
    public bool CornerTicks
    {
        get => _cornerTicks;
        set
        {
            _cornerTicks = value;
            QueueRedraw();
        }
    }

    private Texture2D? _streakTex;

    /// <summary>实例级缓存 GD.Load（命中引擎资源缓存）；不做 static 持有——引擎退出后 .NET finalize 触碰 native 会 segfault（见 UITheme.Font）。</summary>
    private Texture2D? StreakTex
    {
        get
        {
            _streakTex ??= GD.Load<Texture2D>("res://assets/sprites/ui/metal_streak.png");
            return _streakTex;
        }
    }

    /// <summary>内容自适应高度上限（0 = 不限）：内容超出时面板高度被钳制、不再撑超视口。</summary>
    [Export]
    public float MaxContentHeight { get; set; }

    private float _fitCheckTimer;

    public ChamferedPanel()
    {
        // 拉丝纹理 UV 超出 0..1 区间时按平铺采样
        TextureRepeat = TextureRepeatEnum.Enabled;
    }

    // 切角几何缓存（尺寸/chamfer 不变即复用，避免布局变化时的重复构建与分配；
    // 点序唯一来源是 UITheme.FillChamferPoints，本类不另写一份）
    private readonly Vector2[] _cachedPts = new Vector2[8];
    private readonly Vector2[] _cachedLoop = new Vector2[9]; // 闭合轮廓（首点收尾，供发光 halo 的 DrawPolyline 用）
    private float _cachedKeyW = -1.0f;
    private float _cachedKeyH = -1.0f;
    private float _cachedKeyC = -1.0f;

    public override void _Process(double delta)
    {
        // 隐藏面板不做内容自适应（消除不可见实例的每帧空转）
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

        // 几何缓存——尺寸/chamfer 未变直接复用上次数组；点序与全站方形构件同源（UITheme）
        if (_cachedKeyW < 0.0f || w != _cachedKeyW || h != _cachedKeyH || c != _cachedKeyC)
        {
            if (!UITheme.FillChamferPoints(_cachedPts, Size, c))
            {
                return;
            }

            for (var i = 0; i < _cachedPts.Length; i++)
            {
                _cachedLoop[i] = _cachedPts[i];
            }

            _cachedLoop[_cachedPts.Length] = _cachedPts[0]; // DrawPolyline 不自动闭合
            _cachedKeyW = w;
            _cachedKeyH = h;
            _cachedKeyC = c;
        }

        // 垂直渐变底（顶亮底暗，金属板受光）：逐顶点色绘制，色相仍由 BgColor 单源派生；
        // MetalFill 叠加平铺拉丝钢纹理（近白灰度），tint 已按纹理基色 ~0.83 预补偿。
        // 梯度刻意拉大（顶亮底暗一倍以上）——深色 tint 下弱梯度读作平涂，强梯度才是「受光钢板」。
        // 亮度偏移带暖偏（红>绿>蓝）：均匀偏移会在琥珀主题里留下冷蓝灰的面板底
        var topCol = new Color(BgColor.R + 0.135f, BgColor.G + 0.108f, BgColor.B + 0.080f, BgColor.A);
        var botCol = new Color(BgColor.R * 0.78f, BgColor.G * 0.70f, BgColor.B * 0.60f, BgColor.A);
        var vertColors = new[]
        {
            topCol, topCol, botCol, botCol, botCol, botCol, topCol, topCol,
        };
        var streak = MetalFill ? StreakTex : null;
        if (streak != null)
        {
            var ts = streak.GetSize();
            if (ts.X >= 1.0f && ts.Y >= 1.0f)
            {
                var uvs = new Vector2[_cachedPts.Length];
                for (var i = 0; i < _cachedPts.Length; i++)
                {
                    uvs[i] = new Vector2(_cachedPts[i].X / ts.X, _cachedPts[i].Y / ts.Y);
                }

                DrawPolygon(_cachedPts, vertColors, uvs, streak);
            }
            else
            {
                DrawPolygon(_cachedPts, vertColors);
            }
        }
        else
        {
            DrawPolygon(_cachedPts, vertColors);
        }

        if (HoloEdge)
        {
            // 全息发光边缘：宽窄两层外晕沿切角轮廓包边（自发光读感），核心亮线由下方公共边框承担。
            // halo 外溢 ~4px 出矩形：面板间距 ≥4px，不会蹭到邻件。
            DrawPolyline(_cachedLoop, new Color(BorderColor, BorderColor.A * 0.14f), 8.0f, true);
            DrawPolyline(_cachedLoop, new Color(BorderColor, BorderColor.A * 0.30f), 4.0f, true);
            // 内缘体积亮线：投影体内缘更亮（菲涅尔式读感），几何同钢板 keyline 但取暖亮色
            if (w >= 48.0f && h >= 32.0f)
            {
                var d = 2.5f;
                foreach (var line in InsetLoopPoints(Size, Chamfer, d))
                {
                    DrawLine(line.from, line.to, new Color(BorderColor, BorderColor.A * 0.22f), 1.0f, true);
                }
            }
        }
        else
        {
            // 倒角受光逻辑（光来自上偏左，与按钮钢板贴图一致）：顶缘双线受光带托出面板，底/侧缘内阴影沉入底面
            // 顶缘主受光线取全 BorderColor（琥珀受激），读作面板被顶部光源打亮
            DrawLine(new Vector2(c + 3.0f, 1.5f), new Vector2(w - c - 3.0f, 1.5f), BorderColor, 1.0f, true);
            DrawLine(new Vector2(c + 5.0f, 2.5f), new Vector2(w - c - 5.0f, 2.5f), new Color(BorderColor, BorderColor.A * 0.22f), 1.0f, true);
            DrawLine(new Vector2(c + 3.0f, h - 1.5f), new Vector2(w - c - 3.0f, h - 1.5f), new Color(0.0f, 0.0f, 0.0f, 0.55f), 1.0f, true);
            DrawLine(new Vector2(1.5f, c + 3.0f), new Vector2(1.5f, h - c - 3.0f), new Color(0.0f, 0.0f, 0.0f, 0.30f), 1.0f, true);
            DrawLine(new Vector2(w - 1.5f, c + 3.0f), new Vector2(w - 1.5f, h - c - 3.0f), new Color(0.0f, 0.0f, 0.0f, 0.30f), 1.0f, true);
            // 板金拼板缝（大面板自动）：中位暗缝 + 下缘受光棱线，钢板拼接结构感
            if (w >= 320.0f && h >= 240.0f)
            {
                var sy = h * 0.5f;
                DrawLine(new Vector2(c + 12.0f, sy), new Vector2(w - c - 12.0f, sy), new Color(0.0f, 0.0f, 0.0f, 0.32f), 1.0f, true);
                DrawLine(new Vector2(c + 12.0f, sy + 1.5f), new Vector2(w - c - 12.0f, sy + 1.5f), new Color(1.0f, 1.0f, 1.0f, 0.09f), 1.0f, true);
            }
        }

        // 四角琥珀刻度：切角之后的直边短标（受激边缘，战术仪表角标）——尺寸不足时不画
        if (CornerTicks && w >= 64.0f && h >= 48.0f)
        {
            var t = Mathf.Clamp(Mathf.Min(w, h) * 0.06f, 4.0f, 10.0f); // 单臂长
            var tickCol = new Color(BorderColor, Mathf.Min(0.95f, BorderColor.A * 2.0f));
            var o = c + 4.0f; // 越过切角段的起点
            // 每条边在切角两侧各留一短线：顶缘左右、底缘左右、左缘上下、右缘上下
            DrawLine(new Vector2(o, 2.5f), new Vector2(o + t, 2.5f), tickCol, 1.5f, true);
            DrawLine(new Vector2(w - o - t, 2.5f), new Vector2(w - o, 2.5f), tickCol, 1.5f, true);
            DrawLine(new Vector2(o, h - 2.5f), new Vector2(o + t, h - 2.5f), tickCol, 1.5f, true);
            DrawLine(new Vector2(w - o - t, h - 2.5f), new Vector2(w - o, h - 2.5f), tickCol, 1.5f, true);
            DrawLine(new Vector2(2.5f, o), new Vector2(2.5f, o + t), tickCol, 1.5f, true);
            DrawLine(new Vector2(2.5f, h - o - t), new Vector2(2.5f, h - o), tickCol, 1.5f, true);
            DrawLine(new Vector2(w - 2.5f, o), new Vector2(w - 2.5f, o + t), tickCol, 1.5f, true);
            DrawLine(new Vector2(w - 2.5f, h - o - t), new Vector2(w - 2.5f, h - o), tickCol, 1.5f, true);
        }
        // 板金拼缝：外轮廓内缩 2.5px 的暗色 keyline（socket 类 InnerFrame 已有自己的内框则跳过，避免双线打架；
        // 全息面板走 HoloEdge 分支的内缘亮线，暗缝是受光金属的标记）
        if (!HoloEdge && !InnerFrame && w >= 48.0f && h >= 32.0f)
        {
            foreach (var (from, to) in InsetLoopPoints(Size, Chamfer, 2.5f))
            {
                DrawLine(from, to, new Color(0.0f, 0.0f, 0.0f, 0.26f), 1.0f, true);
            }
        }

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
                foreach (var (from, to) in InsetLoopPoints(Size, Chamfer, d))
                {
                    DrawLine(from, to, col, 1.0f, true);
                }
            }
        }

        if (EdgeRivets && !HoloEdge && w >= 150.0f && h >= 40.0f)
        {
            var rx0 = w * 0.14f;
            var rx1 = w * 0.86f;
            DrawRivet(new Vector2(rx0, 9.0f));
            DrawRivet(new Vector2(rx1, 9.0f));
            DrawRivet(new Vector2(rx0, h - 9.0f));
            DrawRivet(new Vector2(rx1, h - 9.0f));
        }
    }

    /// <summary>外轮廓内缩 inset px 的闭合环（8 条线段端点对）：钢板 keyline / 全息内缘线 /
    /// 嵌套内框共用同一份内缩几何——切角随内缩收小，太小处钳到 2px。</summary>
    private static (Vector2 from, Vector2 to)[] InsetLoopPoints(Vector2 size, float chamfer, float inset)
    {
        var w = size.X;
        var h = size.Y;
        var ic = Mathf.Max(chamfer - inset, 2.0f);
        Vector2[] pts =
        {
            new(inset + ic, inset),
            new(w - inset - ic, inset),
            new(w - inset, inset + ic),
            new(w - inset, h - inset - ic),
            new(w - inset - ic, h - inset),
            new(inset + ic, h - inset),
            new(inset, h - inset - ic),
            new(inset, inset + ic),
        };
        var lines = new (Vector2 from, Vector2 to)[8];
        for (var i = 0; i < 8; i++)
        {
            lines[i] = (pts[i], pts[(i + 1) % 8]);
        }

        return lines;
    }

    /// <summary>钢板铆钉：暗钢头 + 左上受光弧 / 右下背光弧（与全站「上偏左」受光一致）。</summary>
    private void DrawRivet(Vector2 p)
    {
        DrawCircle(p, 3.2f, new Color(0.05f, 0.07f, 0.11f, 0.95f));
        DrawArc(p, 3.2f, 0.0f, Mathf.Tau, 12, new Color(0.0f, 0.0f, 0.0f, 0.65f), 1.2f, true);
        DrawArc(p, 2.0f, Mathf.Pi * 0.7f, Mathf.Pi * 1.55f, 5, new Color(1.0f, 1.0f, 1.0f, 0.60f), 1.1f, true);
        DrawArc(p, 2.0f, -Mathf.Pi * 0.3f, Mathf.Pi * 0.45f, 5, new Color(0.0f, 0.0f, 0.0f, 0.60f), 1.1f, true);
    }

}
