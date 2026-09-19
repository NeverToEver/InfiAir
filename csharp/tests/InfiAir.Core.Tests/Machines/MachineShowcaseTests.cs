using System.Collections.Generic;
using InfiAir.Core.Machines;
using Xunit;

namespace InfiAir.Core.Tests.Machines;

/// <summary>机型特性演出映射契约：**六型各演各的**。守的静默错误＝加了一型却漏配演出——
/// 它会静默落回基准演出，玩家看到「新型跟标准型演同一段」，而代码零报错、零警告。</summary>
public sealed class MachineShowcaseTests
{
    [Fact]
    public void EveryMachineHasItsOwnShowcase()
    {
        var seen = new Dictionary<MachineShowcaseKind, string>();
        foreach (var spec in MachineRoster.All)
        {
            var kind = MachineShowcase.KindOf(spec.Id);
            Assert.False(
                seen.TryGetValue(kind, out var other),
                $"{spec.Id} 与 {other} 演同一段（{kind}）——每型一段专属演出");
            seen[kind] = spec.Id;
        }
    }

    [Fact]
    public void StandardIsTheBaselineShowcase_AndUnknownIdsFallBackToIt()
    {
        Assert.Equal(MachineShowcaseKind.Baseline, MachineShowcase.KindOf(MachineRoster.StandardId));
        Assert.Equal(MachineShowcaseKind.Baseline, MachineShowcase.KindOf(null));
        Assert.Equal(MachineShowcaseKind.Baseline, MachineShowcase.KindOf(string.Empty));
        Assert.Equal(MachineShowcaseKind.Baseline, MachineShowcase.KindOf("no-such-machine"));
    }

    [Fact]
    public void EveryShowcaseKindIsUsedByExactlyOneMachine()
    {
        // 枚举值全部有主：新加一种演出却没人用时，要么是名册改了没跟上，要么是枚举里留了死值
        var used = new HashSet<MachineShowcaseKind>();
        foreach (var spec in MachineRoster.All)
        {
            used.Add(MachineShowcase.KindOf(spec.Id));
        }

        foreach (var kind in System.Enum.GetValues<MachineShowcaseKind>())
        {
            Assert.Contains(kind, used);
        }

        Assert.Equal(used.Count, System.Enum.GetValues<MachineShowcaseKind>().Length);
    }
}
