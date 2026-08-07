using Godot;
using InfiAir.Core.Config;

namespace InfiAir;

/// <summary>
/// P1-1 绑定壳：BalanceService.cfg() 点路径解析核心的 C# 桥（InfiAir.Core.Config.PathResolver 纯函数）。
/// GDScript 侧（scripts/balance_service.gd）load() 时 SetData 一次、cfg() 转发 Resolve——
/// 469 处调用点签名不变；BALANCE_MAP 生成器（M8）零影响。
/// </summary>
public partial class PathResolverInterop : RefCounted
{
    private Dictionary<string, object?> _tree = new();

    /// <summary>装载配置树（GDScript 解析 JSON 后的 Dictionary；损坏/缺失时为空字典）。</summary>
    public void SetData(Godot.Collections.Dictionary data)
    {
        _tree = VariantBridge.TryToClr(data, out var clr, out _)
                && clr is Dictionary<string, object?> d
            ? d
            : new Dictionary<string, object?>();
    }

    /// <summary>点路径解析（语义见 PathResolver：缺键/类型不符回退 default；数值宽容；容器拷贝）。</summary>
    public Variant Resolve(string path, Variant defaultValue)
    {
        var kind = VariantBridge.KindOf(defaultValue);
        if (!VariantBridge.TryToClr(defaultValue, out var clr, out _))
        {
            return defaultValue;
        }

        return VariantBridge.ToVariant(PathResolver.Resolve(_tree, path, clr, kind));
    }
}
