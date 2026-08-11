using Godot;

namespace InfiAir;

/// <summary>
/// RP 经济 / 基地任务 / 天赋路线服务（第四轮拆域，2026-08-11）：原 GameState.Missions.cs 全部职责
/// 迁入本服务——征用点(RP)入账消费 / 常驻基地任务(进度按 kind 分发、领取、轮换刷新) / 互斥天赋路线。
/// Godot 绑定层：跨域访问（任务定义/路线表/刷新经济档位/Buffs）统一经 GameState.Instance；
/// 任务池 TaskPool（C# typed）为本服务内部状态（_taskPool，每局 InitMissions 重建）。
/// 门面转发先例：与 MetaService/BalanceService/SaveManager 同构——GameState 组合持有本服务，
/// GameState.Missions.cs 为门面对齐转发（签名/语义不变），保持唯一 autoload：GameState 约定。
/// 信号：本服务以 C# 事件 RpChanged/MissionCompleted/RefreshPointsChanged/RouteChosen 通知变化；
/// GameState 订阅后转发为同名信号。ChooseRoute 另直发 BuffsChanged（经 GameState.Instance，
/// MetaService.ApplyMetaLoadout 同款）。
/// </summary>
public sealed partial class MissionsService : RefCounted
{

    // ---------------- RP 经济 / 基地任务 / 天赋路线 ----------------

    /// <summary>征用点数（基地经济）</summary>
    public int Rp { get; set; } = 0;

    /// <summary>任务 id -> {"progress": int, "claimed": bool}</summary>
    public Godot.Collections.Dictionary Missions { get; set; } = new();

    /// <summary>天赋路线 line -> 所选 buff id</summary>
    public Godot.Collections.Dictionary ChosenRoutes { get; set; } = new();

    /// <summary>天赋路线 line -> 被锁定的未选 buff id（不进奖励池）</summary>
    public Godot.Collections.Dictionary LockedRoutes { get; set; } = new();

    /// <summary>刷新点数（RefreshPoints）经济：进基地每次 +GRANT_PER_VISIT，刷新任务消耗 REFRESH_COST
    /// （balance.json base_task 段覆盖，经 GameState 侧缓存读取）</summary>
    public int RefreshPoints { get; set; } = 0;

    /// <summary>任务池实例（InitMissions 重建，保证每次对局从全新洗牌序列开始；M7 已 typed）。</summary>
    private TaskPool? _taskPool;

    /// <summary>kind -> 池内全部该类型任务 id（进度按 kind 分发，任务轮换后 id 变化仍可推进）</summary>
    private readonly Godot.Collections.Dictionary _missionsByKind = new();

    /// <summary>任务领取奖励 RP（对齐原作 RequisitionConstants）。</summary>
    private const int RpMissionRewardValue = 3;

    /// <summary>RP 变化（AddRp/SpendRp，2 处触发点）；GameState 订阅后转发为 RpChanged 信号
    /// （存档恢复/ResetRun 直接赋值路径由 GameState 侧直发同名信号，不重复）。</summary>
    public event Action<int>? RpChanged;

    /// <summary>任务进度达到 goal（SetMissionProgress 越过完成线触发）；GameState 订阅后转发为
    /// MissionCompleted 信号。</summary>
    public event Action<StringName>? MissionCompleted;

    /// <summary>刷新点数变化（GrantRefreshPoints/RefreshMissions）；GameState 订阅后转发为
    /// RefreshPointsChanged 信号（存档恢复/ResetRun 直接赋值路径由 GameState 侧直发）。</summary>
    public event Action<int>? RefreshPointsChanged;

    /// <summary>天赋路线选定（ChooseRoute 成功）；GameState 订阅后转发为 RouteChosen 信号。</summary>
    public event Action<StringName, StringName>? RouteChosen;

    public void AddRp(int amount)
    {
        Rp += amount;
        RpChanged?.Invoke(Rp);
    }

    /// <summary>余额不足返回 false 且不扣减</summary>
    public bool SpendRp(int amount)
    {
        if (Rp < amount)
        {
            return false;
        }

        Rp -= amount;
        RpChanged?.Invoke(Rp);
        return true;
    }

