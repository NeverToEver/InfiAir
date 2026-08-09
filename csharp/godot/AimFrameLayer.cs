using Godot;

namespace InfiAir;

/// <summary>
/// 辅助瞄准框覆盖层（P1-1，M3b 全量迁移，2026-08-08 自 scripts/aim_frame_layer.gd 迁移）：
/// 世界坐标单节点，main.gd _ready 运行时创建挂 Main 下（tutorial.gd 同款，登记
/// GameState.aim_frame_layer）。
/// 每帧一次 _draw 遍历 GameState.enemies 中带 aim_marked 的 Enemy 统一画四角 bracket 框
/// （单节点零逐敌节点开销）；框半径 = 碰撞半径 + frame_pad（指示器族，frame_pad 不乘
/// world_scale）。青色强对比 + 低频频闪；准星入框的个体框转金色高亮（即时反馈
/// 「追踪已生效」）。Boss/炮塔/编队战机非 Enemy 类，is 判定天然排除；精英纳入。
/// 语义保持：P1-3 磁吸/锥形弱追踪/输入反比/距离衰减（Player.dist_falloff_curve 单实现）；
/// marked_target_at 渲染帧缓存；player_ref/enemies 每渲染帧一次静态缓存（Enemy.cs
/// CachedPlayer 模式，避免逐帧跨语言动态访问）。
/// </summary>
public partial class AimFrameLayer : Node2D
{
    private const float ArmRatio = 0.45f;  // bracket 单臂长占框半宽比例
    private const float Width = 2.0f;
    private static readonly Color FrameColor = new(0.35f, 0.95f, 1.0f);
    private static readonly Color FrameColorHover = new(1.0f, 0.85f, 0.35f);
    /// <summary>bracket 四角符号（静态复用，_draw 零分配）。</summary>
    private static readonly float[] SignValues = { -1.0f, 1.0f };

    /// <summary>当前档位辅助框内边距（balance.json player.aim_assist.levels，信号联动刷新）。</summary>
    private float _framePad = 16.0f;
    /// <summary>P1-3：准星磁吸档位参数（同 levels 前缀、同信号刷新）。</summary>
    private float _magnetRange = 100.0f;
    private float _magnetStrength = 6.0f;
    private float _magnetMaxSpeed = 8.0f;
    /// <summary>P1-3：磁吸输入阈值与距离衰减全局参数（player.aim_assist.input / falloff，_ready 一次缓存）。</summary>
    private float _magnetInputMin = 2.0f;
    private float _magnetInputFull = 40.0f;
    private float _falloffPeak = 400.0f;
    private float _falloffEnd = 1400.0f;
    private float _falloffMin = 0.3f;
    private Enemy? _hover;  // 本帧准星入框的标记敌（高亮显示用）
    /// <summary>P1-3：marked_target_at 渲染帧缓存（player.aim_point 与 aim_frame._process 同帧各调一次，
    /// 命中缓存免重复 O(enemies) 扫描）。</summary>
    private ulong _targetCacheFrame = ulong.MaxValue;
    private Enemy? _targetCacheResult;

    private readonly Callable _onAimAssistChanged;

    /// <summary>热路径缓存：player_ref / enemies 每渲染帧一次动态调用（单实例共享，帧内复用）。
    /// U07：静态 Variant/Array 持 Godot 对象引用改实例字段（悬空访问 + 退出 finalize 触碰风险）。</summary>
    private ulong _cacheFrame = ulong.MaxValue;
    private Variant _framePlayer;
    private Godot.Collections.Array _frameEnemies = new();

    /// <summary>U14：meta 键静态缓存（每敌每帧 HasMeta/GetMeta 字符串字面量转换开销）。</summary>
    private static readonly StringName MetaAimFrameRadius = new("aim_frame_radius");

    public AimFrameLayer()
    {
        _onAimAssistChanged = Callable.From<StringName>(OnAimAssistLevelChanged);
    }

    /// <summary>player_ref / enemies 每渲染帧一次动态调用缓存（帧内复用；M7 后改 typed 直调）。</summary>
    private Godot.Collections.Array CachedEnemies()
    {
        var frame = Engine.GetProcessFrames();
        if (frame != _cacheFrame)
        {
            _cacheFrame = frame;
            _framePlayer = GameState.Instance.PlayerRef!;
            _frameEnemies = (Godot.Collections.Array)GameState.Instance.Enemies;
        }

        return _frameEnemies;
    }

