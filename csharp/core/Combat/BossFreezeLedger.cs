namespace InfiAir.Core.Combat;

/// <summary>Boss 冻结/pending 记账（纯逻辑，零 Godot 依赖）：遭遇事件持有 Boss 调度期间，
/// 到期的 Boss 只记一次 pending（不累积），释放后由「解冻即兑现」补触发一次。
/// 深度计数口径与波次暂停同构——两个事件各自持有时，先结束者不会提前解冻后结束者的冻结。
/// 核心不变式：**打断路径（丢弃补触发）在最后一次释放时必须清掉 pending**——留着的陈旧标记会在
/// 下一次正常收场时被消费，凭空补出一只绕过分数门与最小间隔的 Boss，且全程无报错。
/// 另一条不变式：未持有者不得释放（释放必须与持有成对），否则会替别的持有者消费掉它的到期。</summary>
public sealed class BossFreezeLedger
{
    /// <summary>当前持有深度（&gt;0 即冻结中）。</summary>
    public int Depth { get; private set; }

    /// <summary>冻结期间是否有过到期（一次冻结窗口内只记一次，不累积）。</summary>
    public bool Pending { get; private set; }

    /// <summary>是否处于冻结（仍有持有者）。</summary>
    public bool Frozen => Depth > 0;

    /// <summary>事件持有一次冻结（深度 +1）。</summary>
    public void Hold() => Depth += 1;

    /// <summary>冻结期间到期：记一次 pending。未冻结时的到期不该被吞掉（调用方错序），故不记。</summary>
    public void NoteDue()
    {
        if (Frozen)
        {
            Pending = true;
        }
    }

    /// <summary>兑现成功后的唯一确认口：清掉 pending 并返回是否确实清掉了。
    /// 与 <see cref="Release"/> 配对构成原子操作——两者之间插不进任何「消费了却没触发」的窗口。</summary>
    public bool CommitPending()
    {
        var was = Pending;
        Pending = false;
        return was;
    }

    /// <summary>释放一次持有并判定「期间是否到期」。返回 true 表示**应由调用方当场兑现**，
    /// 此时 pending **尚未清除**——兑现成功必须紧接着调 <see cref="CommitPending"/>，
    /// 兑现被拒（spawner 停驱动、入场窗口等）则标记留在账上，由下一次触发兑现。
    /// 先消费再触发会让被拒的那一次到期静默消失（Boss 不来了，也没有任何报错），
    /// 正是「解冻即兑现，不累积也不丢失」被破坏的形态。
    /// <paramref name="triggerPending"/>＝false 是打断路径（返航/死亡）：此刻补出 Boss 只会在结算画面上
    /// 弹预警横幅，而自然门控（分数/时间门）不会饿死，故由**最后一位**持有者丢弃并清除 pending。
    /// 仍被其他事件持有时不清——那一次到期属于共享冻结窗口，由最后一位持有者收场时兑现。
    /// 未持有（深度已为 0）时整体空转，不动 pending。</summary>
    public bool Release(bool triggerPending)
    {
        if (Depth <= 0)
        {
            return false;
        }

        Depth -= 1;
        if (Frozen)
        {
            return false;
        }

        if (!triggerPending)
        {
            Pending = false;
            return false;
        }

        return Pending;
    }
}
