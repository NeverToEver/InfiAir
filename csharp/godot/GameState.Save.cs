using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：对局存档 / 局外档案 / 榜单。
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
            ["buffs"] = Buffs.Duplicate(),
            ["elapsed"] = elapsed,
            ["rp"] = Rp,
            ["refresh_points"] = RefreshPoints,
            ["missions"] = Missions.Duplicate(true),
            ["chosen_routes"] = ChosenRoutes.Duplicate(),
            ["locked_routes"] = LockedRoutes.Duplicate(),
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
    private int SaveInt(Variant v, int defaultValue) => (int)Math.Clamp(SaveNum(v, defaultValue), 0.0, (double)int.MaxValue);

    public void ApplyRunSave(Godot.Collections.Dictionary data)
    {
        // 逐字段判型：语法合法但结构非法的存档（手改）不崩，异常字段回默认值
        // R07：负值钳 0（L 系列判型族登记遗留）——手改负 score/kills 破坏统计与排行榜
        Score = SaveInt(data.GetValueOrDefault("score", 0), 0);
        Kills = SaveInt(data.GetValueOrDefault("kills", 0), 0);
        BossKills = SaveInt(data.GetValueOrDefault("boss_kills", 0), 0);
        DifficultyMultiplier = SaveNum(data.GetValueOrDefault("difficulty_multiplier", 1.0), 1.0);
        // 第五轮拆域：buffs 恢复改调 CombatStateService（判型/钳制/G013 注释随迁；不发事件——
        // BuffsChanged 仍由下方直发同名信号，不经服务事件，无双发）
        _combat.RestoreBuffs(data.GetValueOrDefault("buffs", new Variant()));
        EmitSignal(SignalName.BuffsChanged);
        // 血量在 buffs 恢复之后再处理（max_health() 依赖 extra_life 层数）
        // v1（3 命制 lives）存档不回迁血量，按满血开；v2 起读 health（钳制在 RestoreHealth 内）
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

        ChosenRoutes.Clear();
        var savedChosen = data.GetValueOrDefault("chosen_routes", new Variant());
        if (savedChosen.VariantType == Variant.Type.Dictionary)
        {
            foreach (var key in savedChosen.AsGodotDictionary().Keys)
            {
                var v = savedChosen.AsGodotDictionary()[key];
                if (v.VariantType is Variant.Type.String or Variant.Type.StringName)
                {
                    ChosenRoutes[key.AsStringName()] = v.AsStringName();
                }
            }
        }

        LockedRoutes.Clear();
        var savedLocked = data.GetValueOrDefault("locked_routes", new Variant());
        if (savedLocked.VariantType == Variant.Type.Dictionary)
        {
            foreach (var key in savedLocked.AsGodotDictionary().Keys)
            {
                var v = savedLocked.AsGodotDictionary()[key];
                if (v.VariantType is Variant.Type.String or Variant.Type.StringName)
                {
                    LockedRoutes[key.AsStringName()] = v.AsStringName();
                }
            }
        }

        // 设置项随存档往返（旧存档无字段时保留当前值）——第六轮拆域：直写 SettingsService 字段
        // （不经 setter，不触发服务事件；TouchControlsChanged 由下方直发同名信号，无双发）
        _settings.CtrlToggleMode = SaveBool(data.GetValueOrDefault("ctrl_toggle_mode", CtrlToggleMode), CtrlToggleMode);
        _settings.ShiftToggleMode = SaveBool(data.GetValueOrDefault("shift_toggle_mode", ShiftToggleMode), ShiftToggleMode);
        _settings.TouchControls = SaveBool(data.GetValueOrDefault("touch_controls", TouchControls), TouchControls);
        // AB14：恢复值回流 VirtualControls——存档恢复只写内存字段不广播，启用态与设置页脱钩
        // （Ctrl/Shift 直读字段不受影响，唯独触屏有状态缓存消费方；与 :134 BuffsChanged 同款发射）
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

    /// <summary>死亡结算原子链（Y 系列下沉，2026-08-09）：DeleteSave → RecordScore →
    /// RecordGameOver → SubmitHighscore(Score)，返回 (是否破纪录, 本地榜名次) 快照——
    /// GameOverUi.OnPlayerDied 不再逐步编排，结算后 UI 读 Score/HighScore 时序与原来一致
    /// （GameOverUi 仍为 PlayerDied 信号订阅者，信号同步调用顺序不变）。</summary>
    public (bool NewRecord, int Rank) SettleRun()
    {
        DeleteSave(); // 死亡删档：防止一死档永存
        var newRecord = RecordScore();
        RecordGameOver(); // Q06：登录用户累计 total_kills/games_played（游客跳过）
        var rank = (int)SubmitHighscore(Score); // P0-3：本局分数提交本地榜
        SettleTechPoints(); // 局外成长：死亡结算科技点（游客/未登录 no-op，2026-08-09）
        return (newRecord, rank);
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

        // save_int 判型+钳制：手改档案 high_score > 2^31 时裸 (int) 截断回绕为负，
        // RecordScore 的 Score > HighScore 恒真 → 每局误报破纪录
        HighScore = SaveInt(parsed.GetValueOrDefault("high_score", 0), 0);
        // 第六轮拆域：设置域持久化桥迁 SettingsService（设置字段应用含键位/窗口/视图缓存副作用）
        _settings.ApplySettingsDict(parsed);
        // P0-3：高分榜判型加载（手改档案的元素级守卫，对齐 E11）——非法条目跳过、排序截断
        Highscores.Clear();
        var savedHighscores = parsed.GetValueOrDefault("highscores", new Variant());
        if (savedHighscores.VariantType == Variant.Type.Array)
        {
            foreach (var entryV in savedHighscores.AsGodotArray())
            {
                if (entryV.VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                var entry = entryV.AsGodotDictionary();
                var s = entry.GetValueOrDefault("score", 0);
                if (s.VariantType is not Variant.Type.Int and not Variant.Type.Float)
                {
                    continue;
                }

                Highscores.Add(new Godot.Collections.Dictionary { ["score"] = SaveInt(s, 0), ["date"] = SaveInt(entry.GetValueOrDefault("date", 0), 0) }); // E11 同款：date 走 save_num 判型
            }

            // 泛型 Array&lt;Dictionary&gt; 无 SortCustom（Godot C# 未绑定）——List.Sort 降序重建
            var sortList = new List<Godot.Collections.Dictionary>();
            foreach (var entry in Highscores)
            {
                sortList.Add(entry);
            }

            // U15：int64 直接比较（原 (int) 截断 + 减法——score>2^31 手改档案排序语义漂移）
            sortList.Sort((a, b) => b["score"].AsInt64().CompareTo(a["score"].AsInt64()));
            Highscores.Clear();
            foreach (var entry in sortList)
            {
                Highscores.Add(entry);
            }

            if (Highscores.Count > HighscoreLimitValue)
            {
                Highscores.Resize(HighscoreLimitValue);
            }
        }
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
        data["high_score"] = HighScore;
        data["highscores"] = Highscores;
        _saveManager.Save(ProfilePathValue, data);
    }

    /// <summary>记录最高分，破纪录返回 true（登录用户写 user_db；游客仅内存；未登录写旧 profile.json）</summary>
    public bool RecordScore()
    {
        if (Score > HighScore)
        {
            HighScore = Score;
            if (IsGuest())
            {
                return true;
            }

            if (CurrentUser != "")
            {
                _userDb.UpdateHighScore(CurrentUser, Score);
            }
            else
            {
                SaveProfile();
            }

            return true;
        }

        return false;
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

    /// <summary>提交本局分数入本地榜，返回名次（1-based；未上榜返回 0）。
    /// 同分新条目排后（先到先得）；超出上限的分数不入榜。登录/游客走 user_db 排行榜（游客以 "Guest" 提交，B7-8）。</summary>
    public int SubmitHighscore(int runScore)
    {
        if (CurrentUser != "")
        {
            return (int)_userDb.SubmitScore(CurrentUser, runScore);
        }

        if (runScore <= 0)
        {
            return 0;
        }

        var rank = 1;
        foreach (var e in Highscores)
        {
            if ((int)e["score"].AsInt64() >= runScore)
            {
                rank += 1;
            }
            else
            {
                break;
            }
        }

        if (rank > HighscoreLimitValue)
        {
            return 0;
        }

        Highscores.Insert(rank - 1, new Godot.Collections.Dictionary
        {
            ["score"] = runScore,
            // 2026-08-10 健壮性审查：时间戳钳 int 上限——2038 年后 (int) 截断回绕为负
            ["date"] = (int)Math.Min(Time.GetUnixTimeFromSystem(), int.MaxValue),
        });
        if (Highscores.Count > HighscoreLimitValue)
        {
            Highscores.Resize(HighscoreLimitValue);
        }

        SaveProfile();
        return rank;
    }

    /// <summary>榜单文本（供结算页/开始页展示）："1. 12345\n2. 9876..."；空榜返回空串</summary>
    public string HighscoresText(int limit = 5)
    {
        if (CurrentUser != "")
        {
            var board = _userDb.GetLeaderboard();
            if (board.Count == 0)
            {
                return "";
            }

            var lines = new List<string>();
            for (var i = 0; i < Mathf.Min(limit, board.Count); i++)
            {
                // AB17：显示处钳制（双保险）——core 已归一化，此处防未来其他入口绕过 (int) 回绕
                lines.Add(GdFormat.Format("%d. %d", i + 1,
                    (int)Math.Clamp(board[i].AsGodotDictionary()["score"].AsInt64(), 0L, (long)int.MaxValue)));
            }

            return string.Join("\n", lines);
        }

        if (Highscores.Count == 0)
        {
            return "";
        }

        var localLines = new List<string>();
        for (var i = 0; i < Mathf.Min(limit, Highscores.Count); i++)
        {
            localLines.Add(GdFormat.Format("%d. %d", i + 1, (int)Highscores[i]["score"].AsInt64()));
        }

        return string.Join("\n", localLines);
    }

    /// <summary>GDScript 字符串 % 格式化语义（%s/%d/%f 占位 + %% 转义；tr() 文案补参用，
}
