using Godot;

namespace InfiAir;

/// <summary>
/// 世界层画面增强层（战术琥珀视觉升级核心）：全屏 ColorRect + world_grade.gdshader，
/// 承载亮部柔光辉光、暖调色彩分级、晕影与胶片颗粒。
/// layer=1、世界之上 / HUD 之下；由 Main 在 MetaHealthFX 之前创建——树序即绘制序：
/// 世界(layer0) → 本层 → MetaHealthFX → HUD(layer2)，故 Meta 后处理自然采样到辉光结果。
///
/// 性能纪律：
/// - 关闭（设置 WorldPostFx=false）时整层 Visible=false，常态零 GPU；
/// - reduce_flash 时颗粒置零、辉光强度减半（不改变亮部阈值，保持画面结构）；
/// - quality 档（effects.world_post.quality）控制环采样数（≥0.5 双环 8 tap / 否则单环 4 tap）；
/// - 参数仅在变化时上传（epsilon 检测），时间 uniform 每帧更新（颗粒/无）。
///
/// 注：C# 静态字段禁止持有 Godot 对象（引擎退出 finalize segfault 实测根因）——Shader/Material
/// 均实例字段，不做静态缓存（UITheme.Font / CinematicFx 同款处理）。
/// </summary>
public partial class WorldPostFx : CanvasLayer
{
    private const float Epsilon = 0.001f;

    // 设置 override：-1 = 未设置，取 balance.json 默认
    private bool _enabled = true;
    private bool _reduceFlash;
    private float _time;
    private bool _paramsDirty = true;

    private ShaderMaterial _mat = null!;
    private ColorRect _rect = null!;
    private readonly Godot.Collections.Dictionary _last = new();

    // cfg 缓存（effects.world_post.*）
    private float _bloomThreshold = 0.52f;
    private float _bloomIntensity = 0.75f;
    private float _bloomRadius = 5.5f;
    private Color _bloomTint = new(1.0f, 0.74f, 0.36f);
    private float _quality = 1.0f;
    private float _grade = 0.55f;
    private Color _gain = new(1.085f, 1.0f, 0.90f);
    private Color _lift = new(0.014f, 0.010f, 0.006f);
    private float _contrast = 0.18f;
    private float _vignette = 0.33f;
    private float _grain = 0.028f;

    private readonly Callable _onWorldPostFxChanged;
    private readonly Callable _onReduceFlashChanged;

    private static readonly StringName UTime = new("u_time");
    private static readonly StringName UTexel = new("u_texel");
    private static readonly StringName UBloomThreshold = new("u_bloom_threshold");
    private static readonly StringName UBloomIntensity = new("u_bloom_intensity");
    private static readonly StringName UBloomRadius = new("u_bloom_radius");
    private static readonly StringName UBloomTint = new("u_bloom_tint");
    private static readonly StringName UQuality = new("u_quality");
    private static readonly StringName UGrade = new("u_grade");
    private static readonly StringName UGain = new("u_gain");
    private static readonly StringName ULift = new("u_lift");
    private static readonly StringName UContrast = new("u_contrast");
    private static readonly StringName UVignette = new("u_vignette");
    private static readonly StringName UGrain = new("u_grain");

    public WorldPostFx()
    {
        _onWorldPostFxChanged = Callable.From<bool>(OnWorldPostFxChanged);
        _onReduceFlashChanged = Callable.From<bool>(OnReduceFlashChanged);
    }

    public override void _Ready()
    {
        Layer = 1;
        LoadCfg();
        _enabled = GameState.Instance.WorldPostFx;
        _reduceFlash = GameState.Instance.ReduceFlash;

        _rect = new ColorRect();
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _rect.MouseFilter = Control.MouseFilterEnum.Ignore;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/world_grade.gdshader") };
        _rect.Material = _mat;
        AddChild(_rect);

        PushStaticParams();
        _paramsDirty = true;
        ApplyEnabled();

        var gs = GameState.Instance;
        if (!gs.IsConnected(GameState.SignalName.WorldPostFxChanged, _onWorldPostFxChanged))
        {
            gs.Connect(GameState.SignalName.WorldPostFxChanged, _onWorldPostFxChanged);
        }

        if (!gs.IsConnected(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged))
        {
            gs.Connect(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged);
        }
    }

