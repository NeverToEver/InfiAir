using System.Collections.Generic;
using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 开场过场导演：6 镜头时序串联、黑场转场、跳过与整树清理。
/// 设计文档（单一事实源）：docs/INTRO_CINEMATIC.md。
/// 全部按 1920×1080 设计坐标布局；镜头内连续动画用 tween / Timer 节点 / _process，
/// 严禁 await create_timer 协程（退出时协程状态泄漏）。
/// M6 全量迁移（2026-08-08 自 scripts/intro_cinematic.gd）：CanvasLayer 子类。
/// CinematicFx/DawnStation/Starfield 已迁 C# typed 直调。
/// 注：原 GDScript signal finished 迁移为 C# [Signal] Finished（Main/测试均 typed 连接）；
/// 测试经 PascalCase SetShotDurations(float[]) 注入镜头时长。
/// </summary>
public partial class IntroCinematic : CanvasLayer
{
    /// <summary>开场过场播放完毕（自然结束或跳过，统一出口 skip()；main.gd `_on_intro_finished` 连接）。</summary>
    [Signal]
    public delegate void FinishedEventHandler();

    private const float Transition = 0.3f;  // 镜头间黑场淡入淡出（含在各镜头时长内）
    private const float OutroFade = 0.7f;  // 镜头 6 末尾淡出到标题定格
    private const float TitleCardIn = 0.2f;  // 收尾标题定格：淡入
    private const float TitleCardHold = 0.8f;  // 收尾标题定格：停留
    private const float TitleCardOut = 0.2f;  // 收尾标题定格：淡出（随后走统一出口 skip）
    /// 过场音频统一策略：全部音量下移 + 变调下沉柔和化（避免爆炸/引擎音突兀炸耳）
    private const float AudioVolOffset = -6.0f;  // 各音效在原设定基础上统一 -6dB
    private const float AudioPitch = 0.88f;  // 变调下沉，音色更闷柔

    /// <summary>原 const PLAYER_SHIP = preload(...)：C# 静态字段禁止持有 Godot Resource（退出 segfault 实测），改实例字段。</summary>
    private readonly Texture2D _playerShip = GD.Load<Texture2D>("res://assets/sprites/player_ship.png");

    /// <summary>每镜头时长（§2 分镜表；六镜头 16.1s = 总和，转场含在内；+标题定格 1.2s = 总 17.3s）。测试可改短。</summary>
    private float[] _shotDurations = { 2.8f, 2.5f, 2.5f, 2.5f, 2.8f, 3.0f };

    private int _shotIndex = -1;
    private Node2D? _currentShot;
    private Godot.Timer _shotTimer = null!;
    private bool _done;
    private float _driftT;  // 导演级手持漂移相位（共享容器，单 _process 零堆分配）
    private bool _whiteTransition;  // 差异化转场：下一镜头以白闪承接
    private Tween? _subTween;  // 字幕淡入/淡出互斥
    private bool _startQueued;  // M6：Play() 幂等入口守卫（_ready 同帧重复触发防护）

    private Node2D _shotRoot = null!;
    private ColorRect _fade = null!;
    private ColorRect _flash = null!;
    private Label _subtitle = null!;
    private Control _titleCard = null!;
    private Label _skipHint = null!;

    // A7：测试/诊断白盒断言经公开接口（过场镜头）
    public void SetShotDurations(float[] durations)
    {
        _shotDurations = durations;
    }

    public int ShotIndex() => _shotIndex;

    public Node2D? CurrentShot() => _currentShot;

    public Node2D ShotRoot() => _shotRoot;

    public Label Subtitle() => _subtitle;

