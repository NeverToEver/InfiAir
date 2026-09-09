using Godot;

namespace InfiAir;

/// <summary>
/// 一次性爆炸粒子：主火花 + 飞散碎片双发射器，纯代码构建。池化复用（上限 24），超出上限的临时实例照旧销毁。
/// P1-5 回池 reparent 到统一 ExplosionPool 节点；P2-1 活跃实例计数（Meta HUD D3 亮度代理）。
/// B16：process_mode Always（玩家死亡爆炸生成于暂停的树）。
/// 注：cfg 读取在 _Ready（C# 构造器内不可访问场景树/autoload）。
/// </summary>
public partial class Explosion : GpuParticles2D
{
    public const int PoolCap = 24;

    private static int _liveCount;
    private static float _visualScale = -1.0f;
    private static int _poolCap = -1;
    // 池可用队列与宿主节点在 GameState 实例字段（ExplosionStock / ExplosionPoolHost）——
    // 静态字段持 Godot 对象为退出 segfault 实测根因（Main/Spawner 同规）

    private GpuParticles2D _debris = null!;
    private Line2D _ring = null!;
    private float _age;
    private bool _pooled;
    private bool _repooling;
    private bool _settled;

    /// <summary>P2-1：活跃爆炸实例数（Meta HUD D3 亮度代理查询）。</summary>
    public static int LiveCount() => _liveCount;

    private static Godot.Collections.Array<Explosion> Stock => GameState.Instance.ExplosionStock;

    public static void SpawnAt(Node parent, Vector2 pos)
    {
        SpawnAt(parent, pos, 1.0f);
    }

    public static void SpawnAt(Node parent, Vector2 pos, float pScale)
    {
        var e = _takeFromPool();
        if (e == null)
        {
            e = new Explosion();
            if (_poolCap < 0)
            {
                _poolCap = (int)GameState.Instance.Cfg("effects.explosion.pool_cap", PoolCap).AsInt64();
            }

            e._pooled = Stock.Count < _poolCap;
            parent.AddChild(e);
        }
        else
        {
            if (e.GetParent() != parent)
            {
                e.Reparent(parent);
            }

            // V 系列（2026-08-09）：U16 复位计数原在 if/else 之外无条件执行——fresh 分支
            // AddChild 后 _Ready 已 ++（U16 前双计数 +1 残留，LiveCount() 永久偏高），
            // 移入池取分支：复用弹不触发 _Ready，在此补计数与 _settled 复位。
            e._settled = false;
            _liveCount++;
        }

        e.Position = pos;
        // effects.explosion_visual_scale：全局特效设计比例 × world_scale（调用方 p_scale 语义不变）
        if (_visualScale < 0.0f)
        {
            _visualScale = (float)GameState.Instance.Cfg("effects.explosion_visual_scale", 1.6).AsDouble(); // G022：一次性缓存
        }

        e.Scale = Vector2.One * pScale * _visualScale * (float)GameState.Instance.WorldScale;
        e.Visible = true;
        e.Restart();
        e._debris.Restart();
        e._age = 0.0f; // 冲击环随粒子生命周期重播（池化复用与新建统一入口）
    }

