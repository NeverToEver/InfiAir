using InfiAir.Core.Config;
using Xunit;

namespace InfiAir.Core.Tests.Config;

/// <summary>PathResolver 契约测试：点路径解析 / 数值宽容 / 容器拷贝 / 判型回退。
/// 这是 balance.json 全部读取的唯一口径——回退语义错了，坏配置会静默分叉。</summary>
public sealed class PathResolverTests
{
    private static Dictionary<string, object?> Tree() => new()
    {
        ["player"] = new Dictionary<string, object?>
        {
            ["max_speed"] = 420.9,
            ["neg_speed"] = -4.9,
            ["level"] = 5L,
            ["title"] = "雾都",
            ["tags"] = new List<object?> { 1L, 2L },
            ["nested"] = new Dictionary<string, object?> { ["on"] = true },
        },
    };

    [Fact]
    public void Resolve_DeepPath_ReturnsNode()
    {
        var v = PathResolver.Resolve(Tree(), "player.nested.on", false, ValueKind.Bool);
        Assert.True((bool)v!);
    }

    [Fact]
    public void Resolve_MissingKey_ReturnsDefault()
    {
        var v = PathResolver.Resolve(Tree(), "player.absent.key", 7L, ValueKind.Int);
        Assert.Equal(7L, v);
    }

    [Fact]
    public void Resolve_IntKind_RoundsDoubleTowardZero()
    {
        // GDScript int() 语义：浮点向零截断。取值必须能分辨「截断」与「四舍六入五取偶」：
        // 420.5 在两种语义下都得 420（banker's rounding 的平局规则恰好同值），钉不住实现。
        var v = PathResolver.Resolve(Tree(), "player.max_speed", 0L, ValueKind.Int);
        Assert.Equal(420L, v);

        var negative = PathResolver.Resolve(Tree(), "player.neg_speed", 0L, ValueKind.Int);
        Assert.Equal(-4L, negative);
    }

    [Fact]
    public void Resolve_FloatKind_WidensInt()
    {
        var v = PathResolver.Resolve(Tree(), "player.level", 0.0, ValueKind.Float);
        Assert.Equal(5.0, (double)v!);
    }

    [Fact]
    public void Resolve_KindMismatch_ReturnsDefault()
    {
        // 字符串键配数值默认（手改数据的非法类型）——必须回默认，不得原样透传字符串
        var v = PathResolver.Resolve(Tree(), "player.title", 1.0, ValueKind.Float);
        Assert.Equal(1.0, v);
    }

    [Fact]
    public void Resolve_NonFiniteNumber_FallsBackToDefault()
    {
        // JSON 溢出（1e999 → ±∞）与手改 NaN：非有限值不得透传。Int 分支的
        // unchecked((long)d) 会把 NaN/±∞ 折成 long.MinValue，Float 分支原样透传 ±∞——
        // 两者都会污染下游数值。
        var tree = new Dictionary<string, object?>
        {
            ["nan"] = double.NaN,
            ["inf"] = double.PositiveInfinity,
            ["neg_inf"] = double.NegativeInfinity,
        };

        foreach (var key in new[] { "nan", "inf", "neg_inf" })
        {
            Assert.Equal(7L, PathResolver.Resolve(tree, key, 7L, ValueKind.Int));
            Assert.Equal(1.5, (double)PathResolver.Resolve(tree, key, 1.5, ValueKind.Float)!);
        }
    }

    [Fact]
    public void Resolve_IntKind_OverflowingDouble_FallsBackToDefault()
    {
        // 1e30 超出 long 域：旧实现 unchecked((long)d) 得 long.MinValue（不是默认值）。
        // 有限且可表示的 double 仍按 GDScript int() 向零截断。
        var tree = new Dictionary<string, object?> { ["huge"] = 1e30, ["ok"] = 4.0e18 };
        Assert.Equal(7L, PathResolver.Resolve(tree, "huge", 7L, ValueKind.Int));
        Assert.Equal(4000000000000000000L, PathResolver.Resolve(tree, "ok", 7L, ValueKind.Int));
    }

    [Fact]
    public void Resolve_UnsupportedKind_FallsBackToDefault()
    {
        // CLR JSON 树里没有 StringName 表达（键与值都退化为 string），Other 亦无判型分支——
        // 两种 kind 的 typeof 相等判定永假，必须显式回退默认值（不得原样透传同形字符串/节点）
        var stringDefault = new object();
        var otherDefault = new object();
        Assert.Same(stringDefault, PathResolver.Resolve(Tree(), "player.title", stringDefault, ValueKind.StringName));
        Assert.Same(otherDefault, PathResolver.Resolve(Tree(), "player.level", otherDefault, ValueKind.Other));
    }

    [Fact]
    public void Resolve_ArrayKind_ReturnsDetachedCopy()
    {
        var tree = Tree();
        var first = PathResolver.Resolve(tree, "player.tags", new List<object?>(), ValueKind.Array);
        var list = Assert.IsType<List<object?>>(first);
        list.Add("污染");

        var second = PathResolver.Resolve(tree, "player.tags", new List<object?>(), ValueKind.Array);
        var fresh = Assert.IsType<List<object?>>(second);
        Assert.Equal(2, fresh.Count); // 调用方误写不污染配置真值
    }

    [Fact]
    public void Resolve_DictionaryKind_ReturnsDetachedCopy()
    {
        var tree = Tree();
        var first = PathResolver.Resolve(tree, "player.nested", new Dictionary<string, object?>(), ValueKind.Dictionary);
        var dict = Assert.IsType<Dictionary<string, object?>>(first);
        dict["污染"] = 1L; // 调用方误写返回容器不得污染配置真值（只断返回类型抓不到共享引用）

        var second = PathResolver.Resolve(tree, "player.nested", new Dictionary<string, object?>(), ValueKind.Dictionary);
        var fresh = Assert.IsType<Dictionary<string, object?>>(second);
        Assert.Single(fresh);
        Assert.False(fresh.ContainsKey("污染"));
    }

    [Fact]
    public void Resolve_NullNodeWithNullKind_ReturnsNull()
    {
        var tree = new Dictionary<string, object?> { ["k"] = null! };
        var v = PathResolver.Resolve(tree, "k", null, ValueKind.Null);
        Assert.Null(v);
    }
}
