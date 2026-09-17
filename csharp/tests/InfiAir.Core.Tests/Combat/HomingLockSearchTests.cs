using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>homing 增幅的落靶搜索：射程闭区间、锁定锥、最近者胜、与原点重合不合格。
/// 这些算式原先内联在 Player.NearestAugHomingTarget 的逐候选循环里，坏了只表现为
/// 「制导时有时无 / 锁错目标」，编译、冒烟与画面都看不出。</summary>
public sealed class HomingLockSearchTests
{
    /// <summary>瞄准方向朝上（-y）；锥阈值取 balance 口径的换算（44° 整角），不在测试里手抄余弦。</summary>
    private static readonly float ConeCos = AimCone.CosFromFullAngleDeg(44.0f);

    private static HomingLockSearch NewSearch(float coneCos = 0.0f, float maxRange = 900.0f)
        => new(0.0f, 0.0f, 0.0f, -1.0f, coneCos == 0.0f ? ConeCos : coneCos, maxRange);

    [Fact]
    public void Consider_PicksNearestQualifyingCandidate()
    {
        var search = NewSearch();

        // 近但出锥（正右方，与瞄准方向差 90°）：不得占位，否则制导会锁死屏侧目标
        Assert.False(search.Consider(100.0f, 0.0f));

        // 远但在锥内：选中
        Assert.True(search.Consider(0.0f, -400.0f));
        Assert.Equal(400.0f, search.BestDistance);

        // 更近且合格：取而代之
        Assert.True(search.Consider(0.0f, -120.0f));
        Assert.Equal(120.0f, search.BestDistance);

        // 更远且合格：不占位
        Assert.False(search.Consider(0.0f, -500.0f));
        Assert.Equal(120.0f, search.BestDistance);
    }

    [Fact]
    public void Consider_RangeBoundaryIsClosed()
    {
        var search = NewSearch();

        // 恰好射程上界（900 = balance augments.homing.lock_range）算合格；出界即不合格
        Assert.True(search.Consider(0.0f, -900.0f));
        Assert.False(search.Consider(0.0f, -900.5f));
        Assert.False(search.Consider(0.0f, -2000.0f));
    }

    [Fact]
    public void Consider_LockConeBoundaryIsInclusive()
    {
        // 阈值取测试内构造的 0.8（不是 balance 取值）：候选方向 (0.6, -0.8) 的点积恰为 0.8，
        // 边界必须算合格——写成严格大于会让「刚好在锥沿上」的目标消失
        var search = NewSearch(coneCos: 0.8f);
        Assert.True(search.Consider(60.0f, -80.0f));

        var outside = NewSearch(coneCos: 0.8f);
        Assert.False(outside.Consider(61.0f, -80.0f));
    }

    [Fact]
    public void Consider_RejectsCandidateAtOrigin()
    {
        // 与原点重合（距离 0）无方向可言：不合格，且不得污染后续比较
        var search = NewSearch();
        Assert.False(search.Consider(0.0f, 0.0f));
        Assert.Equal(900.0f, search.BestDistance);
        Assert.True(search.Consider(0.0f, -300.0f));
    }

    [Fact]
    public void Consider_EqualDistanceReplacesBest()
    {
        // 同距后来者胜（原实现为 d > best 才跳过）：钉住比较方向，别写成严格小于
        var search = NewSearch();
        Assert.True(search.Consider(-100.0f, -300.0f));
        Assert.True(search.Consider(100.0f, -300.0f));
    }
}
