using System;
using Godot;

namespace InfiAir;

/// <summary>
/// 一次性速度线（背景战况响应，`DESIGN_BASELINE` §2.12 判据 3/6）：大事件
/// （Boss 入场 / 遭遇开始 / 母舰召唤 / 返航跃迁）时若干竖直亮线自屏外沿沿飞行方向（下）
/// 掠过一次，随后自隐。**非常驻动效**——普通波次不触发（§1.9.1 克制纪律：静止几乎不动、
/// 变化时才动），触发点全部来自既有信号/既有调用点，本类不新增信号。
///
/// 为什么是独立节点而不是常驻层：静止期必须零成本。常驻层即使 alpha 为 0 也要占一次
/// 逐帧回调与一次绘制；本类静止时 `_Process` 关、`Visible` 关，两层都不进。
///
/// 合批：全部线段在 <see cref="_Draw"/> 里一次 <c>DrawMultiline</c>（2N 个点，照
/// <see cref="Starfield"/> 星点批处理的做法）——逐条 `DrawLine` 是 N 次绘制调用。
/// 数组在 `_Ready` 一次建好，逐帧**原地改写**（热路径零分配、零节点增删）。
///
/// 可读性（判据 3「不得遮盖弹体轮廓与前摇预告」）：`ZIndex = -1`，压在星野（-10）与
/// 纵深背景（-5）之上、全部实体（0）之下——速度线只是背景层的运动提示，绝不压住敌弹/
/// 敌机/玩家轮廓。亮度：峰值 alpha 硬顶 <see cref="AlphaCeiling"/>（0.45，细线运动层的峰值
/// alpha 口径——判据 3 的 25% 是背景**面**的亮度预算，不是同一个量，见该常量注释；balance 里
/// 调高也会被钳回），线色取冷白（同星野），不用琥珀——战术主色留给可交互物。
///
/// 缩放与门控（判据 6）：触发时读 <see cref="VisualRhythm.Intensity"/>（设置项「动效强度」），
/// 为 0 即不触发（＝回到本批次之前的画面），其余档位只缩放峰值 alpha，不改速度/时长
/// （「缩放振幅、不改频率」）。**减少闪光不参与**：这条是运动不是闪烁，XAG 117 明确
/// 活跃玩法期间的背景运动不在该约束内（同星野与纵深背景的既有口径）。
///
/// 确定性（AGENTS §4）：位置/速度/长度抖动取自**固定种子**的
/// <see cref="RandomNumberGenerator"/> 实例，一律不用 `GD.Rand*` 默认序列；
/// 逐帧只吃 `delta`（模拟时间），不碰墙钟。
///
/// 进程模式取 <c>Always</c>：返航跃迁的触发点紧跟 `PlayReturnCinematic` 的整树暂停，
/// 可暂停的话这一趟会冻在半途、等「继续出击」解冻后以残线复现（不崩不报错，只在画面上坏）；
/// 同族的一次性世界层动效（Explosion / OrbitalStrike）同样是 Always。
/// </summary>
public partial class SpeedLines : Node2D
{
    /// <summary>布局抖动种子（固定值：同触发序列下逐帧可复现，同 Starfield 12345 / 纵深层 20260911 口径）。</summary>
    private const ulong LayoutSeed = 20260918;

    /// <summary>峰值 alpha 硬顶（balance 的 speedline_alpha 再高也钳到这里）。
    /// **口径**：判据 3 的「背景层 ≤25%」说的是背景**面**的亮度预算（大面积填充的亮度占比），
    /// 而本项是**细线运动层**的峰值 alpha——2px 宽的掠过一次的线段，屏占远低于面积判据，
    /// 两者不是同一个量（既有星野亮星线的 alpha 比本值更高，同属细线口径）。
    /// 这里取 0.45 作护栏：再高会让速度线在暗背景上读成实体障碍物而非运动提示。</summary>
    private const float AlphaCeiling = 0.45f;

    /// <summary>线宽（像素）。合批线宽，纯形状量——不是可调幅值，不入 balance。</summary>
    private const float LineWidth = 2.0f;

    /// <summary>逐线速度抖动区间（免整齐如尺；同一瞬间的线速不完全一致才像高速掠过）。</summary>
    private const float JitterMin = 0.75f;
    private const float JitterMax = 1.25f;

