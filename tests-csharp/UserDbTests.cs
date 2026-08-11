using InfiAir.Core.Storage;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// UserDb（P0-2）行为单测：CRUD / 校验约束 / 排序 / 统计 / 删号连档清理 /
/// 排行榜 cap 与名次 / Q17/Q18/Q20 结构守卫 / 损坏隔离。语义对齐 test/user_db_test.gd
/// （GDScript 断言场景）与 docs/archive/2026-08-04-local-accounts-plan.md。
/// </summary>
public sealed class UserDbTests
{
    private static UserDb NewDb(out string dir, out string usersPath)
    {
        dir = Path.Combine(Path.GetTempPath(), "infiair-userdb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        usersPath = Path.Combine(dir, "users.json");
        return new UserDb(usersPath);
    }

    [Fact]
    public void CreateVerifyExists_RoundTrip()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            Assert.True(db.CreateUser("alice", "s3cret", 1000));
            Assert.True(db.CreateUser("bob", "pass123", 1000));
            Assert.False(db.CreateUser("alice", "other", 1000), "重名注册被拒绝");
            Assert.True(db.UserExists("alice") && db.UserExists("bob"));
            Assert.True(db.VerifyUser("alice", "s3cret", 1000));
            Assert.False(db.VerifyUser("alice", "wrong", 1000));
            Assert.False(db.VerifyUser("nobody", "s3cret", 1000));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Create_ValidationBounds_AndReservedNames()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            Assert.False(db.CreateUser("ab", "pass123", 1000), "用户名 <3 拒绝");
            Assert.False(db.CreateUser(new string('a', 17), "pass123", 1000), "用户名 >16 拒绝");
            Assert.False(db.CreateUser("carol", "pw", 1000), "密码 <3 拒绝");
            Assert.False(db.CreateUser("carol", new string('p', 17), 1000), "密码 >16 拒绝");
            Assert.False(db.CreateUser("_leaderboard", "pass123", 1000), "保留名 _leaderboard 拒绝");
            Assert.False(db.CreateUser("Guest", "pass123", 1000), "保留名 Guest 拒绝");
            Assert.False(db.UserExists("_leaderboard"));
            // 中文名按 code point 计数（GDScript String.length() 语义）
            Assert.True(db.CreateUser("玩家一", "pass123", 1000), "3 个 code point 的中文名合法");
            Assert.False(db.CreateUser("玩家", "pass123", 1000), "2 个 code point 的中文名 <3 拒绝");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LastLoginSorting_OrderBySequenceThenName()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            db.CreateUser("alice", "pass1", 1000);
            db.CreateUser("bob", "pass1", 1000);
            db.RecordLogin("bob");
            db.RecordLogin("alice");
            Assert.Equal(["alice", "bob"], db.ListUsernames());
            Assert.Equal("alice", db.GetLastLoginUser());
            db.RecordLogin("bob");
            Assert.Equal("bob", db.ListUsernames()[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StatsUpdate_MergeAndHighScoreOnlyIfHigher()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            db.CreateUser("alice", "s3cret", 1000);
            Assert.Equal(0L, (long)db.GetUserData("alice")["high_score"]!);
            db.UpdateHighScore("alice", 100);
            db.UpdateHighScore("alice", 50);
            Assert.Equal(100L, (long)db.GetUserData("alice")["high_score"]!);
            db.UpdateUserData("alice", new Dictionary<string, object?> { ["total_kills"] = 5L });
            db.UpdateUserData("alice", new Dictionary<string, object?> { ["total_kills"] = 9L });
            Assert.Equal(9L, (long)db.GetUserData("alice")["total_kills"]!);
            db.UpdateUserData("alice", new Dictionary<string, object?> { ["password"] = "hacked" });
            Assert.True(db.VerifyUser("alice", "s3cret", 1000), "update_user_data 不可覆盖密码");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_MergeAndIsolation()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            db.CreateUser("alice", "s3cret", 1000);
            db.CreateUser("bob", "pass123", 1000);
            db.UpdateUserSettings("alice", new Dictionary<string, object?> { ["difficulty"] = "hard" });
            db.UpdateUserSettings("alice", new Dictionary<string, object?> { ["locale"] = "en" });
            var settings = db.GetUserSettings("alice");
            Assert.Equal("hard", settings["difficulty"]);
            Assert.Equal("en", settings["locale"]);
            Assert.Empty(db.GetUserSettings("bob"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveFileName_SanitizeAndHashPrefix()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            var alice = db.SaveFileName("alice");
            Assert.StartsWith("savegame_alice_", alice);
            Assert.EndsWith(".json", alice);
            Assert.Equal("savegame_alice_".Length + 12 + ".json".Length, alice.Length);
            Assert.NotEqual(alice, db.SaveFileName("Alice"));
            Assert.StartsWith("savegame_user_", db.SaveFileName("@@@"));
            // 中文名全被清洗 → 回退 user 前缀
            Assert.StartsWith("savegame_user_", db.SaveFileName("玩家一"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DeleteUser_RequiresPassword_AndCleansSaveFiles()
    {
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            db.CreateUser("bob", "pass123", 1000);
            var saveFile = Path.Combine(dir, db.SaveFileName("bob"));
            File.WriteAllText(saveFile, "{}");
            File.WriteAllText(saveFile + ".corrupt", "stale backup");

            Assert.False(db.DeleteUser("bob", "wrongpw", saveFile), "删号错误密码被拒");
            Assert.True(db.DeleteUser("bob", "pass123", saveFile));
            Assert.False(db.UserExists("bob"));
            Assert.False(File.Exists(saveFile), "删号连带删除该用户存档文件");
            Assert.False(File.Exists(saveFile + ".corrupt"), "删号连带清理损坏备份");
            Assert.False(db.VerifyUser("bob", "pass123", 1000));
            _ = usersPath;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DeleteUser_SaveFails_RollsBackInMemoryEntry()
    {
        // 2026-08-10 健壮性审查：落盘失败须回滚内存条目（对称 CreateUser 回滚）——
        // 注入失败方式：users.json 正本替换为同名目录，TrySave rename 覆盖必失败
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            db.CreateUser("bob", "pass123", 1000);
            var saveFile = Path.Combine(dir, db.SaveFileName("bob"));
            File.WriteAllText(saveFile, "{}");
            File.Delete(usersPath);
            Directory.CreateDirectory(usersPath);

            Assert.False(db.DeleteUser("bob", "pass123", saveFile), "落盘失败删号返回 false");
            Assert.True(db.UserExists("bob"), "Save 失败后内存条目回滚");
            Assert.True(db.VerifyUser("bob", "pass123", 1000), "回滚后验密仍通过");
            // AB11：事务轴——users.json 未提交成功则存档文件必须幸存（旧实现先删文件，档毁人留）
            Assert.True(File.Exists(saveFile), "Save 失败时存档文件幸存（不产生删除副作用）");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Leaderboard_OrderingCapAndRanks()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            Assert.Equal(0L, db.SubmitScore("alice", 0));
            Assert.Equal(1L, db.SubmitScore("alice", 100));
            Assert.Equal(2L, db.SubmitScore("bob", 50));
            Assert.Equal(2L, db.SubmitScore("alice", 80));
            Assert.Equal(2L, db.SubmitScore("alice", 100));
            Assert.Equal(0L, db.SubmitScore("carol", -5));
            var board = db.GetLeaderboard();
            Assert.Equal(4, board.Count);
            Assert.Equal(100L, (long)((Dictionary<string, object?>)board[0]!)["score"]!);
            Assert.Equal(100L, (long)((Dictionary<string, object?>)board[1]!)["score"]!);
            Assert.Equal("alice", (string)((Dictionary<string, object?>)board[0]!)["player_name"]!);

            for (var i = 0; i < 100; i++)
            {
                db.SubmitScore("alice", 200 - i);
            }

            board = db.GetLeaderboard();
            Assert.Equal(UserDb.LeaderboardCap, board.Count);
            Assert.Equal(200L, (long)((Dictionary<string, object?>)board[0]!)["score"]!);
            Assert.Equal(0L, db.SubmitScore("alice", 1));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Persistence_ReloadKeepsData()
    {
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            db.CreateUser("alice", "s3cret", 1000);
            db.SubmitScore("alice", 200);
            db.UpdateUserSettings("alice", new Dictionary<string, object?> { ["locale"] = "en" });

            var db2 = new UserDb(usersPath);
            Assert.True(db2.UserExists("alice"));
            Assert.True(db2.VerifyUser("alice", "s3cret", 1000));
            Assert.Single(db2.GetLeaderboard());
            Assert.Equal("en", db2.GetUserSettings("alice")["locale"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CorruptUsersFile_IsQuarantined_AndRebuilt()
    {
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            File.WriteAllText(usersPath, "{not valid json");
            Assert.True(db.CreateUser("dave", "pass123", 1000), "损坏后按空库重建可注册");
            Assert.True(File.Exists(usersPath + ".corrupt"), "损坏文件隔离为 .corrupt");
            Assert.False(db.UserExists("alice"));
            Assert.Empty(db.GetLeaderboard());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Q17_UsersNotDictionary_RebuildsEmpty()
    {
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            File.WriteAllText(usersPath, """{"_users": "not-a-dict", "_leaderboard": [1, 2]}""");
            Assert.False(db.UserExists("alice"), "用户表非 Dictionary → 空库重建（不崩溃）");
            Assert.Empty(db.GetLeaderboard());
            Assert.True(db.CreateUser("bob", "pass123", 1000));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Q20_LeaderboardEntryTyping_FiltersJunk()
    {
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            File.WriteAllText(
                usersPath,
                """{"_users": {"bob": {"last_login_order": 0}}, "_leaderboard": [{"player_name": "bob", "score": 50, "seq": 1}, "junk", {"player_name": "bad", "score": "100", "seq": 2}]}""");
            var board = db.GetLeaderboard();
            Assert.Single(board);
            Assert.Equal(50L, (long)((Dictionary<string, object?>)board[0]!)["score"]!);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AB17_HugeLeaderboardScore_NormalizedOnRead()
    {
        // AB17：手改 users.json 超大 score（>2^31 整数 / 1e300 double）经 GetLeaderboard
        // 归一化钳 [0, long.MaxValue]——旧实现裸传，显示路径 (int) 截断回绕负数名次分
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            File.WriteAllText(
                usersPath,
                """{"_users": {"bob": {"last_login_order": 0}}, "_leaderboard": [{"player_name": "bob", "score": 5000000000, "seq": 1}, {"player_name": "carol", "score": 1e300, "seq": 2}]}""");
            var board = db.GetLeaderboard();
            Assert.Equal(2, board.Count);
            var scores = board
                .Select(b => (long)((Dictionary<string, object?>)b!)["score"]!)
                .OrderDescending()
                .ToArray();
            Assert.Equal(long.MaxValue, scores[0]);  // 1e300 → ToInt64 钳至 long.MaxValue
            Assert.Equal(5_000_000_000L, scores[1]); // >2^31 整数原样保留（>0 不回绕负）
            Assert.All(scores, s => Assert.True(s >= 0, "归一化后不为负"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Q17_JunkUserRecords_DoNotCrashOperations()
    {
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            File.WriteAllText(
                usersPath,
                """{"_users": {"junk": "not-a-dict", "bad": {"last_login_order": "7"}}, "_leaderboard": []}""");
            Assert.False(db.VerifyUser("junk", "x", 1000), "非 Dictionary 条目按不存在处理");
            Assert.Equal(["bad", "junk"], db.ListUsernames()); // 排序：bad(last_login "7") 在 junk(0) 前
            Assert.Equal("7", db.GetUserData("bad")["last_login_order"]);
            db.RecordLogin("bad"); // 不崩溃
            db.UpdateUserData("bad", new Dictionary<string, object?> { ["total_kills"] = 3L });
            Assert.Equal(3L, (long)db.GetUserData("bad")["total_kills"]!);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AC21_LastLoginOrderMaxValue_RecordLoginNoOverflow()
    {
        // AC21（2026-08-11 第九轮审计）：手改 last_login_order = long.MaxValue → RecordLogin
        // 递增 +1 曾回绕 long.MinValue（负值落盘 + 排序垫底）；读值钳 ≥0 + 递增防溢出后
        // 保持原值、落盘值不变量（非负、无回绕）
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            File.WriteAllText(
                usersPath,
                """{"_users": {"bob": {"last_login_order": 9223372036854775807}, "alice": {"last_login_order": 5}}, "_leaderboard": []}""");
            db.RecordLogin("bob");
            Assert.Equal(long.MaxValue, (long)db.GetUserData("bob")["last_login_order"]!); // maxOrder 已达 long.MaxValue → 保持原值不回绕
            db.RecordLogin("alice");
            Assert.Equal(long.MaxValue, (long)db.GetUserData("alice")["last_login_order"]!); // maxOrder 钳至 long.MaxValue → 其余用户递增同样不回绕
            Assert.Equal(["alice", "bob"], db.ListUsernames()); // 排序正常（并列 MaxValue 按名字典序）
            var db2 = new UserDb(usersPath);
            Assert.Equal(long.MaxValue, (long)db2.GetUserData("bob")["last_login_order"]!); // 落盘值不变量：重载后仍为 long.MaxValue
            Assert.Equal(long.MaxValue, (long)db2.GetUserData("alice")["last_login_order"]!); // 落盘值不变量：无回绕负值
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AC24_HugeSeq_SubmitScoreNoOverflow()
    {
        // AC24（2026-08-11 第九轮审计）：手改 _seq 巨值 → SubmitScore 曾 +1 回绕/负数化
        // （新条目负 seq → 排序破坏）；读值钳 ≥0 + 递增防溢出后不溢出、排序不破坏
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            // 9.2e18（double 域，2^63 以下可精确表示）→ 读值 9200000000000000000，+1 后仍为正且递增
            File.WriteAllText(
                usersPath,
                """{"_users": {"bob": {"last_login_order": 0}}, "_leaderboard": [], "_seq": 9.2e18}""");
            Assert.Equal(1L, db.SubmitScore("alice", 100));
            var board = db.GetLeaderboard();
            Assert.Equal(9_200_000_000_000_000_001L, (long)((Dictionary<string, object?>)board[0]!)["seq"]!); // seq 为正且 +1 未溢出
            Assert.Equal(2L, db.SubmitScore("bob", 100));
            board = db.GetLeaderboard();
            Assert.Equal("alice", (string)((Dictionary<string, object?>)board[0]!)["player_name"]!); // 排序不破坏：同分先到先得（seq 升序）
            Assert.Equal("bob", (string)((Dictionary<string, object?>)board[1]!)["player_name"]!);

            // 边界：_seq = long.MaxValue（整数域）→ 递增必须防溢出（曾 +1 回绕 long.MinValue → 新条目排最前）
            File.WriteAllText(
                usersPath,
                """{"_users": {"bob": {"last_login_order": 0}}, "_leaderboard": [{"player_name": "bob", "score": 100, "seq": 5}], "_seq": 9223372036854775807}""");
            db.Reload();
            Assert.Equal(2L, db.SubmitScore("alice", 100)); // seq 钳至 long.MaxValue：新条目排在同分旧条目后（先到先得）
            board = db.GetLeaderboard();
            Assert.Equal(long.MaxValue, (long)((Dictionary<string, object?>)board[1]!)["seq"]!); // seq 不溢出（保持 long.MaxValue）
            Assert.DoesNotContain("-9223372036854775808", File.ReadAllText(usersPath)); // 落盘 _seq 不出现回绕负值
            Assert.Equal(3L, db.SubmitScore("carol", 50)); // 连续提交仍正常（seq 保持钳制值）
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
