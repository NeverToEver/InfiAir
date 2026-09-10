using Godot;

namespace InfiAir;

/// <summary>
/// Enemy.SinFast 静态直调。
/// Meta HUD 血量与受击反馈：全屏后处理承载受击色差/径向模糊、
/// 攻击方向定向波纹、低血裂纹生长/错峰消散、去饱和/冷青色偏/晕影与 DYING 心跳/呼吸/抖动。
/// layer=1：世界之上、HUD 之下（HUD 在主场景抬至 layer=2；低于 OrbitalStrike 24、过场 35）。
/// 性能（§2 决策）：满血静止隐藏全屏 ColorRect + _process 早退（常态零 GPU、≈零 CPU）；
/// 参数上传 D5 epsilon 检测；自适应增益 D3 注册表代理亮度（0.25s 节流，零 GPU 回读）。
/// 注：首帧延后烘焙用一次性 ProcessFrame 信号回调（OneShot 连接，不挂 await 协程——退出无泄漏；
/// C15 守卫保留）；META_SHADER/BAKE_SHADER 不静态持有 Godot Resource（C# 静态字段持 Godot 对象
/// 退出 segfault 实测根因），改 _Ready GD.Load（资源缓存命中）；
/// </summary>
public partial class MetaHealthFX : CanvasLayer
{
    /// <summary>D5 精度口径：动态量稳定判定与参数上传的 epsilon 检测共用同一阈值。</summary>
    private const float Epsilon = 0.001f;

    public const int STATE_NORMAL = 0;
    public const int STATE_DYING = 4;

    public static int GetStateNormal() => STATE_NORMAL;

    public static int GetStateDying() => STATE_DYING;

    // 下行边界（HP 比例，低于即进入下一状态）；末级 DYING 阈值以 balance.json
    // effects.meta_health.dying.threshold 为准（_state_for_x 运行时用 cfg 覆盖 0.20，防双源漂移）
    private static readonly float[] THRESHOLDS = { 0.75f, 0.50f, 0.25f, 0.20f };
    // 各状态裂纹密度上限（NORMAL 无裂纹；balance.json effects.meta_health.crack.density 可覆盖）
    private static readonly float[] DENSITY_CAPS = { 0.0f, 0.30f, 0.50f, 0.75f, 1.0f };

    // 裂纹发光色带（§4.2 crossfade，带宽 0.08）
    private static readonly Color CRACK_CYAN = new(0x35e0ffff);
    private static readonly Color CRACK_YELLOW = new(0xffd23fff);
    private static readonly Color CRACK_ORANGE = new(0xff8a3dff);
    private static readonly Color CRACK_RED = new(0xff3b4eff);
    private const float COLOR_BAND = 0.08f;

    // D5 上传参数名（StringName 为 struct 可静态；等价 GDScript &"..." 字面量）
    private static readonly StringName UHitIntensity = new("u_hit_intensity");
    private static readonly StringName UHitDir = new("u_hit_dir");
    private static readonly StringName UChromaticAmount = new("u_chromatic_amount");
    private static readonly StringName URadialBlurStrength = new("u_radial_blur_strength");
    private static readonly StringName URipplePhase = new("u_ripple_phase");
    private static readonly StringName UCrackProgress = new("u_crack_progress");
    private static readonly StringName UCrackColor = new("u_crack_color");
    private static readonly StringName UCrackDensity = new("u_crack_density");
    private static readonly StringName UHealJitter = new("u_heal_jitter");
    private static readonly StringName UDesaturation = new("u_desaturation");
    private static readonly StringName UHueCool = new("u_hue_cool");
    private static readonly StringName UVignetteStrength = new("u_vignette_strength");
    private static readonly StringName UVignetteInner = new("u_vignette_inner");
    private static readonly StringName UHeartbeat = new("u_heartbeat");
    private static readonly StringName UAdaptGain = new("u_adapt_gain");

