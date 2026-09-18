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
/// - 全屏呼吸（u_rhythm）由节奏服务给出，关闭 / 减闪 / 动效强度 0 时它都是精确 0——
///   本层只上传，相位、硬线与强度缩放全在 VisualRhythm（单一同步源，判据 4）；
/// - quality 档（effects.world_post.quality）控制环采样数（≥0.5 双环 8 tap / 否则单环 4 tap）；
/// - 参数仅在变化时上传（epsilon 检测），时间 uniform 每帧更新（颗粒/无）。
///
/// 注：C# 静态字段禁止持有 Godot 对象（引擎退出 finalize segfault 实测根因）——Shader/Material
/// 均实例字段，不做静态缓存（UITheme.Font / CinematicFx 同款处理）。
/// </summary>
public partial class WorldPostFx : CanvasLayer
{
    private const float Epsilon = 0.001f;

    // 动态战斗分级口径：Engine.TimeScale 低于 1（狂暴子弹时间）时向热档靠拢，恢复即回中。
    // SlowFloor 为归一化下限（缩放 ≤ 此值视为满档）；ramp 收敛到 epsilon 以下即吸回精确 0，
    // 保证中性时上传值与静态调色逐位一致。delta 已被引擎按 TimeScale 缩放，慢速段 ramp
    // 推进同样放缓——与 Main 的演出节奏口径一致。
    private const float CombatSlowFloor = 0.25f;
    private const float CombatRampTau = 0.22f;
    // 动态附加层上限（仅 ramp 非零时出现；空闲分支跳过，零额外采样）。
    private const float CombatChromaMax = 0.004f;   // 全局色差最大偏移（屏幕比例）
    private const float CombatScanlineMax = 0.045f; // 扫描线最深压暗比例
    // 重击脉冲：ScreenShake 强度达阈才触发；参考满量程取 boss 终段量级（24）。
    private const float HeavyHitShakeMin = 8.0f;
    private const float HeavyHitShakeRef = 24.0f;
    private const float HitPulseDecayTau = 0.12f;

    // 设置 override：-1 = 未设置，取 balance.json 默认
    private bool _enabled = true;
    private bool _reduceFlash;
    private float _time;
    private bool _paramsDirty = true;
    private float _combatRamp;
    private float _hitPulse;
    private Viewport? _viewport;
    private Vector2I _vpSize;

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
    private readonly Callable _onScreenShake;
    private readonly Callable _onViewportSizeChanged;

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
    private static readonly StringName UCombatRamp = new("u_combat_ramp");
    private static readonly StringName UHitPulse = new("u_hit_pulse");
    private static readonly StringName UChroma = new("u_chroma");
    private static readonly StringName UScanline = new("u_scanline");
    private static readonly StringName URhythm = new("u_rhythm");

    public WorldPostFx()
    {
        _onWorldPostFxChanged = Callable.From<bool>(OnWorldPostFxChanged);
        _onReduceFlashChanged = Callable.From<bool>(OnReduceFlashChanged);
        _onScreenShake = Callable.From<double>(OnScreenShake);
        _onViewportSizeChanged = Callable.From(OnViewportSizeChanged);
    }

    public override void _Ready()
    {
        Layer = 1;
        LoadCfg();
        _enabled = GameState.Instance.WorldPostFx;
        _reduceFlash = GameState.Instance.ReduceFlash;
        _viewport = GetViewport();

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

        // 重击脉冲来源：震屏信号（只读强度，不做任何玩法状态写入）
        if (!gs.IsConnected(GameState.SignalName.ScreenShake, _onScreenShake))
        {
            gs.Connect(GameState.SignalName.ScreenShake, _onScreenShake);
        }

        // 分辨率切换后 texel 必须重算（_Ready 只读一次，否则环采样半径按旧像素换算）
        if (GodotObject.IsInstanceValid(_viewport)
            && !_viewport.IsConnected(Viewport.SignalName.SizeChanged, _onViewportSizeChanged))
        {
            _viewport.Connect(Viewport.SignalName.SizeChanged, _onViewportSizeChanged);
        }
    }

    public override void _Process(double delta)
    {
        if (!_enabled)
        {
            return;
        }

        var d = (float)delta;
        _time += d;
        _mat.SetShaderParameter(UTime, _time);
        UpdateCombatLayer(d);
        if (_paramsDirty)
        {
            _paramsDirty = false;
            PushDynamicParams();
        }
    }

    public override void _ExitTree()
    {
        // autoload 可能先于本节点释放（非常规拆树序），Instance getter 会抛异常，故安全取值
        var gs = GameState.TryGetInstance();
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

            if (gs.IsConnected(GameState.SignalName.ScreenShake, _onScreenShake))
            {
                gs.Disconnect(GameState.SignalName.ScreenShake, _onScreenShake);
            }
        }

