using InfiAir.Core.Hud;
using Xunit;

namespace InfiAir.Core.Tests.Hud;

/// <summary>准星档案簿：纯逻辑的增删改选（不可变，操作返回新簿）。删档活性索引的重绑与
/// 「至少保留 1 档」下限是静默坏点的常发处——删错一档后指向越界索引，绘制侧读崩。</summary>
public sealed class CrosshairBookTests
{
    private static CrosshairProfile P(byte r) => CrosshairProfile.Default with { R = r };

    private static CrosshairBook Make() => new(new[]
    {
        new CrosshairEntry("A", P(1)),
        new CrosshairEntry("B", P(2)),
        new CrosshairEntry("C", P(3)),
    }, 1);

    [Fact]
    public void EmptyInput_FallsBackToDefaultProfile()
    {
        var book = new CrosshairBook(Array.Empty<CrosshairEntry>(), 5);
        var entry = Assert.Single(book.Entries);
        Assert.Equal(CrosshairProfile.Default, entry.Profile);
        Assert.Equal(0, book.ActiveIndex);
    }

    [Fact]
    public void ActiveIndex_OutOfRange_FallsToZero()
    {
        Assert.Equal(0, new CrosshairBook(new[] { new CrosshairEntry("A", P(1)) }, 7).ActiveIndex);
        Assert.Equal(0, new CrosshairBook(new[] { new CrosshairEntry("A", P(1)) }, -1).ActiveIndex);
    }

    [Fact]
    public void Replace_UpdatesEntryAtOnlyThatIndex()
    {
        var book = Make().WithProfileReplaced(2, P(9));
        Assert.Equal(P(2), book.Entries[1].Profile);
        Assert.Equal(P(9), book.Entries[2].Profile);
        Assert.Equal(1, book.ActiveIndex);
    }

    [Fact]
    public void Replace_OutOfRange_IsNoop()
    {
        var book = Make();
        Assert.Equal(book, book.WithProfileReplaced(-1, P(9)));
        Assert.Equal(book, book.WithProfileReplaced(3, P(9)));
    }

    [Fact]
    public void Add_AppendsAndActivatesNewEntry()
    {
        var book = Make().WithAdded("D", P(4));
        Assert.Equal(4, book.Entries.Count);
        Assert.Equal(3, book.ActiveIndex);
        Assert.Equal("D", book.Entries[3].Name);
    }

    [Fact]
    public void Remove_ShiftsActive_WhenBeforeActive()
    {
        var book = Make().WithRemoved(0);
        Assert.Equal(2, book.Entries.Count);
        Assert.Equal(0, book.ActiveIndex);
        Assert.Equal("B", book.Entries[book.ActiveIndex].Name);
    }

    [Fact]
    public void Remove_Active_ClampsIntoRange()
    {
        var book = Make().WithRemoved(1);
        Assert.Equal(2, book.Entries.Count);
        Assert.Equal(1, book.ActiveIndex);
        Assert.Equal("C", book.Entries[book.ActiveIndex].Name);
    }

    [Fact]
    public void Remove_LastProfile_IsRefused()
    {
        var single = new CrosshairBook(new[] { new CrosshairEntry("A", P(1)) }, 0);
        Assert.Equal(single, single.WithRemoved(0));
        var book = Make();
        Assert.Equal(book, book.WithRemoved(-1));
        Assert.Equal(book, book.WithRemoved(3));
    }

    [Fact]
    public void Rename_KeepsEverythingElse()
    {
        var book = Make().WithRenamed(0, "X");
        Assert.Equal("X", book.Entries[0].Name);
        Assert.Equal(P(1), book.Entries[0].Profile);
        Assert.Equal(1, book.ActiveIndex);
    }
}
