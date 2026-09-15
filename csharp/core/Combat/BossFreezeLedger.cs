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

    /// <summary>读取并清除 pending（唯一消费口）。</summary>
    public bool ConsumePending()
    {
        var was = Pending;
        Pending = false;
        return was;
    }

    /// <summary>释放一次持有并判定是否应立即补触发。返回 true 的调用方负责真正触发 Boss。
    /// <paramref name="triggerPending"/>＝false 是打断路径（返航/死亡）：此刻补出 Boss 只会在结算画面上
    /// 弹预警横幅，而自然门控（分数/时间门）不会饿死，故由**最后一位**持有者丢弃并清除 pending。
    /// 仍被其他事件持有时不清——那一次到期属于共享冻结窗口，由最后一位持有者收场时消费。
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

        return ConsumePending();
    }
}
