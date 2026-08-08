using Godot;

namespace InfiAir;

/// <summary>
/// 敌机移动上下文（M3b 迁移 scripts/enemy_move_strategy.gd 的 ctx，2026-08-08）。
/// A4a/C06：每敌机复用单个实例、字段原地更新，替代 GDScript 每物理帧新建 Dictionary。
/// </summary>
public sealed class MoveCtx
{
    public Rect2 View;
    public float MDelta;
    public float Speed;
    public float Time;
    public float Phase;
    public float SpawnX;
    public float AnchorY;
    public bool Hovering;
    public Node2D? Player;
}

/// <summary>
/// 敌机移动策略基类（A4a 拆分；docs/AUDIT_VAULT.md A4）：各策略自包含的纯位置计算块，
/// 经 ctx 传入共享只读上下文，唯一副作用写 enemy.Position 与少量公开 setter。
/// 纯 C# 类（非 GodotObject）——无外部 GDScript 引用（实测），不注册进引擎。
/// 共享悬停常量经构造 params 注入（Enemy._ready 从 balance 缓存值传入；Q29 策略专属参数覆盖）。
/// </summary>
public abstract class EnemyMoveStrategy
{
    protected float _hoverBobAmp = 12.0f;
    protected float _hoverBobFreq = 2.0f;
    protected float _hoverSwayAmp = 34.0f;
    protected float _hoverSwayFreq = 1.2f;
    protected float _spiralDriftAmp = 56.0f;
    protected float _spiralDriftFreq = 0.7f;
    protected float _spiralRadius = 50.0f;
    protected float _aggressiveChaseSpeed = 140.0f;

    protected EnemyMoveStrategy()
    {
    }

    /// <summary>Q29 参数注入（whitelist 键；freqs/phases 数组长度 ≥3 校验——R07 防越界）。</summary>
    protected EnemyMoveStrategy(Godot.Collections.Dictionary? p)
    {
        if (p == null)
        {
            return;
        }

        ReadShared(p);
    }

    private void ReadShared(Godot.Collections.Dictionary p)
    {
        _hoverBobAmp = GetFloat(p, "hover_bob_amp", _hoverBobAmp);
        _hoverBobFreq = GetFloat(p, "hover_bob_freq", _hoverBobFreq);
        _hoverSwayAmp = GetFloat(p, "hover_sway_amp", _hoverSwayAmp);
        _hoverSwayFreq = GetFloat(p, "hover_sway_freq", _hoverSwayFreq);
        _spiralDriftAmp = GetFloat(p, "spiral_drift_amp", _spiralDriftAmp);
        _spiralDriftFreq = GetFloat(p, "spiral_drift_freq", _spiralDriftFreq);
        _spiralRadius = GetFloat(p, "spiral_radius", _spiralRadius);
        _aggressiveChaseSpeed = GetFloat(p, "aggressive_chase_speed", _aggressiveChaseSpeed);
    }

    protected static float GetFloat(Godot.Collections.Dictionary p, StringName key, float fallback)
    {
        return p.ContainsKey(key) ? (float)p[key].AsDouble() : fallback;
    }

    protected static bool GetBool(Godot.Collections.Dictionary p, StringName key, bool fallback)
    {
        return p.ContainsKey(key) ? p[key].AsBool() : fallback;
    }

    public abstract void Update(float delta, Enemy enemy, MoveCtx ctx);

    /// <summary>共享悬停 y：绕锚点垂直微浮（相位随机错开全波机械感）。</summary>
    protected float HoverY(MoveCtx ctx)
    {
        return ctx.AnchorY + Enemy.SinFast(ctx.Time * _hoverBobFreq + ctx.Phase) * _hoverBobAmp;
    }

    /// <summary>悬停转换查询：dive 冲刺期例外（基类 false）。</summary>
    public virtual bool IsDiving() => false;

    /// <summary>悬停转换查询：spiral 以绕转中心为准（基类 -1 = 用 position.y）。</summary>
    public virtual float HoverReferenceY() => -1.0f;

    /// <summary>重激活/出生状态复位（Enemy._ready / Reactivate 调用）。</summary>
    public virtual void Reset(Enemy enemy)
    {
    }
}