        if (GodotObject.IsInstanceValid(_viewport)
            && _viewport.IsConnected(Viewport.SignalName.SizeChanged, _onViewportSizeChanged))
        {
            _viewport.Disconnect(Viewport.SignalName.SizeChanged, _onViewportSizeChanged);
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

    /// <summary>重击泛光脉冲当前值（只读诊断口，零生产引用；见 ROADMAP 零引用成员口径——
    /// 它是探针观察面）：震屏信号按**原始强度**取阈，与「屏幕震动强度」滑杆（只折算 trauma）
    /// 互不连坐；信号若改发折算值，滑杆 ≤33% 时这份泛光会被一并关掉，无头下不崩不报错。</summary>
    public float HitPulse() => _hitPulse;

    private void ApplyEnabled()
    {
        if (_rect != null)
        {
            _rect.Visible = _enabled;
        }

        // 关闭＝回到改造前：把全屏呼吸显式写回精确 0。隐藏层不绘制，但值要回中性——
        // 重新打开的那一帧不该先亮一下残留振幅；_last 同步置位，守卫的比较基准与材质实际值必须一致
        if (!_enabled && _mat != null)
        {
            _last[URhythm] = 0.0f;
            _mat.SetShaderParameter(URhythm, 0.0f);
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
        _vpSize = new Vector2I((int)vp.X, (int)vp.Y);
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

    /// <summary>动态战斗分级 + 重击脉冲 + 全屏呼吸：只读 Engine.TimeScale、震屏信号与节奏服务，
    /// 写出 u_combat_ramp / u_hit_pulse / u_chroma / u_scanline / u_rhythm（全部 epsilon 守卫）。
    /// 中性时五者恒为精确 0，shader 分支跳过——不产生永久观感偏移、零额外 GPU 成本。</summary>
    private void UpdateCombatLayer(float d)
    {
        var ts = (float)Engine.TimeScale;
        var target = Mathf.Clamp((1.0f - ts) / Mathf.Max(1.0f - CombatSlowFloor, Epsilon), 0.0f, 1.0f);
        _combatRamp += (target - _combatRamp) * (1.0f - Mathf.Exp(-d / CombatRampTau));
        if (_combatRamp < Epsilon)
        {
            _combatRamp = 0.0f; // 吸回中性：保证空闲上传值精确等于静态调色
        }

        _hitPulse *= Mathf.Exp(-d / HitPulseDecayTau);
        if (_hitPulse < Epsilon)
        {
            _hitPulse = 0.0f;
        }

        var pulse = _reduceFlash ? _hitPulse * 0.5f : _hitPulse;
        // 减少闪光：色差/扫描线属可触发附加层，直接置零；重击脉冲减半
        var chroma = _reduceFlash ? 0.0f : _combatRamp * CombatChromaMax;
        var scanline = _reduceFlash ? 0.0f : _combatRamp * CombatScanlineMax;
        // 全屏呼吸：唯一同步源在 VisualRhythm（相位/减闪归零/峰谷硬线/动效强度都在它那边算完），
        // 本层只做上传。取不到服务即中性 0（标题屏等无节奏服务的场景）；非有限值落 0——
        // NaN 会顺乘法污染整屏输出而引擎零报错
        var rhythm = VisualRhythm.Instance?.FullScreenRhythm ?? 0.0f;
        SetIfChanged(UCombatRamp, _combatRamp);
        SetIfChanged(UHitPulse, pulse);
        SetIfChanged(UChroma, chroma);
        SetIfChanged(UScanline, scanline);
        SetIfChanged(URhythm, float.IsFinite(rhythm) ? rhythm : 0.0f);
    }

    /// <summary>震屏信号：仅重击（强度达阈）触发瞬时泛光/晕影脉冲；弱震不参与。</summary>
    private void OnScreenShake(double strength)
    {
        var s = (float)strength;
        if (s < HeavyHitShakeMin)
        {
            return;
        }

        // 下限 0.25 保证达阈重击必有可见脉冲；上限 1 防极端强度过冲
        _hitPulse = Mathf.Max(_hitPulse, Mathf.Clamp(s / HeavyHitShakeRef, 0.25f, 1.0f));
    }

    private void OnViewportSizeChanged()
    {
        RefreshTexel();
    }

    /// <summary>视口尺寸变化时刷新 u_texel（环采样半径按新像素换算，否则分辨率切换后晕光尺度失真）。</summary>
    private void RefreshTexel()
    {
        if (_mat == null || !GodotObject.IsInstanceValid(_viewport))
        {
            return;
        }

        var size = _viewport.GetVisibleRect().Size;
        var sizeI = new Vector2I((int)size.X, (int)size.Y);
        if (sizeI == _vpSize)
        {
            return;
        }

        _vpSize = sizeI;
        _mat.SetShaderParameter(UTexel, new Vector2(1.0f / Mathf.Max(size.X, 1.0f), 1.0f / Mathf.Max(size.Y, 1.0f)));
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
