namespace InfiAir.Core.Events;

/// <summary>
/// 遭遇事件的触发判定（纯逻辑，零 Godot 依赖）：资格合成与触发计时推进。
/// 这两件事实原先只存在于 GameEventManager 的逐帧循环里，规则改了没人验得出——
/// 「分数门槛被绕过」「资格不足时计时仍在推进」都只会表现为事件来得莫名其妙。
/// 引擎侧管理器只负责收集状态（本局分数、Boss 是否活跃、组内是否有别的遭遇在跑）与掷签。
/// </summary>
public static class EncounterTrigger
{
    /// <summary>单步推进结果：剩余计时 + 本步是否到点掷签。</summary>
    public readonly struct Step
    {
        public Step(float remaining, bool due)
        {
            Remaining = remaining;
            Due = due;
        }

        /// <summary>本步之后的剩余计时（到点为复位后的整段间隔）。</summary>
        public readonly float Remaining;

        /// <summary>是否到点（调用方据此掷签并启动）。</summary>
        public readonly bool Due;
    }

    /// <summary>触发资格：事件自身就绪（空闲 + 冷却结束 + 母舰不在场）+ Boss 未激活 +
    /// 组内无其他遭遇在跑 + 分数达标。任一条不满足都不掷签，且计时冻结（见 Advance）。</summary>
    public static bool Eligible(bool selfReady, bool bossActive, bool anyOtherEncounterActive, int score, int minScore)
        => selfReady && !bossActive && !anyOtherEncounterActive && score >= minScore;

    /// <summary>触发计时推进：资格/分数门槛未过时计时冻结，过了才递减；
    /// 到点先把计时复位到整段 <paramref name="interval"/> 再回报掷签——掷签失败与成功都从整段
    /// 重新计，否则停在 0 附近每帧重掷，等于把触发概率变成「迟早必中」。
    /// <paramref name="interval"/> 的域钳（≥最小时长）由调用方在读取配置时完成，本函数按正值处理。</summary>
    public static Step Advance(float remaining, float delta, float interval, bool eligible, int score, int minScore)
    {
        if (!eligible || score < minScore)
        {
            return new Step(remaining, false);
        }

        var left = remaining - delta;
        return left <= 0.0f ? new Step(interval, true) : new Step(left, false);
    }
}