    /// <summary>跳过（幂等）：与自然结束同一出口——停计时、发 finished、整树 queue_free。</summary>
    public void Skip()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _shotTimer.Stop();
        EmitSignal(SignalName.Finished);
        QueueFree();
    }

    /// <summary>播放入口（M6 接线层兼容方法）：原 GDScript 无 play()——_Ready 自动起播；
    /// 幂等：未开始且未结束时把首镜头延后到帧末启动（测试可在 add_child 同帧替换 _shot_durations）。</summary>
    public void Play()
    {
        if (!IsInsideTree() || _done || _startQueued || _shotIndex >= 0)
        {
            return;
        }

        _startQueued = true;
        CallDeferred(MethodName.Advance);
    }

    public bool IsPlaying() => !_done;

    public override void _Ready()
    {
        _shotRoot = GetNode<Node2D>("ShotRoot");
        _fade = GetNode<ColorRect>("Fade");
        _flash = GetNode<ColorRect>("Flash");
        _subtitle = GetNode<Label>("Subtitle");
        _titleCard = GetNode<Control>("TitleCard");
        _skipHint = GetNode<Label>("SkipHint");

        _skipHint.Text = (string)Tr("INTRO_SKIP");
        _skipHint.AddThemeFontOverride("font", UITheme.Font);
        _subtitle.AddThemeFontOverride("font", UITheme.Font);
        GetNode<Label>("TitleCard/Center/VBox/Title").AddThemeFontOverride("font", UITheme.Font);
        _shotTimer = new Godot.Timer { OneShot = true };
        _shotTimer.Timeout += OnShotTimeout;
        AddChild(_shotTimer);
        // 首镜头延后到帧末启动：测试可在 add_child 同帧替换 _shot_durations
        Play();
    }

    /// <summary>任意键/鼠标点击跳过；Esc（ui_cancel）放行给 BackNavigator 路由到 Main.skip_intro()（公开接口，A7 后经 _skip_intro 落地）</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_done || @event.IsActionPressed("ui_cancel"))
        {
            return;
        }

        var pressedKey = @event is InputEventKey key && key.Pressed && !key.Echo;
        var pressedClick = @event is InputEventMouseButton mouse && mouse.Pressed;
        if (pressedKey || pressedClick)
        {
            GetViewport().SetInputAsHandled();
            Skip();
        }
    }

    /// <summary>导演级手持漂移：共享容器低频正弦位移/微旋转，零堆分配</summary>
    public override void _Process(double delta)
    {
        _driftT += (float)delta;
        _shotRoot.Position = new Vector2(
            Mathf.Sin(_driftT * 0.45f) * 3.0f,
            Mathf.Cos(_driftT * 0.38f) * 2.5f);
        _shotRoot.Rotation = Mathf.Sin(_driftT * 0.3f) * 0.003f;
    }

    private void Advance()
    {
        if (_done)
        {
            return;
        }

        if (_currentShot != null)
        {
            _currentShot.QueueFree();
            _currentShot = null;
        }

        _shotIndex += 1;
        if (_shotIndex >= _shotDurations.Length)
        {
            PlayTitleCard();  // 自然结束：标题定格后走统一出口
            return;
        }

        _currentShot = BuildShot(_shotIndex);
        _shotRoot.AddChild(_currentShot);
        var dur = _shotDurations[_shotIndex];
        SetSubtitle(GdFormat.Format("INTRO_SUB_%d", _shotIndex + 1));
        if (_whiteTransition)
        {
            // 白闪承接：黑层保持透明，白闪直接回收
            _whiteTransition = false;
            _fade.Color = new Color(_fade.Color, 0.0f);
            var flashTween = CreateTween();
            flashTween.TweenProperty(_flash, "color:a", 0.0f, 0.28);
        }
        else
        {
            // 黑场淡入（镜头 1 从全黑起，其余承接上一镜头的淡出）
            _fade.Color = new Color(_fade.Color, 1.0f);
            var fadeTween = CreateTween();
            fadeTween.TweenProperty(_fade, "color:a", 0.0f, Mathf.Min(Transition, dur * 0.5f));
        }

        _shotTimer.Start(dur - FadeOutTime());
    }

    private float FadeOutTime()
    {
        var dur = _shotDurations[_shotIndex];
        var t = _shotIndex == _shotDurations.Length - 1 ? OutroFade : Transition;
        return Mathf.Min(t, dur * 0.5f);
    }

    private void OnShotTimeout()
    {
        if (_done)
        {
            return;
        }

        // 差异化转场：镜头 1→2（链爆）与 4→5（点火弹射）用白闪，其余黑场
        _whiteTransition = _shotIndex == 0 || _shotIndex == 3;
        // 字幕随转场淡出
        if (_subTween != null && _subTween.IsValid())
        {
            _subTween.Kill();
        }

        _subTween = CreateTween();
        _subTween.TweenProperty(_subtitle, "modulate:a", 0.0f, FadeOutTime());
        if (_whiteTransition)
        {
            var flashTween = CreateTween();
            flashTween.TweenProperty(_flash, "color:a", 1.0f, 0.10);
            flashTween.TweenCallback(Callable.From(Advance));
        }
        else
        {
            var fadeTween = CreateTween();
            fadeTween.TweenProperty(_fade, "color:a", 1.0f, FadeOutTime());
            fadeTween.TweenCallback(Callable.From(Advance));
        }
    }

    /// <summary>收尾标题定格：淡入 → 停留 → 淡出 → skip() 统一出口</summary>
    private void PlayTitleCard()
    {
        if (_subTween != null && _subTween.IsValid())
        {
            _subTween.Kill();
        }

        _subtitle.Modulate = new Color(_subtitle.Modulate, 0.0f);
        var tween = CreateTween();
        tween.TweenProperty(_titleCard, "modulate:a", 1.0f, TitleCardIn);
        tween.TweenInterval(TitleCardHold);
        tween.TweenProperty(_titleCard, "modulate:a", 0.0f, TitleCardOut);
        tween.TweenCallback(Callable.From(Skip));
    }

    /// <summary>叙事字幕卡：设置文本并淡入（淡出由 _on_shot_timeout 随转场处理）</summary>
    private void SetSubtitle(string key)
    {
        if (_subTween != null && _subTween.IsValid())
        {
            _subTween.Kill();
        }

        _subtitle.Text = (string)Tr(key);
        _subtitle.Modulate = new Color(_subtitle.Modulate, 0.0f);
        _subTween = CreateTween();
        _subTween.TweenProperty(_subtitle, "modulate:a", 1.0f, 0.3);
    }

    private Node2D BuildShot(int i)
    {
        return i switch
        {
            0 => BuildShot1(),
            1 => BuildShot2(),
            2 => BuildShot3(),
            3 => BuildShot4(),
            4 => BuildShot5(),
            _ => BuildShot6(),
        };
    }

    // ---------------- 构图辅助 ----------------

    private static IntroGlowDot Glow(float radius, Color color, bool additive = true)
    {
        var dot = new IntroGlowDot { Radius = radius, DotColor = color };
        if (additive)
        {
            dot.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        }

        return dot;
    }

    private static Polygon2D RectPoly(float w, float h, Color color)
    {
        return new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(-w * 0.5f, -h * 0.5f),
                new Vector2(w * 0.5f, -h * 0.5f),
                new Vector2(w * 0.5f, h * 0.5f),
                new Vector2(-w * 0.5f, h * 0.5f),
            },
            Color = color,
        };
    }

    private static ColorRect BgRect(Color color)
    {
        return new ColorRect
        {
            Color = color,
            Position = Vector2.Zero,
            Size = new Vector2(1920.0f, 1080.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    private static Line2D Line(Vector2[] points, Color color, float width = 2.0f)
    {
        return new Line2D
        {
            Points = points,
            DefaultColor = color,
            Width = width,
        };
    }

    /// <summary>引爆冲击颤动：随机方向脉冲偏移，0.27s 内衰减回基线（tween 驱动，不加 _process）。
    /// state[0] 持有上一次颤动 tween：重复触发时杀旧刷新峰值，形成"每爆一下震一下"的连锁叠加；
    /// host.position 基线必须为 ZERO（挂在镜头自己的 root 上，不与导演手持漂移的 _shot_root 冲突）。</summary>
    private static void KickShake(Node2D host, float amp, Godot.Collections.Array state)
    {
        if (state[0].VariantType != Variant.Type.Nil && state[0].AsGodotObject() is Tween oldTween && oldTween.IsValid())
        {
            oldTween.Kill();
        }

        var dir = new Vector2((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0));
        if (dir.LengthSquared() < 0.01f)
        {
            dir = Vector2.Right;
        }

        var st = host.CreateTween();
        state[0] = Variant.From(st);
        st.TweenProperty(host, "position", dir.Normalized() * amp, 0.04).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        st.TweenProperty(host, "position", dir.Normalized() * -amp * 0.4f, 0.08).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut);
        st.TweenProperty(host, "position", Vector2.Zero, 0.15).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>粒子工厂委托给 CinematicFx（同 cfg 契约）：默认挂共享软点贴图，消除硬边圆点的廉价感；
    /// scale 语义保持「像素直径」，≤96/发射器的硬性上限不变。</summary>
    private static GpuParticles2D Particles(Godot.Collections.Dictionary cfg) => CinematicFx.Particles(cfg);

    // ---------------- 镜头 1：远景推近（2.8s） ----------------
    /// <summary>环形空间站深空爆炸：星空底 + 远处淡星云 + 弧段/舱段拼装环体（分块/外廓细节线），
    /// 容器 0.7→1.0 匀速推近；爆炸核心辉光暗红→橙白放大 + 冲击波扩散环，
    /// 碎片外抛 + 前景尘埃双层粒子 + 破口剥落碎块。</summary>
    private Node2D BuildShot1()
    {
        var dur = _shotDurations[0];
        var root = new Node2D { Name = "Shot1" };
        root.AddChild(new Starfield());  // M1 起 Starfield 为 C#，typed 实例化（原经脚本资源，M6 重定型）
        // 远处星云（比镜头 6 更淡，只铺层次；软径向光晕消除硬边）
        var neb1 = CinematicFx.SoftGlow(520.0f, new Color(0.2f, 0.12f, 0.4f, 0.08f));
        neb1.Position = new Vector2(1560.0f, 260.0f);
        root.AddChild(neb1);
        var neb2 = CinematicFx.SoftGlow(420.0f, new Color(0.08f, 0.18f, 0.4f, 0.08f));
        neb2.Position = new Vector2(300.0f, 840.0f);
        root.AddChild(neb2);

        // 站体构件抽为 DawnStation 共享构建函数（开场=实体毁灭态，纯提取不改视觉；
        // 返航/基地背景复用虚影态，docs/RETURN_HOME_CINEMATIC.md §5）
        var station = DawnStation.Build(DawnStation.Mode.Destroyed);
        station.Position = new Vector2(960.0f, 470.0f);
        station.Scale = Vector2.One * 0.7f;
        root.AddChild(station);

        // 爆炸核心：破口处叠加辉光，暗红 → 橙白并放大（软光晕；scale tween 以 soft_glow 基准缩放为底）
        var blastPos = new Vector2(Mathf.Cos(0.85f), Mathf.Sin(0.85f)) * 260.0f;
        var halo = CinematicFx.SoftGlow(150.0f, new Color(0.6f, 0.2f, 0.08f, 0.35f));
        halo.Position = blastPos;
        var haloBase = halo.Scale;
        station.AddChild(halo);
        var core = CinematicFx.SoftGlow(80.0f, new Color(1.0f, 1.0f, 1.0f, 0.95f));
        core.Position = blastPos;
        core.Modulate = new Color(0.5f, 0.1f, 0.05f);
        var coreBase = core.Scale;
        station.AddChild(core);
        var tween = root.CreateTween().SetParallel(true);
        tween.TweenProperty(station, "scale", Vector2.One, dur).SetTrans(Tween.TransitionType.Linear);
        tween.TweenProperty(core, "modulate", new Color(1.0f, 0.9f, 0.7f), dur * 0.8f);
        tween.TweenProperty(core, "scale", coreBase * 2.4f, dur);
        tween.TweenProperty(halo, "scale", haloBase * 1.8f, dur);

        // 舱段模块舷窗灯 ×8：随爆炸吞噬站体，按距破口角距由近到远逐盏熄灭（错峰 tween 延迟贯穿全镜头）
        var lightSeq = new[] { 1.571f, 0.0f, 2.356f, 5.497f, 3.142f, 4.712f, 3.927f, -1.0f };  // 舱段角（rad），-1 = 中心毂
        for (var lI = 0; lI < lightSeq.Length; lI++)
        {
            var lampA = lightSeq[lI];
            var lamp = Glow(4.5f, new Color(1.0f, 0.75f, 0.4f, 0.85f));
            lamp.Position = lampA < 0.0f ? Vector2.Zero : new Vector2(Mathf.Cos(lampA), Mathf.Sin(lampA)) * 260.0f;
            station.AddChild(lamp);
            var lampT = root.CreateTween();
            lampT.TweenInterval(dur * (0.22f + 0.07f * lI));
            lampT.TweenProperty(lamp, "modulate:a", 0.05f, 0.35);
        }

        // 冲击波扩散环：爆心薄环急速扩大并淡出（叠加态，错开 0.3s 两波）
        var waveShakeState = new Godot.Collections.Array { default(Variant) };  // 两波主爆颤动共享刷新
        for (var wave = 0; wave < 2; wave++)
        {
            var wavePoints = new Vector2[40];
            for (var i = 0; i < 40; i++)
            {
                var a = Mathf.Tau * i / 40.0f;
                wavePoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 60.0f;
            }

            var waveRing = Line(wavePoints, new Color(1.0f, 0.7f, 0.35f, 0.7f), 5.0f);
            waveRing.Closed = true;
            waveRing.Position = blastPos;
            waveRing.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            waveRing.Scale = Vector2.One * 0.2f;
            station.AddChild(waveRing);
            var wt = root.CreateTween();
            wt.TweenInterval(0.2f + 0.3f * wave);
            // 2026-08-03 审计：扩散与淡出同步起播（原 parallel 模式把淡出从 tween 起点开始，
            // 第二波环在扩散中段即不可见）；改为 interval 后 scale/alpha 并行（与镜头 2 ripple 同款）
            wt.TweenProperty(waveRing, "scale", Vector2.One * 4.5f, 0.9).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            wt.Parallel().TweenProperty(waveRing, "modulate:a", 0.0f, 0.8);
            // 起爆同步一次主爆颤动（幅度略大于镜头 2 单节点）
            var kt = root.CreateTween();
            kt.TweenInterval(0.2f + 0.3f * wave);
            kt.TweenCallback(Callable.From(() => KickShake(root, 6.0f, waveShakeState)));
        }

        // 二次殉爆（dur*0.45 起）：破口对侧环缘小型闪爆——软闪光核 + 冲击波扩散环 + 颤动 + 递减音量
        var blast2Pos = new Vector2(Mathf.Cos(3.8f), Mathf.Sin(3.8f)) * 240.0f;
        var boom2 = new Godot.Timer { OneShot = true, WaitTime = dur * 0.45f, Autostart = true };
        root.AddChild(boom2);
        boom2.Timeout += () =>
        {
            var flash2 = CinematicFx.SoftGlow(56.0f, new Color(1.0f, 0.85f, 0.6f, 0.9f));
            flash2.Position = blast2Pos;
            var flash2Base = flash2.Scale;
            flash2.Scale = Vector2.Zero;
            station.AddChild(flash2);
            var f2t = root.CreateTween();
            f2t.TweenProperty(flash2, "scale", flash2Base * 1.3f, 0.1);
            f2t.TweenProperty(flash2, "modulate:a", 0.0f, 0.4);
            f2t.TweenCallback(Callable.From(flash2.QueueFree));
            var wave2 = CinematicFx.Shockwave(new Godot.Collections.Dictionary
            {
                ["radius"] = 200.0f,
                ["time"] = 0.7f,
                ["color"] = new Color(1.0f, 0.6f, 0.25f, 0.55f),
                ["core_color"] = new Color(1.0f, 0.9f, 0.7f, 0.85f),
                ["width"] = 10.0f,
            });
            wave2.Position = blast2Pos;
            station.AddChild(wave2);
            KickShake(root, 5.0f, waveShakeState);
            GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION, -8.0f + AudioVolOffset, AudioPitch);
        };

        // 余烬：全镜头持续的橙色慢速上飘细屑（低透明度，燃烧余韵层）
        var embers = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 40,
            ["lifetime"] = 3.2f,
            ["vel_min"] = 12.0f,
            ["vel_max"] = 40.0f,
            ["spread"] = 35.0f,
            ["scale_min"] = 3.0f,
            ["scale_max"] = 6.0f,
            ["color"] = new Color(1.0f, 0.5f, 0.15f, 0.3f),
        });
        embers.Position = new Vector2(960.0f, 500.0f);
        root.AddChild(embers);

        // 碎片（高速外抛）+ 尘埃（慢速前景低透明度）
        var debris = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 48,
            ["lifetime"] = 2.2f,
            ["vel_min"] = 160.0f,
            ["vel_max"] = 420.0f,
            ["damping_min"] = 60.0f,
            ["damping_max"] = 140.0f,
            ["scale_min"] = 3.0f,
            ["scale_max"] = 7.0f,
            ["color"] = new Color(1.0f, 0.55f, 0.15f),
        });
        debris.Position = new Vector2(960.0f, 470.0f) + blastPos * 0.7f;
        root.AddChild(debris);
        var dust = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 40,
            ["lifetime"] = 3.5f,
            ["vel_min"] = 20.0f,
            ["vel_max"] = 60.0f,
            ["scale_min"] = 6.0f,
            ["scale_max"] = 12.0f,
            ["color"] = new Color(0.6f, 0.5f, 0.4f, 0.12f),
        });
        dust.Position = new Vector2(960.0f, 540.0f);
        root.AddChild(dust);
        // 前景碎片层：深色大块残骸横向漂移 + 翻滚（比站体更快的近景视差）
        for (var k = 0; k < 4; k++)
        {
            var driftShard = new Polygon2D
            {
                Polygon = new[] { new Vector2(-26.0f, -10.0f), new Vector2(20.0f, -18.0f), new Vector2(30.0f, 12.0f), new Vector2(-12.0f, 20.0f) },
                Color = new Color(0.04f, 0.04f, 0.07f),
                Position = new Vector2(260.0f + 470.0f * k, 180.0f + 700.0f * (k % 2)),
                Scale = Vector2.One * (0.8f + 0.25f * (k % 3)),
            };
            root.AddChild(driftShard);
            driftShard.AddChild(Line(new[] { new Vector2(-26.0f, -10.0f), new Vector2(20.0f, -18.0f) }, new Color(1.0f, 0.6f, 0.25f, 0.4f), 2.0f));
            // 顶缘暖色软轮廓光（朝向爆心一侧的反射光，把剪影从深空里托出来）
            var shardRim = CinematicFx.SoftGlow(18.0f, new Color(1.0f, 0.55f, 0.2f, 0.22f));
            shardRim.Position = new Vector2(0.0f, -12.0f);
            driftShard.AddChild(shardRim);
            var spin = root.CreateTween().SetLoops();
            spin.TweenProperty(driftShard, "rotation", driftShard.Rotation + Mathf.Tau, 9.0f + 3.0f * k);
            var move = root.CreateTween().SetLoops();
            move.TweenProperty(driftShard, "position", driftShard.Position + new Vector2(140.0f + 60.0f * k, -30.0f), 4.0f + k).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            move.TweenProperty(driftShard, "position", driftShard.Position, 4.0f + k).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION_BIG, AudioVolOffset, AudioPitch);
        return root;
    }

    // ---------------- 镜头 2：X光链式爆炸（2.5s） ----------------
    /// <summary>冷蓝底剖面：甲板为半透明填充带 + 亮边（有厚度层次），舱室隔断分区清晰；
    /// 橙红能量沿预设折线 0.2s/节点链式点亮（叠加态外发光双线），节点处爆开橙色圆闪，
    /// 爆炸音 ×3 音量递减。</summary>
    private Node2D BuildShot2()
    {
        var root = new Node2D { Name = "Shot2" };
        root.AddChild(BgRect(new Color(0.02f, 0.05f, 0.12f)));
        var wire = new Color(0.3f, 0.6f, 1.0f, 0.4f);
        // 4 层甲板：半透明蓝填充带（厚度）+ 上下亮边
        var deckYs = new[] { 340.0f, 520.0f, 700.0f, 880.0f };
        for (var i = 0; i < deckYs.Length; i++)
        {
            var y = deckYs[i];
            var band = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(150.0f, y),
                    new Vector2(1770.0f, y),
                    new Vector2(1770.0f, y + 16.0f),
                    new Vector2(150.0f, y + 16.0f),
                },
                Color = new Color(0.2f, 0.45f, 0.9f, 0.12f),
            };
            root.AddChild(band);
            root.AddChild(Line(new[] { new Vector2(150.0f, y), new Vector2(1770.0f, y) }, new Color(0.45f, 0.7f, 1.0f, 0.7f), 2.5f));
            root.AddChild(Line(new[] { new Vector2(150.0f, y + 16.0f), new Vector2(1770.0f, y + 16.0f) }, new Color(0.2f, 0.4f, 0.8f, 0.4f)));
        }

        // 舱室分区：相邻舱室交替冷蓝微填充，层级更清
        for (var deck = 0; deck < deckYs.Length - 1; deck++)
        {
            for (var i = 0; i < 6; i++)
            {
                if ((i + deck) % 2 == 0)
                {
                    continue;
                }

                var room = new Polygon2D();
                var rx = 150.0f + 270.0f * i;
                room.Polygon = new[]
                {
                    new Vector2(rx, deckYs[deck] + 16.0f),
                    new Vector2(rx + 270.0f, deckYs[deck] + 16.0f),
                    new Vector2(rx + 270.0f, deckYs[deck + 1]),
                    new Vector2(rx, deckYs[deck + 1]),
                };
                room.Color = new Color(0.15f, 0.35f, 0.8f, 0.05f);
                root.AddChild(room);
            }
        }

        // 骨架竖线 + 舱室隔断（隔断更粗，分区感）
        for (var i = 0; i < 7; i++)
        {
            var x = 150.0f + 270.0f * i;
            root.AddChild(Line(new[] { new Vector2(x, 300.0f), new Vector2(x, 920.0f) }, wire));
        }

        for (var i = 0; i < 6; i++)
        {
            var x = 285.0f + 270.0f * i;
            root.AddChild(Line(new[] { new Vector2(x, 520.0f), new Vector2(x, 700.0f) }, new Color(0.4f, 0.65f, 1.0f, 0.55f), 3.5f));
        }

        // 外框
        var frame = Line(
            new[]
            {
                new Vector2(150.0f, 300.0f),
                new Vector2(1770.0f, 300.0f),
                new Vector2(1770.0f, 920.0f),
                new Vector2(150.0f, 920.0f),
            },
            new Color(0.4f, 0.7f, 1.0f, 0.6f),
            3.0f);
        frame.Closed = true;
        root.AddChild(frame);
        // 蛇形扫描线网格底（单条 Line2D 铺满剖面区）+ 循环往复的亮色扫描带
        var scanPoints = new List<Vector2>();
        var sy = 304.0f;
        var scanLeft = true;
        while (sy <= 916.0f)
        {
            scanPoints.Add(new Vector2(scanLeft ? 150.0f : 1770.0f, sy));
            scanPoints.Add(new Vector2(scanLeft ? 1770.0f : 150.0f, sy));
            sy += 6.0f;
            scanLeft = !scanLeft;
        }

        root.AddChild(Line(scanPoints.ToArray(), new Color(0.4f, 0.7f, 1.0f, 0.05f), 1.0f));
        var scanBand = BgRect(new Color(0.4f, 0.8f, 1.0f, 0.06f));
        scanBand.Size = new Vector2(1620.0f, 46.0f);
        scanBand.Position = new Vector2(150.0f, 300.0f);
        root.AddChild(scanBand);
        var bandSweep = root.CreateTween().SetLoops();
        bandSweep.TweenProperty(scanBand, "position:y", 874.0f, 2.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        bandSweep.TweenProperty(scanBand, "position:y", 300.0f, 2.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        // 顶层甲板状态灯带：一排小舷灯（初始青色），链爆经过时逐点转红（重着色在 Timer 步进内完成）
        var deckLights = new List<IntroGlowDot>();
        for (var dI = 0; dI < 12; dI++)
        {
            var dl = Glow(3.5f, new Color(0.0f, 0.83f, 1.0f, 0.8f));
            dl.Position = new Vector2(220.0f + 134.0f * dI, 348.0f);
            root.AddChild(dl);
            deckLights.Add(dl);
        }

        // 链式能量路径：逐节点点亮（折线穿过各层甲板）；外发光 = 更宽更淡的叠加态底线
        var path = new[]
        {
            new Vector2(200.0f, 700.0f),
            new Vector2(500.0f, 700.0f),
            new Vector2(500.0f, 520.0f),
            new Vector2(900.0f, 520.0f),
            new Vector2(900.0f, 340.0f),
            new Vector2(1300.0f, 340.0f),
            new Vector2(1300.0f, 700.0f),
            new Vector2(1700.0f, 700.0f),
        };
        var energyGlow = Line(System.Array.Empty<Vector2>(), new Color(1.0f, 0.4f, 0.1f, 0.3f), 16.0f);
        energyGlow.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        root.AddChild(energyGlow);
        var energy = Line(System.Array.Empty<Vector2>(), new Color(1.0f, 0.45f, 0.15f), 6.0f);
        root.AddChild(energy);
        var step = new int[] { 0 };
        var shakeState = new Godot.Collections.Array { default(Variant) };  // 每次引爆刷新一次颤动峰值（连锁震感）
        var timer = new Godot.Timer { WaitTime = 0.2f, Autostart = true };  // 构建期未入树，用 autostart（入树后自动启动）
        root.AddChild(timer);
        timer.Timeout += () =>
        {
            if (step[0] >= path.Length)
            {
                timer.Stop();
                return;
            }

            var pos = path[step[0]];
            KickShake(root, 4.0f, shakeState);  // 节点引爆：轻微画面颤动脉冲
            energy.AddPoint(pos);
            energy.Width = 6.0f + step[0] * 1.5f;
            energyGlow.AddPoint(pos);
            energyGlow.Width = 16.0f + step[0] * 3.0f;
            // 节点爆闪（软光晕）+ 一次性火花溅射（textured 软点粒子，播完自毁）
            var flash = CinematicFx.SoftGlow(36.0f, new Color(1.0f, 0.55f, 0.15f, 0.9f));
            flash.Position = pos;
            var flashBase = flash.Scale;
            flash.Scale = Vector2.Zero;
            root.AddChild(flash);
            var ft = root.CreateTween();
            ft.TweenProperty(flash, "scale", flashBase * 1.4f, 0.12);
            ft.TweenProperty(flash, "modulate:a", 0.0f, 0.35);
            ft.TweenCallback(Callable.From(flash.QueueFree));
            var sparks = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 24,
                ["lifetime"] = 0.4f,
                ["one_shot"] = true,
                ["explosiveness"] = 0.9f,
                ["spread"] = 180.0f,
                ["vel_min"] = 120.0f,
                ["vel_max"] = 300.0f,
                ["damping_min"] = 80.0f,
                ["damping_max"] = 160.0f,
                ["scale_min"] = 2.0f,
                ["scale_max"] = 4.0f,
                ["color"] = new Color(1.0f, 0.6f, 0.2f, 0.95f),
            });
            sparks.Position = pos;
            root.AddChild(sparks);
            sparks.Finished += sparks.QueueFree;
            // 顶层甲板状态灯带：链爆波前经过处由青转红（步进内重着色，零逐帧开销）
            foreach (var dl in deckLights)
            {
                if (dl.Position.X <= pos.X)
                {
                    dl.DotColor = new Color(1.0f, 0.3f, 0.2f, 0.9f);
                    dl.QueueRedraw();
                }
            }

            // 冲击波纹：节点爆闪处薄环扩大淡出（仿镜头 1 冲击波）
            var ripplePoints = new Vector2[16];
            for (var rI = 0; rI < 16; rI++)
            {
                var rA = Mathf.Tau * rI / 16.0f;
                ripplePoints[rI] = new Vector2(Mathf.Cos(rA), Mathf.Sin(rA)) * 14.0f;
            }

            var ripple = Line(ripplePoints, new Color(1.0f, 0.6f, 0.2f, 0.6f), 3.0f);
            ripple.Closed = true;
            ripple.Position = pos;
            ripple.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            root.AddChild(ripple);
            var rt = root.CreateTween().SetParallel(true);
            rt.TweenProperty(ripple, "scale", Vector2.One * 3.2f, 0.5).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            rt.TweenProperty(ripple, "modulate:a", 0.0f, 0.45);
            rt.Chain().TweenCallback(Callable.From(ripple.QueueFree));
            if (step[0] == 1 || step[0] == 3 || step[0] == 5)
            {
                // 链式三连发：音量逐发递减
                GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION, -2.0f - 3.0f * (step[0] / 2) + AudioVolOffset, AudioPitch);
            }

            step[0] += 1;
        };
        return root;
    }

    // ---------------- 镜头 3：驾驶员冲刺（2.5s） ----------------

    /// <summary>侧视走廊：透视线 + 天花板管道/舱门框结构 + 黄色警示条纹反向滚动；
    /// 多段式飞行服驾驶员（骨盆/胸廓/头盔/维生背包/双关节四肢）+ 暖色边缘光 + 双层残影，两拍奔跑循环；
    /// 五条顶部锥形体积光带，前景近景支杆快速横扫（-1600px/s 强视差），红色应急灯 6Hz 闪烁，
    /// 蒸汽上飘，加密速度线，低频警报脉冲。</summary>
    private IntroRunnerShot BuildShot3()
    {
        var root = new IntroRunnerShot { Name = "Shot3" };
        root.AddChild(BgRect(new Color(0.02f, 0.02f, 0.04f)));
        // 天花板/地面透视带 + 向右侧灭点收敛的走廊线
        root.AddChild(Line(new[] { new Vector2(0.0f, 200.0f), new Vector2(1920.0f, 430.0f) }, new Color(0.16f, 0.22f, 0.32f, 0.7f), 3.0f));
        root.AddChild(Line(new[] { new Vector2(0.0f, 880.0f), new Vector2(1920.0f, 650.0f) }, new Color(0.16f, 0.22f, 0.32f, 0.7f), 3.0f));
        root.AddChild(Line(new[] { new Vector2(0.0f, 540.0f), new Vector2(1920.0f, 540.0f) }, new Color(0.1f, 0.14f, 0.22f, 0.5f)));
        var ceilPoly = new Polygon2D
        {
            Polygon = new[] { new Vector2(0.0f, 0.0f), new Vector2(1920.0f, 0.0f), new Vector2(1920.0f, 430.0f), new Vector2(0.0f, 200.0f) },
            Color = new Color(0.05f, 0.06f, 0.09f),
        };
        root.AddChild(ceilPoly);
        var floorPoly = new Polygon2D
        {
            Polygon = new[] { new Vector2(0.0f, 880.0f), new Vector2(1920.0f, 650.0f), new Vector2(1920.0f, 1080.0f), new Vector2(0.0f, 1080.0f) },
            Color = new Color(0.06f, 0.07f, 0.1f),
        };
        root.AddChild(floorPoly);
        // 天花板管道：双管沿顶棚走向 + 管节环
        var pipes = new[] { new[] { 150.0f, 380.0f, 8.0f }, new[] { 178.0f, 408.0f, 5.0f } };
        foreach (var pipe in pipes)
        {
            root.AddChild(Line(new[] { new Vector2(0.0f, pipe[0]), new Vector2(1920.0f, pipe[1]) }, new Color(0.2f, 0.26f, 0.36f), pipe[2]));
        }

        for (var i = 0; i < 5; i++)
        {
            var joint = new IntroGlowDot { Radius = 7.0f, DotColor = new Color(0.24f, 0.3f, 0.42f) };
            var jx = 200.0f + 400.0f * i;
            joint.Position = new Vector2(jx, 150.0f + jx * 230.0f / 1920.0f);
            root.AddChild(joint);
        }

        // 顶部体积光：五条锥形光带（叠加态，上窄下宽）
        for (var i = 0; i < 5; i++)
        {
            var cone = new Polygon2D();
            var cx = 320.0f + 320.0f * i;
            cone.Polygon = new[]
            {
                new Vector2(cx - 50.0f, 60.0f),
                new Vector2(cx + 50.0f, 60.0f),
                new Vector2(cx + 170.0f, 950.0f),
                new Vector2(cx - 170.0f, 950.0f),
            };
            cone.Color = new Color(1.0f, 0.85f, 0.6f, 0.05f);
            cone.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            root.AddChild(cone);
        }

        // 顶部旋转警灯光锥：红色叠加态，锚点往复扫掠
        for (var i = 0; i < 2; i++)
        {
            var beacon = new Polygon2D
            {
                Polygon = new[] { new Vector2(0.0f, 0.0f), new Vector2(-70.0f, 760.0f), new Vector2(70.0f, 760.0f) },
                Color = new Color(1.0f, 0.12f, 0.1f, 0.08f),
                Position = new Vector2(640.0f + 640.0f * i, 40.0f),
                Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
            };
            root.AddChild(beacon);
            var beaconSweep = root.CreateTween().SetLoops();
            beaconSweep.TweenProperty(beacon, "rotation", 0.55f, 1.1).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            beaconSweep.TweenProperty(beacon, "rotation", -0.55f, 1.1).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        // 黄色警示条纹（地面）+ 深色墙肋 + 舱门框：加入反向滚动列表
        for (var i = 0; i < 14; i++)
        {
            var stripe = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-40.0f, 0.0f),
                    new Vector2(-10.0f, 0.0f),
                    new Vector2(-26.0f, 14.0f),
                    new Vector2(-56.0f, 14.0f),
                },
                Color = new Color(0.9f, 0.75f, 0.1f, 0.75f),
                Position = new Vector2(80.0f + 160.0f * i, 872.0f),
            };
            root.AddChild(stripe);
            root.Scrollers.Add(stripe);
        }

        for (var i = 0; i < 9; i++)
        {
            var rib = RectPoly(26.0f, 420.0f, new Color(0.09f, 0.11f, 0.16f));
            rib.Position = new Vector2(120.0f + 240.0f * i, 400.0f);
            root.AddChild(rib);
            root.Scrollers.Add(rib);
        }

        for (var i = 0; i < 3; i++)
        {
            var doorX = 400.0f + 720.0f * i;
            var door = RectPoly(150.0f, 460.0f, new Color(0.13f, 0.17f, 0.24f));
            door.Position = new Vector2(doorX, 420.0f);
            root.AddChild(door);
            root.Scrollers.Add(door);
            var doorInner = RectPoly(118.0f, 428.0f, new Color(0.05f, 0.06f, 0.09f));
            doorInner.Position = door.Position;
            root.AddChild(doorInner);
            root.Scrollers.Add(doorInner);
        }

        // 蒸汽：墙壁管道泄漏，白色半透明上飘
        var steam = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 32,
            ["lifetime"] = 1.8f,
            ["vel_min"] = 50.0f,
            ["vel_max"] = 110.0f,
            ["spread"] = 30.0f,
            ["scale_min"] = 5.0f,
            ["scale_max"] = 11.0f,
            ["color"] = new Color(0.9f, 0.95f, 1.0f, 0.2f),
            ["additive"] = false,
        });
        steam.Position = new Vector2(1500.0f, 320.0f);
        root.AddChild(steam);
        // 驾驶员背光（叠加态暖光，把剪影从暗舱里托出来）
        var backlight = CinematicFx.SoftGlow(170.0f, new Color(1.0f, 0.6f, 0.25f, 0.16f));
        backlight.Position = new Vector2(880.0f, 520.0f);
        root.AddChild(backlight);
        // 驾驶员：多段式飞行服人物（骨盆/胸廓/头盔/维生背包/双关节四肢），两拍奔跑由 _process 相位驱动
        var bodyColor = new Color(0.24f, 0.3f, 0.4f);  // 近侧肢体
        var farColor = new Color(0.14f, 0.18f, 0.26f);  // 远侧肢体（深度层次）
        var edgeColor = new Color(0.55f, 0.66f, 0.84f, 0.7f);  // 分件边缘线
        var chestPoints = new[] { new Vector2(-7.0f, -14.0f), new Vector2(13.0f, -16.0f), new Vector2(17.0f, -42.0f), new Vector2(-3.0f, -46.0f) };
        // 双层残影（动态模糊）：胸廓/头盔淡影，拖在奔跑反方向
        foreach (var k in new[] { 2, 1 })
        {
            var ghost = new Node2D
            {
                Position = new Vector2(880.0f - 40.0f * k, 566.0f + 5.0f * k),
                Scale = Vector2.One * 2.3f,
                Rotation = 0.3f,
                Modulate = new Color(1.0f, 1.0f, 1.0f, 0.06f + 0.05f * (2 - k)),
            };
            root.AddChild(ghost);
            var gTorso = new Polygon2D { Polygon = chestPoints, Color = bodyColor };
            ghost.AddChild(gTorso);
            var gHead = new IntroGlowDot { Radius = 10.5f, DotColor = bodyColor, Position = new Vector2(11.0f, -62.0f) };
            ghost.AddChild(gHead);
        }

        var pilot = new Node2D
        {
            Position = new Vector2(880.0f, 566.0f),
            Scale = Vector2.One * 2.3f,
        };
        root.AddChild(pilot);
        root.BobNode = pilot;
        root.BobBaseY = pilot.Position.Y;
        // 腿部（远侧先画）：髋→大腿→膝→小腿→飞行靴（靴底加厚线）
        foreach (var sideI in new[] { 1, 0 })
        {
            var c = sideI == 0 ? bodyColor : farColor;
            var hip = new Node2D { Position = new Vector2(2.0f - 4.0f * sideI, -4.0f + 2.0f * sideI) };
            pilot.AddChild(hip);
            var thigh = RectPoly(6.5f, 22.0f, c);
            thigh.Position = new Vector2(0.0f, 11.0f);
            hip.AddChild(thigh);
            thigh.AddChild(Line(new[] { new Vector2(2.6f, -9.0f), new Vector2(2.6f, 9.0f) }, edgeColor, 1.2f));
            var knee = new Node2D { Position = new Vector2(0.0f, 22.0f) };
            hip.AddChild(knee);
            var shin = RectPoly(5.0f, 20.0f, c);
            shin.Position = new Vector2(0.0f, 10.0f);
            knee.AddChild(shin);
            var boot = new Polygon2D
            {
                Polygon = new[] { new Vector2(-4.0f, 16.0f), new Vector2(7.0f, 16.0f), new Vector2(10.0f, 22.0f), new Vector2(-4.0f, 22.0f) },
                Color = c,
            };
            knee.AddChild(boot);
            knee.AddChild(Line(new[] { new Vector2(-4.0f, 22.5f), new Vector2(10.0f, 22.5f) }, edgeColor, 1.6f));
            root.HipPivots.Add(hip);
            root.KneePivots.Add(knee);
        }

        // 躯干组：绕骨盆前倾 0.3rad（胸廓/背包/头盔/手臂随体倾斜）
        var torsoGrp = new Node2D { Rotation = 0.3f };
        pilot.AddChild(torsoGrp);
        var pelvis = new Polygon2D
        {
            Polygon = new[] { new Vector2(-9.0f, -2.0f), new Vector2(7.0f, -4.0f), new Vector2(9.0f, -14.0f), new Vector2(-7.0f, -14.0f) },
            Color = bodyColor,
        };
        torsoGrp.AddChild(pelvis);
        // 生命维持背包（背部方块结构 + 顶部管线 + 青色指示灯）与腰侧挂点
        var backpack = RectPoly(12.0f, 24.0f, farColor);
        backpack.Position = new Vector2(-11.0f, -30.0f);
        torsoGrp.AddChild(backpack);
        torsoGrp.AddChild(Line(new[] { new Vector2(-11.0f, -44.0f), new Vector2(-11.0f, -50.0f), new Vector2(2.0f, -54.0f) }, edgeColor, 1.4f));
        var packLight = Glow(2.0f, new Color(0.0f, 0.83f, 1.0f, 0.8f));
        packLight.Position = new Vector2(-13.0f, -24.0f);
        torsoGrp.AddChild(packLight);
        var pouch = RectPoly(5.0f, 7.0f, farColor);
        pouch.Position = new Vector2(-9.0f, -8.0f);
        torsoGrp.AddChild(pouch);
        // 胸廓 + 胸包 + 前缘分件线 + 肩部护甲
        var chest = new Polygon2D { Polygon = chestPoints, Color = bodyColor };
        torsoGrp.AddChild(chest);
        torsoGrp.AddChild(Line(new[] { new Vector2(13.0f, -16.0f), new Vector2(17.0f, -42.0f) }, edgeColor, 1.6f));
        var chestPack = RectPoly(6.0f, 9.0f, new Color(0.3f, 0.38f, 0.5f));
        chestPack.Position = new Vector2(11.0f, -28.0f);
        torsoGrp.AddChild(chestPack);
        var shoulderPad = new Polygon2D
        {
            Polygon = new[] { new Vector2(-4.0f, -52.0f), new Vector2(10.0f, -54.0f), new Vector2(12.0f, -44.0f), new Vector2(-2.0f, -43.0f) },
            Color = new Color(0.22f, 0.28f, 0.38f),
        };
        torsoGrp.AddChild(shoulderPad);
        // 颈 + 头盔（面罩高光 + 暖色边缘光）
        var neck = RectPoly(4.0f, 7.0f, bodyColor);
        neck.Position = new Vector2(8.0f, -51.0f);
        torsoGrp.AddChild(neck);
        var helmet = new IntroGlowDot { Radius = 10.5f, DotColor = bodyColor, Position = new Vector2(11.0f, -62.0f) };
        torsoGrp.AddChild(helmet);
        var helmetRim = CinematicFx.SoftGlow(12.0f, new Color(1.0f, 0.6f, 0.3f, 0.3f));
        helmetRim.Position = new Vector2(13.0f, -64.0f);
        torsoGrp.AddChild(helmetRim);
        var visor = Glow(3.5f, new Color(0.5f, 0.9f, 1.0f, 0.8f));
        visor.Position = new Vector2(19.0f, -64.0f);
        torsoGrp.AddChild(visor);
        // 躯干暖色边缘光（胸廓描边副本，叠加态微偏移）
        var rimTorso = new Polygon2D
        {
            Polygon = chestPoints,
            Color = new Color(1.0f, 0.6f, 0.3f, 0.3f),
            Position = new Vector2(2.0f, -2.0f),
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        torsoGrp.AddChild(rimTorso);
        // 手臂（远侧先画）：肩→上臂→肘→前臂→手，与对侧腿反相摆动
        foreach (var sideI in new[] { 1, 0 })
        {
            var c = sideI == 0 ? bodyColor : farColor;
            var shoulder = new Node2D { Position = new Vector2(8.0f - 6.0f * sideI, -42.0f + 2.0f * sideI) };
            torsoGrp.AddChild(shoulder);
            var upper = RectPoly(5.0f, 16.0f, c);
            upper.Position = new Vector2(0.0f, 8.0f);
            shoulder.AddChild(upper);
            var elbow = new Node2D { Position = new Vector2(0.0f, 16.0f) };
            shoulder.AddChild(elbow);
            var forearm = RectPoly(4.5f, 15.0f, c);
            forearm.Position = new Vector2(0.0f, 7.5f);
            elbow.AddChild(forearm);
            forearm.AddChild(Line(new[] { new Vector2(1.8f, -6.0f), new Vector2(1.8f, 6.0f) }, edgeColor, 1.2f));
            var hand = new IntroGlowDot { Radius = 4.0f, DotColor = c, Position = new Vector2(0.0f, 16.0f) };
            elbow.AddChild(hand);
            root.ShoulderPivots.Add(shoulder);
            root.ElbowPivots.Add(elbow);
        }

        // 速度线（加密）
        for (var i = 0; i < 14; i++)
        {
            var sl = Line(new[] { Vector2.Zero, new Vector2(-160.0f - (float)GD.Randf() * 140.0f, 0.0f) }, new Color(0.8f, 0.9f, 1.0f, 0.28f), 2.0f);
            sl.Position = new Vector2((float)GD.Randf() * 2200.0f, (float)GD.Randf() * 1080.0f);
            root.AddChild(sl);
            root.SpeedLines.Add(sl);
        }

        // 前景近景支杆 ×4：宽斜边深色半透明立柱，-1600px/s 反向横扫回卷（近景强视差，在红闪之下受染色）
        for (var fgI = 0; fgI < 4; fgI++)
        {
            var fg = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-90.0f, -620.0f),
                    new Vector2(70.0f, -620.0f),
                    new Vector2(130.0f, 620.0f),
                    new Vector2(-30.0f, 620.0f),
                },
                Color = new Color(0.01f, 0.015f, 0.03f, 0.6f),
                Position = new Vector2(300.0f + 630.0f * fgI, 540.0f),
            };
            root.AddChild(fg);
            root.FgStruts.Add(fg);
        }

        // 红色应急灯全屏闪烁
        var red = BgRect(new Color(1.0f, 0.1f, 0.1f, 0.0f));
        root.AddChild(red);
        root.Red = red;
        // 低频警报脉冲（既有命中音压低至 -14dB 基础，叠加过场音量策略，0.7s 间隔）
        var alarm = new Godot.Timer { WaitTime = 0.7f, Autostart = true };
        root.AddChild(alarm);
        alarm.Timeout += () => GameState.Instance.PlaySfx(GameState.Instance.SFX_PLAYER_HIT, -14.0f + AudioVolOffset, AudioPitch);
        return root;
    }

    // ---------------- 镜头 4：操作台紧急启动（2.5s） ----------------

    /// <summary>驾驶舱前景框架（含舱壁缝线）+ 两侧仪表副屏（玻璃高光；左副屏带雷达距圈/扫掠针/回波亮点）+ 三分区航电控制台
    /// （斜切梯形台体：推进区按钮簇+节流阀滑槽 / 导航区旋钮+按钮排 / 武器区拨杆开关，LED 指示排+分区铭牌）+
    /// 4 指+拇指手形剪影带按下起伏地点按 + 主屏红色倒计时 3→2→1（bezel 边框）与警告行闪烁 + 两侧金属把手；
    /// 倒计时结束五指扣合把手，结尾 0.5s 整体后仰 -3° + 短促震动 + 屏幕白光渐强。</summary>
    private IntroConsoleShot BuildShot4()
    {
        var dur = _shotDurations[3];
        var root = new IntroConsoleShot { Name = "Shot4" };
        root.AddChild(BgRect(new Color(0.02f, 0.03f, 0.05f)));
        // 舱体前景框架（左右楔 + 顶梁）
        var frameColor = new Color(0.05f, 0.06f, 0.09f);
        var leftWedge = new Polygon2D
        {
            Polygon = new[] { new Vector2(0.0f, 0.0f), new Vector2(280.0f, 0.0f), new Vector2(180.0f, 1080.0f), new Vector2(0.0f, 1080.0f) },
            Color = frameColor,
        };
        root.AddChild(leftWedge);
        var rightWedge = new Polygon2D
        {
            Polygon = new[] { new Vector2(1920.0f, 0.0f), new Vector2(1640.0f, 0.0f), new Vector2(1740.0f, 1080.0f), new Vector2(1920.0f, 1080.0f) },
            Color = frameColor,
        };
        root.AddChild(rightWedge);
        var topBeam = new Polygon2D
        {
            Polygon = new[] { new Vector2(0.0f, 0.0f), new Vector2(1920.0f, 0.0f), new Vector2(1920.0f, 120.0f), new Vector2(0.0f, 170.0f) },
            Color = frameColor,
        };
        root.AddChild(topBeam);
        // 舱壁缝线（框架上的细结构线，增加舱内细节密度）
        var seamColor = new Color(0.14f, 0.17f, 0.24f);
        for (var i = 0; i < 4; i++)
        {
            root.AddChild(Line(
                new[] { new Vector2(40.0f + 50.0f * i, 200.0f + 180.0f * i), new Vector2(210.0f - 20.0f * i, 240.0f + 180.0f * i) },
                seamColor,
                1.5f));
            root.AddChild(Line(
                new[] { new Vector2(1880.0f - 50.0f * i, 200.0f + 180.0f * i), new Vector2(1710.0f + 20.0f * i, 240.0f + 180.0f * i) },
                seamColor,
                1.5f));
        }

        root.AddChild(Line(new[] { new Vector2(0.0f, 150.0f), new Vector2(1920.0f, 105.0f) }, seamColor, 1.5f));
        // 顶梁青色灯带：宽淡底光 + 窄亮芯
        var stripGlow = Line(new[] { new Vector2(280.0f, 138.0f), new Vector2(1640.0f, 112.0f) }, new Color(0.0f, 0.83f, 1.0f, 0.12f), 8.0f);
        stripGlow.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        root.AddChild(stripGlow);
        root.AddChild(Line(new[] { new Vector2(280.0f, 138.0f), new Vector2(1640.0f, 112.0f) }, new Color(0.0f, 0.83f, 1.0f, 0.35f), 2.5f));
        // 两侧仪表副屏：青色小屏 + 迷你波形/柱状线
        var sides = new[] { new[] { 330.0f, 250.0f }, new[] { 1390.0f, 250.0f } };
        foreach (var side in sides)
        {
            var subScreen = RectPoly(200.0f, 130.0f, new Color(0.02f, 0.1f, 0.14f));
            subScreen.Position = new Vector2(side[0] + 100.0f, side[1] + 65.0f);
            root.AddChild(subScreen);
            var subBorder = Line(
                new[]
                {
                    new Vector2(side[0], side[1]),
                    new Vector2(side[0] + 200.0f, side[1]),
                    new Vector2(side[0] + 200.0f, side[1] + 130.0f),
                    new Vector2(side[0], side[1] + 130.0f),
                },
                UITheme.AccentDim,
                2.0f);
            subBorder.Closed = true;
            root.AddChild(subBorder);
            // 副屏玻璃高光斜纹
            var subGlass = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(side[0] + 40.0f, side[1]),
                    new Vector2(side[0] + 80.0f, side[1]),
                    new Vector2(side[0] + 36.0f, side[1] + 130.0f),
                    new Vector2(side[0] + 6.0f, side[1] + 130.0f),
                },
                Color = new Color(1.0f, 1.0f, 1.0f, 0.05f),
                Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
            };
            root.AddChild(subGlass);
            var wavePoints = new Vector2[9];
            for (var w = 0; w < 9; w++)
            {
                wavePoints[w] = new Vector2(side[0] + 18.0f + 20.0f * w, side[1] + 65.0f + Mathf.Sin(w * 1.4f) * 32.0f);
            }

            root.AddChild(Line(wavePoints, new Color(0.0f, 0.83f, 1.0f, 0.7f), 2.0f));
            for (var bar = 0; bar < 4; bar++)
            {
                var b = RectPoly(14.0f, 20.0f + 14.0f * bar, new Color(0.0f, 0.83f, 1.0f, 0.4f));
                b.Position = new Vector2(side[0] + 30.0f + 30.0f * bar, side[1] + 118.0f - 7.0f * bar);
                root.AddChild(b);
            }
        }

        // 左副屏雷达：静态距圈 + 旋转扫掠针（叠加态）+ 2 枚回波亮点（扫过点亮，_ConsoleShot._process 驱动）
        var radarC = new Vector2(430.0f, 315.0f);
        var radarRingPts = new Vector2[24];
        for (var rr = 0; rr < 24; rr++)
        {
            var ra = Mathf.Tau * rr / 24.0f;
            radarRingPts[rr] = radarC + new Vector2(Mathf.Cos(ra), Mathf.Sin(ra)) * 52.0f;
        }

        var radarRing = Line(radarRingPts, new Color(0.0f, 0.83f, 1.0f, 0.3f), 1.5f);
        radarRing.Closed = true;
        root.AddChild(radarRing);
        var sweep = Line(new[] { Vector2.Zero, new Vector2(52.0f, 0.0f) }, new Color(0.4f, 0.95f, 1.0f, 0.8f), 2.5f);
        sweep.Position = radarC;
        sweep.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        root.AddChild(sweep);
        root.RadarSweep = sweep;
        var blipAngles = new List<float>();
        var blipEs = new List<float>();
        foreach (var bA in new[] { 0.9f, 3.8f })
        {
            var blip = CinematicFx.SoftGlow(4.0f, new Color(0.5f, 1.0f, 1.0f, 0.9f));
            blip.Position = radarC + new Vector2(Mathf.Cos(bA), Mathf.Sin(bA)) * 34.0f;
            root.AddChild(blip);
            root.RadarBlips.Add(blip);
            blipAngles.Add(bA);
            blipEs.Add(0.0f);
        }

        root.RadarBlipAngles = blipAngles.ToArray();
        root.RadarBlipE = blipEs.ToArray();
        // 控制台：斜切梯形台体 + 三功能分区（推进/导航/武器），控制件成组分布（构图避开底部 letterbox）
        var consoleBody = new Polygon2D
        {
            Polygon = new[] { new Vector2(320.0f, 700.0f), new Vector2(1600.0f, 700.0f), new Vector2(1700.0f, 1080.0f), new Vector2(220.0f, 1080.0f) },
            Color = new Color(0.08f, 0.1f, 0.14f),
        };
        root.AddChild(consoleBody);
        // 台面沿口高光 + 侧棱线 + 台面缝线
        root.AddChild(Line(new[] { new Vector2(320.0f, 700.0f), new Vector2(1600.0f, 700.0f) }, UITheme.PanelBorder, 2.0f));
        root.AddChild(Line(new[] { new Vector2(320.0f, 700.0f), new Vector2(220.0f, 1080.0f) }, new Color(0.14f, 0.17f, 0.24f), 1.5f));
        root.AddChild(Line(new[] { new Vector2(1600.0f, 700.0f), new Vector2(1700.0f, 1080.0f) }, new Color(0.14f, 0.17f, 0.24f), 1.5f));
        root.AddChild(Line(new[] { new Vector2(340.0f, 1000.0f), new Vector2(1580.0f, 1000.0f) }, new Color(0.14f, 0.17f, 0.24f), 1.5f));
        // 分区隔线（随台体透视微斜）
        foreach (var zx in new[] { 770.0f, 1150.0f })
        {
            root.AddChild(Line(new[] { new Vector2(zx, 720.0f), new Vector2(zx - 20.0f, 1000.0f) }, new Color(0.18f, 0.22f, 0.3f), 1.5f));
        }

        // 分区铭牌条：暗底 + 顶部 accent 细线 + 小字
        var zones = new (float X, string Key)[]
        {
            (450.0f, "INTRO_ZONE_PROP"),
            (960.0f, "INTRO_ZONE_NAV"),
            (1380.0f, "INTRO_ZONE_WPN"),
        };
        foreach (var zd in zones)
        {
            var plate = RectPoly(110.0f, 22.0f, new Color(0.03f, 0.12f, 0.16f));
            plate.Position = new Vector2(zd.X, 737.0f);
            root.AddChild(plate);
            root.AddChild(Line(new[] { new Vector2(zd.X - 55.0f, 726.0f), new Vector2(zd.X + 55.0f, 726.0f) }, new Color(0.0f, 0.83f, 1.0f, 0.4f), 1.6f));
            var plateLabel = UITheme.MakeLabel((string)Tr(zd.Key), UITheme.FontCaption, UITheme.Accent);
            plateLabel.AddThemeFontSizeOverride("font_size", 15);
            plateLabel.Position = new Vector2(zd.X - 55.0f, 726.0f);
            plateLabel.Size = new Vector2(110.0f, 22.0f);
            plateLabel.HorizontalAlignment = HorizontalAlignment.Center;
            root.AddChild(plateLabel);
        }

        // LED 指示排（每区一排，青/红交替）
        foreach (var lz in new[] { 400.0f, 900.0f, 1250.0f })
        {
            for (var lI = 0; lI < 5; lI++)
            {
                var led = new IntroGlowDot
                {
                    Radius = 3.0f,
                    DotColor = lI % 2 == 0 ? new Color(0.0f, 0.83f, 1.0f, 0.85f) : new Color(0.9f, 0.2f, 0.25f, 0.85f),
                    Position = new Vector2(lz + 14.0f * lI, 766.0f),
                };
                root.AddChild(led);
            }
        }

        var cells = new List<Polygon2D>();
        // 按钮簇：背板 + rows×cols 小按钮（闪烁重着色，也是双手点按目标池）+ 板角状态 LED
        Action<float, float, int, int> cluster = (cx, cy, cols, rows) =>
        {
            var plate = RectPoly(28.0f * cols + 16.0f, 26.0f * rows + 14.0f, new Color(0.04f, 0.05f, 0.08f));
            plate.Position = new Vector2(cx, cy);
            root.AddChild(plate);
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    var btn = RectPoly(22.0f, 18.0f, new Color(0.0f, 0.3f, 0.4f, 0.5f));
                    btn.Position = new Vector2(cx - 14.0f * (cols - 1) + 28.0f * col, cy - 13.0f * (rows - 1) + 26.0f * row);
                    root.AddChild(btn);
                    cells.Add(btn);
                }
            }

            for (var ledI = 0; ledI < 2; ledI++)  // 簇板顶边两角状态灯（静态，青/红各一）
            {
                var clLed = new IntroGlowDot
                {
                    Radius = 2.5f,
                    DotColor = ledI == 0 ? new Color(0.0f, 0.83f, 1.0f, 0.9f) : new Color(0.9f, 0.2f, 0.25f, 0.9f),
                    Position = new Vector2(cx + 14.0f * cols * (-1.0f + 2.0f * ledI), cy - 13.0f * rows),
                };
                root.AddChild(clLed);
            }
        };
        cluster(450.0f, 855.0f, 4, 2);  // 推进区按钮簇
        cluster(960.0f, 950.0f, 4, 1);  // 导航区按钮排
        cluster(1445.0f, 855.0f, 2, 2);  // 武器区按钮簇
        root.Cells = cells;
        // 推进区：双节流阀滑槽（轨道 + 刻度 + 手柄 + 红色标识线）
        for (var sI = 0; sI < 2; sI++)
        {
            var sx = 640.0f + 60.0f * sI;
            root.AddChild(Line(new[] { new Vector2(sx, 790.0f), new Vector2(sx, 930.0f) }, new Color(0.2f, 0.25f, 0.34f), 4.0f));
            for (var tick = 0; tick < 4; tick++)
            {
                root.AddChild(Line(
                    new[] { new Vector2(sx + 8.0f, 802.0f + 32.0f * tick), new Vector2(sx + 16.0f, 802.0f + 32.0f * tick) },
                    new Color(0.3f, 0.36f, 0.46f, 0.6f),
                    1.2f));
            }

            var handle = RectPoly(22.0f, 12.0f, new Color(0.5f, 0.55f, 0.62f));
            handle.Position = new Vector2(sx, 830.0f + 44.0f * sI);
            root.AddChild(handle);
            root.AddChild(Line(
                new[] { new Vector2(sx - 8.0f, 830.0f + 44.0f * sI), new Vector2(sx + 8.0f, 830.0f + 44.0f * sI) },
                UITheme.Danger,
                2.0f));
        }

        // 导航区：双旋钮（底座圆 + 刻度环 + 指针）
        for (var kI = 0; kI < 2; kI++)
        {
            var kx = 880.0f + 160.0f * kI;
            var knob = new IntroGlowDot { Radius = 22.0f, DotColor = new Color(0.05f, 0.07f, 0.1f), Position = new Vector2(kx, 855.0f) };
            root.AddChild(knob);
            var knobRingPoints = new Vector2[20];
            for (var pI = 0; pI < 20; pI++)
            {
                var pA = Mathf.Tau * pI / 20.0f;
                knobRingPoints[pI] = new Vector2(kx + Mathf.Cos(pA) * 27.0f, 855.0f + Mathf.Sin(pA) * 27.0f);
            }

            var knobRing = Line(knobRingPoints, new Color(0.3f, 0.38f, 0.5f), 2.0f);
            knobRing.Closed = true;
            root.AddChild(knobRing);
            var pointerA = -0.6f + 1.9f * kI;
            root.AddChild(Line(
                new[] { new Vector2(kx, 855.0f), new Vector2(kx + Mathf.Cos(pointerA) * 19.0f, 855.0f + Mathf.Sin(pointerA) * 19.0f) },
                UITheme.Accent,
                3.0f));
        }

        // 武器区：三拨杆开关（槽位 + 拨杆 + 状态灯头）
        for (var tI = 0; tI < 3; tI++)
        {
            var tx = 1230.0f + 70.0f * tI;
            var slot = RectPoly(10.0f, 34.0f, new Color(0.03f, 0.04f, 0.06f));
            slot.Position = new Vector2(tx, 855.0f);
            root.AddChild(slot);
            var leverUp = tI != 1;
            var tipY = leverUp ? 843.0f : 867.0f;
            root.AddChild(Line(new[] { new Vector2(tx, 855.0f), new Vector2(tx + 6.0f, tipY) }, new Color(0.6f, 0.65f, 0.72f), 4.0f));
            var tip = Glow(3.0f, leverUp ? new Color(0.9f, 0.2f, 0.25f, 0.9f) : new Color(0.0f, 0.83f, 1.0f, 0.9f));
            tip.Position = new Vector2(tx + 6.0f, tipY);
            root.AddChild(tip);
        }

        var blink = new Godot.Timer { WaitTime = 0.09f, Autostart = true };  // 构建期未入树，用 autostart
        root.AddChild(blink);
        blink.Timeout += () =>
        {
            var palette = new[]
            {
                new Color(0.0f, 0.83f, 1.0f, 0.9f),
                new Color(1.0f, 0.6f, 0.15f, 0.9f),
                new Color(0.0f, 0.3f, 0.4f, 0.4f),
                new Color(0.9f, 0.2f, 0.25f, 0.9f),
            };
            for (var k = 0; k < 6; k++)
            {
                cells[(int)(GD.Randi() % (uint)cells.Count)].Color = palette[(int)(GD.Randi() % (uint)palette.Length)];
            }
        };
        // 主屏：bezel 边框底板 + 红底倒计时 + 进度环/扫描弧 + 警告行闪烁 + 滚动状态日志
        var bezel = RectPoly(560.0f, 360.0f, new Color(0.04f, 0.05f, 0.08f));
        bezel.Position = new Vector2(960.0f, 380.0f);
        root.AddChild(bezel);
        var screen = RectPoly(520.0f, 320.0f, new Color(0.15f, 0.03f, 0.05f));
        screen.Position = new Vector2(960.0f, 380.0f);
        root.AddChild(screen);
        var screenBorder = Line(
            new[]
            {
                new Vector2(700.0f, 220.0f),
                new Vector2(1220.0f, 220.0f),
                new Vector2(1220.0f, 540.0f),
                new Vector2(700.0f, 540.0f),
            },
            UITheme.Danger,
            3.0f);
        screenBorder.Closed = true;
        root.AddChild(screenBorder);
        // 主屏玻璃高光斜纹
        var glass = new Polygon2D
        {
            Polygon = new[] { new Vector2(780.0f, 220.0f), new Vector2(890.0f, 220.0f), new Vector2(800.0f, 540.0f), new Vector2(710.0f, 540.0f) },
            Color = new Color(1.0f, 1.0f, 1.0f, 0.05f),
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        root.AddChild(glass);
        // 倒计时外圈：静态进度环 + 每秒一圈的红色扫描弧
        var ringPoints = new Vector2[48];
        for (var rI = 0; rI < 48; rI++)
        {
            var rA = Mathf.Tau * rI / 48.0f;
            ringPoints[rI] = new Vector2(960.0f + Mathf.Cos(rA) * 95.0f, 312.0f + Mathf.Sin(rA) * 95.0f);
        }

        var cdRing = Line(ringPoints, new Color(1.0f, 0.25f, 0.25f, 0.35f), 3.0f);
        cdRing.Closed = true;
        root.AddChild(cdRing);
        var arcPoints = new Vector2[12];
        for (var aI = 0; aI < 12; aI++)
        {
            var aA = -Mathf.Pi * 0.5f + aI / 12.0f;
            arcPoints[aI] = new Vector2(Mathf.Cos(aA) * 95.0f, Mathf.Sin(aA) * 95.0f);
        }

        var cdArc = Line(arcPoints, new Color(1.0f, 0.45f, 0.4f, 0.9f), 5.0f);
        cdArc.Position = new Vector2(960.0f, 312.0f);
        root.AddChild(cdArc);
        var arcSweep = root.CreateTween().SetLoops();
        arcSweep.TweenProperty(cdArc, "rotation", Mathf.Tau, 0.6).SetTrans(Tween.TransitionType.Linear);
        var countdown = UITheme.MakeLabel("3", UITheme.FontDisplay, UITheme.Danger);
        countdown.Position = new Vector2(860.0f, 252.0f);
        countdown.Size = new Vector2(200.0f, 120.0f);
        countdown.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(countdown);
        var warning = UITheme.MakeLabel((string)Tr("INTRO_WARNING"), UITheme.FontHeader, UITheme.Danger);
        warning.Position = new Vector2(760.0f, 395.0f);
        warning.Size = new Vector2(400.0f, 44.0f);
        warning.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(warning);
        // 滚动状态日志：INTRO_LOG_1..4 四条 i18n 键，Timer 回调只换 text（零逐帧分配）
        var logLines = new List<Label>();
        for (var li = 0; li < 3; li++)
        {
            var logLine = UITheme.MakeLabel((string)Tr(GdFormat.Format("INTRO_LOG_%d", li + 1)), UITheme.FontCaption, new Color(1.0f, 0.55f, 0.5f));
            logLine.Position = new Vector2(720.0f, 452.0f + 26.0f * li);
            logLine.Size = new Vector2(480.0f, 24.0f);
            root.AddChild(logLine);
            logLines.Add(logLine);
        }

        var remain = new int[] { 3 };
        var countTimer = new Godot.Timer { WaitTime = 0.6f, Autostart = true };
        root.AddChild(countTimer);
        countTimer.Timeout += () =>
        {
            remain[0] -= 1;
            if (remain[0] <= 0)
            {
                countTimer.Stop();
                countdown.Text = "!";
            }
            else
            {
                countdown.Text = remain[0].ToString();
            }
        };
        var warnBlink = new Godot.Timer { WaitTime = 0.4f, Autostart = true };
        root.AddChild(warnBlink);
        warnBlink.Timeout += () => warning.Visible = !warning.Visible;
        // 日志轮换：0.7s 步进，INTRO_LOG_1..4 四条键在三行间滚动
        var logStep = new int[] { 0 };
        var logTimer = new Godot.Timer { WaitTime = 0.7f, Autostart = true };
        root.AddChild(logTimer);
        logTimer.Timeout += () =>
        {
            logStep[0] = (logStep[0] + 1) % 4;
            for (var li = 0; li < 3; li++)
            {
                logLines[li].Text = (string)Tr(GdFormat.Format("INTRO_LOG_%d", (logStep[0] + li) % 4 + 1));
            }
        };
        // 两侧金属把手
        var handleL = RectPoly(36.0f, 160.0f, new Color(0.5f, 0.55f, 0.62f));
        handleL.Position = new Vector2(380.0f, 560.0f);
        root.AddChild(handleL);
        var handleR = RectPoly(36.0f, 160.0f, new Color(0.5f, 0.55f, 0.62f));
        handleR.Position = new Vector2(1540.0f, 560.0f);
        root.AddChild(handleR);
        root.Handles = new List<Vector2> { handleL.Position, handleR.Position };
        // 双手：4 指+拇指手形剪影（前臂自下方伸入），点按带按下-抬起起伏；结尾五指扣合把手
        for (var h = 0; h < 2; h++)
        {
            var hand = new Node2D { Position = new Vector2(700.0f + 500.0f * h, 940.0f) };
            // 前臂（斜向下方伸出画面）
            var forearm = RectPoly(18.0f, 260.0f, new Color(0.16f, 0.2f, 0.28f));
            forearm.Position = new Vector2(-40.0f + 80.0f * h, 130.0f);
            forearm.Rotation = 0.35f - 0.7f * h;
            hand.AddChild(forearm);
            // 张开手形：腕→掌背→4 指节阶梯→拇指侧缘
            var openShape = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-9.0f, 12.0f),
                    new Vector2(-10.0f, -6.0f),
                    new Vector2(-8.0f, -18.0f),
                    new Vector2(-5.0f, -20.0f),
                    new Vector2(-4.0f, -10.0f),
                    new Vector2(-1.0f, -22.0f),
                    new Vector2(0.0f, -11.0f),
                    new Vector2(3.0f, -23.0f),
                    new Vector2(4.0f, -11.0f),
                    new Vector2(7.0f, -20.0f),
                    new Vector2(8.0f, -6.0f),
                    new Vector2(13.0f, -1.0f),
                    new Vector2(11.0f, 5.0f),
                    new Vector2(7.0f, 6.0f),
                    new Vector2(9.0f, 12.0f),
                },
                Color = new Color(0.2f, 0.25f, 0.34f),
            };
            hand.AddChild(openShape);
            // 扣合手形：握拳剪影（指节凹槽线朝把手外侧，初始隐藏）
            var gripShape = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-14.0f, -26.0f),
                    new Vector2(14.0f, -26.0f),
                    new Vector2(22.0f, -18.0f),
                    new Vector2(22.0f, 18.0f),
                    new Vector2(14.0f, 26.0f),
                    new Vector2(-14.0f, 26.0f),
                    new Vector2(-22.0f, 18.0f),
                    new Vector2(-22.0f, -18.0f),
                },
                Color = openShape.Color,
                Visible = false,
            };
            var grooveSign = -1.0f + 2.0f * h;  // 左手凹槽在 -x 侧，右手镜像
            for (var gI = 0; gI < 3; gI++)
            {
                var groove = Line(
                    new[] { new Vector2(grooveSign * 22.0f, -10.0f + 10.0f * gI), new Vector2(grooveSign * 9.0f, -10.0f + 10.0f * gI) },
                    new Color(0.1f, 0.13f, 0.19f),
                    2.0f);
                gripShape.AddChild(groove);
            }

            hand.AddChild(gripShape);
            var palmRim = CinematicFx.SoftGlow(15.0f, new Color(0.0f, 0.83f, 1.0f, 0.15f));
            hand.AddChild(palmRim);
            root.AddChild(hand);
            root.Hands.Add(hand);
            root.Targets.Add(hand.Position);
            root.OpenShapes.Add(openShape);
            root.GripShapes.Add(gripShape);
        }

        // 结尾 0.5s：双手抓把手 + 整体后仰 + 短促震动 + 屏幕白光渐强
        var white = BgRect(new Color(1.0f, 1.0f, 1.0f, 0.0f));
        root.AddChild(white);
        var endTimer = new Godot.Timer { OneShot = true, WaitTime = Mathf.Max(dur - 0.5f, 0.1f), Autostart = true };
        root.AddChild(endTimer);
        endTimer.Timeout += () =>
        {
            root.Grabbing = true;
            for (var hI = 0; hI < 2; hI++)
            {
                root.OpenShapes[hI].Visible = false;
                root.GripShapes[hI].Visible = true;  // 换握拳剪影扣上把手（不动前臂朝向）
            }

            var tween = root.CreateTween().SetParallel(true);
            tween.TweenProperty(root, "rotation", Mathf.DegToRad(-3.0f), 0.5);
            tween.TweenProperty(white, "color:a", 0.9f, 0.5);
            // 顿悟瞬间的短促震动：±5px 快速抖动 6 次
            var shake = root.CreateTween();
            for (var sI = 0; sI < 6; sI++)
            {
                shake.TweenProperty(root, "position", new Vector2((float)GD.RandRange(-5.0, 5.0), (float)GD.RandRange(-5.0, 5.0)), 0.06);
            }

            shake.TweenProperty(root, "position", Vector2.Zero, 0.08);
        };
        return root;
    }

    // ---------------- 镜头 5：弹射尾追视角（2.8s） ----------------

    /// <summary>尾部视角：玩家机贴图置于画面中心偏下（调暗 + 暖色软轮廓光，对齐镜头 6 剪影光比）+ 双层橙色拖影（动态模糊）；
    /// 点火预热 ~0.3s（尾焰/喷口辉光从 0 升起 + 白闪脉冲，时轴随镜头时长缩放）；
    /// 轨道 = 两侧透视收缩的壁面 + 斜向结构线加速向后流动（取代横档「梯子」）；
    /// 尾部双火焰 + 亮白内芯 + 机身两侧舔舐火焰舌 + 双壁轨道电火花；全屏速度线 + 容器 ±6px 震动；引擎音持续。</summary>
    private IntroChaseShot BuildShot5()
    {
        var dur = _shotDurations[4];
        var root = new IntroChaseShot { Name = "Shot5" };
        var shakeRoot = new Node2D();
        root.AddChild(shakeRoot);
        root.ShakeRoot = shakeRoot;
        shakeRoot.AddChild(BgRect(new Color(0.02f, 0.02f, 0.05f)));
        // 轨道壁面：两侧梯形壁板（透视向顶部收缩）+ 内外棱线
        var wallColor = new Color(0.09f, 0.11f, 0.15f);
        var leftWall = new Polygon2D
        {
            Polygon = new[] { new Vector2(430.0f, -50.0f), new Vector2(700.0f, -50.0f), new Vector2(620.0f, 1130.0f), new Vector2(250.0f, 1130.0f) },
            Color = wallColor,
        };
        shakeRoot.AddChild(leftWall);
        var rightWall = new Polygon2D
        {
            Polygon = new[] { new Vector2(1220.0f, -50.0f), new Vector2(1490.0f, -50.0f), new Vector2(1670.0f, 1130.0f), new Vector2(1300.0f, 1130.0f) },
            Color = wallColor,
        };
        shakeRoot.AddChild(rightWall);
        var edgeColor = new Color(0.35f, 0.42f, 0.55f);
        shakeRoot.AddChild(Line(new[] { new Vector2(700.0f, -50.0f), new Vector2(620.0f, 1130.0f) }, edgeColor, 4.0f));
        shakeRoot.AddChild(Line(new[] { new Vector2(430.0f, -50.0f), new Vector2(250.0f, 1130.0f) }, new Color(edgeColor, 0.5f), 3.0f));
        shakeRoot.AddChild(Line(new[] { new Vector2(1220.0f, -50.0f), new Vector2(1300.0f, 1130.0f) }, edgeColor, 4.0f));
        shakeRoot.AddChild(Line(new[] { new Vector2(1490.0f, -50.0f), new Vector2(1670.0f, 1130.0f) }, new Color(edgeColor, 0.5f), 3.0f));
        // 壁面斜向结构线：加速度向画面下方流动（初始等距铺满）
        foreach (var side in new[] { 0, 1 })
        {
            for (var i = 0; i < 7; i++)
            {
                var strut = Line(new[] { Vector2.Zero, Vector2.One }, new Color(0.25f, 0.31f, 0.42f), 5.0f);
                var y = -320.0f + 210.0f * i;
                strut.Points = new[] { new Vector2(0.0f, y), new Vector2(1.0f, y) };
                shakeRoot.AddChild(strut);
                root.Struts.Add((strut, side));
            }
        }

        // 壁面防撞灯点流：红/青交替，随结构线同款透视向后流动（数组预建，_process 零分配）
        foreach (var side in new[] { 0, 1 })
        {
            for (var i = 0; i < 6; i++)
            {
                var lamp = new IntroGlowDot
                {
                    Radius = 5.0f,
                    DotColor = i % 2 == 0 ? new Color(1.0f, 0.35f, 0.2f, 0.85f) : new Color(0.0f, 0.83f, 1.0f, 0.85f),
                    Position = new Vector2(0.0f, -320.0f + 210.0f * i),
                    Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
                };
                shakeRoot.AddChild(lamp);
                root.WallLights.Add((lamp, side));
            }
        }

        // 机身暖色软轮廓光（托底背光，对齐镜头 6 剪影光比；在拖影/机身之下）
        var shipRim = CinematicFx.SoftGlow(120.0f, new Color(1.0f, 0.5f, 0.2f, 0.2f));
        shipRim.Position = new Vector2(960.0f, 600.0f);
        shakeRoot.AddChild(shipRim);
        // 战机双层拖影（动态模糊：橙色淡影拖在机尾方向）
        foreach (var k in new[] { 2, 1 })
        {
            var ghostShip = new Sprite2D
            {
                Texture = _playerShip,
                Scale = Vector2.One * 1.4f,
                Position = new Vector2(960.0f, 560.0f + 30.0f * k),
                Modulate = new Color(1.0f, 0.6f, 0.3f, 0.05f + 0.05f * (2 - k)),
            };
            shakeRoot.AddChild(ghostShip);
        }

        // 战机（尾部视角：机头朝远方/画面上方，发动机喷口朝向镜头；整体调暗对齐镜头 6 剪影调性）
        var ship = new Sprite2D
        {
            Texture = _playerShip,
            Scale = Vector2.One * 1.4f,
            Position = new Vector2(960.0f, 560.0f),
            Modulate = new Color(0.85f, 0.85f, 0.92f),
        };
        shakeRoot.AddChild(ship);
        // 尾部主火焰（橙红、短寿命、向下拖尾；软点贴图边缘衰减，尺寸加大一档补偿）；点火预热期由 amount_ratio 0→1 升起
        var engines = new List<GpuParticles2D>();
        foreach (var side in new[] { -46.0f, 46.0f })
        {
            var flame = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 40,
                ["lifetime"] = 0.35f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 18.0f,
                ["vel_min"] = 380.0f,
                ["vel_max"] = 560.0f,
                ["scale_min"] = 6.0f,
                ["scale_max"] = 12.0f,
                ["color"] = new Color(1.0f, 0.45f, 0.1f, 0.95f),
            });
            flame.Position = new Vector2(960.0f + side, 640.0f);
            shakeRoot.AddChild(flame);
            engines.Add(flame);
        }

        // 亮白内芯：叠在橙红外焰内，更短射程、更高温度感
        foreach (var side in new[] { -46.0f, 46.0f })
        {
            var coreFlame = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 24,
                ["lifetime"] = 0.22f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 10.0f,
                ["vel_min"] = 300.0f,
                ["vel_max"] = 420.0f,
                ["scale_min"] = 3.0f,
                ["scale_max"] = 6.0f,
                ["color"] = new Color(1.0f, 0.95f, 0.85f, 1.0f),
            });
            coreFlame.Position = new Vector2(960.0f + side, 636.0f);
            shakeRoot.AddChild(coreFlame);
            engines.Add(coreFlame);
        }

        // 机身两侧舔舐火焰舌（斜外下方向、更短寿命，包住机身两侧）
        foreach (var side in new[] { -1.0f, 1.0f })
        {
            var lick = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 32,
                ["lifetime"] = 0.28f,
                ["direction"] = new Vector3(0.35f * side, 1.0f, 0.0f),
                ["spread"] = 25.0f,
                ["vel_min"] = 200.0f,
                ["vel_max"] = 380.0f,
                ["scale_min"] = 4.5f,
                ["scale_max"] = 9.0f,
                ["color"] = new Color(1.0f, 0.5f, 0.12f, 0.8f),
            });
            lick.Position = new Vector2(960.0f + 62.0f * side, 590.0f);
            shakeRoot.AddChild(lick);
            engines.Add(lick);
        }

        // 点火预热：前 ~0.3s（时轴随镜头时长缩放）尾焰 amount_ratio 0→1 + 喷口软辉光从 0 弹起 + 白闪脉冲
        var preRoll = dur * 0.11f;
        var ignite = root.CreateTween().SetParallel(true);
        foreach (var e in engines)
        {
            e.AmountRatio = 0.0f;
            ignite.TweenProperty(e, "amount_ratio", 1.0f, preRoll).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }

        foreach (var side in new[] { -46.0f, 46.0f })
        {
            var nozzle = CinematicFx.SoftGlow(26.0f, new Color(1.0f, 0.55f, 0.15f, 0.75f));
            nozzle.Position = new Vector2(960.0f + side, 632.0f);
            var nozzleBase = nozzle.Scale;
            nozzle.Scale = Vector2.Zero;
            shakeRoot.AddChild(nozzle);
            ignite.TweenProperty(nozzle, "scale", nozzleBase, preRoll).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }

        var igniteFlash = BgRect(new Color(1.0f, 1.0f, 1.0f, 0.55f));
        shakeRoot.AddChild(igniteFlash);
        var igniteFlashT = root.CreateTween();
        igniteFlashT.TweenProperty(igniteFlash, "color:a", 0.0f, dur * 0.07f);
        // 轨道电火花：两侧壁轨各一发射器，火花顺轨向 +y 高速喷洒（textured 软点，≤32/侧）
        foreach (var side in new[] { 0, 1 })
        {
            var railSpark = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 28,
                ["lifetime"] = 0.35f,
                ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
                ["spread"] = 22.0f,
                ["vel_min"] = 500.0f,
                ["vel_max"] = 850.0f,
                ["damping_min"] = 100.0f,
                ["damping_max"] = 220.0f,
                ["scale_min"] = 2.5f,
                ["scale_max"] = 4.5f,
                ["color"] = new Color(1.0f, 0.75f, 0.35f, 0.9f),
            });
            railSpark.Position = new Vector2(side == 0 ? 645.0f : 1275.0f, 760.0f);
            shakeRoot.AddChild(railSpark);
        }

        // 全屏速度线
        for (var i = 0; i < 10; i++)
        {
            var sl = Line(new[] { Vector2.Zero, new Vector2(0.0f, 180.0f + (float)GD.Randf() * 160.0f) }, new Color(0.7f, 0.85f, 1.0f, 0.18f), 2.0f);
            sl.Position = new Vector2((float)GD.Randf() * 1920.0f, (float)GD.Randf() * 1300.0f - 200.0f);
            shakeRoot.AddChild(sl);
            root.SpeedLines.Add(sl);
        }

        // 左右边缘放射状速度线（斜向，集中在壁外边缘区，回卷保持同侧）
        for (var i = 0; i < 8; i++)
        {
            var sideSign = i % 2 == 0 ? -1.0f : 1.0f;
            var edgeSl = Line(
                new[] { Vector2.Zero, new Vector2((34.0f + (float)GD.Randf() * 40.0f) * sideSign, 220.0f + (float)GD.Randf() * 120.0f) },
                new Color(0.7f, 0.85f, 1.0f, 0.22f),
                2.5f);
            edgeSl.Position = new Vector2(
                sideSign < 0.0f ? (float)GD.Randf() * 230.0f : 1690.0f + (float)GD.Randf() * 230.0f,
                (float)GD.Randf() * 1300.0f - 260.0f);
            shakeRoot.AddChild(edgeSl);
            root.EdgeLines.Add((edgeSl, sideSign));
        }

        GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, AudioVolOffset, AudioPitch);
        // 引擎音持续：1.1s 后压低 6dB 补一发，覆盖镜头后段
        var engine = new Godot.Timer { OneShot = true, WaitTime = 1.1f, Autostart = true };
        root.AddChild(engine);  // 随镜头销毁：跳过/切镜后不残留迟发回调
        engine.Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(root))
            {
                GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -6.0f + AudioVolOffset, AudioPitch);
            }
        };
        return root;
    }

    // ---------------- 镜头 6：远景收束（3.0s） ----------------
    /// <summary>星云叠加圆 + 右上恒星背光；画面底部行星弧线 + 青蓝大气辉光带压底；
    /// 左下残骸剪影与余烬闪烁（外加缓慢翻滚的漂浮碎片）；
    /// 战机 = 深色剪影 + 引擎亮斑，向右上加速驶离；补给舰编队两列三行光点同向跟随；
    /// 末尾 0.7s 由导演淡黑衔接标题定格。</summary>
    private Node2D BuildShot6()
    {
        var dur = _shotDurations[5];
        var root = new Node2D { Name = "Shot6" };
        root.AddChild(new Starfield());  // M1 起 Starfield 为 C#，typed 实例化（原经脚本资源，M6 重定型）
        // 星云（大半径低透明度叠加圆，软径向光晕）
        var nebula1 = CinematicFx.SoftGlow(430.0f, new Color(0.35f, 0.15f, 0.5f, 0.13f));
        nebula1.Position = new Vector2(1380.0f, 320.0f);
        root.AddChild(nebula1);
        var nebula2 = CinematicFx.SoftGlow(340.0f, new Color(0.1f, 0.2f, 0.5f, 0.17f));
        nebula2.Position = new Vector2(420.0f, 780.0f);
        root.AddChild(nebula2);
        var nebula3 = CinematicFx.SoftGlow(260.0f, new Color(0.1f, 0.4f, 0.45f, 0.1f));
        nebula3.Position = new Vector2(1050.0f, 850.0f);
        root.AddChild(nebula3);
        // 星云缓慢异向漂移（远景视差呼吸感，往返循环）
        var nebulae = new List<Node2D> { nebula1, nebula2, nebula3 };
        var nebDirs = new[] { new Vector2(60.0f, 24.0f), new Vector2(-70.0f, 30.0f), new Vector2(50.0f, -36.0f) };
        for (var nI = 0; nI < 3; nI++)
        {
            var nebTween = root.CreateTween().SetLoops();
            nebTween.TweenProperty(nebulae[nI], "position", nebulae[nI].Position + nebDirs[nI], 7.0f + 2.0f * nI).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            nebTween.TweenProperty(nebulae[nI], "position", nebulae[nI].Position, 7.0f + 2.0f * nI).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        // 行星弧线（画面底部）：巨大半径圆弧（圆心远在屏下）+ 暗色星体填充 + 青蓝大气辉光带
        var limbPts = new Vector2[64];
        for (var pI = 0; pI < 64; pI++)
        {
            var la = -Mathf.Pi * 0.5f + ((float)pI / 63.0f - 0.5f) * 1.0f;
            limbPts[pI] = new Vector2(960.0f, 3350.0f) + new Vector2(Mathf.Cos(la), Mathf.Sin(la)) * 2480.0f;
        }

        var planetPts = new Vector2[limbPts.Length + 2];
        System.Array.Copy(limbPts, planetPts, limbPts.Length);
        planetPts[limbPts.Length] = new Vector2(2200.0f, 1200.0f);
        planetPts[limbPts.Length + 1] = new Vector2(-280.0f, 1200.0f);
        var planet = new Polygon2D { Polygon = planetPts, Color = new Color(0.02f, 0.04f, 0.09f) };
        root.AddChild(planet);
        root.AddChild(Line(limbPts, new Color(0.25f, 0.5f, 0.75f, 0.55f), 3.0f));
        var atmo = CinematicFx.SoftGlow(90.0f, new Color(0.3f, 0.65f, 0.9f, 0.22f));
        atmo.Position = new Vector2(960.0f, 880.0f);
        atmo.Scale *= new Vector2(16.0f, 0.9f);  // 横向拉扁成大气光带
        root.AddChild(atmo);
        // 恒星背光（右上强辉光，软径向光晕）
        var starHalo = CinematicFx.SoftGlow(230.0f, new Color(1.0f, 0.9f, 0.7f, 0.12f));
        starHalo.Position = new Vector2(1620.0f, 180.0f);
        root.AddChild(starHalo);
        var star = CinematicFx.SoftGlow(80.0f, new Color(1.0f, 0.95f, 0.85f, 0.75f));
        star.Position = new Vector2(1620.0f, 180.0f);
        root.AddChild(star);
        // 恒星横向 anamorphic 光晕：宽蓝白亮条 + 短竖条（叠加态），缓慢脉动
        var flareMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        var flareH = RectPoly(600.0f, 3.0f, new Color(0.6f, 0.8f, 1.0f, 0.35f));
        flareH.Position = star.Position;
        flareH.Material = flareMat;
        root.AddChild(flareH);
        var flareV = RectPoly(4.0f, 90.0f, new Color(0.6f, 0.8f, 1.0f, 0.2f));
        flareV.Position = star.Position;
        flareV.Material = flareMat;
        root.AddChild(flareV);
        var flarePulse = root.CreateTween().SetLoops();
        flarePulse.TweenProperty(flareH, "scale:x", 1.15f, 1.8).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        flarePulse.TweenProperty(flareH, "scale:x", 1.0f, 1.8).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        // 左下燃烧残骸剪影 + 橙色余烬闪烁
        var wreck = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(0.0f, 1080.0f),
                new Vector2(0.0f, 830.0f),
                new Vector2(150.0f, 790.0f),
                new Vector2(280.0f, 850.0f),
                new Vector2(420.0f, 810.0f),
                new Vector2(540.0f, 900.0f),
                new Vector2(560.0f, 1080.0f),
            },
            Color = new Color(0.03f, 0.03f, 0.05f),
        };
        root.AddChild(wreck);
        // 残骸顶缘余烬反光轮廓（否则在暗角下不可读）
        root.AddChild(Line(
            new[]
            {
                new Vector2(0.0f, 830.0f),
                new Vector2(150.0f, 790.0f),
                new Vector2(280.0f, 850.0f),
                new Vector2(420.0f, 810.0f),
                new Vector2(540.0f, 900.0f),
            },
            new Color(1.0f, 0.5f, 0.15f, 0.35f),
            3.0f));
        foreach (var pos in new[] { new Vector2(140.0f, 860.0f), new Vector2(300.0f, 900.0f), new Vector2(430.0f, 870.0f), new Vector2(220.0f, 950.0f) })
        {
            var ember = Glow(6.0f + (float)GD.Randf() * 4.0f, new Color(1.0f, 0.5f, 0.15f, 0.8f));
            ember.Position = pos;
            root.AddChild(ember);
            var et = root.CreateTween().SetLoops();
            et.TweenProperty(ember, "modulate:a", 0.15f, 0.4f + (float)GD.Randf() * 0.3f);
            et.TweenProperty(ember, "modulate:a", 1.0f, 0.4f + (float)GD.Randf() * 0.3f);
        }

        // 残骸上方缓慢翻滚的漂浮碎片（深色剪影 + 边缘微光）
        for (var k = 0; k < 3; k++)
        {
            var shard = new Polygon2D
            {
                Polygon = new[] { new Vector2(-14.0f, -6.0f), new Vector2(12.0f, -10.0f), new Vector2(16.0f, 8.0f), new Vector2(-8.0f, 10.0f) },
                Color = new Color(0.05f, 0.05f, 0.08f),
                Position = new Vector2(520.0f + 130.0f * k, 760.0f - 90.0f * k),
            };
            root.AddChild(shard);
            var shardEdge = Line(new[] { new Vector2(-14.0f, -6.0f), new Vector2(12.0f, -10.0f) }, new Color(1.0f, 0.6f, 0.25f, 0.55f), 2.5f);
            shard.AddChild(shardEdge);
            var st = root.CreateTween().SetParallel(true).SetLoops();
            st.TweenProperty(shard, "rotation", shard.Rotation + Mathf.Tau, 7.0f + 2.0f * k);
            st.TweenProperty(shard, "position", shard.Position + new Vector2(30.0f, -46.0f), 4.5f + k).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        // 战机剪影：深色机身 + 引擎亮斑，向右上加速驶离（ease_in）
        var ship = new Node2D
        {
            Position = new Vector2(760.0f, 640.0f),
            Rotation = new Vector2(720.0f, -360.0f).Angle(),  // 机头对准航向
            Scale = Vector2.One * 1.3f,
        };
        root.AddChild(ship);
        var fuselage = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(26.0f, 0.0f),
                new Vector2(-4.0f, -6.0f),
                new Vector2(-22.0f, -18.0f),
                new Vector2(-14.0f, -4.0f),
                new Vector2(-14.0f, 4.0f),
                new Vector2(-22.0f, 18.0f),
                new Vector2(-4.0f, 6.0f),
            },
            Color = new Color(0.1f, 0.13f, 0.2f),
        };
        ship.AddChild(fuselage);
        // 恒星背光边缘光：机身上缘（朝恒星一侧）暖色描边
        ship.AddChild(Line(
            new[]
            {
                new Vector2(26.0f, 0.0f),
                new Vector2(-4.0f, -6.0f),
                new Vector2(-22.0f, -18.0f),
            },
            new Color(1.0f, 0.85f, 0.6f, 0.6f),
            2.0f));
        var canopy = Glow(3.0f, new Color(0.4f, 0.7f, 1.0f, 0.6f));
        canopy.Position = new Vector2(8.0f, 0.0f);
        ship.AddChild(canopy);
        var engineGlow = Glow(9.0f, new Color(1.0f, 0.6f, 0.2f, 0.9f));
        engineGlow.Position = new Vector2(-16.0f, 0.0f);
        ship.AddChild(engineGlow);
        var engineFlicker = root.CreateTween().SetLoops();
        engineFlicker.TweenProperty(engineGlow, "scale", Vector2.One * 1.5f, 0.12);
        engineFlicker.TweenProperty(engineGlow, "scale", Vector2.One, 0.12);
        var shipTween = root.CreateTween();
        shipTween.TweenProperty(ship, "position", new Vector2(1480.0f, 280.0f), dur).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        // 补给舰编队：两列 × 三行光点阵列同向跟随（各带引擎拖尾短线，作为光点子节点随 tween 同行）
        for (var i = 0; i < 6; i++)
        {
            var dot = Glow(4.0f, new Color(0.6f, 0.8f, 1.0f, 0.8f));
            dot.Position = new Vector2(420.0f + 70.0f * (i % 2), 800.0f + 52.0f * (i / 2));
            dot.AddChild(Line(new[] { Vector2.Zero, new Vector2(-18.0f, 10.0f) }, new Color(0.5f, 0.75f, 1.0f, 0.45f), 2.0f));
            root.AddChild(dot);
            var dt = root.CreateTween();
            dt.TweenProperty(dot, "position", dot.Position + new Vector2(240.0f, -130.0f), dur);
        }

        return root;
    }

    /// <summary>GDScript 字符串 % 格式化语义（%s/%d/%f 占位 + %% 转义；tr() 文案补参用，
}

