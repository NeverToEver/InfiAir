using Godot;

namespace InfiAir.Tests;

/// <summary>
/// welcome 主场景流程测试（2026-08-04 账户系统 T3）：注册/登录/游客/删除、焦点环、
/// ESC 层级（overlay→模态→退出确认）、排行榜 overlay、难度持久化、主区显隐。
/// 场景切换路径（继续/新游戏 → main.tscn）由既有对局测试适配覆盖（T4），本测试不切换场景。
/// </summary>
public partial class WelcomeFlowTest : Node
{
    private static readonly string[] UserFiles =
    {
        "user://users.json", "user://users.json.corrupt", "user://profile.json", "user://savegame.json",
    };

    private int _failures;
    private Welcome _welcome = null!;

    /// <summary>Q23（2026-08-05）：开头备份、结尾还原用户文件——本地跑测试不再永久销毁开发者账户表</summary>
    private Godot.Collections.Dictionary _fileBackups = new();

    private void Check(bool cond, string label)
    {
        if (cond)
        {
            GD.Print("[PASS] " + label);
        }
        else
        {
            _failures++;
            GD.PushError("[FAIL] " + label);
        }
    }

    private void WipeUserFiles()
    {
        foreach (var f in UserFiles)
        {
            if (Godot.FileAccess.FileExists(f))
            {
                DirAccess.RemoveAbsolute(f);
            }
        }
    }

    private void BackupUserFiles()
    {
        _fileBackups = new Godot.Collections.Dictionary();
        foreach (var f in UserFiles)
        {
            var exists = Godot.FileAccess.FileExists(f);
            _fileBackups[f] = new Godot.Collections.Dictionary
            {
                ["exists"] = exists,
                ["content"] = exists ? Godot.FileAccess.GetFileAsString(f) : "",
            };
        }
    }

    private void RestoreUserFiles()
    {
        foreach (var fKey in _fileBackups.Keys)
        {
            var f = fKey.AsString();
            var b = _fileBackups[f].AsGodotDictionary();
            if (b["exists"].AsBool())
            {
                var fh = Godot.FileAccess.Open(f, Godot.FileAccess.ModeFlags.Write);
                fh.StoreString(b["content"].AsString());
                fh.Close();
            }
            else if (Godot.FileAccess.FileExists(f))
            {
                DirAccess.RemoveAbsolute(f);
            }
        }
    }

