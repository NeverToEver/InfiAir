using System;
using System.IO;

namespace InfiAir.Core.Tests;

/// <summary>读仓库内源文件的统一入口（结构性判定的取源）：从测试输出目录逐级上溯到含
/// InfiAir.sln 的仓库根，不依赖当前工作目录。取不到即抛——取不到判据必须显式失败，
/// 不得静默跳过（AGENTS §6 铁律 2）。</summary>
public static class RepoFiles
{
    public static string Read(string relativePath) => File.ReadAllText(PathOf(relativePath));

    /// <summary>仓库内相对路径 → 绝对路径（「产物文件真的在」这类存在性事实的取源）。</summary>
    public static string PathOf(string relativePath) => Path.Combine(Root(), relativePath);

    private static string Root()
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

        throw new InvalidOperationException("未找到仓库根（InfiAir.sln）：结构性判定取不到源文件，显式失败");
    }
}
