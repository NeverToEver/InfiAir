namespace InfiAir.Core.Config;

/// <summary>
/// 默认值变体类型标签（对齐 GDScript <c>typeof()</c> 语义，跨语言桥接用）。
/// <see cref="PathResolver.Resolve(IReadOnlyDictionary{string, object?}, string, object?, ValueKind)"/>
/// 的 <c>typeof(node) == typeof(default)</c> 判定据此进行——绑定壳在转换 Variant 时
/// 一并给出类型标签，避免 StringName 等类型在 CLR 转换后丢失区分度。
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
/// 点路径解析核心（P1-1，2026-08-07 落地）：在 CLR JSON 兼容树上按 "a.b.c" 路径取值。
/// 逐条镜像原 GDScript <c>BalanceService.cfg()</c>（scripts/balance_service.gd，469 处调用点语义）
/// 并保持数值宽容 / 容器浅拷贝 / typeof 相等判定语义；纯 .NET、零 Godot 依赖，xUnit 可直测。
/// 行为差异（均为防御性收敛，仅作用于手改/非 JSON 数据）：
/// 容器拷贝为逐层新建（嵌套容器不再与源共享——比 GDScript 单层 duplicate 更隔离，不污染配置真值）。
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

        // 数值宽容：JSON 整数/浮点互通；按 default 类型显式转换（GDScript int()/float() 语义）
        if (kind is ValueKind.Int or ValueKind.Float)
        {
            if (node is long or double)
            {
                // 注意：此处不可用三元（long→double 隐式拓宽会把结果统一装箱成 double，
                // GDScript int 默认 + int 节点应原样返回 int——数值类型语义差异）
                if (kind == ValueKind.Int)
                {
                    return ToInt(node);
                }

                return ToDouble(node);
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

    /// <summary>按默认值 CLR 类型推断 <see cref="ValueKind"/> 的简版（xUnit 直测用）。</summary>
    public static object? Resolve(
        IReadOnlyDictionary<string, object?> root, string path, object? defaultValue)
    {
        return Resolve(root, path, defaultValue, KindOf(defaultValue));
    }

    /// <summary>CLR 类型 → <see cref="ValueKind"/>（绑定壳对 Variant 类型用同一映射，见 csharp/godot/VariantBridge.cs）。</summary>
    public static ValueKind KindOf(object? value)
    {
        return value switch
        {
            null => ValueKind.Null,
            bool => ValueKind.Bool,
            long => ValueKind.Int,
            double => ValueKind.Float,
            string => ValueKind.String,
            List<object?> => ValueKind.Array,
            Dictionary<string, object?> => ValueKind.Dictionary,
            _ => ValueKind.Other,
        };
    }

    /// <summary>GDScript int() 语义：float → 向零截断（JSON 数值域内安全，unchecked 防越界 UB）。</summary>
    private static long ToInt(object? node)
    {
        return node switch
        {
            long l => l,
            double d => unchecked((long)d),
            _ => 0,
        };
    }

    /// <summary>GDScript float() 语义：int → 拓宽为 double。</summary>
    private static double ToDouble(object? node)
    {
        return node switch
        {
            long l => l,
            double d => d,
            _ => 0,
        };
    }
}
