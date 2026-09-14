using InfiAir.Core.Storage;
using Xunit;

namespace InfiAir.Core.Tests.Storage;

/// <summary>
/// 设置档迁移/版本决策测试。
/// 这些判定原先只写在 Godot 侧 ApplySettingsDict 里，零回归面：改错了表现为「老玩家升级后
/// 分辨率/键位静默回默认」，既不崩也不报错，构建与冒烟都抓不到。
/// </summary>
public sealed class SettingsMigrationTests
{
    private static readonly string[] ValidResolutions =
        ["1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160"];

    private static string Resolve(Dictionary<string, object?> data, string fallback = "1920x1080") =>
        SettingsMigration.ResolveResolution(data, ValidResolutions, "custom", fallback);

    [Fact]
    public void TryMapKeyBindingAction_RenamesLegacyBuffPanel()
    {
        Assert.True(SettingsMigration.TryMapKeyBindingAction(
            Array.Empty<string>(), "buff_panel", out var mapped));
        Assert.Equal(SettingsMigration.AugmentPanelAction, mapped);
    }

    [Fact]
    public void TryMapKeyBindingAction_KeepsUnrelatedAction()
    {
        Assert.True(SettingsMigration.TryMapKeyBindingAction(
            Array.Empty<string>(), "fire", out var mapped));
        Assert.Equal("fire", mapped);
    }

    [Fact]
    public void TryMapKeyBindingAction_NewNameAlreadyBound_DropsLegacyEntry()
    {
        // 新名已在表中：旧名不得覆盖真值（优先级＝新键存在则不迁移）
        Assert.False(SettingsMigration.TryMapKeyBindingAction(
            new[] { "fire", SettingsMigration.AugmentPanelAction }, "buff_panel", out var mapped));
        Assert.Equal(SettingsMigration.LegacyBuffPanelAction, mapped);
    }

    [Theory]
    [InlineData("small", "1280x720")]
    [InlineData("medium", "1600x900")]
    [InlineData("large", "1920x1080")]
    [InlineData("unknown", "1920x1080")]
    [InlineData(null, "1920x1080")]
    public void MapLegacyWindowSize_MapsThreeTiers(string? legacy, string expected)
    {
        Assert.Equal(expected, SettingsMigration.MapLegacyWindowSize(legacy));
    }

    [Fact]
    public void ResolveResolution_NewKeyPresent_TakesPriorityOverLegacy()
    {
        var data = new Dictionary<string, object?>
        {
            ["resolution"] = "2560x1440",
            ["window_mode"] = "borderless",
            ["window_size"] = "small",
        };
        Assert.Equal("2560x1440", Resolve(data));
    }

    [Fact]
    public void ResolveResolution_CustomKey_IsAccepted()
    {
        Assert.Equal("custom", Resolve(new Dictionary<string, object?> { ["resolution"] = "custom" }));
    }

    [Fact]
    public void ResolveResolution_NoNewFormatKeys_MigratesLegacyWindowSize()
    {
        // 旧档只有 window_size（无 window_mode/resolution）：按三档映射迁移
        Assert.Equal("1280x720", Resolve(new Dictionary<string, object?> { ["window_size"] = "small" }));
        Assert.Equal("1600x900", Resolve(new Dictionary<string, object?> { ["window_size"] = "medium" }));
        Assert.Equal("1920x1080", Resolve(new Dictionary<string, object?> { ["window_size"] = "large" }));
    }

    [Fact]
    public void ResolveResolution_NewFormatWithoutLegacy_KeepsDefault()
    {
        // 新格式档位字段非法（手改字符串）：不得回退到 window_size 迁移路径（新格式已声明）
        var data = new Dictionary<string, object?>
        {
            ["window_mode"] = "windowed",
            ["resolution"] = "bogus",
        };
        Assert.Equal("1920x1080", Resolve(data));
    }

    [Fact]
    public void ResolveResolution_NoKeysAtAll_KeepsDefault()
    {
        Assert.Equal("1366x768", Resolve(new Dictionary<string, object?>(), "1366x768"));
    }

    [Fact]
    public void ResolveResolution_NonStringValues_FallBack()
    {
        Assert.Equal("1920x1080", Resolve(new Dictionary<string, object?> { ["resolution"] = 42L }));
        Assert.Equal("1920x1080", Resolve(new Dictionary<string, object?> { ["window_size"] = 42L }));
    }

    [Fact]
    public void DecideVersion_EqualIsAccepted_OlderMigrates_NewerRejected()
    {
        Assert.Equal(SaveVersionDecision.Accepted, SettingsMigration.DecideVersion(4, 4));
        Assert.Equal(SaveVersionDecision.Migrate, SettingsMigration.DecideVersion(3, 4));
        Assert.Equal(SaveVersionDecision.Migrate, SettingsMigration.DecideVersion(0, 4));
        // 版本高于当前（降级安装/手改）：保守拒绝，按默认值继续，不猜未来字段语义
        Assert.Equal(SaveVersionDecision.RejectNewer, SettingsMigration.DecideVersion(5, 4));
    }
}