    /// <summary>Q24（2026-08-05）：真实按键事件走完整输入管线（原直调 _unhandled_input 绕过输入管线，
    /// C30 已修模式回归；esc_navigation_test 同款 InputEventKey + parse_input_event）</summary>
    private async Task PressEsc()
    {
        var ev = new InputEventKey();
        ev.Keycode = Key.Escape;
        ev.Pressed = true;
        Input.ParseInputEvent(ev);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var up = new InputEventKey();
        up.Keycode = Key.Escape;
        up.Pressed = false;
        Input.ParseInputEvent(up);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task PressEnter()
    {
        var ev = new InputEventKey();
        ev.Keycode = Key.Enter;
        ev.Pressed = true;
        Input.ParseInputEvent(ev);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var up = new InputEventKey();
        up.Keycode = Key.Enter;
        up.Pressed = false;
        Input.ParseInputEvent(up);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var gs = GetNode<GameState>("/root/GameState");
        try
        {
            gs.LogoutUser();
            BackupUserFiles(); // Q23：快照用户文件，结尾还原
            WipeUserFiles();
            gs.ReloadUserDb(); // 2026-08-06 审计：GameState._ready 迁移探测缓存了真实用户表，wipe 后须刷新
            gs.HighScore = 0;
            gs.Difficulty = new StringName("medium");

            _welcome = (Welcome)GD.Load<PackedScene>("res://scenes/welcome.tscn").Instantiate();
            AddChild(_welcome);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 1. 初始状态：登录阶段、登录面板可见、主区隐藏
            Check(!_welcome.MainZoneVisible(), "初始为登录阶段");
            Check(_welcome.UsernameLine() != null && _welcome.PasswordLine() != null, "登录面板输入框存在");
            Check(_welcome.PasswordLine().Secret, "密码框掩码");

            // 2. 注册校验：短名/短密码/空 → 错误消息；保留名被拒
            _welcome.UsernameLine().Text = "ab";
            _welcome.PasswordLine().Text = "pass123";
            _welcome.PressRegister();
            Check(!gs.UserExists("ab"), "短用户名注册被拒");
            _welcome.UsernameLine().Text = "carol";
            _welcome.PasswordLine().Text = "pw";
            _welcome.PressRegister();
            Check(!gs.UserExists("carol"), "短密码注册被拒");
            _welcome.UsernameLine().Text = "";
            _welcome.PasswordLine().Text = "";
            _welcome.PressRegister();
            Check(!gs.UserExists(""), "空凭证注册被拒");
            _welcome.UsernameLine().Text = "Guest";
            _welcome.PasswordLine().Text = "pass123";
            _welcome.PressRegister();
            Check(!gs.UserExists("Guest"), "保留名 Guest 注册被拒");

            // 3. 注册成功：保留用户名、清空密码（B7-9）
            _welcome.UsernameLine().Text = "pilot";
            _welcome.PasswordLine().Text = "s3cret";
            _welcome.PressRegister();
            Check(gs.UserExists("pilot"), "注册成功入库");
            Check(_welcome.UsernameLine().Text == "pilot", "注册成功保留用户名（B7-9）");
            Check(_welcome.PasswordLine().Text == "", "注册成功清空密码（B7-9）");

            // 4. 错误密码登录被拒，仍处登录阶段
            _welcome.UsernameLine().Text = "pilot";
            _welcome.PasswordLine().Text = "wrong";
            _welcome.PressLogin();
            Check(!_welcome.MainZoneVisible(), "错误凭证登录被拒");

            // 5. 正确登录 → 主区显示、会话置位、难度按钮同步
            _welcome.PasswordLine().Text = "s3cret";
            _welcome.PressLogin();
            Check(_welcome.MainZoneVisible(), "正确凭证登录放行");
            Check(gs.CurrentUser == "pilot", "登录置位当前用户");
            Check(!gs.IsGuest(), "登录非游客");

            // 6. 难度单选：切换持久化（主区难度按钮）
            Check(gs.Difficulty == new StringName("medium"), "主区难度初始为档案值");

            // 7. 排行榜 overlay：打开（空榜显示占位）→ 关闭
            _welcome.PressLeaderboard();
            Check(_welcome.LeaderboardOverlay().Visible, "排行榜 overlay 打开");
            await PressEsc();
            Check(!_welcome.LeaderboardOverlay().Visible, "ESC 关闭排行榜 overlay（B7-1）");
            _welcome.PressLeaderboard();
            _welcome.CloseLeaderboard();
            Check(!_welcome.LeaderboardOverlay().Visible, "× 关闭排行榜 overlay");

            // 8. 游客流程：确认框（B7-6 统一走确认；B7-5 默认焦点返回）
            gs.LogoutUser();
            _welcome.QueueFree(); // 释放旧实例（Q09 修复：残留实例的 SettingsUI 抢占 group，press_settings 命中错误节点）
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _welcome = (Welcome)GD.Load<PackedScene>("res://scenes/welcome.tscn").Instantiate();
            AddChild(_welcome);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _welcome.PressGuest();
            Check(_welcome.GuestConfirm().Visible, "游客按钮弹确认框");
            await PressEsc();
            Check(!_welcome.GuestConfirm().Visible, "ESC 关闭游客确认（B7-2）");
            _welcome.PressGuest();
            _welcome.ConfirmGuest();
            Check(_welcome.MainZoneVisible() && gs.IsGuest(), "游客放行进入主区");

            // 9. 删除流程：确认前密码非空校验；确认后删号清空表单
            gs.LogoutUser();
            _welcome.QueueFree(); // 释放旧实例（Q09 修复，同场景 8）
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _welcome = (Welcome)GD.Load<PackedScene>("res://scenes/welcome.tscn").Instantiate();
            AddChild(_welcome);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _welcome.UsernameLine().Text = "pilot";
            _welcome.PasswordLine().Text = "";
            _welcome.PressDelete();
            Check(!_welcome.DeleteConfirm().Visible, "密码为空时不弹删除确认（B7-13 先验密码）");
            _welcome.PasswordLine().Text = "s3cret";
            _welcome.PressDelete();
            Check(_welcome.DeleteConfirm().Visible, "删除确认框弹出");
            _welcome.ConfirmDelete();
            Check(!gs.UserExists("pilot"), "删除确认后用户移除");
            Check(_welcome.UsernameLine().Text == "", "删除成功清空用户名（B7-9）");

            // 10. ESC 层级：无 overlay/模态时 → 退出确认；退出确认关闭
            await PressEsc();
            Check(_welcome.ExitConfirmLayer().Visible, "顶层 ESC 弹退出确认");
            await PressEsc();
            Check(!_welcome.ExitConfirmLayer().Visible, "ESC 关闭退出确认");

            // 11. 空凭证 ENTER → 游客确认框（B7-5 防连按游客开局）
            _welcome.UsernameLine().Text = "";
            _welcome.PasswordLine().Text = "";
            await PressEnter();
            Check(_welcome.GuestConfirm().Visible, "空凭证 ENTER 弹游客确认框");
            await PressEsc();
            Check(!_welcome.GuestConfirm().Visible, "ENTER 后的游客确认 ESC 关闭");

            // 12. Q09（2026-08-05）：设置页打开时 Esc 关闭设置页（原 Esc 落到隐藏层退出确认，
            // 设置页永远关不掉——与 EXIT_FLOW「settings back = Esc」矛盾）
            _welcome.PressSettings();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var settingsUi = (CanvasLayer)_welcome.GetNode("SettingsUI");
            Check(settingsUi.Visible, "Q09：设置页打开");
            Check(!_welcome.Visible, "Q09：welcome 主层随设置页打开隐藏");
            await PressEsc();
            Check(!settingsUi.Visible, "Q09：设置页 Esc 关闭");
            Check(_welcome.Visible, "Q09：welcome 主层恢复显示");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"WELCOME FLOW TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"WELCOME FLOW TEST DONE, failures = {_failures}");
            gs.LogoutUser();
            RestoreUserFiles(); // Q23：还原用户文件快照
            TestExit.Quit(_failures);
        }
    }
}
