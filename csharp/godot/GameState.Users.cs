using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：用户会话（账户系统）。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 用户会话（2026-08-04 账户系统） ----------------

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
        TranslationServer.SetLocale(Locale);
        ApplyKeyBindings();
        ApplyWindowSize();
        InvalidateViewRectCache();
    }

    /// <summary>游客进入：设置仅内存、不存档、不写统计（B7-8）；保留当前内存值（启动 profile 值视作游客会话）</summary>
    public void LoginGuest() => CurrentUser = "Guest";

    /// <summary>退出：登录用户落盘设置；游客丢弃（内存）；复位未登录</summary>
    public void LogoutUser()
    {
        if (CurrentUser != "" && CurrentUser != "Guest")
        {
            SaveProfile();
        }

        CurrentUser = "";
    }

    public bool IsGuest() => CurrentUser == "Guest";

    /// <summary>当前会话存档路径：登录用户 = 每用户文件；未登录 = 旧单文件；游客无路径（不存档）</summary>
    private string SavePathForCurrent()
    {
        if (CurrentUser == "")
        {
            return SavePathValue;
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

        ApplySettingsDict(_userDb.GetUserSettings(CurrentUser));
        HighScore = (int)_userDb.GetUserData(CurrentUser).GetValueOrDefault("high_score", 0).AsInt64();
    }

    /// <summary>profile.json 退役迁移（B5）：启动时存在旧 profile 且用户表为空 → 缓存待首个注册用户合并</summary>
    private void MaybeMigrateLegacyProfile()
    {
        if (_pendingLegacyProfile.Count > 0)
        {
            return;
        }

        if (_saveManager.Exists(ProfilePathValue) && _userDb.ListUsernames().Count == 0)
        {
            var parsed = _saveManager.Load(ProfilePathValue);
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
            _userDb.UpdateHighScore(name, (int)SaveNum(legacy.GetValueOrDefault("high_score", 0), 0.0));
            legacy.Remove("high_score");
            legacy.Remove("version");
            legacy.Remove("highscores");
            _userDb.UpdateUserSettings(name, legacy);
            _saveManager.Delete(ProfilePathValue);
        }

        return true;
    }

    /// <summary>用户数据库转发（A2 组合服务；供 welcome 登录面板使用）</summary>
    public bool VerifyUser(string name, string password) => _userDb.VerifyUser(name, password);

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

    public string UserDbSavefileFor(string name) => _userDb.SavefileForUser(name);
}
