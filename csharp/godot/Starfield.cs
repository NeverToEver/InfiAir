using Godot;

namespace InfiAir;

/// <summary>
/// 程序化多层视差滚动星空背景。
/// 合批：每层单条 draw_multiline（星点 1px 短线段 + 线宽，视觉等价圆点）；
/// _Process 原地写 PackedVector2Array，零每帧分配（热路径红线）。
/// 星点范围随可见世界区域 view_world_rect（尺寸 + 锚点，zoom>1 时锚点
/// 随可见区平移，回绕同基线）；配置须判型 + 非负钳制。
/// 语境适配：挂在 CanvasLayer 下（标题屏/开场返航过场镜头）
/// 渲染 1:1 画布、相机 zoom 不作用于该画布——星区取全视口 rect；挂世界层（main/tutorial）
/// 才走 zoom 感知的 view_world_rect，且视角档位切换时星区按相对坐标重映射（星云/回绕基线
/// 同步），消除「大档位建区后切回小档位，星空只盖中央一块」的残留。
/// 视觉增厚（2026-09）：星云贴图双层（确定性程序化生成，灰度能量场 modulate 染色）+
/// 亮星层（软点贴图、逐星色温/闪烁相位）+ 低频流星；全部一次性建缓存，绘制零分配。
/// 星云 _Draw 为 3×3×2 = 18 次 DrawTextureRect（≈8.8 屏/帧混合填充）
/// → 单全屏精灵 + canvas_item shader（GPU repeat 平铺，1 draw、1 屏/帧；相位/tint/混合序
/// 逐位还原，见 starfield_nebula.gdshader 头注）。
/// </summary>
public partial class Starfield : Node2D
{
    private int _farCount = 180;
    private int _nearCount = 120;
    private float _farSpeed = 60.0f;
    private float _nearSpeed = 140.0f;
    private int _brightCount = 20;
    private float _nebulaAlpha = 0.3f;
    private float _meteorMinDelay = 6.0f;
    private float _meteorMaxDelay = 13.0f;

    private Vector2[] _far = System.Array.Empty<Vector2>();
    private Vector2[] _near = System.Array.Empty<Vector2>();
    private Vector2[] _farLines = System.Array.Empty<Vector2>(); // Godot C#：PackedVector2Array → Vector2[]
    private Vector2[] _nearLines = System.Array.Empty<Vector2>();
    private const float LineLen = 1.0f;

    // ---- 亮星层（比近层更快 = 更近的视差深度；逐星色温 + 闪烁相位，_Draw 内查表零分配） ----
    private Vector2[] _bright = System.Array.Empty<Vector2>();
    private Color[] _brightColors = System.Array.Empty<Color>();
    private float[] _brightPhase = System.Array.Empty<float>();
    private float[] _brightSize = System.Array.Empty<float>();
    private float[] _brightBaseA = System.Array.Empty<float>();
    private static readonly Color[] BrightPalette =
    {
        new(1.0f, 0.94f, 0.82f),  // 暖白
        new(0.82f, 0.86f, 1.0f),  // 冷蓝白
        new(1.0f, 0.78f, 0.46f),  // 琥珀（战术主色温）
        new(0.66f, 0.88f, 1.0f),  // 全息青（次要数据通道）
    };

    // ---- 星云层：一张灰度能量场贴图（环面无缝），暖琥珀/冷青双色 tint 错半格滚动 ----
    private const float NebulaTexSize = 768.0f; // 贴图边长（NebulaTexture 实参，平铺相位换算用）
    private Texture2D? _nebulaTex;
    private Texture2D? _starTex;
    private float _nebulaScroll;
    // 星云全屏精灵材质（精灵本体 _Ready 建为子节点由树持有，无需字段；每帧仅 1 个相位 uniform）
    private ShaderMaterial? _nebulaMat;
    private float _nebulaTileY = 1.0f; // 平铺世界高（= 区域高 ×0.7），滚动回绕基线
    private static readonly StringName UNebulaPhase = new("phase_off");

