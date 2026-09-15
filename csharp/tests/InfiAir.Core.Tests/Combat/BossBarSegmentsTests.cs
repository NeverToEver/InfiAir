using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>BossBarSegments 契约测试：血条段权与阶段刻度线必须由阶段阈值派生（段界即阈值）。
/// 守的是「HUD 另存一份硬编码段权」这类静默错位——改 boss.phase2_hp_ratio 后 Boss 在新区间
/// 转阶段而血条段界/刻度仍在旧值，玩家读到的阶段边界是假的，冒烟/长局/截图探针都不判段界。</summary>
public sealed class BossBarSegmentsTests
{
    [Fact]
    public void Weights_DefaultThresholds_MatchDesignerRatios()
    {
        // 生产默认（boss.phase2_hp_ratio 0.7 / boss.enrage.hp_ratio 0.3）：段权仍是 0.3/0.4/0.3，
        // 刻度仍是 70%/30%——派生改造不改变默认档观感（float 减法有末位误差，按容差比）
        AssertSame(new[] { 0.3f, 0.4f, 0.3f }, BossBarSegments.Weights(0.7f, 0.3f));
        AssertSame(new[] { 0.7f, 0.3f }, BossBarSegments.Ticks(0.7f, 0.3f));
    }

    /// <summary>逐项浮点比较（权重由减法派生，末位与手写常量不同：0.7f−0.3f = 0.39999998）。</summary>
    private static void AssertSame(IReadOnlyList<float> expected, IReadOnlyList<float> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i], 5);
        }
    }

    [Fact]
    public void Weights_FollowThresholds_ForNonDefaultValues()
    {
        // 非默认阈值：段权随阈值变（硬编码 0.3/0.4/0.3 的实现在此红）
        var w = BossBarSegments.Weights(0.6f, 0.25f);
        Assert.Equal(3, w.Length);
        Assert.Equal(BossBarSegments.Count, w.Length);
        Assert.Equal(0.40f, w[0], 5);
        Assert.Equal(0.35f, w[1], 5);
        Assert.Equal(0.25f, w[2], 5);
        Assert.Equal(1.0f, w[0] + w[1] + w[2], 5);
    }

    [Fact]
    public void Ticks_FollowThresholds_ForNonDefaultValues()
    {
        // 刻度随阈值变（硬编码 [0.7, 0.3] 的实现在此红）
        AssertSame(new[] { 0.6f, 0.25f }, BossBarSegments.Ticks(0.6f, 0.25f));
    }

    [Theory]
    [InlineData(0.7f, 0.3f)]
    [InlineData(0.6f, 0.25f)]
    [InlineData(0.9f, 0.1f)]
    [InlineData(0.55f, 0.4f)]
    public void Ticks_AlwaysEqualSegmentBoundaries(float p2, float e)
    {
        // 不变量：第 i 条刻度 == 第 i 段的上界（自左端量起的比例 = 1 − 累计段宽）。
        // 血条掉段闪定位（SegmentIndexAt）与刻度线因此不会分叉。
        var w = BossBarSegments.Weights(p2, e);
        var ticks = BossBarSegments.Ticks(p2, e);
        Assert.Equal(2, ticks.Length);

        var cumulative = 0.0f;
        for (var i = 0; i < ticks.Length; i++)
        {
            cumulative += w[i];
            Assert.Equal(1.0f - cumulative, ticks[i], 5);
        }
    }

    [Fact]
    public void Weights_AreNonNegativeAndOrdered_AcrossThresholdSweep()
    {
        // 防御：倒挂配置（p2 ≤ e）不得产出负段权或反序段界（Boss 侧已收口，此处仍须成立）。
        for (var i = 0; i <= 20; i++)
        {
            for (var j = 0; j <= 20; j++)
            {
                var p2 = i / 20.0f;
                var e = j / 20.0f;
                var w = BossBarSegments.Weights(p2, e);
                var ticks = BossBarSegments.Ticks(p2, e);
                Assert.All(w, x => Assert.True(x >= 0.0f, $"负段权：p2={p2} e={e}"));
                Assert.Equal(1.0f, w[0] + w[1] + w[2], 4);
                Assert.True(ticks[0] > ticks[1], $"段界反序：p2={p2} e={e}");
                Assert.InRange(ticks[0], 0.0f, 1.0f);
                Assert.InRange(ticks[1], 0.0f, 1.0f);
            }
        }
    }

    [Fact]
    public void Thresholds_NonFiniteAndOutOfRange_AreClamped()
    {
        var w = BossBarSegments.Weights(float.NaN, 0.3f);
        Assert.All(w, x => Assert.True(float.IsFinite(x) && x >= 0.0f));
        Assert.Equal(1.0f, w[0] + w[1] + w[2], 4);
        // >1 与 <0 的阈值同样收口（段权不得为负）
        var clamped = BossBarSegments.Weights(1.5f, -0.2f);
        Assert.All(clamped, x => Assert.True(x >= 0.0f));
        Assert.Equal(1.0f, clamped[0] + clamped[1] + clamped[2], 4);
    }

    [Fact]
    public void Normalize_SingleSource_MatchBossLoadBalanceContract()
    {
        // 保序收口是单源（Boss.LoadBalance 与本类段权/刻度同调）：对同一对阈值必须给出同一结果，
        // 且 P2 严格高于 ENRAGE。这里把「Boss 侧原实现」的形态钉死为反例——它在 e=0.99 处
        // 产出 p2=0.98 < e 的倒挂，正是本函数要消除的分叉。
        var (p2, e) = BossBarSegments.Normalize(0.7f, 0.3f);
        Assert.Equal(0.7f, p2, 5);
        Assert.Equal(0.3f, e, 5);

        // e 顶到上界 0.99：Normalize 必须把 e 压到 0.98 并让 p2 高于它（原实现会倒挂）
        var (hiP2, hiE) = BossBarSegments.Normalize(0.2f, 0.99f);
        Assert.True(hiP2 > hiE, $"保序失败：p2={hiP2} e={hiE}");
        Assert.Equal(0.99f, hiP2, 5);
        Assert.Equal(0.98f, hiE, 5);

        // 幂等：对已收口的值再调一次不得再变（Boss 收口后 HUD 从实例读回再派生）
        Assert.Equal((hiP2, hiE), BossBarSegments.Normalize(hiP2, hiE));
        Assert.Equal((p2, e), BossBarSegments.Normalize(p2, e));

        // 倒挂输入（p2 ≤ e）一律修正为 p2 > e
        var (a, b) = BossBarSegments.Normalize(0.2f, 0.5f);
        Assert.True(a > b, $"倒挂未修正：p2={a} e={b}");
    }
}
