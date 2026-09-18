namespace InfiAir.Core.Combat;

/// <summary>
/// 输入缓冲（§2.14③）：就绪前一小窗内的按下在动作就绪帧自动生效——「早了一点」的按键不丢
/// （行业惯例窗口远小于 150ms；生产取值 0.1s，balance.json player.input_buffer_window）。
///
/// 只缓冲**时机类锁定**（冷却会在可预期时刻归零）；资源类锁定（燃料）不缓冲——资源恢复
/// 不可预期，「资源一到就自走一格」是意外行为。该判据在调用侧（Player 只在燃料足够时 arm），
/// 本类只管时序，不知晓燃料/冷却的来源。
///
/// 语义：
///   Arm(cooldownRemaining, window)：按下但动作未就绪时调用；冷却剩余落在 (0, window] 才记暂存
///   （恰好 ≤0 不记——那一帧本就应直接触发，缓冲不得替代直接按下的语义）；
///   Tick(delta)：暂存寿命随模拟时间衰减；**过期线＝寿命耗尽的下一帧**——暂存保证活过
///   「冷却最早归零」的那一帧（否则寿命与冷却的浮点残渣会在同帧边界互斥，早按被丢），
///   之后仍未消费即作废，被外部编排锁住的按下最多顺延一帧、不会「隔空触发」；
///   ConsumeIfReady(actionReady)：暂存有效且动作此刻就绪才消费并返回真；消费即清（幂等），
///   就绪判据由调用方给（弹反＝冷却归零且不在流程中；冲刺＝冷却归零且燃料足够）。
///
/// 帧内次序约定（调用侧遵守）：先推进冷却并做消费判定，最后 Tick——消费发生在本帧寿命
/// 判死之前，冷却归零帧与暂存过期帧重合时按下仍被兑现。
/// </summary>
public class InputBuffer
{
    /// <summary>是否有暂存在等（与寿命分离：寿命只管过期，不参与「有没有」的判定）。</summary>
    private bool _pending;

    /// <summary>暂存剩余寿命（秒）；≤0 且经 Tick 判死后暂存作废。</summary>
    private float _remaining;

    /// <summary>暂存剩余寿命（无暂存时为 0；只作诊断读数，不参与判定）。</summary>
    public float PendingRemaining => _pending ? _remaining : 0.0f;

    /// <summary>记一次早按：冷却剩余落在 (0, window] 才生效；窗口非法（≤0）按不缓冲处理。</summary>
    public void Arm(float cooldownRemaining, float window)
    {
        if (window <= 0.0f || cooldownRemaining <= 0.0f || cooldownRemaining > window)
        {
            return;
        }

        _pending = true;
        _remaining = cooldownRemaining;
    }

    /// <summary>暂存寿命推进（模拟时间）：寿命耗尽（≤0）即作废——此刻冷却必然已归零至少一帧，
    /// 没触发说明动作被外部编排拦下，不再无限期等下去。</summary>
    public void Tick(float delta)
    {
        if (!_pending)
        {
            return;
        }

        _remaining -= delta;
        if (_remaining <= 0.0f)
        {
            _pending = false;
        }
    }

    /// <summary>暂存有效且动作此刻就绪则消费并返回真；无暂存或未就绪都不消费。
    /// 未就绪返回假但不消费——就绪判据含燃料这类可恢复量，恢复前暂存仍随寿命自然过期。</summary>
    public bool ConsumeIfReady(bool actionReady)
    {
        if (!_pending || !actionReady)
        {
            return false;
        }

        _pending = false;
        return true;
    }

    /// <summary>丢弃暂存（外部编排清理时用）。</summary>
    public void Clear() => _pending = false;
}
