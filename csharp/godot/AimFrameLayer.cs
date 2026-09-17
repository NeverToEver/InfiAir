using Godot;
using InfiAir.Core.Combat;

namespace InfiAir;

/// <summary>
/// 辅助瞄准框覆盖层：
/// 世界坐标单节点，Main._Ready 运行时创建挂 Main 下（Tutorial 同款，登记
/// GameState.AimFrameLayer）。
/// 每帧一次 _draw 遍历 GameState.enemies 中带标记的可瞄准目标统一画四角 bracket 框
/// （单节点零逐敌节点开销）；框半径 = 碰撞半径 + frame_pad（指示器族，frame_pad 不乘
/// world_scale）。青色强对比 + 低频频闪；准星入框的个体框转金色高亮（即时反馈
/// 「追踪已生效」）。
/// 扫描口径 = <see cref="IAimTarget"/> 契约：普通敌机、精英、遭遇单位（炮塔/编队机）同一路径，
/// Boss 不实现契约故天然排除（既有例外，不扩大）。仅 <see cref="AimTargetCount.Marked"/> 为零时
/// 整表跳过（零标记是常态）。
/// 语义：磁吸/锥形弱追踪/输入反比/距离衰减（Player.dist_falloff_curve 单实现）；
/// marked_target_at 渲染帧缓存；player_ref/enemies 每渲染帧一次静态缓存（Enemy.cs
/// CachedPlayer 模式，避免逐帧跨语言动态访问）。
/// </summary>
public partial class AimFrameLayer : Node2D
{
    private const float ArmRatio = 0.45f;  // bracket 单臂长占框半宽比例
    private const float Width = 2.0f;
    private static readonly Color FrameColor = UITheme.AimAmberLit;
    private static readonly Color FrameColorHover = UITheme.AimAmberHot;
    /// <summary>bracket 四角符号（静态复用，_draw 零分配）。</summary>
    private static readonly float[] SignValues = { -1.0f, 1.0f };

    /// <summary>当前档位辅助框内边距（balance.json player.aim_assist.levels，信号联动刷新）。</summary>
    private float _framePad = 16.0f;
    /// <summary>准星磁吸档位参数（同 levels 前缀、同信号刷新）。负值属配置损坏，读取处按
    /// core <see cref="AimAssistParams.NonNegativeOr"/> 回退默认——钳成 0 等于磁吸失效/反向
    /// （护栏加在诊断读口上而不在这里，就是原缺陷的形态）。</summary>
    private float _magnetRange = 100.0f;
    private float _magnetStrength = 6.0f;
    private float _magnetMaxSpeed = 8.0f;
    /// <summary>磁吸输入阈值与距离衰减全局参数（player.aim_assist.input / falloff，_ready 一次缓存）。</summary>
    private float _magnetInputMin = 2.0f;
    private float _magnetInputFull = 40.0f;
    private float _falloffPeak = 400.0f;
    private float _falloffEnd = 1400.0f;
    private float _falloffMin = 0.3f;
    private IAimTarget? _hover;  // 本帧准星入框的标记目标（高亮显示用）
    /// <summary>marked_target_at 渲染帧缓存（player.aim_point 与 aim_frame._process 同帧各调一次，
    /// 命中缓存免重复 O(enemies) 扫描）。</summary>
    private ulong _targetCacheFrame = ulong.MaxValue;
    private IAimTarget? _targetCacheResult;

    private readonly Callable _onAimAssistChanged;

    /// <summary>热路径缓存：enemies 每渲染帧一次取注册表引用（单实例共享，帧内复用；
    /// 注册表是托管 List，取用零封送）。不得用静态集合持 Godot 对象引用
    /// （悬空访问 + 退出 finalize 触碰风险）。</summary>
    private ulong _cacheFrame = ulong.MaxValue;
    private List<Node2D> _frameEnemies = new();

    /// <summary>上帧是否存在标记敌（归零边界补一帧重绘清残框用）。</summary>
    private bool _hadMarked;

    /// <summary>模拟时间（秒，本节点累计 _Process delta）：框低频闪相位基准，替代墙钟
    /// （帧率/机器性能无关；闪频与原墙钟版一致 4Hz）。</summary>
    private float _simTime;

    public AimFrameLayer()
    {
        _onAimAssistChanged = Callable.From<StringName>(OnAimAssistLevelChanged);
    }