    private int _state = STATE_NORMAL;
    private float _damageX; // 平滑后的损伤度（0=满血，1=空血）
    private float _targetX;
    private float _hitPulse;
    private Vector2 _hitDir = Vector2.Zero; // 零向量 = 波纹退化为全边缘均匀环
    private float _rippleT = 2.0f; // >1 表示无波纹
    private float _growBoost; // 跨阈值裂纹生长过冲（+overshoot，grow_time 内回落）
    private float _healT = -1.0f; // >=0：修复错峰消散进行中（0.7s 全程）
    private float _healJitter;
    private float _heartPhase = -1.0f; // <0 表示非 DYING
    private float _heartEnv; // 心跳脉冲包络（0.3s，减少闪光时视觉置零、音效保留）
    private float _heartRate; // 当前心率 Hz
    private float _breath = 1.0f;
    private float _vigInner = 0.62f;
    private float _warnT; // DYING 警告边框正弦相位
    private ShaderMaterial _mat = null!;
    private ColorRect _rect = null!;
    private readonly Godot.Collections.Dictionary _last = new(); // D5 epsilon 缓存（参数名 -> 上次上传值）
    private Godot.Collections.Dictionary _cfg = null!; // effects.meta_health.* 一次性缓存
    // 热路径字段化（空间换时间）：非 idle 帧每帧必用的配置键提为字段，LoadCfg 一次性缓存，
    // 免每帧 `_cfg[key].AsSingle()` 字典查找（哈希查询 + Variant 转换）；值继承 _cfg 入列时的
    // H15 钳制（Mathf.Max 下限）/类型转换，运行期恒定，行为逐位等价
    private float _smoothDownTau;
    private float _smoothUpTau;
    private float _crackExponent;
    private float _pulseScale;
    private float _pulseMin;
    private float _pulseDecayTau;
    private float _chromaticBase;
    private float _chromaticPeak;
    private float _reduceFlashChromaticScale;
    private float _blurStrength;
    private float _rippleDuration;
    private float _rippleAlpha;
    private float _crackHealJitter;
    private float _crackGrowOvershoot;
    private float _crackGrowTime;
    private float _desatMax;
    private float _desatExponent;
    private float _vignetteMaxAlpha;
    private float _vignetteInnerCfg;
    private float _vignetteDyingShrink;
    private float _dyingThreshold;
    private float _heartMinHz;
    private float _heartMaxHz;
    private float _breathAmp;
    private float _jitterPx;
    private float _warnHz;
    private float _dyingFade;
    private float _adaptInterval;
    private float _adaptMin;
    private float _adaptMax;
    private float _adaptBulletWeight;
    private float _adaptExplosionWeight;
    private float[] _crackDensityCaps = null!; // crack_density 数组同族字段化（原 _Process 每帧字典查找 + Variant 数组转换）
    private int _lod;
    private float _adaptTimer;
    private float _adaptGain = 1.0f;
    private bool _fieldReady;
    private Texture2D _fieldTex = null!;
    private bool _forceRefresh; // 减少闪光切换等外部态变化时强制刷新一帧
    private Hud? _hudCache; // A5 同族：HUD 延迟缓存（DYING 心跳 meta_jitter 直调用）
    // 诊断计数（§7 验收口径）：per-frame 参数上传次数 / 早退命中次数
    private int _uploadCount;
    private int _earlyOutCount;

    private Shader _metaShader = null!;
    private Shader _bakeShader = null!;

    private readonly Callable _onHealthChanged;
    private readonly Callable _onPlayerDamaged;
    private readonly Callable _onPlayerDied;
    private readonly Callable _onReduceFlashChanged;
    private readonly Callable _deferFrame;
    private SubViewport? _bakeVp;

    public MetaHealthFX()
    {
        _onHealthChanged = Callable.From<float>(OnHealthChanged);
        _onPlayerDamaged = Callable.From<float, Vector2>(OnPlayerDamaged);
        _onPlayerDied = Callable.From(OnPlayerDied);
        _onReduceFlashChanged = Callable.From<bool>(OnReduceFlashChanged);
        _deferFrame = Callable.From(OnDeferFrame);
    }

    // ---------------- A7：诊断白盒断言经公开接口（平滑参数注入口 + 状态 getter） ----------------

    /// <summary>血量-裂纹映射曲线（§4.2；纯映射值，不含生长过冲）</summary>
    public float CrackProgress()
    {
        return Mathf.Pow(_damageX, _crackExponent);
    }

    public float HitPulse() => _hitPulse;

    public float DamageX() => _damageX;

    public int State() => _state;

    public float HealJitter() => _healJitter;

    public float HeartRate() => _heartRate;

    public float Breath() => _breath;

    public ColorRect Rect() => _rect;

    public int UploadCount() => _uploadCount;

    public int EarlyOutCount() => _earlyOutCount;

    /// <summary>DYING 呼吸缩放（Main D6 组合相机 zoom 用）</summary>
    public float BreathScale() => _breath;

    public bool BreathActive()
    {
        return _state == STATE_DYING && GameState.Instance.Health > 0.0 && !GameState.Instance.ReduceFlash;
    }

