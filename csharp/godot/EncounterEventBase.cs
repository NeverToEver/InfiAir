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

    /// <summary>本事件是否持有 Boss 冻结（HoldBoss/ReleaseBoss 配对，见 HoldBoss）。</summary>
    private bool _bossHeld;

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
        if (_wavesHeld || _spawner == null || !GodotObject.IsInstanceValid(_spawner))
        {
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
    /// 与 HoldWaves 同构地记一次持有标志：重复持有会让一次释放留下未解冻的深度，
    /// 表现是「本局再也不出 Boss」。</summary>
    protected void HoldBoss()
    {
        if (_bossHeld || _spawner == null || !GodotObject.IsInstanceValid(_spawner))
        {
            return;
        }

        _spawner.SetBossFrozen(true);
        _bossHeld = true;
    }

    /// <summary>释放 Boss 互斥：完全释放（无其他持有者）时补触发一次期间被冻结的 Boss
    /// ——冻结期间的到期只记一次 pending，解冻即兑现，不累积也不丢失。
    /// <paramref name="triggerPending"/>＝false 用于打断路径（返航/死亡）：此刻补出 Boss 只会在
    /// 结算画面上弹预警横幅，pending 留给本局恢复后的自然门控（分数/时间门不会饿死）。</summary>
    protected void ReleaseBoss(bool triggerPending = true)
    {
        if (!_bossHeld)
        {
            return;
        }

        _bossHeld = false;
        if (_spawner == null || !GodotObject.IsInstanceValid(_spawner))
        {
            return;
        }

        _spawner.SetBossFrozen(false);
        if (!triggerPending || _spawner.BossFrozen())
        {
            return; // 丢弃补触发 / 仍有其他事件持有
        }

        if (_spawner.ConsumeBossPending())
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
