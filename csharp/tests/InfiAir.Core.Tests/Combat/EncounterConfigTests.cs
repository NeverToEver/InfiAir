using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>遭遇单位配置的条目级读取：元素类型损坏（Godot 的 As* 宽松转换得 0，不抛）时
/// 必须回退默认——fire_interval 恒 0 会让炮台每物理帧开火，turn_rate 恒 0 会让炮台不再转向。</summary>
public sealed class EncounterConfigTests
{
    private const float Floor = 0.05f;

    [Fact]
    public void BrokenElement_FallsBackInsteadOfZero()
    {
        // 判别式：判型失败（isValid=false）时取默认 2.0/2.4，而不是被宽松转换成 0
        var (min, max) = EncounterConfig.Range(false, 0.0f, false, 0.0f, 2.0f, 2.4f, Floor);
        Assert.Equal(2.0f, min);
        Assert.Equal(2.4f, max);
        Assert.True(min >= Floor && max >= Floor, "回退值也必须在下界之上，否则炮台每帧开火");
    }

    [Fact]
    public void HalfBrokenElement_KeepsTheValidOne()
    {
        var (min, max) = EncounterConfig.Range(false, 0.0f, true, 3.5f, 2.0f, 2.4f, Floor);
        Assert.Equal(2.0f, min);
        Assert.Equal(3.5f, max);
    }

    [Fact]
    public void BelowFloor_IsClampedNotZero()
    {
        // 合法数值但小于下界（含 0）→ 钳到下界：0 会让 _fireTimer 恒 ≤0、炮台每物理帧开火
        var (min, max) = EncounterConfig.Range(true, 0.0f, true, 0.01f, 2.0f, 2.4f, Floor);
        Assert.Equal(Floor, min);
        Assert.Equal(Floor, max);
    }

    [Fact]
    public void Negative_IsTreatedAsCorruption()
    {
        // 负值经下游 clamp/取反会变成另一套行为（如负 turn_rate 使转向钳制区间倒置），回退默认
        var (min, max) = EncounterConfig.Range(true, -2.0f, true, -2.4f, 2.0f, 2.4f, Floor);
        Assert.Equal(2.0f, min);
        Assert.Equal(2.4f, max);
    }

    [Fact]
    public void InvertedRange_IsOrderedNotReversed()
    {
        // 上界低于下界（配置写反）经 RandRange 会反向取值：保序为 (max, min)
        var (min, max) = EncounterConfig.Range(true, 3.0f, true, 1.0f, 2.0f, 2.4f, Floor);
        Assert.Equal(1.0f, min);
        Assert.Equal(3.0f, max);
    }

    [Fact]
    public void ZeroFloorKey_KeepsZero()
    {
        // turn_rate / spread_deg 等下界为 0 的键：0 合法（不会转向是配置选择），负值回退默认
        Assert.Equal(0.0f, EncounterConfig.RangeEndpoint(true, 0.0f, 2.0f, 0.0f));
        Assert.Equal(2.0f, EncounterConfig.RangeEndpoint(true, -3.0f, 2.0f, 0.0f));
        Assert.Equal(2.0f, EncounterConfig.RangeEndpoint(false, 0.0f, 2.0f, 0.0f));
    }

    [Fact]
    public void NonFinite_FallsBack()
    {
        Assert.Equal(2.0f, EncounterConfig.RangeEndpoint(true, float.NaN, 2.0f, Floor));
        Assert.Equal(2.0f, EncounterConfig.RangeEndpoint(true, float.PositiveInfinity, 2.0f, Floor));
    }
}
