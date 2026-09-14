namespace InfiAir.Core.Storage;

/// <summary>
/// 本局存档字段的判型与规范化（零 Godot 依赖，可单测）。
///
/// 存在理由：这些规则原先写死在 Godot 侧 GameState.RunSave/SaveInt 里——同一件事两处口径，
/// 且 <c>ReadIntMap</c> 用裸 <c>(int)val.AsDouble()</c> 无钳制（同文件 SaveInt 却钳了
/// [0, int.MaxValue]）：手改档的超大值经裸转换回绕成负数，统计与里程碑静默错乱；
/// 旧档字段缺失时的回退语义也没有任何用例钉住。判定下沉后由单测覆盖。
///
/// 语义对齐 Godot 侧既有实现（SaveInt / SaveNum / NormalizeAugments）：
/// 数值宽容（int/float 互通）、判型不符回退默认值、int 域钳 [0, int.MaxValue]、非有限值回退。
/// 输入为 CLR JSON 兼容树（<c>Dictionary&lt;string, object?&gt;</c>，数值为 long/double）。
/// </summary>
public static class RunFieldNormalize
{
    /// <summary>读存档 int 字段（对齐 SaveInt）：缺键 / 判型不符 / 非有限回退 <paramref name="fallback"/>；
    /// 有限数值向零截断并钳 [0, int.MaxValue]。</summary>
    public static int ReadInt(IReadOnlyDictionary<string, object?> data, string key, int fallback) =>
        data.TryGetValue(key, out var raw) && TryClampInt(raw, out var value) ? value : fallback;

    /// <summary>读存档数值字段（对齐 SaveNum）：缺键 / 判型不符 / 非有限回退 <paramref name="fallback"/>。
    /// 非有限值必须回退——NaN 会顺着求和/比较污染血量与难度状态。</summary>
    public static double ReadNum(IReadOnlyDictionary<string, object?> data, string key, double fallback) =>
        data.TryGetValue(key, out var raw) && TryClampNum(raw, out var value) ? value : fallback;

    /// <summary>数值 → int 域：非数值返回 false；有限值向零截断并钳 [0, int.MaxValue]，非有限返回 false。</summary>
    public static bool TryClampInt(object? raw, out int value)
    {
        switch (raw)
        {
            case long l:
                value = ClampLong(l);
                return true;
            case double d when double.IsFinite(d):
                value = d <= 0.0 ? 0 : (d >= int.MaxValue ? int.MaxValue : (int)d);
                return true;
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>数值 → double：非数值/非有限返回 false，其余原样（int 拓宽为 double）。</summary>
    public static bool TryClampNum(object? raw, out double value)
    {
        switch (raw)
        {
            case long l:
                value = l;
                return true;
            case double d when double.IsFinite(d):
                value = d;
                return true;
            default:
                value = 0.0;
                return false;
        }
    }

    /// <summary>
    /// 存档 int 子表读取（talent_levels / last_kind_value 等）：逐项判型 + 钳 [0, int.MaxValue]；
    /// 非数值项整条跳过。键缺失 / 非字典输入返回空表（调用方按空进度处理）。
    /// </summary>
    public static Dictionary<string, int> ReadIntMap(object? raw)
    {
        var result = new Dictionary<string, int>();
        if (raw is not IReadOnlyDictionary<string, object?> map)
        {
            return result;
        }

        foreach (var kv in map)
        {
            if (TryClampInt(kv.Value, out var value))
            {
                result[kv.Key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// 增幅表规范化：只保留正层级（0 / 负 / 非数值丢弃），层级钳 [0, int.MaxValue]。
    /// 键在 Godot 侧需重建为 StringName（JSON 往返会退化为 String），本函数只产出字符串键。
    /// </summary>
    public static Dictionary<string, int> NormalizeAugments(object? raw)
    {
        var result = new Dictionary<string, int>();
        if (raw is not IReadOnlyDictionary<string, object?> map)
        {
            return result;
        }

        foreach (var kv in map)
        {
            if (TryClampInt(kv.Value, out var level) && level > 0)
            {
                result[kv.Key] = level;
            }
        }

        return result;
    }

    private static int ClampLong(long value)
    {
        if (value <= 0L)
        {
            return 0;
        }

        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }
}