    public override void _Ready()
    {
        Layer = 1;
        _metaShader = GD.Load<Shader>("res://assets/shaders/meta_health.gdshader");
        _bakeShader = GD.Load<Shader>("res://assets/shaders/crack_field_bake.gdshader");
        LoadCfg();
        _lod = _cfg["lod"].AsInt32();
        GameState.Instance.MetaFxLod = _lod; // 供 Hud 低血晕影回退判断（D2）
        _rect = new ColorRect();
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _rect.MouseFilter = Control.MouseFilterEnum.Ignore;
        _mat = new ShaderMaterial { Shader = _metaShader };
        _rect.Material = _mat;
        AddChild(_rect);
        _mat.SetShaderParameter("u_lod", _lod);
        _mat.SetShaderParameter("u_crack_spread_min", CfgFloat("crack_spread_min"));
        _mat.SetShaderParameter("u_crack_edge_softness", CfgFloat("crack_edge_softness"));
        _mat.SetShaderParameter("u_crack_width", CfgFloat("crack_width"));
        // K04：crack_glow 死配置键接线——shader ADD 伪泛光强度原为字面 0.8，改由配置驱动
        _mat.SetShaderParameter("u_crack_glow", CfgFloat("crack_glow"));
        _vigInner = _vignetteInnerCfg;
        // 启动即对齐当前血量（读档续局/场景重载），不产生过渡演出
        var health = GameState.Instance.Health;
        var maxHealth = GameState.Instance.MaxHealth();
        _targetX = 1.0f - (float)Mathf.Clamp(health / maxHealth, 0.0, 1.0);
        _damageX = _targetX;
        _state = StateForX(_damageX);
        if (_state == STATE_DYING && health > 0.0)
        {
            _heartPhase = 0.0f;
        }

        // P1-4（2026-08-05 审计）：裂纹场烘焙延后到首帧后——SubViewport 512² GPU 回读是
        // 启动一次性 pipeline stall，不占首帧关键路径；烘焙完成前 _field_ready=false，
        // shader 侧已早退不显示裂纹（379 行），满血开局无感知、读档续局延迟一帧
        DeferBake();
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (!gs.IsConnected(GameState.SignalName.HealthChanged, _onHealthChanged))
            {
                gs.Connect(GameState.SignalName.HealthChanged, _onHealthChanged);
            }

            if (!gs.IsConnected(GameState.SignalName.PlayerDamaged, _onPlayerDamaged))
            {
                gs.Connect(GameState.SignalName.PlayerDamaged, _onPlayerDamaged);
            }

            if (!gs.IsConnected(GameState.SignalName.PlayerDied, _onPlayerDied))
            {
                gs.Connect(GameState.SignalName.PlayerDied, _onPlayerDied);
            }

            if (!gs.IsConnected(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged))
            {
                gs.Connect(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged);
            }
        }

