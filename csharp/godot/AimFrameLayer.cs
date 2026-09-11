using Godot;

namespace InfiAir;

/// <summary>
/// 辅助瞄准框覆盖层：
/// 世界坐标单节点，Main._Ready 运行时创建挂 Main 下（Tutorial 同款，登记
/// GameState.AimFrameLayer）。
/// 每帧一次 _draw 遍历 GameState.enemies 中带 aim_marked 的 Enemy 统一画四角 bracket 框
/// （单节点零逐敌节点开销）；框半径 = 碰撞半径 + frame_pad（指示器族，frame_pad 不乘
/// world_scale）。青色强对比 + 低频频闪；准星入框的个体框转金色高亮（即时反馈
/// 「追踪已生效」）。Boss/炮塔/编队战机非 Enemy 类，is 判定天然排除；精英纳入。
/// 语义：磁吸/锥形弱追踪/输入反比/距离衰减（Player.dist_falloff_curve 单实现）；
/// marked_target_at 渲染帧缓存；player_ref/enemies 每渲染帧一次静态缓存（Enemy.cs
/// CachedPlayer 模式，避免逐帧跨语言动态访问）。
/// </summary>
public partial class AimFrameLayer : Node2D
{
    private const float ArmRatio = 0.45f;  // bracket 单臂长占框半宽比例
    private const float Width = 2.0f;
    private static readonly Color FrameColor = new(1.0f, 0.72f, 0.30f);
    private static readonly Color FrameColorHover = new(1.0f, 0.90f, 0.45f);
    /// <summary>bracket 四角符号（静态复用，_draw 零分配）。</summary>
    private static readonly float[] SignValues = { -1.0f, 1.0f };

    /// <summary>当前档位辅助框内边距（balance.json player.aim_assist.levels，信号联动刷新）。</summary>
    private float _framePad = 16.0f;
    /// <summary>准星磁吸档位参数（同 levels 前缀、同信号刷新）。</summary>
    private float _magnetRange = 100.0f;
    private float _magnetStrength = 6.0f;
    private float _magnetMaxSpeed = 8.0f;
    /// <summary>磁吸输入阈值与距离衰减全局参数（player.aim_assist.input / falloff，_ready 一次缓存）。</summary>
    private float _magnetInputMin = 2.0f;
    private float _magnetInputFull = 40.0f;
    private float _falloffPeak = 400.0f;
    private float _falloffEnd = 1400.0f;
    private float _falloffMin = 0.3f;
    private Enemy? _hover;  // 本帧准星入框的标记敌（高亮显示用）
    /// <summary>marked_target_at 渲染帧缓存（player.aim_point 与 aim_frame._process 同帧各调一次，
    /// 命中缓存免重复 O(enemies) 扫描）。</summary>
    private ulong _targetCacheFrame = ulong.MaxValue;
    private Enemy? _targetCacheResult;

    private readonly Callable _onAimAssistChanged;

    /// <summary>热路径缓存：enemies 每渲染帧一次取 typed Array<Node>（单实例共享，帧内复用）。
    /// 不得用静态集合持 Godot 对象引用（悬空访问 + 退出 finalize 触碰风险）。</summary>
    private ulong _cacheFrame = ulong.MaxValue;
    private Godot.Collections.Array<Node> _frameEnemies = new();

    /// <summary>上帧是否存在标记敌（归零边界补一帧重绘清残框用）。</summary>
    private bool _hadMarked;

    public AimFrameLayer()
    {
        _onAimAssistChanged = Callable.From<StringName>(OnAimAssistLevelChanged);
    }

