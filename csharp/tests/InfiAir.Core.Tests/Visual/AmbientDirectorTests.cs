using System;
using System.Collections.Generic;
using InfiAir.Core.Visual;
using Xunit;

namespace InfiAir.Core.Tests.Visual;

/// <summary>
/// 标题屏远景演出抽取器的判据。抓的是三种「不报错、只是背景变难看」的坏法：
/// 冷却失效（稀有演出连发，背景抢戏）、权重失效（每类等概率，战场节奏读不出疏密）、
/// 全冷却时死循环或抛错（标题屏开天窗）。另加种子可复现与非法表收口两条护栏。
/// </summary>
public sealed class AmbientDirectorTests
{
    private static readonly AmbientAct[] Acts =
    {
        new("formation", 3.0, 4.0),
        new("strike", 2.5, 3.0),
        new("boss", 0.6, 24.0),
    };

    /// <summary>按种子跑一段抽取，返回 (id, 发生时刻) 时间线——与生产调用同一条路径：
    /// 时刻由 <see cref="AmbientDirector.Gap"/> 累加，不另喂一套时间。</summary>
    private static List<(string Id, double At)> Run(AmbientDirector director, int count)
    {
        var timeline = new List<(string, double)>();
        var now = 0.0;
        for (var i = 0; i < count; i++)
        {
            var id = director.Next(now);
            timeline.Add((id, now));
            now += director.Gap();
        }

        return timeline;
    }

    [Fact]
    public void 权重为零的类从不被抽中()
    {
        var director = new AmbientDirector(
            new[] { new AmbientAct("on", 1.0, 0.0), new AmbientAct("off", 0.0, 0.0) },
            0.5,
            1.0,
            seed: 5150);
        var timeline = Run(director, 200);
        Assert.DoesNotContain(timeline, t => t.Id == "off");
        Assert.All(timeline, t => Assert.Equal("on", t.Id));
    }

    [Fact]
    public void 同类两次抽取至少隔一个冷却()
    {
        // filler 冷却 0 → 任意时刻都有就绪候选，「全在冷却」的兜底不会介入，
        // 于是冷却是一条硬保证（生产表里各类都有冷却，兜底只在该它出手时才出手）
        var director = new AmbientDirector(
            new[]
            {
                new AmbientAct("rare", 1.0, 30.0),
                new AmbientAct("mid", 1.0, 10.0),
                new AmbientAct("filler", 1.0, 0.0),
            },
            0.5,
            1.2,
            seed: 20260919);
        var timeline = Run(director, 300);
        var lastAt = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, at) in timeline)
        {
            if (lastAt.TryGetValue(id, out var prev))
            {
                var cooldown = id switch { "rare" => 30.0, "mid" => 10.0, _ => 0.0 };
                Assert.True(at - prev >= cooldown - 1e-9,
                    $"{id} 在冷却内被连抽：{prev:F3}s → {at:F3}s（冷却 {cooldown}s）");
            }

            lastAt[id] = at;
        }

        Assert.Contains(timeline, t => t.Id == "rare"); // 稀有类不是永不出现
    }

    [Fact]
    public void 全在冷却时取最早就绪者不抛不挂()
    {
        var director = new AmbientDirector(
            new[] { new AmbientAct("a", 1.0, 100.0), new AmbientAct("b", 1.0, 100.0) },
            1.0,
            1.0,
            seed: 8);
        var first = director.Next(0.0);
        var second = director.Next(1.0); // 两者都远未就绪 → 兜底：取最早就绪的那一个
        Assert.NotEqual(first, second);
        Assert.Contains(first, new[] { "a", "b" });
        Assert.Contains(second, new[] { "a", "b" });

        // 连续兜底抽取不得挂死、不得返回空串（表非空）
        for (var i = 0; i < 50; i++)
        {
            Assert.NotEqual(string.Empty, director.Next(i * 0.1));
        }
    }

    [Fact]
    public void 权重高的类明显更常出场()
    {
        // 与等权重表对比读数：单看一张表的比例不够——「不连出同类」本身就会压平分布
        // （两类 9:1 时它退化成强制交替，出场次数变成 1:1），比值必须相对同一张表的等权版本读
        var weighted = new AmbientDirector(
            new[]
            {
                new AmbientAct("heavy", 9.0, 0.0),
                new AmbientAct("mid", 1.0, 0.0),
                new AmbientAct("low", 1.0, 0.0),
            },
            0.5,
            1.0,
            seed: 4096);
        var flat = new AmbientDirector(
            new[]
            {
                new AmbientAct("heavy", 1.0, 0.0),
                new AmbientAct("mid", 1.0, 0.0),
                new AmbientAct("low", 1.0, 0.0),
            },
            0.5,
            1.0,
            seed: 4096);
        var weightedShare = ShareOf(Run(weighted, 400), "heavy");
        var flatShare = ShareOf(Run(flat, 400), "heavy");
        // 门槛只取 +10 个百分点：9:1 的权重不可能兑成 9:1 的出场比——「不连出同类」把上限
        // 压在「上一次抽中 heavy 时这一轮轮不到它」之下，实际差值是 13.5 个百分点（实测）
        Assert.True(weightedShare > flatShare + 0.10,
            $"权重没有体现在出场频次上：加权表 {weightedShare:P} vs 等权表 {flatShare:P}");
    }

    private static double ShareOf(List<(string Id, double At)> timeline, string id)
    {
        var hits = 0;
        foreach (var (actId, _) in timeline)
        {
            if (string.Equals(actId, id, StringComparison.Ordinal))
            {
                hits++;
            }
        }

        return (double)hits / timeline.Count;
    }

    [Fact]
    public void 不连出同类()
    {
        var director = new AmbientDirector(
            new[]
            {
                new AmbientAct("a", 1.0, 0.0),
                new AmbientAct("b", 1.0, 0.0),
                new AmbientAct("c", 1.0, 0.0),
            },
            0.5,
            1.0,
            seed: 6);
        var timeline = Run(director, 60);
        for (var i = 1; i < timeline.Count; i++)
        {
            Assert.NotEqual(timeline[i - 1].Id, timeline[i].Id);
        }
    }

    [Fact]
    public void 间隔落在区间内_非法区间收口()
    {
        var director = new AmbientDirector(Acts, 3.2, 6.4, seed: 11);
        for (var i = 0; i < 200; i++)
        {
            var gap = director.Gap();
            Assert.InRange(gap, 3.2, 6.4);
        }

        // 表写反（max < min）与负值都不该让调用方等到负数或空转
        var reversed = new AmbientDirector(Acts, 5.0, 1.0, seed: 11);
        Assert.Equal(5.0, reversed.Gap());
        var negative = new AmbientDirector(Acts, -2.0, -1.0, seed: 11);
        Assert.Equal(0.0, negative.Gap());
    }

    [Fact]
    public void 同种子同序列()
    {
        var a = Run(new AmbientDirector(Acts, 3.2, 6.4, seed: 20260101), 24);
        var b = Run(new AmbientDirector(Acts, 3.2, 6.4, seed: 20260101), 24);
        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Id, b[i].Id);
            Assert.Equal(a[i].At, b[i].At, 9);
        }
    }

    [Fact]
    public void 空表返回空串不抛()
    {
        var director = new AmbientDirector(Array.Empty<AmbientAct>(), 1.0, 2.0, seed: 1);
        Assert.Equal(0, director.Count);
        Assert.Equal(string.Empty, director.Next(0.0));
        Assert.InRange(director.Gap(), 1.0, 2.0);
    }
}
