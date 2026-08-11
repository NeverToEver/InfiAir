using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：常量表与静态构造辅助。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 常量（private const + 同名 UPPER_SNAKE 实例属性 + 静态 GetXxx() 访问器） ----------------

    /// <summary>得分总量上限（P4 防御：手改 difficulty score 倍率防 int64 溢出；正常对局远达不到）</summary>
    private const int ScoreCapValue = 1_000_000_000;

    /// <summary>难度档位表（开始面板选择，profile 持久化；对齐原作 settings.py DIFFICULTY_SETTINGS）
    /// hp/speed/spawn 为敌机数值与刷怪间隔倍率；score 为分数倍率（add_score 统一乘算）；
    /// spread_cap 为 spread 弹种敌机同屏上限；milestone 为里程碑阈值倍率
    /// （原作阈值与分数同倍 ×1/×2/×3，此处按设计取 ×1/×1/×1.5，避免高难 Buff 节奏过稀）；
    /// regen_delay/regen_rate 为被动回血（对齐原作 settings.py HEALTH_REGEN）：
    /// 距上次受伤 regen_delay 秒起每秒回 regen_rate HP（原作延迟不重置为疑似 bug，本版受伤即重置）。</summary>
    public Godot.Collections.Dictionary DIFFICULTY_DEFS { get; set; } = BuildDifficultyDefs();

    /// <summary>难度档位顺序（开始面板选择顺序）。</summary>
    public Godot.Collections.Array<StringName> DIFFICULTY_ORDER { get; } = new()
    {
        new StringName("easy"),
        new StringName("medium"),
        new StringName("hard"),
    };

    private const double MilestoneCycleMultValue = 1.35;

    // ---------------- 静态构造辅助（集合常量；局部构造，规则 19 不静态持有） ----------------

    private static Godot.Collections.Dictionary BuildDifficultyDefs() => new()
    {
        [new StringName("easy")] = new Godot.Collections.Dictionary
        {
            ["hp"] = 0.75,
            ["speed"] = 0.85,
            ["spawn"] = 1.25,
            ["score"] = 1,
            ["spread_cap"] = 1,
            ["milestone"] = 1.0,
            ["regen_delay"] = 3.0,
            ["regen_rate"] = 4.0,
        },
        [new StringName("medium")] = new Godot.Collections.Dictionary
        {
            ["hp"] = 1.0,
            ["speed"] = 1.0,
            ["spawn"] = 1.0,
            ["score"] = 2,
            ["spread_cap"] = 2,
            ["milestone"] = 1.0,
            ["regen_delay"] = 4.0,
            ["regen_rate"] = 2.0,
        },
        [new StringName("hard")] = new Godot.Collections.Dictionary
        {
            ["hp"] = 1.5,
            ["speed"] = 1.2,
            ["spawn"] = 0.8,
            ["score"] = 3,
            ["spread_cap"] = 3,
            ["milestone"] = 1.5,
            ["regen_delay"] = 5.0,
            ["regen_rate"] = 0.67,
        },
    };

    /// <summary>A7 白盒（第三轮 SOLID 重构，测试桥）：暴露内建难度默认表的原始构造结果。
    /// DIFFICULTY_DEFS 实例属性会被 ApplyBalance（GameState.State.cs）在 json difficulty 节
    /// 校验通过时整表替换为 balance.json 值，无法代表 C# 内建默认表；DifficultyTest 全表
    /// 一致性断言须直取私有构造器比对。不改任何逻辑，仅测试调用（非热路径）。</summary>
    public static Godot.Collections.Dictionary BuildDifficultyDefsPublic() => BuildDifficultyDefs();

    private static Godot.Collections.Array<Godot.Collections.Dictionary> BuildMissionDefs() => new()
    {
        new() { ["id"] = new StringName("kill_5"), ["goal"] = 5, ["kind"] = new StringName("kill") },
        new() { ["id"] = new StringName("survive_180"), ["goal"] = 180, ["kind"] = new StringName("survive") },
        new() { ["id"] = new StringName("boss_1"), ["goal"] = 1, ["kind"] = new StringName("boss") },
    };

    private static Godot.Collections.Array<Godot.Collections.Dictionary> BuildMissionPool() => new()
    {
        new() { ["id"] = new StringName("kill_5"), ["goal"] = 5, ["kind"] = new StringName("kill") },
        new() { ["id"] = new StringName("kill_15"), ["goal"] = 15, ["kind"] = new StringName("kill") },
        new() { ["id"] = new StringName("kill_30"), ["goal"] = 30, ["kind"] = new StringName("kill") },
        new() { ["id"] = new StringName("survive_60"), ["goal"] = 60, ["kind"] = new StringName("survive") },
        new() { ["id"] = new StringName("survive_180"), ["goal"] = 180, ["kind"] = new StringName("survive") },
        new() { ["id"] = new StringName("survive_300"), ["goal"] = 300, ["kind"] = new StringName("survive") },
        new() { ["id"] = new StringName("boss_1"), ["goal"] = 1, ["kind"] = new StringName("boss") },
        new() { ["id"] = new StringName("boss_2"), ["goal"] = 2, ["kind"] = new StringName("boss") },
        new() { ["id"] = new StringName("boss_3"), ["goal"] = 3, ["kind"] = new StringName("boss") },
    };

    private static Godot.Collections.Dictionary BuildRouteLines() => new()
    {
        [new StringName("offense")] = new Godot.Collections.Array { new StringName("spread_shot"), new StringName("laser_beam") },
        [new StringName("mobility")] = new Godot.Collections.Array { new StringName("phase_dash"), new StringName("mothership_recall") },
    };

    /// <summary>里程碑阈值曲线（对齐原作 constants.py GameBalanceConstants 算法）：
    /// 首循环 8 档基础阈值，之后每循环的档差按 ×1.35^cycle 放大（阈值单调不回退）。</summary>
    private static Godot.Collections.Array<int> BuildMilestoneBase() => new()
    {
        3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000,
    };
}