    private static Explosion? _takeFromPool()
    {
        while (Stock.Count > 0)
        {
            var e = Stock[Stock.Count - 1];
            Stock.RemoveAt(Stock.Count - 1);
            if (GodotObject.IsInstanceValid(e))
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>Boss 多段爆炸序列：连续小爆炸 + 最终大爆炸 + 震动（Timer 驱动而非协程，防泄漏）。</summary>
    public static void SpawnBossSequence(Node parent, Vector2 pos)
    {
        GameState.Instance.PlaySfx(SfxId.ExplosionBig);
        GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.boss_seq_initial", 20.0).AsDouble());
        _bossSeqBurst(parent, pos); // 第 1 段立即触发
        var step = new int[] { 1 }; // 已触发段数（数组引用跨回调共享计数）
        var timer = new Godot.Timer { ProcessMode = Node.ProcessModeEnum.Always, WaitTime = 0.12 };
        parent.AddChild(timer);
        timer.Timeout += () => _bossSeqStep(parent, pos, step, timer);
        timer.Start();
    }

    private static void _bossSeqBurst(Node parent, Vector2 pos)
    {
        var offset = new Vector2((float)GD.RandRange(-130.0, 130.0), (float)GD.RandRange(-90.0, 90.0));
        SpawnAt(parent, pos + offset, (float)GD.RandRange(0.9, 1.5));
        GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.boss_seq_step", 8.0).AsDouble());
    }

    private static void _bossSeqStep(Node parent, Vector2 pos, int[] step, Godot.Timer timer)
    {
        if (!GodotObject.IsInstanceValid(parent))
        {
            return; // G023：parent 已销毁时 timer 必然已随父销毁
        }

        step[0]++;
        if (step[0] <= 6)
        {
            _bossSeqBurst(parent, pos);
        }
        else
        {
            SpawnAt(parent, pos, 3.0f);
            GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.boss_seq_final", 24.0).AsDouble());
            timer.QueueFree();
        }
    }

    public override void _Ready()
    {
        // B16：process_mode Always（死亡/放弃/暂停时爆炸仍正常播放）
        ProcessMode = Node.ProcessModeEnum.Always;
        Amount = (int)GameState.Instance.Cfg("effects.explosion.amount", 24).AsInt64();
        Lifetime = 0.6;
        OneShot = true;
        Explosiveness = 0.9f;
        // 软点贴图替代默认方粒（64px 软点 → scale 参数按 1/64 换算），白热→橙→暗红渐隐色阶
        Texture = CinematicFx.SoftTexture();
        var mat = new ParticleProcessMaterial
        {
            Direction = new Vector3(0.0f, -1.0f, 0.0f),
            Spread = 180.0f,
            InitialVelocityMin = 120.0f,
            InitialVelocityMax = 320.0f,
            Gravity = Vector3.Zero,
            DampingMin = 60.0f,
            DampingMax = 140.0f,
            ScaleMin = 2.0f / CinematicFx.SoftTexSize,
            ScaleMax = 5.0f / CinematicFx.SoftTexSize,
            Color = new Color(1.0f, 0.6f, 0.15f),
            ColorRamp = FireRamp(),
        };
        ProcessMaterial = mat;
        Finished += OnFinished;

        // 碎片发射器：少量、更大、更慢、寿命更长（同款软点 + 余烬色阶）
        _debris = new GpuParticles2D
        {
            Amount = (int)GameState.Instance.Cfg("effects.explosion.debris_amount", 10).AsInt64(),
            Lifetime = 0.9,
            OneShot = true,
            Explosiveness = 0.85f,
            Texture = CinematicFx.SoftTexture(),
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = new Vector3(0.0f, -1.0f, 0.0f),
                Spread = 180.0f,
                InitialVelocityMin = 200.0f,
                InitialVelocityMax = 420.0f,
                Gravity = Vector3.Zero,
                DampingMin = 100.0f,
                DampingMax = 220.0f,
                ScaleMin = 3.0f / CinematicFx.SoftTexSize,
                ScaleMax = 7.0f / CinematicFx.SoftTexSize,
                Color = new Color(0.9f, 0.4f, 0.1f),
                ColorRamp = EmberRamp(),
            },
        };
        AddChild(_debris);

        // 冲击环：随池化节点一次构建，_Process 按 Lifetime 推进扩散（复用零分配）
        _ring = new Line2D
        {
            Points = CinematicFx.RingPoints(40, 26.0f),
            Closed = true,
            DefaultColor = new Color(1.0f, 0.7f, 0.35f, 0.45f),
            Width = 5.0f,
            Material = CinematicFx.AdditiveMaterial(),
        };
        AddChild(_ring);

        _liveCount++;
        _settled = false;
        _age = 0.0f;
        Emitting = true;
        _debris.Emitting = true;
    }

    public override void _Process(double delta)
    {
        // 冲击环推进：前 80% 生命周期扩散+淡出，之后隐藏等回池（不可见期直接跳过）
        if (!Visible || _age >= Lifetime)
        {
            return;
        }

        _age += (float)delta;
        var p = Mathf.Clamp((float)(_age / Lifetime), 0.0f, 1.0f);
        var eased = 1.0f - Mathf.Pow(1.0f - p, 2.0f);
        _ring.Scale = Vector2.One * (0.15f + 3.1f * eased);
        var ringColor = _ring.DefaultColor;
        _ring.DefaultColor = new Color(ringColor.R, ringColor.G, ringColor.B, 0.45f * (1.0f - p));
        _ring.Visible = p < 0.8f;
    }

    /// <summary>主火花色阶：白热 → 橙 → 暗红渐隐（GradientTexture1D 一次性构建）。</summary>
    private static GradientTexture1D FireRamp()
    {
        var g = new Gradient
        {
            Offsets = new[] { 0.0f, 0.3f, 1.0f },
            Colors = new[]
            {
                new Color(1.0f, 0.97f, 0.85f, 1.0f),
                new Color(1.0f, 0.6f, 0.15f, 0.9f),
                new Color(0.55f, 0.12f, 0.05f, 0.0f),
            },
        };
        return new GradientTexture1D { Gradient = g };
    }

    /// <summary>碎片色阶：亮橙余烬 → 深红熄灭。</summary>
    private static GradientTexture1D EmberRamp()
    {
        var g = new Gradient
        {
            Offsets = new[] { 0.0f, 1.0f },
            Colors = new[]
            {
                new Color(1.0f, 0.62f, 0.2f, 1.0f),
                new Color(0.45f, 0.1f, 0.04f, 0.0f),
            },
        };
        return new GradientTexture1D { Gradient = g };
    }

    public override void _ExitTree()
    {
        // 场景重载/外部销毁时从池中移除引用（池内 reparent 置位跳过）；未结算实例补减活跃计数
        if (!_repooling)
        {
            if (!_settled)
            {
                _settled = true;
                _liveCount--;
            }

            GameState.Instance.ExplosionStock.Remove(this);
        }
    }

    private void OnFinished()
    {
        if (!_settled)
        {
            _settled = true;
            _liveCount--;
        }

        if (_pooled)
        {
            Visible = false;
            Stock.Add(this);
            // P1-5：回池统一池节点——隐藏爆炸不再堆积在各 parent 下
            _repooling = true;
            var pool = GameState.Instance.ExplosionPoolHost();
            if (pool != null && pool != GetParent())
            {
                Reparent(pool);
            }

            _repooling = false;
        }
        else
        {
            QueueFree();
        }
    }
}
