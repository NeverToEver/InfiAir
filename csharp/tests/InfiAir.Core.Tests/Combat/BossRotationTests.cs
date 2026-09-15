using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>Boss 轮换与离场结算测试。核心不变式两条：
/// ① 出场型号按「第 N 只 = 第 (N-1)%4+1 型」循环；
/// ② 逃跑离场既不推进轮换（下一只同型）、也不给休整——两条都由击杀数不推进实现，
/// 故用例按生产同款口径把「型号派生」与「离场是否推进」串起来判（分开各测一半会漏掉脱钩）。</summary>
public sealed class BossRotationTests
{
    [Fact]
    public void TypeForKills_FollowsFourWayCycle()
    {
        Assert.Equal(1, BossRotation.TypeForKills(0));
        Assert.Equal(2, BossRotation.TypeForKills(1));
        Assert.Equal(3, BossRotation.TypeForKills(2));
        Assert.Equal(4, BossRotation.TypeForKills(3));
        Assert.Equal(1, BossRotation.TypeForKills(4));
        Assert.Equal(2, BossRotation.TypeForKills(5));
    }

    [Fact]
    public void TypeForKills_TreatsNegativeAsZero()
    {
        // 负数取模会得到 0 或负型号（轮换表错位到不存在的型），统一按 0 计
        Assert.Equal(1, BossRotation.TypeForKills(-1));
        Assert.Equal(1, BossRotation.TypeForKills(int.MinValue));
    }

    [Fact]
    public void OnLeave_KillAdvancesRotationAndRests()
    {
        Assert.True(BossRotation.RotationAdvances(false));
        Assert.True(BossRotation.Rests(false));
    }

    [Fact]
    public void OnLeave_EscapeNeitherAdvancesNorRests()
    {
        Assert.False(BossRotation.RotationAdvances(true));
        Assert.False(BossRotation.Rests(true));
    }

    /// <summary>逃跑后下一只必须仍是**同一型**：型号取自击杀数，而击杀数在逃跑路径不推进
    /// （生产推进点只有 Boss 击毁时的 AddBossKill）。把两件事串起来判——只判「逃跑不推进」
    /// 会让「型号改用别的计数源」这类脱钩照样绿。</summary>
    [Fact]
    public void Escape_KeepsSameType()
    {
        var kills = 2; // 刚打完第 3 只（3 型），击杀数 2
        var fighting = BossRotation.TypeForKills(kills);
        Assert.Equal(3, fighting);

        // 逃跑：击杀数不动 → 下一只仍是 3 型
        if (BossRotation.RotationAdvances(escaped: true))
        {
            kills += 1;
        }

        Assert.Equal(fighting, BossRotation.TypeForKills(kills));
    }

    /// <summary>击毁则推进到下一型，且四只一循环后回首型——「打完一只又是同一型」的坏法在此判红。</summary>
    [Fact]
    public void Kill_AdvancesTypeAndCyclesAfterFour()
    {
        var kills = 0;
        var seen = new System.Collections.Generic.List<int>();
        for (var i = 0; i < 4; i++)
        {
            seen.Add(BossRotation.TypeForKills(kills));
            if (BossRotation.RotationAdvances(escaped: false))
            {
                kills += 1;
            }
        }

        Assert.Equal(new[] { 1, 2, 3, 4 }, seen);
        Assert.Equal(1, BossRotation.TypeForKills(kills));
    }
}
