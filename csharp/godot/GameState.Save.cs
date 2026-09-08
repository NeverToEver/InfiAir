using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：局外档案（profile.json 设置持久化）。
/// 对局存档系统已移除：每次启动均为全新一局，死亡即结算、无续局。
/// </summary>
public partial class GameState : Node
{

    /// <summary>存档数值字段安全读取：手改数据的非法类型（字符串/数组/字典等）回默认值
    /// （委托 SaveManager 壳 sanitize_num——GDScript 浮点为 64 位，经 Variant 往返保持逐位等价）</summary>
    public double SaveNum(Variant v, double defaultValue) => _saveManager.SanitizeNum(v, defaultValue);

    /// <summary>C16 修复：布尔字段安全读取——仅接受真 bool（GDScript 的 bool("false") 为 true，
    /// 手改数据写字符串 "false"/"0" 会被误读为开；与 sanitize_num 同款判型回退）</summary>
    public bool SaveBool(Variant v, bool defaultValue) => v.VariantType == Variant.Type.Bool ? v.AsBool() : defaultValue;

    /// <summary>存档整数字段安全读取：save_num 判型 + 钳入 [0, int.MaxValue]（2026-08-10 健壮性审查）——
    /// 手改超大值（&gt;2^31）经裸 (int) 截断会回绕成负数（统计与里程碑错乱、2038 年后时间戳回绕），
    /// 先钳 long 域再转 int</summary>
    public int SaveInt(Variant v, int defaultValue) => (int)Math.Clamp(SaveNum(v, defaultValue), 0.0, (double)int.MaxValue);

    /// <summary>死亡结算原子链（Y 系列下沉，2026-08-09）：RecordGameOver →
    /// SettleTechPoints——GameOverUi 仍为 PlayerDied 信号订阅者，信号同步调用顺序不变。</summary>
    public void SettleRun()
    {
        RecordGameOver(); // Q06：登录用户累计 total_kills/games_played（游客跳过）
        SettleTechPoints(); // 局外成长：死亡结算科技点（游客/未登录 no-op，2026-08-09）
    }

    // ---------------- 局外档案（登录用户 = user_db settings；游客仅内存；未登录 = 旧 profile.json 兼容路径） ----------------

    /// <summary>局外档案：最高分 + 难度档位 + 设置项（旧版 talents/talent_points 字段读取时忽略；
    /// 旧档案缺少新字段时保留当前内存值，保证兼容；损坏文件隔离备份后按默认值继续）</summary>
    public void LoadProfile()
    {
        ProfileCorrupt = false;
        if (CurrentUser != "")
        {
            return; // 会话模式下档案由登录流程管理（_load_session_settings）
        }

        var parsed = _saveManager.Load(ProfilePathValue);
        if (_saveManager.LastWasCorrupt)
        {
            ProfileCorrupt = true;
            return;
        }

        if (parsed.Count == 0)
        {
            return;
        }

        // 第六轮拆域：设置域持久化桥迁 SettingsService（设置字段应用含键位/窗口/视图缓存副作用）
        _settings.ApplySettingsDict(parsed);
    }

    /// <summary>当前设置字段收集（profile.json 与 user_db settings 共用；统计类字段不在此列）——
    /// 第六轮拆域：设置域持久化桥迁 SettingsService（CollectSettingsDict 本体在服务侧，此处委托）。</summary>
    public void SaveProfile()
    {
        if (IsGuest())
        {
            return; // 游客设置仅内存（B7-8）
        }

        if (CurrentUser != "")
        {
            _userDb.UpdateUserSettings(CurrentUser, _settings.CollectSettingsDict());
            return;
        }

        var data = _settings.CollectSettingsDict();
        data["version"] = PersistVersionValue;
        _saveManager.Save(ProfilePathValue, data);
    }

    /// <summary>Q06（2026-08-05）：一局对局统计落地（账户计划 Task 2 game_over_stats）——死亡结算调用。
    /// 登录用户累计 total_kills/games_played；游客/未登录跳过（游客不写统计，B7-8）</summary>
    public void RecordGameOver()
    {
        if (CurrentUser == "" || IsGuest() || !_userDb.UserExists(CurrentUser))
        {
            return;
        }

        var data = _userDb.GetUserData(CurrentUser);
        // 2026-08-10 审查修复：累计字段读取改 save_int（判型 + 钳 [0, int.MaxValue]）——
        // 原裸 AsInt64() 对手改字符串/数组等非法类型无回退，且 (int) 截断使 >2^31 回绕为负
        _userDb.UpdateUserData(CurrentUser, new Godot.Collections.Dictionary
        {
            ["total_kills"] = SaveInt(data.GetValueOrDefault("total_kills", 0), 0) + Kills,
            ["games_played"] = SaveInt(data.GetValueOrDefault("games_played", 0), 0) + 1,
        });
    }

    /// <summary>GDScript 字符串 % 格式化语义（%s/%d/%f 占位 + %% 转义；tr() 文案补参用，
}
