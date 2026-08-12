using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：用户会话（账户系统）。
/// 第七轮拆域收官（2026-08-12）：全部职责迁至 UserSessionService（csharp/godot/UserSessionService.cs，
/// 组合持有；LoginUser/LoginGuest/LogoutUser/IsGuest/LoadSessionSettings/MaybeMigrateLegacyProfile/
/// LegacyMigrationPending/ScanLegacyMigration/ClearLegacyMigration/CreateUser + UserDB 转发 14 个
/// 一并迁入），本文件为门面对齐转发——公开 API 签名/语义不变（测试白盒经此处零适配直调
/// LoginUser/LoginGuest/LogoutUser/IsGuest/CreateUser/VerifyUser/ListUsernames/GetLeaderboard 等
/// 全保留）；CurrentUser 状态在 GameState.State.cs 转发（原定义处）；SavePathForCurrent 保留
/// 私有一行包装（GameState.Save.cs 内部调用零改动）。
/// 信号：Users 域不广播，无需信号重发。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 用户会话（门面转发 → UserSessionService） ----------------

    /// <summary>登录已有用户：载入其设置/最高分并即时生效（locale 即时 set_locale——B7-11）</summary>
    public void LoginUser(string name) => _session.LoginUser(name);

    /// <summary>游客进入：设置仅内存、不存档、不写统计（B7-8）；保留当前内存值（启动 profile 值视作游客会话）</summary>
    public void LoginGuest() => _session.LoginGuest();

    /// <summary>退出：登录用户落盘设置；游客丢弃（内存）；复位未登录</summary>
    public void LogoutUser() => _session.LogoutUser();

    public bool IsGuest() => _session.IsGuest();

    /// <summary>当前会话存档路径：登录用户 = 每用户文件；未登录 = 旧单文件；游客无路径（不存档）
    /// ——私有一行包装（本体在 UserSessionService；GameState.Save.cs 内部调用）。</summary>
    private string SavePathForCurrent() => _session.SavePathForCurrent();

    /// <summary>Q25（2026-08-05）：旧 profile 迁移缓存查询/触发/清空公开化（A7 私有访问残留收敛，
    /// 测试经公开接口；生产路径不变——create_user 消费后自清）</summary>
    public bool LegacyMigrationPending() => _session.LegacyMigrationPending();

    public void ScanLegacyMigration() => _session.ScanLegacyMigration();

    public void ClearLegacyMigration() => _session.ClearLegacyMigration();

    /// <summary>注册用户（转发 user_db.create_user）；成功后合并旧 profile 迁移数据并删除 profile.json（B5）</summary>
    public bool CreateUser(string name, string password) => _session.CreateUser(name, password);

    /// <summary>用户数据库转发（A2 组合服务；供 welcome 登录面板使用）</summary>
    public bool VerifyUser(string name, string password) => _session.VerifyUser(name, password);

    /// <summary>users.json 损坏标志（2026-08-10 健壮性审查：与 SaveCorrupt/ProfileCorrupt 对齐，
    /// 欢迎页提示玩家账号已隔离备份而非丢失）</summary>
    public bool UserDbCorrupt => _session.UserDbCorrupt;

    public bool UserExists(string name) => _session.UserExists(name);

    public Godot.Collections.Array<String> ListUsernames() => _session.ListUsernames();

    /// <summary>2026-08-06 审计：UserDB 显式重载（测试 wipe user:// 后刷新缓存起点——GameState
    /// _ready 的迁移探测会提前缓存真实用户表；Q23 快照范式配套）</summary>
    public void ReloadUserDb() => _session.ReloadUserDb();

    public string GetLastLoginUser() => _session.GetLastLoginUser();

    public bool DeleteUser(string name, string password) => _session.DeleteUser(name, password);

    public Godot.Collections.Array GetLeaderboard() => _session.GetLeaderboard();

    public Godot.Collections.Dictionary GetUserSettings(string name) => _session.GetUserSettings(name);

    public void UpdateUserSettings(string name, Godot.Collections.Dictionary settings) => _session.UpdateUserSettings(name, settings);

    public Godot.Collections.Dictionary GetUserData(string name) => _session.GetUserData(name);

    /// <summary>用户记录通用字段合并更新（统计/档案类；密码/盐/迭代数不可经此覆盖）</summary>
    public void UpdateUserData(string name, Godot.Collections.Dictionary data) => _session.UpdateUserData(name, data);

    public string UserDbSavefileFor(string name) => _session.UserDbSavefileFor(name);
}
