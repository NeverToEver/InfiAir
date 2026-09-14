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

    /// <summary>存档数值字段安全读取：手改数据的非法类型（字符串/数组/字典等）回默认值
    /// （委托 SaveManager.SanitizeNum——GDScript 浮点为 64 位，经 Variant 往返保持逐位等价）</summary>
    public double SaveNum(Variant v, double defaultValue) => _saveManager.SanitizeNum(v, defaultValue);

    /// <summary>布尔字段安全读取——仅接受真 bool（GDScript 的 bool("false") 为 true，
    /// 手改数据写字符串 "false"/"0" 会被误读为开；与 sanitize_num 同款判型回退）</summary>
    public bool SaveBool(Variant v, bool defaultValue) => v.VariantType == Variant.Type.Bool ? v.AsBool() : defaultValue;

    /// <summary>存档整数字段安全读取：save_num 判型 + 钳入 [0, int.MaxValue]——
    /// 手改超大值（&gt;2^31）经裸 (int) 截断会回绕成负数（统计与里程碑错乱、2038 年后时间戳回绕），
    /// 先钳 long 域再转 int</summary>
    public int SaveInt(Variant v, int defaultValue) => (int)Math.Clamp(SaveNum(v, defaultValue), 0.0, (double)int.MaxValue);

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
