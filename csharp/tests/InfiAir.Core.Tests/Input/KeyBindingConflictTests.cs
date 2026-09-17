using InfiAir.Core.Input;
using Xunit;

namespace InfiAir.Core.Tests.Input;

/// <summary>改键冲突清理契约：冲突键**只从占用者移除该键**，其余绑定保留。
/// 实现若退化为「把占用者整套默认绑定清空」，玩家会被连带打掉同一动作的另一个键
/// （project.godot 的 4 个移动动作都是双键默认值），设置页显示「未绑定」。</summary>
public sealed class KeyBindingConflictTests
{
    private static readonly string[] Actions =
    {
        "move_up", "move_down", "dash", "boost",
    };

    private static Dictionary<string, int[]> Defaults() => new()
    {
        ["move_up"] = new[] { 87, 4194320 }, // W + ↑
        ["move_down"] = new[] { 83, 4194322 }, // S + ↓
        ["dash"] = new[] { 4194321 }, // ↑ 之外的 Shift？此处仅作单键占用者
        ["boost"] = new[] { 4194325 },
    };

    [Fact]
    public void Cleanup_DefaultOccupier_KeepsRemainingDefaultKeys()
    {
        // 回归核心：把 dash 改到 W 抢占 move_up 的默认键，move_up 必须仍保留 ↑
        var changes = KeyBindingConflict.Cleanup(Defaults(), new Dictionary<string, int[]>(), Actions, "dash", 87);

        Assert.Equal(new[] { 4194320 }, changes["move_up"]);
        Assert.DoesNotContain("move_down", changes.Keys); // 未被抢占的动作不得写覆盖条目
        Assert.Equal(new[] { 87 }, changes["dash"]);
    }

    [Fact]
    public void Cleanup_DefaultOccupier_SingleKeyBecomesUnbound()
    {
        // 默认只有一键的动作被抢走该键后，覆盖为空数组（= 未绑定），不得回落到默认表重灌
        var changes = KeyBindingConflict.Cleanup(Defaults(), new Dictionary<string, int[]>(), Actions, "dash", 4194325);
        Assert.Empty(changes["boost"]);
    }

    [Fact]
    public void Cleanup_CustomOccupier_RemovesOnlyConflictingKey()
    {
        var overrides = new Dictionary<string, int[]>
        {
            ["move_up"] = new[] { 87, 88, 4194320 }, // 玩家自定义三键
        };

        var changes = KeyBindingConflict.Cleanup(Defaults(), overrides, Actions, "dash", 88);
        Assert.Equal(new[] { 87, 4194320 }, changes["move_up"]);
    }

    [Fact]
    public void Cleanup_DoesNotMutateInputs()
    {
        var defaults = Defaults();
        var overrides = new Dictionary<string, int[]> { ["move_up"] = new[] { 87, 88 } };

        KeyBindingConflict.Cleanup(defaults, overrides, Actions, "dash", 87);
        Assert.Equal(new[] { 87, 4194320 }, defaults["move_up"]);
        Assert.Equal(new[] { 87, 88 }, overrides["move_up"]);
    }

    [Fact]
    public void Cleanup_UnoccupiedTarget_OnlyWritesTargetAction()
    {
        var changes = KeyBindingConflict.Cleanup(Defaults(), new Dictionary<string, int[]>(), Actions, "dash", 90);
        Assert.Equal(new[] { 90 }, Assert.Single(changes).Value);
        Assert.Equal("dash", Assert.Single(changes).Key);
    }

    [Fact]
    public void Cleanup_ActionWithoutAnyBinding_ProducesNoEntry()
    {
        // 动作在两张表里都没有条目（无默认、无覆盖）= 不占用任何键，不得产生空覆盖条目
        var actions = new[] { "move_up", "dash", "never_bound" };
        var changes = KeyBindingConflict.Cleanup(Defaults(), new Dictionary<string, int[]>(), actions, "dash", 87);
        Assert.DoesNotContain("never_bound", changes.Keys);
    }
}
