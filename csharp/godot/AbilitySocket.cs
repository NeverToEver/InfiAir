using Godot;

namespace InfiAir;

/// <summary>
/// 能力充能槽（相位冲刺 / 弧光弹反共用，同一构件两个实例）：切角瓦片 + 中心功能字形 + 外圈充能环。
///
/// 形态依据——冲刺与弹反是同一类量：**充满即可用、用掉清空、冷却回充**（循环充能），
/// 不是需要读刻度的连续量。故用环形冷却（动作游戏 / MOBA 的能力冷却通用语汇：
/// 一眼只需回答「能不能用」），不用指针表盘——指针读作「模拟量测」，会诱导玩家去读数而非判断可用性。
/// 两者共用同一构件、只换字形与身份色，保证仪表盘只有一个「方形构件」语汇而不是每元素一套隐喻。
///
/// 三态（状态即读数，不看数值也能判断）：
///   Charging＝字形压暗 + 环按进度填充；Ready＝字形点亮 + 满环 + 一次性外扩脉冲；Locked＝整体压暗 + 锁定横杠。
/// 静止零逐帧（仅就绪脉冲与充能追赶期间推进）；脉冲按 reduce_flash 递减。
/// Control 子类，_Draw 程序化绘制（文件名与类型同名）。
/// </summary>
public partial class AbilitySocket : Control
{
    /// <summary>字形种类：决定中心画什么（含义靠形状区分，不靠颜色）。</summary>
    public enum Glyph
    {
        /// <summary>相位冲刺：三重右向箭头（位移语义）。</summary>
        Dash,

        /// <summary>弧光弹反：开口弧盾 + 反弹回来的弹体点（反射语义）。</summary>
        Parry,
    }

    /// <summary>切角尺寸（px）：与全站面板同语汇的小构件。</summary>
    private const float Chamfer = 8.0f;

    /// <summary>充能环线宽（px）。</summary>
    private const float RingWidth = 3.0f;

    /// <summary>充能环离瓦片边缘的内缩（px）。</summary>
    private const float RingInset = 4.0f;

    /// <summary>就绪脉冲时长（秒）与最大外扩半径（px）：一次性，不常驻。</summary>
    private const float ReadyPulseTime = 0.42f;
    private const float ReadyPulseGrow = 9.0f;

    /// <summary>充能追赶速率（每秒，比例收敛）：让环平滑追上而非随 0.1s 轮询跳格。</summary>
    private const float ChargeEaseRate = 8.0f;

    private const float Epsilon = 0.004f;

    private Glyph _kind = Glyph.Dash;
    private Color _accent = UITheme.Accent;
    private float _target = 1.0f;
    private float _shown = 1.0f;
    private bool _ready = true;
    private bool _locked;
    private bool _reduceFlash;
    private float _pulse = -1.0f;

    // ---- 绘制顶点缓冲（实例级一次分配）----
    // 充能追赶与就绪脉冲期间本控件逐帧重绘，_Draw 内不得再 new 顶点数组
    // （与 FuelTank 同款纪律）；容量由切角点数固定（八边形 + 回起点），绘制时只原地改写。
    // Draw* 调用在调色器入队时即复制数据，复用同一缓冲安全（同一缓冲不得跨两次 Draw 调用并存）。
    private readonly Vector2[] _chamfer = new Vector2[8];   // 瓦片切角八边形
    private readonly Vector2[] _frameLoop = new Vector2[9]; // 瓦片闭合描边（八边形 + 回起点）

    /// <summary>字形种类与身份色（装配时一次设定；两者共同构成该槽的身份）。</summary>
    public void Configure(Glyph kind, Color accent)
    {
        _kind = kind;
        _accent = accent;
        QueueRedraw();
    }

    public override void _Ready()
    {
        _shown = _target;
        SetProcess(false);
    }

    /// <summary>充能进度（0..1）：环的填充来源。</summary>
    public void SetRatio(float ratio)
    {
        var next = Mathf.Clamp(ratio, 0.0f, 1.0f);
        if (Mathf.Abs(next - _target) < Epsilon)
        {
            return;
        }

        _target = next;
        SetProcess(true);
    }

    /// <summary>就绪态（满格可用）：点亮字形与满环；由未就绪转就绪时播一次性脉冲。</summary>
    public void SetReady(bool ready)
    {
        if (ready == _ready)
        {
            return;
        }

        _ready = ready;
        if (ready && !_reduceFlash)
        {
            _pulse = 0.0f; // 就绪瞬间的确认反馈：外扩一圈后收紧，此后不再动
            SetProcess(true);
        }

        QueueRedraw();
    }

    /// <summary>未解锁（如未取相位冲刺增幅）：整体压暗 + 锁定横杠，与「充能中」明确区分。</summary>
    public void SetLocked(bool locked)
    {
        if (locked == _locked)
        {
            return;
        }

        _locked = locked;
        SetProcess(true);
        QueueRedraw();
    }

