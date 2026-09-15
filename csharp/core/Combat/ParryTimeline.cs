namespace InfiAir.Core.Combat;

/// <summary>弧光弹反盾的时间轴相位（顺序即引擎侧 HUD/探针读取的序数，不得重排）。</summary>
public enum ParryPhase
{
    /// <summary>待机：不判定，冷却递减中。</summary>
    Idle = 0,

    /// <summary>前摇：不判定。</summary>
    Windup = 1,

    /// <summary>有效弹反窗口：引擎侧据此启用盾 Area2D 判定。</summary>
    Active = 2,

    /// <summary>后摇：不判定。</summary>
    Recover = 3,
}

/// <summary>
/// 弧光弹反盾的时间轴与硬冷却（纯逻辑，零 Godot 依赖）。
/// IDLE → WINDUP（前摇，无判定）→ ACTIVE（有效弹反）→ RECOVER（后摇，无判定）→ IDLE；
/// 硬冷却自 RECOVER 完成（进入 IDLE）起算——完整周期 0.8 + 3.0 = 3.8s，占空比约 21%，
/// 盾是「决策性资源」而非常驻免伤。数值由引擎侧注入（balance.json player.parry），core 不产生取值。
/// 原先整块判定留在 PlayerParry 节点里，引擎冒烟只判「不崩」；两种坏法都静默：相位边界少了
/// epsilon 容差会永久停在边界相位（盾再也进不了有效窗口），冷却起点挪到流程开始则占空比翻倍。
/// </summary>
public sealed class ParryTimeline
{
    /// <summary>相位边界容差：浮点累加不精确落在 0.15/0.5 上（如 10 帧 × 0.014995 = 0.14995）
    /// 会让相位卡在边界，容差必须大于累加误差、又小于一整帧。</summary>
    public const float BoundaryEpsilon = 0.0001f;

    /// <summary>时间轴相位（引擎侧据此启用盾判定）。</summary>
    public ParryPhase Phase { get; set; } = ParryPhase.Idle;

    /// <summary>当前相位内已推进时长（秒）。</summary>
    public float FlowTimer { get; set; }

    /// <summary>硬冷却剩余（秒，自流程结束起算）。</summary>
    public float Cooldown { get; set; }

    /// <summary>完整流程时长（秒）= 前摇 + 有效 + 后摇。</summary>
    public float Duration { get; private set; } = 0.8f;

    /// <summary>有效弹反窗口（秒，居中）。</summary>
    public float ActiveTime { get; private set; } = 0.5f;

    /// <summary>硬冷却（秒，自流程结束起算）。</summary>
    public float CooldownMax { get; private set; } = 3.0f;

    /// <summary>数值注入（引擎侧传入 balance.json 取值与相位下限；下限口径由调用方持有，
    /// 见 <c>CfgFx.IntervalFloor</c>）。</summary>
    public void Configure(float duration, float activeTime, float cooldown, float phaseFloor)
    {
        ActiveTime = Clamp(activeTime, phaseFloor, Max(duration, phaseFloor));
        Duration = Max(duration, ActiveTime);
        CooldownMax = Max(cooldown, 0.0f);
    }

    public bool IsFlowing() => Phase != ParryPhase.Idle;

    public float CooldownRemaining() => Cooldown;

    /// <summary>HUD 能量槽比例：满格=可用；流程期清空；流程结束起按冷却匀速充能回满。</summary>
    public float EnergyRatio()
    {
        if (IsFlowing())
        {
            return 0.0f;
        }

        if (CooldownMax <= 0.0f)
        {
            return 1.0f;
        }

        return 1.0f - Clamp(Cooldown / CooldownMax, 0.0f, 1.0f);
    }

    /// <summary>机身金色 tint 强度（0..1）：WINDUP 渐强 → ACTIVE 保持 → RECOVER 渐弱 → IDLE 0。</summary>
    public float TintStrength()
    {
        switch (Phase)
        {
            case ParryPhase.Windup:
                return PhaseProgress();
            case ParryPhase.Active:
                return 1.0f;
            case ParryPhase.Recover:
                return 1.0f - PhaseProgress();
            default:
                return 0.0f;
        }
    }

    /// <summary>盾视觉展开进度（WINDUP 小弧展开到全弧，ACTIVE/RECOVER 全弧，IDLE 0）。</summary>
    public float ShieldExpand()
    {
        switch (Phase)
        {
            case ParryPhase.Windup:
                return PhaseProgress();
            case ParryPhase.Active:
            case ParryPhase.Recover:
                return 1.0f;
            default:
                return 0.0f;
        }
    }

    /// <summary>珍珠流光扫过进度（ACTIVE 期 0→1，自弧线左端扫至右端；其余阶段 0）。</summary>
    public float ShineProgress() => Phase == ParryPhase.Active ? PhaseProgress() : 0.0f;

    /// <summary>尝试启动：仅 IDLE 且冷却结束可启动（调用方已校验输入）。</summary>
    public bool TryStart()
    {
        if (Phase != ParryPhase.Idle || Cooldown > 0.0f)
        {
            return false;
        }

        Phase = ParryPhase.Windup;
        FlowTimer = 0.0f;
        return true;
    }

    /// <summary>流程推进（每物理帧调用）：IDLE 期冷却递减；流程期按相位时长推进。
    /// 每帧至多推进一个相位（与帧长无关的相位机，不是「按经过时间补相位」）。</summary>
    public void Tick(float delta)
    {
        if (Phase == ParryPhase.Idle)
        {
            Cooldown = Max(Cooldown - delta, 0.0f);
            return;
        }

        FlowTimer += delta;
        var half = (Duration - ActiveTime) / 2.0f;
        if (Phase == ParryPhase.Windup && FlowTimer >= half - BoundaryEpsilon)
        {
            Phase = ParryPhase.Active;
            FlowTimer = 0.0f;
        }
        else if (Phase == ParryPhase.Active && FlowTimer >= ActiveTime - BoundaryEpsilon)
        {
            Phase = ParryPhase.Recover;
            FlowTimer = 0.0f;
        }
        else if (Phase == ParryPhase.Recover && FlowTimer >= half - BoundaryEpsilon)
        {
            Phase = ParryPhase.Idle;
            FlowTimer = 0.0f;
            Cooldown = CooldownMax; // 硬冷却自流程结束（RECOVER 完成）起算
        }
    }

    /// <summary>当前阶段进度（0..1，按阶段时长归一；前摇/后摇共用半程口径）。</summary>
    private float PhaseProgress()
    {
        switch (Phase)
        {
            case ParryPhase.Windup:
            case ParryPhase.Recover:
                {
                    var half = (Duration - ActiveTime) / 2.0f;
                    return Clamp(FlowTimer / Max(half, 0.001f), 0.0f, 1.0f);
                }

            case ParryPhase.Active:
                return Clamp(FlowTimer / Max(ActiveTime, 0.001f), 0.0f, 1.0f);
            default:
                return 0.0f;
        }
    }

    // 逐位等价于引擎侧的 Mathf.Clamp/Max（Godot 实现是先后比较、NaN 原样传递；
    // 换成 System.Math.Clamp(Max(Min)) 会在 NaN 上分叉）。
    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static float Max(float a, float b) => a > b ? a : b;
}