/// <summary>straight / hover 合成策略：直线下压 + 悬停水平摇摆（共享逻辑）。</summary>
public sealed class HoverMove : EnemyMoveStrategy
{
    public HoverMove(Godot.Collections.Dictionary? p)
        : base(p)
    {
    }

    public override void Update(float delta, Enemy enemy, MoveCtx ctx)
    {
        if (ctx.Hovering)
        {
            enemy.Position = new Vector2(
                Mathf.Clamp(
                    ctx.SpawnX + Enemy.SinFast(ctx.Time * _hoverSwayFreq + ctx.Phase) * _hoverSwayAmp,
                    ctx.View.Position.X + 40.0f,
                    ctx.View.End.X - 40.0f),
                HoverY(ctx));
        }
        else
        {
            enemy.Position += new Vector2(0.0f, ctx.Speed * ctx.MDelta);
        }
    }
}

/// <summary>sine：横向正弦 + 悬停微浮（参数：enemies.move_strategies.sine，Q29 入库）。</summary>
public sealed class SineMove : EnemyMoveStrategy
{
    private float _amp = 90.0f;
    private float _freq = 3.0f;

    public SineMove(Godot.Collections.Dictionary? p)
        : base(p)
    {
        if (p == null)
        {
            return;
        }

        _amp = GetFloat(p, "amp", _amp);
        _freq = GetFloat(p, "freq", _freq);
    }

    public override void Update(float delta, Enemy enemy, MoveCtx ctx)
    {
        enemy.Position = new Vector2(ctx.SpawnX + Enemy.SinFast(ctx.Time * _freq + ctx.Phase) * _amp, enemy.Position.Y);
        if (ctx.Hovering)
        {
            enemy.Position = new Vector2(enemy.Position.X, HoverY(ctx));
        }
        else
        {
            enemy.Position += new Vector2(0.0f, ctx.Speed * ctx.MDelta);
        }
    }
}

/// <summary>zigzag：折返横移 + 悬停微浮（参数：enemies.move_strategies.zigzag，Q29 入库）。</summary>
public sealed class ZigzagMove : EnemyMoveStrategy
{
    private float _zigDir = 1.0f;
    private float _zigTimer = 0.7f;
    private float _flipInterval = 0.7f;
    private float _speedScale = 0.9f;
    private float _resetFlipMin = 0.15f;

    public ZigzagMove(Godot.Collections.Dictionary? p)
        : base(p)
    {
        if (p == null)
        {
            return;
        }

        _flipInterval = GetFloat(p, "flip_interval", _flipInterval);
        _speedScale = GetFloat(p, "speed_scale", _speedScale);
        _resetFlipMin = GetFloat(p, "reset_flip_min", _resetFlipMin);
    }

    public override void Update(float delta, Enemy enemy, MoveCtx ctx)
    {
        _zigTimer -= delta;
        if (_zigTimer <= 0.0f)
        {
            _zigDir = -_zigDir;
            _zigTimer = _flipInterval;
        }

        enemy.Position += new Vector2(_zigDir * ctx.Speed * _speedScale * ctx.MDelta, 0.0f);
        if (enemy.Position.X < ctx.View.Position.X + 40.0f || enemy.Position.X > ctx.View.End.X - 40.0f)
        {
            _zigDir = -_zigDir;
            enemy.Position = new Vector2(
                Mathf.Clamp(enemy.Position.X, ctx.View.Position.X + 40.0f, ctx.View.End.X - 40.0f), enemy.Position.Y);
        }

        if (ctx.Hovering)
        {
            enemy.Position = new Vector2(enemy.Position.X, HoverY(ctx));
        }
        else
        {
            enemy.Position += new Vector2(0.0f, ctx.Speed * ctx.MDelta);
        }
    }

    public override void Reset(Enemy enemy)
    {
        _zigDir = 1.0f;
        _zigTimer = (float)GD.RandRange(_resetFlipMin, _flipInterval);
    }
}

/// <summary>dive：入场冲刺直扑玩家 → 转悬停（冲刺期例外；参数：enemies.move_strategies.dive，Q29 入库）。</summary>
public sealed class DiveMove : EnemyMoveStrategy
{
    private Vector2 _diveTarget = Vector2.Zero;
    private float _diveTimer;
    private float _speedScale = 1.7f;
    private float _duration = 1.2f;

