using System;
using System.Collections.Generic;
using System.Linq;
using InfiAir.Core.Machines;
using Xunit;

namespace InfiAir.Core.Tests.Machines;

/// <summary>
/// 标题屏机型轮展袋的判据。这里抓的是「不报错、只让玩家觉得没换」的那种坏法：
/// 连出同型（洗牌或跨袋换位写错）、袋内重复（洗牌写成随机取值）、
/// 种子没吃进 RNG（每次开机顺序一样）——三者都不崩、不上日志，只有盯着标题屏看才发觉得到。
/// </summary>
public sealed class MachineBagTests
{
    private static readonly IReadOnlyList<MachineSpec> Roster = MachineRoster.All;

    [Fact]
    public void 一袋之内每一型各出现一次()
    {
        var bag = new MachineBag(Roster, seed: 20260919);
        var ids = new List<string>();
        for (var i = 0; i < MachineRoster.Count; i++)
        {
            ids.Add(bag.Next().Id);
        }

        Assert.Equal(MachineRoster.Count, ids.Count);
        Assert.Equal(MachineRoster.Count, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (var spec in Roster)
        {
            Assert.Contains(spec.Id, ids);
        }
    }

    [Fact]
    public void 连续抽取永不连出同型()
    {
        var bag = new MachineBag(Roster, seed: 7);
        var last = string.Empty;
        // 覆盖 5 袋以上：跨袋边界（第 6、12、18… 次）正是最容易连出同型的位置
        for (var i = 0; i < MachineRoster.Count * 5 + 3; i++)
        {
            var id = bag.Next().Id;
            Assert.NotEqual(last, id);
            last = id;
        }
    }

    [Fact]
    public void 名册含重复id时仍不连出同型()
    {
        // 手改配置把同一型列两遍：判据按 id 比对才不会把两架同型机当成两次不同的展示
        var dup = new List<MachineSpec> { MachineRoster.At(0), MachineRoster.At(0), MachineRoster.At(1) };
        var bag = new MachineBag(dup, seed: 3);
        var last = string.Empty;
        for (var i = 0; i < 24; i++)
        {
            var id = bag.Next().Id;
            Assert.NotEqual(last, id);
            last = id;
        }
    }

    [Fact]
    public void 同种子逐位一致_异种子不同()
    {
        var a = new MachineBag(Roster, seed: 12345);
        var b = new MachineBag(Roster, seed: 12345);
        for (var i = 0; i < 18; i++)
        {
            Assert.Equal(a.Next().Id, b.Next().Id);
        }

        var c = new MachineBag(Roster, seed: 999);
        var d = new MachineBag(Roster, seed: 12345);
        var differs = false;
        for (var i = 0; i < 18; i++)
        {
            if (!string.Equals(c.Next().Id, d.Next().Id, StringComparison.Ordinal))
            {
                differs = true;
                break;
            }
        }

        Assert.True(differs, "不同种子产出同一串展示顺序——种子没喂进洗牌");
    }

    [Fact]
    public void 空名册回退默认型不抛()
    {
        var bag = new MachineBag(Array.Empty<MachineSpec>(), seed: 1);
        Assert.Equal(MachineRoster.Default.Id, bag.Next().Id);
        Assert.Equal(MachineRoster.Default.Id, bag.Next().Id);
    }

    [Fact]
    public void 默认名册即机型名册()
    {
        var bag = new MachineBag(seed: 42);
        for (var i = 0; i < MachineRoster.Count * 2; i++)
        {
            Assert.Contains(bag.Next().Id, Roster.Select(s => s.Id));
        }
    }
}
