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
            ["max_speed"] = 420.5,
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
        // GDScript int() 语义：浮点向零截断
        var v = PathResolver.Resolve(Tree(), "player.max_speed", 0L, ValueKind.Int);
        Assert.Equal(420L, v);
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
        Assert.IsType<Dictionary<string, object?>>(first);
    }

    [Fact]
    public void Resolve_NullNodeWithNullKind_ReturnsNull()
    {
        var tree = new Dictionary<string, object?> { ["k"] = null! };
        var v = PathResolver.Resolve(tree, "k", null, ValueKind.Null);
        Assert.Null(v);
    }
}