    /// <summary>enemies 每渲染帧一次取注册表引用（帧内复用，免逐次 GameState 单例校验）。</summary>
    private List<Node2D> CachedEnemies()
    {
        var frame = Engine.GetProcessFrames();
        if (frame != _cacheFrame)
        {
            _cacheFrame = frame;
            _frameEnemies = GameState.Instance.Enemies;
        }

        return _frameEnemies;
    }

    /// <summary>player 直读 typed 属性（O(1)，无需缓存；统一入口见 FrameCache.cs）。</summary>
    private Player? CachedPlayer() => FrameCache.Player() as Player;

    public override void _Ready()
    {
        ZIndex = 9;  // 世界实体之上、准星（10）之下
        var gs = GameState.Instance;
        if (gs != null)
        {
            gs.AimFrameLayer = this;
        }

        LoadLevelParams();
        // 输入窗口与衰减域一律经 core AimAssistParams 钳（与 Player.LoadAimAssistParams 同口径）：
        // 负值属配置损坏，回退默认而非钳 0（0 是「机制关闭」的合法取值，与损坏不是一个语义）
        (_magnetInputMin, _magnetInputFull) = AimAssistParams.MagnetWindow(
            (float)GameState.Instance.Cfg("player.aim_assist.input.magnet_input_min", _magnetInputMin).AsDouble(),
            (float)GameState.Instance.Cfg("player.aim_assist.input.magnet_input_full", _magnetInputFull).AsDouble(),
            _magnetInputMin,
            _magnetInputFull);
        _falloffPeak = AimAssistParams.NonNegativeOr(
            (float)GameState.Instance.Cfg("player.aim_assist.falloff.peak", _falloffPeak).AsDouble(), _falloffPeak);
        _falloffEnd = AimAssistParams.NonNegativeOr(
            (float)GameState.Instance.Cfg("player.aim_assist.falloff.end", _falloffEnd).AsDouble(), _falloffEnd);
        _falloffMin = AimAssistParams.NonNegativeOr(
            (float)GameState.Instance.Cfg("player.aim_assist.falloff.min", _falloffMin).AsDouble(), _falloffMin);
        if (gs != null)
        {
            gs.Connect(GameState.SignalName.AimAssistChanged, _onAimAssistChanged);
        }
    }

    public override void _ExitTree()
    {
        // 显式断开档位信号（Player 同款），节点未 free 重新入树不重复连接
        // autoload 可能先于本节点释放（非常规拆树序），Instance getter 会抛异常，故安全取值
        var gs = GameState.TryGetInstance();
        if (gs != null)
        {
            if (gs.IsConnected(GameState.SignalName.AimAssistChanged, _onAimAssistChanged))
            {
                gs.Disconnect(GameState.SignalName.AimAssistChanged, _onAimAssistChanged);
            }

            if (gs.AimFrameLayer == this)
            {
                gs.AimFrameLayer = null;  // 清注册
            }
        }
    }

    /// <summary>档位参数重读（_ready 初始 + aim_assist_changed 信号驱动）。</summary>
    private void LoadLevelParams()
    {
        var basePath = "player.aim_assist.levels." + GameState.Instance.AimAssistLevel.ToString() + ".";
        // 负值口径见字段注释：损坏回退默认，不是钳 0
        _framePad = AimAssistParams.NonNegativeOr(
            (float)GameState.Instance.Cfg(basePath + "frame_pad", _framePad).AsDouble(), _framePad);
        _magnetRange = AimAssistParams.NonNegativeOr(
            (float)GameState.Instance.Cfg(basePath + "magnet_range", _magnetRange).AsDouble(), _magnetRange);
        _magnetStrength = AimAssistParams.NonNegativeOr(
            (float)GameState.Instance.Cfg(basePath + "magnet_strength", _magnetStrength).AsDouble(), _magnetStrength);
        _magnetMaxSpeed = AimAssistParams.NonNegativeOr(
            (float)GameState.Instance.Cfg(basePath + "magnet_max_speed", _magnetMaxSpeed).AsDouble(), _magnetMaxSpeed);
    }

    private void OnAimAssistLevelChanged(StringName level)
    {
        LoadLevelParams();
    }

