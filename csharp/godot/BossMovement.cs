using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// Boss 走位策略（M3 批次迁移，2026-08-08 自 scripts/boss_movement.gd 迁移；docs/AUDIT_VAULT.md A3）。
/// 四型移动（strafe / dash / bulwark 纵向下压 / 月蚀中心微摆）与移动状态；写 boss.position（Node2D 公开属性），
/// 经 boss 公开查询（slow_factor/strafe_range/is_enraged/fight_phase）交互，不访问私有字段（A1 约束）。
/// Boss 为 C# 类；访问经动态派发（Get/Call，StringName 静态缓存避免每帧字面量分配——U10 已消除
/// 每帧构造，剩余动态派发频率 1 次/物理帧/Boss，收益低登记不修，typed 化留待 Boss API 重构一并做）。
/// Enemy.SinFast 查表静态直接调用。纯 C# 类（原 RefCounted）。
/// </summary>
public partial class BossMovement : RefCounted
{
    /// <summary>对齐 Boss.FightPhase.P1（enum FightPhase { P1, P2, ENRAGE }）。</summary>
    private const int FightP1 = 0;

    /// <summary>L14：段切换 y 平滑过渡时长（ease-out）。</summary>
    private const float BobSmoothTime = 0.6f;

    /// <summary>一型 P1 纵向下压窗口（周期末段 1.6s）。</summary>
    private const float PressWindow = 1.6f;

    // boss 动态成员 StringName 静态缓存（_physics_process 每帧访问，热路径禁字面量分配）
    private static readonly StringName PropBossType = new("boss_type");
    private static readonly StringName PropPosition = new("position");
    private static readonly StringName PropStrafeSpeeds = new("STRAFE_SPEEDS");
    private static readonly StringName PropPressInterval = new("PRESS_INTERVAL");
    private static readonly StringName PropPressDepth = new("PRESS_DEPTH");
    private static readonly StringName PropType1P2Strafe = new("TYPE1_P2_STRAFE");
    private static readonly StringName PropType1P2BobAmp = new("TYPE1_P2_BOB_AMP");
    private static readonly StringName PropType1P2BobPeriod = new("TYPE1_P2_BOB_PERIOD");
    private static readonly StringName PropType2P2DashTime = new("TYPE2_P2_DASH_TIME");
    private static readonly StringName PropType2P2RestTime = new("TYPE2_P2_REST_TIME");
    private static readonly StringName PropType3P1BobMin = new("TYPE3_P1_BOB_MIN");
    private static readonly StringName PropType3P1BobMax = new("TYPE3_P1_BOB_MAX");
    private static readonly StringName PropType3P1BobPeriod = new("TYPE3_P1_BOB_PERIOD");
    private static readonly StringName PropType3P2Strafe = new("TYPE3_P2_STRAFE");
    private static readonly StringName PropType3P2BobAmp = new("TYPE3_P2_BOB_AMP");
    private static readonly StringName PropType3P2BobPeriod = new("TYPE3_P2_BOB_PERIOD");
    private static readonly StringName PropMove4BobAmp = new("MOVE4_BOB_AMP");
    private static readonly StringName PropMove4BobPeriod = new("MOVE4_BOB_PERIOD");
    private static readonly StringName PropEnrageSpeedMult = new("ENRAGE_SPEED_MULT");
    private static readonly StringName MethFightPhase = new("fight_phase");
    private static readonly StringName MethStrafeRange = new("strafe_range");
    private static readonly StringName MethSlowFactor = new("slow_factor");
    private static readonly StringName MethIsEnraged = new("is_enraged");
    private static readonly StringName MethFightAnchorY = new("fight_anchor_y");
    private static readonly StringName MethEscapeDriftOffset = new("escape_drift_offset");

    private float _strafeDir = 1.0f;
    private float _moveTimer;
    private bool _dashing;
    private float _pressTimer = 6.0f;
    private float _pressOffset;
    /// <summary>纵向正弦相位累计（段切换归零，sin 0 = 0 无跳变）。</summary>
    private float _bobPhase;
    /// <summary>三型 P1 下压周期计时（独立于 press）。</summary>
    private float _bandTimer;
    /// <summary>三型 P1 下压偏移（target 从 0 起步，无初始跳变）。</summary>
    private float _bandOffset;
    // L14：段切换 y 平滑过渡——P1 增量式下压（一型 press / 三型 band）的当前偏移未补偿，
    // P2 绝对赋值锚线会瞬间跳变（三型可达 ~280px）；切换后从当前 y 平滑追锚线（ease-out）
    private float _bobSmoothT;
    private float _bobSmoothFrom;

