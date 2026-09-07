namespace InfiAir.Core;

/// <summary>轮盘选项图标原型（Godot 层程序化绘制，不用外部贴图）。</summary>
public enum RadialGlyph
{
    Diamond,
    Triangle,
    Hex,
    Bolt,
    Ring,
    Cross,
    Chevron,
    Star,
}

/// <summary>轮盘选项（层级树节点）。Children 非空 = 目录节点，可继续下钻；否则为叶子（确认即触发）。</summary>
public sealed class RadialWheelOption
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public RadialGlyph Glyph { get; init; } = RadialGlyph.Diamond;

    public IReadOnlyList<RadialWheelOption>? Children { get; init; }

    public bool HasChildren => Children is { Count: > 0 };
}

/// <summary>
/// 左缘轮盘的层级导航 + 弧面滚动状态机（纯逻辑，零 Godot 依赖，供 xUnit 覆盖）。
/// 坐标约定：弧面角以「度」计，0 = 弧面中点（轮盘圆心向右的射线），正角朝下；
/// item i 的弧角 = (i - 有效滚动位) × 槽距。滚动位以「槽」为单位。
/// </summary>
public sealed class RadialWheelModel
{
    /// <summary>层深上限：层级栈到底即视为叶子（防深树无限下钻）。</summary>
    public const int MaxDepth = 4;

    private sealed class Level
    {
        public Level(IReadOnlyList<RadialWheelOption> options)
        {
            Options = options;
        }

        public IReadOnlyList<RadialWheelOption> Options { get; }

        /// <summary>原始滚动位（槽位单位，可越界；展示层经 EffectiveScroll 钳制/居中）。</summary>
        public double Scroll { get; set; }
    }

    private readonly List<Level> _stack = new();

    public RadialWheelModel(IReadOnlyList<RadialWheelOption> roots)
    {
        if (roots is not { Count: > 0 })
        {
            throw new ArgumentException("radial wheel requires at least one root option", nameof(roots));
        }

        _stack.Add(new Level(roots));
    }

    /// <summary>每项弧距（度）。</summary>
    public double SlotAngle { get; init; } = 27.0;

    /// <summary>可视半幅（度）：|弧角| 超出即不可见。</summary>
    public double HalfSpan { get; init; } = 66.0;

    /// <summary>弧端渐隐带宽（度）：从 HalfSpan - FadeDeg 起向边缘平滑渐隐到 0。</summary>
    public double FadeDeg { get; init; } = 18.0;

    public int Depth => _stack.Count;

    public IReadOnlyList<RadialWheelOption> Current => Top.Options;

    public int OptionCount => Top.Options.Count;

    public double Scroll => Top.Scroll;

    private Level Top => _stack[^1];

    /// <summary>本层选项是否全部容于可视弧（全部可见时皮带整体居中、滚动无效）。</summary>
    public bool FitsSpan(int count) => count * SlotAngle <= HalfSpan * 2.0;

    /// <summary>有效滚动位：装不下时取原始位并钳制到 [0, n-1]；装得下时固定居中 (n-1)/2。</summary>
    public double EffectiveScroll
    {
        get
        {
            var n = Top.Options.Count;
            return FitsSpan(n) ? (n - 1) / 2.0 : Math.Clamp(Top.Scroll, 0, n - 1);
        }
    }

    /// <summary>弧面中点聚焦项（四舍五入到最近槽；.5 远离零）。</summary>
    public int FocusedIndex
    {
        get
        {
            var n = Top.Options.Count;
            return Math.Clamp((int)Math.Round(EffectiveScroll, MidpointRounding.AwayFromZero), 0, n - 1);
        }
    }

    public RadialWheelOption? FocusedOption
    {
        get
        {
            var i = FocusedIndex;
            return i >= 0 && i < Current.Count ? Current[i] : null;
        }
    }

    /// <summary>设置原始滚动位（调用方负责动画；展示按 EffectiveScroll 钳制）。</summary>
    public void ScrollTo(double slots) => Top.Scroll = slots;

    public void ScrollBy(double slots) => ScrollTo(Top.Scroll + slots);

    public bool CanDrill(int index)
    {
        if (Depth >= MaxDepth || index < 0 || index >= Current.Count)
        {
            return false;
        }

        return Current[index].HasChildren;
    }

    /// <summary>下钻：子层入栈（滚动位归 0，聚焦首项）。不可下钻返回 false（叶子语义由调用方触发确认）。</summary>
    public bool Drill(int index)
    {
        if (!CanDrill(index))
        {
            return false;
        }

        _stack.Add(new Level(Current[index].Children!));
        return true;
    }

    /// <summary>回退上一层。已在根层返回 false。</summary>
    public bool Back()
    {
        if (_stack.Count <= 1)
        {
            return false;
        }

        _stack.RemoveAt(_stack.Count - 1);
        return true;
    }

    /// <summary>item index 的弧角（度）。|弧角| > HalfSpan + FadeDeg 之外的项由展示层剔除。</summary>
    public double AngleOf(int index) => (index - EffectiveScroll) * SlotAngle;

    /// <summary>弧端渐隐系数（0..1，smoothstep）：提示弧面两端还有更多内容。</summary>
    public double AlphaAt(double angleDeg)
    {
        var a = Math.Abs(angleDeg);
        if (a >= HalfSpan)
        {
            return 0.0;
        }

        var t = (HalfSpan - a) / FadeDeg;
        return t >= 1.0 ? 1.0 : t * t * (3.0 - 2.0 * t);
    }

    /// <summary>命中：查询弧角须落在可视弧内（弧外不可见不可点），命中距查询角最近（半槽距内）的
    /// 可选项；无命中返回 null。（径向距离判定在展示层）</summary>
    public int? IndexAtAngle(double angleDeg)
    {
        if (Math.Abs(angleDeg) > HalfSpan)
        {
            return null;
        }

        var n = Current.Count;
        var nearest = (int)Math.Round(EffectiveScroll + angleDeg / SlotAngle, MidpointRounding.AwayFromZero);
        if (nearest < 0 || nearest >= n)
        {
            return null;
        }

        return Math.Abs(AngleOf(nearest) - angleDeg) <= SlotAngle * 0.5 ? nearest : null;
    }

    /// <summary>bounce ease-out（overshoot 后回落），轮盘收缩/回弹的统一缓动。t 需在 [0,1]。</summary>
    public static double BounceOut(double t)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;
        if (t < 1.0 / d1)
        {
            return n1 * t * t;
        }

        if (t < 2.0 / d1)
        {
            return n1 * (t -= 1.5 / d1) * t + 0.75;
        }

        if (t < 2.5 / d1)
        {
            return n1 * (t -= 2.25 / d1) * t + 0.9375;
        }

        return n1 * (t -= 2.625 / d1) * t + 0.984375;
    }

    /// <summary>ease-out cubic：淡入/吸附等次级动效。</summary>
    public static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - Math.Clamp(t, 0.0, 1.0), 3.0);
}
