using Godot;
using InfiAir.Core.Storage;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：设置持久化（user://settings.json，单一本地档案）。
/// </summary>
public partial class GameState : Node
{
    /// <summary>存档文件核心存储（读档状态分型：Missing/Ok/Corrupt/Unreadable 与大小上限判定）。
    /// 写路径走 SaveManager（其 Variant→CLR 转换与告警是唯一入口）；读路径需要观测四态，
    /// 而 SaveManager 只做写入门面，故此处直接用核心层。</summary>
    private readonly SaveStore _jsonStore = new();

    /// <summary>读 JSON 存档并保留四态（路径为 user:// 形式，内部折算成 OS 路径）。</summary>
    private SaveLoadResult LoadJsonCore(string godotPath) =>
        _jsonStore.Load(ProjectSettings.GlobalizePath(godotPath));

    /// <summary>删除 JSON 存档（不存在＝成功；IO/权限失败返回 false 并带出原因）。</summary>
    private bool DeleteJsonCore(string godotPath, out string? error) =>
        _jsonStore.Delete(ProjectSettings.GlobalizePath(godotPath), out error);

    /// <summary>存档数值字段安全读取：手改数据的非法类型（字符串/数组/字典等）回默认值。
    /// 判型与钳制口径单源在 <see cref="RunFieldNormalize"/>（core，纯逻辑、可单测）：
    /// Int/Float 载入为 CLR 数值，其余（含 Nil）落 <c>null</c> 由 core 判否回退默认值。</summary>
    public double SaveNum(Variant v, double defaultValue) =>
        RunFieldNormalize.TryClampNum(SaveVariantNumber(v), out var value) ? value : defaultValue;

    /// <summary>布尔字段安全读取——仅接受真 bool（GDScript 的 bool("false") 为 true，
    /// 手改数据写字符串 "false"/"0" 会被误读为开；与 save_num 同款判型回退）</summary>
    public bool SaveBool(Variant v, bool defaultValue) => v.VariantType == Variant.Type.Bool ? v.AsBool() : defaultValue;

    /// <summary>存档整数字段安全读取：save_num 判型 + 钳入 [0, int.MaxValue]——
    /// 手改超大值（&gt;2^31）经裸 (int) 截断会回绕成负数（统计与里程碑错乱、2038 年后时间戳回绕）。
    /// 钳制语义与 <see cref="RunFieldNormalize"/>.ReadInt 同一份：本处只做 Variant 载入，
    /// 非数值回退 <paramref name="defaultValue"/>。</summary>
    public int SaveInt(Variant v, int defaultValue) =>
        RunFieldNormalize.TryClampInt(SaveVariantNumber(v), out var value) ? value : defaultValue;

    /// <summary>存档数值 Variant → core 判型入口形态（Int→long / Float→double；
    /// 其余返回 <c>null</c> 即「非数值」）。core 的 <c>TryClampInt</c>/<c>TryClampNum</c>
    /// 再统一处理非有限值（NaN/Inf 回退）与 int 域钳制——两份实现不再各写一套钳制。</summary>
    private static object? SaveVariantNumber(Variant v) => v.VariantType switch
    {
        Variant.Type.Int => v.AsInt64(),
        Variant.Type.Float => v.AsDouble(),
        _ => null,
    };

    /// <summary>帧率上限档位表（设置页九选；值为 Engine.MaxFps，0 = 不限制）——SettingsService 转发，
    /// 设置页据此构建档位行（与 RESOLUTION_ORDER 同例，UI 不复制档位表）。</summary>
    public Godot.Collections.Array<StringName> FPS_CAP_ORDER => _settings.FPS_CAP_ORDER;

    // ---------------- 设置持久化（键位/locale/难度/视图/无障碍/手柄/TutorialDone） ----------------

    /// <summary>启动加载设置：缺少新字段时保留当前内存值；损坏文件隔离备份后按默认值继续；
    /// 暂时读不出（IO/权限）时告警并按当前默认值继续，不覆盖也不隔离。</summary>
    public void LoadSettings()
    {
        var result = LoadJsonCore(SettingsPathValue);
        if (result.Status != SaveLoadStatus.Ok || result.Tree is null || result.Tree.Count == 0)
        {
            if (result.Status == SaveLoadStatus.Unreadable)
            {
                GD.PushWarning($"InfiAir: 设置档暂时不可读（{result.Error}）——按当前默认值继续");
            }

            return;
        }

        // 设置域持久化桥在 SettingsService（设置字段应用含键位/窗口/视图缓存副作用）
        _settings.ApplySettingsDict(VariantBridge.ToVariant(result.Tree).AsGodotDictionary());
        // 机型偏好决定生效乘区与血上限，而 ApplyBalance 早于本方法执行（开机链路里数值先就位、
        // 设置后载入）——偏好读到之后必须再落一次生效值，否则盘上选了特种型、本局却按标准型起飞。
        // persist=false：本次读到的正是盘上的值，回写只会给每次开机添一次无谓写盘。
        ApplyMachine(_settings.MachineId, persistPreference: false);
    }

    /// <summary>当前设置字段收集落盘（键位/难度/locale 等设置项变更即调用）——
    /// 设置域持久化桥在 SettingsService（CollectSettingsDict 本体在服务侧，此处委托）。</summary>
    public void SaveSettings()
    {
        var data = _settings.CollectSettingsDict();
        data["version"] = PersistVersionValue;
        _saveManager.Save(SettingsPathValue, data);
    }
}
