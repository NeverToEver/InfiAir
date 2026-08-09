using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：RP 经济 / 基地任务 / 天赋路线。
/// </summary>
public partial class GameState : Node
{

    // ---------------- RP 经济 / 基地任务 / 天赋路线 ----------------

    public void AddRp(int amount)
    {
        Rp += amount;
        EmitSignal(SignalName.RpChanged, Rp);
    }

    /// <summary>余额不足返回 false 且不扣减</summary>
    public bool SpendRp(int amount)
    {
        if (Rp < amount)
        {
            return false;
        }

        Rp -= amount;
        EmitSignal(SignalName.RpChanged, Rp);
        return true;
    }

    private void InitMissions()
    {
        Missions.Clear();
        foreach (var def in MISSION_DEFS)
        {
            // P0-3：goal 一次性缓存进条目，_set_mission_progress 免每帧线性扫 MISSION_POOL
            Missions[def["id"]] = new Godot.Collections.Dictionary { ["progress"] = 0, ["claimed"] = false, ["goal"] = (int)def["goal"].AsInt64() };
        }

        // 任务轮换：每局从全新洗牌序列开始（初始手牌固定 MISSION_DEFS，刷新才随机）
        _taskPool = new TaskPool(MISSION_POOL); // M7：TaskPool 迁 C#，typed
        RebuildKindIndex();
    }

    /// <summary>kind -> 池内 id 索引重建（MISSION_POOL 为 const，仅 _init_missions 调用一次）</summary>
    private void RebuildKindIndex()
    {
        _missionsByKind.Clear();
        foreach (var def in MISSION_POOL)
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
            EmitSignal(SignalName.MissionCompleted, id);
        }
    }

    /// <summary>按 kind 推进全部该类型在场任务的进度（任务轮换后 id 变化，进度源按 kind 分发；
    /// 已不在场的 id 由 _set_mission_progress 的 missions.has 守卫自动跳过）</summary>
    private void SetKindProgress(StringName kind, int value)
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
    private Godot.Collections.Dictionary MissionDef(StringName id)
    {
        foreach (var def in MISSION_POOL)
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
        RefreshPoints += amount < 0 ? GRANT_PER_VISIT : amount;
        EmitSignal(SignalName.RefreshPointsChanged, RefreshPoints);
    }

    /// <summary>刷新资格校验（点数不足禁止刷新；UI 据此禁用按钮并提示）</summary>
    public bool CanRefreshMissions() => RefreshPoints >= REFRESH_COST;

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

        RefreshPoints -= REFRESH_COST;
        EmitSignal(SignalName.RefreshPointsChanged, RefreshPoints);
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

        var drawn = _taskPool!.Draw(MissionSlotsValue - kept.Count, exclude);
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
        if (!ROUTE_LINES.ContainsKey(line))
        {
            return false;
        }

        var options = ROUTE_LINES[line].AsGodotArray();
        if (!options.Contains(buffId))
        {
            return false;
        }

        var other = options[1].AsStringName() == buffId ? options[0].AsStringName() : options[1].AsStringName();
        var total = BuffCount(buffId) + BuffCount(other);
        if (total <= 0)
        {
            return false;
        }

        Buffs[buffId] = total;
        Buffs.Remove(other);
        ChosenRoutes[line] = buffId;
        LockedRoutes[line] = other;
        EmitSignal(SignalName.RouteChosen, line, buffId);
        EmitSignal(SignalName.BuffsChanged);
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
