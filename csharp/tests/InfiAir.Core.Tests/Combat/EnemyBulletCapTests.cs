using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>同屏敌弹硬上限判据契约：只统计敌弹、玩家火力永不限量。
/// 判据若用「全场弹总数」（玩家弹 + 敌弹），上限会随玩家火力被吃掉，
/// 表现为打得猛时敌弹变稀（无日志的隐性难度漂移）。</summary>
public sealed class EnemyBulletCapTests
{
    [Fact]
    public void ShouldDrop_PlayerBullet_NeverLimited()
    {
        // 玩家弹即使全场弹数远超上限也一律放行（玩家火力不受限）
        Assert.False(EnemyBulletCap.ShouldDrop(false, 10_000, 500));
        Assert.False(EnemyBulletCap.ShouldDrop(false, 0, 500));
    }

    [Fact]
    public void ShouldDrop_EnemyBullet_DropsAtCapOnly()
    {
        Assert.False(EnemyBulletCap.ShouldDrop(true, 499, 500));
        Assert.True(EnemyBulletCap.ShouldDrop(true, 500, 500));
        Assert.True(EnemyBulletCap.ShouldDrop(true, 501, 500));
    }

    [Fact]
    public void ShouldDrop_IsFactionExclusive()
    {
        // 同一计数下两种阵营结论必须相反——判据若漏掉阵营分支，玩家弹会被敌弹上限一并截断
        Assert.NotEqual(
            EnemyBulletCap.ShouldDrop(true, 600, 500),
            EnemyBulletCap.ShouldDrop(false, 600, 500));
    }
}
