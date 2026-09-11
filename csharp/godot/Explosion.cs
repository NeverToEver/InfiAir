using Godot;

namespace InfiAir;

/// <summary>
/// 一次性爆炸粒子：主火花 + 余烬碎片 + 烟尾三发射器，叠白炽核心闪帧、装甲碎片（Polygon2D）
/// 与双层冲击环，纯代码构建。池化复用（上限 24），超出上限的临时实例照旧销毁。
/// 回池 reparent 到统一 ExplosionPool 节点；活跃实例计数（Meta HUD 亮度代理）。
/// 回池时机由 _Process 按总寿命（烟尾最晚熄灭）驱动，不再依赖主火花 Finished 信号。
/// process_mode Always（玩家死亡爆炸生成于暂停的树）。
/// 注：cfg 读取在 _Ready（C# 构造器内不可访问场景树/autoload）。
/// </summary>
public partial class Explosion : GpuParticles2D
{
    public const int PoolCap = 24;

    /// <summary>装甲碎片预制上限（每实例预建，激活时随机启用 4~上限 片）。</summary>
    private const int ShardMax = 8;

    /// <summary>总寿命：烟尾 1.1s 最晚熄灭，留 0.15s 余量后回池。</summary>
    private const float TotalLife = 1.25f;

    private static int _liveCount;
    private static float _visualScale = -1.0f;
    private static int _poolCap = -1;
    private static int _shardCap = -1;
    // 池可用队列与宿主节点在 GameState 实例字段（ExplosionStock / ExplosionPoolHost）——
    // 静态字段持 Godot 对象为退出 segfault 实测根因（Main/Spawner 同规）

    // 敌机残骸配色：暗钢 + 紫晶；玩家侧残骸：琥珀钢
    private static readonly Color[] EnemyShardPalette =
    {
        new(0.30f, 0.30f, 0.35f), new(0.20f, 0.20f, 0.25f),
        new(0.44f, 0.28f, 0.56f), new(0.56f, 0.36f, 0.66f),
    };
    private static readonly Color[] PlayerShardPalette =
    {
        new(0.66f, 0.45f, 0.20f), new(0.50f, 0.34f, 0.17f),
        new(0.82f, 0.60f, 0.28f), new(0.38f, 0.30f, 0.24f),
    };

    // 碎片形体模板：三角 / 不规则四边（半径 3~5px，设计单位，随节点缩放放大）
    private static readonly Vector2[] ShardTri = { new(4.5f, 0.0f), new(-3.0f, 3.4f), new(-2.2f, -3.2f) };
    private static readonly Vector2[] ShardQuad = { new(4.2f, -1.6f), new(1.2f, -3.8f), new(-4.0f, -1.2f), new(-1.8f, 3.4f) };

    private GpuParticles2D _debris = null!;
    private GpuParticles2D _smoke = null!;
    private Sprite2D _coreFlash = null!;
    private Line2D _ringOuter = null!;
    private Line2D _ringInner = null!;
    private readonly Polygon2D[] _shards = new Polygon2D[ShardMax];
    private readonly Vector2[] _shardVel = new Vector2[ShardMax];
    private readonly float[] _shardSpin = new float[ShardMax];
    private readonly float[] _shardLife = new float[ShardMax];
    private readonly Color[] _shardColor = new Color[ShardMax];
    private int _shardCount;
    private float _coreFlashTime = 0.09f;
    private float _age;
    private bool _pooled;
    private bool _repooling;
    private bool _settled;

    /// <summary>活跃爆炸实例数（Meta HUD 亮度代理查询）。</summary>
    public static int LiveCount() => _liveCount;

    private static Godot.Collections.Array<Explosion> Stock => GameState.Instance.ExplosionStock;

    /// <summary>在 parent 下 pos 处引爆。pScale 为规模分档（普通 1.0 / 精英 1.5 / Boss 3.0）；
    /// playerSide 决定装甲碎片配色（敌机残骸暗钢紫晶 / 玩家侧琥珀钢）。</summary>
    public static void SpawnAt(Node parent, Vector2 pos, float pScale = 1.0f, bool playerSide = false)
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