    /// <summary>线长 = 线速 × 该系数（1800px/s → ≈144px）。「长度与当前速度相关」的第二半
    /// 是 <see cref="LengthScaleMin"/>..<see cref="LengthScaleMax"/>：按星野当前滚动倍率伸缩。</summary>
    private const float LengthPerSpeed = 0.08f;
    private const float LengthScaleMin = 0.6f;
    private const float LengthScaleMax = 2.0f;

    /// <summary>数量上界（手改 JSON 写出巨值不炸分配；同 Starfield / 纵深背景的计数口径）。</summary>
    private const int MaxCount = 256;

    /// <summary>时长上界（一次性动效的护栏：调成分钟级就成了常驻动效，违反 §1.9.1）。</summary>
    private const float MaxLife = 3.0f;

    /// <summary>线色（冷白，与星野亮星同族；alpha 逐帧另给）。</summary>
    private static readonly Color LineColor = new(0.82f, 0.88f, 1.0f);

    // ---- balance 缓存（`effects.motion.speedline_*`，_Ready 读一次；默认值与 data/balance.json 同值）----
    private int _count = 48;
    private float _alpha = 0.45f;
    private float _life = 1.2f;
    private float _fadeIn = 0.12f;
    private float _fadeOut = 0.5f;
    private float _speed = 1800.0f;

    private readonly RandomNumberGenerator _rng = new();

    // 逐线状态（_Ready 分配，触发时重掷，逐帧原地改写——热路径零分配）
    private Vector2[] _lines = Array.Empty<Vector2>(); // 2N 点：单条 DrawMultiline 的全部线段
    private float[] _x = Array.Empty<float>();
    private float[] _y = Array.Empty<float>();
    private float[] _velocityK = Array.Empty<float>();
    private float[] _len = Array.Empty<float>();

    // 触发时刻的可见世界区域快照（含锚点，zoom 感知）：线带 = 可见区上方一屏 + 可见区，
    // 出带底回绕到带顶（回绕落点恒在屏外，无突现）
    private Rect2 _area;
    private float _bandTop;
    private float _band;

    private float _t;
    private float _strength;
    private float _curAlpha;

    public override void _Ready()
    {
        ZIndex = -1; // 背景层之内、实体之外（判据 3）
        ProcessMode = ProcessModeEnum.Always; // 返航跃迁的触发点紧跟整树暂停，见类注释
        LoadCfg();
        _rng.Seed = LayoutSeed;
        _lines = new Vector2[_count * 2];
        _x = new float[_count];
        _y = new float[_count];
        _velocityK = new float[_count];
        _len = new float[_count];
        Stop(); // 静止态：不逐帧、不绘制（零成本），一切重掷都发生在 Burst
    }

    /// <summary>数值配置缓存（启动一次读入；判型 + 钳制，手改非法值不崩，同 Starfield 口径）。</summary>
    private void LoadCfg()
    {
        _count = CfgFx.Int("effects.motion.speedline_count", _count, 0, MaxCount);
        _alpha = CfgFx.Float("effects.motion.speedline_alpha", _alpha, 0.0f, AlphaCeiling);
        _life = CfgFx.Float("effects.motion.speedline_life", _life, CfgFx.IntervalFloor, MaxLife);
        // 淡入淡出各自钳到时长以内：两者之和超过时长时保留下方的包络兜底（不产生负 alpha）
        _fadeIn = CfgFx.Float("effects.motion.speedline_fade_in", _fadeIn, 0.0f, _life);
        _fadeOut = CfgFx.Float("effects.motion.speedline_fade_out", _fadeOut, 0.0f, _life);
        _speed = CfgFx.Float("effects.motion.speedline_speed", _speed, 1.0f, 20000.0f);
    }