    public void InitMissions()
    {
        Missions.Clear();
        foreach (var def in GameState.Instance.MISSION_DEFS)
        {
            // P0-3：goal 一次性缓存进条目，_set_mission_progress 免每帧线性扫 MISSION_POOL
            Missions[def["id"]] = new Godot.Collections.Dictionary { ["progress"] = 0, ["claimed"] = false, ["goal"] = (int)def["goal"].AsInt64() };
        }

        // 任务轮换：每局从全新洗牌序列开始（初始手牌固定 MISSION_DEFS，刷新才随机）
        _taskPool = new TaskPool(GameState.Instance.MISSION_POOL); // M7：TaskPool 迁 C#，typed
        RebuildKindIndex();
    }

    /// <summary>kind -> 池内 id 索引重建（MISSION_POOL 为 const，仅 _init_missions 调用一次）</summary>
    private void RebuildKindIndex()
    {
        _missionsByKind.Clear();
        foreach (var def in GameState.Instance.MISSION_POOL)
        {
            var kind = def["kind"].AsStringName();
            if (!_missionsByKind.ContainsKey(kind))
            {
                _missionsByKind[kind] = new Godot.Collections.Array();
            }

            _missionsByKind[kind].AsGodotArray().Add(def["id"]);
        }
    }

    /// <summary>C32 修复：公开任务重置口（仅清任务进度，不清 rp/buffs——比 reset_run 副作用小，
    /// 供测试/调用方在保留状态的前提下重置 missions）</summary>
    public void ResetMissions() => InitMissions();

    private void SetMissionProgress(StringName id, int value)
    {
        if (!Missions.ContainsKey(id))
        {
            return;
        }

        var m = Missions[id].AsGodotDictionary();
        // P4（2026-08-05）：进度负值钳 0（防御；正常路径 value 恒 ≥0，手改存档/异常注入不产生负进度）
        var clamped = Mathf.Max(value, 0);
        // P0-3：survive 类每帧触发但整秒才变化一次，未变化跳过字典写与完成判定
        if ((int)m["progress"].AsInt64() == clamped)
        {
            return;
        }

        var goal = (int)m.GetValueOrDefault("goal", 0).AsInt64();
        var wasDone = (int)m["progress"].AsInt64() >= goal;
        m["progress"] = clamped;
        if (!wasDone && clamped >= goal)
        {
            MissionCompleted?.Invoke(id);
        }
    }

    /// <summary>按 kind 推进全部该类型在场任务的进度（任务轮换后 id 变化，进度源按 kind 分发；
    /// 已不在场的 id 由 _set_mission_progress 的 missions.has 守卫自动跳过）</summary>
    public void SetKindProgress(StringName kind, int value)
    {
        // U16：TryGetValue 免空容器默认值每次分配（原 GetValueOrDefault 实参先求值分配空 Array）
        if (_missionsByKind.TryGetValue(kind, out var list))
        {
            foreach (var idV in list.AsGodotArray())
            {
                SetMissionProgress(idV.AsStringName(), value);
            }
        }
    }

    /// <summary>在场任务 id 列表（base_console 任务面板据此渲染；任务轮换后不再等于 MISSION_DEFS）</summary>
    public Godot.Collections.Array<StringName> ActiveMissionIds()
    {
        var outArr = new Godot.Collections.Array<StringName>();
        foreach (var id in Missions.Keys)
        {
            outArr.Add(id.AsStringName());
        }

        return outArr;
    }

    /// <summary>任务定义查询（MISSION_POOL 无命中返回 {}，供 goal/存档恢复校验共用）</summary>
    public Godot.Collections.Dictionary MissionDef(StringName id)
    {
        foreach (var def in GameState.Instance.MISSION_POOL)
        {
            if (def["id"].AsStringName() == id)
            {
                return def;
            }
        }

        return new Godot.Collections.Dictionary();
    }

    public int MissionGoal(StringName id) => (int)MissionDef(id).GetValueOrDefault("goal", 0).AsInt64();

    // U16：TryGetValue 免空容器默认值每次分配（原 GetValueOrDefault 实参先求值分配空 Dictionary）
    public int MissionProgress(StringName id) =>
        Missions.TryGetValue(id, out var rec)
            ? (int)rec.AsGodotDictionary().GetValueOrDefault("progress", 0).AsInt64()
            : 0;

    public bool IsMissionDone(StringName id) => Missions.ContainsKey(id) && MissionProgress(id) >= MissionGoal(id);

    public bool IsMissionClaimed(StringName id) =>
        Missions.TryGetValue(id, out var rec)
            && (bool)rec.AsGodotDictionary().GetValueOrDefault("claimed", false).AsBool();

