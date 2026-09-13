using Godot;

namespace InfiAir;

/// <summary>
/// 受击方向指示：订阅 GameState.PlayerDamaged，把伤害来源的世界坐标差换算成屏幕方向，
/// 在屏幕对应边缘画一段渐隐的弧 + 外指箭头。纯显示消费者——只读信号与玩家位置，
/// 不回写任何本局状态、不改变伤害结算。同屏弧数封顶（超出丢最旧），无弧时停 _process 且隐藏。
/// 世界→屏幕：只取画布变换的基（平移无意义——指示的是方向），兼容视角缩放/偏移。
/// 闪动脉冲受 ReduceFlash 无障碍约束（开启后退化为稳定渐隐，不做亮度泵动）。
/// </summary>
public partial class HudDamageArcs : Control
{
    /// <summary>同屏弧上限（多余伤害不排队，只保留最近方向）。</summary>
    private const int MaxArcs = 4;

    /// <summary>单弧存活时长（秒）。</summary>
    private const float ArcLife = 0.7f;

    /// <summary>弧心距屏幕边缘的内缩量（px）。</summary>
    private const float EdgeInset = 30.0f;

    /// <summary>弧半径与弧宽。</summary>
    private const float ArcRadius = 110.0f;
    private const float ArcWidth = 3.0f;

    /// <summary>弧半张角（弧度）与箭头臂长。</summary>
    private const float ArcHalfSpan = 0.40f;
    private const float ChevronArm = 13.0f;

    /// <summary>基础峰值不透明度。</summary>
    private const float MaxAlpha = 0.85f;

    /// <summary>脉动频率（Hz）；仅 ReduceFlash 关闭时启用。</summary>
    private const float PulseHz = 5.0f;

    /// <summary>弧数据：方向角 / 剩余寿命 / 强度权重（0.2..1，来自伤害占比）。</summary>
    private struct Arc
    {
        public float Angle;
        public float Life;
        public float Weight;
    }

    private readonly Arc[] _arcs = new Arc[MaxArcs];
    private int _arcCount;

    private readonly Callable _onPlayerDamaged;

    public HudDamageArcs()
    {
        _onPlayerDamaged = Callable.From<float, Vector2>(OnPlayerDamaged);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false; // 无弧时不参与绘制
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        SetProcess(false); // 空闲零逐帧开销
        var gs = GameState.Instance;
        if (!gs.IsConnected(GameState.SignalName.PlayerDamaged, _onPlayerDamaged))
        {
            gs.Connect(GameState.SignalName.PlayerDamaged, _onPlayerDamaged);
        }
    }

    public override void _ExitTree()
    {
        // GameState 为 autoload 恒存：显式断开，防本节点释放后信号回调指向已释放对象
        var gs = GameState.Instance;
        if (gs != null && gs.IsConnected(GameState.SignalName.PlayerDamaged, _onPlayerDamaged))
        {
            gs.Disconnect(GameState.SignalName.PlayerDamaged, _onPlayerDamaged);
        }
    }

    private void OnPlayerDamaged(float amount, Vector2 fromPos)
    {
        if (fromPos == Vector2.Inf)
        {
            return; // 无方向来源（环境伤害等）：不画方向指示
        }

        if (GameState.Instance.PlayerRef is not Player player)
        {
            return;
        }

        var worldDelta = fromPos - player.GlobalPosition;
        if (worldDelta.LengthSquared() < 1.0f)
        {
            return; // 贴脸/重合：方向无意义
        }

        var screenDelta = GetViewport().GetCanvasTransform().BasisXform(worldDelta);
        if (!screenDelta.IsFinite() || screenDelta.LengthSquared() < 0.0001f)
        {
            return;
        }

        var dir = screenDelta.Normalized();
        if (_arcCount >= MaxArcs)
        {
            // 丢最旧（数组左移，无分配）
            for (var i = 1; i < MaxArcs; i++)
            {
                _arcs[i - 1] = _arcs[i];
            }

            _arcCount = MaxArcs - 1;
        }

        var ratio = amount / (float)GameState.Instance.MaxHealth();
        _arcs[_arcCount].Angle = Mathf.Atan2(dir.Y, dir.X);
        _arcs[_arcCount].Life = ArcLife;
        _arcs[_arcCount].Weight = Mathf.Clamp(ratio * 4.0f + 0.25f, 0.25f, 1.0f); // 单次占比小，放大后仍可辨
        _arcCount += 1;
        Visible = true;
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        var kept = 0;
        for (var i = 0; i < _arcCount; i++)
        {
            _arcs[i].Life -= d;
            if (_arcs[i].Life > 0.0f)
            {
                _arcs[kept] = _arcs[i];
                kept += 1;
            }
        }

        _arcCount = kept;
        if (_arcCount == 0)
        {
            Visible = false;
            SetProcess(false);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_arcCount == 0 || Size.X <= 0.0f || Size.Y <= 0.0f)
        {
            return;
        }

        var center = Size * 0.5f;
        var half = center - new Vector2(EdgeInset, EdgeInset);
        var pulse = 1.0f;
        if (!GameState.Instance.ReduceFlash)
        {
            pulse = 0.78f + 0.22f * Enemy.SinFast((float)Time.GetTicksMsec() / 1000.0f * PulseHz * Mathf.Tau);
        }

        for (var i = 0; i < _arcCount; i++)
        {
            var arc = _arcs[i];
            var t = Mathf.Clamp(arc.Life / ArcLife, 0.0f, 1.0f);
            var col = new Color(UITheme.Danger, MaxAlpha * arc.Weight * t * pulse);
            var dir = new Vector2(Mathf.Cos(arc.Angle), Mathf.Sin(arc.Angle));
            var pos = EdgePoint(center, half, dir);
            DrawArc(pos, ArcRadius, arc.Angle - ArcHalfSpan, arc.Angle + ArcHalfSpan, 20, col, ArcWidth, true);
            // 外指箭头：明确「威胁来自这一侧」
            var perp = new Vector2(-dir.Y, dir.X);
            var tip = pos + dir * (ChevronArm * 0.5f);
            DrawLine(tip, tip - dir * ChevronArm + perp * (ChevronArm * 0.7f), col, ArcWidth, true);
            DrawLine(tip, tip - dir * ChevronArm - perp * (ChevronArm * 0.7f), col, ArcWidth, true);
        }
    }

    /// <summary>从中心沿 dir 射线与内缩矩形边界的交点（方向归一化，half 为内缩后的半宽高）。</summary>
    private static Vector2 EdgePoint(Vector2 center, Vector2 half, Vector2 dir)
    {
        var tx = Mathf.Abs(dir.X) > 0.0001f ? half.X / Mathf.Abs(dir.X) : float.PositiveInfinity;
        var ty = Mathf.Abs(dir.Y) > 0.0001f ? half.Y / Mathf.Abs(dir.Y) : float.PositiveInfinity;
        return center + dir * Mathf.Min(tx, ty);
    }
}
