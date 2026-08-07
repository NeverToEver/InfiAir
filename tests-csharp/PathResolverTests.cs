using InfiAir.Core.Config;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// PathResolver（P1-1）单测：逐条对齐原 GDScript BalanceService.cfg() 语义
/// （数值宽容 / 容器浅拷贝 / typeof 相等 / 缺键回退），抽查值语义同 test/balance_test.gd。
/// </summary>
public sealed class PathResolverTests
{
    private static Dictionary<string, object?> BuildTree()
    {
        return new Dictionary<string, object?>
        {
            ["version"] = 2L,
            ["player"] = new Dictionary<string, object?>
            {
                ["max_speed"] = 420L,
                ["max_health"] = 100.0,
                ["fuel"] = new Dictionary<string, object?> { ["drain"] = 35.0 },
            },
            ["mothership"] = new Dictionary<string, object?>
            {
                ["depart_cooldown"] = 60.0,
                ["mag_cells"] = 10L,
                ["missile"] = new Dictionary<string, object?> { ["damage"] = 80L },
            },
            ["boss"] = new Dictionary<string, object?>
            {
                ["hp_mults"] = new List<object?> { 1.3, 1.5, 1.8, 2.2 },
                ["collision_damage"] = 30L,
            },
            ["flag"] = true,
            ["label"] = "str",
            ["empty"] = new Dictionary<string, object?>(),
        };
    }

    [Fact]
    public void Resolve_NestedPath_FindsValue()
    {
        Assert.Equal(420L, PathResolver.Resolve(BuildTree(), "player.max_speed", 0L));
        Assert.Equal(35.0, PathResolver.Resolve(BuildTree(), "player.fuel.drain", 0.0));
        Assert.Equal(80L, PathResolver.Resolve(BuildTree(), "mothership.missile.damage", 0L));
    }

    [Fact]
    public void Resolve_MissingKey_ReturnsDefault()
    {
        Assert.Equal(7L, PathResolver.Resolve(BuildTree(), "player.missing", 7L));
        Assert.Equal("d", PathResolver.Resolve(BuildTree(), "a.b.c", "d"));
        Assert.Equal(5.0, PathResolver.Resolve(BuildTree(), "player.fuel", 5.0)); // 节点存在但类型不符
    }

    [Fact]
    public void Resolve_NumericCoercion_IntDefaultTruncatesFloatNode()
    {
        Assert.Equal(60L, PathResolver.Resolve(BuildTree(), "mothership.depart_cooldown", 0L));
        Assert.Equal(420L, PathResolver.Resolve(BuildTree(), "player.max_speed", 0L)); // int 节点 + int 默认
    }

    [Fact]
    public void Resolve_NumericCoercion_FloatDefaultWidensIntNode()
    {
        Assert.Equal(420.0, PathResolver.Resolve(BuildTree(), "player.max_speed", 0.0));
        Assert.Equal(60.0, PathResolver.Resolve(BuildTree(), "mothership.depart_cooldown", 0.0));
    }

    [Fact]
    public void Resolve_NonNumericNode_WithNumericDefault_ReturnsDefault()
    {
        Assert.Equal(0L, PathResolver.Resolve(BuildTree(), "label", 0L));
        Assert.Equal(0.0, PathResolver.Resolve(BuildTree(), "flag", 0.0));
    }

    [Fact]
    public void Resolve_ArrayNode_ReturnsShallowCopy()
    {
        var result = PathResolver.Resolve(BuildTree(), "boss.hp_mults", new List<object?>());
        Assert.IsType<List<object?>>(result);
        Assert.Equal(4, ((List<object?>)result!).Count);
        Assert.Equal(1.3, ((List<object?>)result)[0]);

        // 拷贝隔离：修改返回值不影响源树
        ((List<object?>)result!).Clear();
        var again = PathResolver.Resolve(BuildTree(), "boss.hp_mults", new List<object?>());
        Assert.Equal(4, ((List<object?>)again!).Count);
    }

    [Fact]
    public void Resolve_DictionaryNode_ReturnsShallowCopy()
    {
        var result = PathResolver.Resolve(BuildTree(), "player", new Dictionary<string, object?>());
        Assert.IsType<Dictionary<string, object?>>(result);
        Assert.Equal(420L, ((Dictionary<string, object?>)result!)["max_speed"]);

        ((Dictionary<string, object?>)result!).Clear();
        var again = PathResolver.Resolve(BuildTree(), "player", new Dictionary<string, object?>());
        Assert.Equal(420L, ((Dictionary<string, object?>)again!)["max_speed"]);
    }

    [Fact]
    public void Resolve_StringNode_ReturnsNode()
    {
        Assert.Equal("str", PathResolver.Resolve(BuildTree(), "label", "d"));
    }

    [Fact]
    public void Resolve_BoolNode_ReturnsNode()
    {
        Assert.Equal(true, PathResolver.Resolve(BuildTree(), "flag", false));
    }

    [Fact]
    public void Resolve_StringNode_WithStringNameKind_ReturnsDefault()
    {
        // typeof(String) != typeof(StringName) → 回退默认（绑定壳经 kind 标签区分）
        Assert.Equal("d", PathResolver.Resolve(BuildTree(), "label", "d", ValueKind.StringName));
    }

    [Fact]
    public void Resolve_NullNode_WithNullDefault_ReturnsNull()
    {
        var tree = new Dictionary<string, object?> { ["k"] = null! };
        Assert.Null(PathResolver.Resolve(tree, "k", null));
    }

    [Fact]
    public void Resolve_MissingKey_WithNullDefault_ReturnsNull()
    {
        Assert.Null(PathResolver.Resolve(BuildTree(), "no.such", null));
    }

    [Fact]
    public void Resolve_EmptyPathSegment_MirrorsGdscriptSplit()
    {
        var tree = new Dictionary<string, object?> { ["a"] = new Dictionary<string, object?> { ["b"] = 1L } };
        // "a..b" → ["a", "", "b"]：空段无键 → 回退
        Assert.Equal(9L, PathResolver.Resolve(tree, "a..b", 9L));
    }

    [Fact]
    public void KindOf_MapsClrTypes()
    {
        Assert.Equal(ValueKind.Null, PathResolver.KindOf(null));
        Assert.Equal(ValueKind.Bool, PathResolver.KindOf(true));
        Assert.Equal(ValueKind.Int, PathResolver.KindOf(1L));
        Assert.Equal(ValueKind.Float, PathResolver.KindOf(1.5));
        Assert.Equal(ValueKind.String, PathResolver.KindOf("s"));
        Assert.Equal(ValueKind.Array, PathResolver.KindOf(new List<object?>()));
        Assert.Equal(ValueKind.Dictionary, PathResolver.KindOf(new Dictionary<string, object?>()));
    }
}