/// <summary>软圆点（原 GDScript intro_cinematic.gd 内嵌类 _GlowDot，迁移为同文件顶层类；
/// C# 源生成器不支持内嵌类，BaseConsoleScanlines 同款处理）。</summary>
public partial class IntroGlowDot : Node2D
{
    public float Radius = 8.0f;

    public Color DotColor = Colors.White;

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, DotColor);
    }
}

/// <summary>镜头 3：侧视走廊奔跑容器（原 GDScript intro_cinematic.gd 内嵌类 _RunnerShot，迁移为同文件顶层类）。
/// 两拍跑步循环：双腿反相、手臂与对侧腿反相、躯干 2 倍频起伏（就地写 rotation/position，零堆分配）。</summary>
public partial class IntroRunnerShot : Node2D
{
    public List<Polygon2D> Scrollers = new();  // 警示条纹/墙肋/舱门框，反向滚动表现冲刺
    public List<Node2D> HipPivots = new();  // 髋：大腿前后摆幅
    public List<Node2D> KneePivots = new();  // 膝：摆动相屈膝、支撑相伸展
    public List<Node2D> ShoulderPivots = new();  // 肩：与对侧腿反相摆动
    public List<Node2D> ElbowPivots = new();  // 肘：保持弯曲微振
    public Node2D BobNode = null!;  // 人物整体随步频上下起伏
    public float BobBaseY;
    public ColorRect Red = null!;  // 应急灯全屏红闪
    public List<Line2D> SpeedLines = new();
    public List<Polygon2D> FgStruts = new();  // 前景近景支杆（比中景更快的反向视差，-1600px/s 回卷）

