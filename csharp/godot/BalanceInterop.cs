using Godot;
using InfiAir.Core;
// ImplicitUsings 引入 System.IO 后与 Godot.FileAccess 歧义（CS0104），显式别名消解
using FileAccess = Godot.FileAccess;

namespace InfiAir;

/// <summary>
/// GDScript → C# 互操作演示端点（混编样板）：薄 Godot 绑定壳 + InfiAir.Core 纯逻辑。
/// GDScript 侧通过 load("res://csharp/godot/BalanceInterop.cs").new() 调用，
/// 验证跨语言路径（GDScript → C# 绑定层 → 纯 .NET 核心库）与 test/csharp_interop_test.gd 断言场景。
/// 不接入任何生产运行时链路（GameState.cfg() 读取保持不变）。
/// </summary>
public partial class BalanceInterop : RefCounted
{
    /// <summary>解析 data/balance.json 并返回类型化抽查结果（Dictionary 桥接，避开跨语言 out 参数）。</summary>
    public Godot.Collections.Dictionary ParseBalance(string jsonPath)
    {
        var result = new Godot.Collections.Dictionary();
        var file = FileAccess.Open(jsonPath, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            result["ok"] = false;
            result["error"] = "cannot open " + jsonPath;
            return result;
        }

        var root = BalanceRoot.Load(file.GetAsText(), out var error);
        result["ok"] = root is not null;
        result["error"] = error ?? "";
        if (root is not null)
        {
            result["version"] = root.Version;
            result["max_speed"] = root.Player?.MaxSpeed ?? -1;
            result["mag_cells"] = root.Mothership?.MagCells ?? -1;
        }

        return result;
    }
}
