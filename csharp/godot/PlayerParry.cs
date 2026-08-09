using Godot;

namespace InfiAir;

/// <summary>
/// 弧光弹反盾组件（M3c 全量迁移，2026-08-08 自 scripts/player_parry.gd 迁移；2026-08-03 公平感
/// 机制四，docs/archive/2026-08-03-combat-fairness-plan.md §5）。时间轴状态机
/// IDLE → WINDUP(前摇，无判定) → ACTIVE(有效弹反) → RECOVER(后摇，无判定) → IDLE；
/// 硬冷却 3.0s 自 RECOVER 完成（进入 IDLE）起算——完整周期 0.8 + 3.0 = 3.8s，占空比约 21%，
/// 盾是「决策性资源」而非常驻免伤。仅 ACTIVE 期由 Player 侧启用盾 Area2D 判定。
/// 暂停随玩家 process_mode 冻结（流程/冷却计时同步暂停）。数值经 Player._load_balance 注入。
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

    /// <summary>时间轴相位（Player 侧据此启用盾判定；parry_test 白盒读）。</summary>
    public ParryPhase Phase { get; set; } = ParryPhase.IDLE;

    /// <summary>当前相位内已推进时长（秒）。</summary>
    public float FlowTimer { get; set; }

    /// <summary>硬冷却剩余（秒，自流程结束起算）。</summary>
    public float Cooldown { get; set; }

    // ---- 数值配置（Player._load_balance 经 Configure 注入；与脚本默认值一致） ----
    /// <summary>完整流程时长（秒）= 前摇 + 有效 + 后摇。</summary>
    public float Duration { get; private set; } = 0.8f;

    /// <summary>有效弹反窗口（秒，居中）。</summary>
    public float ActiveTime { get; private set; } = 0.5f;

    /// <summary>硬冷却（秒，自流程结束起算）。</summary>
    public float CooldownMax { get; private set; } = 3.0f;

    public void Configure(float pDuration, float pActiveTime, float pCooldown)
    {
        ActiveTime = Mathf.Clamp(pActiveTime, 0.05f, Mathf.Max(pDuration, 0.05f));
        Duration = Mathf.Max(pDuration, ActiveTime);
        CooldownMax = Mathf.Max(pCooldown, 0.0f);
    }

    public bool IsFlowing() => Phase != ParryPhase.IDLE;

    public float CooldownRemaining() => Cooldown;

    /// <summary>HUD 能量槽比例：满格=可用；流程期清空；流程结束起按 COOLDOWN 匀速充能回满。</summary>
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

        return 1.0f - Mathf.Clamp(Cooldown / CooldownMax, 0.0f, 1.0f);
    }

    /// <summary>机身金色 tint 强度（0..1）：WINDUP 渐强 → ACTIVE 保持 → RECOVER 渐弱 → IDLE 0。</summary>
    public float TintStrength()
    {
        switch (Phase)
        {
            case ParryPhase.WINDUP:
                return PhaseProgress();
            case ParryPhase.ACTIVE:
                return 1.0f;
            case ParryPhase.RECOVER:
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
            case ParryPhase.WINDUP:
                return PhaseProgress();
            case ParryPhase.ACTIVE:
            case ParryPhase.RECOVER:
                return 1.0f;
            default:
                return 0.0f;
        }
    }

    /// <summary>珍珠流光扫过进度（ACTIVE 期 0→1，自弧线左端扫至右端；其余阶段 0）。</summary>
    public float ShineProgress()
    {
        if (Phase == ParryPhase.ACTIVE)
        {
            return PhaseProgress();
        }

        return 0.0f;
    }

    /// <summary>尝试启动：仅 IDLE 且冷却结束可启动（Player 门面已校验输入）。</summary>
    public bool TryStart()
    {
        if (Phase != ParryPhase.IDLE || Cooldown > 0.0f)
        {
            return false;
        }

        Phase = ParryPhase.WINDUP;
        FlowTimer = 0.0f;
        return true;
    }

    /// <summary>流程推进（Player._physics_process 每帧调用）：IDLE 期冷却递减；流程期按相位时长推进。
    /// 相位边界用 epsilon 容差（浮点累加不精确落在 0.15/0.5 上会卡在边界相位）。</summary>
    public void Tick(float delta)
    {
        if (Phase == ParryPhase.IDLE)
        {
            Cooldown = Mathf.Max(Cooldown - delta, 0.0f);
            return;
        }

        FlowTimer += delta;
        var half = (Duration - ActiveTime) / 2.0f;
        const float Eps = 0.0001f;
        if (Phase == ParryPhase.WINDUP && FlowTimer >= half - Eps)
        {
            Phase = ParryPhase.ACTIVE;
            FlowTimer = 0.0f;
        }
        else if (Phase == ParryPhase.ACTIVE && FlowTimer >= ActiveTime - Eps)
        {
            Phase = ParryPhase.RECOVER;
            FlowTimer = 0.0f;
        }
        else if (Phase == ParryPhase.RECOVER && FlowTimer >= half - Eps)
        {
            Phase = ParryPhase.IDLE;
            FlowTimer = 0.0f;
            Cooldown = CooldownMax; // 硬冷却自流程结束（RECOVER 完成）起算
        }
    }

    /// <summary>当前阶段进度（0..1，按阶段时长归一）。</summary>
    private float PhaseProgress()
    {
        switch (Phase)
        {
            case ParryPhase.WINDUP:
            case ParryPhase.RECOVER:
                {
                    var half = (Duration - ActiveTime) / 2.0f;
                    return Mathf.Clamp(FlowTimer / Mathf.Max(half, 0.001f), 0.0f, 1.0f);
                }

            case ParryPhase.ACTIVE:
                return Mathf.Clamp(FlowTimer / Mathf.Max(ActiveTime, 0.001f), 0.0f, 1.0f);
            default:
                return 0.0f;
        }
    }

    // GDScript 无法以类名引用 C# 嵌套枚举（实测）——相位值经静态方法访问（脚本资源可调）
    public static int GetPhaseIdle() => (int)ParryPhase.IDLE;

    public static int GetPhaseWindup() => (int)ParryPhase.WINDUP;

    public static int GetPhaseActive() => (int)ParryPhase.ACTIVE;

    public static int GetPhaseRecover() => (int)ParryPhase.RECOVER;

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public void tick(float delta) => Tick(delta);

    public float cooldown { get => Cooldown; set => Cooldown = value; }

    public float DURATION { get => Duration; set => Duration = value; }
}
