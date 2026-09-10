using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：RP 经济 / 基地任务 / 天赋路线，职责在 MissionsService
/// （csharp/godot/MissionsService.cs，组合持有），本文件为门面转发——公开 API 签名/语义不变；
/// RpChanged/MissionCompleted/RefreshPointsChanged/RouteChosen 信号由 MissionsService 的 C# 事件
/// 经 GameState 订阅重发（消费方不变；ResetRun 直接赋值路径在 GameState 侧直发同名信号）。
/// </summary>
public partial class GameState : Node
{

    // ---------------- RP 经济 / 基地任务 / 天赋路线（门面转发 → MissionsService） ----------------

    /// <summary>征用点数（基地经济）——MissionsService 转发。</summary>
    public int Rp { get => _missions.Rp; set => _missions.Rp = value; }

    /// <summary>任务 id -> {"progress": int, "claimed": bool, "goal": int, "baseline": int}
    /// （progress 为相对口径：本局绝对计数 − 任务入场基线快照）——MissionsService 转发。</summary>
    public Godot.Collections.Dictionary Missions { get => _missions.Missions; set => _missions.Missions = value; }

    /// <summary>刷新点数（RefreshPoints）经济：进基地每次 +GRANT_PER_VISIT，刷新任务消耗 REFRESH_COST
    /// （balance.json base_task 段覆盖；默认 1 点/次进基地、2 点/次刷新 = 攒两次基地换一次刷新）——
    /// MissionsService 转发。</summary>
    public int RefreshPoints { get => _missions.RefreshPoints; set => _missions.RefreshPoints = value; }

    public void AddRp(int amount) => _missions.AddRp(amount);

    /// <summary>余额不足返回 false 且不扣减</summary>
    public bool SpendRp(int amount) => _missions.SpendRp(amount);

    /// <summary>初始手牌/任务池/kind 索引重建（_Ready/ResetRun/ResetMissions 调用）</summary>
    private void InitMissions() => _missions.InitMissions();

    /// <summary>公开任务重置口（仅清任务进度，不清 rp/buffs——比 ResetRun 副作用小，
    /// 供需要在保留其余本局状态的前提下重置 missions 的调用方）</summary>
    public void ResetMissions() => _missions.ResetMissions();

    /// <summary>按 kind 推进全部该类型在场任务的进度（任务轮换后 id 变化，进度源按 kind 分发；
    /// 已不在场的 id 由 _set_mission_progress 的 missions.has 守卫自动跳过）</summary>
    private void SetKindProgress(StringName kind, int value) => _missions.SetKindProgress(kind, value);

    /// <summary>在场任务 id 列表（base_console 任务面板据此渲染；任务轮换后不再等于 MISSION_DEFS）</summary>
    public Godot.Collections.Array<StringName> ActiveMissionIds() => _missions.ActiveMissionIds();

    public int MissionGoal(StringName id) => _missions.MissionGoal(id);

    /// <summary>任务定义查询（MISSION_POOL 无命中返回 {}，供 goal 查询等调用方共用）</summary>
    private Godot.Collections.Dictionary MissionDef(StringName id) => _missions.MissionDef(id);

    // TryGetValue 免空容器默认值每次分配（GetValueOrDefault 实参先求值会分配空 Dictionary）
    public int MissionProgress(StringName id) => _missions.MissionProgress(id);

    public bool IsMissionDone(StringName id) => _missions.IsMissionDone(id);

    public bool IsMissionClaimed(StringName id) => _missions.IsMissionClaimed(id);

    /// <summary>领取已完成任务的 +3RP，每任务每局限领一次</summary>
    public bool ClaimMission(StringName id) => _missions.ClaimMission(id);

    /// <summary>进基地发放刷新点数（amount &lt; 0 用 GRANT_PER_VISIT 档位值；base_console.show_base 调用）</summary>
    public void GrantRefreshPoints(int amount = -1) => _missions.GrantRefreshPoints(amount);

    /// <summary>刷新资格校验（点数不足禁止刷新；UI 据此禁用按钮并提示）</summary>
    public bool CanRefreshMissions() => _missions.CanRefreshMissions();

    /// <summary>刷新任务：消耗 RefreshPoints 重抽任务（槽位数 MISSION_SLOTS）。
    /// 已完成未领取的任务保留（防止刷新吞掉待领奖励），其余槽位从任务池无放回重抽
    /// （排除在场 id，避免与在场任务重号）。余额不足返回 false 且不扣减。</summary>
    public bool RefreshMissions() => _missions.RefreshMissions();
}
