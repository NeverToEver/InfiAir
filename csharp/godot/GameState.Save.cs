using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：对局存档 / 局外档案。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 对局存档（登录用户 = user://savegame_<user>_<hash12>.json；游客不存档） ----------------

    public void SaveRun(double fuel, double elapsed)
    {
        if (IsGuest())
        {
            return; // 游客不存档（B7-8）
        }

        var path = SavePathForCurrent();
        if (path == "")
        {
            return;
        }

        var data = new Godot.Collections.Dictionary
        {
            ["version"] = PersistVersionValue,
            ["score"] = Score,
            ["kills"] = Kills,
            ["health"] = Health,
            ["fuel"] = fuel,
            ["boss_kills"] = BossKills,
            ["difficulty_multiplier"] = DifficultyMultiplier,
            // v3：buffs/chosen_routes/locked_routes 废弃，天赋缓存域整体随档往返（TalentService.SaveState）
            ["talent"] = _talent.SaveState(),
            ["elapsed"] = elapsed,
            ["rp"] = Rp,
            ["refresh_points"] = RefreshPoints,
            ["missions"] = Missions.Duplicate(true),
            ["ctrl_toggle_mode"] = CtrlToggleMode,
            ["shift_toggle_mode"] = ShiftToggleMode,
            ["touch_controls"] = TouchControls,
        };
        if (CurrentUser != "")
        {
            data["username"] = CurrentUser;
        }

        // A2 阶段 2：文件 IO 委托 SaveManager
        _saveManager.Save(path, data);
    }

    public bool HasSave()
    {
        if (IsGuest())
        {
            return false;
        }

        return _saveManager.Exists(SavePathForCurrent());
    }

    public Godot.Collections.Dictionary LoadRunData()
    {
        SaveCorrupt = false;
        var path = SavePathForCurrent();
        if (path == "" || !_saveManager.Exists(path))
        {
            return new Godot.Collections.Dictionary();
        }

        var data = _saveManager.Load(path);
        if (_saveManager.LastWasCorrupt)
        {
            // 损坏存档已由 SaveManager 隔离备份（<path>.corrupt），按无存档处理（不留死路径）。
            // 2026-08-06 审计 M2：必须直接返回——空字典继续做档主校验会走 quarantine 二次隔离，
            // 先删刚生成的 .corrupt 备份再 rename 已不存在的正本（失败刷伪警告），损坏档彻底消失
            SaveCorrupt = true;
            return new Godot.Collections.Dictionary();
        }

        if (CurrentUser != "" && data.GetValueOrDefault("username", "").AsString() != CurrentUser)
        {
            // B5 读档校验：档主不匹配（手改/旧匿名档）→ 隔离备份按无存档处理
            _saveManager.Quarantine(path);
            SaveCorrupt = true;
            return new Godot.Collections.Dictionary();
        }

        return data;
    }

    /// <summary>存档数值字段安全读取：手改存档的非法类型（字符串/数组/字典等）回默认值
    /// （委托 SaveManager 壳 sanitize_num——GDScript 浮点为 64 位，经 Variant 往返保持逐位等价）</summary>
    public double SaveNum(Variant v, double defaultValue) => _saveManager.SanitizeNum(v, defaultValue);

    /// <summary>C16 修复：布尔字段安全读取——仅接受真 bool（GDScript 的 bool("false") 为 true，
    /// 手改存档写字符串 "false"/"0" 会被误读为开；与 save_num 同款判型回退）</summary>
    public bool SaveBool(Variant v, bool defaultValue) => v.VariantType == Variant.Type.Bool ? v.AsBool() : defaultValue;

    /// <summary>存档整数字段安全读取：save_num 判型 + 钳入 [0, int.MaxValue]（2026-08-10 健壮性审查）——
    /// 手改存档超大值（&gt;2^31）经裸 (int) 截断会回绕成负数（score/kills/rp/date 等统计与
    /// 里程碑错乱、2038 年后时间戳回绕），先钳 long 域再转 int</summary>
    /// <summary>public：天赋域服务（TalentService）存档恢复同样依赖判型+钳制读取（SaveNum/SaveBool 同口径）。</summary>
    public int SaveInt(Variant v, int defaultValue) => (int)Math.Clamp(SaveNum(v, defaultValue), 0.0, (double)int.MaxValue);

    public void ApplyRunSave(Godot.Collections.Dictionary data)
    {
        // 逐字段判型：语法合法但结构非法的存档（手改）不崩，异常字段回默认值
        // R07：负值钳 0（L 系列判型族登记遗留）——手改负 score/kills 破坏统计与排行榜
        Score = SaveInt(data.GetValueOrDefault("score", 0), 0);
        Kills = SaveInt(data.GetValueOrDefault("kills", 0), 0);
        BossKills = SaveInt(data.GetValueOrDefault("boss_kills", 0), 0);
        DifficultyMultiplier = SaveNum(data.GetValueOrDefault("difficulty_multiplier", 1.0), 1.0);
        // 天赋缓存域恢复（v3 起随档往返；v2 旧档无 talent 键 → 全新天赋态，无兼容层）。
        // 判型/钳制/成员资格校验在 RestoreState 内；不发事件——AugmentsChanged 仍由下方直发
        // （Player.RefreshAugmentFactors/Hud 坞缓存+信号驱动）
        var talentV = data.GetValueOrDefault("talent", new Variant());
        if (talentV.VariantType == Variant.Type.Dictionary)
        {
            _talent.RestoreState(talentV.AsGodotDictionary());
        }
        else
        {
            _talent.RestoreState(new Godot.Collections.Dictionary());
        }

        EmitSignal(SignalName.AugmentsChanged);
        // 血量在天赋恢复之后再处理（max_health 依赖 extra_life 层级）
        // v1/v2 存档天赋态为全新（extra_life 归零 → 上限回落基础值），health 钳制后按该上限恢复
        if ((int)SaveNum(data.GetValueOrDefault("version", 1), 1.0) >= 2)
        {
            _combat.RestoreHealth(SaveNum(data.GetValueOrDefault("health", MaxHealth()), MaxHealth()));
        }
        else
        {
            _combat.RestoreHealth(MaxHealth());
        }

        // AB12：elapsed 钳 [0, 1e6]（≈11.6 天，远超合理对局时长）——SaveNum 仅判型无上界，
        // 手改超大值经 (long) 未定义转换得 long.MinValue 使难度乘数巨负，击穿单调不减防线
        RunTime = Math.Clamp(SaveNum(data.GetValueOrDefault("elapsed", 0.0), 0.0), 0.0, 1e6);
        // 难度乘数按曲线从 boss_kills + run_time 重算（旧档的 difficulty_multiplier 字段仅作读入兼容）
        _runProg.RecomputeDifficultyInternal();
        Rp = SaveInt(data.GetValueOrDefault("rp", 0), 0);
        // 任务轮换：刷新点数随存档往返（手改负值钳制 ≥0）
        RefreshPoints = SaveInt(data.GetValueOrDefault("refresh_points", 0), 0);
        EmitSignal(SignalName.RefreshPointsChanged, RefreshPoints);
        InitMissions();
        // 任务轮换：先清空初始手牌再恢复存档任务——存档集合可能含池内非手牌 id
        // （如 kill_15），不清空会使初始手牌未在存档中的 id（survive_180/boss_1）残留
        Missions.Clear();
        var savedMissions = data.GetValueOrDefault("missions", new Variant());
        if (savedMissions.VariantType == Variant.Type.Dictionary)
        {
            foreach (var key in savedMissions.AsGodotDictionary().Keys)
            {
                var id = key.AsStringName();
                var m = savedMissions.AsGodotDictionary()[key];
                // 任务轮换：恢复条件从「初始手牌包含」放宽为「id 属于任务池」——
                // 轮换后的任务（如 kill_15）不在初始手牌，必须能随存档恢复
                if (MissionDef(id).Count == 0 || m.VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                var md = m.AsGodotDictionary();
                var claimed = md.GetValueOrDefault("claimed", false);
                // H18（健壮性审核）：恢复保留 goal 键——整体替换会丢 goal 致
                // mission_completed 判定 progress >= 0 恒真而永久哑火（潜伏）
                Missions[id] = new Godot.Collections.Dictionary
                {
                    ["progress"] = SaveInt(md.GetValueOrDefault("progress", 0), 0),
                    ["claimed"] = claimed.VariantType == Variant.Type.Bool ? claimed : false,
                    // 2026-08-06 审计：goal 走 save_num 判型（R06/R07 判型族同族遗漏——裸 int()
                    // 对手改字符串/数组值抛类型错误或静默转 0 使任务永久可领）；2026-08-10 起
                    // SaveInt 统一钳入 [0, int.MaxValue]（防超大值截断回绕致任务瞬时可领）
                    ["goal"] = SaveInt(md.GetValueOrDefault("goal", MissionGoal(id)), MissionGoal(id)),
                };
            }
        }

        // 设置项随存档往返（旧存档无字段时保留当前值）——第六轮拆域：直写 SettingsService 字段
        // （不经 setter，不触发服务事件；TouchControlsChanged 由下方直发同名信号，无双发）
        _settings.CtrlToggleMode = SaveBool(data.GetValueOrDefault("ctrl_toggle_mode", CtrlToggleMode), CtrlToggleMode);
        _settings.ShiftToggleMode = SaveBool(data.GetValueOrDefault("shift_toggle_mode", ShiftToggleMode), ShiftToggleMode);
        _settings.TouchControls = SaveBool(data.GetValueOrDefault("touch_controls", TouchControls), TouchControls);
        // AB14：恢复值回流 VirtualControls——存档恢复只写内存字段不广播，启用态与设置页脱钩
        // （Ctrl/Shift 直读字段不受影响，唯独触屏有状态缓存消费方；与 :134 AugmentsChanged 同款发射）
        EmitSignal(SignalName.TouchControlsChanged, TouchControls);
        // 里程碑曲线：恢复到大于当前分数的第一档（2026-08-07 批量推进迁移 C# 侧——
        // CountThresholdsUpTo 单次调用 + O(1)/档 增量推进，含原 while 的 10000 档挂死守卫；
        // 原逐档跨语言往返的 while 循环删除，存档恢复路径不再每档一次 GDScript 求值）
        // 第五轮拆域：直写两行收拢为 ScoreService.RestoreMilestones（内部字段/阈值求值，
        // 不发事件——信号由下方直发保持顺序）
        _score.RestoreMilestones(Score);
        EmitSignal(SignalName.ScoreChanged, Score);
        EmitSignal(SignalName.HealthChanged, Health);
        EmitSignal(SignalName.DifficultyChanged, DifficultyMultiplier);
        EmitSignal(SignalName.RpChanged, Rp);
    }

    public void DeleteSave()
    {
        var path = SavePathForCurrent();
        if (path != "")
        {
            _saveManager.Delete(path);
        }
    }

    /// <summary>死亡结算原子链（Y 系列下沉，2026-08-09）：DeleteSave → RecordGameOver →
    /// SettleTechPoints——GameOverUi 仍为 PlayerDied 信号订阅者，信号同步调用顺序不变。</summary>
    public void SettleRun()
    {
        DeleteSave(); // 死亡删档：防止一死档永存
        RecordGameOver(); // Q06：登录用户累计 total_kills/games_played（游客跳过）
        SettleTechPoints(); // 局外成长：死亡结算科技点（游客/未登录 no-op，2026-08-09）
    }

    /// <summary>对局存档（无参版，Y 系列下沉）：内部经注册表取 PlayerRef→FuelAmount()，
    /// elapsed 直读 RunTime（2026-08-13：与恢复侧 RunTime 回灌同一时钟源；缺 Player 兜底 100）
    /// ——PauseUi 不再编排取值。两参版保留（Main.cs 返航自动存档持有实例直传；测试契约）。</summary>
    public void SaveRun()
    {
        var player = PlayerRef as Player;
        SaveRun(player != null ? player.FuelAmount() : 100.0f, RunTime);
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
