using Godot;

namespace InfiAir;

/// <summary>
/// P0-1 绑定壳：SaveManager 文件 IO 的 C# 桥（InfiAir.Core.Storage.SaveStore 纯逻辑）。
/// GDScript 侧 scripts/save_manager.gd 全量转发（公开 API 不变），user:// 路径经
/// GlobalizePath 转 OS 路径后交给核心层；损坏隔离 + last_was_corrupt 语义保持。
/// </summary>
public partial class SaveStoreInterop : RefCounted
{
    private readonly Core.Storage.SaveStore _store = new();

    public bool Exists(string path) => _store.Exists(Globalize(path));

    public void Delete(string path) => _store.Delete(Globalize(path));

    /// <summary>保存 Dictionary：Variant → CLR 树 → System.Text.Json 序列化 + 原子写。</summary>
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

    /// <summary>加载：返回 {corrupt: bool, data: Dictionary}；缺失/损坏时 data 为空值。</summary>
    public Godot.Collections.Dictionary Load(string path)
    {
        var res = _store.Load(Globalize(path));
        var result = new Godot.Collections.Dictionary();
        result["corrupt"] = res.Status == Core.Storage.SaveLoadStatus.Corrupt;
        result["data"] = res.Status == Core.Storage.SaveLoadStatus.Ok && res.Tree is not null
            ? VariantBridge.ToVariant(res.Tree)
            : new Variant();
        if (res.QuarantineError is not null)
        {
            GD.PushWarning($"InfiAir: 无法备份损坏文件 {path}（{res.QuarantineError}）");
        }

        return result;
    }

    /// <summary>损坏文件隔离（GameState 档主不匹配路径显式调用）。</summary>
    public bool Quarantine(string path) => _store.Quarantine(Globalize(path), out _);

    private static string Globalize(string path) => ProjectSettings.GlobalizePath(path);
}
