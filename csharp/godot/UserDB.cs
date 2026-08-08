using Godot;

namespace InfiAir;

/// <summary>
/// 本地用户数据库（2026-08-04 账户系统；规格 docs/archive/2026-08-04-local-accounts-plan.md + PORTING_PARITY 附录 B）。
/// P0-2（2026-08-07）：数据层迁移 InfiAir.Core.Storage.UserDb（C#，见 csharp/core/Storage/UserDb.cs +
/// csharp/godot/UserDbInterop.cs），本文件为薄壳转发——公开 API/常量/iterations 降档机制不变。
/// 密码派生为逐字节等价迁移（自建 PBKDF2 变体，固定向量对照 tests-csharp/UserDbPasswordTests.cs）。
/// 不移植：fcntl 文件锁（单进程桌面无并发）、远程排行榜（联机已砍）。
/// M7 全量迁移（2026-08-09 自 scripts/user_db.gd）
/// </summary>
public partial class UserDB : RefCounted
{
    public const string UsersPath = "user://users.json";

    public static readonly string[] ReservedNames = { "_leaderboard", "Guest" };

    public const int NameMin = 3;

    public const int NameMax = 16;

    public const int PasswordMin = 3;

    public const int PasswordMax = 16;

    public const int LeaderboardCap = 10;

    public const int PlayerNameMax = 32;

    public const int Pbkdf2Iterations = 50_000;

    /// <summary>PBKDF2 迭代数（测试场景降档加速；生产默认 50_000——实测 100k 迭代 create/verify ≈330ms，
    /// 超过计划书 300ms 判定线，按 docs/2026-08-04-local-accounts-plan.md 约定降档至 ~165ms；
    /// 单机离线文件无在线暴力破解面，数据文件亦无需与 py 端互通，迭代数非兼容约束）</summary>
    public int Iterations { get; set; } = Pbkdf2Iterations;

    private readonly UserDbInterop _interop = new();

    /// <summary>强制重新加载磁盘状态（2026-08-06 审计：GameState._ready 的迁移探测会提前缓存真实用户表
    /// ——测试 wipe user:// 文件后缓存仍非空，「空用户表」起点失效；显式重载供测试/诊断刷新。
    /// 生产无调用点）</summary>
    public void Reload() => _interop.Reload();

    /// <summary>注册：拒绝保留名（B7-7：_leaderboard 与 Guest）与长度不合规；成功写入并落盘。</summary>
    public bool CreateUser(string name, string password) => _interop.CreateUser(name, password, Iterations);

    public bool VerifyUser(string name, string password) => _interop.VerifyUser(name, password, Iterations);

    public bool UserExists(string name) => _interop.UserExists(name);

    /// <summary>用户名列表：last_login_order 降序 + 名字典序（B4 last_login 为自增序号非时间戳）</summary>
    public Godot.Collections.Array<string> ListUsernames()
    {
        var names = new Godot.Collections.Array<string>();
        foreach (var n in _interop.ListUsernames())
        {
            names.Add(n.AsString());
        }

        return names;
    }

    public string GetLastLoginUser() => _interop.GetLastLoginUser();

    public void RecordLogin(string name) => _interop.RecordLogin(name);

    public Godot.Collections.Dictionary GetUserData(string name) => _interop.GetUserData(name);

    /// <summary>通用字段合并更新（统计类）；密码/盐/迭代数不可经此覆盖</summary>
    public void UpdateUserData(string name, Godot.Collections.Dictionary data) => _interop.UpdateUserData(name, data);

    /// <summary>仅更高才写（B4 update_high_score 语义）；分数负钳 0</summary>
    public void UpdateHighScore(string name, int score) => _interop.UpdateHighScore(name, score);

    public Godot.Collections.Dictionary GetUserSettings(string name) => _interop.GetUserSettings(name);

