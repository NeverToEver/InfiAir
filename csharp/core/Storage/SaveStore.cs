using System.Text.Json;
using System.Text.Json.Nodes;

namespace InfiAir.Core.Storage;

/// <summary>加载结果状态（对齐 GDScript SaveManager.load 三态：缺失/正常/损坏隔离）。</summary>
public enum SaveLoadStatus
{
    /// <summary>文件不存在或读取失败（不置损坏——GDScript f == null 语义）。</summary>
    Missing,

    /// <summary>JSON 解析成功且根为对象。</summary>
    Ok,

    /// <summary>解析失败或根非对象：已隔离为 &lt;path&gt;.corrupt 并按无存档处理。</summary>
    Corrupt,
}

/// <summary>加载结果（Tree 仅 Ok 时非空；QuarantineError 为隔离失败时的警告信息）。</summary>
public sealed record SaveLoadResult(
    SaveLoadStatus Status, Dictionary<string, object?>? Tree, string? QuarantineError)
{
    public static SaveLoadResult Missing() => new(SaveLoadStatus.Missing, null, null);
}

/// <summary>
/// 存档文件存储核心（P0-1，2026-08-07 落地）：原子写（临时文件 + rename 回退）、
/// 损坏隔离（.corrupt + 状态标记）、JSON 序列化（System.Text.Json）。
/// 逐条对齐原 GDScript SaveManager（scripts/save_manager.gd，E12 审计口径）；
/// 纯 .NET、零 Godot 依赖，xUnit 直测。文件内容差异（无害）：System.Text.Json 对整数值
/// double 不写小数点（35.0 → "35"），回读数值等价；键序随 Dictionary 枚举序（插入序）。
/// </summary>
public sealed class SaveStore
{
    public bool Exists(string path) => File.Exists(path);

    /// <summary>删除文件（不存在时静默成功，对齐 GDScript 先判存在再删）。</summary>
    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 原子写：先写 &lt;path&gt;.tmp 再 rename 覆盖正本；首次 rename 失败（平台不支持
    /// 原子覆盖）时删正本重试（回退路径，与原实现风险窗口等价）。失败返回 false + 错误信息。
    /// </summary>
    public bool TrySave(string path, object? tree, out string? error)
    {
        try
        {
            var json = JsonSerializer.Serialize(tree);
            var tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json);
            try
            {
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (IOException) when (File.Exists(path))
            {
                File.Delete(path);
                File.Move(tmpPath, path);
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 读 JSON 文件：缺失/读取失败 → Missing（不置损坏）；解析失败或根非对象 → 隔离
    /// 备份为 &lt;path&gt;.corrupt 并返回 Corrupt（对齐 GDScript load + quarantine 语义）。
    /// </summary>
    public SaveLoadResult Load(string path)
    {
        if (!File.Exists(path))
        {
            return SaveLoadResult.Missing();
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return SaveLoadResult.Missing();
        }
        catch (UnauthorizedAccessException)
        {
            return SaveLoadResult.Missing();
        }

        try
        {
            var root = JsonNode.Parse(text);
            if (root is JsonObject obj)
            {
                return new SaveLoadResult(SaveLoadStatus.Ok, JsonToClr(obj), null);
            }
        }
        catch (JsonException)
        {
            // 语法损坏 → 隔离
        }

        var quarantineError = Quarantine(path, out var qError) ? null : qError;
        return new SaveLoadResult(SaveLoadStatus.Corrupt, null, quarantineError);
    }

    /// <summary>损坏文件隔离：重命名为 &lt;path&gt;.corrupt（已有备份先删），失败返回 false + 错误。</summary>
    public bool Quarantine(string path, out string? error)
    {
        try
        {
            var backup = path + ".corrupt";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }

            File.Move(path, backup);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>JsonNode 树 → CLR JSON 兼容树（数值按 GDScript JSON.parse 语义：整型 → long、浮点 → double）。</summary>
    private static Dictionary<string, object?> JsonToClr(JsonObject root)
    {
        var tree = new Dictionary<string, object?>();
        foreach (var kv in root)
        {
            tree[kv.Key] = JsonToClr(kv.Value);
        }

        return tree;
    }

    private static object? JsonToClr(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
                return JsonToClr(obj);
            case JsonArray arr:
                var list = new List<object?>();
                foreach (var item in arr)
                {
                    list.Add(JsonToClr(item));
                }

                return list;
            case JsonValue val:
                switch (val.GetValueKind())
                {
                    case JsonValueKind.Number:
                        // 注意：不可用三元（long→double 隐式拓宽会把整型结果统一装箱成 double）
                        if (val.TryGetValue<long>(out var l))
                        {
                            return l;
                        }

                        // U09（2026-08-09 审计）：溢出数字（如手改 1e999）TryGetValue 返回 false，
                        // 回退 null 而非 GetValue<double>() 抛异常击穿 Load 的"损坏回退"契约
                        if (val.TryGetValue<double>(out var dbl))
                        {
                            return dbl;
                        }

                        return null;
                    case JsonValueKind.String:
                        return val.GetValue<string>();
                    case JsonValueKind.True:
                        return true;
                    case JsonValueKind.False:
                        return false;
                    default:
                        return null;
                }

            default:
                return null;
        }
    }
}
