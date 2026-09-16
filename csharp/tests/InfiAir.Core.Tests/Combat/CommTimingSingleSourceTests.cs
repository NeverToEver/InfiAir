using System;
using System.IO;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>通讯节奏常量的单源判据（读源码文本的结构性判定，不改运行态）。
/// 停留时长与打字机字间隔的单源在 core FormationComms，引擎侧 CommOverlay 必须引用它们：
/// 编队侧用这两个量推算「战术提示最早可播时刻」（新提示顶掉仍在播的进度台词，正是该组常量
/// 要防的），副本一旦分叉，只调观感（纯引擎侧改动）就会让 core 的推算失真——而两侧取值相等时
/// 运行期无法分辨谁是副本，故只能判「不留副本」这一结构事实。</summary>
public sealed class CommTimingSingleSourceTests
{
    [Fact]
    public void CommOverlay_ReferencesCoreTimingConstantsWithoutLocalCopies()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "csharp", "godot", "CommOverlay.cs"));
        Assert.Contains("FormationComms.HoldTime", src, StringComparison.Ordinal);
        Assert.Contains("FormationComms.CharInterval", src, StringComparison.Ordinal);
        // 副本形态：再声明一次同名常量（改一侧不改另一侧时两侧都是「正常」字面量，判不出来）
        Assert.DoesNotContain("const float HoldTime", src, StringComparison.Ordinal);
        Assert.DoesNotContain("const float CharInterval", src, StringComparison.Ordinal);
    }

    /// <summary>仓库根（含 InfiAir.sln）：测试输出目录逐级上溯，不依赖当前工作目录。</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "InfiAir.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("未找到仓库根（InfiAir.sln）：单源判定取不到判据，显式失败");
    }
}
