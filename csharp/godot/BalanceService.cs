using Godot;

namespace InfiAir;

/// <summary>
/// 数值配置中心（M2 全量迁移，2026-08-08 自 scripts/balance_service.gd 迁移）。
/// 持有 balance.json 解析字典，提供 cfg 路径查询与纯数值查询；由 GameState 组合并委托。
/// 语义保持（A2/P1-1 约定）：缺失/损坏 JSON 回退脚本默认值；点路径解析经
/// PathResolverInterop → InfiAir.Core.Config.PathResolver（数值宽容/容器拷贝/typeof 相等）；
/// ramp 因子 load() 缓存一次——热路径（每发敌弹创建）免 path.split/字典遍历。
/// </summary>
public partial class BalanceService : RefCounted
{
    private Godot.Collections.Dictionary _balance = new();
    private readonly PathResolverInterop _interop = new();

    /// <summary>G09：ramp 因子 load() 时缓存一次（热路径免 JSON 查询）。
    /// U15：64 位 double 运算（对齐原 GDScript 与文件头"纯标量 double 逐位等价"纪律）。</summary>
    private double _hpRampFactor = 0.25;
    private double _damageRampFactor = 0.20;

    /// <summary>2026-08-10 perf 批次：每 spawn 热路径配置 load() 时缓存（G09 hp_ramp 同款机制；
    /// ReloadBalance → Load 重缓存自然失效）。move_strategies 缓存解析后字典引用（只读消费）；
    /// telegraph_duration 的判型/下限钳制随缓存内聚（原 Spawner.QueueEnemy 每 spawn 判型）。</summary>
    private Godot.Collections.Dictionary _moveStrategies = new();
    private double _speedRampFactor = 0.1;
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

        // P1-1：配置树同步到 C# 解析壳（损坏/缺失时为空字典，全部回退默认）
        _interop.SetData(_balance);
        // G09：缓存 ramp 因子（缺键回退脚本默认，与 cfg 语义一致）
        _hpRampFactor = _interop.Resolve("enemies.hp_ramp_factor", 0.25).AsDouble();
        _damageRampFactor = _interop.Resolve("enemies.damage_ramp_factor", 0.20).AsDouble();
        // 2026-08-10 perf 批次：每 spawn 热路径键同款缓存（默认值与原调用点回退一致）
        var ms = _interop.Resolve("enemies.move_strategies", new Godot.Collections.Dictionary());
        _moveStrategies = ms.VariantType == Variant.Type.Dictionary ? ms.AsGodotDictionary() : new Godot.Collections.Dictionary();
        _speedRampFactor = _interop.Resolve("enemies.speed_ramp_factor", 0.1).AsDouble();
        _aimMarkRatio = _interop.Resolve("player.aim_assist.mark_ratio", 0.25).AsDouble();
        // R07：判型 + 下限钳制（0/负值使预告线立即超时或 Timer 反向；坏值回退默认）
        var td = _interop.Resolve("spawner.telegraph_duration", SpawnTelegraph.GetDefaultDuration());
        _telegraphDuration = Mathf.Max(
            td.VariantType == Variant.Type.Float || td.VariantType == Variant.Type.Int
                ? (float)td.AsDouble()
                : SpawnTelegraph.GetDefaultDuration(),
            0.01f);
    }

    /// <summary>配置字典是否为空（缺失/损坏 JSON 时为空，全部回退脚本默认值）。</summary>
    public bool IsEmpty() => _balance.Count == 0;

    /// <summary>统一配置访问：路径如 "player.fuel.drain"。缺键/类型不符回退 default。</summary>
    public Variant Cfg(string path, Variant defaultValue) => _interop.Resolve(path, defaultValue);

    /// <summary>敌方 HP 对局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))。</summary>
    public double EnemyHpRamp(double difficultyMultiplier) => 1.0 + _hpRampFactor * (difficultyMultiplier - 1.0);

    /// <summary>敌方伤害对局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))。</summary>
    public double EnemyDamageRamp(double difficultyMultiplier) => 1.0 + _damageRampFactor * (difficultyMultiplier - 1.0);

    /// <summary>敌方速度对局进程 ramp：×(1 + speed_ramp_factor × (难度乘数 − 1))（load 缓存；原 Enemy.Setup 每 spawn Cfg 全链路）。</summary>
    public double EnemySpeedRamp(double difficultyMultiplier) => 1.0 + _speedRampFactor * (difficultyMultiplier - 1.0);

    /// <summary>敌机移动策略参数表（load 缓存引用，只读消费；原 Enemy.MakeStrategy 每 spawn Cfg 深拷贝）。</summary>
    public Godot.Collections.Dictionary MoveStrategies() => _moveStrategies;

    /// <summary>辅助瞄准「强辅助」标记概率（load 缓存；原 Enemy.Setup 每 spawn Cfg 全链路）。</summary>
    public double AimMarkRatio() => _aimMarkRatio;

    /// <summary>敌机入场预告时长（load 缓存，判型/钳制已完成；原 Spawner.QueueEnemy 每 spawn Cfg 判型）。</summary>
    public float SpawnerTelegraphDuration() => _telegraphDuration;
}