    private float _t;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        foreach (var s in Scrollers)
        {
            s.Position += new Vector2(-900.0f * (float)delta, 0.0f);
            if (s.Position.X < -160.0f)
            {
                s.Position += new Vector2(2240.0f, 0.0f);
            }
        }

        // 前景支杆：近景快速横扫（-1600px/s，快于中景 -900），出界回卷
        foreach (var fs in FgStruts)
        {
            fs.Position += new Vector2(-1600.0f * (float)delta, 0.0f);
            if (fs.Position.X < -200.0f)
            {
                fs.Position += new Vector2(2520.0f, 0.0f);
            }
        }

        // 两拍跑步循环：双腿反相、手臂与对侧腿反相、躯干 2 倍频起伏（就地写 rotation/position，零堆分配）
        // 关节符号约定（人物朝 +x）：正 rotation = 肢尖向 -x（后），负 = 向 +x（前）
        var runPhase = _t * 11.0f;
        for (var i = 0; i < 2; i++)
        {
            var p = runPhase + Mathf.Pi * i;
            HipPivots[i].Rotation = Mathf.Sin(p) * 0.72f;
            // 膝只向后弯：摆动相（p≈1.6..4.7）脚跟踢向臀部，触地前基本伸直
            KneePivots[i].Rotation = 0.08f + Mathf.Max(0.0f, Mathf.Sin(p - 1.8f)) * 1.35f;
            ShoulderPivots[i].Rotation = 0.1f - Mathf.Sin(p) * 0.6f;
            // 肘只向前弯：前臂保持朝前（奔跑摆臂姿态）
            ElbowPivots[i].Rotation = -(1.0f + Mathf.Sin(p + 0.8f) * 0.25f);
        }

