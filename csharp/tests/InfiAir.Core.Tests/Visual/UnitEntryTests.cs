using System.Text.Json;
using InfiAir.Core.Tests;
using InfiAir.Core.Visual;
using Xunit;

namespace InfiAir.Core.Tests.Visual;

/// <summary>入场落位缓动契约测试（B3「敌机入场落位」）：进度必须起 0、终 1、单调不减。
/// 守的静默错误＝异常帧长把单位永久钉在入场姿态（机体一直偏小偏高）：入场是纯外观，
/// 落在终态与落在起点在画面上都「不报错」，只能由算式两侧的边界钉住。</summary>
public sealed class UnitEntryTests
{
    [Fact]
    public void Placement01_StartsAtZeroEndsAtOne()
    {
        const float Duration = 0.3f;
        Assert.Equal(0.0f, UnitEntry.Placement01(0.0f, Duration));
        Assert.Equal(1.0f, UnitEntry.Placement01(Duration, Duration));
        // 过冲（一帧跨过整段）与继续推进都停在 1，不会回到 0 或越界
        Assert.Equal(1.0f, UnitEntry.Placement01(Duration * 4.0f, Duration));
        Assert.Equal(0.0f, UnitEntry.Placement01(-0.1f, Duration));
    }

    [Fact]
    public void Placement01_IsMonotonicAndEaseOut()
    {
        const float Duration = 0.3f;
        var prev = -1.0f;
        for (var i = 0; i <= 30; i++)
        {
            var v = UnitEntry.Placement01(Duration * i / 30.0f, Duration);
            Assert.True(v >= prev, $"进度必须单调不减：t={i}/30 处 {v} < 上一次 {prev}");
            Assert.InRange(v, 0.0f, 1.0f);
            prev = v;
        }

        // ease-out：前半段走完的行程多于一半（线性＝一半，ease-in 则少于一半），
        // 写反成 ease-in 时终帧速度非零、机体是「砸」下来的，观感退化无任何自动判据
        Assert.True(UnitEntry.Placement01(Duration * 0.5f, Duration) > 0.5f);
    }

    [Fact]
    public void Placement01_NonFiniteOrZeroDurationLandsImmediately()
    {
        // 异常 delta / 零时长配置：落到落位态，不是永久停在入场姿态（0 才是那个坏点）
        Assert.Equal(1.0f, UnitEntry.Placement01(float.NaN, 0.3f));
        Assert.Equal(1.0f, UnitEntry.Placement01(0.1f, float.NaN));
        Assert.Equal(1.0f, UnitEntry.Placement01(0.0f, 0.0f));
        Assert.Equal(1.0f, UnitEntry.Placement01(0.1f, -1.0f));
    }

    [Fact]
    public void Balance_EntryStaysInSpecWindow()
    {
        using var doc = JsonDocument.Parse(RepoFiles.Read("data/balance.json"));
        var motion = doc.RootElement.GetProperty("effects").GetProperty("motion");

        // B3 规格硬线：入场 250–400ms。越界即从「落位」变成可见的拖沓/瞬移，
        // 而两者都不崩不报错（观感面没有自动判据）
        var entry = motion.GetProperty("enemy_entry_time").GetDouble();
        Assert.InRange(entry, 0.25, 0.4);

        // 编队相位间隔：>0（0 等于没有相位波）且小于入场时长（大于整段会让末位机体明显掉队）
        var phase = motion.GetProperty("formation_phase_delay").GetDouble();
        Assert.True(phase > 0.0 && phase < entry, $"编队相位间隔 {phase} 必须落在 (0, {entry}) 内");
    }
}