    public DiveMove(Godot.Collections.Dictionary? p)
        : base(p)
    {
        if (p == null)
        {
            return;
        }

        _speedScale = GetFloat(p, "speed_scale", _speedScale);
        _duration = GetFloat(p, "duration", _duration);
    }

    public override void Update(float delta, Enemy enemy, MoveCtx ctx)
    {
        if (_diveTimer > 0.0f)
        {
            _diveTimer -= delta;
            var dir = (_diveTarget - enemy.Position).Normalized();
            enemy.Position += dir * ctx.Speed * _speedScale * ctx.MDelta;
            enemy.Position = new Vector2(enemy.Position.X, Mathf.Min(enemy.Position.Y, ctx.View.End.Y - 200.0f));
            if (_diveTimer <= 0.0f)
            {
                // 冲刺结束后以当前深度与锚点较深者为新锚点，转入悬停
                enemy.AnchorY = Mathf.Clamp(
                    Mathf.Max(enemy.AnchorY, enemy.Position.Y),
                    ctx.View.Position.Y + enemy.HoverBand.X,
                    ctx.View.End.Y - 200.0f);
            }
        }
        else if (ctx.Hovering)
        {
            enemy.Position = new Vector2(enemy.Position.X, HoverY(ctx));
        }
        else
        {
            enemy.Position += new Vector2(0.0f, ctx.Speed * ctx.MDelta);
        }
    }

    public override bool IsDiving() => _diveTimer > 0.0f;

    public override void Reset(Enemy enemy)
    {
        _diveTimer = _duration;
        var player = GameState.Instance.PlayerRef;
        _diveTarget = player != null
            ? ((Node2D)player).GlobalPosition
            : new Vector2(enemy.Position.X, 1200.0f);
    }
}

/// <summary>spiral：绕转中心下压 → 悬停期中心漂移（悬停转换以中心为准）。</summary>
public sealed class SpiralMove : EnemyMoveStrategy
{
    private Vector2 _center = Vector2.Zero;

    public SpiralMove(Godot.Collections.Dictionary? p)
        : base(p)
    {
    }

    public override void Update(float delta, Enemy enemy, MoveCtx ctx)
    {
        if (!ctx.Hovering)
        {
            _center += new Vector2(0.0f, ctx.Speed * ctx.MDelta);
        }
        else
        {
            _center = new Vector2(
                Mathf.Clamp(
                    ctx.SpawnX + Enemy.SinFast(ctx.Time * _spiralDriftFreq + ctx.Phase) * _spiralDriftAmp,
                    ctx.View.Position.X + 40.0f,
                    ctx.View.End.X - 40.0f),
                _center.Y);
        }

        enemy.Position = _center
            + new Vector2(Enemy.CosFast(ctx.Time * 4.0f + ctx.Phase), Enemy.SinFast(ctx.Time * 4.0f + ctx.Phase)) * _spiralRadius;
    }

    public override float HoverReferenceY() => _center.Y;

    public override void Reset(Enemy enemy)
    {
        _center = enemy.Position;
    }
}

/// <summary>noise：三正弦叠加伪噪声横移 + 悬停微浮（参数：enemies.move_strategies.noise，Q29 入库）。</summary>
public sealed class NoiseMove : EnemyMoveStrategy
{
    private readonly float[] _freqs = { 1.7f, 2.9f, 4.3f };
    private readonly float[] _phases = { 0.0f, 1.3f, 2.1f };
    private float _speedScale = 1.2f;

    public NoiseMove(Godot.Collections.Dictionary? p)
        : base(p)
    {
        if (p == null)
        {
            return;
        }

        _speedScale = GetFloat(p, "speed_scale", _speedScale);
        ReadArrays(p);
    }

    /// <summary>Q29/R07：freqs/phases 数组长度 ≥3 才覆盖，坏值回退默认。</summary>
    private void ReadArrays(Godot.Collections.Dictionary p)
    {
        if (p.ContainsKey("freqs") && p["freqs"].VariantType == Variant.Type.Array)
        {
            var arr = p["freqs"].AsGodotArray();
            if (arr.Count >= 3)
            {
                _freqs[0] = (float)arr[0].AsDouble();
                _freqs[1] = (float)arr[1].AsDouble();
                _freqs[2] = (float)arr[2].AsDouble();
            }
        }

        if (p.ContainsKey("phases") && p["phases"].VariantType == Variant.Type.Array)
        {
            var arr = p["phases"].AsGodotArray();
            if (arr.Count >= 3)
            {
                _phases[0] = (float)arr[0].AsDouble();
                _phases[1] = (float)arr[1].AsDouble();
                _phases[2] = (float)arr[2].AsDouble();
            }
        }
    }

