using Godot;

namespace InfiAir;

/// <summary>
/// AC4 判型口径统一助手：Cfg 标量键读取 + 类型守卫 + 可选域钳（2026-08-11 健壮性审查，
/// 对齐 Enemy.cs 同款 AC4 判型）——PathResolver 已保证数值键坏类型回退默认，此处判型为
/// 第二道防线（坏类型 AsDouble/AsInt64 抛 InvalidCastException 崩溃）；仅 _Ready 一次性
/// 调用非热路径。min/max 与原调用点 Mathf.Max/Mathf.Clamp 参数逐一对应，不得新增/删除钳制。
/// </summary>
public static class CfgFx
{
    public static float Float(string path, float def, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
    {
        var v = GameState.Instance.Cfg(path, def);
        var x = v.VariantType is Variant.Type.Int or Variant.Type.Float ? (float)v.AsDouble() : def;
        return Mathf.Clamp(x, min, max);
    }

    public static int Int(string path, int def, int min = int.MinValue, int max = int.MaxValue)
    {
        var v = GameState.Instance.Cfg(path, def);
        var x = v.VariantType is Variant.Type.Int or Variant.Type.Float ? (int)v.AsInt64() : def;
        return Mathf.Clamp(x, min, max);
    }
}
