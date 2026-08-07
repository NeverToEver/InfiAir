using Godot;
using InfiAir.Core.Missions;

namespace InfiAir;

/// <summary>
/// 2026-08-07 绑定壳：GDScript → C# 任务池桥（InfiAir.Core.Missions.TaskPool 纯逻辑）。
/// GDScript 侧（scripts/task_pool.gd）_init 时 SetDefs 一次、draw 转发 Draw——
/// 公开签名不变；返回原始任务 Dictionary 引用（id 身份映射，调用方读 def["id"]/["goal"]）。
/// 分布语义与旧 GDScript shuffle 等价；RNG 独立于 GDScript 全局随机源（序列不等价承诺，性质等价）。
/// </summary>
public partial class TaskPoolInterop : RefCounted
{
    private TaskPool? _pool;
    private readonly Dictionary<string, Godot.Collections.Dictionary> _byId = new();

    /// <summary>装载任务定义池（Array[Dictionary]，条目须含 id/goal/kind）。</summary>
    public void SetDefs(Godot.Collections.Array defs)
    {
        _byId.Clear();
        var coreDefs = new TaskDef[defs.Count];
        for (int i = 0; i < defs.Count; i++)
        {
            var def = defs[i].AsGodotDictionary();
            var id = def["id"].AsStringName().ToString();
            coreDefs[i] = new TaskDef(id, (int)def["goal"].AsInt64(), def["kind"].AsStringName().ToString());
            _byId[id] = def;
        }
        _pool = new TaskPool(coreDefs);
    }

    /// <summary>抽取 count 个任务定义（无放回）；excludeIds 为在场 id（StringName 数组）。
    /// 返回抽取到的原始定义字典（按抽取顺序；排除覆盖全池时为空）。</summary>
    public Godot.Collections.Array Draw(int count, Godot.Collections.Array excludeIds)
    {
        var result = new Godot.Collections.Array();
        if (_pool is null)
        {
            return result;
        }
        var exclude = new HashSet<string>();
        foreach (var v in excludeIds)
        {
            if (v.VariantType is Variant.Type.String or Variant.Type.StringName)
            {
                exclude.Add(v.ToString()!);
            }
        }
        foreach (var def in _pool.Draw(count, exclude))
        {
            result.Add(_byId[def.Id]);
        }
        return result;
    }
}