    /// <summary>无障碍：关就绪脉冲（就绪仍以亮度与满环表达，不靠闪）。</summary>
    public void SetReduceFlash(bool reduce)
    {
        if (reduce == _reduceFlash)
        {
            return;
        }

        _reduceFlash = reduce;
        if (reduce)
        {
            _pulse = -1.0f;
        }

        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        var busy = false;

        if (Mathf.Abs(_shown - _target) > Epsilon)
        {
            _shown = Mathf.MoveToward(_shown, _target, ChargeEaseRate * d * Mathf.Max(Mathf.Abs(_target - _shown), 0.05f));
            busy = true;
        }
        else
        {
            _shown = _target;
        }

        if (_pulse >= 0.0f)
        {
            _pulse += d / ReadyPulseTime;
            if (_pulse >= 1.0f)
            {
                _pulse = -1.0f;
            }
            else
            {
                busy = true;
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

    public override void _Draw()
    {
        if (!UITheme.FillChamferPoints(_chamfer, Size, Chamfer))
        {
            return;
        }

        var center = Size * 0.5f;
        var lit = _ready && !_locked;
        var glyphAlpha = _locked ? 0.22f : (lit ? 1.0f : 0.34f);

        // 瓦片底：未就绪更暗（读数靠亮度层级，不靠边框粗细）
        var bgAlpha = lit ? 0.30f : 0.16f;
        DrawColoredPolygon(_chamfer, new Color(_accent, bgAlpha));
        var borderCol = _locked
            ? new Color(UITheme.TextDim, 0.35f)
            : new Color(lit ? _accent : new Color(UITheme.TextDim, 1.0f), lit ? 0.9f : 0.5f);
        DrawPolyline(ClosedFrame(), borderCol, 1.0f, true);

        DrawGlyph(center, glyphAlpha);

        // 充能环：从正上方顺时针填充；未解锁不画环（无充能语义）
        if (!_locked)
        {
            DrawChargeRing(center);
        }

        if (_locked)
        {
            DrawLockBar(center);
        }

        if (_pulse >= 0.0f)
        {
            DrawPulse(center);
        }
    }

    /// <summary>充能环：暗轨 + 亮填充弧（填充从正上方顺时针起，符合冷却惯例）。</summary>
    private void DrawChargeRing(Vector2 center)
    {
        var radius = Mathf.Min(Size.X, Size.Y) * 0.5f - RingInset;
        if (radius <= 2.0f)
        {
            return;
        }

        var from = -Mathf.Pi * 0.5f;
        DrawArc(center, radius, from, from + Mathf.Tau, 32, new Color(UITheme.SlotDark, 0.9f), RingWidth, true);
        if (_shown > 0.004f)
        {
            DrawArc(center, radius, from, from + Mathf.Tau * _shown, 32,
                _ready ? new Color(_accent.Lightened(0.25f), 0.95f) : new Color(_accent, 0.6f), RingWidth, true);
        }
    }

    /// <summary>就绪确认脉冲：一圈外扩并淡出（一次性），不参与常态循环（只走圆弧，不参与切角几何）。</summary>
    private void DrawPulse(Vector2 center)
    {
        var radius = Mathf.Min(Size.X, Size.Y) * 0.5f + 1.0f + ReadyPulseGrow * _pulse;
        var alpha = (1.0f - _pulse) * 0.5f;
        DrawArc(center, radius, 0.0f, Mathf.Tau, 28, new Color(_accent, alpha), 1.5f, true);
    }

    /// <summary>闭合环（首点补到末尾）写入并返回实例缓冲，零分配。</summary>
    private Vector2[] ClosedFrame()
    {
        System.Array.Copy(_chamfer, _frameLoop, _chamfer.Length);
        _frameLoop[^1] = _chamfer[0];
        return _frameLoop;
    }

    /// <summary>锁定横杠：一道压在字形上的短横线，与「充能中」的压暗明确区分。</summary>
    private void DrawLockBar(Vector2 center)
    {
        var half = Mathf.Min(Size.X, Size.Y) * 0.16f;
        DrawLine(center - new Vector2(half, 0.0f), center + new Vector2(half, 0.0f),
            new Color(UITheme.TextDim, 0.75f), 2.0f, true);
    }

    /// <summary>功能字形（形状即语义）：冲刺＝三重右向箭头；弹反＝开口弧盾 + 回弹点。
    /// 可用半径扣掉环与内缩后已很小，故字形按「占满可用圆」画，不再二次收缩。</summary>
    private void DrawGlyph(Vector2 center, float alpha)
    {
        var col = new Color(_locked ? UITheme.TextDim : _accent.Lightened(_ready ? 0.45f : 0.1f), alpha);
        var r = Mathf.Min(Size.X, Size.Y) * 0.5f - RingInset - RingWidth - 1.0f;
        if (r <= 3.0f)
        {
            return;
        }

        if (_kind == Glyph.Dash)
        {
            DrawDashGlyph(center, r, col);
        }
        else
        {
            DrawParryGlyph(center, r, col);
        }
    }

    /// <summary>冲刺：三重右向箭尖，铺满可用圆（间距与高度由半径推导，不写死像素）。</summary>
    private void DrawDashGlyph(Vector2 center, float r, Color col)
    {
        var span = r * 1.5f;
        var height = r * 1.0f;
        var step = span / 2.0f;
        for (var i = 0; i < 3; i++)
        {
            var x = center.X - span * 0.5f + step * i;
            var tip = new Vector2(x + height * 0.62f, center.Y);
            DrawLine(new Vector2(x, center.Y - height * 0.5f), tip, col, 2.0f, true);
            DrawLine(new Vector2(x, center.Y + height * 0.5f), tip, col, 2.0f, true);
        }
    }

    /// <summary>弹反：开口弧盾（缺口朝右上）+ 沿缺口弹出的回弹点（读作「把弹体挡回去」）。</summary>
    private void DrawParryGlyph(Vector2 center, float r, Color col)
    {
        var radius = r * 0.82f;
        DrawArc(center, radius, Mathf.DegToRad(-34.0f), Mathf.DegToRad(196.0f), 24, col, 2.4f, true);
        // 回弹点：自盾心沿缺口方向弹出，配一段短划读作反弹轨迹
        var dir = Vector2.Right.Rotated(Mathf.DegToRad(-40.0f));
        DrawLine(center + dir * (radius * 0.30f), center + dir * (radius * 1.20f), col, 2.0f, true);
        DrawCircle(center + dir * (radius * 1.34f), Mathf.Max(r * 0.17f, 1.6f), col);
    }
}
