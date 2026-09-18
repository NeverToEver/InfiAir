using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>LifestealHeal 契约测试：定额化公式（生产值 基础上限 100 × 5% = 5/杀，
/// balance.json augments.lifesteal.base_hp_fraction）。
/// 「基数必须取基础上限、不得取含 extra_life 加成的当前上限」由签名（baseMaxHp）与调用点承担，
/// 纯函数测不了调用方传什么；本测试锁公式本身。病根背景：治疗曾按当前上限取比例，
/// extra_life 叠层同步放大单杀治疗（上限 600 时 60/杀），生存与续航投入复合成
/// 对死亡惩罚的完全中和（蒙特卡洛：400 池休闲档 10 分钟死亡率 裸装 97.5% → 旧口径 0.0%）。</summary>
public sealed class LifestealHealTests
{
    [Fact]
    public void ProductionValuesGiveFlatFivePerKill()
    {
        Assert.Equal(5, LifestealHeal.PerKill(100.0, 0.05));
    }

    [Fact]
    public void TruncatesAndFloorsAtOne()
    {
        Assert.Equal(5, LifestealHeal.PerKill(100.0, 0.057)); // 5.7 截断为 5（不四舍五入）
        Assert.Equal(1, LifestealHeal.PerKill(100.0, 0.0));   // 零比例保底 1
        Assert.Equal(1, LifestealHeal.PerKill(0.1, 0.05));    // 注入侧钳制后的极小上限仍保底 1
    }
}
