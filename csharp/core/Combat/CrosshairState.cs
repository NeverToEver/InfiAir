namespace InfiAir.Core.Combat;

/// <summary>
/// 准星交战反馈状态机（纯逻辑，零 Godot 依赖）：三态
/// <see cref="Phase.Idle"/>（无目标）→ <see cref="Phase.Hostile"/>（准星压住任一可打目标，
/// 出弹即命中）→ <see cref="Phase.Locked"/>（准星入辅助瞄准标记目标的框，强追踪已生效）。
/// 可攻击 = 变色混合；锁定 = 叠加一次性整圈旋转（ease-out）+ 括角收拢视觉，锁定视觉在
/// 目标离开后按退出时长淡出而旋转是一次性承诺（转完即停）。
///
/// 全部时长按模拟时间推进（调用方逐帧喂 delta，帧率/机器性能无关）；锁定态蕴含可攻击态
/// （框包含 ⊃ 碰撞包含，混合各自独立推进故两层反馈互不遮蔽）。旋转防抖：框沿抖动引起的
/// 快速失锁再入锁不重转——上一次旋转转完之前，锁定的上升沿不再触发新旋转。
/// </summary>
public sealed class CrosshairState
{
    public enum Phase { Idle, Hostile, Locked }

    private const float Tau = MathF.PI * 2.0f;
    private const float QuarterTurn = MathF.PI / 2.0f;
    /// <summary>时长下界：0 时长会把除法变成 Inf/NaN 灌进角度与混合（护栏加在构造器）。</summary>
    private const float MinTime = 0.0001f;

    private readonly float _lockTime;
    private readonly float _colorBlendTime;
    private readonly float _releaseFadeTime;

    private Phase _phase = Phase.Idle;
    private float _hostileBlend;
    private float _lockedBlend;
    /// <summary>本次旋转已推进时长；≥ _lockTime 即无旋转进行中（初值无穷 = 从未旋转，且
    /// Inf 在本类全部算式中保持良定义：+dt 仍 Inf、与 lockTime 之比取 min 后为 1）。</summary>
    private float _spinElapsed = float.PositiveInfinity;
    /// <summary>旋转基角（四分圈的整数倍）：四折对称下吸附不可见，保证每次旋转终点都回正。
    /// 初值 −2π 与静止读数里的「已转完整圈」相抵——从未旋转时角度恰读 0（基角恒为四分圈
    /// 整数倍的不变量对 −2π 同样成立）。</summary>
    private float _spinBase = -Tau;
    private bool _markedPrev;

    public CrosshairState(float lockTime, float colorBlendTime, float releaseFadeTime)
    {
        _lockTime = Positive(lockTime);
        _colorBlendTime = Positive(colorBlendTime);
        _releaseFadeTime = Positive(releaseFadeTime);
    }

    /// <summary>当前交战态（属性与枚举同名会撞 CS0102，读口叫 Current）。</summary>
    public Phase Current => _phase;

    /// <summary>可攻击变色混合（0=常态色，1=可攻击色）：锁定态同样推满。</summary>
    public float HostileBlend => _hostileBlend;

    /// <summary>锁定视觉混合（0=无，1=锁定框完全收拢）：进入按 _lockTime，退出按 _releaseFadeTime。</summary>
    public float LockedBlend => _lockedBlend;

    /// <summary>括角收拢进度（ease-out 过的 <see cref="LockedBlend"/>），绘制层据此插值锁定框半宽。</summary>
    public float LockContract => EaseOutCubic(_lockedBlend);

    /// <summary>准星整体旋转角（弧度）：停在四分圈的整数倍上（视觉回正），无漂移。</summary>
    public float SpinAngleRad => _spinBase + Tau * EaseOutCubic(MathF.Min(1.0f, _spinElapsed / _lockTime));

    /// <summary>逐帧推进。<paramref name="markedInFrame"/>（锁定）蕴含 <paramref name="hostileInReach"/>，
    /// 后者只喂 false 时不拖累变色。</summary>
    public void Update(float dt, bool hostileInReach, bool markedInFrame)
    {
        if (dt <= 0.0f)
        {
            return;
        }

        // 旋转是一次性承诺：失锁也照转完；只在锁定的上升沿且上一圈已转完时开新圈
        //（防抖：框沿抖动的快速失锁再入锁不把一次性旋转打成连转）
        _spinElapsed += dt;
        if (markedInFrame && !_markedPrev && _spinElapsed >= _lockTime)
        {
            _spinBase = MathF.Floor(SpinAngleRad / QuarterTurn) * QuarterTurn;
            _spinElapsed = 0.0f;
        }

        _markedPrev = markedInFrame;
        _phase = markedInFrame ? Phase.Locked : hostileInReach ? Phase.Hostile : Phase.Idle;

        var hostileTarget = markedInFrame || hostileInReach;
        _hostileBlend = MoveTowards(_hostileBlend, hostileTarget ? 1.0f : 0.0f, dt / _colorBlendTime);
        _lockedBlend = MoveTowards(
            _lockedBlend, markedInFrame ? 1.0f : 0.0f, dt / (markedInFrame ? _lockTime : _releaseFadeTime));
    }

    private static float Positive(float t) => t >= MinTime ? t : MinTime;

    private static float EaseOutCubic(float p) => 1.0f - (1.0f - p) * (1.0f - p) * (1.0f - p);

    private static float MoveTowards(float current, float target, float maxDelta)
        => current < target ? MathF.Min(current + maxDelta, target) : MathF.Max(current - maxDelta, target);
}
