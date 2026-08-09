using Godot;

namespace InfiAir;

/// <summary>
/// A2 阶段 2：对局存档 / 局外档案的文件 IO（docs/AUDIT_VAULT.md A2）。
/// P0-1（2026-08-07）：原子写 / 损坏隔离 / JSON 序列化迁移 InfiAir.Core.Storage.SaveStore
/// （C#，见 csharp/core/Storage/SaveStore.cs + csharp/godot/SaveStoreInterop.cs），
/// 本文件为薄壳转发——公开 API 与行为等价不变（损坏隔离 &lt;path&gt;.corrupt + last_was_corrupt）。
/// 数据模型（序列化字段组装/回读）仍由 GameState 负责。
/// M7 全量迁移（2026-08-09 自 scripts/save_manager.gd）
/// </summary>
public partial class SaveManager : RefCounted
{
    /// <summary>最近一次 load 是否因损坏而隔离（GameState 据此设置 save_corrupt / profile_corrupt）</summary>
    public bool LastWasCorrupt { get; set; }

    private readonly SaveStoreInterop _interop = new();

    public bool Exists(string path) => _interop.Exists(path);

    public void Delete(string path) => _interop.Delete(path);

    /// <summary>写 JSON 文件：C# SaveStore 原子写（临时文件 + rename 回退，E12 审计口径：
    /// 先尝试原子 rename 覆盖，首次失败才删正本重试——回退路径才触发风险窗口）。
    /// 打开/写入失败 push_warning 并返回 false（对齐原 save_run/save_profile 行为）。</summary>
    public bool Save(string path, Godot.Collections.Dictionary data) => _interop.Save(path, data);

    /// <summary>读 JSON 文件：不存在/读取失败返回 {}（不置损坏）；损坏则隔离备份并置 last_was_corrupt。</summary>
    public Godot.Collections.Dictionary Load(string path)
    {
        LastWasCorrupt = false;
        var res = _interop.Load(path);
        // 对齐 GDScript `res.get("corrupt") == true`：仅 bool true 置损坏（非 bool 值不置）
        var corrupt = res.GetValueOrDefault("corrupt", new Variant());
        if (corrupt.VariantType == Variant.Type.Bool && corrupt.AsBool())
        {
            LastWasCorrupt = true;
        }

        var data = res.GetValueOrDefault("data", new Variant());
        return data.VariantType == Variant.Type.Dictionary
            ? data.AsGodotDictionary()
            : new Godot.Collections.Dictionary();
    }

    /// <summary>损坏文件隔离：重命名为 &lt;path&gt;.corrupt（已有备份则先删），给玩家留排查余地</summary>
    public void Quarantine(string path) => _interop.Quarantine(path);

    /// <summary>存档数值字段安全读取：手改存档的非法类型（字符串/数组/字典等）回默认值
    /// （bool 非 int/float，回默认——对齐 GDScript `v is int or v is float`）</summary>
    public double SanitizeNum(Variant v, double defaultValue)
        => (v.VariantType == Variant.Type.Int || v.VariantType == Variant.Type.Float) && v.VariantType != Variant.Type.Bool
            ? (double)v.AsDouble()
            : defaultValue;

}
