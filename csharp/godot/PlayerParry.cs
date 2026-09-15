using Godot;
using InfiAir.Core.Combat;

namespace InfiAir;

/// <summary>
/// 弧光弹反盾组件：时间轴与硬冷却判定下沉 <see cref="ParryTimeline"/>（csharp/core/Combat，
/// 回归面在 csharp/tests/InfiAir.Core.Tests/Combat/ParryTimelineTests.cs），本类只做适配——
/// 注入 balance 数值、转发相位/进度查询供 Player 与 HUD 读取。
/// 盾 Area2D 的判定启停由 Player._PhysicsProcess 按 Phase 延迟写（不在物理 in/out 回调栈内直写）。
/// 暂停随玩家 process_mode 冻结（流程/冷却计时同步暂停）。
/// 纯 C# 逻辑类（原 RefCounted、无信号/导出）：由 C# Player 组合持有；无 GameState 访问。
/// </summary>
public partial class PlayerParry : RefCounted
{
    public enum ParryPhase
    {
        IDLE,
        WINDUP,
        ACTIVE,
        RECOVER,
    }

    private readonly ParryTimeline _timeline = new();

    /// <summary>时间轴相位（Player 侧据此启用盾判定；HUD 与探针按序数读）。</summary>
    public ParryPhase Phase
    {
        get => (ParryPhase)(int)_timeline.Phase;
        set => _timeline.Phase = (Core.Combat.ParryPhase)(int)value;
    }

    /// <summary>当前相位内已推进时长（秒）。</summary>
    public float FlowTimer
    {
        get => _timeline.FlowTimer;
        set => _timeline.FlowTimer = value;
    }

    /// <summary>硬冷却剩余（秒，自流程结束起算）。</summary>
    public float Cooldown
    {
        get => _timeline.Cooldown;
        set => _timeline.Cooldown = value;
    }

    /// <summary>完整流程时长（秒）= 前摇 + 有效 + 后摇。</summary>
    public float Duration => _timeline.Duration;

    /// <summary>有效弹反窗口（秒，居中）。</summary>
    public float ActiveTime => _timeline.ActiveTime;

    /// <summary>硬冷却（秒，自流程结束起算）。</summary>
    public float CooldownMax => _timeline.CooldownMax;

    public void Configure(float pDuration, float pActiveTime, float pCooldown)
        => _timeline.Configure(pDuration, pActiveTime, pCooldown, CfgFx.IntervalFloor);

    public bool IsFlowing() => _timeline.IsFlowing();

    public float CooldownRemaining() => _timeline.CooldownRemaining();

    /// <summary>HUD 能量槽比例：满格=可用；流程期清空；流程结束起按 COOLDOWN 匀速充能回满。</summary>
    public float EnergyRatio() => _timeline.EnergyRatio();

    /// <summary>机身金色 tint 强度（0..1）：WINDUP 渐强 → ACTIVE 保持 → RECOVER 渐弱 → IDLE 0。</summary>
    public float TintStrength() => _timeline.TintStrength();

    /// <summary>盾视觉展开进度（WINDUP 小弧展开到全弧，ACTIVE/RECOVER 全弧，IDLE 0）。</summary>
    public float ShieldExpand() => _timeline.ShieldExpand();

    /// <summary>珍珠流光扫过进度（ACTIVE 期 0→1；其余阶段 0）。</summary>
    public float ShineProgress() => _timeline.ShineProgress();

    /// <summary>尝试启动：仅 IDLE 且冷却结束可启动（Player 门面已校验输入）。</summary>
    public bool TryStart() => _timeline.TryStart();

    /// <summary>流程推进（Player._PhysicsProcess 每帧调用）：IDLE 期冷却递减；流程期按相位时长推进。</summary>
    public void Tick(float delta) => _timeline.Tick(delta);

    // 相位值静态访问器（int 口径；当前无生产调用方，保留为公开查询口）
    public static int GetPhaseIdle() => (int)ParryPhase.IDLE;

    public static int GetPhaseWindup() => (int)ParryPhase.WINDUP;

    public static int GetPhaseActive() => (int)ParryPhase.ACTIVE;

    public static int GetPhaseRecover() => (int)ParryPhase.RECOVER;
}
