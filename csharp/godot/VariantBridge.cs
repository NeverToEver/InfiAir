using Godot;
using InfiAir.Core.Config;

namespace InfiAir;

/// <summary>
/// GDScript Variant ↔ CLR JSON 兼容树双向转换（Godot 绑定层共享工具，P1-1 起各互操作壳复用）。
/// 纯 JSON 语义映射：Dictionary → Dictionary&lt;string, object?&gt;、Array → List&lt;object?&gt;、
/// int → long、float → double、StringName → string（键与值同规），其余引擎类型不支持（返回失败）。
/// 供 PathResolverInterop / SaveStoreInterop / UserDbInterop 复用；核心层零 Godot 依赖。
/// </summary>
internal static class VariantBridge
{
    /// <summary>Godot Variant → CLR 值；失败返回 false 并给出原因（不支持的类型/键）。</summary>
    public static bool TryToClr(Variant value, out object? clr, out string? error)
    {
        switch (value.VariantType)
        {
            case Variant.Type.Nil:
                clr = null;
                error = null;
                return true;
            case Variant.Type.Bool:
                clr = value.AsBool();
                error = null;
                return true;
            case Variant.Type.Int:
                clr = value.AsInt64();
                error = null;
                return true;
            case Variant.Type.Float:
                clr = value.AsDouble();
                error = null;
                return true;
            case Variant.Type.String:
                clr = value.AsString();
                error = null;
                return true;
            case Variant.Type.StringName:
                clr = value.AsStringName().ToString();
                error = null;
                return true;
            case Variant.Type.Dictionary:
                var dict = value.AsGodotDictionary();
                var tree = new Dictionary<string, object?>();
                foreach (var key in dict.Keys)
                {
                    var k = key.AsStringName().ToString();
                    if (!TryToClr(dict[key], out var v, out error))
                    {
                        clr = null;
                        return false;
                    }

                    tree[k] = v;
                }

                clr = tree;
                error = null;
                return true;
            case Variant.Type.Array:
                var arr = value.AsGodotArray();
                var list = new List<object?>();
                foreach (var item in arr)
                {
                    if (!TryToClr(item, out var v, out error))
                    {
                        clr = null;
                        return false;
                    }

                    list.Add(v);
                }

                clr = list;
                error = null;
                return true;
            default:
                clr = null;
                error = $"unsupported variant type {value.VariantType}";
                return false;
        }
    }

    /// <summary>Variant 类型 → 解析类型标签（StringName 与 String 区分——core 的 typeof 相等判定依据）。</summary>
    public static ValueKind KindOf(Variant value)
    {
        return value.VariantType switch
        {
            Variant.Type.Nil => ValueKind.Null,
            Variant.Type.Bool => ValueKind.Bool,
            Variant.Type.Int => ValueKind.Int,
            Variant.Type.Float => ValueKind.Float,
            Variant.Type.String => ValueKind.String,
            Variant.Type.StringName => ValueKind.StringName,
            Variant.Type.Array => ValueKind.Array,
            Variant.Type.Dictionary => ValueKind.Dictionary,
            _ => ValueKind.Other,
        };
    }

    /// <summary>CLR 值 → Godot Variant（JSON 兼容树反向；不支持类型抛异常——仅内部使用，输入受控）。</summary>
    public static Variant ToVariant(object? value)
    {
        switch (value)
        {
            case null:
                return new Variant();
            case Dictionary<string, object?> dict:
                var gdDict = new Godot.Collections.Dictionary();
                foreach (var kv in dict)
                {
                    gdDict[kv.Key] = ToVariant(kv.Value);
                }

                return Variant.From(gdDict);
            case List<object?> list:
                var gdArray = new Godot.Collections.Array();
                foreach (var item in list)
                {
                    gdArray.Add(ToVariant(item));
                }

                return Variant.From(gdArray);
            case long l:
                return Variant.From(l);
            case double d:
                return Variant.From(d);
            case string s:
                return Variant.From(s);
            case bool b:
                return Variant.From(b);
            default:
                throw new InvalidOperationException($"unsupported CLR value type {value.GetType()}");
        }
    }
}