    /// <summary>A3 收敛：机型移动器注册表（boss_type → 移动策略方法，构造函数装配）。
    /// 新增机型只需注册一行 + 一个策略方法，不再改 update 的分发（O 原则达成）。</summary>
    private readonly Dictionary<int, System.Action<float, GodotObject>> _movers;

    public BossMovement()
    {
        _movers = new Dictionary<int, System.Action<float, GodotObject>>
        {
            { 1, MoveType1 },
            { 2, MoveType2 },
            { 3, MoveType3 },
            { 4, MoveType4 },
        };
    }

    /// <summary>同步下压周期初始值（Boss._ready 在 PRESS_INTERVAL 从 balance 覆盖后调用，保持精确一致）。</summary>
    public void SyncPressTimer(float interval) => _pressTimer = interval;

    /// <summary>C11 + L14：段切换（P1→P2）时归零下压偏移——若切换恰落在下压窗口内，
    /// _press_offset/_band_offset 保留非零值而 _update_press/_move_band 不再被调用，
    /// 机身会以偏移永久留在锚线下方（C11 原只清 press，L14 补清三型 band）。</summary>
    public void ResetPress()
    {
        _pressOffset = 0.0f; // 仅清偏移，保留下压周期相位（_press_timer 不动）
        _bandOffset = 0.0f; // L14：三型 band 同族清理
        _bobPhase = 0.0f; // D05：段切换归零纵向正弦（sin 0 = 0 平滑衔接锚线）
    }

    /// <summary>L14：段切换入口——记录当前 y 作为平滑过渡起点（由 boss._enter_phase 在切换帧调用）。
    /// 不在此处直接写 y（走位由各 mover 每帧驱动），过渡在 _move_bob 内收敛到锚线正弦轨迹。</summary>
    public void BeginBobSmooth(float currentY)
    {
        _bobSmoothT = BobSmoothTime;
        _bobSmoothFrom = currentY;
    }

    /// <summary>每物理帧驱动：注册表分发（非法 boss_type 回退一型）。</summary>
    public void Update(float delta, GodotObject boss)
    {
        if (!_movers.TryGetValue((int)boss.Get(PropBossType).AsInt64(), out var mover))
        {
            MoveType1(delta, boss); // K13：非法 boss_type（防御，正常路径恒 1..4）回退一型走位，防非法值下完全静止
            return;
        }

        mover(delta, boss);
    }

    /// <summary>注册表完整性查询（A3 架构断言测试经公开接口访问）。</summary>
    public bool HasMover(int type) => _movers.ContainsKey(type);

    // ---------------- 内部实现 ----------------

    /// <summary>一型「堡垒」：慢速 strafe + P1 每 6s 纵向下压 80px 再回（§5.1）。</summary>
    private void MoveType1(float delta, GodotObject boss)
    {
        var phase = (int)boss.Call(MethFightPhase).AsInt64();
        if (phase == FightP1)
        {
            MoveStrafe(delta, boss, StrafeSpeed(boss, 0));
            UpdatePress(delta, boss);
        }
        else if (phase == 1) // FightPhase.P2
        {
            // D05：strafe 提速 + 纵向正弦往复
            MoveStrafe(delta, boss, (float)boss.Get(PropType1P2Strafe).AsDouble());
            MoveBob(
                delta, boss,
                (float)boss.Get(PropType1P2BobAmp).AsDouble(),
                (float)boss.Get(PropType1P2BobPeriod).AsDouble());
        }
        else // ENRAGE：狂暴轨道由 enrage_sequence 接管，走位维持现状
        {
            MoveStrafe(delta, boss, StrafeSpeed(boss, 0));
        }
    }

    /// <summary>二型「游击」：周期性冲刺换向（偏向屏幕中心，避免长期贴边）。</summary>
    private void MoveType2(float delta, GodotObject boss) => MoveDash(delta, boss);

    /// <summary>三型「母舰」：P1 缓慢下压/回升 + P2 提速正弦（§5.3）。</summary>
    private void MoveType3(float delta, GodotObject boss)
    {
        var phase = (int)boss.Call(MethFightPhase).AsInt64();
        if (phase == FightP1)
        {
            // D05：三型 P1 缓慢下压/回升（锚线下 [lo, hi] 区间，周期 9s）
            MoveStrafe(delta, boss, StrafeSpeed(boss, 2));
            MoveBand(
                delta, boss,
                (float)boss.Get(PropType3P1BobMin).AsDouble(),
                (float)boss.Get(PropType3P1BobMax).AsDouble(),
                (float)boss.Get(PropType3P1BobPeriod).AsDouble());
        }
        else if (phase == 1) // FightPhase.P2
        {
            // D05：strafe 提速 + 纵向正弦往复
            MoveStrafe(delta, boss, (float)boss.Get(PropType3P2Strafe).AsDouble());
            MoveBob(
                delta, boss,
                (float)boss.Get(PropType3P2BobAmp).AsDouble(),
                (float)boss.Get(PropType3P2BobPeriod).AsDouble());
        }
        else // ENRAGE 现状
        {
            MoveStrafe(delta, boss, StrafeSpeed(boss, 2));
        }
    }

