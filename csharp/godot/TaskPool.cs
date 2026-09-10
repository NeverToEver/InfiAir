using Godot;
using InfiAir.Core.Missions;

namespace InfiAir;

/// <summary>
/// 基地任务池（TaskPool，任务轮换核心）：无放回随机抽取任务定义。
/// 抽取算法核心在 InfiAir.Core.Missions.TaskPool（csharp/core/Missions/TaskPool.cs），
/// 本壳直接组合 core TaskPool + id→原始定义字典映射，行为语义不变：
/// 单次 Draw 内不重复；一次 Draw 消耗完当前批次后若仍有名额且全池还有可用候选则重洗
/// 继续补足（跨 Draw 尽量延迟复用，排除在场任务导致的提前耗尽不截断）；
/// 排除覆盖全池时安全返回空。RNG 为独立随机源（性质等价、序列不等价——无外部依赖具体序列）。
/// </summary>
public partial class TaskPool : RefCounted
{
    // InfiAir.TaskPool（本类）遮蔽 using 引入的 InfiAir.Core.Missions.TaskPool
    // （当前命名空间成员优先于 using），此处全限定。
    private InfiAir.Core.Missions.TaskPool? _pool;
    private readonly Dictionary<string, Godot.Collections.Dictionary> _byId = new();

    /// <summary>无参构造（空池；调用方经 defs 属性注入任务定义）。</summary>
    public TaskPool()
        : this(new Godot.Collections.Array<Godot.Collections.Dictionary>())
    {
    }

    public TaskPool(Godot.Collections.Array<Godot.Collections.Dictionary> defs)
    {
        // Array<T> 不继承 untyped Array——经 Variant 桥转（同一原生数组，零拷贝）
        SetDefs(Variant.From(defs).AsGodotArray());
    }

    /// <summary>任务定义注入（setter 供先无参构造后注入的调用方；C# 带参构造等价）。</summary>
    public Godot.Collections.Array defs
    {
        get => new Godot.Collections.Array(); // 定义已注入 core TaskPool，无回读需求
        set => SetDefs(Variant.From(value).AsGodotArray());
    }

    /// <summary>抽取 count 个任务定义（无放回：单次 draw 内不重复、耗尽跨批补足直到可用候选抽完）。
    /// exclude_ids：排除这些 id（刷新时排除全部在场任务，防重号/覆盖保留任务）。
    /// 返回实际抽到的定义（排除覆盖全池时安全返回空，不抛错不死循环）。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> Draw(int count, Godot.Collections.Array<StringName> excludeIds)
    {
        var outDefs = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (_pool is null)
        {
            return outDefs;
        }

        var exclude = new HashSet<string>();
        foreach (var id in excludeIds)
        {
            exclude.Add(id.ToString());
        }

        foreach (var def in _pool.Draw(count, exclude))
        {
            outDefs.Add(_byId[def.Id]);
        }

        return outDefs;
    }

    /// <summary>可选参数重载：excludeIds 缺省时为空数组。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> Draw(int count)
        => Draw(count, new Godot.Collections.Array<StringName>());

    /// <summary>装载任务定义池（条目须含 id/goal/kind；
    /// 条目级判型——非 Dictionary / 缺键 / 类型不符的条目跳过（当前数据源为
    /// 代码内建受信，防未来外部数据源接入；对齐本库「防御性收敛」口径）。</summary>
    private void SetDefs(Godot.Collections.Array defs)
    {
        _byId.Clear();
        var defList = new List<TaskDef>();
        for (int i = 0; i < defs.Count; i++)
        {
            var entry = defs[i];
            if (entry.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            var def = entry.AsGodotDictionary();
            var idV = def.GetValueOrDefault("id", new Variant());
            var goalV = def.GetValueOrDefault("goal", new Variant());
            var kindV = def.GetValueOrDefault("kind", new Variant());
            if (idV.VariantType is not (Variant.Type.String or Variant.Type.StringName)
                || goalV.VariantType is not (Variant.Type.Int or Variant.Type.Float)
                || kindV.VariantType is not (Variant.Type.String or Variant.Type.StringName))
            {
                continue;
            }

            var id = idV.AsStringName().ToString();
            defList.Add(new TaskDef(id));
            _byId[id] = def;
        }

        _pool = new InfiAir.Core.Missions.TaskPool(defList.ToArray());
    }

}
