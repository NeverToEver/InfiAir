using Godot;

namespace InfiAir;

/// <summary>
/// 遭遇事件共享基类：EliteTurretEvent / FormationStrikeEvent 逐字重复的
/// 公共骨架——spawner 依赖注入与入树兜底、CommOverlay 台词层创建、触发冷却（_cooldownLeft
/// 递减）、母舰在场惰性缓存（MothershipPresent）、CanTrigger 公共段（IDLE+冷却+母舰）、
/// 波次恢复 ResumeWaves。子类只持各自状态机枚举（经 IsIdle 抽象属性桥接）与触发/演出逻辑。
/// 数值/时序/信号/组名保持统一；ResumeWaves 必须取判活版——spawner 已释放时
/// `?.` 会触碰死引用。
/// </summary>
public abstract partial class EncounterEventBase : Node, IEncounterEvent // 遭遇契约接口（管理器 typed 轮询）
{
    /// <summary>spawner 依赖注入（main._ready 经 SetSpawner 设置；替代 group 现找）。
    /// 字段 typed 化（Spawner 已 C#，消除动态派发）。</summary>
    protected Spawner? _spawner;

    /// <summary>台词层（typed CommOverlay；基类 _Ready 统一创建，子类先读配置再 base._Ready()）。</summary>
    protected CommOverlay? _comm;

    /// <summary>触发冷却剩余秒：事件结束/打断时由子类置回各自 Cooldown，IDLE 期逐帧递减。</summary>
    protected float _cooldownLeft;

    /// <summary>母舰在场惰性缓存：首次查得后缓存引用，释放/退组失效自动重查（替代每帧组查询）。</summary>
    private Node? _mothershipCache;

    /// <summary>本事件是否持有波次暂停（HoldWaves/ResumeWaves 配对，见 ResumeWaves）。</summary>
    private bool _wavesHeld;

    /// <summary>本事件是否持有 Boss 冻结（记账在 core BossFreezeLedger：深度 + pending 语义同源）。
    /// 只用于「重复持有/重复释放」的配对守卫；Boss 是否兑现由 spawner 侧那份记账决定。</summary>
    private readonly Core.Combat.BossFreezeLedger _bossLedger = new();

    /// <summary>子类 FSM 是否处于 IDLE（桥接各事件私有状态枚举；IsActive/CanTrigger/TickCooldown 共用）。</summary>
    protected abstract bool IsIdle { get; }

    /// <summary>通讯浮层强调色（子类各自的身份色）：精英炮塔＝品红、轰炸编队＝琥珀。
    /// 两个事件共用同一浮层实现，但「谁在说话」必须一眼可辨。</summary>
    protected virtual Color CommAccent => UITheme.EventMagenta;

    public bool IsActive() => !IsIdle;

    /// <summary>spawner 依赖注入（main._ready 调用；替代 group 现找）。</summary>
    public void SetSpawner(Node spawner) => _spawner = spawner as Spawner;

    public override void _Ready()
    {
        _comm = new CommOverlay(CommAccent);
        AddChild(_comm);
        // 对称兜底——事件节点先于 spawner 入树时注入为 null，Boss 冻结/波次暂停
        // 钩子会静默失效；兜底 group 现找
        _spawner ??= GetTree().GetFirstNodeInGroup("spawner") as Spawner;
    }

    /// <summary>触发条件公共段：IDLE 且冷却结束、母舰不在场（Boss 互斥由管理器/spawner 侧检查；
    /// 子类 override 追加各自门槛，均为纯谓词、短路序可调换）。</summary>
    public virtual bool CanTrigger()
    {
        if (!IsIdle || _cooldownLeft > 0.0f)
        {
            return false;
        }

        // 母舰在场期不触发——母舰自动火力（玩家弹阵营）可摧毁事件单位并全额发奖，
        // 玩家进保护舱零参与挂机收益；在场判定经惰性缓存（节点失效重查）
        if (MothershipPresent())
        {
            return false;
        }

        return true;
    }

    /// <summary>事件启动（互斥检查通过后由事件管理器调用；子类实现各自入场编排）。</summary>
    public abstract void Start();

    /// <summary>返航中止（main._start_homecoming 调用；子类实现各自清理/结算跳过逻辑）。</summary>
    public abstract void Abort();

