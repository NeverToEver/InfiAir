using Godot;

namespace InfiAir;

/// <summary>
/// 写入侧的文件 IO 门面（user://settings.json 与 run.json 共用）。
/// 原子写 / 损坏隔离 / JSON 序列化在 InfiAir.Core.Storage.SaveStore
/// （C#，见 csharp/core/Storage/SaveStore.cs），本类承担 GlobalizePath（user:// → OS 路径）
/// 与 Variant↔CLR 转换（VariantBridge）后调用核心层。
/// 读路径不经本类——需要观测「无档 / 损坏 / 暂时不可读」四态，而本门面会把非 Ok 压成空字典；
/// 数据模型（序列化字段组装/回读）由 GameState 负责。
/// </summary>
public partial class SaveManager : RefCounted
{
    private readonly Core.Storage.SaveStore _store = new();

    /// <summary>写 JSON 文件：C# SaveStore 原子写（尝试原子 rename 覆盖，失败则把正本改名 .bak 后落新档）。
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

    /// <summary>存档数值字段安全读取：手改存档的非法类型（字符串/数组/字典等）回默认值
    /// （Int/Float 类型不可能是 Bool，直接判类型即可）。</summary>
    public double SanitizeNum(Variant v, double defaultValue)
        => v.VariantType is Variant.Type.Int or Variant.Type.Float
            ? (double)v.AsDouble()
            : defaultValue;

    /// <summary>user:// 等 Godot 路径 → OS 路径（核心层只做纯文件 IO）。</summary>
    private static string Globalize(string path) => ProjectSettings.GlobalizePath(path);

}