    public override void _Process(double delta)
    {
        if (!_enabled)
        {
            return;
        }

        _time += (float)delta;
        _mat.SetShaderParameter(UTime, _time);
        if (_paramsDirty)
        {
            _paramsDirty = false;
            PushDynamicParams();
        }
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (gs.IsConnected(GameState.SignalName.WorldPostFxChanged, _onWorldPostFxChanged))
            {
                gs.Disconnect(GameState.SignalName.WorldPostFxChanged, _onWorldPostFxChanged);
            }

            if (gs.IsConnected(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged))
            {
                gs.Disconnect(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged);
            }
        }
    }

    /// <summary>运行期同步开关（设置页信号或外部直调；enabled=false 隐藏整层，零 GPU）。</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled == _enabled)
        {
            return;
        }

        _enabled = enabled;
        ApplyEnabled();
    }

    public bool Enabled() => _enabled;

    private void ApplyEnabled()
    {
        if (_rect != null)
        {
            _rect.Visible = _enabled;
        }
    }

    private void OnWorldPostFxChanged(bool enabled)
    {
        SetEnabled(enabled);
    }

    private void OnReduceFlashChanged(bool enabled)
    {
        _reduceFlash = enabled;
        _paramsDirty = true;
    }

    /// <summary>静态参数（启动一次）：分辨率 texel + 配置驱动的分级/晕影/quality。</summary>
    private void PushStaticParams()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        _mat.SetShaderParameter(UTexel, new Vector2(1.0f / Mathf.Max(vp.X, 1.0f), 1.0f / Mathf.Max(vp.Y, 1.0f)));
        _mat.SetShaderParameter(UBloomThreshold, _bloomThreshold);
        _mat.SetShaderParameter(UBloomRadius, _bloomRadius);
        _mat.SetShaderParameter(UBloomTint, new Vector3(_bloomTint.R, _bloomTint.G, _bloomTint.B));
        _mat.SetShaderParameter(UQuality, _quality);
        _mat.SetShaderParameter(UGrade, _grade);
        _mat.SetShaderParameter(UGain, new Vector3(_gain.R, _gain.G, _gain.B));
        _mat.SetShaderParameter(ULift, new Vector3(_lift.R, _lift.G, _lift.B));
        _mat.SetShaderParameter(UContrast, _contrast);
        _mat.SetShaderParameter(UVignette, _vignette);
    }

    /// <summary>动态参数（reduce_flash 影响）：颗粒与辉光强度，仅在变化时上传。</summary>
    private void PushDynamicParams()
    {
        var bloom = _reduceFlash ? _bloomIntensity * 0.5f : _bloomIntensity;
        var grain = _reduceFlash ? 0.0f : _grain;
        SetIfChanged(UBloomIntensity, bloom);
        SetIfChanged(UGrain, grain);
    }

    private void SetIfChanged(StringName name, float value)
    {
        if (_last.TryGetValue(name, out var prev) && Mathf.Abs(prev.AsSingle() - value) < Epsilon)
        {
            return;
        }

        _last[name] = value;
        _mat.SetShaderParameter(name, value);
    }

    /// <summary>数值配置缓存（启动一次读入；默认值与 balance.json effects.world_post.* 一致）。
    /// 类型/范围守卫：手改 JSON 非法值不崩，做钳制。</summary>
    private void LoadCfg()
    {
        var gs = GameState.Instance;
        _bloomThreshold = Mathf.Clamp(CfgF(gs, "effects.world_post.bloom_threshold", _bloomThreshold), 0.0f, 0.98f);
        _bloomIntensity = Mathf.Clamp(CfgF(gs, "effects.world_post.bloom_intensity", _bloomIntensity), 0.0f, 3.0f);
        _bloomRadius = Mathf.Clamp(CfgF(gs, "effects.world_post.bloom_radius", _bloomRadius), 0.5f, 24.0f);
        _quality = CfgF(gs, "effects.world_post.quality", _quality);
        _grade = Mathf.Clamp(CfgF(gs, "effects.world_post.grade", _grade), 0.0f, 1.0f);
        _contrast = Mathf.Clamp(CfgF(gs, "effects.world_post.contrast", _contrast), 0.0f, 1.0f);
        _vignette = Mathf.Clamp(CfgF(gs, "effects.world_post.vignette", _vignette), 0.0f, 1.0f);
        _grain = Mathf.Clamp(CfgF(gs, "effects.world_post.grain", _grain), 0.0f, 0.12f);
        _bloomTint = CfgColor(gs, "effects.world_post.bloom_tint", _bloomTint);
        _lift = CfgColor(gs, "effects.world_post.lift", _lift);
        _gain = new Color(
            CfgF(gs, "effects.world_post.gain_r", _gain.R),
            CfgF(gs, "effects.world_post.gain_g", _gain.G),
            CfgF(gs, "effects.world_post.gain_b", _gain.B));
    }

    private static float CfgF(GameState gs, string key, float def)
    {
        var v = gs.Cfg(key, def);
        return v.VariantType is Variant.Type.Float or Variant.Type.Int ? (float)v.AsDouble() : def;
    }

    /// <summary>颜色配置读取：支持 "#rrggbb" 字符串形式（JSON 无 Color 类型）。非法值回退默认。</summary>
    private static Color CfgColor(GameState gs, string key, Color def)
    {
        var v = gs.Cfg(key, def);
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
