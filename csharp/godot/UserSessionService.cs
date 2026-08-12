using Godot;

namespace InfiAir;

/// <summary>
/// 用户会话域服务（第七轮拆域收官，2026-08-12）：原 GameState.Users.cs 账户系统/会话与
/// GameState.State.cs 的 CurrentUser 状态迁入本服务——LoginUser/LoginGuest/LogoutUser/IsGuest/
/// SavePathForCurrent/LoadSessionSettings/MaybeMigrateLegacyProfile/LegacyMigrationPending/
/// ScanLegacyMigration/ClearLegacyMigration/CreateUser 与 UserDB 转发 14 个（VerifyUser/
/// UserDbCorrupt/UserExists/ListUsernames/ReloadUserDb/GetLastLoginUser/DeleteUser/
/// GetLeaderboard/GetUserSettings/UpdateUserSettings/GetUserData/UpdateUserData/
/// UserDbSavefileFor），B5/Q25/2026-08-10 健壮性审查/2026-08-06 审计等注释随迁。
/// Godot 绑定层：UserDB 与 SaveManager 经构造注入（与 MetaService 注入 UserDB 同构——
/// GameState 无 SaveManager 公开门面，迁移探测/迁移清理需文件 IO）；跨域访问统一经
/// GameState.Instance——LoadMeta（Meta 门面）、ApplySettingsDict/ApplyWindowSize/
/// InvalidateViewRectCache（Settings 门面）、ApplyKeyBindings（Input 门面）、SaveProfile/
/// SaveNum/Locale、SAVE_PATH/PROFILE_PATH。
/// 门面转发先例：与 MetaService/SettingsService 同构——GameState 组合持有本服务，
/// GameState.Users.cs 为门面对齐转发（签名/语义不变）+ SavePathForCurrent 私有一行包装
/// （GameState.Save.cs 内部调用零改动），State.cs CurrentUser 转发；保持唯一 autoload：
/// GameState 约定。
/// 信号：Users 域不广播（无 EmitSignal），无需信号重发。
/// </summary>
public sealed partial class UserSessionService : RefCounted
{

    /// <summary>组合注入：GameState 持有的本地用户数据库（GameState.cs 构造器传入，
    /// 与 MetaService 构造注入 UserDB 同构）。</summary>
    private readonly UserDB _userDb;

    /// <summary>组合注入：GameState 持有的存档文件管理器（迁移探测/迁移清理的 profile.json
    /// Exists/Load/Delete 需要；GameState 无 SaveManager 公开门面，故构造注入）。</summary>
    private readonly SaveManager _saveManager;

    public UserSessionService(UserDB userDb, SaveManager saveManager)
    {
        _userDb = userDb;
        _saveManager = saveManager;
    }

    // ---------------- 用户会话（2026-08-04 账户系统；2026-08-12 自 GameState.Users.cs/State.cs 迁入） ----------------

    /// <summary>2026-08-04 账户系统：当前用户会话——"" = 未登录（welcome 前/测试兼容，档案走旧 profile.json 路径）、
    /// "Guest" = 游客（设置仅内存、不存档、不写统计，B7-8）、否则为已登录用户名（档案/存档走 user_db）。</summary>
    public string CurrentUser { get; set; } = "";

    /// <summary>profile.json 退役迁移缓存：启动时存在旧 profile 且用户表为空 → 首个注册用户合并后删除（B5）</summary>
    private Godot.Collections.Dictionary _pendingLegacyProfile = new();

    /// <summary>登录已有用户：载入其设置/最高分并即时生效（locale 即时 set_locale——B7-11）</summary>
    public void LoginUser(string name)
    {
        if (!_userDb.UserExists(name))
        {
            return;
        }

        CurrentUser = name;
        _userDb.RecordLogin(name);
        LoadSessionSettings();
        TranslationServer.SetLocale(GameState.Instance.Locale);
        GameState.Instance.ApplyKeyBindings();
        GameState.Instance.ApplyWindowSize();
        GameState.Instance.InvalidateViewRectCache();
    }

    /// <summary>游客进入：设置仅内存、不存档、不写统计（B7-8）；保留当前内存值（启动 profile 值视作游客会话）</summary>
    public void LoginGuest()
    {
        CurrentUser = "Guest";
        GameState.Instance.LoadMeta(); // 局外成长：清空游客会话 meta 内存态（不持久化，B7-8）
    }

    /// <summary>退出：登录用户落盘设置；游客丢弃（内存）；复位未登录</summary>
    public void LogoutUser()
    {
        if (CurrentUser != "" && CurrentUser != "Guest")
        {
            GameState.Instance.SaveProfile();
        }

        CurrentUser = "";
        GameState.Instance.LoadMeta(); // 局外成长：清空未登录会话 meta 内存态
    }

    public bool IsGuest() => CurrentUser == "Guest";

    /// <summary>当前会话存档路径：登录用户 = 每用户文件；未登录 = 旧单文件；游客无路径（不存档）</summary>
    public string SavePathForCurrent()
    {
        if (CurrentUser == "")
        {
            return GameState.Instance.SAVE_PATH;
        }

        if (IsGuest())
        {
            return "";
        }

        return _userDb.SavefileForUser(CurrentUser);
    }

