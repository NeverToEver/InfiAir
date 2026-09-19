using InfiAir.Core.Config;
using Xunit;

namespace InfiAir.Core.Tests.Config;

/// <summary>游戏 ↔ 数值管理器的联动协议测试。
/// 端口/身份串/参数是两边共用的契约（改一边忘一边表现为「点了没反应」，只在实机上才看得出来），
/// 重载判定则承载安全语义：拿不到有效时间戳时必须判否，否则会在导出包里反复重载。</summary>
public sealed class BalanceEditorLinkTests
{
    [Fact]
    public void Url_And_PingUrl_UsePort()
    {
        Assert.Equal("http://127.0.0.1:8931/", BalanceEditorLink.Url());
        Assert.Equal("http://127.0.0.1:8931/api/ping", BalanceEditorLink.PingUrl());
        Assert.Equal("http://127.0.0.1:9000/", BalanceEditorLink.Url(9000));
    }

    [Fact]
    public void AppId_MatchesToolContract()
    {
        // 与 scripts/tools/balance_editor.py 的 APP_ID 同串：端口被别的程序占用时据此拒绝复用
        Assert.Equal("infiair-balance-editor", BalanceEditorLink.AppId);
        Assert.Equal("scripts/tools/balance_editor.py", BalanceEditorLink.ToolScriptPath);
        Assert.Equal(8931, BalanceEditorLink.DefaultPort);
    }

    [Theory]
    [InlineData("Windows", "py")]
    [InlineData("Linux", "python3")]
    [InlineData("macOS", "python3")]
    public void PythonCandidates_PutPlatformFavouriteFirst(string osName, string first)
    {
        var candidates = BalanceEditorLink.PythonCandidates(osName);
        Assert.Equal(first, candidates[0]);
        Assert.Contains("python3", candidates);
    }

    [Fact]
    public void LaunchArguments_DisableSelfBrowser()
    {
        var args = BalanceEditorLink.LaunchArguments("/repo/scripts/tools/balance_editor.py");
        Assert.Equal(new[] { "/repo/scripts/tools/balance_editor.py", "--no-browser" }, args);
    }

    [Fact]
    public void StatusKey_PrefersUnavailableThenFailureThenBusy()
    {
        // 不可用最高：导出包里即便上一次失败过，也该说「仅源码运行可用」而不是弹一个玩家无从处理的错
        Assert.Equal("SET_MANAGER_STATUS_UNAVAILABLE",
            BalanceEditorLink.StatusKey(false, false, false, "SET_MANAGER_NO_PYTHON"));
        Assert.Equal("SET_MANAGER_NO_PYTHON",
            BalanceEditorLink.StatusKey(true, false, false, "SET_MANAGER_NO_PYTHON"));
        Assert.Equal("SET_MANAGER_STATUS_BUSY", BalanceEditorLink.StatusKey(true, false, true, ""));
        Assert.Equal("SET_MANAGER_STATUS_RUNNING", BalanceEditorLink.StatusKey(true, true, false, ""));
        Assert.Equal("SET_MANAGER_STATUS_STOPPED", BalanceEditorLink.StatusKey(true, false, false, ""));
    }

    [Fact]
    public void ToneOf_MatchesStatusKey()
    {
        Assert.Equal(BalanceEditorLink.StatusTone.Dim, BalanceEditorLink.ToneOf(false, true, false, ""));
        Assert.Equal(BalanceEditorLink.StatusTone.Failure, BalanceEditorLink.ToneOf(true, false, false, "SET_MANAGER_LAUNCH_FAILED"));
        Assert.Equal(BalanceEditorLink.StatusTone.Active, BalanceEditorLink.ToneOf(true, true, false, ""));
        // 启动中不算「活跃」：此刻管理器还没接上，用金色会读成「已就绪」
        Assert.Equal(BalanceEditorLink.StatusTone.Dim, BalanceEditorLink.ToneOf(true, true, true, ""));
        Assert.Equal(BalanceEditorLink.StatusTone.Dim, BalanceEditorLink.ToneOf(true, false, false, ""));
    }

    [Fact]
    public void NeedsReload_OnlyWhenBothTimestampsAreReal()
    {
        Assert.True(BalanceEditorLink.NeedsReload(100.5, 100.9));
        Assert.False(BalanceEditorLink.NeedsReload(100.5, 100.5));
        Assert.False(BalanceEditorLink.NeedsReload(0.0, 100.5));      // 从没记过基准
        Assert.False(BalanceEditorLink.NeedsReload(100.5, 0.0));      // 取不到时间戳（导出包/文件没了）
        Assert.False(BalanceEditorLink.NeedsReload(-1.0, 100.5));
        Assert.False(BalanceEditorLink.NeedsReload(double.NaN, 100.5));
        Assert.False(BalanceEditorLink.NeedsReload(100.5, double.PositiveInfinity));
    }
}
