using System;
using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>爆炸池入池资格契约：总量（空闲 + 活跃 + 本实例）不超过上限才留作复用。
/// 本组用例钉住两条语义——① 池被取空（并发高峰）时上限仍在生效，② 上限是**保留实例总数**的
/// 天花板，不是空闲队列长度。前者是原实现的漏洞形态：判据只看空闲数、池忙时空闲恒 0，
/// 「空闲 &lt; 上限」恒真，24 个上限永不触发，实例按历史峰值并发长期保留
/// （每实例含 4 张贴图 + 3 个发射器 + 2 个环 + 8 片碎片）。</summary>
public sealed class ExplosionPoolPolicyTests
{
    [Fact]
    public void ShouldPool_BelowCap_Retains()
    {
        Assert.True(ExplosionPoolPolicy.ShouldPool(0, 0, 24));
        Assert.True(ExplosionPoolPolicy.ShouldPool(5, 10, 24));
    }

    [Fact]
    public void ShouldPool_TotalReachesCap_Rejects()
    {
        // 边界：入池后总量恰好等于上限仍可保留（上限是「不超过」，不是「小于」）
        Assert.True(ExplosionPoolPolicy.ShouldPool(23, 0, 24));
        Assert.True(ExplosionPoolPolicy.ShouldPool(10, 13, 24));
        // 再多一个即越限
        Assert.False(ExplosionPoolPolicy.ShouldPool(24, 0, 24));
        Assert.False(ExplosionPoolPolicy.ShouldPool(10, 14, 24));
    }

    [Fact]
    public void ShouldPool_BusyPool_StillEngagesCap()
    {
        // 原形态漏洞：池被取空（空闲 0）时全部实例都在场上，若不数活跃数则上限恒不触发
        Assert.False(ExplosionPoolPolicy.ShouldPool(0, 24, 24));
        Assert.False(ExplosionPoolPolicy.ShouldPool(0, 60, 24));
        // 空闲队列短但活跃已满，同样拒绝
        Assert.False(ExplosionPoolPolicy.ShouldPool(3, 21, 24));
    }

    [Fact]
    public void ShouldPool_CapNotPositive_DisablesRetention()
    {
        // cap ≤ 0（配置缺省/写坏）：不复用，全部临时实例播完销毁
        Assert.False(ExplosionPoolPolicy.ShouldPool(0, 0, 0));
        Assert.False(ExplosionPoolPolicy.ShouldPool(0, 0, -1));
    }

    [Fact]
    public void ShouldPool_CapOne_RetainsExactlyOne()
    {
        Assert.True(ExplosionPoolPolicy.ShouldPool(0, 0, 1));
        Assert.False(ExplosionPoolPolicy.ShouldPool(0, 1, 1));
        Assert.False(ExplosionPoolPolicy.ShouldPool(1, 0, 1));
    }

    [Fact]
    public void Explosion_DelegatesPoolRetentionToCorePolicy()
    {
        // 结构性判据（读源码文本）：判定单源在 core，引擎侧只做取用/回池，不留第二份判据副本。
        // 副本形态＝把空闲队列长度直接与上限比（池忙时空闲恒 0，"空闲 < 上限" 恒真）——
        // 两处取值相等时运行期分辨不出谁是副本，只能判「不留副本」这一结构事实。
        var src = RepoFiles.Read("csharp/godot/Explosion.cs");
        Assert.Contains("ExplosionPoolPolicy.ShouldPool", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Stock.Count < _poolCap", src, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldPool_PeakBurst_RetainedNeverExceedsCap()
    {
        // 生命周期模拟（与 Explosion.SpawnAt / Finish 同序）：取用优先走空闲队列，队列空了才新建并
        // 询问入池资格；留作复用的实例播完回队列，临时实例播完销毁。
        // 突发形态（无间歇连续引爆）正是原实现失效的场景：空闲队列恒空，"空闲 < 上限" 恒真，
        // 保留总量随峰值并发无界增长。
        const int cap = 4;
        var idle = 0; // 空闲队列长度
        var live = 0; // 在播实例数（含不可复用者）
        var retained = 0; // 已留作复用、可回池的实例总数（空闲 + 在播的可复用者）
        for (var burst = 0; burst < 3; burst++)
        {
            for (var i = 0; i < 12; i++)
            {
                if (idle > 0)
                {
                    // 复用池内实例：不新建、不问资格，保留总量不变
                    idle--;
                    live++;
                    continue;
                }

                if (ExplosionPoolPolicy.ShouldPool(idle, live, cap))
                {
                    retained++;
                }

                live++;
                Assert.True(retained <= cap, $"保留实例数 {retained} 越过上限 {cap}（第 {burst} 轮突发第 {i} 个）");
            }

            // 全部播完：可复用者回队列（保留总量不变），临时实例销毁
            live = 0;
            idle = retained;
            Assert.True(idle <= cap, $"空闲队列 {idle} 越过上限 {cap}");
        }
    }
}