        // bob 最低点对齐支撑相中点（腿在重心正下方），腾空相最高
        BobNode.Position = new Vector2(BobNode.Position.X, BobBaseY + 2.6f * (0.5f + 0.5f * Mathf.Cos(runPhase * 2.0f)));
        var redColor = Red.Color;
        redColor.A = 0.08f + 0.08f * Mathf.Max(0.0f, Mathf.Sin(_t * Mathf.Tau * 6.0f));  // 6Hz 呼吸闪烁
        Red.Color = redColor;
        foreach (var sl in SpeedLines)
        {
            sl.Position += new Vector2(-2200.0f * (float)delta, 0.0f);
            if (sl.Position.X < -320.0f)
            {
                sl.Position = new Vector2(2200.0f + (float)GD.Randf() * 400.0f, (float)GD.Randf() * 1080.0f);
            }
        }
    }
}

/// <summary>镜头 4：操作台紧急启动容器（原 GDScript intro_cinematic.gd 内嵌类 _ConsoleShot，迁移为同文件顶层类）。
/// 双手点按 / 扣合 + 雷达扫掠/回波点亮（预计算角度，零堆分配）。</summary>
public partial class IntroConsoleShot : Node2D
{
    public List<Node2D> Hands = new();  // 双手剪影：倒计时前在按钮簇上快速点按
    public List<Vector2> Targets = new();
    public List<Polygon2D> Cells = new();  // 可点按按钮（闪烁重着色 + 双手目标池）
    public List<Vector2> Handles = new();
    public List<Polygon2D> OpenShapes = new();  // 张开手形（点按态）
    public List<Polygon2D> GripShapes = new();  // 扣合手形（抓把手态）
    public bool Grabbing;  // 倒计时结束：双手猛抓两侧把手
    public float[] Retarget = new float[] { 0.0f, 0.15f };
    public float[] Press = new float[] { 0.0f, 0.0f };  // 按下-抬起起伏剩余时长
    public Line2D RadarSweep = null!;  // 左副屏雷达扫掠针（rotation 随时间推进）
    public List<Sprite2D> RadarBlips = new();  // 雷达回波亮点（扫过点亮、余晖衰减）
    public float[] RadarBlipAngles = System.Array.Empty<float>();
    public float[] RadarBlipE = System.Array.Empty<float>();  // 回波余晖能量 0..1