    public override void _Process(double delta)
    {
        _simTime += (float)delta; // 闪相位基准（模拟时间），见字段注释
        // 无标记目标常态跳过扫描+重绘（否则每渲染帧无条件 MarkedTargetAt + QueueRedraw）；
        // 归零当帧补一次重绘清残框
        if (AimTargetCount.Marked == 0)
        {
            _hover = null;
            if (_hadMarked)
            {
                _hadMarked = false;
                QueueRedraw();
            }

            return;
        }

        _hadMarked = true;
        var p = CachedPlayer();
        // 只读取准点：本处是 hover 查询，不回写系统光标（回写是推点方的事，且瞄准非活跃时
        // 回写会把可见光标拖住——见 Player.AimActive）
        _hover = p != null ? MarkedTargetAt(p.AimPointNoWarp()) : null;
        QueueRedraw();
    }

    /// <summary>框半宽：目标碰撞半径（机体尺寸族，已含 world_scale）+ frame_pad
    /// ——实机调参阅数读口（框尺寸与标靶视觉对齐的观察面）。算式在 core AimTargeting。</summary>
    public float FrameHalfSize(IAimTarget t) => AimTargeting.FrameHalfSize(t.AimCollisionRadius, _framePad);

    /// <summary>世界坐标点命中的标记目标：方形框包含判定，多重叠时取框心最近者；无命中返回 null。
    /// 同渲染帧缓存（aim_point 平滑推点与 _process 高亮各查一次，帧内结果一致；
    /// 按帧共享帧首结果，见字段注释）。</summary>
    public IAimTarget? MarkedTargetAt(Vector2 point)
    {
        // 零标记早退（外部调用方不经 _Process 门控）
        if (AimTargetCount.Marked == 0)
        {
            return null;
        }

        var frame = Engine.GetProcessFrames();
        if (frame == _targetCacheFrame)
        {
            return _targetCacheResult;
        }

        IAimTarget? best = null;
        var bestSq = float.PositiveInfinity;
        var arr = CachedEnemies();
        for (var i = 0; i < arr.Count; i++)
        {
            // 注册表含 Enemy 与 Boss，扫描按契约判型：Boss 不实现契约（既有例外），
            // 遭遇单位（炮塔/编队机）与普通敌机同一路径
            if (arr[i] is not IAimTarget t || !t.AimMarked)
            {
                continue;
            }

            var center = t.AimWorldPosition;
            if (!AimTargeting.InFrame(point.X, point.Y, center.X, center.Y, FrameHalfSize(t)))
            {
                continue;
            }

            var dSq = point.DistanceSquaredTo(center);
            if (dSq < bestSq)
            {
                bestSq = dSq;
                best = t;
            }
        }

        _targetCacheFrame = frame;
        _targetCacheResult = best;
        return best;
    }

    /// <summary>准星磁吸修正向量：把准星轻微拉向最近框外标记目标（框内归 stickiness 管辖）。
    /// 静止/抖动（|delta| &lt; input_min）与高速甩枪（&gt;= input_full）直接返回 ZERO——输入优先，
    /// 静止无磁吸天然满足；强度 = strength × (1 - 框沿距/range) × 输入 smoothstep × 距离衰减，
    /// 钳到 max_speed 防瞬移。无标记目标返回 ZERO。热路径无 sin/cos、无 cfg。</summary>
    public Vector2 MagnetPull(Vector2 point, Vector2 inputDelta)
    {
        var ilen = inputDelta.Length();
        // 零标记早退（省整表扫描）
        if (ilen < _magnetInputMin || ilen >= _magnetInputFull || AimTargetCount.Marked == 0)
        {
            return Vector2.Zero;
        }

        IAimTarget? best = null;
        var bestD = float.PositiveInfinity;
        var arr = CachedEnemies();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not IAimTarget t || !t.AimMarked)
            {
                continue;
            }

            var center = t.AimWorldPosition;
            var half = FrameHalfSize(t);
            var dx = Mathf.Abs(point.X - center.X) - half;
            var dy = Mathf.Abs(point.Y - center.Y) - half;
            if (dx <= 0.0f && dy <= 0.0f)
            {
                continue;  // 框内（0 = 框内，归 stickiness 不磁吸）
            }

            if (dx > _magnetRange || dy > _magnetRange)
            {
                continue;  // 轴距粗筛，省 sqrt
            }

