using Godot;

namespace InfiAir;

/// <summary>
/// 程序化多层视差滚动星空背景（M1 全量迁移，2026-08-08 自 scripts/starfield.gd 迁移）。
/// P1-4 合批保持：每层单条 draw_multiline（星点 1px 短线段 + 线宽，视觉等价圆点）；
/// _Process 原地写 PackedVector2Array，零每帧分配（热路径红线）。
/// C07/M5 保持：星点范围随可见世界区域 view_world_rect（尺寸 + 锚点，zoom>1 时锚点
/// 随可见区平移，回绕同基线）；R07 判型 + 非负钳制保持。
/// 视觉增厚（2026-09）：星云贴图双层（确定性程序化生成，灰度能量场 modulate 染色）+
/// 亮星层（软点贴图、逐星色温/闪烁相位）+ 低频流星；全部一次性建缓存，绘制零分配。
/// </summary>
public partial class Starfield : Node2D
{
    private int _farCount = 140;
    private int _nearCount = 90;
    private float _farSpeed = 60.0f;
    private float _nearSpeed = 140.0f;
    private int _brightCount = 16;
    private float _nebulaAlpha = 0.22f;
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
        new(1.0f, 0.95f, 0.85f),  // 暖白
        new(0.80f, 0.88f, 1.0f),  // 冷蓝白
        new(0.60f, 0.90f, 1.0f),  // 全息青
        new(1.0f, 0.80f, 0.55f),  // 琥珀
    };

    // ---- 星云层：一张灰度能量场贴图、2×2 平铺滚动（环面无缝），双色 tint 错半格 ----
    private Texture2D? _nebulaTex;
    private Texture2D? _starTex;
    private float _nebulaScroll;

    // ---- 流星：低频装饰（GD.Randf 运行时随机，非 gameplay 元素允许） ----
    private bool _meteorActive;
    private float _meteorNextDelay = 4.0f;
    private float _meteorT;
    private Vector2 _meteorPos;
    private Vector2 _meteorVel;
    private float _t;

    /// <summary>返航过场的星光拉伸倍率，随时间衰减回 1（过场导演 warp() 设置）。</summary>
    public float WarpFactor { get; private set; } = 1.0f;

    /// <summary>C07 修复：可见世界区域尺寸缓存（view_world_rect），替代硬编码 1920×1080。</summary>
    private Vector2 _areaSize = new(1920.0f, 1080.0f);

    /// <summary>M5 审计：星点区域锚点（_ready 时可见区左上角），随可见区平移，回绕同基线。</summary>
    private Vector2 _origin = Vector2.Zero;

    /// <summary>A7：测试/诊断白盒断言经公开接口（M5 断言星空覆盖区域）。</summary>
    public Vector2 Origin() => _origin;

    public Vector2 AreaSize() => _areaSize;

    public void Warp(float factor) => WarpFactor = factor;

    /// <summary>M3a 探针：验证 GDScript → C# 静态方法经脚本资源调用（静态属性不可达——实测）。</summary>
    public static int StaticProbe() => 42;

    public override void _Ready()
    {
        ZIndex = -10;
        // R07：判型 + 非负钳制（L 系列判型族登记遗留）——字符串/负数手改配置不崩、不做负尺寸 resize
        // U03（2026-08-09 审计）：M7d 漏改的 Call("cfg") → typed（原动态调用已不存在，配置静默失效 + 每局 4 条引擎错误）
        // AB16：count 钳 [0, 4096]（默认 140/90；上界防手改巨值 OOM——new Vector2[1e9] ≈ 24GB 启动即崩，
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

        // C07：星点范围随可见世界区域而非写死 1920×1080；M5：区域锚点 = 可见区左上角
        var rng = new RandomNumberGenerator();
        rng.Seed = 12345;
        var view = GameState.Instance.ViewWorldRect();
        _areaSize = view.Size;
        _origin = view.Position;
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

        // P1-4：线段数组一次性分配（每星 2 点：起点 + 1px 尾端）
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
        _nebulaTex = CinematicFx.NebulaTexture(768, 20260907);
        _starTex = CinematicFx.SoftTexture();
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _t += d;
        WarpFactor = Mathf.Lerp(WarpFactor, 1.0f, 1.5f * d);
        var wrapY = _origin.Y + _areaSize.Y; // M5：回绕基线随区域锚点（zoom>1 时非 0）
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
        // 星云底：2×2 平铺（tile = 区域 0.7），紫/青双色 tint 各错半格叠 depth；滚动回绕同星空基线
        if (_nebulaTex != null && _nebulaAlpha > 0.001f)
        {
            var tile = _areaSize * 0.7f;
            var off = Mathf.PosMod(_nebulaScroll, tile.Y);
            var purple = new Color(0.45f, 0.32f, 0.68f, _nebulaAlpha);
            var teal = new Color(0.22f, 0.45f, 0.58f, _nebulaAlpha * 0.7f);
            for (var gy = -1; gy <= 1; gy++)
            {
                for (var gx = -1; gx <= 1; gx++)
                {
                    var basePos = _origin + new Vector2(gx * tile.X - tile.X * 0.15f, gy * tile.Y + off - tile.Y);
                    DrawTextureRect(_nebulaTex, new Rect2(basePos, tile), false, purple);
                    var tealPos = basePos + new Vector2(tile.X * 0.5f, tile.Y * 0.5f);
                    DrawTextureRect(_nebulaTex, new Rect2(tealPos, tile), false, teal);
                }
            }
        }

        // P1-4：每层单条 draw_multiline 合批（230 条绘制指令 → 2 条）；线宽对应原圆直径
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
