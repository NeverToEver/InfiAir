using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 启动流程测试（2026-08-04 账户系统 T4 重写）：welcome 主场景入口状态、
/// 存档/档案损坏隔离提示、教程按钮门控、main 启动自动读档继续。
/// </summary>
public partial class StartupFlowTest : Node
{
    private int _failures;
    private GameState _gs = null!;

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
        foreach (var f in new[]
        {
            "user://users.json",
            "user://users.json.corrupt",
            "user://profile.json",
            "user://profile.json.corrupt",
            "user://savegame.json",
            "user://savegame.json.corrupt",
        })
        {
            if (Godot.FileAccess.FileExists(f))
            {
                DirAccess.RemoveAbsolute(f);
            }
        }
    }

    private void WipeUserSaves()
    {
        var dir = DirAccess.Open("user://");
        if (dir == null)
        {
            return;
        }
        dir.ListDirBegin();
        var name = dir.GetNext();
        while (name != "")
        {
            if (name.StartsWith("savegame_") || name.EndsWith(".corrupt"))
            {
                DirAccess.RemoveAbsolute("user://" + name);
            }
            name = dir.GetNext();
        }
        dir.ListDirEnd();
    }

    /// <summary>
    /// Q23（2026-08-05）：开头备份、结尾还原用户文件——本地跑测试不再永久销毁开发者账户表
    /// </summary>
    private Godot.Collections.Dictionary _fileBackups = new();

    private void BackupUserFiles()
    {
        _fileBackups = new Godot.Collections.Dictionary();
        var files = new Godot.Collections.Array<string>
        {
            "user://users.json",
            "user://users.json.corrupt",
            "user://profile.json",
            "user://profile.json.corrupt",
            "user://savegame.json",
            "user://savegame.json.corrupt",
        };
        var dir = DirAccess.Open("user://");
        if (dir != null)
        {
            dir.ListDirBegin();
            var name = dir.GetNext();
            while (name != "")
            {
                if (name.StartsWith("savegame_") || name.EndsWith(".corrupt"))
                {
                    files.Add("user://" + name);
                }
                name = dir.GetNext();
            }
            dir.ListDirEnd();
        }
        foreach (var f in files)
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
        foreach (var key in _fileBackups.Keys)
        {
            var f = key.AsString();
            var b = _fileBackups[key].AsGodotDictionary();
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

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            _gs = GetNode<GameState>("/root/GameState");
            // R07（2026-08-05 独立审计）：Q23 修复顺序遗漏——delete_save() 原在快照之前执行，
            // savegame.json 快照捕捉的是已删除状态，结尾还原后进行中存档仍缺失（开发者存档
            // 被销毁）；备份必须捕获全部改动路径之前的原始状态
            BackupUserFiles();
            _gs.LogoutUser();
            _gs.DeleteSave();
            WipeUserFiles();
            WipeUserSaves();
            _gs.ReloadUserDb();  // 2026-08-06 审计：GameState._ready 迁移探测缓存了真实用户表，wipe 后须刷新
            _gs.HighScore = 0;
            _gs.ProfileCorrupt = false;
            _gs.SaveCorrupt = false;

            var welcomeScene = GD.Load<PackedScene>("res://scenes/welcome.tscn");
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");

            // 1. 注册 + 登录（无存档）：主区显示「开始游戏」且无「继续对局」
            Welcome welcome = (Welcome)welcomeScene.Instantiate();
            AddChild(welcome);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!welcome.MainZoneVisible(), "未登录时处于登录阶段");
            Check(_gs.CreateUser("flow", "pass123"), "注册测试用户");
            welcome.UsernameLine().Text = "flow";
            welcome.PasswordLine().Text = "pass123";
            welcome.PressLogin();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(welcome.MainZoneVisible(), "登录放行进入主区");
            Check(!_gs.HasSave(), "无存档时 has_save 为 false");
            Check(!welcome.ContinueButton().Visible, "无存档时不显示继续对局");
            welcome.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 2. 有存档：主区显示「继续对局」+ 开始游戏为次按钮
            _gs.LoginUser("flow");
            _gs.SaveRun(50.0, 10.0);
            Check(_gs.HasSave(), "登录用户存档存在");
            welcome = (Welcome)welcomeScene.Instantiate();
            AddChild(welcome);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            welcome.UsernameLine().Text = "flow";
            welcome.PasswordLine().Text = "pass123";
            welcome.PressLogin();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(welcome.ContinueButton().Visible, "有存档时显示继续对局");
            Check(welcome.NewButton().Text != "", "开始游戏按钮存在");
            welcome.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 3. 损坏存档：隔离备份 + 继续按钮隐藏 + 损坏提示可见
            var savePath = _gs.UserDbSavefileFor("flow");
            var f = Godot.FileAccess.Open(savePath, Godot.FileAccess.ModeFlags.Write);
            f.StoreString("{broken json");
            f.Close();
            _gs.LoginUser("flow");
            Check(_gs.LoadRunData().Count == 0 && _gs.SaveCorrupt, "损坏存档隔离并按无存档处理");
            Check(!_gs.HasSave(), "损坏存档隔离后 has_save 为 false");
            // M2（2026-08-06 审计）：损坏备份必须保留——原 load_run_data 对空字典继续做档主
            // 校验，quarantine 二次隔离先删刚生成的 .corrupt 再 rename 不存在的正本（损坏档
            // 彻底消失 + 伪警告）；修复后备份存在、正本已隔离
            Check(Godot.FileAccess.FileExists(savePath + ".corrupt"), "损坏存档 .corrupt 备份保留（M2）");
            Check(!Godot.FileAccess.FileExists(savePath), "损坏存档正本已隔离（M2）");
            welcome = (Welcome)welcomeScene.Instantiate();
            AddChild(welcome);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            welcome.UsernameLine().Text = "flow";
            welcome.PasswordLine().Text = "pass123";
            welcome.PressLogin();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!welcome.ContinueButton().Visible, "损坏存档后继续对局隐藏");
            Check(welcome.CorruptLabel().Visible, "损坏存档提示可见");
            Check(welcome.CorruptLabel().Text != "START_SAVE_CORRUPT", "损坏提示文案已翻译（tr 命中）");
            welcome.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 4. 损坏档案（profile.json）：损坏提示可见且文案为档案口径
            _gs.LogoutUser();
            var legacy = Godot.FileAccess.Open("user://profile.json", Godot.FileAccess.ModeFlags.Write);
            legacy.StoreString("{broken profile");
            legacy.Close();
            _gs.ProfileCorrupt = false;
            _gs.LoadProfile();
            Check(_gs.ProfileCorrupt, "损坏档案隔离置 profile_corrupt");
            welcome = (Welcome)welcomeScene.Instantiate();
            AddChild(welcome);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            welcome.UsernameLine().Text = "flow";
            welcome.PasswordLine().Text = "pass123";
            welcome.PressLogin();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(welcome.CorruptLabel().Visible, "损坏档案提示可见");
            Check(welcome.CorruptLabel().Text != "START_SAVE_CORRUPT", "损坏档案文案区分于存档");
            welcome.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 5. 教程门控：进行中存档禁用教程；通关且无存档时放行
            _gs.LoginUser("flow");
            _gs.SaveRun(50.0, 10.0);
            welcome = (Welcome)welcomeScene.Instantiate();
            AddChild(welcome);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            welcome.UsernameLine().Text = "flow";
            welcome.PasswordLine().Text = "pass123";
            welcome.PressLogin();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(welcome.TutorialButton().Disabled, "进行中存档时教程按钮禁用（防删档）");
            welcome.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _gs.DeleteSave();
            _gs.TutorialDone = true;
            _gs.SaveProfile();
            welcome = (Welcome)welcomeScene.Instantiate();
            AddChild(welcome);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            welcome.UsernameLine().Text = "flow";
            welcome.PasswordLine().Text = "pass123";
            welcome.PressLogin();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!welcome.TutorialButton().Disabled, "通关且无存档时教程按钮放行");
            welcome.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 6. main 启动自动继续：登录用户有档 → 实例化 main → 分数/对局恢复（T3 新逻辑）
            _gs.LoginUser("flow");
            _gs.Score = 12345;
            _gs.Kills = 7;
            _gs.SaveRun(50.0, 10.0);
            _gs.Score = 0;
            var main = (Main)mainScene.Instantiate();
            AddChild(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_gs.Score == 12345, "main 启动自动读档恢复分数");
            Check(_gs.Kills == 7, "main 启动自动读档恢复击杀");
            Check(!GetTree().Paused, "继续对局不冻结（无开始面板暂停门控）");
            main.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"STARTUP FLOW TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"STARTUP FLOW TEST DONE, failures = {_failures}");
            Cleanup();
            TestExit.Quit(_failures);
        }
    }

    private void Cleanup()
    {
        try
        {
            _gs.LogoutUser();
            _gs.DeleteSave();
            _gs.ResetRun();
            RestoreUserFiles();  // Q23：还原用户文件快照（原 wipe 不还原，永久销毁开发者账户表）
            WipeUserSaves();
        }
        catch (System.Exception e)
        {
            GD.PushError($"STARTUP FLOW TEST 清理异常: {e}");
        }
    }
}