    /// <summary>
    /// 触发一次速度线。
    /// </summary>
    /// <param name="strength">本次强度 0..1（事件档位：Boss 入场满档，遭遇/母舰次之）——
    /// 只缩放峰值 alpha，不改速度与时长。</param>
    /// <param name="speedK">当前星野滚动速度倍率（<see cref="Starfield.ScrollK"/>；同帧内刚改过拉伸
    /// 倍率的调用方传 <see cref="Starfield.CurrentScrollK"/> 取现算值）：线长按它伸缩，
    /// 使速度线与同屏的星野读起来是同一件事（跃迁倍率会被钳到
    /// <see cref="LengthScaleMax"/>，免得线长到横贯全屏）。</param>
    public void Burst(float strength, float speedK)
    {
        // 动效强度 0 不触发（判据 6：0 ＝ 回到本批次之前的画面）；取不到节奏服务
        // （标题屏/拆树期）按 0 处理，与其余消费方的中性口径一致
        var intensity = VisualRhythm.Instance?.Intensity ?? 0.0f;
        var s = Mathf.Clamp(strength, 0.0f, 1.0f) * intensity;
        if (_count <= 0 || s <= 0.0f)
        {
            return;
        }

        _area = GameState.Instance.ViewWorldRect();
        _band = Mathf.Max(_area.Size.Y, 0.0f) * 2.0f;
        _bandTop = _area.Position.Y - _area.Size.Y;
        var lengthScale = float.IsFinite(speedK) ? Mathf.Clamp(speedK, LengthScaleMin, LengthScaleMax) : 1.0f;
        for (var i = 0; i < _count; i++)
        {
            var jitter = _rng.RandfRange(JitterMin, JitterMax);
            _velocityK[i] = jitter;
            _x[i] = _area.Position.X + _rng.Randf() * _area.Size.X;
            _y[i] = _bandTop + _rng.Randf() * _band;
            _len[i] = _speed * jitter * LengthPerSpeed * lengthScale;
        }

        // 重复触发只是把进度拨回 0：本动效是「掠过一次」，不做叠加（叠幅度会让密集事件越来越亮，
        // 与「最亮＝最危险」的读法冲突，同舰体扫光的处置）
        _strength = s;
        _t = 0.0f;
        _curAlpha = 0.0f;
        Visible = true;
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _t += d;
        if (_t >= _life)
        {
            Stop();
            return;
        }

        _curAlpha = _alpha * _strength * Envelope(_t);
        var bottom = _area.Position.Y + _area.Size.Y;
        for (var i = 0; i < _count; i++)
        {
            var y = _y[i] + _speed * _velocityK[i] * d;
            if (_band > 0.0f && y > bottom)
            {
                y -= _band; // 回绕落点 = 带顶一侧（屏外），故不会在可见区里突现
            }

            _y[i] = y;
            _lines[i * 2] = new Vector2(_x[i], y);
            _lines[i * 2 + 1] = new Vector2(_x[i], y - _len[i]); // 尾端在运动反方向（向下飞 → 尾在上）
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_curAlpha <= 0.0f)
        {
            return; // 淡入首帧 / 淡出末帧：alpha 归零时整批不提交
        }

        // 一条 DrawMultiline 画完全部线段（N 条线 → 1 次绘制调用）
        DrawMultiline(_lines, new Color(LineColor, _curAlpha), LineWidth);
    }

    /// <summary>峰值包络（0..1）：淡入线性升、中段满、淡出线性降。
    /// 两段重叠（淡入点晚于淡出起点，手改 JSON 写出 fade_in+fade_out &gt; life）时取淡入点作为
    /// 淡出起点——包络保持单调、不出现负值或二次抬升。</summary>
    private float Envelope(float t)
    {
        if (_fadeIn > 0.0f && t < _fadeIn)
        {
            return t / _fadeIn;
        }

        var fadeStart = Mathf.Max(_fadeIn, _life - _fadeOut);
        if (_fadeOut > 0.0f && t > fadeStart)
        {
            return Mathf.Clamp((_life - t) / _fadeOut, 0.0f, 1.0f);
        }

        return 1.0f;
    }

    /// <summary>回到静止态（零成本）：不可见 + 不逐帧。淡出末帧的残留靠 Visible=false 从绘制列表移除，
    /// 不依赖「最后一帧画成透明」——半途停下会留下一道常驻亮线，只在画面上看得见（引擎零报错）。</summary>
    private void Stop()
    {
        _t = 0.0f;
        _strength = 0.0f;
        _curAlpha = 0.0f;
        Visible = false;
        SetProcess(false);
    }
}
