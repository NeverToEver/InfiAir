using Godot;

namespace InfiAir.Tests;

/// <summary>
/// P0-2 断言场景：UserDbInterop（C# 绑定壳）——注册/验密/登录序/排行榜/删号，
/// 并含「存量 GDScript 账号固定向量验密」（引擎环境内证明密码派生逐字节等价）与
/// GDScript UserDB 壳的生产转发路径。只操作 user:// 文件；M6 快照范式备份/还原。
/// </summary>
public partial class UserDbInteropTest : Node
{
    private int _failures;

    /// <summary>M6（2026-08-06 审计）：Q23 快照范式——开头备份、结尾还原 user:// 用户文件</summary>
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
            BackupUserFiles();
            Cleanup();

            // 0. C# 绑定壳可加载
            var cls = GD.Load<Script>("res://csharp/godot/UserDbInterop.cs");
            Check(cls != null, "UserDbInterop 脚本资源可加载");
            var interop = new UserDbInterop();

            // 1. 存量 GDScript 账号固定向量验密（哈希由迁移前 GDScript _derive 生成：
            //    alice/s3cret/盐 deadbeef…/1000 次迭代）
            var fixtureJson = Json.Stringify(new Godot.Collections.Dictionary
            {
                ["_users"] = new Godot.Collections.Dictionary
                {
                    ["alice"] = new Godot.Collections.Dictionary
                    {
                        ["password"] = "76a4bc7172651d55d0139b2622619de3ce63d54dec94d0598021db6ad0b9d03d",
                        ["salt"] = "deadbeefdeadbeefdeadbeefdeadbeef",
                        ["iterations"] = 1000,
                        ["high_score"] = 0,
                        ["last_login_order"] = 0,
                        ["settings"] = new Godot.Collections.Dictionary(),
                    },
                },
                ["_leaderboard"] = new Godot.Collections.Array(),
                ["_seq"] = 0,
            });
            var fixture = Godot.FileAccess.Open("user://users.json", Godot.FileAccess.ModeFlags.Write);
            fixture.StoreString(fixtureJson);
            fixture.Close();
            Check(interop.VerifyUser("alice", "s3cret", 1000) == true, "存量 GDScript 账号验密通过（固定向量）");
            Check(interop.VerifyUser("alice", "wrong", 1000) == false, "存量账号错误密码验密失败");

            // 2. 注册/存在性/降档迭代数
            Check(interop.CreateUser("bob", "pass123", 1000) == true, "注册 bob 成功");
            Check(interop.UserExists("alice") && interop.UserExists("bob"), "user_exists 命中");
            Check(interop.CreateUser("bob", "other", 1000) == false, "重名注册被拒绝");
            Check(interop.VerifyUser("bob", "pass123", 1000) == true, "新注册用户验密通过");

            // 3. 登录序（last_login_order 降序）
            interop.RecordLogin("bob");
            interop.RecordLogin("alice");
            Check(interop.GetLastLoginUser() == "alice", "最近登录用户为 alice");
            interop.RecordLogin("bob");
            Check(interop.GetLastLoginUser() == "bob", "record_login 推进排序");

            // 4. 统计与设置
            Check(interop.GetUserData("alice").GetValueOrDefault("high_score", -1).AsInt32() == 0, "初始最高分 0");
            interop.UpdateHighScore("alice", 150);
            Check(interop.GetUserData("alice").GetValueOrDefault("high_score", -1).AsInt32() == 150, "update_high_score 写入");
            interop.UpdateUserSettings("alice", new Godot.Collections.Dictionary { ["difficulty"] = new StringName("hard") });
            Check(interop.GetUserSettings("alice").GetValueOrDefault("difficulty", "").AsString() == "hard", "设置合并（StringName → string）");

            // 5. 排行榜：排序/名次/cap
            Check(interop.SubmitScore("alice", 100) == 1, "排行榜：首条排第 1");
            Check(interop.SubmitScore("bob", 50) == 2, "排行榜：低分排第 2");
            Check(interop.SubmitScore("alice", 100) == 2, "排行榜：同分新条目排后");
            var board = interop.GetLeaderboard();
            Check(board.Count == 3, "排行榜：条目数正确");
            Check(board[0].AsGodotDictionary().GetValueOrDefault("score", -1).AsInt32() == 100, "排行榜：榜首为最高分");
            Check(board[0].AsGodotDictionary().GetValueOrDefault("player_name", "").AsString() == "alice", "排行榜：榜首玩家名正确");

            // 6. 每用户存档文件名（B5 路径语义）
            var aliceSave = interop.SaveFileName("alice");
            Check(aliceSave.StartsWith("savegame_alice_") && aliceSave.EndsWith(".json"), "存档文件名含清洗后用户名");
            Check(interop.SaveFileName("Alice") != aliceSave, "大小写不同的用户路径不同");

            // 7. 删号：验密 + 连带清理存档文件（B7-12）
            Check(interop.DeleteUser("bob", "wrongpw") == false, "删号错误密码被拒");
            var bobSave = Godot.FileAccess.Open("user://" + interop.SaveFileName("bob"), Godot.FileAccess.ModeFlags.Write);
            bobSave.StoreString("{}");
            bobSave.Close();
            Check(interop.DeleteUser("bob", "pass123") == true, "删号正确密码成功");
            Check(interop.UserExists("bob") == false, "删号后用户消失");
            Check(!Godot.FileAccess.FileExists("user://" + interop.SaveFileName("bob")), "删号连带删除该用户存档文件");

            // 8. 持久化往返（Reload 重读磁盘）
            interop.Reload();
            Check(interop.UserExists("alice") == true, "Reload 后用户保留");
            Check(interop.VerifyUser("alice", "s3cret", 1000) == true, "Reload 后验密一致");

            // 9. GDScript UserDB 壳（生产转发路径：GameState/welcome 同款调用）
            var udb = new UserDB();
            udb.Iterations = 1000;
            Check(udb.UserExists("alice"), "GDScript 壳 user_exists 转发");
            Check(udb.VerifyUser("alice", "s3cret"), "GDScript 壳验密转发（迭代数降档参数透传）");
            Check(udb.GetLeaderboard().Count == 3, "GDScript 壳排行榜转发（榜单与用户表独立，删号不清理条目）");
            Check(udb.SavefileForUser("alice").StartsWith("user://savegame_alice_"), "GDScript 壳存档路径转发");

            Cleanup();
            RestoreUserFiles();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"USER DB INTEROP TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"USER DB INTEROP TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
