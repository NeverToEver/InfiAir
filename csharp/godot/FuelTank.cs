using Godot;
using InfiAir.Core;

namespace InfiAir;

/// <summary>
/// 燃料量槽（观测窗式）：切角瓦片外框 + 深腔 + 液位 + 侧缘刻度 + 低量警戒线。
///
/// 形态依据——燃料是**持续消耗的预算**（注入/消耗的存量），关心「还剩多少、还够不够」，
/// 故用容器量槽（液位高度即读数），不用环形（环形是充能循环的语汇）也不用指针（无需读速率）。
///
/// 液体表现的克制纪律：**静止几乎不动、变化时才动**。常驻液面只有约 2px 的低频起伏（给出「是液体」
/// 的材质暗示但不持续抢视线——弹幕游戏里任何常驻动效都会分心），数值变化时叠加一次衰减晃动读作惯性。
/// 不做气泡：那是无信息量的装饰，正是「展示品」的常见标志。
///
/// 成本：常态每帧 ≤12 点液面多边形 + 4 条刻度线，控件仅 30×52；低量时液色与外框转警戒色。
/// Control 子类，_Draw 程序化绘制（文件名与类型同名）。
/// </summary>
public partial class FuelTank : Control
{
    /// <summary>液面横向采样段数：12 段足够读作液面，绘制量固定不随尺寸增长。</summary>
    private const int WaveSegments = 12;

    /// <summary>液面低频起伏（Hz）：只做材质暗示，不做动效表演。</summary>
    private const float WaveHz = 0.32f;

    /// <summary>液态起伏幅度（占内腔高度比例）：约 4% ≈ 2px，刚好读作液面而非静止切线。</summary>
    private const float WaveAmpBase = 0.040f;

    /// <summary>数值变化时的附加幅度（随 SloshDecay 衰减回常态）。</summary>
    private const float WaveAmpSlosh = 0.110f;

    /// <summary>晃动衰减速率（每秒）与液位追赶速率（比例收敛）。</summary>
    private const float SloshDecay = 2.8f;
    private const float LevelEaseRate = 9.0f;

    /// <summary>切角与外框内缩（px）：与 AbilitySocket 同一形状语汇。</summary>
    private const float Chamfer = 7.0f;
    private const float Pad = 3.0f;

    /// <summary>低量警戒线（比例）：液位低于此值转警戒色，刻度区同步标红。</summary>
    private const float WarnRatio = 0.3f;

    /// <summary>侧缘刻度数：每 25% 一格，给液位提供量程参照（不写数字）。</summary>
    private const int Ticks = 5;

    private float _ratio = 1.0f;
    private float _shown = 1.0f;
    private float _wavePhase;
    private float _slosh;
    private bool _warn;
    private bool _reduceFlash;

    public override void _Ready()
    {
        _shown = _ratio;
        // 常态推进（液面材质起伏）：每帧开销有界且极小；reduce_flash 下由 _Process 冻结
        SetProcess(true);
    }

    /// <summary>燃料比例（0..1）：变化即叠加一次衰减晃动，液位平滑追赶。</summary>
    public void SetRatio(float ratio)
    {
        var next = Mathf.Clamp(ratio, 0.0f, 1.0f);
        if (Mathf.Abs(next - _ratio) < 0.0005f)
        {
            return;
        }

        _slosh = Mathf.Min(_slosh + Mathf.Abs(next - _ratio) * 3.4f, 1.0f);
        _ratio = next;
        SetProcess(true);
    }

    /// <summary>低量警戒：液色与刻度区转危险色。</summary>
    public void SetWarn(bool warn)
    {
        if (warn == _warn)
        {
            return;
        }

        _warn = warn;
        QueueRedraw();
    }

    /// <summary>无障碍：冻结液面起伏（转平面），不再泵动。</summary>
    public void SetReduceFlash(bool reduce)
    {
        if (reduce == _reduceFlash)
        {
            return;
        }

        _reduceFlash = reduce;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        var busy = false;

        if (!_reduceFlash)
        {
            _wavePhase = Mathf.PosMod(_wavePhase + d * WaveHz, 1.0f);
            if (_slosh > 0.001f)
            {
                _slosh = Mathf.Max(_slosh - d * SloshDecay, 0.0f);
            }

            busy = true;
        }

        if (Mathf.Abs(_shown - _ratio) > 0.0005f)
        {
            _shown = Mathf.MoveToward(_shown, _ratio, LevelEaseRate * d * Mathf.Max(Mathf.Abs(_ratio - _shown), 0.05f));
            busy = true;
        }
        else
        {
            _shown = _ratio;
        }

        if (busy)
        {
            QueueRedraw();
        }
        else
        {
            SetProcess(false); // reduce_flash 冻结后无需逐帧
        }
    }