    /// <summary>载入当前会话档案：登录用户 → user_db settings + 统计；游客/未登录 → 保留内存（游客不落盘）</summary>
    private void LoadSessionSettings()
    {
        if (CurrentUser == "" || IsGuest())
        {
            return;
        }

        // 第七轮拆域：设置域持久化桥经 GameState 门面跨域（ApplySettingsDict 本体在 SettingsService）
        GameState.Instance.ApplySettingsDict(_userDb.GetUserSettings(CurrentUser));
        // 2026-08-10 健壮性审查：判型守卫 + 截断钳制——手改 users.json 的 high_score 为字符串时
        // AsInt64 抛 InvalidCastException（登录即崩）；超大值裸 (int) 截断回绕为负
        var hs = _userDb.GetUserData(CurrentUser).GetValueOrDefault("high_score", 0);
        GameState.Instance.HighScore = hs.VariantType is Variant.Type.Int or Variant.Type.Float
            ? (int)Math.Clamp(hs.AsInt64(), 0L, (long)int.MaxValue)
            : 0;
        GameState.Instance.LoadMeta(); // 局外成长：会话 meta 档案加载（2026-08-09）
    }

    /// <summary>profile.json 退役迁移（B5）：启动时存在旧 profile 且用户表为空 → 缓存待首个注册用户合并</summary>
    public void MaybeMigrateLegacyProfile()
    {
        if (_pendingLegacyProfile.Count > 0)
        {
            return;
        }

        if (_saveManager.Exists(GameState.Instance.PROFILE_PATH) && _userDb.ListUsernames().Count == 0)
        {
            var parsed = _saveManager.Load(GameState.Instance.PROFILE_PATH);
            if (parsed.Count > 0)
            {
                _pendingLegacyProfile = parsed;
            }
        }
    }

    /// <summary>Q25（2026-08-05）：旧 profile 迁移缓存查询/触发/清空公开化（A7 私有访问残留收敛，
    /// 测试经公开接口；生产路径不变——create_user 消费后自清）</summary>
    public bool LegacyMigrationPending() => _pendingLegacyProfile.Count > 0;

    public void ScanLegacyMigration() => MaybeMigrateLegacyProfile();

    public void ClearLegacyMigration() => _pendingLegacyProfile.Clear();

    /// <summary>注册用户（转发 user_db.create_user）；成功后合并旧 profile 迁移数据并删除 profile.json（B5）</summary>
    public bool CreateUser(string name, string password)
    {
        if (!_userDb.CreateUser(name, password))
        {
            return false;
        }

        if (_pendingLegacyProfile.Count > 0)
        {
            var legacy = (Godot.Collections.Dictionary)_pendingLegacyProfile.Duplicate();
            _pendingLegacyProfile.Clear();
            _userDb.UpdateHighScore(name, (int)Math.Clamp(GameState.Instance.SaveNum(legacy.GetValueOrDefault("high_score", 0), 0.0), 0.0, (double)int.MaxValue));
            legacy.Remove("high_score");
            legacy.Remove("version");
            legacy.Remove("highscores");
            _userDb.UpdateUserSettings(name, legacy);
            _saveManager.Delete(GameState.Instance.PROFILE_PATH);
        }

        return true;
    }

    // ---------------- 用户数据库转发（A2 组合服务；供 welcome 登录面板使用） ----------------

    public bool VerifyUser(string name, string password) => _userDb.VerifyUser(name, password);

    /// <summary>users.json 损坏标志（2026-08-10 健壮性审查：与 SaveCorrupt/ProfileCorrupt 对齐，
    /// 欢迎页提示玩家账号已隔离备份而非丢失）</summary>
    public bool UserDbCorrupt => _userDb.LastWasCorrupt;

    public bool UserExists(string name) => _userDb.UserExists(name);

    public Godot.Collections.Array<String> ListUsernames()
    {
        var outArr = new Godot.Collections.Array<String>();
        foreach (var n in _userDb.ListUsernames()) // U13：typed Array<string>，元素直接是 string
        {
            outArr.Add(n);
        }

        return outArr;
    }

    /// <summary>2026-08-06 审计：UserDB 显式重载（测试 wipe user:// 后刷新缓存起点——GameState
    /// _ready 的迁移探测会提前缓存真实用户表；Q23 快照范式配套）</summary>
    public void ReloadUserDb() => _userDb.Reload();

    public string GetLastLoginUser() => _userDb.GetLastLoginUser();

    public bool DeleteUser(string name, string password) => _userDb.DeleteUser(name, password);

    public Godot.Collections.Array GetLeaderboard() => _userDb.GetLeaderboard();

    public Godot.Collections.Dictionary GetUserSettings(string name) => _userDb.GetUserSettings(name);

    public void UpdateUserSettings(string name, Godot.Collections.Dictionary settings) => _userDb.UpdateUserSettings(name, settings);

    public Godot.Collections.Dictionary GetUserData(string name) => _userDb.GetUserData(name);

    /// <summary>用户记录通用字段合并更新（统计/档案类；密码/盐/迭代数不可经此覆盖）</summary>
    public void UpdateUserData(string name, Godot.Collections.Dictionary data) => _userDb.UpdateUserData(name, data);

    public string UserDbSavefileFor(string name) => _userDb.SavefileForUser(name);
}
