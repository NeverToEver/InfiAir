using Godot;
using InfiAir.Core.Config;
using InfiAir.Core.Progression;

namespace InfiAir;

/// <summary>
/// 数值配置中心。
/// 持有 balance.json 解析字典，提供 cfg 路径查询与纯数值查询；由 GameState 组合并委托。
/// 语义保持：缺失/损坏 JSON 回退脚本默认值；点路径解析直调
/// InfiAir.Core.Config.PathResolver（数值宽容/容器拷贝/typeof 相等），Variant↔CLR
/// 转换经 VariantBridge；
/// ramp 因子 load() 缓存一次——热路径（每发敌弹创建）免 path.split/字典遍历。
/// </summary>
public partial class BalanceService : RefCounted
{
    private Godot.Collections.Dictionary _balance = new();
    private Dictionary<string, object?> _tree = new();

    /// <summary>难度映射配置（load() 时缓存一次，热路径免 JSON 查询）：斜率与上限的单一事实源在
    /// csharp/core/Progression/DifficultyScaling.cs。64 位 double 运算
    /// （对齐文件头"纯标量 double 逐位等价"纪律）。</summary>
    private DifficultyScalingConfig _scaling = new();

    /// <summary>每 spawn 热路径配置 load() 时缓存（与 ramp 因子同款；
    /// ReloadBalance → Load 重缓存自然失效）。move_strategies 缓存解析后字典引用（只读消费）；
    /// telegraph_duration 的判型/下限钳制随缓存内聚（免 Spawner.QueueEnemy 每 spawn 判型）。</summary>
    private Godot.Collections.Dictionary _moveStrategies = new();
    private double _aimMarkRatio = 0.25;
    private float _telegraphDuration = 0.6f;

    public void Load(string path)
    {
        _balance = new Godot.Collections.Dictionary();
        if (Godot.FileAccess.FileExists(path))
        {
            var parsed = Godot.Json.ParseString(Godot.FileAccess.GetFileAsString(path));
            if (parsed.VariantType == Variant.Type.Dictionary)
            {
                _balance = parsed.AsGodotDictionary();
            }
        }

        // 配置树转 CLR（损坏/缺失时为空字典，全部回退默认）
        _tree = VariantBridge.TryToClr(_balance, out var clr, out _)
                && clr is Dictionary<string, object?> d
            ? d
            : new Dictionary<string, object?>();
        // 缓存难度映射配置（缺键回退脚本默认，与 cfg 语义一致）。
        // 上限类判型：≤0 视为「不设限/关闭」由 DifficultyScaling 内部兜底（速度上限除外——见 core 注释）
        _scaling = new DifficultyScalingConfig
        {
            HpRampFactor = Cfg("enemies.hp_ramp_factor", _scaling.HpRampFactor).AsDouble(),
            DamageRampFactor = Cfg("enemies.damage_ramp_factor", _scaling.DamageRampFactor).AsDouble(),
            SpeedRampFactor = Cfg("enemies.speed_ramp_factor", _scaling.SpeedRampFactor).AsDouble(),
            SpeedRampCap = Cfg("enemies.speed_ramp_cap", _scaling.SpeedRampCap).AsDouble(),
            BossHpRampFactor = Cfg("boss.hp_ramp_factor", _scaling.BossHpRampFactor).AsDouble(),
            SpawnDifficultyFactor = Cfg("spawner.difficulty_factor", _scaling.SpawnDifficultyFactor).AsDouble(),
            WaveIntervalFloor = Cfg("spawner.interval_min", _scaling.WaveIntervalFloor).AsDouble(),
            FireIntervalFloor = Cfg("enemies.fire_interval_floor", _scaling.FireIntervalFloor).AsDouble(),
            ElitePerDifficulty = Cfg("spawner.elite_per_difficulty", _scaling.ElitePerDifficulty).AsDouble(),
            EliteCountCap = Mathf.Max((int)Cfg("spawner.elite_count_cap", _scaling.EliteCountCap).AsInt64(), 1),
        };
        // 每 spawn 热路径键同款缓存（默认值与调用点回退一致）
        var ms = Cfg("enemies.move_strategies", new Godot.Collections.Dictionary());
        _moveStrategies = ms.VariantType == Variant.Type.Dictionary ? ms.AsGodotDictionary() : new Godot.Collections.Dictionary();
        _aimMarkRatio = Cfg("player.aim_assist.mark_ratio", 0.25).AsDouble();
        // 判型 + 下限钳制（0/负值使预告线立即超时或 Timer 反向；坏值回退默认）
        var td = Cfg("spawner.telegraph_duration", SpawnTelegraph.GetDefaultDuration());
        _telegraphDuration = Mathf.Max(
            td.VariantType == Variant.Type.Float || td.VariantType == Variant.Type.Int
                ? (float)td.AsDouble()
                : SpawnTelegraph.GetDefaultDuration(),
            0.01f);
    }

    /// <summary>配置字典是否为空（缺失/损坏 JSON 时为空，全部回退脚本默认值）。</summary>
    public bool IsEmpty() => _balance.Count == 0;

    /// <summary>统一配置访问：路径如 "player.fuel.drain"。缺键/类型不符回退 default
    /// （语义见 PathResolver：数值宽容；容器拷贝；不可转换的默认值原样返回）。</summary>
    public Variant Cfg(string path, Variant defaultValue)
    {
        var kind = VariantBridge.KindOf(defaultValue);
        if (!VariantBridge.TryToClr(defaultValue, out var clr, out _))
        {
            return defaultValue;
        }

        return VariantBridge.ToVariant(PathResolver.Resolve(_tree, path, clr, kind));
    }

    /// <summary>敌方 HP 本局进程 ramp（斜率与域钳在 core DifficultyScaling）。</summary>
    public double EnemyHpRamp(double difficultyMultiplier) =>
        DifficultyScaling.EnemyHpRamp(difficultyMultiplier, _scaling);

    /// <summary>敌方伤害本局进程 ramp。</summary>
    public double EnemyDamageRamp(double difficultyMultiplier) =>
        DifficultyScaling.EnemyDamageRamp(difficultyMultiplier, _scaling);

    /// <summary>敌方速度 ramp（带硬上限；load 缓存，免每 spawn Cfg 全链路）。</summary>
    public double EnemySpeedRamp(double difficultyMultiplier) =>
        DifficultyScaling.EnemySpeedRamp(difficultyMultiplier, _scaling);

    /// <summary>Boss HP ramp（斜率独立于杂兵，见 core）。</summary>
    public double BossHpRamp(double difficultyMultiplier) =>
        DifficultyScaling.BossHpRamp(difficultyMultiplier, _scaling);

    /// <summary>难度映射配置快照（只读；Spawner/Enemy 侧区间/数量/地板查询用）。</summary>
    public DifficultyScalingConfig Scaling() => _scaling;

    /// <summary>敌机移动策略参数表（load 缓存引用，只读消费；免每 spawn Cfg 深拷贝）。</summary>
    public Godot.Collections.Dictionary MoveStrategies() => _moveStrategies;

    /// <summary>辅助瞄准「强辅助」标记概率（load 缓存；免每 spawn Cfg 全链路）。</summary>
    public double AimMarkRatio() => _aimMarkRatio;

    /// <summary>敌机入场预告时长（load 缓存，判型/钳制已完成；免每 spawn Cfg 判型）。</summary>
    public float SpawnerTelegraphDuration() => _telegraphDuration;
}
