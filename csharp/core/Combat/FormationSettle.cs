namespace InfiAir.Core.Combat;

/// <summary>轰炸编队结算分档（纯逻辑，零 Godot 依赖）：收益上限由玩家操作决定。
/// 档位口径：全数拦截 = 投出的弹全被空中拆掉 + 编队机一架未坠（最难，专属奖励与台词）；
/// 全歼 = 打光编队；清除 = 有弹漏网。逐枚拦截分在拦截当帧已入账，本判定只管收场档位。
/// 旧字面口径「一架未坠 + 一枚未投」不可达（存活机的投弹点收场前必然全部到点）——
/// 「一枚未投」的实质是「投出全拦」。</summary>
public static class FormationSettle
{
    public enum Tier
    {
        None,
        Clear,
        AllClear,
        InterceptBonus,
    }

    /// <summary>分档判定：优先级 全数拦截 &gt; 全歼 &gt; 清除 &gt; 无。
    /// intercepted &gt; dropped 属计数错乱，不按全数拦截计。</summary>
    public static Tier Verdict(int dropped, int intercepted, int alive, int total, bool allClear)
    {
        if (dropped > 0 && intercepted == dropped && alive == total)
        {
            return Tier.InterceptBonus;
        }

        if (allClear)
        {
            return Tier.AllClear;
        }

        return dropped > 0 ? Tier.Clear : Tier.None;
    }
}