            // 框沿距只取框外分量（算式在 core AimTargeting）：单轴出框时另一轴为负，
            // 计入欧氏长度会系统性偏近（磁吸偏弱、range 边界误判）
            var d = AimTargeting.FrameEdgeDistance(point.X, point.Y, center.X, center.Y, half);
            if (d >= bestD || d > _magnetRange)
            {
                continue;
            }

            bestD = d;
            best = t;
        }

        if (best == null)
        {
            return Vector2.Zero;
        }

        var target = best.AimWorldPosition;
        var inputRatio = (ilen - _magnetInputMin) / (_magnetInputFull - _magnetInputMin);
        var inputScale = 1.0f - inputRatio * inputRatio * (3.0f - 2.0f * inputRatio);  // smoothstep：慢速精瞄全辅助，快速甩枪退出
        var p = CachedPlayer();
        var falloff = DistFalloff(target.DistanceTo(p != null ? p.GlobalPosition : point));
        var mag = _magnetStrength * (1.0f - bestD / _magnetRange) * inputScale * falloff;
        return (target - point).Normalized() * Mathf.Min(mag, _magnetMaxSpeed);
    }

    /// <summary>锥形弱追踪查询：从 origin 沿 aim_dir（单位向量）锥角（cone_cos 余弦值）内的最近目标；
    /// 距离超过 falloff.end 硬截止（远距不误绑）；无命中返回 null。O(enemies) 与 marked_target_at 同级。
    /// **覆盖全部存活目标**（不限标记目标）：弱追踪的定位是「轻微修正瞄偏」，给所有可瞄准目标都生效；
    /// 标记只决定框显式与框内强追踪，不是弱追踪的准入门槛（早期按 aim_marked 过滤时，约七成敌机
    /// 完全拿不到辅助，玩家实感即「弱辅瞄时有时无」；遭遇单位同属「屏上可打目标」，同一路径）。</summary>
    public IAimTarget? NearestConeTarget(Vector2 origin, Vector2 aimDir, float coneCos)
    {
        IAimTarget? best = null;
        var bestD = float.PositiveInfinity;
        var arr = CachedEnemies();
        for (var i = 0; i < arr.Count; i++)
        {
            // 契约判型：Boss 不实现（既有例外），遭遇单位与敌机同一路径
            if (arr[i] is not IAimTarget t || !t.AimTargetable)
            {
                continue;
            }

            var target = t.AimWorldPosition;
            var to = target - origin;
            var d = to.Length();
            if (d > _falloffEnd || d >= bestD)
            {
                continue;
            }

            // 与原点重合（to/0 得 NaN）或锥阈值为 NaN 时不得把目标排除，见 core AimTargeting.InCone
            if (!AimTargeting.InCone(aimDir.X, aimDir.Y, to.X, to.Y, coneCos))
            {
                continue;
            }

            bestD = d;
            best = t;
        }

        return best;
    }

    /// <summary>距离衰减（与 Player.aim_dist_falloff 共用 Player.dist_falloff_curve 单实现）。</summary>
    private float DistFalloff(float d)
    {
        return Player.DistFalloffCurve(d, _falloffPeak, _falloffEnd, _falloffMin);
    }

    public override void _Draw()
    {
        var flicker = 0.55f + 0.35f * Enemy.SinFast(_simTime * 4.0f);
        var arr = CachedEnemies();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not IAimTarget t || !t.AimMarked)
            {
                continue;
            }

            var c = (ReferenceEquals(t, _hover) ? FrameColorHover : FrameColor) * new Color(1.0f, 1.0f, 1.0f, flicker);
            DrawBracket(t.AimWorldPosition, FrameHalfSize(t), c);
        }
    }

    /// <summary>画四角 bracket 框（指示器族，不乘 world_scale）。</summary>
    private void DrawBracket(Vector2 center, float half, Color c)
    {
        var arm = half * ArmRatio;
        foreach (var sx in SignValues)
        {
            foreach (var sy in SignValues)
            {
                var corner = center + new Vector2(sx * half, sy * half);
                DrawLine(corner, corner - new Vector2(sx * arm, 0.0f), c, Width, true);
                DrawLine(corner, corner - new Vector2(0.0f, sy * arm), c, Width, true);
            }
        }
    }
}