    /// <summary>领取已完成任务的 +3RP，每任务每局限领一次</summary>
    public bool ClaimMission(StringName id)
    {
        if (!IsMissionDone(id) || IsMissionClaimed(id))
        {
            return false;
        }

        Missions[id].AsGodotDictionary()["claimed"] = true;
        AddRp(RpMissionRewardValue);
        return true;
    }

    // ---------------- 基地任务轮换（RefreshPoints 经济 + TaskPool 重抽） ----------------

    /// <summary>进基地发放刷新点数（amount &lt; 0 用 GRANT_PER_VISIT 档位值；base_console.show_base 调用）</summary>
    public void GrantRefreshPoints(int amount = -1)
    {
        RefreshPoints += amount < 0 ? GameState.Instance.GRANT_PER_VISIT : amount;
        RefreshPointsChanged?.Invoke(RefreshPoints);
    }

    /// <summary>刷新资格校验（点数不足禁止刷新；UI 据此禁用按钮并提示）</summary>
    public bool CanRefreshMissions() => RefreshPoints >= GameState.Instance.REFRESH_COST;

    /// <summary>刷新任务：消耗 RefreshPoints 重抽任务（槽位数 MISSION_SLOTS）。
    /// 已完成未领取的任务保留（防止刷新吞掉待领奖励），其余槽位从任务池无放回重抽
    /// （排除在场 id，避免与保留槽位重号）。余额不足返回 false 且不扣减。</summary>
    public bool RefreshMissions()
    {
        if (!CanRefreshMissions())
        {
            return false;
        }

        if (_taskPool == null || !GodotObject.IsInstanceValid(_taskPool))
        {
            InitMissions(); // 防御：池未初始化（异常时序）时重建
        }

        RefreshPoints -= GameState.Instance.REFRESH_COST;
        RefreshPointsChanged?.Invoke(RefreshPoints);
        // 收集保留条目（已完成未领取）与在场 id（重抽排除全部在场 id：
        // 既防抽回刚换下的任务，也防与保留任务重号覆盖其进度）
        var kept = new Godot.Collections.Dictionary();
        var exclude = new Godot.Collections.Array<StringName>();
        foreach (var idV in Missions.Keys)
        {
            var id = idV.AsStringName();
            if (IsMissionDone(id) && !IsMissionClaimed(id))
            {
                kept[id] = Missions[id];
            }

            exclude.Add(id);
        }

        var drawn = _taskPool!.Draw(GameState.Instance.MISSION_SLOTS - kept.Count, exclude);
        Missions.Clear();
        foreach (var idV in kept.Keys)
        {
            Missions[idV] = kept[idV];
        }

        foreach (var def in drawn) // U13：Draw 返回 typed Array<Dictionary>，元素直接是 Dictionary
        {
            Missions[def["id"]] = new Godot.Collections.Dictionary { ["progress"] = 0, ["claimed"] = false, ["goal"] = def["goal"] };
        }

        return true;
    }

    /// <summary>选择天赋路线：该线两个 buff 的层数合并到所选 buff，另一个锁定不进奖励池。
    /// line/buff 非法或该线没有任何层数时返回 false。</summary>
    public bool ChooseRoute(StringName line, StringName buffId)
    {
        if (!GameState.Instance.ROUTE_LINES.ContainsKey(line))
        {
            return false;
        }

        var options = GameState.Instance.ROUTE_LINES[line].AsGodotArray();
        if (!options.Contains(buffId))
        {
            return false;
        }

        var other = options[1].AsStringName() == buffId ? options[0].AsStringName() : options[1].AsStringName();
        var total = GameState.Instance.BuffCount(buffId) + GameState.Instance.BuffCount(other);
        if (total <= 0)
        {
            return false;
        }

        GameState.Instance.Buffs[buffId] = total;
        GameState.Instance.Buffs.Remove(other);
        ChosenRoutes[line] = buffId;
        LockedRoutes[line] = other;
        RouteChosen?.Invoke(line, buffId);
        // 2026-08-11 拆域后同 MetaService.ApplyMetaLoadout 口径：Buffs 直写后广播 buffs_changed
        // （Player.RefreshBuffFactors/Hud.RebuildBuffDock 为缓存+信号驱动）
        GameState.Instance.EmitSignal(GameState.SignalName.BuffsChanged);
        return true;
    }

    /// <summary>奖励池抽取时排除锁定 buff</summary>
    public bool IsBuffLocked(StringName buffId)
    {
        foreach (var v in LockedRoutes.Values)
        {
            if (v.AsStringName() == buffId)
            {
                return true;
            }
        }

        return false;
    }
}
