using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 用户会话测试（2026-08-04 账户系统 T2）：profile.json 退役迁移合并、登录/游客/登出会话、
/// 每用户存档隔离与档主校验、排行榜/统计按会话路由。只操作 GameState autoload 与 user:// 文件。
/// </summary>
public partial class UserSessionTest : Node
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

    /// <summary>清空 user:// 全部游戏文件（含每用户存档与隔离备份），保证测试确定性</summary>
    private void WipeUserFiles()
    {
        foreach (var f in new[]
        {
            "user://profile.json",
            "user://profile.json.corrupt",
            "user://savegame.json",
            "user://savegame.json.corrupt",
            "user://users.json",
            "user://users.json.corrupt",
        })
        {
            if (Godot.FileAccess.FileExists(f))
            {
                DirAccess.RemoveAbsolute(f);
            }
        }
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
    /// （L15 快照范式推广；原 _wipe_user_files 删档不还原）
    /// </summary>
    private Godot.Collections.Dictionary _fileBackups = new();

    private void BackupUserFiles()
    {
        _fileBackups = new Godot.Collections.Dictionary();
        var files = new Godot.Collections.Array<string>
        {
            "user://profile.json",
            "user://profile.json.corrupt",
            "user://savegame.json",
            "user://savegame.json.corrupt",
            "user://users.json",
            "user://users.json.corrupt",
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

    private void ResetMembers()
    {
        _gs.HighScore = 0;
        _gs.TutorialDone = false;
        _gs.Difficulty = "medium";
        _gs.Locale = "zh";
        _gs.ViewZoom = "small";
        _gs.WindowSize = "large";
        _gs.AimAssistLevel = "medium";
        _gs.CtrlToggleMode = false;
        _gs.ShiftToggleMode = false;
        _gs.ReduceFlash = false;
        _gs.MouseLock = true;
        _gs.Highscores.Clear();
        _gs.KeyBindings.Clear();
        _gs.ClearLegacyMigration();  // Q25：公开接口复位（原直写 _pending_legacy_profile）
    }

    public override void _Ready()
    {
        Run();
    }

    private void Run()
    {
        try
        {
            _gs = GetNode<GameState>("/root/GameState");
            _gs.LogoutUser();
            BackupUserFiles();  // Q23：快照用户文件，结尾还原
            WipeUserFiles();
            _gs.ReloadUserDb();  // 2026-08-06 审计：GameState._ready 迁移探测缓存了真实用户表，wipe 后须刷新
            ResetMembers();

            // 1. profile.json 退役迁移：存在旧档案且无用户 → 首个注册用户合并后删除
            var legacy = Godot.FileAccess.Open("user://profile.json", Godot.FileAccess.ModeFlags.Write);
            legacy.StoreString(Json.Stringify(new Godot.Collections.Dictionary
            {
                ["version"] = 2,
                ["high_score"] = 5000,
                ["difficulty"] = "hard",
                ["locale"] = "en",
                ["tutorial_done"] = true,
                ["view_zoom"] = "large",
                ["window_size"] = "large",
                ["aim_assist"] = "medium",
                ["reduce_flash"] = false,
                ["mouse_lock"] = true,
                ["key_bindings"] = new Godot.Collections.Dictionary(),
                ["highscores"] = new Godot.Collections.Array(),
            }));
            legacy.Close();
            _gs.ScanLegacyMigration();  // Q25：公开触发（原直调 _maybe_migrate_legacy_profile）
            Check(_gs.LegacyMigrationPending(), "迁移缓存旧 profile");  // Q25：公开查询
            Check(_gs.CreateUser("migrator", "pass123"), "迁移后注册用户成功");
            Check(!Godot.FileAccess.FileExists("user://profile.json"), "迁移后 profile.json 删除");
            var migratorSettings = _gs.GetUserSettings("migrator");
            Check(migratorSettings.GetValueOrDefault("difficulty", "").AsString() == "hard", "迁移：难度并入新用户设置");
            Check(migratorSettings.GetValueOrDefault("locale", "").AsString() == "en", "迁移：locale 并入新用户设置");
            Check(migratorSettings.GetValueOrDefault("tutorial_done", false).AsBool(), "迁移：教程标记并入新用户设置");
            Check(_gs.GetUserData("migrator").GetValueOrDefault("high_score", 0).AsInt32() == 5000, "迁移：最高分并入新用户统计");
            Check(!_gs.LegacyMigrationPending(), "迁移后缓存清空");

            // 2. 登录会话：载入用户设置并即时生效（B7-11 locale）
            Check(_gs.CreateUser("alice", "pass123"), "注册 alice");
            _gs.UpdateUserSettings("alice", new Godot.Collections.Dictionary { ["difficulty"] = "hard", ["locale"] = "en" });
            _gs.LoginUser("alice");
            Check(_gs.CurrentUser == "alice", "登录置位当前用户");
            Check(_gs.Difficulty == "hard", "登录载入用户难度");
            Check(_gs.Locale == "en" && TranslationServer.GetLocale() == "en", "登录 locale 即时生效");
            _gs.SetLocale("zh");  // 复位语言（设置写入 alice 档案）
            Check(_gs.IsGuest() == false, "登录态非游客");
            // 设置变更经成员落盘：登出写档案 → 重登载入
            _gs.Difficulty = "easy";
            _gs.LogoutUser();
            _gs.LoginUser("alice");
            Check(_gs.Difficulty == "easy", "登出重登载入最新设置");
            Check(_gs.CurrentUser == "alice", "重登后会话正确");

            // 3. 游客会话：仅内存、不落盘、不入表
            _gs.LoginGuest();
            Check(_gs.IsGuest() && _gs.CurrentUser == "Guest", "游客会话置位");
            _gs.SaveProfile();
            Check(!Godot.FileAccess.FileExists("user://profile.json"), "游客 save_profile 不落盘");
            Check(!_gs.UserExists("Guest"), "游客不入用户表");
            _gs.LogoutUser();
            Check(_gs.CurrentUser == "", "登出复位未登录");

            // 4. 每用户存档隔离：登录写用户路径；档主不匹配隔离；游客不存档；未登录走旧路径
            _gs.LoginUser("alice");
            _gs.SaveRun(50.0, 10.0);
            var savePath = _gs.UserDbSavefileFor("alice");
            Check(Godot.FileAccess.FileExists(savePath), "登录用户存档写入每用户路径");
            Check(_gs.HasSave(), "登录用户 has_save 命中用户档");
            Check(_gs.LoadRunData().GetValueOrDefault("username", "").AsString() == "alice", "存档含档主用户名");
            // 手改档主 → 隔离备份按无存档处理（B5）
            var tampered = Godot.FileAccess.Open(savePath, Godot.FileAccess.ModeFlags.Write);
            tampered.StoreString(Json.Stringify(new Godot.Collections.Dictionary { ["username"] = "bob", ["score"] = 1 }));
            tampered.Close();
            Check(_gs.LoadRunData().Count == 0 && _gs.SaveCorrupt, "档主不匹配按无存档处理");
            Check(Godot.FileAccess.FileExists(savePath + ".corrupt"), "不匹配档隔离备份");
            _gs.DeleteSave();
            Check(!_gs.HasSave(), "登录用户 delete_save 清除用户档");
            _gs.LoginGuest();
            _gs.SaveRun(50.0, 10.0);
            Check(!_gs.HasSave(), "游客不存档、has_save 恒 false");
            _gs.LogoutUser();
            _gs.SaveRun(50.0, 10.0);
            Check(_gs.HasSave(), "未登录存档走旧单文件路径（兼容）");
            _gs.DeleteSave();
            Check(!Godot.FileAccess.FileExists("user://savegame.json"), "未登录 delete_save 清除旧档");

            // 5. 排行榜与统计按会话路由
            _gs.LoginUser("alice");
            Check(_gs.SubmitHighscore(100) == 1, "登录用户成绩入 user_db 榜");
            Check(_gs.HighscoresText(3) == "1. 100", "登录用户榜单文本走 user_db");
            _gs.Score = 200;
            Check(_gs.RecordScore(), "登录用户破纪录");
            Check(_gs.GetUserData("alice").GetValueOrDefault("high_score", 0).AsInt32() == 200, "登录用户纪录写入 user_db");
            _gs.Score = 0;
            _gs.LoginGuest();
            _gs.HighScore = 0;
            _gs.Score = 300;
            Check(_gs.RecordScore(), "游客破纪录（仅内存）");
            Check(_gs.HighScore == 300, "游客纪录仅存内存");
            Check(_gs.GetUserData("alice").GetValueOrDefault("high_score", 0).AsInt32() == 200, "游客纪录不落盘");
            // 排行榜条目：alice 榜上为 100（record_score 只写最高分统计，不入榜）→ Guest 150 应排第 1
            Check(_gs.SubmitHighscore(150) == 1, "游客以 Guest 提交入榜");
            var board = _gs.GetLeaderboard();
            Check(board[0].AsGodotDictionary()["player_name"].AsString() == "Guest" && board[0].AsGodotDictionary()["score"].AsInt32() == 150, "榜首为 Guest（150 高于 alice 100）");
            Check(board[1].AsGodotDictionary()["player_name"].AsString() == "alice", "次席为 alice");

            // 5b. Q06（2026-08-05）：game_over_stats 死亡统计——登录用户累计，游客/未登录跳过
            _gs.LoginUser("alice");
            _gs.Kills = 7;
            _gs.RecordGameOver();
            _gs.Kills = 3;
            _gs.RecordGameOver();
            var stats = _gs.GetUserData("alice");
            Check(stats.GetValueOrDefault("total_kills", 0).AsInt32() == 10, $"Q06：total_kills 两局累计 7+3=10（实测 {stats.GetValueOrDefault("total_kills", 0).AsInt32()}）");
            Check(stats.GetValueOrDefault("games_played", 0).AsInt32() == 2, $"Q06：games_played 累计 2 局（实测 {stats.GetValueOrDefault("games_played", 0).AsInt32()}）");
            _gs.LoginGuest();
            _gs.Kills = 99;
            _gs.RecordGameOver();
            Check(_gs.GetUserData("alice").GetValueOrDefault("total_kills", 0).AsInt32() == 10, "Q06：游客局不写统计");
            _gs.LogoutUser();
            _gs.RecordGameOver();
            Check(_gs.GetUserData("alice").GetValueOrDefault("games_played", 0).AsInt32() == 2, "Q06：未登录局不写统计");
            _gs.Kills = 0;
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"USER SESSION TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"USER SESSION TEST DONE, failures = {_failures}");
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
            RestoreUserFiles();  // Q23：还原用户文件快照
        }
        catch (System.Exception e)
        {
            GD.PushError($"USER SESSION TEST 清理异常: {e}");
        }
    }
}
