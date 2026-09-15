using System.Text.Json;
using System.Text.Json.Nodes;

namespace InfiAir.Core.Storage;

/// <summary>加载结果状态（四态：缺失/正常/损坏隔离/不可读）。</summary>
public enum SaveLoadStatus
{
    /// <summary>文件确实不存在（按无存档处理）。</summary>
    Missing,

    /// <summary>JSON 解析成功且根为对象。</summary>
    Ok,

    /// <summary>解析失败 / 根非对象 / 超过大小上限：已隔离为 &lt;path&gt;.corrupt 并按无存档处理。</summary>
    Corrupt,

    /// <summary>IO / 权限 / 被占用导致读不出内容——**不得**当成无存档：
    /// 旧档可能只是暂时打不开，按无档处理会让上层把玩家进度覆盖成全新一局。</summary>
    Unreadable,
}

/// <summary>加载结果（Tree 仅 Ok 时非空；QuarantineError 为隔离失败时的警告信息，
/// Error 为 Unreadable 的 IO/权限原因）。</summary>
public sealed record SaveLoadResult(
    SaveLoadStatus Status, Dictionary<string, object?>? Tree, string? QuarantineError)
{
    /// <summary>Unreadable 的原因（供调用方告警；其余状态为 null）。</summary>
    public string? Error { get; init; }

    public static SaveLoadResult Missing() => new(SaveLoadStatus.Missing, null, null);

    public static SaveLoadResult Unreadable(string error) =>
        new(SaveLoadStatus.Unreadable, null, null) { Error = error };
}

/// <summary>
/// 存档文件存储核心：原子写（临时文件 + rename 回退）、
/// 损坏隔离（.corrupt + 状态标记）、JSON 序列化（System.Text.Json）。
/// 语义与 Godot 侧 SaveManager 一致；纯 .NET、零 Godot 依赖，可独立单测。
/// 文件内容差异（无害）：System.Text.Json 对整数值
/// double 不写小数点（35.0 → "35"），回读数值等价；键序随 Dictionary 枚举序（插入序）。
/// </summary>
public sealed class SaveStore
{
    /// <summary>单个存档文件的大小上限（1 MiB）：远超正常档体积的「损坏档」不整份读入内存
    /// （手改/被写坏的巨型文件会让 ReadAllText 直接 OOM），超限按 Corrupt 隔离。</summary>
    public const long MaxSaveBytes = 1024 * 1024;

    public bool Exists(string path) => File.Exists(path);

    /// <summary>删除文件（不存在＝成功）；IO/权限失败返回 false 并带出原因。
    /// 旧实现吞掉全部异常，死亡删档失败时玩家既能继续读回进度、又无人知晓。</summary>
    public bool Delete(string path, out string? error)
    {
        try
        {
            // 目录占位（或路径被目录顶替）时 File.Exists 为 false 却删不掉——显式判成失败，
            // 不落入「不存在＝成功」的假象
            if (Directory.Exists(path))
            {
                error = "路径被目录占用";
                return false;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            error = null;
            return true;
        }
        catch (FileNotFoundException)
        {
            // 判存在与删除之间文件消失：目标已不存在，等同成功
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>无原因删除（SaveManager 门面沿用）；需要观测失败请用带 out 重载。</summary>
    public void Delete(string path) => Delete(path, out _);

    /// <summary>
    /// 原子写：先写 &lt;path&gt;.tmp，首次覆盖既有档前留一份 &lt;path&gt;.bak，再 rename 覆盖正本；
    /// rename 失败（平台不支持原子覆盖）时把正本改名为 .bak 再落新档。
    /// 失败返回 false + 错误信息。回退路径不得先删正本——那会开出「正本已删、新档未落」的丢档窗口。
    /// 序列化后先判 <see cref="MaxSaveBytes"/>：读侧把超限档当损坏隔离，写侧若照写不误，就会
    /// 产出「自己刚写出、自己读不回」的档——下一次开机该档被判 Corrupt 改名隔离，进度静默丢失
    /// （触发源是手改档塞入超长点值序列：TalentCache.RestoreValues 接受任意长度，读得进、
    /// 再落盘就写出巨型档）。两侧必须同一上限。
    /// </summary>
    public bool TrySave(string path, object? tree, out string? error)
    {
        try
        {
            var json = JsonSerializer.Serialize(tree);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxSaveBytes)
            {
                error = $"序列化结果超过单档上限 {MaxSaveBytes} 字节——写出即变成读侧判损坏的巨型档";
                return false;
            }

            var tmpPath = path + ".tmp";
            var backupPath = path + ".bak";
            File.WriteAllText(tmpPath, json);
            // 首次覆盖既有档前确保 .bak 存在：覆盖写坏/写一半时旧进度仍可取回。
            // 尽力而为——旧档可能暂时不可读（占用/权限），此时不能因此阻断保存本身；
            // 回退路径的改名仍会产出一份 .bak。
            if (File.Exists(path) && !File.Exists(backupPath))
            {
                try
                {
                    File.Copy(path, backupPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // .bak 非必需；保存本身继续
                }
            }

            try
            {
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (IOException) when (File.Exists(path))
            {
                // 平台不支持原子覆盖：先把正本改名成 .bak（内容仍在盘上），再落新档
                File.Move(path, backupPath, overwrite: true);
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
    /// 读 JSON 文件四态：不存在 → Missing；目录占位 / IO / 权限失败 → Unreadable（不置损坏，
    /// 也不当成无档）；解析失败、根非对象或超过 <see cref="MaxSaveBytes"/> → 隔离为
    /// &lt;path&gt;.corrupt 并返回 Corrupt（对齐 GDScript load + quarantine 语义）。
    /// </summary>
    public SaveLoadResult Load(string path)
    {
        if (Directory.Exists(path))
        {
            return SaveLoadResult.Unreadable("路径被目录占用");
        }

        if (!File.Exists(path))
        {
            return SaveLoadResult.Missing();
        }

        string text;
        try
        {
            if (new FileInfo(path).Length > MaxSaveBytes)
            {
                // 超大损坏档：不整份读入（OOM 防御），直接同路径隔离
                return QuarantineResult(path);
            }

            text = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return SaveLoadResult.Missing(); // 判存在与读取之间消失：确实无档
        }
        catch (DirectoryNotFoundException)
        {
            return SaveLoadResult.Missing(); // 父目录不存在：确实无档
        }
        catch (IOException ex)
        {
            return SaveLoadResult.Unreadable(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return SaveLoadResult.Unreadable(ex.Message);
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
        catch (ArgumentException)
        {
            // 重复键 JSON 在 JsonNode.Parse/JsonToClr 枚举时抛
            // ArgumentException（dotnet/runtime#71784，.NET 8 未修）——只 catch JsonException
            // 会让异常逃逸击穿"损坏隔离"契约（欢迎页崩溃），此处与语法损坏同路径隔离
        }

        return QuarantineResult(path);
    }

    private SaveLoadResult QuarantineResult(string path)
    {
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

                        // 溢出数字（手改 1e999 等）解析为 ±∞——非有限值按缺失处理，
                        // 不让它渗进存档字段（下游 Clamp 虽能兜住部分路径，但 ∞ 参与比较/求和仍会产 NaN）
                        if (val.TryGetValue<double>(out var dbl) && double.IsFinite(dbl))
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
