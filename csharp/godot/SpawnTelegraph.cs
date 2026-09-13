using Godot;

namespace InfiAir;

/// <summary>
/// 敌机/编队入场预告：
/// 可见区域顶部对应 x 位置的竖线 + 箭头，闪烁淡出后自毁。
/// 三角几何静态复用（P2：替代每帧构造 PackedVector2Array）；
/// 身份色可注入（普通波次＝警示红，编队事件＝事件身份色）——几何与寿命口径全库一份。
/// </summary>
public partial class SpawnTelegraph : Node2D
{
    private const float DefaultDuration = 0.6f;
    private static readonly Vector2[] ArrowTriangle =
    {
        new(-8.0f, 70.0f), new(8.0f, 70.0f), new(0.0f, 86.0f),
    };

    /// <summary>默认时长公开访问口（BalanceService 读取作 balance.json 缺键回退）。</summary>
    public static float GetDefaultDuration() => DefaultDuration;

    /// <summary>实例视觉寿命（spawner 注入 balance.json spawner.telegraph_duration）。</summary>
    public float Duration { get; set; } = DefaultDuration;

    /// <summary>线/箭头本色（默认警示红；alpha 由闪烁曲线逐帧写入）。</summary>
    public Color Tint { get; set; } = new(1.0f, 0.2f, 0.2f);

    /// <summary>精英身份标记（调用方按精英波置位）：在原竖线/箭头之上加亮加粗的同位线、
    /// 两道侧翼刻度与脉动同心环——只强化「这条航道来的是精英」的身份读感，
    /// 不改变预告时长、竖线长度/宽度语义与箭头几何。默认 false 时与既有普通预告完全一致。</summary>
    public bool EliteVariant { get; set; }

    private float _t;

    /// <summary>构造后由调用方设置 Position。</summary>
    public SpawnTelegraph()
    {
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= Duration)
        {
            QueueFree();
        }
        else
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var alpha = 0.8f * (1.0f - (_t / Duration)) * (0.6f + (0.4f * Enemy.SinFast(_t * 30.0f)));
        var color = new Color(Tint, alpha);
        DrawRect(new Rect2(-2.0f, 0.0f, 4.0f, 70.0f), color);
        DrawColoredPolygon(ArrowTriangle, color);
        if (EliteVariant)
        {
            DrawEliteMarkers(alpha);
        }
    }

    /// <summary>精英附加标记（几何全部围绕 x=0、落在原预告footprint 内，不改变指示的落点与覆盖范围）。</summary>
    private void DrawEliteMarkers(float alpha)
    {
        var bright = new Color(Tint.Lightened(0.3f), Mathf.Min(alpha * 1.4f, 1.0f));
        // 同位加粗底衬（低 alpha 垫亮，不改变线宽语义）
        DrawRect(new Rect2(-3.5f, 0.0f, 7.0f, 70.0f), new Color(bright, Mathf.Min(alpha * 0.5f, 0.5f)));
        // 两道侧翼刻度：精英身份符号
        DrawLine(new Vector2(-12.0f, 22.0f), new Vector2(12.0f, 22.0f), bright, 2.0f);
        DrawLine(new Vector2(-9.0f, 50.0f), new Vector2(9.0f, 50.0f), bright, 2.0f);
        // 脉动同心环（相位与原闪烁错开，强化身份）
        var pulse = Mathf.Abs(Enemy.SinFast(_t * 18.0f));
        DrawArc(Vector2.Zero, 15.0f + 3.0f * pulse, 0.0f, Mathf.Tau, 24, bright, 2.0f);
    }
}