    private float _radarAngle;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        for (var h = 0; h < Hands.Count; h++)
        {
            var hand = Hands[h];
            if (Grabbing)
            {
                hand.Position = hand.Position.Lerp(Handles[h], 9.0f * d);
            }
            else
            {
                Retarget[h] -= d;
                if (Retarget[h] <= 0.0f)
                {
                    Retarget[h] = (float)GD.RandRange(0.12, 0.28);
                    Targets[h] = Cells[(int)(GD.Randi() % (uint)Cells.Count)].Position;
                }

                // 到位后触发一次按下-抬起起伏（指尖压向台面再弹回）
                if (Press[h] <= 0.0f && hand.Position.DistanceSquaredTo(Targets[h]) < 100.0f)
                {
                    Press[h] = 0.16f;
                }

                var dip = 0.0f;
                if (Press[h] > 0.0f)
                {
                    Press[h] -= d;
                    dip = 5.0f * Mathf.Sin(Mathf.Pi * Mathf.Clamp(1.0f - Press[h] / 0.16f, 0.0f, 1.0f));
                }

                hand.Position = hand.Position.Lerp(Targets[h] + new Vector2(0.0f, dip), 16.0f * d);
            }
        }

        // 雷达：扫掠针匀速旋转；扫过回波角时点亮，余晖按 0.7/s 衰减（预计算角度，零堆分配）
        _radarAngle = Mathf.Wrap(_radarAngle + d * 3.6f, 0.0f, Mathf.Tau);
        RadarSweep.Rotation = _radarAngle;
        for (var b = 0; b < RadarBlips.Count; b++)
        {
            var bDiff = Mathf.Abs(Mathf.Wrap(_radarAngle - RadarBlipAngles[b] + Mathf.Pi, 0.0f, Mathf.Tau) - Mathf.Pi);
            if (bDiff < 0.2f)
            {
                RadarBlipE[b] = 1.0f;
            }
            else
            {
                RadarBlipE[b] = Mathf.Max(0.0f, RadarBlipE[b] - d * 0.7f);
            }

            var blipModulate = RadarBlips[b].Modulate;
            blipModulate.A = 0.12f + 0.88f * RadarBlipE[b];
            RadarBlips[b].Modulate = blipModulate;
        }
    }
}

