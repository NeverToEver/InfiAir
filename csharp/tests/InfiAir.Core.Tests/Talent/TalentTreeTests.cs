using InfiAir.Core.Talent;
using Xunit;

namespace InfiAir.Core.Tests.Talent;

/// <summary>TalentTree 结构不变式测试：id 唯一、前置链无环且按声明序、
/// 互斥对与路线绑定的节点真实存在。结构是静态声明——写坏的表现是
/// 「天赋前置漏判 / 路线绑到不存在的大类」，编译与冒烟都不报警。</summary>
public sealed class TalentTreeTests
{
    [Fact]
    public void NodeIds_AllUnique()
    {
        var ids = TalentTree.NodeIds();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.NotEmpty(ids);
    }

    [Fact]
    public void NodeIds_DeclarationOrder_CategoryThenLineThenIndex()
    {
        var ids = TalentTree.NodeIds();
        Assert.Equal("power_shot", ids[0]);
        Assert.Equal("combo_guard", ids[^1]);
    }

    [Fact]
    public void CategoryAndLine_IdsUnique()
    {
        Assert.Equal(TalentTree.Categories.Count, TalentTree.Categories.Select(c => c.Id).Distinct().Count());
        var lineIds = TalentTree.Categories.SelectMany(c => c.Lines).Select(l => l.Id).ToList();
        Assert.Equal(lineIds.Count, lineIds.Distinct().Count());
    }

    [Fact]
    public void PrerequisiteChains_AcyclicAndFollowDeclarationOrder()
    {
        foreach (var id in TalentTree.NodeIds())
        {
            var def = TalentTree.Find(id)!;
            var line = TalentTree.Line(def.LineId);
            Assert.Equal(def.Id, line.NodeIds[def.Index]);

            // 沿前置链走：每步 Index 严格递减 1，必然终止于根节点——不可能成环
            var steps = 0;
            var current = id;
            while (TalentTree.Prerequisite(current) is { } prev)
            {
                Assert.Equal(TalentTree.Find(current)!.Index - 1, TalentTree.Find(prev)!.Index);
                current = prev;
                Assert.True(++steps <= line.NodeIds.Count, $"prerequisite cycle at {id}");
            }

            Assert.Equal(0, TalentTree.Find(current)!.Index);
        }
    }

    [Fact]
    public void Prerequisite_RootAndUnknown_ReturnNull()
    {
        Assert.Null(TalentTree.Prerequisite("power_shot"));
        Assert.Null(TalentTree.Prerequisite("no_such_node"));
    }

    [Fact]
    public void Prerequisite_MidLine_ReturnsPreviousNode()
    {
        Assert.Equal("power_shot", TalentTree.Prerequisite("bullet_speed"));
        Assert.Equal("regen", TalentTree.Prerequisite("lifesteal"));
    }

    [Fact]
    public void MutexPairs_ReferenceExistingDistinctCategories()
    {
        foreach (var (a, b) in TalentTree.MutexPairs)
        {
            Assert.NotEqual(a, b);
            Assert.NotNull(TalentTree.Category(a));
            Assert.NotNull(TalentTree.Category(b));
        }
    }

    [Fact]
    public void Routes_ReferenceExistingCategories()
    {
        Assert.Equal(TalentTree.Routes.Count, TalentTree.Routes.Select(r => r.Id).Distinct().Count());
        foreach (var route in TalentTree.Routes)
        {
            Assert.NotNull(TalentTree.Category(route.CoreCategoryId));
        }
    }

    [Fact]
    public void Routes_SpecialCategoryHasNoRoute()
    {
        // 机制 C 既定口径：special 无对应路线，始终属「非路线」侧
        Assert.DoesNotContain(TalentTree.Routes, r => r.CoreCategoryId == "special");
    }

    [Fact]
    public void Find_ReturnsStructuralAttributes()
    {
        var def = TalentTree.Find("homing")!;
        Assert.Equal("offense", def.CategoryId);
        Assert.Equal("gunnery", def.LineId);
        Assert.Equal(3, def.Index);
        Assert.Null(TalentTree.Find("no_such_node"));
    }

    [Fact]
    public void CategoryAndLine_UnknownId_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => TalentTree.Category("no_such_cat"));
        Assert.Throws<KeyNotFoundException>(() => TalentTree.Line("no_such_line"));
    }

    [Fact]
    public void OpposingCategoryOf_MutexPairResolvesBothWays()
    {
        Assert.Equal("defense", TalentTree.OpposingCategoryOf("offense"));
        Assert.Equal("offense", TalentTree.OpposingCategoryOf("defense"));
        Assert.Null(TalentTree.OpposingCategoryOf("mobility"));
        Assert.Null(TalentTree.OpposingCategoryOf("special"));
    }

    [Fact]
    public void LocalizationKeys_AllPresent()
    {
        // 键存在性由 check_ui_copy.sh 判玩家侧；这里只钉「结构声明没有空键」
        foreach (var cat in TalentTree.Categories)
        {
            Assert.False(string.IsNullOrWhiteSpace(cat.NameKey));
            foreach (var line in cat.Lines)
            {
                Assert.False(string.IsNullOrWhiteSpace(line.NameKey));
            }
        }

        foreach (var route in TalentTree.Routes)
        {
            Assert.False(string.IsNullOrWhiteSpace(route.NameKey));
        }
    }
}
