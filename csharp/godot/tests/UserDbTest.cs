using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 本地账户数据层测试（2026-08-04 账户系统 T1）：注册/验密/保留名/排序/统计/
/// 删号连档清理/排行榜 cap 与名次/损坏隔离重置。只操作 user:// 文件，不加载 main 场景。
/// </summary>
public partial class UserDbTest : Node
{
    private int _failures;
    private UserDB _db = null!;

    /// <summary>M6（2026-08-06 审计）：Q23 快照范式补漏——原 _cleanup 直接删除 user://users.json
    /// 且无还原，本地跑一次即永久销毁开发者全部账户与用户排行榜；开头备份、结尾还原</summary>
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

    private void BackupUserFiles()
    {
        _fileBackups = new Godot.Collections.Dictionary();
        var files = new Godot.Collections.Array<string> { "user://users.json", "user://users.json.corrupt" };
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

    private void Cleanup()
    {
        var paths = new[] { "user://users.json", "user://users.json.corrupt" };
        foreach (var p in paths)
        {
            if (Godot.FileAccess.FileExists(p))
            {
                DirAccess.RemoveAbsolute(p);
            }
        }
    }

    public override void _Ready()
    {
        Run();
    }

    private void Run()
    {
        try
        {
            BackupUserFiles(); // M6：快照须在任何删除/覆写之前（_cleanup 会删 users.json）
            Cleanup();
            _db = new UserDB();
            _db.Iterations = 1000; // 测试降档加速（生产 100_000）

            // 1. 注册 / 验密 / 存在性
            Check(_db.CreateUser("alice", "s3cret"), "注册 alice 成功");
            Check(_db.CreateUser("bob", "pass123"), "注册 bob 成功");
            Check(!_db.CreateUser("alice", "other"), "重名注册被拒绝");
            Check(_db.UserExists("alice") && _db.UserExists("bob"), "user_exists 命中");
            Check(_db.VerifyUser("alice", "s3cret"), "正确密码验密通过");
            Check(!_db.VerifyUser("alice", "wrong"), "错误密码验密失败");
            Check(!_db.VerifyUser("nobody", "s3cret"), "不存在用户验密失败");

            // 2. 长度与保留名约束（B3 16 上限 / B7-7 保留名）
            Check(!_db.CreateUser("ab", "pass123"), "用户名 <3 拒绝");
            Check(!_db.CreateUser(new string('a', 17), "pass123"), "用户名 >16 拒绝");
            Check(!_db.CreateUser("carol", "pw"), "密码 <3 拒绝");
            Check(!_db.CreateUser("carol", new string('p', 17)), "密码 >16 拒绝");
            Check(!_db.CreateUser("_leaderboard", "pass123"), "保留名 _leaderboard 拒绝");
            Check(!_db.CreateUser("Guest", "pass123"), "保留名 Guest 拒绝");
            Check(!_db.UserExists("_leaderboard"), "保留名未入库");

            // 3. last_login 排序：序号降序 + 名字典序
            _db.RecordLogin("bob");
            _db.RecordLogin("alice");
            var names = _db.ListUsernames();
            Check(names.Count == 2 && names[0] == "alice" && names[1] == "bob", "list_usernames 按最近登录降序");
            Check(_db.GetLastLoginUser() == "alice", "get_last_login_user 取最近登录");
            _db.RecordLogin("bob");
            Check(_db.ListUsernames()[0] == "bob", "record_login 推进排序");

            // 4. 统计更新：合并写 / 最高分仅更高才写
            Check(_db.GetUserData("alice")["high_score"].AsInt32() == 0, "初始最高分 0");
            _db.UpdateHighScore("alice", 100);
            _db.UpdateHighScore("alice", 50);
            Check(_db.GetUserData("alice")["high_score"].AsInt32() == 100, "update_high_score 仅更高才写");
            _db.UpdateUserData("alice", new Godot.Collections.Dictionary { ["total_kills"] = 5 });
            _db.UpdateUserData("alice", new Godot.Collections.Dictionary { ["total_kills"] = 9 });
            Check(_db.GetUserData("alice")["total_kills"].AsInt32() == 9, "update_user_data 合并累加");
            _db.UpdateUserData("alice", new Godot.Collections.Dictionary { ["password"] = "hacked" });
            Check(_db.VerifyUser("alice", "s3cret"), "update_user_data 不可覆盖密码");

            // 5. 设置隔离
            _db.UpdateUserSettings("alice", new Godot.Collections.Dictionary { ["difficulty"] = new StringName("hard") });
            _db.UpdateUserSettings("alice", new Godot.Collections.Dictionary { ["locale"] = "en" });
            var settings = _db.GetUserSettings("alice");
            Check(
                settings.GetValueOrDefault("difficulty", new Variant()).AsStringName() == new StringName("hard")
                    && settings.GetValueOrDefault("locale", "").AsString() == "en",
                "update_user_settings 合并"
            );
            Check(_db.GetUserSettings("bob").Count == 0, "用户设置互不泄漏");

            // 6. 每用户存档路径
            var aliceSave = _db.SavefileForUser("alice");
            Check(aliceSave.StartsWith("user://savegame_alice_"), "存档路径含清洗后用户名");
            Check(aliceSave.EndsWith(".json") && aliceSave.Length == "user://savegame_alice_.json".Length + 12, "存档路径含 sha256[:12]");
            Check(_db.SavefileForUser("Alice") != aliceSave, "大小写不同的用户路径不同");
            Check(_db.SavefileForUser("@@@").StartsWith("user://savegame_user_"), "纯符号用户名回退 user 前缀");

            // 7. 删号：验密 + 连带清理存档（B7-12）
            Check(!_db.DeleteUser("bob", "wrongpw"), "删号错误密码被拒");
            var bobSave = _db.SavefileForUser("bob");
            var f = Godot.FileAccess.Open(bobSave, Godot.FileAccess.ModeFlags.Write);
            f.StoreString("{}");
            f.Close();
            Check(_db.DeleteUser("bob", "pass123"), "删号正确密码成功");
            Check(!_db.UserExists("bob"), "删号后用户消失");
            Check(!Godot.FileAccess.FileExists(bobSave), "删号连带删除该用户存档文件");
            Check(!_db.VerifyUser("bob", "pass123"), "删号后验密失败");

            // 8. 排行榜：0 分不入榜 / 排序 / 同分后 / cap / 名次
            Check(_db.SubmitScore("alice", 0) == 0, "排行榜：0 分不入榜");
            Check(_db.SubmitScore("alice", 100) == 1, "排行榜：首条排第 1");
            Check(_db.SubmitScore("bob", 50) == 2, "排行榜：低分排第 2");
            Check(_db.SubmitScore("alice", 80) == 2, "排行榜：中间分插入第 2");
            Check(_db.SubmitScore("alice", 100) == 2, "排行榜：同分新条目排后");
            Check(_db.SubmitScore("carol", -5) == 0, "排行榜：负分不入榜");
            var board = _db.GetLeaderboard();
            Check(board.Count == 4, "排行榜：条目数正确");
            Check(
                board[0].AsGodotDictionary()["score"].AsInt32() == 100 && board[1].AsGodotDictionary()["score"].AsInt32() == 100,
                "排行榜：榜首与同分先到先得"
            );
            Check(board[0].AsGodotDictionary()["player_name"].AsString() == "alice", "排行榜：榜首玩家名正确");
            for (int i = 0; i < 100; i++)
            {
                _db.SubmitScore("alice", 200 - i);
            }
            board = _db.GetLeaderboard();
            Check(board.Count == UserDB.LeaderboardCap, "排行榜：上限截断");
            Check(board[0].AsGodotDictionary()["score"].AsInt32() == 200, "排行榜：截断后榜首不变");
            Check(_db.SubmitScore("alice", 1) == 0, "排行榜：超出上限的分数不入榜");

            // 9. 持久化往返：重载后数据一致
            _db = new UserDB();
            _db.Iterations = 1000;
            Check(_db.UserExists("alice"), "持久化往返：用户保留");
            Check(_db.VerifyUser("alice", "s3cret"), "持久化往返：验密一致");
            board = _db.GetLeaderboard();
            Check(board.Count == UserDB.LeaderboardCap && board[0].AsGodotDictionary()["score"].AsInt32() == 200, "持久化往返：榜单一致");

            // 10. 损坏隔离重置（B4：备份 .corrupt + 按空库处理）
            Cleanup();
            var corrupted = Godot.FileAccess.Open("user://users.json", Godot.FileAccess.ModeFlags.Write);
            corrupted.StoreString("{not valid json");
            corrupted.Close();
            _db = new UserDB();
            _db.Iterations = 1000;
            Check(_db.CreateUser("dave", "pass123"), "损坏后按空库重建可注册");
            Check(Godot.FileAccess.FileExists("user://users.json.corrupt"), "损坏文件隔离为 .corrupt 备份");
            Check(!_db.UserExists("alice"), "损坏库不残留旧用户");
            Check(_db.GetLeaderboard().Count == 0, "损坏库不残留旧榜单");

            // 11. Q17/Q18/Q20（2026-08-05）：结构守卫 / hex 校验 / 榜单判型
            Cleanup();
            _db = new UserDB();
            _db.Iterations = 1000;
            Check(_db.CreateUser("alice", "s3cret"), "Q17：重建后注册 alice");
            Check(_db.SubmitScore("alice", 100) == 1, "Q17：提交榜单条目");
            // 用户表非 Dictionary → 空库重建不崩溃（原 users.has() 运行时错误）
            var q17Fh = Godot.FileAccess.Open("user://users.json", Godot.FileAccess.ModeFlags.Write);
            q17Fh.StoreString(Json.Stringify(new Godot.Collections.Dictionary { ["_users"] = "not-a-dict", ["_leaderboard"] = new Godot.Collections.Array { 1, 2 } }));
            q17Fh.Close();
            _db = new UserDB();
            _db.Iterations = 1000;
            Check(!_db.UserExists("alice"), "Q17：用户表非 Dictionary → 空库重建（不崩溃）");
            Check(_db.GetLeaderboard().Count == 0, "Q17：非法榜单结构重建为空");
            Check(_db.CreateUser("bob", "pass123"), "Q17：重建后可注册");
            // 榜单条目判型：非 Dictionary 条目/字符串 score 跳过（原渲染与排序崩溃/静默转 0）
            var q20Fh = Godot.FileAccess.Open("user://users.json", Godot.FileAccess.ModeFlags.Write);
            q20Fh.StoreString(Json.Stringify(new Godot.Collections.Dictionary
            {
                ["_users"] = new Godot.Collections.Dictionary { ["bob"] = new Godot.Collections.Dictionary { ["last_login_order"] = 0 } },
                ["_leaderboard"] = new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary { ["player_name"] = "bob", ["score"] = 50, ["seq"] = 1 },
                    "junk",
                    new Godot.Collections.Dictionary { ["player_name"] = "bad", ["score"] = "100", ["seq"] = 2 },
                },
            }));
            q20Fh.Close();
            _db = new UserDB();
            _db.Iterations = 1000;
            var q20Board = _db.GetLeaderboard();
            Check(q20Board.Count == 1 && q20Board[0].AsGodotDictionary()["score"].AsInt32() == 50, "Q20：非 Dictionary/字符串 score 条目被过滤（保留 1 条）");
            // Q18：手改奇数长度/非法 hex salt → 验密安全失败（原 hex[i+1] 越界 / -1 append 崩溃）
            var q18Fh = Godot.FileAccess.Open("user://users.json", Godot.FileAccess.ModeFlags.Write);
            q18Fh.StoreString(Json.Stringify(new Godot.Collections.Dictionary
            {
                ["_users"] = new Godot.Collections.Dictionary
                {
                    ["bob"] = new Godot.Collections.Dictionary { ["password"] = "00", ["salt"] = "abc", ["iterations"] = 1000, ["last_login_order"] = 0 },
                },
            }));
            q18Fh.Close();
            _db = new UserDB();
            _db.Iterations = 1000;
            Check(!_db.VerifyUser("bob", "pass123"), "Q18：奇数长度 salt 验密安全失败（无越界崩溃）");
            q18Fh = Godot.FileAccess.Open("user://users.json", Godot.FileAccess.ModeFlags.Write);
            q18Fh.StoreString(Json.Stringify(new Godot.Collections.Dictionary
            {
                ["_users"] = new Godot.Collections.Dictionary
                {
                    ["bob"] = new Godot.Collections.Dictionary { ["password"] = "zz", ["salt"] = "zz", ["iterations"] = 1000, ["last_login_order"] = 0 },
                },
            }));
            q18Fh.Close();
            _db = new UserDB();
            _db.Iterations = 1000;
            Check(!_db.VerifyUser("bob", "pass123"), "Q18：非法 hex 盐/密文验密安全失败（不 append -1）");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"USER DB TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"USER DB TEST DONE, failures = {_failures}");
            Cleanup();
            RestoreUserFiles(); // M6：还原开发者原始用户表/排行榜/存档
            TestExit.Quit(_failures);
        }
    }
}