/// <summary>镜头 5：弹射尾追视角容器（原 GDScript intro_cinematic.gd 内嵌类 _ChaseShot，迁移为同文件顶层类）。
/// 壁面/防撞灯/速度线按透视收缩向后流动（数组预建，_process 零分配）。</summary>
public partial class IntroChaseShot : Node2D
{
    public Node2D ShakeRoot = null!;
    public List<(Line2D Strut, int Side)> Struts = new();  // [Line2D, side(0=左壁 1=右壁)]：壁面斜向结构线，透视收缩向后流动
    public List<(IntroGlowDot Lamp, int Side)> WallLights = new();  // [_GlowDot, side]：壁面防撞灯，随结构线同款透视滚动
    public List<Line2D> SpeedLines = new();
    public List<(Line2D Line, float SideSign)> EdgeLines = new();  // [Line2D, side_sign]：边缘放射速度线，回卷保持同侧

    private float _railSpeed = 400.0f;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        ShakeRoot.Position = new Vector2((float)GD.RandRange(-6.0, 6.0), (float)GD.RandRange(-6.0, 6.0));
        _railSpeed += 2600.0f * d;
        foreach (var pair in Struts)
        {
            var strut = pair.Strut;
            var y = strut.Points[0].Y + _railSpeed * d;
            if (y > 1200.0f)
            {
                y -= 1560.0f;
            }

            // 透视：越远（靠上）壁面越窄，结构线随之收缩
            var ty = Mathf.Clamp((y + 50.0f) / 1180.0f, 0.0f, 1.2f);
            float innerX;
            float outerX;
            if (pair.Side == 0)
            {
                innerX = Mathf.Lerp(700.0f, 620.0f, ty);
                outerX = Mathf.Lerp(430.0f, 250.0f, ty);
            }
            else
            {
                innerX = Mathf.Lerp(1220.0f, 1300.0f, ty);
                outerX = Mathf.Lerp(1490.0f, 1670.0f, ty);
            }

            // C28：创建时已预分配 2 点，set_point_position 原地写（points[i]= 是值语义副本不生效）
            strut.SetPointPosition(0, new Vector2(innerX, y));
            strut.SetPointPosition(1, new Vector2(outerX, y + 70.0f));
        }

