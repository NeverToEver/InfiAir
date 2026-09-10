using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：设置持久化（user://settings.json，单一本地档案）。
/// </summary>
public partial class GameState : Node
{

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

    // ---------------- 设置持久化（键位/locale/难度/视图/无障碍/手柄/TutorialDone/跳过过场） ----------------

    /// <summary>启动加载设置：缺少新字段时保留当前内存值；损坏文件隔离备份后按默认值继续。</summary>
    public void LoadSettings()
    {
        var parsed = _saveManager.Load(SettingsPathValue);
        if (_saveManager.LastWasCorrupt)
        {
            return;
        }

        if (parsed.Count == 0)
        {
            return;
        }

        // 设置域持久化桥在 SettingsService（设置字段应用含键位/窗口/视图缓存副作用）
        _settings.ApplySettingsDict(parsed);
    }

    /// <summary>当前设置字段收集落盘（键位/难度/locale 等设置项变更即调用）——
    /// 设置域持久化桥在 SettingsService（CollectSettingsDict 本体在服务侧，此处委托）。</summary>
    public void SaveSettings()
    {
        var data = _settings.CollectSettingsDict();
        data["version"] = PersistVersionValue;
        _saveManager.Save(SettingsPathValue, data);
    }

    /// <summary>GDScript 字符串 % 格式化语义（%s/%d/%f 占位 + %% 转义；tr() 文案补参用，
}