    /// <summary>4 型「月蚀」（2026-08-04）：中心悬停微摆——不 strafe，纵向小振幅正弦（相位归零平滑衔接锚线）。
    /// Q27（2026-08-05）：正弦峰值速度（AMP×TAU/PERIOD ≈ 78.5px/s）> 原 MOVE4_SPEED 40 时，
    /// move_toward 速度上限把振幅压到 ±15px 且波形低通失真——与 _move_bob 同款直接绝对赋值
    /// （战斗与逃跑警告期独占 y，入场/逃跑/狂暴序列均早退不干扰；MOVE4_SPEED 键已随修复移除）。</summary>
    private void MoveType4(float delta, GodotObject boss)
    {
        _bobPhase += delta * Mathf.Tau / (float)boss.Get(PropMove4BobPeriod).AsDouble();
        // 2026-08-06 审计：绝对 y 赋值叠加逃跑警告期上飘偏移（原赋值覆盖 boss 侧上飘）
        var pos = boss.Get(PropPosition).AsVector2();
        pos.Y = (float)boss.Call(MethFightAnchorY).AsDouble()
            + (float)boss.Get(PropMove4BobAmp).AsDouble() * Enemy.SinFast(_bobPhase)
            + (float)boss.Call(MethEscapeDriftOffset).AsDouble();
        boss.Set(PropPosition, pos);
    }

    /// <summary>一型「堡垒」纵向下压：周期最后 1.6s 窗口内正弦下压再回升（增量式施加，不覆盖逃跑上飘）。</summary>
    private void UpdatePress(float delta, GodotObject boss)
    {
        _pressTimer -= delta;
        var pressInterval = (float)boss.Get(PropPressInterval).AsDouble();
        if (_pressTimer <= 0.0f)
        {
            _pressTimer = pressInterval;
        }

        var elapsed = pressInterval - _pressTimer;
        var target = 0.0f;
        if (elapsed >= pressInterval - PressWindow)
        {
            target = (float)boss.Get(PropPressDepth).AsDouble()
                * Enemy.SinFast(Mathf.Pi * (elapsed - (pressInterval - PressWindow)) / PressWindow);
        }

        var pos = boss.Get(PropPosition).AsVector2();
        pos.Y += target - _pressOffset;
        boss.Set(PropPosition, pos);
        _pressOffset = target;
    }

    /// <summary>纵向正弦（P2 通用，D05）：围绕锚线 ±amp 正弦往复。
    /// 直接设置 y（_in_fight 后才被调用，入场/逃跑/狂暴序列均早退不干扰；fight_anchor_y()
    /// 逐帧求值支持战斗中切视角档）。相位累计驱动，Enemy.SinFast 查表零分配。
    /// L14：段切换后 BOB_SMOOTH_TIME 内从切换前 y 平滑收敛到锚线正弦轨迹（ease-out），
    /// 消除 P1 增量式下压（press/band）残留偏移的瞬间跳变。</summary>
    private void MoveBob(float delta, GodotObject boss, float amp, float period)
    {
        _bobPhase += Mathf.Tau * delta / Mathf.Max(period, 0.01f);
        // 2026-08-06 审计：绝对 y 赋值叠加逃跑警告期上飘偏移（原赋值覆盖 boss 侧上飘，三型无效果）
        var target = (float)boss.Call(MethFightAnchorY).AsDouble()
            + Enemy.SinFast(_bobPhase) * amp
            + (float)boss.Call(MethEscapeDriftOffset).AsDouble();
        if (_bobSmoothT > 0.0f)
        {
            _bobSmoothT -= delta;
            var k = 1.0f - _bobSmoothT / BobSmoothTime;
            k = 1.0f - Mathf.Pow(1.0f - k, 3.0f); // ease-out：先快后慢追锚线（视觉上「回落」而非「漂移」）
            target = Mathf.Lerp(_bobSmoothFrom, target, k);
        }

        var pos = boss.Get(PropPosition).AsVector2();
        pos.Y = target;
        boss.Set(PropPosition, pos);
    }

