using System;
using InfiAir.Core.Talent;
using Xunit;

namespace InfiAir.Core.Tests.Talent;

/// <summary>
/// 加点放行的失败关闭契约。钉住的是旧实现「UI 以 else 兜底＝可升级」造成的静默形态：
/// 未知/未登记原因让升级按钮亮起，点下去被判定侧拦下——按钮可点、无响应、无提示。
/// </summary>
public sealed class TalentUpgradeGateTests
{
    [Fact]
    public void OnlyNone_IsUpgradeable()
    {
        Assert.True(TalentUpgradeGate.UpgradeEnabled(TalentUpgradeBlock.None));

        // 遍历全部取值：新增原因若被放行，本用例立刻红（新原因必须显式分类）
        foreach (var reason in Enum.GetValues<TalentUpgradeBlock>())
        {
            if (reason == TalentUpgradeBlock.None)
            {
                continue;
            }

            Assert.False(TalentUpgradeGate.UpgradeEnabled(reason), $"{reason} 必须失败关闭");
        }
    }

    [Fact]
    public void UnknownNode_IsFailClosed()
    {
        // 判别用例：节点 id 不在树里时旧 UI 走「可升级」兜底分支
        Assert.False(TalentUpgradeGate.UpgradeEnabled(TalentUpgradeBlock.Unknown));
    }
}
