using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：难度档位 / 里程碑阈值曲线。
/// 第五轮拆域（2026-08-11）：全部职责迁至 RunProgressionService（csharp/godot/RunProgressionService.cs，
/// 组合持有；ScoreService 迁出计分域语义的 SetMilestoneOverride/MilestoneCount/SetMilestoneCount/
/// NextMilestone，见下方门面转发），本文件为门面对齐转发——公开 API 签名/语义不变（测试与对局
/// 消费方经此处零适配调用）；DifficultyChanged/DifficultySelected 信号由 RunProgressionService 的
/// C# 事件经 GameState 订阅重发（AddBossKill/ApplyRunSave 直发路径在 GameState 侧直发同名信号，不重复）。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 难度档位（门面转发 → RunProgressionService） ----------------

    /// <summary>切换难度档位（非法档位忽略），持久化到 profile 并广播</summary>
    public void SetDifficulty(StringName pDifficulty) => _runProg.SetDifficulty(pDifficulty);

    public string DifficultyLabel() => _runProg.DifficultyLabel();

    /// <summary>B 梯队：受击触发 DDA 降档（重入安全——幂等置位，重复受击刷新计时）；
    /// 2026-08-11：同源断连（受击 = 降档 + 断连双通道，均不致命）。</summary>
    private void OnPlayerDamagedDda(float amount, Vector2 fromPos) => _runProg.OnPlayerDamagedDda(amount, fromPos);

    public int ScoreMultiplier() => _runProg.ScoreMultiplier();

    /// <summary>B 梯队（fair plan §8）：DDA 降档中（玩家受击后 DDA_DURATION 内）——消费方
    /// （enemy 开火计时 / spawner 波次间隔 / boss 攻击间隔）乘 dda_factor() 拉长间隔</summary>
    public bool DdaActive() => _runProg.DdaActive();

    /// <summary>DDA 降档乘区：active 时返回配置因子（>1 拉长间隔），否则 1.0（热路径零分支常态）</summary>
    public double DdaFactor() => _runProg.DdaFactor();

    /// <summary>测试/诊断：立即结束降档（对齐「测试经公开接口」白盒契约）</summary>
    public void ResetDda() => _runProg.ResetDda();

    public double EnemyHpMultiplier() => _runProg.EnemyHpMultiplier();

    public double EnemySpeedMultiplier() => _runProg.EnemySpeedMultiplier();

    /// <summary>敌方 HP 对局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))，随 Boss 击杀线性成长。
    /// 纯查询委托 BalanceService（难度乘数作参数）。</summary>
    public float EnemyHpRamp() => _runProg.EnemyHpRamp();

    /// <summary>敌方 HP ramp（显式难度乘数版本，2026-08-09 审计补充）：调用方以自身难度快照计算——
    /// Enemy.Setup 的 pDifficulty 参数是显式入参（分裂子机/测试可传非全局 DifficultyMultiplier 值），
    /// 语义同原直查 Cfg 全链路，但走 Load 时缓存的 ramp 因子（免每敌机 path.Split + Variant 装箱）。</summary>
    public float EnemyHpRamp(double difficultyMultiplier) => _runProg.EnemyHpRamp(difficultyMultiplier);

    /// <summary>敌方伤害对局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))，
    /// 统一作用于全部敌方伤害源（敌弹/Boss 弹/撞体/编队炸弹；2026-07-29 无限段修订）。
    /// 纯查询委托 BalanceService（难度乘数作参数）。</summary>
    public float EnemyDamageRamp() => _runProg.EnemyDamageRamp();

    public double SpawnIntervalMultiplier() => _runProg.SpawnIntervalMultiplier();

    /// <summary>spread 弹种敌机同屏上限（easy 1 / medium 2 / hard 3）。
    /// AB15：钳 [0, int.MaxValue]（同文件 ScoreMultiplier 已钳，孪生遗漏）——手改 &gt;2^31
    /// 经裸 (int) 回绕负 → spread 敌机同屏上限恒负、整类玩法消失。</summary>
    public int SpreadEnemyCap() => _runProg.SpreadEnemyCap();

    /// <summary>被动回血：距上次受伤 regen_delay 秒起每秒回 regen_rate HP（对齐原作 HEALTH_REGEN）
    /// P0-2：档位值在难度变更/重新加载时缓存，热路径免双层字典查找</summary>
    public double PassiveRegenDelay() => _runProg.PassiveRegenDelay();

    public double PassiveRegenRate() => _runProg.PassiveRegenRate();

    /// <summary>回血链/倍率缓存刷新（SetDifficulty/ApplyBalance/ApplySettingsDict 调用；
    /// 本体在 RunProgressionService，此处私有一行包装）。</summary>
    private void RefreshRegenCache() => _runProg.RefreshRegenCache();

    // ---------------- BalanceService Load 缓存转发（2026-08-10 perf 批次；原每 spawn Cfg 全链路） ----------------

    /// <summary>敌方速度 ramp（显式难度乘数版本）：Enemy.Setup 以自身难度快照计算（EnemyHpRamp 同款模式）。</summary>
    public float EnemySpeedRamp(double difficultyMultiplier) => _runProg.EnemySpeedRamp(difficultyMultiplier);

    /// <summary>敌机移动策略参数表（Load 缓存引用，只读消费；Enemy.MakeStrategy 每 spawn 读取）。</summary>
    public Godot.Collections.Dictionary MoveStrategies() => _runProg.MoveStrategies();

    /// <summary>辅助瞄准「强辅助」标记概率（Load 缓存；Enemy.Setup 每 spawn 读取）。</summary>
    public double AimMarkRatio() => _runProg.AimMarkRatio();

    /// <summary>敌机入场预告时长（Load 缓存，判型/钳制已完成；Spawner.QueueEnemy 每 spawn 读取）。</summary>
    public float SpawnerTelegraphDuration() => _runProg.SpawnerTelegraphDuration();

    // ---------------- 里程碑阈值曲线 ----------------

    /// <summary>第 index 次（0 起）里程碑的分数阈值：8 档基础阈值循环，档差按 ×1.35^cycle 增长，
    /// 再乘难度阈值倍率（easy ×1 / medium ×1 / hard ×1.5）。
    /// 2026-08-07：算法核心迁移 InfiAir.Core.Progression.MilestoneCurve（C# 纯函数，xUnit 直测；
    /// 逐位等价：pow 钳制、roundf half-away-from-zero、累加顺序一致）——RunProgressionService 转发。</summary>
    public int MilestoneThreshold(int index) => _runProg.MilestoneThreshold(index);

    /// <summary>难度档阈值倍率（DIFFICULTY_DEFS 经 _valid_difficulty_defs 校验，milestone 恒为正数；
    /// ScoreService.RestoreMilestones 经此 internal 包装跨域调用——原私有，第五轮拆域起 internal）。</summary>
    internal double MilestoneMult() => _runProg.MilestoneMult();

    /// <summary>测试钩子（A7 遗留清理，公开化）：直接设定下一个里程碑阈值（不动曲线计数，保证测试确定性）
    /// ——ScoreService 转发（计分域语义，2026-08-11 归 ScoreService）。</summary>
    public void SetMilestoneOverride(int threshold) => _score.SetMilestoneOverride(threshold);

    /// <summary>A7：测试/诊断白盒断言经公开接口
    /// 当前已触发的里程碑数（2026-08-04 母舰升级档位等消费点）——ScoreService 转发。</summary>
    public int MilestoneCount() => _score.MilestoneCount();

    /// <summary>A7：测试/诊断白盒 setter（2026-08-06 审计：mothership_upgrade_test 曾直写
    /// _milestone_count ×5，补语义化公开接口；负值钳 0）——ScoreService 转发。</summary>
    public void SetMilestoneCount(int count) => _score.SetMilestoneCount(count);

    public int NextMilestone() => _score.NextMilestone();

    /// <summary>难度乘数对局进程曲线重算（公开口；曲线公式/迭代语义见
    /// RunProgressionService.RecomputeDifficultyInternal——2026-08-11 迁入）。</summary>
    public void RecomputeDifficulty() => _runProg.RecomputeDifficulty();

    /// <summary>难度乘数对局进程曲线（2026-07-29 无限段修订，D1=必死曲线，docs/archive/ENDLESS_BALANCE_PLAN.md）：
    /// 1 + per_boss_kill×Boss击杀 + 时间轴累进（每 time_step_seconds 量化一档，每 10 分钟 +per_ten_minutes）。
    /// 返回乘数是否变化；变化时由调用方广播 difficulty_changed（apply_run_save 统一在末尾广播）——
    /// 私有一行包装（本体在 RunProgressionService）。</summary>
    private bool RecomputeDifficultyInternal() => _runProg.RecomputeDifficultyInternal();
}
