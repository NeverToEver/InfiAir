using System;
using System.Text.Json;
using InfiAir.Core;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>FlashBudget 契约测试：全屏尺度闪烁的频率与「减少闪光」处置只有一份登记表。
/// 守的静默错误＝`DESIGN_BASELINE` §1.9.1 的「闪烁频率低于 WCAG 2.3.1 阈值」只是散文声明：
/// 改动前改一行 `Hud.FuelPulseHz` 即超阈值，而门禁、单测、探针全绿。
/// 两半互补：只判频率会让「一律照闪」混过，只判归零会让「一律不闪」混过。</summary>
public sealed class FlashBudgetTests
{
    [Fact]
    public void Sources_RowOrderMatchesPulseIdEnum()
    {
        // 行序即枚举序：错位时 Source(id) 会取到别人的频率，而调用点看到的仍是「合法值」
        Assert.Equal(Enum.GetValues<PulseId>().Length, FlashBudget.Sources.Length);
        for (var i = 0; i < FlashBudget.Sources.Length; i++)
        {
            Assert.Equal((PulseId)i, FlashBudget.Sources[i].Id);
            Assert.Equal(FlashBudget.Sources[i].Hz, FlashBudget.Source((PulseId)i).Hz, 5);
        }
    }

    [Fact]
    public void FullScreenPulses_AreWithinFlashLimit()
    {
        // 上半：全屏尺度项一律低于阈值（表内自检，不依赖任何外部来源）
        foreach (var src in FlashBudget.Sources)
        {
            Assert.True(
                src.Hz < FlashBudget.HzLimit,
                $"{src.Id} 的 {src.Hz:0.###}Hz 越过全屏尺度闪烁阈值 {FlashBudget.HzLimit}Hz（XAG 118）");
        }

        Assert.Equal(3.0f, FlashBudget.HzLimit, 5);
    }

    [Fact]
    public void BalanceBackedRows_MatchShippedBalanceValues()
    {
        // 下半的取值面：来源是 balance 的行必须与随包数据一致——否则改 balance 就能把
        // 全屏脉冲推过阈值而单测照样绿（表与数据各说各话时，运行期无法分辨谁是权威）
        using var doc = JsonDocument.Parse(RepoFiles.Read("data/balance.json"));
        var checkedRows = 0;
        foreach (var src in FlashBudget.Sources)
        {
            if (src.Origin != "balance")
            {
                continue;
            }

            Assert.False(string.IsNullOrEmpty(src.BalanceKey), $"{src.Id} 标了 balance 来源却没有键路径");
            var node = doc.RootElement;
            foreach (var segment in src.BalanceKey.Split('.'))
            {
                Assert.True(node.TryGetProperty(segment, out node), $"{src.Id} 的键 {src.BalanceKey} 在 balance.json 里不存在");
            }

            var value = node.GetDouble();
            var hz = src.BalanceIsPeriod ? 1.0 / value : value;
            Assert.Equal(src.Hz, (float)hz, 4);
            checkedRows++;
        }

        // 一条都没有＝键路径写法漂移（判据退化成空转），显式失败
        Assert.True(checkedRows > 0, "没有任何 balance 来源的行被核对——键路径漂移？");
    }

    [Fact]
    public void ReduceFlash_ZeroesEveryRegisteredPulse()
    {
        // 两半互补的另半边：减少闪光下振幅为零，且非减少闪光时原样返回
        // （后半句同样要判——helper 一律返回 0 的实现会让「一律不闪」平凡通过）
        foreach (var src in FlashBudget.Sources)
        {
            Assert.True(src.ZeroUnderReduceFlash, $"{src.Id} 登记为减少闪光下不归零——加例外须同时改本判据与理由");
            Assert.Equal(0.0f, FlashBudget.Amplitude(1.0f, src.Id, reduceFlash: true));
            Assert.Equal(0.5f, FlashBudget.Amplitude(0.5f, src.Id, reduceFlash: false));
        }
    }

    [Fact]
    public void OneShots_RowOrderMatchesEnum_AndGateHasBothHalves()
    {
        // 一次性闪光无频率可言（不进频率表），门控同样单源在这里：行序错位时 AllowsOneShot
        // 会读到别人的处置，而调用点看到的仍是「合法值」
        Assert.Equal(Enum.GetValues<OneShotFlashId>().Length, FlashBudget.OneShots.Length);
        for (var i = 0; i < FlashBudget.OneShots.Length; i++)
        {
            Assert.Equal((OneShotFlashId)i, FlashBudget.OneShots[i].Id);

            var id = (OneShotFlashId)i;
            Assert.True(
                FlashBudget.OneShots[i].SuppressedByReduceFlash,
                $"{id} 登记为减闪下不抑制——加例外须同时改本判据与理由");
            Assert.False(FlashBudget.AllowsOneShot(id, reduceFlash: true));
            // 非减闪那半同样要判：一律返回 false 的实现会让「减少闪光下仍闪一下」退化成
            // 「什么都不闪」——玩家没开减闪时本该看到这些确认反馈
            Assert.True(FlashBudget.AllowsOneShot(id, reduceFlash: false));
        }
    }
}
