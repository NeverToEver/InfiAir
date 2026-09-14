namespace InfiAir.Core.Config;

/// <summary>
/// 默认值变体类型标签（对齐 GDScript <c>typeof()</c> 语义，跨语言桥接用）。
/// <see cref="PathResolver.Resolve(IReadOnlyDictionary{string, object?}, string, object?, ValueKind)"/>
/// 的 <c>typeof(node) == typeof(default)</c> 判定据此进行——绑定壳在转换 Variant 时
/// 一并给出类型标签，避免 StringName 等类型在 CLR 转换后丢失区分度。
///
/// <see cref="StringName"/> 与 <see cref="Other"/> 在 CLR JSON 兼容树里无法表示对应值：
/// StringName 值经绑定时退化为 string（与 String 无区分度），Other 无判型分支。
/// 两者一律回退默认值，不参与原样透传。
/// </summary>
public enum ValueKind
{
    Null,
    Bool,
    Int,
    Float,
    String,
    StringName,
    Array,
    Dictionary,
    Other,
}

/// <summary>
/// 点路径解析核心：在 CLR JSON 兼容树上按 "a.b.c" 路径取值。
/// 承载 Godot 侧 BalanceService.cfg() 全部调用点语义：数值宽容 / 容器浅拷贝 / typeof 相等判定；
/// 纯 .NET、零 Godot 依赖，可独立单测。
/// 容器拷贝为单层新容器（与 GDScript <c>duplicate()</c> 默认语义一致）：顶层增删不污染配置真值，
/// 但嵌套容器仍与源共享——消费方不得就地改写取回容器里的嵌套容器。
/// </summary>
public static class PathResolver
{
    /// <summary>按路径取值；缺键 / 类型不符回退 <paramref name="defaultValue"/>。</summary>
    public static object? Resolve(
        IReadOnlyDictionary<string, object?> root, string path, object? defaultValue, ValueKind kind)
    {
        object? node = root;
        foreach (var key in path.Split('.'))
        {
            if (node is IReadOnlyDictionary<string, object?> dict && dict.TryGetValue(key, out var next))
            {
                node = next;
            }
            else
            {
                return defaultValue;
            }
        }

        // 数值宽容：JSON 整数/浮点互通；按 default 类型显式转换（GDScript int()/float() 语义）。
        // 非有限（NaN/±∞，JSON 溢出 1e999 或手改数据）一律回退默认值：透传 ±∞ 会污染下游数值，
        // 而 unchecked((long)d) 对 NaN/越界 double 折成 long.MinValue（不是默认值）。
        if (kind is ValueKind.Int or ValueKind.Float)
        {
            if (node is long or double)
            {
                // 注意：此处不可用三元（long→double 隐式拓宽会把结果统一装箱成 double，
                // GDScript int 默认 + int 节点应原样返回 int——数值类型语义差异）
                if (kind == ValueKind.Int)
                {
                    return ToInt(node, defaultValue);
                }

                return ToDouble(node, defaultValue);
            }

            return defaultValue;
        }

        // 容器浅拷贝语义：返回新容器，调用方误写不污染配置真值
        if (node is List<object?> list && kind == ValueKind.Array)
        {
            return new List<object?>(list);
        }

        if (node is Dictionary<string, object?> dictNode && kind == ValueKind.Dictionary)
        {
            return new Dictionary<string, object?>(dictNode);
        }

        // typeof(node) == typeof(default) 判定（GDScript 其余类型走相等返回 node）
        if (node is null && kind == ValueKind.Null)
        {
            return null;
        }

        if (node is string s && kind == ValueKind.String)
        {
            return s;
        }

        if (node is bool b && kind == ValueKind.Bool)
        {
            return b;
        }

        return defaultValue;
    }

    /// <summary>GDScript int() 语义：float → 向零截断。
    /// NaN/±∞ 与超出 long 域的 double 回退 <paramref name="defaultValue"/>——
    /// 旧实现的 unchecked((long)d) 对这类输入折成 long.MinValue，把「缺省」伪装成一个极端有效值。</summary>
    private static object? ToInt(object? node, object? defaultValue)
    {
        switch (node)
        {
            case long l:
                return l;
            case double d when double.IsFinite(d) && d >= LongMinAsDouble && d < LongMaxExclusiveAsDouble:
                return (long)d;
            default:
                return defaultValue;
        }
    }

    /// <summary>GDScript float() 语义：int → 拓宽为 double；NaN/±∞ 回退 <paramref name="defaultValue"/>。</summary>
    private static object? ToDouble(object? node, object? defaultValue)
    {
        return node switch
        {
            long l => (double)l,
            double d when double.IsFinite(d) => d,
            _ => defaultValue,
        };
    }

    /// <summary>long.MinValue 的 double 表示（-2^63，精确可表示）——cast 安全下界。</summary>
    private const double LongMinAsDouble = -9223372036854775808.0;

    /// <summary>2^63（long.MaxValue + 1，精确可表示）——开区间上界，越过即越界。</summary>
    private const double LongMaxExclusiveAsDouble = 9223372036854775808.0;
}
