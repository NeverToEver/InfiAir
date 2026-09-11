using Godot;

namespace InfiAir;

/// <summary>
/// 机体能量发光层（GlowLayer）装配助手：ship_energy.gdshader 参数写入 +
/// 遮罩贴图派生（主贴图资源路径 ".png" → "_glow.png"）。
/// GlowLayer 为主 Sprite2D 的子节点（tscn 挂载，变换严格随主贴图），运行期只写
/// tint/intensity/流动三频与遮罩贴图——纯视觉叠加层，玩法判定零改动。
/// 静态类不持有 Godot 对象（退出 segfault 铁律）；流动三频为标量懒缓存（同一配置值重复写无副作用）。
/// </summary>
public static class ShipEnergyFx
{
    /// <summary>GlowLayer 节点名（tscn 挂载与运行期查找的统一口径）。</summary>
    public const string NodeName = "GlowLayer";

    private static readonly StringName PFlowSpeed = new("flow_speed");
    private static readonly StringName PPulseHz = new("pulse_hz");
    private static readonly StringName PBreathHz = new("breath_hz");
    private static readonly StringName PIntensity = new("intensity");
    private static readonly StringName PTint = new("tint");

    // 流动三频懒缓存（-1 哨兵；effects.ship_energy.* 首次读取后复用）
    private static float _flowSpeed = -1.0f;
    private static float _pulseHz = -1.0f;
    private static float _breathHz = -1.0f;

    /// <summary>写入能量层参数：阵营 tint + 强度 + 流动三频（ glowLayer 缺节点/缺材质时静默跳过——
    /// 纯装饰层缺失不构成玩法故障）。</summary>
    public static void Apply(Sprite2D? glowLayer, Color tint, float intensity)
    {
        if (glowLayer?.Material is not ShaderMaterial mat)
        {
            return;
        }

        if (_flowSpeed < 0.0f)
        {
            _flowSpeed = CfgFx.Float("effects.ship_energy.flow_speed", 0.55f, 0.0f);
            _pulseHz = CfgFx.Float("effects.ship_energy.pulse_hz", 0.8f, 0.0f);
            _breathHz = CfgFx.Float("effects.ship_energy.breath_hz", 1.7f, 0.0f);
        }

        mat.SetShaderParameter(PTint, tint);
        mat.SetShaderParameter(PIntensity, intensity);
        mat.SetShaderParameter(PFlowSpeed, _flowSpeed);
        mat.SetShaderParameter(PPulseHz, _pulseHz);
        mat.SetShaderParameter(PBreathHz, _breathHz);
    }

    /// <summary>按主贴图资源路径派生能量遮罩（"_glow.png" 后缀）；
    /// 路径缺失/遮罩不存在返回 null（调用方保持默认遮罩，不覆盖）。</summary>
    public static Texture2D? GlowTextureFor(Texture2D? baseTexture)
    {
        var path = baseTexture?.ResourcePath;
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".png", System.StringComparison.Ordinal))
        {
            return null;
        }

        var glowPath = path[..^4] + "_glow.png";
        return ResourceLoader.Exists(glowPath) ? GD.Load<Texture2D>(glowPath) : null;
    }

    /// <summary>颜色配置读取：支持 "#rrggbb" 字符串形式（JSON 无 Color 类型）。非法值回退默认。</summary>
    public static Color CfgColor(string key, Color def)
    {
        var v = GameState.Instance.Cfg(key, def);
        if (v.VariantType == Variant.Type.Color)
        {
            return v.AsColor();
        }

        if (v.VariantType == Variant.Type.String)
        {
            var s = v.AsString();
            if (!string.IsNullOrEmpty(s) && Color.HtmlIsValid(s))
            {
                return Color.FromHtml(s);
            }
        }

        return def;
    }
}