    public void UpdateUserSettings(string name, Godot.Collections.Dictionary settings) => _interop.UpdateUserSettings(name, settings);

    /// <summary>删除用户（先验密）；连带清理该用户存档文件与 .corrupt 备份（B7-12 + 2026-08-06 审计口径）</summary>
    public bool DeleteUser(string name, string password) => _interop.DeleteUser(name, password);

    /// <summary>每用户存档路径：user://savegame_&lt;sanitized&gt;_&lt;sha256[:12]&gt;.json（B5，对齐原作 _save_file_for_user）</summary>
    public string SavefileForUser(string name) => "user://" + _interop.SaveFileName(name);

    /// <summary>提交成绩：score 负钳 0（≤0 不入榜，对齐现 GameState 高分榜语义）；cap 10；
    /// 排序 score 降序 + 提交序（seq 自增，先到先得）；返回 1-indexed 名次，0 = 未上榜。</summary>
    public int SubmitScore(string name, int score) => (int)_interop.SubmitScore(name, score);

    public Godot.Collections.Array GetLeaderboard()
    {
        var board = new Godot.Collections.Array();
        foreach (var entry in _interop.GetLeaderboard())
        {
            board.Add(entry);
        }

        return board;
    }

    // ---------------- GDScript 鸭子调用兼容桥（M7 过渡，删除前） ----------------
    // 调用方：autoload/game_state.gd（M7 并行迁移为 C# 后改 typed PascalCase）、
    // test/{user_db_test,user_db_interop_test}.gd（GDScript 经脚本资源实例化后以
    // snake_case 名访问——桥保持原名精确匹配）、csharp/godot/Welcome.cs（常量改 typed）。

    public int iterations { get => Iterations; set => Iterations = value; }

    public void reload() => Reload();

    public bool create_user(string name, string password) => CreateUser(name, password);

    public bool verify_user(string name, string password) => VerifyUser(name, password);

    public bool user_exists(string name) => UserExists(name);

    public Godot.Collections.Array<string> list_usernames() => ListUsernames();

    public string get_last_login_user() => GetLastLoginUser();

    public void record_login(string name) => RecordLogin(name);

    public Godot.Collections.Dictionary get_user_data(string name) => GetUserData(name);

    public void update_user_data(string name, Godot.Collections.Dictionary data) => UpdateUserData(name, data);

    public void update_high_score(string name, int score) => UpdateHighScore(name, score);

    public Godot.Collections.Dictionary get_user_settings(string name) => GetUserSettings(name);

    public void update_user_settings(string name, Godot.Collections.Dictionary settings) => UpdateUserSettings(name, settings);

    public bool delete_user(string name, string password) => DeleteUser(name, password);

    public string savefile_for_user(string name) => SavefileForUser(name);

    public int submit_score(string name, int score) => SubmitScore(name, score);

    public Godot.Collections.Array get_leaderboard() => GetLeaderboard();

    // ---------------- GDScript 静态常量访问器（M7 过渡，删除前） ----------------
    // GDScript 不能读 C# 常量/静态字段——经脚本资源调静态访问器（UITheme.GetAccent()/Spawner
    // .BuildEnemyTypes() 先例）；C# 调用方（Welcome.cs 等）可直接 typed 读 const/static readonly。

    public static string GetUsersPath() => UsersPath;

    public static Godot.Collections.Array GetReservedNames()
    {
        var arr = new Godot.Collections.Array();
        foreach (var n in ReservedNames)
        {
            arr.Add(n);
        }

        return arr;
    }

    public static int GetNameMin() => NameMin;

    public static int GetNameMax() => NameMax;

    public static int GetPasswordMin() => PasswordMin;

    public static int GetPasswordMax() => PasswordMax;

    public static int GetLeaderboardCap() => LeaderboardCap;

    public static int GetPlayerNameMax() => PlayerNameMax;

    public static int GetPbkdf2Iterations() => Pbkdf2Iterations;
}