        _rect.Visible = _damageX > 0.001f;
    }

    public override void _ExitTree()
    {
        // U11（2026-08-09 审计）：FramePostDraw 静态事件显式断开（连接存在才断开，防
        // "disconnect nonexistent" 报错）；与下方 GameState 信号断开并列
        if (_bakeFrameConnected)
        {
            RenderingServer.FramePostDraw -= OnBakeFrame;
            _bakeFrameConnected = false;
        }

        // C22 模式：GameState 信号显式断开 + 挂起的烘焙延迟回调——防退出 segfault
        // MetaFX 不在场时 hud 低血晕影走回退路径（D2）
        var gs = GameState.Instance;
        if (gs != null)
        {
            gs.MetaFxLod = 1;
            if (gs.IsConnected(GameState.SignalName.HealthChanged, _onHealthChanged))
            {
                gs.Disconnect(GameState.SignalName.HealthChanged, _onHealthChanged);
            }

            if (gs.IsConnected(GameState.SignalName.PlayerDamaged, _onPlayerDamaged))
            {
                gs.Disconnect(GameState.SignalName.PlayerDamaged, _onPlayerDamaged);
            }

            if (gs.IsConnected(GameState.SignalName.PlayerDied, _onPlayerDied))
            {
                gs.Disconnect(GameState.SignalName.PlayerDied, _onPlayerDied);
            }

            if (gs.IsConnected(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged))
            {
                gs.Disconnect(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged);
            }
        }

        var tree = GetTree();
        if (tree != null && tree.IsConnected(SceneTree.SignalName.ProcessFrame, _deferFrame))
        {
            tree.Disconnect(SceneTree.SignalName.ProcessFrame, _deferFrame);
        }
    }

    /// <summary>数值配置缓存（启动一次读入；默认值与 balance.json effects.meta_health.* 保持一致）
    /// H08（健壮性审核）：crack.density 长度/元素校验回退——损坏 JSON 短数组/非数值时
    /// 用默认档位，防每帧越界索引与 float 转换报错</summary>
    private float[] LoadDensityCaps()
    {
        var def = new Godot.Collections.Array();
        foreach (var d in DENSITY_CAPS)
        {
            def.Add(d);
        }

        var raw = GameState.Instance.Cfg("effects.meta_health.crack.density", def);
        if (raw.VariantType == Variant.Type.Array)
        {
            var arr = raw.AsGodotArray();
            if (arr.Count == DENSITY_CAPS.Length)
            {
                foreach (var v in arr)
                {
                    if (v.VariantType != Variant.Type.Int && v.VariantType != Variant.Type.Float)
                    {
                        return (float[])DENSITY_CAPS.Clone();
                    }
                }

                var caps = new float[arr.Count];
                for (var i = 0; i < arr.Count; i++)
                {
                    caps[i] = arr[i].AsSingle();
                }

                return caps;
            }
        }

        return (float[])DENSITY_CAPS.Clone();
    }

    private void LoadCfg()
    {
        _crackDensityCaps = LoadDensityCaps(); // 字段化缓存（热路径直读 float[]）
        var densityCaps = new Godot.Collections.Array();
        foreach (var d in _crackDensityCaps)
        {
            densityCaps.Add(d);
        }

        _cfg = new Godot.Collections.Dictionary
        {
            ["lod"] = GameState.Instance.Cfg("effects.meta_health.lod", 0).AsInt32(),
            ["pulse_scale"] = CfgVal("effects.meta_health.pulse.scale", 2.5f),
            ["pulse_min"] = CfgVal("effects.meta_health.pulse.min", 0.15f),
            // H15
            ["pulse_decay_tau"] = Mathf.Max(CfgVal("effects.meta_health.pulse.decay_tau", 0.09f), 0.001f),
            ["chromatic_base"] = CfgVal("effects.meta_health.chromatic.base", 0.006f),
            ["chromatic_peak"] = CfgVal("effects.meta_health.chromatic.peak", 0.014f),
            ["blur_strength"] = CfgVal("effects.meta_health.blur.strength", 0.6f),
            // H15
            ["ripple_duration"] = Mathf.Max(CfgVal("effects.meta_health.ripple.duration", 0.4f), 0.001f),
            ["ripple_alpha"] = CfgVal("effects.meta_health.ripple.alpha", 0.8f),
            ["crack_exponent"] = CfgVal("effects.meta_health.crack.exponent", 1.6f),
            ["crack_spread_min"] = CfgVal("effects.meta_health.crack.spread_min", 0.10f),
            ["crack_edge_softness"] = CfgVal("effects.meta_health.crack.edge_softness", 0.08f),
            ["crack_width"] = CfgVal("effects.meta_health.crack.width", 0.03f),
            ["crack_glow"] = CfgVal("effects.meta_health.crack.glow", 0.8f),
            ["crack_heal_jitter"] = CfgVal("effects.meta_health.crack.heal_jitter", 0.35f),
            ["crack_grow_overshoot"] = CfgVal("effects.meta_health.crack.grow_overshoot", 0.08f),
            // K03：H15 同族遗漏（=0 时 _grow_boost 衰减除零）
            ["crack_grow_time"] = Mathf.Max(CfgVal("effects.meta_health.crack.grow_time", 0.6f), 0.001f),
            ["crack_density"] = densityCaps,
            ["desat_max"] = CfgVal("effects.meta_health.desat.max", 0.35f),
            ["desat_exponent"] = CfgVal("effects.meta_health.desat.exponent", 2.0f),
            ["vignette_max_alpha"] = CfgVal("effects.meta_health.vignette.max_alpha", 0.5f),
            ["vignette_inner"] = CfgVal("effects.meta_health.vignette.inner", 0.62f),
            ["vignette_dying_shrink"] = CfgVal("effects.meta_health.vignette.dying_shrink", 0.06f),
            ["dying_threshold"] = CfgVal("effects.meta_health.dying.threshold", 0.2f),
            ["heart_min_hz"] = CfgVal("effects.meta_health.dying.heart_min_hz", 1.0f),
            ["heart_max_hz"] = CfgVal("effects.meta_health.dying.heart_max_hz", 1.2f),
            ["breath"] = CfgVal("effects.meta_health.dying.breath", 0.015f),
            ["jitter_px"] = CfgVal("effects.meta_health.dying.jitter_px", 2.0f),
            ["warn_hz"] = CfgVal("effects.meta_health.dying.warn_hz", 2.5f),
            // H15
            ["dying_fade"] = Mathf.Max(CfgVal("effects.meta_health.dying.fade", 0.3f), 0.001f),
            // H15
            ["smooth_down_tau"] = Mathf.Max(CfgVal("effects.meta_health.smooth.down_tau", 0.10f), 0.001f),
            // H15
            ["smooth_up_tau"] = Mathf.Max(CfgVal("effects.meta_health.smooth.up_tau", 0.80f), 0.001f),
            ["adapt_interval"] = CfgVal("effects.meta_health.adapt.interval", 0.25f),
            ["adapt_min"] = CfgVal("effects.meta_health.adapt.min", 0.8f),
            ["adapt_max"] = CfgVal("effects.meta_health.adapt.max", 1.3f),
            ["adapt_bullet_weight"] = CfgVal("effects.meta_health.adapt.bullet_weight", 0.002f),
            ["adapt_explosion_weight"] = CfgVal("effects.meta_health.adapt.explosion_weight", 0.15f),
            ["reduce_flash_chromatic_scale"] = CfgVal("effects.meta_health.reduce_flash.chromatic_scale", 0.4f),
        };

        // 每帧必用键字段化（见字段声明注释）：值已含 H15 钳制/类型转换，运行期恒定
        _smoothDownTau = _cfg["smooth_down_tau"].AsSingle();
        _smoothUpTau = _cfg["smooth_up_tau"].AsSingle();
        _crackExponent = _cfg["crack_exponent"].AsSingle();
        _pulseScale = _cfg["pulse_scale"].AsSingle();
        _pulseMin = _cfg["pulse_min"].AsSingle();
        _pulseDecayTau = _cfg["pulse_decay_tau"].AsSingle();
        _chromaticBase = _cfg["chromatic_base"].AsSingle();
        _chromaticPeak = _cfg["chromatic_peak"].AsSingle();
        _reduceFlashChromaticScale = _cfg["reduce_flash_chromatic_scale"].AsSingle();
        _blurStrength = _cfg["blur_strength"].AsSingle();
        _rippleDuration = _cfg["ripple_duration"].AsSingle();
        _rippleAlpha = _cfg["ripple_alpha"].AsSingle();
        _crackHealJitter = _cfg["crack_heal_jitter"].AsSingle();
        _crackGrowOvershoot = _cfg["crack_grow_overshoot"].AsSingle();
        _crackGrowTime = _cfg["crack_grow_time"].AsSingle();
        _desatMax = _cfg["desat_max"].AsSingle();
        _desatExponent = _cfg["desat_exponent"].AsSingle();
        _vignetteMaxAlpha = _cfg["vignette_max_alpha"].AsSingle();
        _vignetteInnerCfg = _cfg["vignette_inner"].AsSingle();
        _vignetteDyingShrink = _cfg["vignette_dying_shrink"].AsSingle();
        _dyingThreshold = _cfg["dying_threshold"].AsSingle();
        _heartMinHz = _cfg["heart_min_hz"].AsSingle();
        _heartMaxHz = _cfg["heart_max_hz"].AsSingle();
        _breathAmp = _cfg["breath"].AsSingle();
        _jitterPx = _cfg["jitter_px"].AsSingle();
        _warnHz = _cfg["warn_hz"].AsSingle();
        _dyingFade = _cfg["dying_fade"].AsSingle();
        _adaptInterval = _cfg["adapt_interval"].AsSingle();
        _adaptMin = _cfg["adapt_min"].AsSingle();
        _adaptMax = _cfg["adapt_max"].AsSingle();
        _adaptBulletWeight = _cfg["adapt_bullet_weight"].AsSingle();
        _adaptExplosionWeight = _cfg["adapt_explosion_weight"].AsSingle();
    }

    /// <summary>effects.meta_health.* 配置读入（GameState.cfg 动态调用，Variant → float）。</summary>
    private static float CfgVal(string path, float def)
    {
        return (float)GameState.Instance.Cfg(path, def).AsDouble();
    }

    private float CfgFloat(string key)
    {
        return _cfg[key].AsSingle();
    }

    /// <summary>A5 同族（Mothership 模式）：HUD 延迟缓存——hud 组仅 Hud 单节点（main.tscn 固定层，
    /// 生命周期内恒定）；IsInstanceValid 守卫，缓存失效重新查找。替代 CallGroup 字符串派发。</summary>
    private Hud? Hud()
    {
        if (!GodotObject.IsInstanceValid(_hudCache))
        {
            _hudCache = GetTree().GetFirstNodeInGroup("hud") as Hud;
        }

        return _hudCache;
    }

    private int StateForX(float x)
    {
        var ratio = Mathf.Clamp(1.0f - x, 0.0f, 1.0f);
        var s = STATE_NORMAL;
        for (var i = 0; i < THRESHOLDS.Length; i++)
        {
            var t = THRESHOLDS[i];
            if (i == THRESHOLDS.Length - 1)
            {
                t = _dyingThreshold; // DYING 阈值以配置优先于常量表（默认 0.2），防双源漂移
            }

            if (ratio < t)
            {
                s += 1;
            }
        }

        return s;
    }

    private void OnHealthChanged(float newHealth)
    {
        _targetX = 1.0f - (float)Mathf.Clamp(newHealth / (float)GameState.Instance.MaxHealth(), 0.0, 1.0);
    }

    private void OnPlayerDamaged(float amount, Vector2 fromPos)
    {
        var r = amount / (float)GameState.Instance.MaxHealth();
        // max 池化：高频低伤不累积（R2）
        _hitPulse = Mathf.Max(_hitPulse, Mathf.Clamp(r * _pulseScale, _pulseMin, 1.0f));
        var playerV = GameState.Instance.PlayerRef;
        if (fromPos == Vector2.Inf || playerV == null)
        {
            _hitDir = Vector2.Zero; // 无方向：波纹退化为全边缘均匀环
        }
        else
        {
            var d = fromPos - ((Node2D)playerV).GlobalPosition;
            _hitDir = d.Length() > 1.0f ? d.Normalized() : Vector2.Zero;
        }

        _rippleT = 0.0f;
    }

    private void OnPlayerDied()
    {
        // 死亡即停心跳/呼吸（包络不再触发新拍），裂纹/去饱和定格作死亡底衬
        _heartPhase = -1.0f;
        _heartEnv = 0.0f;
    }

    private void OnReduceFlashChanged(bool enabled)
    {
        _forceRefresh = true;
    }

    /// <summary>裂纹颜色 crossfade（带宽 0.08）：青 → 黄 → 橙 → 红</summary>
    private Color CrackColor(float x)
    {
        var c = CRACK_CYAN;
        c = BlendBand(c, CRACK_YELLOW, x, 0.25f);
        c = BlendBand(c, CRACK_ORANGE, x, 0.50f);
        c = BlendBand(c, CRACK_RED, x, 0.80f);
        return c;
    }

    private Color BlendBand(Color a, Color b, float x, float edge)
    {
        return a.Lerp(b, Mathf.SmoothStep(edge - COLOR_BAND * 0.5f, edge + COLOR_BAND * 0.5f, x));
    }

    public override void _Process(double delta)
    {
        // 早退（D10）：全部动态量稳定时零参数上传；满血时连全屏 ColorRect 也隐藏（零 GPU）
        var d = (float)delta;
        var idle = (
            Mathf.Abs(_targetX - _damageX) < Epsilon
            && _hitPulse < Epsilon
            && _rippleT > 1.0f
            && _heartPhase < 0.0f
            && _heartEnv < Epsilon
            && _healT < 0.0f
            && _growBoost < Epsilon
            && Mathf.Abs(_breath - 1.0f) < Epsilon
        );
        if (idle && !_forceRefresh)
        {
            _earlyOutCount += 1;
            if (_damageX < Epsilon && _rect.Visible)
            {
                _rect.Visible = false;
            }

            return;
        }

        if (!_rect.Visible)
        {
            _rect.Visible = true;
        }

        // 1. 损伤度指数趋近：下行快入（tau=0.10）、上行慢出（tau=0.80）
        var down = _targetX > _damageX;
        var tau = down ? _smoothDownTau : _smoothUpTau;
        _damageX += (_targetX - _damageX) * (1.0f - Mathf.Exp(-d / tau));

        // 2. 状态跃迁：下行跨阈值 → 裂纹生长过冲；上行跨阈值 → 修复错峰消散（0.7s）
        var newState = StateForX(_damageX);
        if (newState > _state)
        {
            _growBoost = _crackGrowOvershoot;
        }
        else if (newState < _state)
        {
            _healT = 0.0f;
        }

        _state = newState;
        _growBoost = Mathf.MoveToward(_growBoost, 0.0f, _crackGrowOvershoot / _crackGrowTime * d);
        if (_healT >= 0.0f)
        {
            _healT += d / 0.7f;
            if (_healT >= 1.0f)
            {
                _healT = -1.0f;
                _healJitter = 0.0f;
            }
            else
            {
                _healJitter = _crackHealJitter * Enemy.SinFast(Mathf.Pi * _healT);
            }
        }

        // 3. HitPulse 指数衰减与波纹推进（与状态正交）
        _hitPulse *= Mathf.Exp(-d / _pulseDecayTau);
        _rippleT += d / _rippleDuration;

        // 4. DYING 临界层：心跳（1.0→1.2Hz 随 x 插值）/呼吸/抖动/警告脉动；进出均 0.3s 淡出无硬切
        var reduceFlash = GameState.Instance.ReduceFlash;
        var healthNow = GameState.Instance.Health;
        if (_state == STATE_DYING && healthNow > 0.0)
        {
            if (_heartPhase < 0.0f)
            {
                _heartPhase = 0.0f;
            }

            var thresholdX = 1.0f - _dyingThreshold;
            _heartRate = Mathf.Lerp(
                _heartMinHz,
                _heartMaxHz,
                Mathf.Clamp((_damageX - thresholdX) / Mathf.Max(1.0f - thresholdX, 0.01f), 0.0f, 1.0f));
            var prev = _heartPhase;
            _heartPhase += d * _heartRate;
            if (Mathf.Floor(_heartPhase) > Mathf.Floor(prev))
            {
                _heartEnv = 1.0f;
                GameState.Instance.PlaySfx(SfxId.Heartbeat); // D7：单发触发，音效不受减少闪光影响
                if (!reduceFlash)
                {
                    Hud()?.MetaJitter(_jitterPx); // D9（A5 同族：缓存直调替代 CallGroup 字符串派发）
                }
            }

            _heartEnv = Mathf.Max(_heartEnv - d / _dyingFade, 0.0f);
            _breath = 1.0f + _breathAmp * Enemy.SinFast(_heartPhase * Mathf.Tau);
            _warnT += d;
        }
        else
        {
            _heartPhase = -1.0f;
            _heartEnv = Mathf.Max(_heartEnv - d / _dyingFade, 0.0f);
            _breath = Mathf.MoveToward(_breath, 1.0f, d * _breathAmp / _dyingFade);
            _warnT = 0.0f;
        }

        // DYING 视野收窄 6%（0.3s 平滑）
        var vigInnerTarget = _vignetteInnerCfg;
        if (_state == STATE_DYING && healthNow > 0.0)
        {
            vigInnerTarget -= _vignetteDyingShrink;
        }

        _vigInner = Mathf.MoveToward(_vigInner, vigInnerTarget, _vignetteDyingShrink / _dyingFade * d);

        // 5. D3 自适应可读性：注册表代理亮度（活跃弹数/爆炸数），0.25s 节流，零 GPU 回读
        _adaptTimer -= d;
        if (_adaptTimer <= 0.0f)
        {
            _adaptTimer = _adaptInterval;
            // P2-1（2026-08-05 审计）：注册表/静态计数替代 get_children 扫描——活跃子弹数
            // （Bullet activate/deactivate 成对维护）与活跃爆炸数（Explosion.LiveCount()），
            // 语义与原 get_children + is_active/visible 过滤等价，消除 4 次/秒树遍历。
            // 计数经 GameState.BulletPool 实例读取（ActiveBulletCount/LiveExplosionCount，判空）
            var bullets = 0;
            var explosions = 0;
            var poolV = GameState.Instance.BulletPool;
            if (poolV != null && poolV is BulletPool pool)
            {
                bullets = pool.ActiveBulletCount;
                explosions = pool.LiveExplosionCount;
            }

            var proxy = bullets * _adaptBulletWeight + explosions * _adaptExplosionWeight;
            _adaptGain = Mathf.Clamp(1.0f - proxy, _adaptMin, _adaptMax);
        }

        // 6. 参数合成（§4.2 曲线；「减少闪光」在传参前折算，shader 零分支）
        var x = _damageX;
        var progress = Mathf.Min(CrackProgress() + _growBoost, 1.0f);
        var pulse = _hitPulse;
        var chromatic = 0.0f;
        if (pulse > Epsilon)
        {
            chromatic = _chromaticBase + _chromaticPeak * pulse;
            if (reduceFlash)
            {
                chromatic *= _reduceFlashChromaticScale;
            }
        }

        var blur = _blurStrength * pulse;
        var rippleOn = _rippleT <= 1.0f;
        var density = _crackDensityCaps[Mathf.Min(_state, _crackDensityCaps.Length - 1)]; // 字段化（LoadCfg 一次性缓存，免每帧字典 + Variant 数组转换）
        if (!_fieldReady)
        {
            density = 0.0f; // 距离场未烘焙完成前不出裂纹（避免空采样全屏闪）
        }

        var vigStrength = Mathf.Min(_vignetteMaxAlpha, CrackProgress() * 0.55f);
        if (_state == STATE_DYING && healthNow > 0.0 && !reduceFlash)
        {
            // 警告边框 2.5Hz 正弦（减少闪光时不进本分支，边框保持静态）
            vigStrength *= 1.0f + 0.25f * Enemy.SinFast(_warnT * Mathf.Tau * _warnHz);
        }

        var heartbeat = reduceFlash ? 0.0f : _heartEnv;

        // 7. D5 epsilon 检测上传（变化 <0.001 不上传）
        Put(UHitIntensity, rippleOn ? pulse * _rippleAlpha : 0.0f);
        Put(UHitDir, _hitDir);
        Put(UChromaticAmount, chromatic);
        Put(URadialBlurStrength, blur);
        Put(URipplePhase, Mathf.Clamp(_rippleT, 0.0f, 1.0f));
        Put(UCrackProgress, progress);
        Put(UCrackColor, CrackColor(x));
        Put(UCrackDensity, density);
        Put(UHealJitter, _healJitter);
        Put(UDesaturation, _desatMax * Mathf.Pow(x, _desatExponent));
        Put(UHueCool, 0.6f * x);
        Put(UVignetteStrength, vigStrength);
        Put(UVignetteInner, _vigInner);
        Put(UHeartbeat, heartbeat);
        Put(UAdaptGain, _adaptGain);
        _forceRefresh = false;
    }

    /// <summary>D5：epsilon 变化检测后上传；上传计数供诊断读取</summary>
    private void Put(StringName pname, Variant value)
    {
        if (_last.TryGetValue(pname, out var prev) && SameParam(prev, value))
        {
            return;
        }

        _last[pname] = value;
        _mat.SetShaderParameter(pname, value);
        _uploadCount += 1;
    }

    private bool SameParam(Variant a, Variant b)
    {
        if (a.VariantType == Variant.Type.Float && b.VariantType == Variant.Type.Float)
        {
            return Mathf.Abs(a.AsSingle() - b.AsSingle()) < Epsilon;
        }

        if (a.VariantType == Variant.Type.Vector2 && b.VariantType == Variant.Type.Vector2)
        {
            return (a.AsVector2() - b.AsVector2()).LengthSquared() < 0.000001f;
        }

        if (a.VariantType == Variant.Type.Color && b.VariantType == Variant.Type.Color)
        {
            var ca = a.AsColor();
            var cb = b.AsColor();
            return Mathf.Abs(ca.R - cb.R) < 0.004f && Mathf.Abs(ca.G - cb.G) < 0.004f && Mathf.Abs(ca.B - cb.B) < 0.004f;
        }

        // GDScript `a == b`：Variant 为 struct 无 == 运算符，Equals 语义等价
        //（本路径实际仅兜底——上传值均为 float/Vector2/Color，已在上方分支处理）
        return a.Equals(b);
    }

    // ---------------- 裂纹距离场预烘焙（D1） ----------------

    /// <summary>P1-4（2026-08-05 审计）：延后烘焙——await 首帧后执行，SubViewport GPU 回读不占
    /// 启动关键路径；headless CPU 回退与窗口 GPU 路径均延后（等价性不变量保持）。
    /// C# 侧改一次性 ProcessFrame 信号回调（OneShot）而非 await 协程：进程退出时挂起协程会
    /// 泄漏函数状态；C15 同款守卫：首帧前本节点被释放（场景早退/摘树路径）则不再操作 freed 实例。</summary>
    private void DeferBake()
    {
        GetTree().Connect(SceneTree.SignalName.ProcessFrame, _deferFrame, (uint)GodotObject.ConnectFlags.OneShot);
    }

    private void OnDeferFrame()
    {
        // C15 同款守卫：首帧前本节点被释放（场景早退/摘树路径）则不再操作 freed 实例
        if (!IsInsideTree())
        {
            return;
        }

        BakeCrackField();
    }

    /// <summary>运行时 SubViewport 单帧烘焙（启动一次，512²，成本 1 帧）；headless dummy 渲染走 CPU 回退。</summary>
    private void BakeCrackField()
    {
        if (DisplayServer.GetName() == "headless")
        {
            ApplyCrackFieldImage(CpuBakeImage(64));
            return;
        }

        var vp = new SubViewport
        {
            Size = new Vector2I(512, 512),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        var rectNode = new ColorRect();
        rectNode.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var mat = new ShaderMaterial { Shader = _bakeShader };
        rectNode.Material = mat;
        vp.AddChild(rectNode);
        AddChild(vp);
        // 一次性信号回调而非 await 协程：进程退出时挂起协程会泄漏函数状态
        _bakeVp = vp;
        RenderingServer.FramePostDraw += OnBakeFrame;
        _bakeFrameConnected = true;
    }

    /// <summary>烘焙回调是否已注册（U11：_ExitTree 断开前判活，未连接时 -= 报错）。</summary>
    private bool _bakeFrameConnected;

    private void OnBakeFrame()
    {
        // 一次性：回调内断开
        RenderingServer.FramePostDraw -= OnBakeFrame;
        _bakeFrameConnected = false;
        var vp = _bakeVp;
        _bakeVp = null;
        if (!GodotObject.IsInstanceValid(vp))
        {
            return;
        }

        var img = vp.GetTexture().GetImage();
        vp.QueueFree();
        if (img == null || img.IsEmpty())
        {
            img = CpuBakeImage(64);
        }

        ApplyCrackFieldImage(img);
    }

    private void ApplyCrackFieldImage(Image img)
    {
        _fieldTex = ImageTexture.CreateFromImage(img);
        _mat.SetShaderParameter("u_crack_field", _fieldTex);
        _fieldReady = true;
    }

    /// <summary>CPU 等价回退（headless / 回读失败兜底）：与 crack_field_bake.gdshader 公式一致。</summary>
    private Image CpuBakeImage(int size)
    {
        var seeds = new Vector2[12];
        for (var i = 0; i < 12; i++)
        {
            seeds[i] = new Vector2(
                Fract(Mathf.Sin((float)i * 12.9898f) * 43758.5453f),
                Fract(Mathf.Sin((float)i * 78.233f) * 43758.5453f));
        }

        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var uv = new Vector2(((float)x + 0.5f) / size, ((float)y + 0.5f) / size);
                var p = new Vector2(uv.X * 1.7778f, uv.Y);
                var f1 = 10.0f;
                var f2 = 10.0f;
                var h = 0.0f;
                foreach (var s0 in seeds)
                {
                    var s = new Vector2(s0.X * 1.7778f, s0.Y);
                    var d = p.DistanceTo(s);
                    if (d < f1)
                    {
                        f2 = f1;
                        f1 = d;
                        h = Fract(Mathf.Sin(s.Dot(new Vector2(12.9898f, 78.233f))) * 43758.5453f);
                    }
                    else if (d < f2)
                    {
                        f2 = d;
                    }
                }

                var border = f2 - f1;
                var radial = ((uv - new Vector2(0.5f, 0.5f)) * new Vector2(1.7778f, 1.0f)).Length();
                var gate = 1.0f - Mathf.Clamp(radial, 0.0f, 1.0f); // 生长门：边缘 0（最先蔓延）→ 中心 1
                img.SetPixel(x, y, new Color(Mathf.Clamp(border * 2.5f, 0.0f, 1.0f), h, gate, 1.0f));
            }
        }

        return img;
    }

    /// <summary>GLSL fract 等价（x - floor(x)），GDScript 无内建 fractf</summary>
    private static float Fract(float v)
    {
        return v - Mathf.Floor(v);
    }

}