    /// <summary>母舰在场惰性缓存：首次查得后缓存引用，释放/退组失效自动重查（替代每帧组查询）。</summary>
    protected bool MothershipPresent()
    {
        if (_mothershipCache != null && GodotObject.IsInstanceValid(_mothershipCache)
            && _mothershipCache.IsInGroup("mothership"))
        {
            return true;
        }

        _mothershipCache = GetTree().GetFirstNodeInGroup("mothership");
        return _mothershipCache != null;
    }

    /// <summary>暂停普通波次（事件启动时；与 ResumeWaves 成对，重复调用只算一次持有）。
    /// spawner 侧是深度计数，事件侧再记一次持有标志——收场路径可能重复调用释放，
    /// 多余的释放会把别的持有者（Boss/另一个事件）的暂停一起解掉。</summary>
    protected void HoldWaves()
    {
        if (_wavesHeld)
        {
            return;
        }

        if (_spawner == null || !GodotObject.IsInstanceValid(_spawner))
        {
            // 静默空转会演成「事件在跑但波次不暂停/Boss 不冻结」——必须可见
            GD.PushWarning($"{GetType().Name}: HoldWaves 时 spawner 未注入，波次未暂停");
            return;
        }

        _spawner.SetWavesPaused(true);
        _wavesHeld = true;
    }

    /// <summary>恢复普通波次（事件结束/打断时）；未持有则为空操作（幂等）。</summary>
    protected void ResumeWaves()
    {
        if (!_wavesHeld)
        {
            return;
        }

        _wavesHeld = false;
        if (_spawner != null && GodotObject.IsInstanceValid(_spawner))
        {
            _spawner.SetWavesPaused(false);
        }
    }

    /// <summary>冻结 Boss 调度（事件占用 Boss 槽时；spawner 侧深度计数，多事件同时持有安全）。
    /// 本事件是否已持有由 ledger 深度判定：重复持有会让一次释放留下未解冻的深度，
    /// 表现是「本局再也不出 Boss」。</summary>
    protected void HoldBoss()
    {
        if (_bossLedger.Frozen)
        {
            return;
        }

        if (_spawner == null || !GodotObject.IsInstanceValid(_spawner))
        {
            GD.PushWarning($"{GetType().Name}: HoldBoss 时 spawner 未注入，Boss 未冻结");
            return;
        }

        _spawner.HoldBossFreeze();
        _bossLedger.Hold();
    }

    /// <summary>释放 Boss 互斥：完全释放（无其他持有者）时补触发一次期间被冻结的 Boss
    /// ——冻结期间的到期只记一次 pending，解冻即兑现，不累积也不丢失。
    /// <paramref name="triggerPending"/>＝false 用于打断路径（返航/死亡）：此刻补出 Boss 只会在
    /// 结算画面上弹预警横幅，故丢弃；**丢弃必须连同 pending 一起清掉**——自然门控（分数/时间门）
    /// 不会饿死，留着的陈旧标记会在下一次遭遇正常收场时被消费，凭空补出一只绕过分数门与
    /// 最小间隔的 Boss。仍被其他事件持有时不清：那一次到期属于共享冻结窗口，
    /// 由最后一位持有者收场时消费。</summary>
    protected void ReleaseBoss(bool triggerPending = true)
    {
        if (!_bossLedger.Frozen)
        {
            return; // 未持有：空操作（幂等），不得替其他持有者消费共享冻结窗口里的到期
        }

        if (_spawner == null || !GodotObject.IsInstanceValid(_spawner))
        {
            // spawner 已释放：无可解冻、无 Boss 可触发，只同步递减本端持有
            _bossLedger.Release(false);
            return;
        }

        // spawner 侧决定「兑现还是丢弃」（含仍有其他持有者时不动 pending 的语义）；
        // 本端持有随之同步递减——两侧只递减各自的深度，pending 只由 spawner 侧那份记账持有
        var trigger = _spawner.ReleaseBossFreeze(triggerPending);
        _bossLedger.Release(triggerPending);
        if (trigger)
        {
            _spawner.TriggerBoss();
        }
    }

    /// <summary>IDLE 期触发冷却逐帧递减（两事件 _Process 同构段）。</summary>
    protected void TickCooldown(float delta)
    {
        if (IsIdle && _cooldownLeft > 0.0f)
        {
            _cooldownLeft -= delta;
        }
    }
}
