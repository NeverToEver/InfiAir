using Godot;

namespace InfiAir;

/// <summary>
/// 设置持久化（user://settings.json）的文件 IO。
/// 原子写 / 损坏隔离 / JSON 序列化在 InfiAir.Core.Storage.SaveStore
/// （C#，见 csharp/core/Storage/SaveStore.cs），本类承担 GlobalizePath（user:// → OS 路径）
/// 与 Variant↔CLR 转换（VariantBridge）后直接调用核心层——公开 API 与行为等价不变
/// （损坏隔离 &lt;path&gt;.corrupt + last_was_corrupt）。
/// 数据模型（序列化字段组装/回读）仍由 GameState 负责。
/// </summary>
public partial class SaveManager : RefCounted
{
    /// <summary>最近一次 load 是否因损坏而隔离（损坏文件已改名备份为 &lt;path&gt;.corrupt，GameState 据此按默认值继续）</summary>
    public bool LastWasCorrupt { get; set; }

    private readonly Core.Storage.SaveStore _store = new();

    public bool Exists(string path) => _store.Exists(Globalize(path));

    public void Delete(string path) => _store.Delete(Globalize(path));

    /// <summary>写 JSON 文件：C# SaveStore 原子写（临时文件 + rename 回退：
    /// 先尝试原子 rename 覆盖，首次失败才删正本重试——回退路径才触发风险窗口）。
    /// 打开/写入失败 push_warning 并返回 false。</summary>
    public bool Save(string path, Godot.Collections.Dictionary data)
    {
        if (!VariantBridge.TryToClr(data, out var clr, out var convError))
        {
            GD.PushWarning($"InfiAir: 存档数据含不支持的类型（{convError}）——{path}");
            return false;
        }

        if (!_store.TrySave(Globalize(path), clr, out var error))
        {
            GD.PushWarning($"InfiAir: 无法写入 {path}（{error}）");
            return false;
        }

        return true;
    }

    /// <summary>读 JSON 文件：不存在/读取失败返回 {}（不置损坏）；损坏则隔离备份并置 last_was_corrupt。</summary>
    public Godot.Collections.Dictionary Load(string path)
    {
        LastWasCorrupt = false;
        var res = _store.Load(Globalize(path));
        if (res.Status == Core.Storage.SaveLoadStatus.Corrupt)
        {
            LastWasCorrupt = true;
        }

        if (res.QuarantineError is not null)
        {
            GD.PushWarning($"InfiAir: 无法备份损坏文件 {path}（{res.QuarantineError}）");
        }

        return res.Status == Core.Storage.SaveLoadStatus.Ok && res.Tree is not null
            ? VariantBridge.ToVariant(res.Tree).AsGodotDictionary()
            : new Godot.Collections.Dictionary();
    }

    /// <summary>损坏文件隔离：重命名为 &lt;path&gt;.corrupt（已有备份则先删），给玩家留排查余地</summary>
    public void Quarantine(string path) => _store.Quarantine(Globalize(path), out _);

    /// <summary>存档数值字段安全读取：手改存档的非法类型（字符串/数组/字典等）回默认值
    /// （Int/Float 类型不可能是 Bool，直接判类型即可）。</summary>
    public double SanitizeNum(Variant v, double defaultValue)
        => v.VariantType is Variant.Type.Int or Variant.Type.Float
            ? (double)v.AsDouble()
            : defaultValue;

    /// <summary>user:// 等 Godot 路径 → OS 路径（核心层只做纯文件 IO）。</summary>
    private static string Globalize(string path) => ProjectSettings.GlobalizePath(path);

}
