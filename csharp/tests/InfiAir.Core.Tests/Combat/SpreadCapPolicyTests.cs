using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>spread 同屏上限判定契约：上限只约束 spread，超限按精英与否退化（精英 → laser，其余 → single）。
/// 判定必须能在实际入场处逐只收敛——敌机延后进场，同一波抽签同帧发生，抽签处判定会让整波全部抽中
/// spread（上限形同虚设）。本组用例钉住「逐只递减配额」的语义。</summary>
public sealed class SpreadCapPolicyTests
{
    [Fact]
    public void Resolve_NonSpread_AlwaysPassThrough()
    {
        Assert.Equal(EnemyBulletKind.Single, SpreadCapPolicy.Resolve(EnemyBulletKind.Single, 99, 1, false));
        Assert.Equal(EnemyBulletKind.Laser, SpreadCapPolicy.Resolve(EnemyBulletKind.Laser, 99, 1, true));
        Assert.Equal(EnemyBulletKind.Other, SpreadCapPolicy.Resolve(EnemyBulletKind.Other, 99, 0, false));
    }

    [Fact]
    public void Resolve_SpreadUnderCap_StaysSpread()
    {
        Assert.Equal(EnemyBulletKind.Spread, SpreadCapPolicy.Resolve(EnemyBulletKind.Spread, 0, 3, false));
        Assert.Equal(EnemyBulletKind.Spread, SpreadCapPolicy.Resolve(EnemyBulletKind.Spread, 2, 3, true));
    }

    [Fact]
    public void Resolve_SpreadAtOrOverCap_DowngradesByElite()
    {
        Assert.Equal(EnemyBulletKind.Single, SpreadCapPolicy.Resolve(EnemyBulletKind.Spread, 3, 3, false));
        Assert.Equal(EnemyBulletKind.Single, SpreadCapPolicy.Resolve(EnemyBulletKind.Spread, 5, 3, false));
        Assert.Equal(EnemyBulletKind.Laser, SpreadCapPolicy.Resolve(EnemyBulletKind.Spread, 3, 3, true));
        Assert.Equal(EnemyBulletKind.Laser, SpreadCapPolicy.Resolve(EnemyBulletKind.Spread, 9, 3, true));
    }

    [Fact]
    public void Resolve_CapZero_RejectsSpreadFromEmptyBoard()
    {
        // cap=0（难度档不允许在场 spread）：在册数为 0 也已触顶
        Assert.Equal(EnemyBulletKind.Single, SpreadCapPolicy.Resolve(EnemyBulletKind.Spread, 0, 0, false));
    }

    [Fact]
    public void Resolve_SameFrameWave_ConsumesQuotaOneByOne()
    {
        // 同帧抽签 n 只（hard cap=3）：逐只收敛，最终恰好 3 只 spread
        var cap = 3;
        var active = 0;
        var spreadCount = 0;
        for (var i = 0; i < 6; i++)
        {
            var resolved = SpreadCapPolicy.Resolve(EnemyBulletKind.Spread, active, cap, false);
            if (resolved == EnemyBulletKind.Spread)
            {
                active += 1;
                spreadCount += 1;
            }
            else
            {
                Assert.Equal(EnemyBulletKind.Single, resolved);
            }
        }

        Assert.Equal(cap, spreadCount);
    }

    [Fact]
    public void Resolve_QuotaReopens_WhenCountDropsBack()
    {
        // 前 3 只占满配额，第 4 只被降级；在册数回落到 2（有敌机离场/被击毁）后配额重新可用
        Assert.Equal(EnemyBulletKind.Single, SpreadCapPolicy.Resolve(EnemyBulletKind.Spread, 3, 3, false));
        Assert.Equal(EnemyBulletKind.Spread, SpreadCapPolicy.Resolve(EnemyBulletKind.Spread, 2, 3, false));
    }
}
