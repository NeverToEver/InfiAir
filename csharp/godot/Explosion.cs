using Godot;

namespace InfiAir;

/// <summary>
/// 一次性爆炸粒子（M3a 全量迁移，2026-08-08 自 scripts/explosion.gd 迁移）：
/// 主火花 + 飞散碎片双发射器，纯代码构建。池化复用（上限 24），超出上限的临时实例照旧销毁。
/// P1-5 回池 reparent 到统一 ExplosionPool 节点；P2-1 活跃实例计数（Meta HUD D3 亮度代理）。
/// B16：process_mode Always（玩家死亡爆炸生成于暂停的树）。
/// 注：GDScript _init 的 cfg 读取移入 _Ready（C# 构造器内不可访问场景树/autoload）。
/// </summary>
public partial class Explosion : GpuParticles2D
{
    public const int PoolCap = 24;

    private static readonly Godot.Collections.Array<Explosion> Pool = new();
    private static int _liveCount;
    private static float _visualScale = -1.0f;
    private static int _poolCap = -1;
    private static Node? _poolNode;

    private GpuParticles2D _debris = null!;
    private bool _pooled;
    private bool _repooling;
    private bool _settled;

    /// <summary>P2-1：活跃爆炸实例数（Meta HUD D3 亮度代理查询）。</summary>
    public static int LiveCount() => _liveCount;

    /// <summary>P1-5：统一池节点惰性创建（挂 current_scene 下；跨场景重载失效重建）。</summary>
    private static Node? _ensurePoolNode()
    {
        if (_poolNode != null && GodotObject.IsInstanceValid(_poolNode))
        {
            return _poolNode;
        }

        var tree = (SceneTree?)Engine.GetMainLoop();
        if (tree == null || tree.CurrentScene == null)
        {
            return null;
        }

        _poolNode = new Node { Name = "ExplosionPool" };
        tree.CurrentScene.AddChild(_poolNode);
        return _poolNode;
    }

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

            e._pooled = Pool.Count < _poolCap;
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
    }

    private static Explosion? _takeFromPool()
    {
        while (Pool.Count > 0)
        {
            var e = Pool[Pool.Count - 1];
            Pool.RemoveAt(Pool.Count - 1);
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
        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION_BIG);
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
        var mat = new ParticleProcessMaterial
        {
            Direction = new Vector3(0.0f, -1.0f, 0.0f),
            Spread = 180.0f,
            InitialVelocityMin = 120.0f,
            InitialVelocityMax = 320.0f,
            Gravity = Vector3.Zero,
            DampingMin = 60.0f,
            DampingMax = 140.0f,
            ScaleMin = 2.0f,
            ScaleMax = 5.0f,
            Color = new Color(1.0f, 0.6f, 0.15f),
        };
        ProcessMaterial = mat;
        Finished += OnFinished;

        // 碎片发射器：少量、更大、更慢、寿命更长
        _debris = new GpuParticles2D
        {
            Amount = (int)GameState.Instance.Cfg("effects.explosion.debris_amount", 10).AsInt64(),
            Lifetime = 0.9,
            OneShot = true,
            Explosiveness = 0.85f,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = new Vector3(0.0f, -1.0f, 0.0f),
                Spread = 180.0f,
                InitialVelocityMin = 200.0f,
                InitialVelocityMax = 420.0f,
                Gravity = Vector3.Zero,
                DampingMin = 100.0f,
                DampingMax = 220.0f,
                ScaleMin = 3.0f,
                ScaleMax = 7.0f,
                Color = new Color(0.9f, 0.4f, 0.1f),
            },
        };
        AddChild(_debris);

        _liveCount++;
        _settled = false;
        Emitting = true;
        _debris.Emitting = true;
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

            Pool.Remove(this);
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
            Pool.Add(this);
            // P1-5：回池统一池节点——隐藏爆炸不再堆积在各 parent 下
            _repooling = true;
            var pool = _ensurePoolNode();
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
