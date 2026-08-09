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
    }

    /// <summary>配置字典是否为空（缺失/损坏 JSON 时为空，全部回退脚本默认值）。</summary>
    public bool IsEmpty() => _balance.Count == 0;

    /// <summary>统一配置访问：路径如 "player.fuel.drain"。缺键/类型不符回退 default。</summary>
    public Variant Cfg(string path, Variant defaultValue) => _interop.Resolve(path, defaultValue);

    /// <summary>敌方 HP 对局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))。</summary>
    public double EnemyHpRamp(double difficultyMultiplier) => 1.0 + _hpRampFactor * (difficultyMultiplier - 1.0);

    /// <summary>敌方伤害对局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))。</summary>
    public double EnemyDamageRamp(double difficultyMultiplier) => 1.0 + _damageRampFactor * (difficultyMultiplier - 1.0);
}