    private Player? CachedPlayer()
    {
        CachedEnemies();
        return _framePlayer.AsGodotObject() as Player;
    }

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
        _falloffPeak = (float)GameState.Instance.Cfg("player.aim_assist.falloff.peak", _falloffPeak).AsDouble();
        _falloffEnd = (float)GameState.Instance.Cfg("player.aim_assist.falloff.end", _falloffEnd).AsDouble();
        _falloffMin = (float)GameState.Instance.Cfg("player.aim_assist.falloff.min", _falloffMin).AsDouble();
        if (gs != null)
        {
            gs.Connect("AimAssistChanged", _onAimAssistChanged);
        }
    }

    public override void _ExitTree()
    {
        // G016：显式断开档位信号（对齐 player.gd C22 模式），节点未 free 重新入树不重复连接
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (gs.IsConnected("AimAssistChanged", _onAimAssistChanged))
            {
                gs.Disconnect("AimAssistChanged", _onAimAssistChanged);
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
        var p = CachedPlayer();
        _hover = p != null ? MarkedTargetAt(p.AimPoint()) : null;
        QueueRedraw();
    }

    /// <summary>框半宽：碰撞半径（机体尺寸族，setup 已 ×ws 写入 meta）+ frame_pad
    /// A7：测试/诊断白盒断言经公开接口。</summary>
    public float FrameHalfSize(Enemy e)
    {
        // C23：碰撞半径经 meta 缓存——setup 后恒定（仅 scale.x 随缩放变化），
        // 避免 _draw/扫描路径每帧 get_node_or_null("CollisionShape2D")。
        // 2026-08-03 审计：meta 值已在 enemy.setup 乘过 world_scale，此处不得再乘 e.scale.x
        //（scale.x 同样含 ws，再乘即 ws 平方，0.5 钳制恰好掩盖；ws 上调时框尺寸非线性暴涨）
        // U14：meta 键静态 StringName 缓存（每敌每帧 HasMeta/GetMeta 字符串字面量转换开销）
        var r = 0.0f;
        if (!e.HasMeta(MetaAimFrameRadius))
        {
            var shapeNode = e.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            var rBase = 0.0f;
            if (shapeNode != null && shapeNode.Shape is CircleShape2D)
            {
                rBase = ((CircleShape2D)shapeNode.Shape).Radius;
            }

            e.SetMeta(MetaAimFrameRadius, rBase);
        }

        r = (float)e.GetMeta(MetaAimFrameRadius).AsDouble();
        return r + _framePad;
    }

    /// <summary>当前档位辅助框内边距（A7：测试/诊断白盒断言经公开接口）。</summary>
    public float FramePad() => _framePad;

    /// <summary>世界坐标点命中的标记敌：方形框包含判定，多重叠时取框心最近者；无命中返回 null。
    /// P1-3：同渲染帧缓存（aim_point 平滑推点与 _process 高亮各查一次，帧内结果一致；
    /// 2026-08-07 起按帧共享帧首结果，见字段注释）。</summary>
    public Enemy? MarkedTargetAt(Vector2 point)
    {
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
            if (arr[i].AsGodotObject() is not Enemy e || !e.AimMarked)
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

    /// <summary>P1-3 准星磁吸修正向量：把准星轻微拉向最近框外标记敌（框内归 stickiness 管辖）。
    /// 静止/抖动（|delta| &lt; input_min）与高速甩枪（&gt;= input_full）直接返回 ZERO——输入优先，
    /// 静止无磁吸天然满足；强度 = strength × (1 - 框沿距/range) × 输入 smoothstep × 距离衰减，
    /// 钳到 max_speed 防瞬移。无标记敌返回 ZERO。热路径无 sin/cos、无 cfg。</summary>
    public Vector2 MagnetPull(Vector2 point, Vector2 inputDelta)
    {
        var ilen = inputDelta.Length();
        if (ilen < _magnetInputMin || ilen >= _magnetInputFull)
        {
            return Vector2.Zero;
        }

        Enemy? best = null;
        var bestD = float.PositiveInfinity;
        var arr = CachedEnemies();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i].AsGodotObject() is not Enemy e || !e.AimMarked)
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

            var d = new Vector2(dx, dy).Length();
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

    /// <summary>P1-3 锥形弱追踪查询：从 origin 沿 aim_dir（单位向量）锥角（cone_cos 余弦值）内的最近标记敌；
    /// 距离超过 falloff.end 硬截止（远距不误绑）；无命中返回 null。O(enemies) 与 marked_target_at 同级。</summary>
    public Enemy? NearestConeTarget(Vector2 origin, Vector2 aimDir, float coneCos)
    {
        Enemy? best = null;
        var bestD = float.PositiveInfinity;
        var arr = CachedEnemies();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i].AsGodotObject() is not Enemy e || !e.AimMarked)
            {
                continue;
            }

            var to = e.GlobalPosition - origin;
            var d = to.Length();
            if (d > _falloffEnd || d >= bestD)
            {
                continue;
            }

            // 与原点重合时 to/d 除零（NaN）→ NaN < cone_cos 恒 false，同原 GDScript 语义
            if (aimDir.Dot(to / d) < coneCos)
            {
                continue;
            }

            bestD = d;
            best = e;
        }

        return best;
    }

    /// <summary>P1-3 距离衰减（G018：与 Player.aim_dist_falloff 共用 Player.dist_falloff_curve 单实现）。</summary>
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
            if (arr[i].AsGodotObject() is not Enemy e || !e.AimMarked)
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

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public float frame_pad() => FramePad();
}