    public override void Update(float delta, Enemy enemy, MoveCtx ctx)
    {
        // 数组已类型化（float[]），无需惰性固化
        var vx = (Enemy.SinFast(ctx.Time * _freqs[0] + ctx.Phase)
            + Enemy.SinFast(ctx.Time * _freqs[1] + _phases[1] + ctx.Phase)
            + Enemy.SinFast(ctx.Time * _freqs[2] + _phases[2] + ctx.Phase))
            / 3.0f * ctx.Speed * _speedScale;
        enemy.Position += new Vector2(vx * ctx.MDelta, 0.0f);
        enemy.Position = new Vector2(
            Mathf.Clamp(enemy.Position.X, ctx.View.Position.X + 40.0f, ctx.View.End.X - 40.0f), enemy.Position.Y);
        if (ctx.Hovering)
        {
            enemy.Position = new Vector2(enemy.Position.X, HoverY(ctx));
        }
        else
        {
            enemy.Position += new Vector2(0.0f, ctx.Speed * ctx.MDelta);
        }
    }
}

/// <summary>aggressive：追踪性噪声漂移（持续偏向玩家 x）+ 悬停微浮
/// （参数：enemies.move_strategies.aggressive，Q29 入库；悬停下移系数 hover_speed_scale）。</summary>
public sealed class AggressiveMove : EnemyMoveStrategy
{
    private readonly float[] _freqs = { 2.1f, 3.4f, 5.3f };
    private readonly float[] _phases = { 0.0f, 1.7f, 0.6f };
    private float _speedScale = 1.1f;
    private float _hoverSpeedScale = 0.9f;

    public AggressiveMove(Godot.Collections.Dictionary? p)
        : base(p)
    {
        if (p == null)
        {
            return;
        }

        _speedScale = GetFloat(p, "speed_scale", _speedScale);
        _hoverSpeedScale = GetFloat(p, "hover_speed_scale", _hoverSpeedScale);
        if (p.ContainsKey("freqs") && p["freqs"].VariantType == Variant.Type.Array)
        {
            var arr = p["freqs"].AsGodotArray();
            if (arr.Count >= 3)
            {
                _freqs[0] = (float)arr[0].AsDouble();
                _freqs[1] = (float)arr[1].AsDouble();
                _freqs[2] = (float)arr[2].AsDouble();
            }
        }

        if (p.ContainsKey("phases") && p["phases"].VariantType == Variant.Type.Array)
        {
            var arr = p["phases"].AsGodotArray();
            if (arr.Count >= 3)
            {
                _phases[0] = (float)arr[0].AsDouble();
                _phases[1] = (float)arr[1].AsDouble();
                _phases[2] = (float)arr[2].AsDouble();
            }
        }
    }

    public override void Update(float delta, Enemy enemy, MoveCtx ctx)
    {
        var vx = (Enemy.SinFast(ctx.Time * _freqs[0] + ctx.Phase)
            + Enemy.SinFast(ctx.Time * _freqs[1] + _phases[1] + ctx.Phase)
            + Enemy.SinFast(ctx.Time * _freqs[2] + _phases[2] + ctx.Phase))
            / 3.0f * ctx.Speed * _speedScale;
        var player = ctx.Player;
        if (player != null)
        {
            var dx = player.GlobalPosition.X - enemy.Position.X;
            vx += Mathf.Clamp(dx, -1.0f, 1.0f) * _aggressiveChaseSpeed;
        }

        enemy.Position += new Vector2(vx * ctx.MDelta, 0.0f);
        enemy.Position = new Vector2(
            Mathf.Clamp(enemy.Position.X, ctx.View.Position.X + 40.0f, ctx.View.End.X - 40.0f), enemy.Position.Y);
        if (ctx.Hovering)
        {
            enemy.Position = new Vector2(enemy.Position.X, HoverY(ctx));
        }
        else
        {
            enemy.Position += new Vector2(0.0f, ctx.Speed * _hoverSpeedScale * ctx.MDelta);
        }
    }
}