    /// <summary>enemies 每渲染帧一次 typed 缓存（帧内复用，避免逐敌 Variant 拆装箱）。</summary>
    private Godot.Collections.Array<Node> CachedEnemies()
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
        _magnetInputMin = (float)GameState.Instance.Cfg("player.aim_assist.input.magnet_input_min", _magnetInputMin).AsDouble();
        _magnetInputFull = (float)GameState.Instance.Cfg("player.aim_assist.input.magnet_input_full", _magnetInputFull).AsDouble();
        // full 钳到 min 之上——两键相等时 MagnetPull 的 t = 0/0 = NaN 污染准星
        _magnetInputFull = Mathf.Max(_magnetInputFull, _magnetInputMin + 0.01f);
        _falloffPeak = (float)GameState.Instance.Cfg("player.aim_assist.falloff.peak", _falloffPeak).AsDouble();
        _falloffEnd = (float)GameState.Instance.Cfg("player.aim_assist.falloff.end", _falloffEnd).AsDouble();
        _falloffMin = (float)GameState.Instance.Cfg("player.aim_assist.falloff.min", _falloffMin).AsDouble();
        if (gs != null)
        {
            gs.Connect(GameState.SignalName.AimAssistChanged, _onAimAssistChanged);
        }
    }

    public override void _ExitTree()
    {
        // 显式断开档位信号（Player 同款），节点未 free 重新入树不重复连接
        var gs = GameState.Instance;
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
        _framePad = (float)GameState.Instance.Cfg(basePath + "frame_pad", _framePad).AsDouble();
        _magnetRange = (float)GameState.Instance.Cfg(basePath + "magnet_range", _magnetRange).AsDouble();
        _magnetStrength = (float)GameState.Instance.Cfg(basePath + "magnet_strength", _magnetStrength).AsDouble();
        _magnetMaxSpeed = (float)GameState.Instance.Cfg(basePath + "magnet_max_speed", _magnetMaxSpeed).AsDouble();
    }

    private void OnAimAssistLevelChanged(StringName level)
    {
        LoadLevelParams();
    }

    public override void _Process(double delta)
    {
        // 无标记敌常态跳过扫描+重绘（否则每渲染帧无条件 MarkedTargetAt + QueueRedraw）；
        // 归零当帧补一次重绘清残框
        if (Enemy.AimMarkedCount == 0)
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
        _hover = p != null ? MarkedTargetAt(p.AimPoint()) : null;
        QueueRedraw();
    }

    /// <summary>框半宽：碰撞半径（机体尺寸族，setup 已 ×ws 缓存进 Enemy.AimFrameRadius）+ frame_pad
    /// ——实机调参阅数读口（框尺寸与标靶视觉对齐的观察面）。</summary>
    public float FrameHalfSize(Enemy e)
    {
        // 碰撞半径缓存放 Enemy 实例字段——setup 后恒定（仅 scale.x 随缩放变化），
        // 避免 _draw/扫描路径每帧 get_node_or_null("CollisionShape2D")。
        // meta 值已在 enemy.setup 乘过 world_scale，此处不得再乘 e.scale.x
        //（scale.x 同样含 ws，再乘即 ws 平方，0.5 钳制恰好掩盖；ws 上调时框尺寸非线性暴涨）
        // HasMeta/GetMeta 直读实例字段（每敌每扫描 ×3 路）
        var r = e.AimFrameRadius;
        if (r < 0.0f)
        {
            // 未经 setup 的兼容路径：回退读碰撞形状并回填（meta 缺键回退同款语义）
            var shapeNode = e.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            r = 0.0f;
            if (shapeNode != null && shapeNode.Shape is CircleShape2D circle)
            {
                r = circle.Radius;
            }

            e.AimFrameRadius = r;
        }

        return r + _framePad;
    }

    /// <summary>世界坐标点命中的标记敌：方形框包含判定，多重叠时取框心最近者；无命中返回 null。
    /// 同渲染帧缓存（aim_point 平滑推点与 _process 高亮各查一次，帧内结果一致；
    /// 按帧共享帧首结果，见字段注释）。</summary>
    public Enemy? MarkedTargetAt(Vector2 point)
    {
        // 零标记早退（外部调用方不经 _Process 门控）
        if (Enemy.AimMarkedCount == 0)
        {
            return null;
        }

        var frame = Engine.GetProcessFrames();
        if (frame == _targetCacheFrame)
        {
            return _targetCacheResult;
        }

        Enemy? best = null;
        var bestSq = float.PositiveInfinity;
        var arr = CachedEnemies();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not Enemy e || !e.AimMarked)
            {
                continue;  // 注册表含 Enemy 与 Boss，Boss 非 Enemy 类——is 判定语义等价排除
            }

            var half = FrameHalfSize(e);
            var d = (point - e.GlobalPosition).Abs();
            if (d.X > half || d.Y > half)
            {
                continue;
            }

            var dSq = point.DistanceSquaredTo(e.GlobalPosition);
            if (dSq < bestSq)
            {
                bestSq = dSq;
                best = e;
            }
        }

        _targetCacheFrame = frame;
        _targetCacheResult = best;
        return best;
    }

    /// <summary>准星磁吸修正向量：把准星轻微拉向最近框外标记敌（框内归 stickiness 管辖）。
    /// 静止/抖动（|delta| &lt; input_min）与高速甩枪（&gt;= input_full）直接返回 ZERO——输入优先，
    /// 静止无磁吸天然满足；强度 = strength × (1 - 框沿距/range) × 输入 smoothstep × 距离衰减，
    /// 钳到 max_speed 防瞬移。无标记敌返回 ZERO。热路径无 sin/cos、无 cfg。</summary>
    public Vector2 MagnetPull(Vector2 point, Vector2 inputDelta)
    {
        var ilen = inputDelta.Length();
        // 零标记早退（省整表扫描）
        if (ilen < _magnetInputMin || ilen >= _magnetInputFull || Enemy.AimMarkedCount == 0)
        {
            return Vector2.Zero;
        }

        Enemy? best = null;
        var bestD = float.PositiveInfinity;
        var arr = CachedEnemies();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not Enemy e || !e.AimMarked)
            {
                continue;
            }

            var half = FrameHalfSize(e);
            // 矩形框沿距（0 = 框内，归 stickiness 不磁吸）
            var dx = Mathf.Abs(point.X - e.GlobalPosition.X) - half;
            var dy = Mathf.Abs(point.Y - e.GlobalPosition.Y) - half;
            if (dx <= 0.0f && dy <= 0.0f)
            {
                continue;
            }

            if (dx > _magnetRange || dy > _magnetRange)
            {
                continue;  // 轴距粗筛，省 sqrt
            }

            // 框沿距负分量钳 0——单轴出框时另一轴为负，计入欧氏长度会
            // 系统性偏近（磁吸偏弱、range 边界误判），标准 AABB 距离只取框外分量
            var d = new Vector2(Mathf.Max(dx, 0.0f), Mathf.Max(dy, 0.0f)).Length();
            if (d >= bestD || d > _magnetRange)
            {
                continue;
            }

            bestD = d;
            best = e;
        }

        if (best == null)
        {
            return Vector2.Zero;
        }

        var t = (ilen - _magnetInputMin) / (_magnetInputFull - _magnetInputMin);
        var inputScale = 1.0f - t * t * (3.0f - 2.0f * t);  // smoothstep：慢速精瞄全辅助，快速甩枪退出
        var p = CachedPlayer();
        var falloff = DistFalloff(best.GlobalPosition.DistanceTo(p != null ? p.GlobalPosition : point));
        var mag = _magnetStrength * (1.0f - bestD / _magnetRange) * inputScale * falloff;
        return (best.GlobalPosition - point).Normalized() * Mathf.Min(mag, _magnetMaxSpeed);
    }

    /// <summary>锥形弱追踪查询：从 origin 沿 aim_dir（单位向量）锥角（cone_cos 余弦值）内的最近标记敌；
    /// 距离超过 falloff.end 硬截止（远距不误绑）；无命中返回 null。O(enemies) 与 marked_target_at 同级。</summary>
    public Enemy? NearestConeTarget(Vector2 origin, Vector2 aimDir, float coneCos)
    {
        // 零标记早退
        if (Enemy.AimMarkedCount == 0)
        {
            return null;
        }

        Enemy? best = null;
        var bestD = float.PositiveInfinity;
        var arr = CachedEnemies();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not Enemy e || !e.AimMarked)
            {
                continue;
            }

            var to = e.GlobalPosition - origin;
            var d = to.Length();
            if (d > _falloffEnd || d >= bestD)
            {
                continue;
            }

            // 与原点重合时 to/d 除零得 NaN，NaN < coneCos 恒 false，该敌不得被排除（会被选中）；
            // coneCos 为 NaN 时同理不排除
            if (aimDir.Dot(to / d) < coneCos)
            {
                continue;
            }

            bestD = d;
            best = e;
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
        var flicker = 0.55f + 0.35f * Enemy.SinFast((float)Time.GetTicksMsec() / 1000.0f * 4.0f);
        var arr = CachedEnemies();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not Enemy e || !e.AimMarked)
            {
                continue;
            }

            var c = (e == _hover ? FrameColorHover : FrameColor) * new Color(1.0f, 1.0f, 1.0f, flicker);
            DrawBracket(e.GlobalPosition, FrameHalfSize(e), c);
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