    /// <summary>星云精灵引用（视角档位切换重映射时同步位置/缩放；未建星云层时为 null）。</summary>
    private Sprite2D? _nebulaSprite;

    /// <summary>渲染语境：true = 挂 CanvasLayer 下（标题屏/过场镜头）1:1 画布，星区恒为全视口；
    /// false = 世界层，星区走 zoom 感知的 view_world_rect。_Ready 判定一次（节点不迁移语境）。</summary>
    private bool _canvasSpace;

    /// <summary>建区时生效的视角档位倍率（世界语境重映射判据；CanvasLayer 语境不参与）。
    /// 轮询设置档位而非视口 rect——DYING 呼吸缩放每帧改相机 Zoom 组合，按 rect 判会逐帧抖动重映射。</summary>
    private float _builtZoom = -1.0f;

    // ---- 流星：低频装饰（GD.Randf 运行时随机，非 gameplay 元素允许） ----
    private bool _meteorActive;
    private float _meteorNextDelay = 4.0f;
    private float _meteorT;
    private Vector2 _meteorPos;
    private Vector2 _meteorVel;
    private float _t;

    /// <summary>返航过场的星光拉伸倍率，随时间衰减回 1（过场导演 warp() 设置）。</summary>
    public float WarpFactor { get; private set; } = 1.0f;

    /// <summary>可见世界区域尺寸缓存（view_world_rect），不得硬编码 1920×1080。</summary>
    private Vector2 _areaSize = new(1920.0f, 1080.0f);

    /// <summary>星点区域锚点（_ready 时可见区左上角），随可见区平移，回绕同基线。</summary>
    private Vector2 _origin = Vector2.Zero;

    public void Warp(float factor) => WarpFactor = factor;

