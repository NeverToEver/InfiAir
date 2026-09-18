using Godot;

namespace InfiAir;

/// <summary>
/// 全息面板叠加层（虚影皮肤共用，基地控制台页板）：静态扫描线 + 周期下扫柔边亮带。
/// 扫描线沿袭旧 BaseConsoleScanlines（每 4px 一条 1px 暖线，单节点 1 draw call）；
/// 亮带是柔边渐变纹理（透明→全息琥珀→透明），tween 只动 position/modulate——
/// 不进 _Process、不触发重绘，走合成器平移。静态扫描线无闪烁，减少闪光下保留；
/// 亮带属周期闪动，减少闪光下停驻隐藏（与母舰召唤小窗的实况扫描带同一口径）。
/// 活跃条件三合一：宿主层可见（SetRunning，CanvasLayer 隐藏不发可见性信号）×
/// 所在页可见（自订阅 VisibilityChanged）× 未被减少闪光抑制；
/// 非活跃时不留冻结的半程亮带（杀 tween + 复位隐藏）。
/// </summary>
public partial class HoloPanelFx : Control
{
    /// <summary>亮带高（px）：占面板高 8%，钳在 56–110——太窄读成一条硬线，太宽洗掉内容。</summary>
    private const float BandMinHeight = 56.0f;

    private const float BandMaxHeight = 110.0f;

    /// <summary>单程扫掠秒数：慢到读作「环境投影噪声」而不是「扫一遍的事件」。</summary>
    private const float SweepSeconds = 6.0f;

    /// <summary>两趟之间的空窗秒数。</summary>
    private const float SweepGapSeconds = 3.0f;

    private const float BandFadeSeconds = 0.9f;

    private TextureRect? _band;
    private Tween? _sweepTween;
    private bool _treeShown = true; // 宿主层（CanvasLayer）显隐——层隐藏不触发子节点 VisibilityChanged
    private bool _suppressed;

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;
        // 亮带随本层矩形裁剪（面板底之上、内容之下的整幅叠加层）：上下程不外溢面板
        ClipContents = true;
        // 分类切页是普通 Control 显隐，引擎会向子节点传播可见性变化——亮带随所在页自动启停；
        // 整层显隐（CanvasLayer）传播不到，由宿主经 SetRunning 手工驱动，见 OnVisibleChangedForFx
        VisibilityChanged += UpdateSweep;
        Resized += () =>
        {
            QueueRedraw();
            LayoutBand();
        };
        BuildBand();
        UpdateSweep();
    }

    public override void _Draw()
    {
        var y = 2.0f;
        while (y < Size.Y)
        {
            DrawLine(new Vector2(0.0f, y), new Vector2(Size.X, y), UITheme.PhantomScan, 1.0f);
            y += 4.0f;
        }
    }

    /// <summary>宿主层显隐驱动的播放/暂停（CanvasLayer 隐藏不触发 VisibilityChanged，
    /// 只能由宿主手工转发；页面级显隐由本类自订阅 VisibilityChanged 处理）。</summary>
    public void SetRunning(bool running)
    {
        _treeShown = running;
        if (IsInsideTree())
        {
            UpdateSweep();
        }
    }

    /// <summary>减少闪光设置驱动的抑制（亮带停驻隐藏，扫描线保留）。</summary>
    public void SetFxSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        if (IsInsideTree())
        {
            UpdateSweep();
        }
    }

    private void BuildBand()
    {
        _band = new TextureRect
        {
            Texture = MakeBandTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f),
        };
        AddChild(_band);
        LayoutBand();
    }

    private void LayoutBand()
    {
        if (_band == null)
        {
            return;
        }

        var bandH = Mathf.Clamp(Size.Y * 0.08f, BandMinHeight, BandMaxHeight);
        _band.Size = new Vector2(Size.X, bandH);
        _band.Position = new Vector2(0.0f, -bandH);
    }

    /// <summary>重建亮带循环 tween：杀旧再建（抑制/恢复/显隐切换走全量重建，
    /// 免去「暂停在半程 + 手动复位位置」与 tween 起点快照打架的不一致）。
    /// 活跃条件三合一：宿主层可见 × 所在页可见 × 未被减少闪光抑制。</summary>
    private void UpdateSweep()
    {
        if (_sweepTween != null && _sweepTween.IsValid())
        {
            _sweepTween.Kill();
        }

        _sweepTween = null;
        if (_band == null || !_treeShown || _suppressed || !IsVisibleInTree())
        {
            return;
        }

        var bandH = _band.Size.Y;
        _band.Position = new Vector2(0.0f, -bandH);
        _band.Modulate = new Color(_band.Modulate, 0.0f);
        var tween = CreateTween().SetLoops();
        tween.TweenProperty(_band, "position:y", Size.Y, SweepSeconds)
            .From(-bandH).SetTrans(Tween.TransitionType.Linear).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenProperty(_band, "modulate:a", 1.0f, BandFadeSeconds);
        tween.Parallel().TweenProperty(_band, "modulate:a", 0.0f, BandFadeSeconds).SetDelay(SweepSeconds - BandFadeSeconds);
        tween.TweenInterval(SweepGapSeconds);
        _sweepTween = tween;
    }

    /// <summary>柔边亮带纹理（垂直线性：透明→琥珀→透明）。每次调用新建（Godot 对象不做
    /// static 持有——引擎退出后 .NET finalize 触碰 native 会 segfault，见 UITheme.Font）；
    /// 64×64 渐变，尺寸微不足道。peakAlpha：常态环境扫掠取低值（0.10），开机显影等
    /// 一次性事件取高值（0.35）——同一纹理工厂，亮度语义由调用方声明。</summary>
    public static GradientTexture2D MakeBandTexture(float peakAlpha = 0.10f)
    {
        var gradient = new Gradient
        {
            Offsets = new float[] { 0.0f, 0.5f, 1.0f },
            Colors = new[]
            {
                new Color(UITheme.Holo, 0.0f),
                new Color(UITheme.Holo, peakAlpha),
                new Color(UITheme.Holo, 0.0f),
            },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = new Vector2(0.0f, 0.0f),
            FillTo = new Vector2(0.0f, 1.0f),
            Width = 64,
            Height = 64,
        };
    }
}