            // 复位计数只在池取分支（else）内执行——新建分支 AddChild 后 _Ready 已 ++，
            // 若在 if/else 之外无条件执行会双计数（LiveCount() 永久偏高）；
            // 复用弹不触发 _Ready，故在此补计数与 _settled 复位。
            e._settled = false;
            _liveCount++;
        }

        e.Position = pos;
        // effects.explosion_visual_scale：全局特效设计比例 × world_scale（调用方 p_scale 语义不变）
        if (_visualScale < 0.0f)
        {
            _visualScale = (float)GameState.Instance.Cfg("effects.explosion_visual_scale", 1.6).AsDouble(); // 一次性缓存
        }

        e.Scale = Vector2.One * pScale * _visualScale * (float)GameState.Instance.WorldScale;
        e.Visible = true;
        e.Restart();
        e._debris.Restart();
        e._smoke.Restart();
        e._age = 0.0f; // 冲击环/闪帧/碎片随粒子生命周期重播（池化复用与新建统一入口）
        e._primeShards(playerSide);
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

    /// <summary>Boss 多段爆炸序列：连续小爆炸 + 最终大爆炸 + 震动（Timer 驱动而非协程，防泄漏）。
    /// 各段间隔在 cfg 区间内随机错拍，规模在 0.8~1.7 抖动，避免机械节拍感。</summary>
    public static void SpawnBossSequence(Node parent, Vector2 pos)
    {
        GameState.Instance.PlaySfx(SfxId.ExplosionBig);
        GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.boss_seq_initial", 20.0).AsDouble());
        _bossSeqBurst(parent, pos); // 第 1 段立即触发
        var step = new int[] { 1 }; // 已触发段数（数组引用跨回调共享计数）
        var timer = new Godot.Timer { ProcessMode = Node.ProcessModeEnum.Always, WaitTime = _bossSeqStepDelay() };
        parent.AddChild(timer);
        timer.Timeout += () => _bossSeqStep(parent, pos, step, timer);
        timer.Start();
    }

    /// <summary>多段爆炸段间隔：effects.explosion.boss_seq_step_min/max 区间内随机（错拍）。</summary>
    private static double _bossSeqStepDelay()
    {
        var min = GameState.Instance.Cfg("effects.explosion.boss_seq_step_min", 0.07).AsDouble();
        var max = GameState.Instance.Cfg("effects.explosion.boss_seq_step_max", 0.17).AsDouble();
        return GD.RandRange(min, Mathf.Max(max, min));
    }

    private static void _bossSeqBurst(Node parent, Vector2 pos)
    {
        var offset = new Vector2((float)GD.RandRange(-130.0, 130.0), (float)GD.RandRange(-90.0, 90.0));
        SpawnAt(parent, pos + offset, (float)GD.RandRange(0.8, 1.7));
        GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.boss_seq_step", 8.0).AsDouble());
    }

    private static void _bossSeqStep(Node parent, Vector2 pos, int[] step, Godot.Timer timer)
    {
        if (!GodotObject.IsInstanceValid(parent))
        {
            return; // parent 已销毁时 timer 必然已随父销毁
        }

        step[0]++;
        if (step[0] <= 6)
        {
            _bossSeqBurst(parent, pos);
            timer.WaitTime = _bossSeqStepDelay(); // 下一段重新掷签错拍
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
        // process_mode Always（死亡/放弃/暂停时爆炸仍正常播放）
        ProcessMode = Node.ProcessModeEnum.Always;
        _coreFlashTime = Mathf.Max((float)GameState.Instance.Cfg("effects.explosion.core_flash_time", _coreFlashTime).AsDouble(), 0.02f);
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
            Color = new Color(1.0f, 0.66f, 0.22f),
            ColorRamp = FireRamp(),
        };
        ProcessMaterial = mat;

        // 烟尾：慢速深灰软点，上浮漂移 + 随寿命放大 + 淡出（火花熄灭后残留；普通混合压暗背景）
        _smoke = new GpuParticles2D
        {
            Amount = (int)GameState.Instance.Cfg("effects.explosion.smoke_amount", 4).AsInt64(),
            Lifetime = 1.1,
            OneShot = true,
            Explosiveness = 0.4f,
            Texture = CinematicFx.SoftTexture(),
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = new Vector3(0.0f, -1.0f, 0.0f),
                Spread = 60.0f,
                InitialVelocityMin = 14.0f,
                InitialVelocityMax = 42.0f,
                Gravity = Vector3.Zero,
                DampingMin = 6.0f,
                DampingMax = 18.0f,
                ScaleMin = 7.0f / CinematicFx.SoftTexSize,
                ScaleMax = 12.0f / CinematicFx.SoftTexSize,
                ScaleCurve = SmokeGrowCurve(),
                Color = new Color(0.26f, 0.24f, 0.28f),
                ColorRamp = SmokeRamp(),
            },
        };
        AddChild(_smoke);

        // 余烬发射器：少量、更大、更慢、寿命更长（同款软点 + 余烬色阶）
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

        // 装甲碎片：预建 ShardMax 片多边形，激活时按阵营配色随机启用（_primeShards）
        for (var i = 0; i < ShardMax; i++)
        {
            var shard = new Polygon2D { Visible = false };
            _shards[i] = shard;
            AddChild(shard);
        }

        // 双层冲击环：外环琥珀粗、慢速长留；内环白细、快速早出。随池化节点一次构建，
        // _Process 按生命周期推进扩散（复用零分配）
        var ringPts = CinematicFx.RingPoints(40, 26.0f);
        _ringOuter = new Line2D
        {
            Points = ringPts,
            Closed = true,
            DefaultColor = new Color(1.0f, 0.70f, 0.30f, 0.5f),
            Width = 6.0f,
            Material = CinematicFx.AdditiveMaterial(),
        };
        AddChild(_ringOuter);
        _ringInner = new Line2D
        {
            Points = ringPts,
            Closed = true,
            DefaultColor = new Color(1.0f, 0.97f, 0.88f, 0.85f),
            Width = 2.0f,
            Material = CinematicFx.AdditiveMaterial(),
        };
        AddChild(_ringInner);

        // 白炽核心闪帧：爆炸头几帧一枚纯白高亮软点（additive），喂 world_grade 辉光形成爆闪
        _coreFlash = CinematicFx.SoftGlow(26.0f, new Color(1.0f, 1.0f, 1.0f, 0.0f));
        AddChild(_coreFlash);

        _liveCount++;
        _settled = false;
        _age = 0.0f;
        Emitting = true;
        _debris.Emitting = true;
        _smoke.Emitting = true;
    }

    /// <summary>激活时初始化装甲碎片：数量 4~cfg 上限随机，初速飞散 + 自旋 + 按阵营取色。</summary>
    private void _primeShards(bool playerSide)
    {
        if (_shardCap < 0)
        {
            _shardCap = Mathf.Clamp((int)GameState.Instance.Cfg("effects.explosion.shard_amount", ShardMax).AsInt64(), 0, ShardMax);
        }

        _shardCount = _shardCap <= 4 ? _shardCap : (int)GD.RandRange(4.0, _shardCap + 1.0);
        var palette = playerSide ? PlayerShardPalette : EnemyShardPalette;
        for (var i = 0; i < ShardMax; i++)
        {
            var shard = _shards[i];
            if (i >= _shardCount)
            {
                shard.Visible = false;
                continue;
            }

            shard.Polygon = GD.Randf() < 0.5f ? ShardTri : ShardQuad;
            shard.Position = Vector2.Zero;
            shard.Rotation = (float)GD.RandRange(0.0, Mathf.Tau);
            var dir = Vector2.Right.Rotated((float)GD.RandRange(0.0, Mathf.Tau));
            _shardVel[i] = dir * (float)GD.RandRange(90.0, 260.0);
            _shardSpin[i] = (float)GD.RandRange(-9.0, 9.0);
            _shardLife[i] = (float)GD.RandRange(0.5, 0.8);
            _shardColor[i] = palette[GD.RandRange(0, palette.Length - 1)];
            shard.Color = _shardColor[i];
            shard.Visible = true;
        }
    }

    public override void _Process(double delta)
    {
        // 回池由总寿命驱动（烟尾最晚熄灭）；隐藏/播完实例直接跳过
        if (!Visible || _age >= TotalLife)
        {
            return;
        }

        var d = (float)delta;
        _age += d;

        // 白炽核心闪帧：cfg 窗口内急速放大 + 淡出，之后隐藏（爆闪只在前几帧）
        var cp = Mathf.Clamp(_age / _coreFlashTime, 0.0f, 1.0f);
        if (cp < 1.0f)
        {
            var ce = 1.0f - Mathf.Pow(1.0f - cp, 2.0f);
            _coreFlash.Visible = true;
            _coreFlash.Scale = Vector2.One * ((26.0f / (CinematicFx.SoftTexSize * 0.5f)) * (0.6f + 1.1f * ce));
            _coreFlash.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.95f * (1.0f - cp));
        }
        else if (_coreFlash.Visible)
        {
            _coreFlash.Visible = false;
        }

        // 双层冲击环异速扩散：内环快而短，外环慢而长；各自淡出后隐藏
        var p = Mathf.Clamp(_age / 0.6f, 0.0f, 1.0f);
        var eased = 1.0f - Mathf.Pow(1.0f - p, 2.0f);
        _ringOuter.Scale = Vector2.One * (0.12f + 3.0f * eased);
        var outerColor = _ringOuter.DefaultColor;
        _ringOuter.DefaultColor = new Color(outerColor.R, outerColor.G, outerColor.B, 0.5f * (1.0f - p));
        _ringOuter.Visible = p < 0.85f;
        var pi = Mathf.Clamp(_age / 0.38f, 0.0f, 1.0f);
        var easedI = 1.0f - Mathf.Pow(1.0f - pi, 2.0f);
        _ringInner.Scale = Vector2.One * (0.2f + 3.4f * easedI);
        var innerColor = _ringInner.DefaultColor;
        _ringInner.DefaultColor = new Color(innerColor.R, innerColor.G, innerColor.B, 0.85f * (1.0f - pi));
        _ringInner.Visible = pi < 1.0f;

        // 装甲碎片：阻尼减速 + 微重力下坠 + 自旋 + 按各自寿命淡出
        for (var i = 0; i < _shardCount; i++)
        {
            var shard = _shards[i];
            if (!shard.Visible)
            {
                continue;
            }

            var fade = 1.0f - _age / _shardLife[i];
            if (fade <= 0.0f)
            {
                shard.Visible = false;
                continue;
            }

            _shardVel[i] = _shardVel[i] * Mathf.Exp(-3.2f * d) + new Vector2(0.0f, 150.0f * d);
            shard.Position += _shardVel[i] * d;
            shard.Rotation += _shardSpin[i] * d;
            var c = _shardColor[i];
            shard.Color = new Color(c.R, c.G, c.B, c.A * Mathf.Min(fade * 2.0f, 1.0f));
        }

        if (_age >= TotalLife)
        {
            Finish();
        }
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
                new Color(1.0f, 0.66f, 0.22f, 0.9f),
                new Color(0.58f, 0.16f, 0.04f, 0.0f),
            },
        };
        return new GradientTexture1D { Gradient = g };
    }

    /// <summary>余烬色阶：亮橙余烬 → 深红熄灭。</summary>
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

    /// <summary>烟尾色阶：深灰半透明 → 透明（普通混合，压暗形成体积感）。</summary>
    private static GradientTexture1D SmokeRamp()
    {
        var g = new Gradient
        {
            Offsets = new[] { 0.0f, 0.25f, 1.0f },
            Colors = new[]
            {
                new Color(0.30f, 0.28f, 0.32f, 0.0f),
                new Color(0.28f, 0.26f, 0.30f, 0.55f),
                new Color(0.20f, 0.19f, 0.22f, 0.0f),
            },
        };
        return new GradientTexture1D { Gradient = g };
    }

    /// <summary>烟尾随寿命缓慢放大（0.6× → 1.6×）。</summary>
    private static CurveTexture SmokeGrowCurve()
    {
        var c = new Curve();
        c.AddPoint(new Vector2(0.0f, 0.6f));
        c.AddPoint(new Vector2(1.0f, 1.6f));
        return new CurveTexture { Curve = c };
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

            // 用安全取值：autoload 先于场景节点失效的非常规拆树序（崩溃恢复/编辑器停止）下
            // Instance getter 会抛 InvalidOperationException
            GameState.TryGetInstance()?.ExplosionStock.Remove(this);
        }
    }

    /// <summary>总寿命播完结算：活跃计数归位，池化实例回池，非池化销毁。</summary>
    private void Finish()
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
            // 回池统一池节点——隐藏爆炸不再堆积在各 parent 下
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