    /// <summary>是否挂在 CanvasLayer 之下（标题屏/开场返航过场镜头等 1:1 画布语境）。</summary>
    private bool InCanvasLayerSpace()
    {
        for (Node? n = this; n != null; n = n.GetParent())
        {
            if (n is CanvasLayer)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>视角档位切换后的星区重映射（世界语境）：三层星点按旧区相对坐标原地映射进
    /// 新区（密度模式连续，无重掷跳变、零分配），星云精灵位置/缩放与回绕基线同步更新。
    /// 区域未变（如教程等相机未注册语境 zoom 不影响 rect）则只对齐倍率记录，幂等跳过。</summary>
    private void RebuildArea()
    {
        var view = GameState.Instance.ViewWorldRect();
        var factor = (float)GameState.Instance.ViewZoomFactor();
        _builtZoom = factor;
        if (view.Size == _areaSize && view.Position == _origin)
        {
            return;
        }

        RemapArea(_far, view);
        RemapArea(_near, view);
        RemapArea(_bright, view);
        _areaSize = view.Size;
        _origin = view.Position;
        _nebulaTileY = _areaSize.Y * 0.7f;
        if (_nebulaSprite != null)
        {
            _nebulaSprite.Position = _origin;
            _nebulaSprite.Scale = _areaSize / NebulaTexSize;
        }
    }

    /// <summary>单层星点相对坐标重映射（RebuildArea 口径；旧区/新区字段由调用方维护次序）。</summary>
    private void RemapArea(Vector2[] stars, Rect2 view)
    {
        var oldOrigin = _origin;
        var oldSize = _areaSize;
        for (var i = 0; i < stars.Length; i++)
        {
            var rel = (stars[i] - oldOrigin) / oldSize;
            stars[i] = new Vector2(view.Position.X + rel.X * view.Size.X, view.Position.Y + rel.Y * view.Size.Y);
        }
    }

    public override void _Ready()
    {
        ZIndex = -10;
        // 判型 + 非负钳制——字符串/负数手改配置不崩、不做负尺寸 resize；
        // 配置读取必须 typed 直调（动态 Call("cfg") 会静默失效 + 每局 4 条引擎错误）
        // count 钳 [0, 4096]（默认 180/120；上界防手改巨值 OOM——new Vector2[1e9] ≈ 24GB 启动即崩，
        // >2^31 还经 (int) 回绕负）
        const long MaxCount = 4096;
        var fc = GameState.Instance.Cfg("effects.starfield.far_count", _farCount);
        if (fc.VariantType == Variant.Type.Int && fc.AsInt64() >= 0)
        {
            _farCount = (int)Math.Min(fc.AsInt64(), MaxCount);
        }

        var nc = GameState.Instance.Cfg("effects.starfield.near_count", _nearCount);
        if (nc.VariantType == Variant.Type.Int && nc.AsInt64() >= 0)
        {
            _nearCount = (int)Math.Min(nc.AsInt64(), MaxCount);
        }

        var fs = GameState.Instance.Cfg("effects.starfield.far_speed", _farSpeed);
        if (fs.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            _farSpeed = (float)fs.AsDouble();
        }

        var ns = GameState.Instance.Cfg("effects.starfield.near_speed", _nearSpeed);
        if (ns.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            _nearSpeed = (float)ns.AsDouble();
        }

        var bc = GameState.Instance.Cfg("effects.starfield.bright_count", _brightCount);
        if (bc.VariantType == Variant.Type.Int && bc.AsInt64() >= 0)
        {
            _brightCount = (int)Math.Min(bc.AsInt64(), MaxCount);
        }

        var na = GameState.Instance.Cfg("effects.starfield.nebula_alpha", _nebulaAlpha);
        if (na.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            _nebulaAlpha = Mathf.Clamp((float)na.AsDouble(), 0.0f, 1.0f);
        }

        var mMin = GameState.Instance.Cfg("effects.starfield.meteor_min_delay", _meteorMinDelay);
        if (mMin.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            _meteorMinDelay = Mathf.Max(1.0f, (float)mMin.AsDouble());
        }

        var mMax = GameState.Instance.Cfg("effects.starfield.meteor_max_delay", _meteorMaxDelay);
        if (mMax.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            _meteorMaxDelay = Mathf.Max(_meteorMinDelay, (float)mMax.AsDouble());
        }

        // 星点范围随可见世界区域而非写死 1920×1080；区域锚点 = 可见区左上角。
        // 语境适配：CanvasLayer 下 1:1 画布取全视口（zoom 不作用于该画布，
        // 过场镜头/标题屏按 zoom 收窄会把星空缩成屏幕中央一块）；世界层走 view_world_rect。
        _canvasSpace = InCanvasLayerSpace();
        var rng = new RandomNumberGenerator();
        rng.Seed = 12345;
        var view = _canvasSpace
            ? GameState.Instance.GetViewport().GetVisibleRect()
            : GameState.Instance.ViewWorldRect();
        _areaSize = view.Size;
        _origin = view.Position;
        _builtZoom = _canvasSpace ? -1.0f : (float)GameState.Instance.ViewZoomFactor();
        _far = new Vector2[_farCount];
        _near = new Vector2[_nearCount];
        for (int i = 0; i < _farCount; i++)
        {
            _far[i] = new Vector2(_origin.X + rng.Randf() * _areaSize.X, _origin.Y + rng.Randf() * _areaSize.Y);
        }

        for (int i = 0; i < _nearCount; i++)
        {
            _near[i] = new Vector2(_origin.X + rng.Randf() * _areaSize.X, _origin.Y + rng.Randf() * _areaSize.Y);
        }

        // 线段数组一次性分配（每星 2 点：起点 + 1px 尾端）
        _farLines = new Vector2[_farCount * 2];
        _nearLines = new Vector2[_nearCount * 2];

        // 亮星层：接续同一 RNG 序列（全局确定性重绘一致）；色温/相位/尺寸/基线亮度逐星随机
        _bright = new Vector2[_brightCount];
        _brightColors = new Color[_brightCount];
        _brightPhase = new float[_brightCount];
        _brightSize = new float[_brightCount];
        _brightBaseA = new float[_brightCount];
        for (int i = 0; i < _brightCount; i++)
        {
            _bright[i] = new Vector2(_origin.X + rng.Randf() * _areaSize.X, _origin.Y + rng.Randf() * _areaSize.Y);
            _brightColors[i] = BrightPalette[rng.RandiRange(0, BrightPalette.Length - 1)];
            _brightPhase[i] = rng.Randf() * Mathf.Tau;
            _brightSize[i] = rng.RandfRange(10.0f, 24.0f);
            _brightBaseA[i] = rng.RandfRange(0.3f, 0.6f);
        }

        // 星云/亮星贴图一次性构建（灰度能量场 + 软点），实例字段持有（C# 静态禁持 Godot 对象规则）
        _nebulaTex = CinematicFx.NebulaTexture((int)NebulaTexSize, 20260907);
        _starTex = CinematicFx.SoftTexture();

        // 星云改单全屏精灵（repeat 平铺 + 相位 shader），替代 _Draw 18 次 DrawTextureRect；
        // ShowBehindParent 保持星云在星点之下；alpha≈0 整层不建（同原早退门槛）
        if (_nebulaTex != null && _nebulaAlpha > 0.001f)
        {
            _nebulaTileY = _areaSize.Y * 0.7f;
            _nebulaMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/starfield_nebula.gdshader") };
            _nebulaMat.SetShaderParameter("warm", new Color(0.72f, 0.42f, 0.16f, _nebulaAlpha));
            _nebulaMat.SetShaderParameter("cool", new Color(0.16f, 0.38f, 0.55f, _nebulaAlpha * 0.7f));
            var nebula = new Sprite2D
            {
                Texture = _nebulaTex,
                Centered = false,
                Position = _origin,
                Scale = _areaSize / NebulaTexSize, // 贴图铺满可见区域（UV 0..1 ↔ 区域）
                TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
                ShowBehindParent = true,
                Material = _nebulaMat,
            };
            _nebulaSprite = nebula; // 重映射同步位置/缩放（RebuildArea）
            AddChild(nebula);
        }
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        // 世界语境下切换视角档位 → 可见区域变化 → 星区必须重映射——否则大档位开局再切
        // 小档位会留下「星空只盖中央一块」的空边残留。
        // 只比对设置档位倍率：DYING 呼吸缩放走相机 Zoom 组合、不改档位，不会触发抖动。
        if (!_canvasSpace && (float)GameState.Instance.ViewZoomFactor() != _builtZoom)
        {
            RebuildArea();
        }

        _t += d;
        WarpFactor = Mathf.Lerp(WarpFactor, 1.0f, 1.5f * d);
        var wrapY = _origin.Y + _areaSize.Y; // 回绕基线随区域锚点（zoom>1 时非 0）
        for (int i = 0; i < _far.Length; i++)
        {
            var p = _far[i] + new Vector2(0.0f, _farSpeed * WarpFactor * d);
            if (p.Y > wrapY)
            {
                p.Y -= _areaSize.Y;
            }

            _far[i] = p;
            _farLines[i * 2] = p;
            _farLines[i * 2 + 1] = p + new Vector2(LineLen, 0.0f);
        }

        for (int i = 0; i < _near.Length; i++)
        {
            var p = _near[i] + new Vector2(0.0f, _nearSpeed * WarpFactor * d);
            if (p.Y > wrapY)
            {
                p.Y -= _areaSize.Y;
            }

            _near[i] = p;
            _nearLines[i * 2] = p;
            _nearLines[i * 2 + 1] = p + new Vector2(LineLen, 0.0f);
        }

        // 星云缓慢下卷（Warp 时同步加速）；亮星 1.35× 近层速度（更近的视差层）
        _nebulaScroll += 12.0f * WarpFactor * d;
        // 星云相位单 uniform（x 偏 0.15 格；y = 1 − PosMod(scroll, tile)/tile，同原回绕基线）
        _nebulaMat?.SetShaderParameter(UNebulaPhase, new Vector2(0.15f, 1.0f - Mathf.PosMod(_nebulaScroll, _nebulaTileY) / _nebulaTileY));
        var brightSpeed = _nearSpeed * 1.35f * WarpFactor;
        for (int i = 0; i < _bright.Length; i++)
        {
            var p = _bright[i] + new Vector2(0.0f, brightSpeed * d);
            if (p.Y > wrapY)
            {
                p.Y -= _areaSize.Y;
            }

            _bright[i] = p;
        }

        // 流星状态机：非激活倒计时 → 激活飞行 0.9s → 归位换随机间隔
        if (_meteorActive)
        {
            _meteorT += d / 0.9f;
            _meteorPos += _meteorVel * d;
            if (_meteorT >= 1.0f)
            {
                _meteorActive = false;
                _meteorNextDelay = (float)GD.RandRange(_meteorMinDelay, _meteorMaxDelay);
            }
        }
        else
        {
            _meteorNextDelay -= d;
            if (_meteorNextDelay <= 0.0f)
            {
                _meteorActive = true;
                _meteorT = 0.0f;
                var dirSign = GD.Randf() < 0.5f ? -1.0f : 1.0f;
                _meteorPos = new Vector2(_origin.X + (float)GD.Randf() * _areaSize.X, _origin.Y - 40.0f);
                _meteorVel = new Vector2(dirSign * (float)GD.RandRange(180.0, 320.0), (float)GD.RandRange(420.0, 580.0));
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        // 星云底已迁至全屏精灵（_Ready 建，ShowBehindParent 绘于本节点星点之下）

        // 每层单条 draw_multiline 合批（230 条绘制指令 → 2 条）；线宽对应原圆直径
        DrawMultiline(_farLines, new Color(0.7f, 0.75f, 0.9f, 0.6f), 3.0f);
        DrawMultiline(_nearLines, new Color(1.0f, 1.0f, 1.0f, 0.9f), 5.0f);

        // 亮星层：软点贴图 + 逐星色温/正弦闪烁；最大的几枚加十字微光
        if (_starTex != null)
        {
            for (var i = 0; i < _bright.Length; i++)
            {
                var twinkle = 0.65f + 0.35f * Mathf.Sin(_t * 2.1f + _brightPhase[i]);
                var a = _brightBaseA[i] * twinkle;
                var c = _brightColors[i];
                var half = _brightSize[i] * 0.5f;
                DrawTextureRect(_starTex, new Rect2(_bright[i] - new Vector2(half, half), new Vector2(_brightSize[i], _brightSize[i])), false, new Color(c, a));
                if (_brightSize[i] > 20.0f)
                {
                    var r = _brightSize[i] * 0.9f;
                    DrawLine(_bright[i] + new Vector2(-r, 0.0f), _bright[i] + new Vector2(r, 0.0f), new Color(c, a * 0.4f), 1.0f, true);
                    DrawLine(_bright[i] + new Vector2(0.0f, -r), _bright[i] + new Vector2(0.0f, r), new Color(c, a * 0.4f), 1.0f, true);
                }
            }
        }

        // 流星：头亮尾淡 4 段拖尾，出现/收尾各留包络（无突现突失）
        if (_meteorActive)
        {
            var envelope = Mathf.Clamp(_meteorT / 0.1f, 0.0f, 1.0f) * Mathf.Clamp((1.0f - _meteorT) / 0.2f, 0.0f, 1.0f);
            if (envelope > 0.01f)
            {
                var tail = -_meteorVel.Normalized();
                var head = _meteorPos;
                for (var s = 0; s < 4; s++)
                {
                    var a = 0.55f * envelope * (1.0f - s / 4.0f);
                    var from = head + tail * (18.0f * s);
                    var to = head + tail * (18.0f * (s + 1));
                    DrawLine(from, to, new Color(0.85f, 0.95f, 1.0f, a), 2.5f - s * 0.4f, true);
                }
            }
        }
    }
}
