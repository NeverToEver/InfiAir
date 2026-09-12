using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>FormationSettle 结算分档契约：全数拦截 > 全歼 > 清除 > 无。
/// 原实现在引擎层且条件恒假（_dropped==0 && _alive>0 不可达——存活机的投弹点
/// 收场前必然全部到点），最难操作落入低档奖励；下沉后由单测钉住可达口径。</summary>
public sealed class FormationSettleTests
{
    [Fact]
    public void Verdict_AllBombsInterceptedAndNoCraftLost_InterceptBonus()
    {
        // 新口径（待人类确认）：投出的弹全被空中拆掉 + 编队机一架未坠
        Assert.Equal(FormationSettle.Tier.InterceptBonus, FormationSettle.Verdict(6, 6, 5, 5, false));
    }

    [Fact]
    public void Verdict_OldLiteralCondition_DoesNotYieldInterceptBonus()
    {
        // 钉住旧口径不可达的证明：dropped==0 && alive>0 不得判全数拦截——
        // 该分支在原实现里是死代码，reward_intercept 永不发放
        Assert.NotEqual(FormationSettle.Tier.InterceptBonus, FormationSettle.Verdict(0, 0, 5, 5, false));
    }

    [Fact]
    public void Verdict_InterceptedAllButCraftLost_IsClearNotBonus()
    {
        // 一架已坠即掉出全数拦截档（「一架未坠」是档位的一部分）
        Assert.Equal(FormationSettle.Tier.Clear, FormationSettle.Verdict(4, 4, 4, 5, false));
    }

    [Fact]
    public void Verdict_AllClear_WithOrWithoutDrops()
    {
        Assert.Equal(FormationSettle.Tier.AllClear, FormationSettle.Verdict(3, 1, 0, 5, true));
        Assert.Equal(FormationSettle.Tier.AllClear, FormationSettle.Verdict(0, 0, 0, 5, true));
    }

    [Fact]
    public void Verdict_BombsGotThrough_IsClear()
    {
        Assert.Equal(FormationSettle.Tier.Clear, FormationSettle.Verdict(4, 2, 3, 5, false));
    }

    [Fact]
    public void Verdict_NothingDroppedNoAllClear_IsNone()
    {
        Assert.Equal(FormationSettle.Tier.None, FormationSettle.Verdict(0, 0, 5, 5, false));
    }

    [Fact]
    public void Verdict_InterceptBonusTakesPriority()
    {
        // 矛盾输入（allClear 与一架未坠并存）钉住分档优先级
        Assert.Equal(FormationSettle.Tier.InterceptBonus, FormationSettle.Verdict(2, 2, 5, 5, true));
    }

    [Fact]
    public void Verdict_InterceptedOverDropped_IsNotBonus()
    {
        // 计数错乱的防御分支：拦截数超过投出数不得判全数拦截
        Assert.Equal(FormationSettle.Tier.Clear, FormationSettle.Verdict(3, 4, 5, 5, false));
    }
}
