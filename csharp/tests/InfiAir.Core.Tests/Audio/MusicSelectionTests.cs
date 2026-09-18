using System;
using System.Collections.Generic;
using System.IO;
using InfiAir.Core.Audio;
using Xunit;

namespace InfiAir.Core.Tests.Audio;

/// <summary>曲目选择判定测试。钉住三件事：
/// ① 三档映射的优先级（基地休整优先于 Boss——返航刻意保留 Boss，反了就是「带 Boss 回基地后
/// 基地曲目整段不播」）；② 越界 / 缺省一律落默认曲目（映射写漏一档时不得静默无声）；
/// ③ 三条资源名两两不同——映射塌陷（两档返回同一首）在运行期只表现为「某个时段音乐没变」，
/// 判定本身却照常通过。</summary>
public sealed class MusicSelectionTests
{
    [Fact]
    public void Select_PlainFight_IsDefault()
    {
        Assert.Equal(MusicCue.Default, MusicSelection.Select(false, false));
    }

    [Fact]
    public void Select_BossEngaged_IsBoss()
    {
        Assert.Equal(MusicCue.Boss, MusicSelection.Select(true, false));
    }

    [Fact]
    public void Select_AtBase_IsBase()
    {
        Assert.Equal(MusicCue.Base, MusicSelection.Select(false, true));
    }

    /// <summary>返航保留 Boss：两者同时成立时基地曲目接管（Boss 冻在场上，休整时段归基地）。</summary>
    [Fact]
    public void Select_AtBaseWithLiveBoss_IsBase()
    {
        Assert.Equal(MusicCue.Base, MusicSelection.Select(true, true));
    }

    [Fact]
    public void TrackName_MapsEachCueToItsOwnTrack()
    {
        Assert.Equal("bgm_loop", MusicSelection.TrackName(MusicCue.Default));
        Assert.Equal("bgm_boss", MusicSelection.TrackName(MusicCue.Boss));
        Assert.Equal("bgm_base", MusicSelection.TrackName(MusicCue.Base));
        var names = new HashSet<string>
        {
            MusicSelection.TrackName(MusicCue.Default),
            MusicSelection.TrackName(MusicCue.Boss),
            MusicSelection.TrackName(MusicCue.Base),
        };
        Assert.Equal(3, names.Count);
    }

    /// <summary>越界档位（枚举被强转 / 跨版本读入）回落默认曲目：不得抛异常、不得返回空串。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void TrackName_UnknownCue_FallsBackToDefault(int raw)
    {
        Assert.Equal(MusicSelection.TrackName(MusicCue.Default), MusicSelection.TrackName((MusicCue)raw));
    }

    /// <summary>档位映射出的曲目必须真有产物：core 只给名，产物在 assets/audio（生成器
    /// scripts/tools/generate_audio.py 产出、随仓库入库）。改名不同步时运行期只打一条加载告警、
    /// 该时段静默无声，编译 / 单测 / 冒烟都无信号——这里按名取文件，判「映射与产物同一份事实」。</summary>
    [Theory]
    [InlineData(MusicCue.Default)]
    [InlineData(MusicCue.Boss)]
    [InlineData(MusicCue.Base)]
    public void TrackName_HasShippedAsset(MusicCue cue)
    {
        var path = $"assets/audio/{MusicSelection.TrackName(cue)}.wav";
        Assert.True(File.Exists(RepoFiles.PathOf(path)), $"曲目产物缺失：{path}");
    }
}
