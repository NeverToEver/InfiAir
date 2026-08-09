using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：难度档位 / 里程碑阈值曲线。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 难度档位 ----------------

    /// <summary>切换难度档位（非法档位忽略），持久化到 profile 并广播</summary>
    public void SetDifficulty(StringName pDifficulty)
    {
        if (!DIFFICULTY_DEFS.ContainsKey(pDifficulty) || pDifficulty == Difficulty)
        {
            return;
        }

        Difficulty = pDifficulty;
        RefreshRegenCache();
        EmitSignal(SignalName.DifficultySelected, Difficulty);
        SaveProfile();
    }

    public string DifficultyLabel() => (string)Tr("DIFF_" + Difficulty.ToString().ToUpperInvariant());

    /// <summary>B 梯队：受击触发 DDA 降档（重入安全——幂等置位，重复受击刷新计时）</summary>
    private void OnPlayerDamagedDda(float amount, Vector2 fromPos)
    {
        _ddaTimer = DDA_DURATION;
    }

    public int ScoreMultiplier()
    {
        // 2026-08-03 审计回退：曾尝试缓存 _score_multiplier_cache，但 difficulty 是公开字段，
        // 测试/调用方直写不触发 _refresh_regen_cache（白盒契约），缓存会返回旧值；与同族
        // enemy_hp_multiplier/enemy_speed_multiplier/spawn_interval_multiplier 一致保持直接查表
        return (int)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["score"].AsInt64();
    }

    /// <summary>B 梯队（fair plan §8）：DDA 降档中（玩家受击后 DDA_DURATION 内）——消费方
    /// （enemy 开火计时 / spawner 波次间隔 / boss 攻击间隔）乘 dda_factor() 拉长间隔</summary>
    public bool DdaActive() => _ddaTimer > 0.0;

    /// <summary>DDA 降档乘区：active 时返回配置因子（>1 拉长间隔），否则 1.0（热路径零分支常态）</summary>
    public double DdaFactor() => _ddaTimer > 0.0 ? DDA_FACTOR : 1.0;

    /// <summary>测试/诊断：立即结束降档（对齐「测试经公开接口」白盒契约）</summary>
    public void ResetDda() => _ddaTimer = 0.0;

    public double EnemyHpMultiplier() => (double)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["hp"].AsDouble();

    public double EnemySpeedMultiplier() => (double)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["speed"].AsDouble();

    /// <summary>敌方 HP 对局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))，随 Boss 击杀线性成长。
    /// 纯查询委托 BalanceService（难度乘数作参数）。</summary>
    public float EnemyHpRamp() => (float)_balanceService.EnemyHpRamp(DifficultyMultiplier);

    /// <summary>敌方 HP ramp（显式难度乘数版本，2026-08-09 审计补充）：调用方以自身难度快照计算——
    /// Enemy.Setup 的 pDifficulty 参数是显式入参（分裂子机/测试可传非全局 DifficultyMultiplier 值），
    /// 语义同原直查 Cfg 全链路，但走 Load 时缓存的 ramp 因子（免每敌机 path.Split + Variant 装箱）。</summary>
    public float EnemyHpRamp(double difficultyMultiplier) => (float)_balanceService.EnemyHpRamp(difficultyMultiplier);

    /// <summary>敌方伤害对局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))，
    /// 统一作用于全部敌方伤害源（敌弹/Boss 弹/撞体/编队炸弹；2026-07-29 无限段修订）。
    /// 纯查询委托 BalanceService（难度乘数作参数）。</summary>
    public float EnemyDamageRamp() => (float)_balanceService.EnemyDamageRamp(DifficultyMultiplier);

    public double SpawnIntervalMultiplier() => (double)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["spawn"].AsDouble();

    /// <summary>spread 弹种敌机同屏上限（easy 1 / medium 2 / hard 3）</summary>
    public int SpreadEnemyCap() => (int)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["spread_cap"].AsInt64();

    /// <summary>被动回血：距上次受伤 regen_delay 秒起每秒回 regen_rate HP（对齐原作 HEALTH_REGEN）
    /// P0-2：档位值在难度变更/重新加载时缓存，热路径免双层字典查找</summary>
    public double PassiveRegenDelay() => _regenDelay;

    public double PassiveRegenRate() => _regenRate;

    private void RefreshRegenCache()
    {
        var def = DIFFICULTY_DEFS.GetValueOrDefault(Difficulty, new Variant());
        if (def.VariantType == Variant.Type.Dictionary)
        {
            _regenDelay = (double)def.AsGodotDictionary().GetValueOrDefault("regen_delay", _regenDelay).AsDouble();
            _regenRate = (double)def.AsGodotDictionary().GetValueOrDefault("regen_rate", _regenRate).AsDouble();
        }
    }

    // ---------------- 里程碑阈值曲线 ----------------

    /// <summary>第 index 次（0 起）里程碑的分数阈值：8 档基础阈值循环，档差按 ×1.35^cycle 增长，
    /// 再乘难度阈值倍率（easy ×1 / medium ×1 / hard ×1.5）。
    /// 2026-08-07：算法核心迁移 InfiAir.Core.Progression.MilestoneCurve（C# 纯函数，xUnit 直测；
    /// 逐位等价：pow 钳制、roundf half-away-from-zero、累加顺序一致）。</summary>
    public int MilestoneThreshold(int index) => (int)_progression.MilestoneThreshold(
        index, Variant.From(MilestoneBase).AsGodotArray(), MilestoneCycleMult, MilestoneMult());

    /// <summary>难度档阈值倍率（DIFFICULTY_DEFS 经 _valid_difficulty_defs 校验，milestone 恒为正数）</summary>
    private double MilestoneMult() => (double)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["milestone"].AsDouble();

    /// <summary>测试钩子（A7 遗留清理，公开化）：直接设定下一个里程碑阈值（不动曲线计数，保证测试确定性）</summary>
    public void SetMilestoneOverride(int threshold) => _nextMilestone = threshold;

    /// <summary>A7：测试/诊断白盒断言经公开接口
    /// 当前已触发的里程碑数（2026-08-04 母舰升级档位等消费点）</summary>
    public int MilestoneCount() => _milestoneCount;

    /// <summary>A7：测试/诊断白盒 setter（2026-08-06 审计：mothership_upgrade_test 曾直写
    /// _milestone_count ×5，补语义化公开接口；负值钳 0）</summary>
    public void SetMilestoneCount(int count) => _milestoneCount = Mathf.Max(count, 0);

    public int NextMilestone() => _nextMilestone;

    public void RecomputeDifficulty() => RecomputeDifficultyInternal();
}