        foreach (var lampPair in WallLights)
        {
            var lamp = lampPair.Lamp;
            var ly = lamp.Position.Y + _railSpeed * d;
            if (ly > 1200.0f)
            {
                ly -= 1560.0f;
            }

            var lty = Mathf.Clamp((ly + 50.0f) / 1180.0f, 0.0f, 1.2f);
            // 灯点贴在壁面中线，随透视收缩
            var lampPos = lamp.Position;
            lampPos.X = lampPair.Side == 0 ? Mathf.Lerp(565.0f, 435.0f, lty) : Mathf.Lerp(1355.0f, 1485.0f, lty);
            lampPos.Y = ly;
            lamp.Position = lampPos;
        }

        foreach (var sl in SpeedLines)
        {
            sl.Position += new Vector2(0.0f, 2600.0f * d);
            if (sl.Position.Y > 1300.0f)
            {
                sl.Position = new Vector2((float)GD.Randf() * 1920.0f, -200.0f - (float)GD.Randf() * 300.0f);
            }
        }

        foreach (var edgePair in EdgeLines)
        {
            var el = edgePair.Line;
            el.Position += new Vector2(0.0f, 2600.0f * d);
            if (el.Position.Y > 1300.0f)
            {
                el.Position = new Vector2(
                    edgePair.SideSign < 0.0f ? (float)GD.Randf() * 230.0f : 1690.0f + (float)GD.Randf() * 230.0f,
                    -260.0f);
            }
        }
    }
}