    public override void _Draw()
    {
        var pts = UITheme.ChamferPoints(Size, Chamfer);
        if (pts.Length == 0)
        {
            return;
        }

        var inner = new Rect2(Pad, Pad, Size.X - Pad * 2.0f, Size.Y - Pad * 2.0f);
        if (inner.Size.X <= 2.0f || inner.Size.Y <= 2.0f)
        {
            return;
        }

        DrawColoredPolygon(pts, new Color(UITheme.SlotDark, 0.75f));

        var body = _warn ? UITheme.Danger : UITheme.Accent;
        var levelY = inner.Position.Y + inner.Size.Y * (1.0f - Mathf.Clamp(_shown, 0.0f, 1.0f));
        if (_shown > 0.002f)
        {
            // 填充多边形要求液层够厚（薄到近共线时三角化失败、整块不画）；不够厚只留液面线。
            if (TankLiquid.HasDrawableFill(inner.Size.Y, _shown))
            {
                DrawLiquid(inner, levelY, body);
            }

            DrawMeniscus(inner, levelY, body);
        }

        DrawGraduations(inner);
        DrawFrame(pts);
    }

    /// <summary>液位以下填充 + 近液面提亮带（体积感的唯一来源，不做内层分区等装饰）。</summary>
    private void DrawLiquid(Rect2 inner, float levelY, Color body)
    {
        var surface = SurfacePoints(inner, levelY);
        var poly = new Vector2[surface.Length + 2];
        System.Array.Copy(surface, poly, surface.Length);
        poly[^2] = new Vector2(inner.Position.X + inner.Size.X, inner.Position.Y + inner.Size.Y);
        poly[^1] = new Vector2(inner.Position.X, inner.Position.Y + inner.Size.Y);
        DrawColoredPolygon(poly, new Color(body, 0.70f));

        var bandY = Mathf.Min(levelY + inner.Size.Y * 0.20f, inner.Position.Y + inner.Size.Y);
        var band = new Vector2[surface.Length + 2];
        System.Array.Copy(surface, band, surface.Length);
        band[^2] = new Vector2(inner.Position.X + inner.Size.X, bandY);
        band[^1] = new Vector2(inner.Position.X, bandY);
        DrawColoredPolygon(band, new Color(body.Lightened(0.28f), 0.40f));
    }

    /// <summary>液面弯月线：贴合波形的亮线——「这是液面」最强的单一信号。</summary>
    private void DrawMeniscus(Rect2 inner, float levelY, Color body)
    {
        DrawPolyline(SurfacePoints(inner, levelY), new Color(body.Lightened(0.6f), 1.0f), 1.4f, true);
    }

    private Vector2[] SurfacePoints(Rect2 inner, float levelY)
    {
        // 波幅上限由 TankLiquid 定（波谷不得越过内腔底，否则填充多边形自交、三角化整块失败）。
        var amp = TankLiquid.WaveAmplitude(
            inner.Size.Y,
            _shown,
            WaveAmpBase + (_reduceFlash ? 0.0f : _slosh * WaveAmpSlosh));
        var pts = new Vector2[WaveSegments + 1];
        for (var i = 0; i <= WaveSegments; i++)
        {
            var t = (float)i / WaveSegments;
            var x = inner.Position.X + inner.Size.X * t;
            var wave = _reduceFlash ? 0.0f : Mathf.Sin((_wavePhase + t * 1.2f) * Mathf.Tau);
            pts[i] = new Vector2(x, levelY + wave * amp);
        }

        return pts;
    }

    /// <summary>侧缘刻度：短横线给液位量程参照；低量区在警戒时标红。</summary>
    private void DrawGraduations(Rect2 inner)
    {
        for (var i = 1; i < Ticks; i++)
        {
            var f = (float)i / Ticks;
            var y = inner.Position.Y + inner.Size.Y * f;
            var warnTick = (1.0f - f) < WarnRatio;
            var col = warnTick && _warn
                ? new Color(UITheme.Danger, 0.9f)
                : new Color(UITheme.TickWhite, warnTick ? 0.5f : 0.32f);
            var len = warnTick ? inner.Size.X * 0.42f : inner.Size.X * 0.26f;
            DrawLine(new Vector2(inner.Position.X, y), new Vector2(inner.Position.X + len, y), col, 1.0f, true);
        }
    }

    /// <summary>外框：切角描边（与面板/插座同语汇）；警戒时边框转红（与液色同步）。</summary>
    private void DrawFrame(Vector2[] pts)
    {
        var loop = new Vector2[pts.Length + 1];
        System.Array.Copy(pts, loop, pts.Length);
        loop[^1] = pts[0];
        var col = _warn ? new Color(UITheme.Danger, 0.85f) : new Color(UITheme.PanelBorder, 0.9f);
        DrawPolyline(loop, col, 1.2f, true);
    }
}
