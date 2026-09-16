namespace InfiAir.Core.Combat;

/// <summary>
/// 遭遇单位配置（精英炮塔 <c>fire_interval</c> / <c>weak_lock</c>）的条目级读取口径
/// （纯逻辑，零 Godot 依赖）。
///
/// 抓的静默错误：容器判型（Array / Dictionary）之外还要判**元素**——元素写成字符串/数组时
/// Godot 的 <c>As*</c> 宽松转换得 0（不抛），于是 <c>fire_interval=[坏,坏]</c> 让开火计时恒 0，
/// 炮台每物理帧开火；<c>weak_lock.turn_rate=坏</c> 让炮台不再转向、弹道永远朝下。两者都不崩、
/// 不打日志，只在画面与难度上静默变形。
///
/// 判型失败与负值一律回退默认（数值权威仍在 balance.json，本类只做域护栏）。
/// </summary>
public static class EncounterConfig
{
    /// <summary>区间端点的域钳，分三档：判型失败 / 非有限 / **负值** → 回退默认（负值经下游
    /// clamp 与取反会变成另一套行为，如负 <c>turn_rate</c> 使每帧转向钳制区间倒置、炮台瞬间对准玩家）；
    /// 0 与小于下界的正值 → 钳到下界（合法取值但过小：0 会让开火计时每帧归零）；
    /// 合法值原样返回。<paramref name="isValid"/> 由调用侧按 Variant 类型给出（引擎判型不下沉）。</summary>
    public static float RangeEndpoint(bool isValid, float value, float fallback, float floor)
    {
        if (!isValid || !float.IsFinite(value) || value < 0.0f)
        {
            return fallback;
        }

        return value > floor ? value : floor;
    }

    /// <summary>区间两端：各自按 <see cref="RangeEndpoint"/> 读取，倒置（上界低于下界）时交换——
    /// 倒置区间经 <c>RandRange</c> 会反向取值，等价于配置损坏。</summary>
    public static (float Min, float Max) Range(
        bool minValid, float min, bool maxValid, float max, float defMin, float defMax, float floor)
    {
        var lo = RangeEndpoint(minValid, min, defMin, floor);
        var hi = RangeEndpoint(maxValid, max, defMax, floor);
        return lo <= hi ? (lo, hi) : (hi, lo);
    }
}