    /// <summary>三型 P1「缓慢下压/回升」（§5.3）：周期内正弦下压到锚线下 [y_lo, y_hi] 区间再回升。
    /// 与 _update_press 同构（target 为纯偏移、从 0 起步无初始跳变）；wob 慢相位使下压轨迹
    /// 在 [lo, hi] 邻域摆动（9s 慢周期，与模式循环错开）。</summary>
    private void MoveBand(float delta, GodotObject boss, float yLo, float yHi, float period)
    {
        if (_bandTimer <= 0.0f)
        {
            _bandTimer = period;
        }

        _bandTimer -= delta;
        var elapsed = period - _bandTimer;
        var u = Mathf.Clamp(elapsed / period, 0.0f, 1.0f);
        var depth = (yLo + yHi) * 0.5f;
        var wob = (yHi - yLo) * 0.5f;
        var target = depth * Enemy.SinFast(Mathf.Pi * u)
            + wob * Enemy.SinFast(Mathf.Tau * u * 0.5f) * Enemy.SinFast(Mathf.Pi * u);
        var pos = boss.Get(PropPosition).AsVector2();
        pos.Y += target - _bandOffset;
        boss.Set(PropPosition, pos);
        _bandOffset = target;
    }

    /// <summary>水平巡航（各型通用）：速度 × slow_factor × 狂暴余怒倍率，越界翻转并钳回。</summary>
    private void MoveStrafe(float delta, GodotObject boss, float pSpeed)
    {
        var pos = boss.Get(PropPosition).AsVector2();
        pos.X += _strafeDir * pSpeed * (float)boss.Call(MethSlowFactor).AsDouble() * EnrageSpeedMult(boss) * delta;
        var bounds = boss.Call(MethStrafeRange).AsVector2();
        if (pos.X < bounds.X || pos.X > bounds.Y)
        {
            _strafeDir = -_strafeDir;
            pos.X = Mathf.Clamp(pos.X, bounds.X, bounds.Y);
        }

        boss.Set(PropPosition, pos);
    }

    /// <summary>二型「游击」：周期性冲刺换向（偏向屏幕中心，避免长期贴边）。</summary>
    private void MoveDash(float delta, GodotObject boss)
    {
        _moveTimer -= delta;
        if (_moveTimer <= 0.0f)
        {
            _dashing = !_dashing;
            // D05：P2 冲刺更频（0.4s/0.5s）；P1 与 ENRAGE 维持现状（0.5s/0.7s）
            var phase = (int)boss.Call(MethFightPhase).AsInt64();
            var dashT = phase == 1 ? (float)boss.Get(PropType2P2DashTime).AsDouble() : 0.5f;
            var restT = phase == 1 ? (float)boss.Get(PropType2P2RestTime).AsDouble() : 0.7f;
            _moveTimer = _dashing ? dashT : restT;
            if (_dashing)
            {
                // 偏向屏幕中心方向冲刺，避免长期贴边（C14：中心取可见世界，不写死 960）
                var centerX = GameState.Instance.ViewWorldRect().GetCenter().X;
                _strafeDir = GD.Randf() < 0.6f ? Mathf.Sign(centerX - boss.Get(PropPosition).AsVector2().X) : -_strafeDir;
                if (_strafeDir == 0.0f)
                {
                    _strafeDir = 1.0f;
                }
            }
        }

        if (_dashing)
        {
            var pos = boss.Get(PropPosition).AsVector2();
            pos.X += _strafeDir * StrafeSpeed(boss, 1) * (float)boss.Call(MethSlowFactor).AsDouble() * EnrageSpeedMult(boss) * delta;
            var bounds = boss.Call(MethStrafeRange).AsVector2();
            if (pos.X < bounds.X || pos.X > bounds.Y)
            {
                _strafeDir = -_strafeDir;
                pos.X = Mathf.Clamp(pos.X, bounds.X, bounds.Y);
            }

            boss.Set(PropPosition, pos);
        }
    }

    /// <summary>狂暴「余怒」移速倍率（未狂暴 = 1.0）。</summary>
    private float EnrageSpeedMult(GodotObject boss)
        => (bool)boss.Call(MethIsEnraged).AsBool() ? (float)boss.Get(PropEnrageSpeedMult).AsDouble() : 1.0f;

    /// <summary>STRAFE_SPEEDS 数组元素读取（typed Array[float] 经 Variant 动态取）。</summary>
    private static float StrafeSpeed(GodotObject boss, int index)
        => (float)boss.Get(PropStrafeSpeeds).AsGodotArray()[index].AsDouble();
}
