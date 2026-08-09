using System.Collections.Generic;
using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 返航过场导演：7 镜头时序串联、黑场转场、跳过与整树清理。
/// 设计文档（单一事实源）：docs/RETURN_HOME_CINEMATIC.md §2 分镜表。
/// 架构镜像 scripts/intro_cinematic.gd；无标题定格——镜头 7 渐暗停在全黑后直接走统一出口，
/// 让基地 UI 在黑场下淡入。严禁 await create_timer 协程（退出时协程状态泄漏）。
/// M6 全量迁移（2026-08-08 自 scripts/return_cinematic.gd）：CanvasLayer 子类；
/// UITheme/Starfield 为 C# typed 直调；CinematicFx/DawnStation 仍为 GDScript，
/// 原内嵌镜头类 _PortalShot/_CaptureShot/_WalkShot/_RoomShot 迁为同文件顶层类
/// （C# 源生成器不支持内嵌类，BaseConsole 先例）。
/// 注：原 signal finished 迁移为 [Signal] Finished——ClassDB 以 PascalCase 注册，
/// main.gd / return_cinematic_test.gd 的 finished.connect 需改连 Finished（主代理集中处理）。
/// </summary>
public partial class ReturnCinematic : CanvasLayer
{
    /// <summary>过场播完（skip 与自然结束同一出口发出；main.gd `_on_return_finished` 连接）。</summary>
    [Signal]
    public delegate void FinishedEventHandler();

    private const float Transition = 0.3f; // 镜头间黑场淡入淡出（含在各镜头时长内）
    private const float OutroFade = 0.9f; // 镜头 7 末尾渐暗到全黑（与闭眼重叠，BGM 同步淡出）

    // 战机贴图（static 禁止持有 Godot Resource——退出 segfault 实测；实例字段持有）
    private readonly Texture2D _playerShip = GD.Load<Texture2D>("res://assets/sprites/player_ship.png");

    /// <summary>输入宽限：开播前 SKIP_GRACE 秒内忽略跳过（防实战中 WASD/Shift/Space 持续按键瞬间误触；
    /// 任意键/点击/Esc 路由统一收敛在 skip() 内受控；effects.return_skip_grace 可调）</summary>
    public float SKIP_GRACE = 1.2f;

    /// <summary>每镜头时长（§2 分镜表；七镜头 11.8s = 总和，转场含在内）。测试可改短。</summary>
    private float[] _shotDurations = { 1.6f, 1.2f, 1.4f, 2.2f, 1.8f, 1.6f, 2.0f };

    /// <summary>由 main 注入（_bgm_player 异步创建，可能为 null）：镜头 7 渐暗期淡出到 -40dB</summary>
    public AudioStreamPlayer? BgmPlayer { get; set; }

    private int _shotIndex = -1;
    private Node2D? _currentShot;
    private Godot.Timer _shotTimer = null!;
    private bool _done;
    private ulong _startMsec; // 开播真实时刻（输入宽限计时基准）
    private float _driftT; // 导演级手持漂移相位（共享容器，单 _process 零堆分配）
    private bool _seamlessNext; // 差异化转场：2→3 画面连续（端口内推，不黑场）
    private Tween? _subTween; // 字幕淡入/淡出互斥
    private Tween? _bgmTween; // 镜头 7 BGM 淡出（skip 时 kill 并立即置目标音量）

    private Node2D _shotRoot = null!;
    private ColorRect _fade = null!;
    private Label _subtitle = null!;
    private Label _skipHint = null!;

    // CinematicFx/DawnStation 仍为 GDScript 静态工厂（class_name），经脚本资源动态访问

    // ---------------- A7：测试/诊断白盒断言经公开接口（过场镜头） ----------------

    public void SetShotDurations(Godot.Collections.Array durations)
    {
        var arr = new float[durations.Count];
        for (var i = 0; i < durations.Count; i++)
        {
            arr[i] = (float)durations[i].AsDouble();
        }

        _shotDurations = arr;
    }

    public int ShotIndex() => _shotIndex;

    public Node2D? CurrentShot() => _currentShot;

    public Node2D ShotRoot() => _shotRoot;

    public Label Subtitle() => _subtitle;

    public override void _Ready()
    {
        SKIP_GRACE = (float)GameState.Instance.Cfg("effects.return_skip_grace", SKIP_GRACE).AsDouble();
        _startMsec = Time.GetTicksMsec();
        _shotRoot = GetNode<Node2D>("ShotRoot");
        _fade = GetNode<ColorRect>("Fade");
        _subtitle = GetNode<Label>("Subtitle");
        _skipHint = GetNode<Label>("SkipHint");
        _skipHint.Text = (string)Tr("INTRO_SKIP"); // 跳过提示复用开场键
        _skipHint.AddThemeFontOverride("font", UITheme.Font);
        _subtitle.AddThemeFontOverride("font", UITheme.Font);
        _shotTimer = new Godot.Timer { OneShot = true };
        _shotTimer.Timeout += OnShotTimeout;
        AddChild(_shotTimer);
        // 首镜头延后到帧末启动：测试可在 add_child 同帧替换 _shot_durations
        Callable.From(Advance).CallDeferred();
    }

    /// <summary>任意键/鼠标点击跳过；Esc（ui_cancel）放行给 BackNavigator 路由到 Main.skip_return()（公开接口，A7 后经 _skip_return 落地）</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_done || @event.IsActionPressed("ui_cancel"))
        {
            return;
        }

        var pressedKey = @event is InputEventKey key && key.Pressed && !key.Echo;
        var pressedClick = @event is InputEventMouseButton button && button.Pressed;
        if (pressedKey || pressedClick)
        {
            GetViewport().SetInputAsHandled();
            Skip();
        }
    }

    /// <summary>导演级手持漂移：共享容器低频正弦位移/微旋转，零堆分配</summary>
    public override void _Process(double delta)
    {
        var d = (float)delta;
        _driftT += d;
        var pos = _shotRoot.Position;
        pos.X = Mathf.Sin(_driftT * 0.45f) * 3.0f;
        pos.Y = Mathf.Cos(_driftT * 0.38f) * 2.5f;
        _shotRoot.Position = pos;
        _shotRoot.Rotation = Mathf.Sin(_driftT * 0.3f) * 0.003f;
    }

    /// <summary>跳过（幂等）：与自然结束同一出口——停计时、kill 音频 tween 并置目标音量、
    /// 停在全黑画面、发 finished、整树 queue_free。
    /// 输入宽限期（开播前 SKIP_GRACE 秒）内直接忽略：任意键/点击与 Esc 路由都经此收敛。
    /// B15 修复：输入宽限只门控"输入跳过"，不门控"程序化自然结束"（_advance 走 _do_skip(true)，
    /// 否则未来压缩过场总时长 &lt;SKIP_GRACE 时自然结束会被永久拦截）。</summary>
    public void Skip() => DoSkip(false);

    private void DoSkip(bool bypassGrace)
    {
        if (_done)
        {
            return;
        }

        if (!bypassGrace && (float)(Time.GetTicksMsec() - _startMsec) / 1000.0f < SKIP_GRACE)
        {
            return; // 输入宽限期内忽略跳过
        }

        _done = true;
        _shotTimer.Stop();
        if (_bgmTween != null && _bgmTween.IsValid())
        {
            _bgmTween.Kill();
        }

        if (BgmPlayer != null)
        {
            BgmPlayer.VolumeDb = -40.0f;
        }

        var fadeColor = _fade.Color;
        fadeColor.A = 1.0f; // 停留在全黑再发 finished（基地 UI 在黑场下淡入）
        _fade.Color = fadeColor;
        EmitSignal(SignalName.Finished);
        QueueFree();
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
            DoSkip(true); // 自然结束：绕过输入宽限（B15），无标题定格，渐暗已停在全黑，直接走统一出口
            return;
        }

        _currentShot = BuildShot(_shotIndex);
        _shotRoot.AddChild(_currentShot);
        var dur = _shotDurations[_shotIndex];
        SetSubtitle(GdFormat.Format("RETURN_SUB_%d", _shotIndex + 1));
        if (_seamlessNext)
        {
            // 2→3 画面连续（端口内推）：不黑场
            _seamlessNext = false;
            var color = _fade.Color;
            color.A = 0.0f;
            _fade.Color = color;
        }
        else
        {
            // 黑场淡入（镜头 1 从全黑起，其余承接上一镜头的淡出）
            var color = _fade.Color;
            color.A = 1.0f;
            _fade.Color = color;
            var fadeTween = CreateTween();
            fadeTween.TweenProperty(_fade, "color:a", 0.0f, Mathf.Min(Transition, dur * 0.5f));
        }

        _shotTimer.Start(dur - FadeOutTime());
    }

    private float FadeOutTime()
    {
        var dur = _shotDurations[_shotIndex];
        if (_shotIndex == _shotDurations.Length - 1)
        {
            return Mathf.Min(OutroFade, dur * 0.5f); // 镜头 7 末尾渐暗
        }

        if (_shotIndex == 1)
        {
            return 0.0f; // 2→3 保持画面连续
        }

        return Mathf.Min(Transition, dur * 0.5f);
    }

    private void OnShotTimeout()
    {
        if (_done)
        {
            return;
        }

        // 字幕随转场淡出
        if (_subTween != null && _subTween.IsValid())
        {
            _subTween.Kill();
        }

        _subTween = CreateTween();
        var t = FadeOutTime();
        _subTween.TweenProperty(_subtitle, "modulate:a", 0.0f, t);
        if (_shotIndex == 1)
        {
            _seamlessNext = true;
            Advance();
            return;
        }

        var fadeTween = CreateTween();
        fadeTween.TweenProperty(_fade, "color:a", 1.0f, t);
        fadeTween.TweenCallback(Callable.From(Advance));
    }

    /// <summary>叙事字幕卡：设置文本并淡入（淡出由 _on_shot_timeout 随转场处理）</summary>
    private void SetSubtitle(string key)
    {
        if (_subTween != null && _subTween.IsValid())
        {
            _subTween.Kill();
        }

        _subtitle.Text = (string)Tr(key);
        var modulate = _subtitle.Modulate;
        modulate.A = 0.0f;
        _subtitle.Modulate = modulate;
        _subTween = CreateTween();
        _subTween.TweenProperty(_subtitle, "modulate:a", 1.0f, 0.3f);
    }

    private Node2D BuildShot(int i)
    {
        switch (i)
        {
            case 0:
                return BuildShot1();
            case 1:
                return BuildShot2();
            case 2:
                return BuildShot3();
            case 3:
                return BuildShot4();
            case 4:
                return BuildShot5();
            case 5:
                return BuildShot6();
            default:
                return BuildShot7();
        }
    }

    // ---------------- 构图辅助（与 intro_cinematic.gd 同款，项目惯例直接复制） ----------------

    /// <summary>叠加态辉光圆点（复用 C# 顶层类 GlowDot，原内嵌 _GlowDot 同构）。</summary>
    private static GlowDot Glow(float radius, Color color, bool additive = true)
    {
        var dot = new GlowDot { Radius = radius, DotColor = color };
        if (additive)
        {
            var mat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            dot.Material = mat;
        }

        return dot;
    }

    private static Polygon2D RectPoly(float w, float h, Color color)
    {
        var p = new Polygon2D
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
        return p;
    }

    private static ColorRect BgRect(Color color)
    {
        var r = new ColorRect
        {
            Color = color,
            Position = Vector2.Zero,
            Size = new Vector2(1920.0f, 1080.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        return r;
    }

    private static Line2D Line(Vector2[] points, Color color, float width = 2.0f)
    {
        var l = new Line2D
        {
            Points = points,
            DefaultColor = color,
            Width = width,
        };
        return l;
    }

    /// <summary>粒子工厂：全局委托 CinematicFx（同 dict 契约，默认挂软点贴图，scale 语义保持"像素直径"）</summary>
    private static GpuParticles2D Particles(Godot.Collections.Dictionary cfg)
    {
        return CinematicFx.Particles(cfg);
    }

    /// <summary>软径向光晕（CinematicFx.soft_glow，Sprite2D，可直接 tween）。</summary>
    private static Sprite2D SoftGlow(float radius, Color color, bool additive = true)
    {
        return CinematicFx.SoftGlow(radius, color, additive);
    }

    // ---------------- 人物构件（复用开场镜头 3 多段式飞行服人物，改姿态/相位） ----------------

    /// <summary>多段式飞行服驾驶员：骨盆/胸廓/头盔/维生背包/双关节四肢，面朝 +x。
    /// 返回 {node, hips[2], knees[2], shoulders[2], elbows[2], torso, eyelid}，
    /// 步行循环由镜头 _process 相位驱动，姿态关键帧直接写关节 rotation。</summary>
    private static Godot.Collections.Dictionary BuildPerson()
    {
        var bodyColor = new Color(0.24f, 0.3f, 0.4f); // 近侧肢体
        var farColor = new Color(0.14f, 0.18f, 0.26f); // 远侧肢体（深度层次）
        var edgeColor = new Color(0.55f, 0.66f, 0.84f, 0.7f); // 分件边缘线
        var person = new Node2D { Name = "Person" };
        var hips = new Godot.Collections.Array();
        var knees = new Godot.Collections.Array();
        var shoulders = new Godot.Collections.Array();
        var elbows = new Godot.Collections.Array();
        // 腿部（远侧先画）：髋→大腿→膝→小腿→飞行靴
        foreach (var sideI in new[] { 1, 0 })
        {
            var c = sideI == 0 ? bodyColor : farColor;
            var hip = new Node2D { Position = new Vector2(2.0f - 4.0f * sideI, -4.0f + 2.0f * sideI) };
            person.AddChild(hip);
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
            hips.Add(Variant.From(hip));
            knees.Add(Variant.From(knee));
        }

        // 躯干组（胸廓/背包/头盔/手臂随体倾斜）
        var torsoGrp = new Node2D();
        person.AddChild(torsoGrp);
        var pelvis = new Polygon2D
        {
            Polygon = new[] { new Vector2(-9.0f, -2.0f), new Vector2(7.0f, -4.0f), new Vector2(9.0f, -14.0f), new Vector2(-7.0f, -14.0f) },
            Color = bodyColor,
        };
        torsoGrp.AddChild(pelvis);
        // 生命维持背包 + 顶部管线 + 青色指示灯
        var backpack = RectPoly(12.0f, 24.0f, farColor);
        backpack.Position = new Vector2(-11.0f, -30.0f);
        torsoGrp.AddChild(backpack);
        torsoGrp.AddChild(Line(new[] { new Vector2(-11.0f, -44.0f), new Vector2(-11.0f, -50.0f), new Vector2(2.0f, -54.0f) }, edgeColor, 1.4f));
        var packLight = Glow(2.0f, new Color(0.0f, 0.83f, 1.0f, 0.8f));
        packLight.Position = new Vector2(-13.0f, -24.0f);
        torsoGrp.AddChild(packLight);
        // 胸廓 + 胸包 + 肩部护甲
        var chest = new Polygon2D
        {
            Polygon = new[] { new Vector2(-7.0f, -14.0f), new Vector2(13.0f, -16.0f), new Vector2(17.0f, -42.0f), new Vector2(-3.0f, -46.0f) },
            Color = bodyColor,
        };
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
        // 颈 + 头盔（面罩高光改冷青——返航无暖色）
        var neck = RectPoly(4.0f, 7.0f, bodyColor);
        neck.Position = new Vector2(8.0f, -51.0f);
        torsoGrp.AddChild(neck);
        var helmet = new GlowDot { Radius = 10.5f, DotColor = bodyColor };
        helmet.Position = new Vector2(11.0f, -62.0f);
        torsoGrp.AddChild(helmet);
        var visor = Glow(3.5f, new Color(0.5f, 0.9f, 1.0f, 0.8f));
        visor.Position = new Vector2(19.0f, -64.0f);
        torsoGrp.AddChild(visor);
        // 眼睑：面部特写闭合用（初始 scale.y=0 藏于头盔上缘，特写时 0.8s 放下盖住面罩）
        var eyelid = RectPoly(9.0f, 7.0f, new Color(0.1f, 0.13f, 0.2f));
        eyelid.Position = new Vector2(17.5f, -67.5f);
        eyelid.Scale = new Vector2(1.0f, 0.0f);
        torsoGrp.AddChild(eyelid);
        // 手臂（远侧先画）：肩→上臂→肘→前臂→手
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
            var hand = new GlowDot { Radius = 4.0f, DotColor = c };
            hand.Position = new Vector2(0.0f, 16.0f);
            elbow.AddChild(hand);
            shoulders.Add(Variant.From(shoulder));
            elbows.Add(Variant.From(elbow));
        }

        return new Godot.Collections.Dictionary
        {
            ["node"] = Variant.From(person),
            ["hips"] = Variant.From(hips),
            ["knees"] = Variant.From(knees),
            ["shoulders"] = Variant.From(shoulders),
            ["elbows"] = Variant.From(elbows),
            ["torso"] = Variant.From(torsoGrp),
            ["eyelid"] = Variant.From(eyelid),
        };
    }

    /// <summary>直立姿态（步行/站立基准）</summary>
    private static void PoseStand(Godot.Collections.Dictionary p)
    {
        var hips = p["hips"].AsGodotArray();
        var knees = p["knees"].AsGodotArray();
        var shoulders = p["shoulders"].AsGodotArray();
        var elbows = p["elbows"].AsGodotArray();
        for (var i = 0; i < 2; i++)
        {
            ((Node2D)hips[i].AsGodotObject()).Rotation = 0.0f;
            ((Node2D)knees[i].AsGodotObject()).Rotation = 0.05f;
            ((Node2D)shoulders[i].AsGodotObject()).Rotation = 0.1f;
            ((Node2D)elbows[i].AsGodotObject()).Rotation = -0.3f;
        }

        ((Node2D)p["torso"].AsGodotObject()).Rotation = 0.0f;
    }

    /// <summary>坐姿：大腿前抬、小腿下垂（坐在休眠床沿）</summary>
    private static void PoseSit(Godot.Collections.Dictionary p)
    {
        var hips = p["hips"].AsGodotArray();
        var knees = p["knees"].AsGodotArray();
        var shoulders = p["shoulders"].AsGodotArray();
        var elbows = p["elbows"].AsGodotArray();
        for (var i = 0; i < 2; i++)
        {
            ((Node2D)hips[i].AsGodotObject()).Rotation = -1.5f;
            ((Node2D)knees[i].AsGodotObject()).Rotation = 1.4f;
            ((Node2D)shoulders[i].AsGodotObject()).Rotation = 0.2f;
            ((Node2D)elbows[i].AsGodotObject()).Rotation = -0.8f;
        }

        ((Node2D)p["torso"].AsGodotObject()).Rotation = 0.1f;
    }

    /// <summary>平躺姿态：四肢舒展微放松（整体旋转由镜头另控）</summary>
    private static void PoseLie(Godot.Collections.Dictionary p)
    {
        var hips = p["hips"].AsGodotArray();
        var knees = p["knees"].AsGodotArray();
        var shoulders = p["shoulders"].AsGodotArray();
        var elbows = p["elbows"].AsGodotArray();
        for (var i = 0; i < 2; i++)
        {
            ((Node2D)hips[i].AsGodotObject()).Rotation = 0.1f;
            ((Node2D)knees[i].AsGodotObject()).Rotation = 0.12f;
            ((Node2D)shoulders[i].AsGodotObject()).Rotation = 0.15f;
            ((Node2D)elbows[i].AsGodotObject()).Rotation = -0.2f;
        }

        ((Node2D)p["torso"].AsGodotObject()).Rotation = 0.0f;
    }

    /// <summary>一次性触发 Timer（随镜头节点销毁，跳过/切镜不残留迟发回调）</summary>
    private static void Once(Node parent, float wait, Callable cb)
    {
        var t = new Godot.Timer
        {
            OneShot = true,
            WaitTime = Mathf.Max(wait, 0.05f),
            Autostart = true,
        };
        parent.AddChild(t);
        t.Connect(Godot.Timer.SignalName.Timeout, cb);
    }

    // ---------------- 镜头 1：曲率充能（1.6s） ----------------
    /// <summary>深空战机悬停画面中心偏下；喷口辉光暗红 (0.5,0.1,0.05) → 炽白 (1,0.95,0.85) 并放大 1→1.8；
    /// 机身周围能量粒子向内收束（负速度）；结尾 0.4s 同心细环微缩脉动（空间扭曲前兆）。</summary>
    private Node2D BuildShot1()
    {
        var dur = _shotDurations[0];
        var root = new Node2D { Name = "Shot1" };
        var starfield = new Starfield(); // M1 起 Starfield 为 C#，typed 实例化
        starfield.Warp(12.0f); // 承接对局 _starfield.Warp(18.0) 的星光拉伸（自身 lerp 衰减回 1）
        root.AddChild(starfield);
        var neb1 = Glow(480.0f, new Color(0.08f, 0.18f, 0.4f, 0.05f));
        neb1.Position = new Vector2(360.0f, 800.0f);
        root.AddChild(neb1);
        var neb2 = Glow(420.0f, new Color(0.1f, 0.3f, 0.45f, 0.05f));
        neb2.Position = new Vector2(1520.0f, 280.0f);
        root.AddChild(neb2);
        // 战机尾部视角悬停（同开场镜头 5 摆位）
        var ship = new Sprite2D
        {
            Texture = _playerShip,
            Scale = Vector2.One * 1.4f,
            Position = new Vector2(960.0f, 560.0f),
        };
        root.AddChild(ship);
        // 喷口充能：双层软辉光暗红 → 炽白放大
        foreach (var side in new[] { -46.0f, 46.0f })
        {
            var halo = SoftGlow(42.0f, new Color(0.9f, 0.4f, 0.15f, 0.2f));
            halo.Position = new Vector2(960.0f + side, 648.0f);
            root.AddChild(halo);
            var nozzle = SoftGlow(15.0f, new Color(1.0f, 1.0f, 1.0f, 0.9f));
            nozzle.Position = new Vector2(960.0f + side, 644.0f);
            nozzle.Modulate = new Color(0.5f, 0.1f, 0.05f);
            root.AddChild(nozzle);
            var nozzleBase = nozzle.Scale;
            var haloBase = halo.Scale;
            var charge = root.CreateTween().SetParallel(true);
            charge.TweenProperty(nozzle, "modulate", new Color(1.0f, 0.95f, 0.85f), dur * 0.8f);
            charge.TweenProperty(nozzle, "scale", nozzleBase * 1.8f, dur * 0.8f);
            charge.TweenProperty(halo, "scale", haloBase * 1.5f, dur * 0.8f);
        }

        // 能量粒子向内收束（负速度朝发射点汇聚）
        var inbound = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 48,
            ["lifetime"] = 1.2f,
            ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
            ["spread"] = 180.0f,
            ["vel_min"] = -70.0f,
            ["vel_max"] = -25.0f,
            ["scale_min"] = 1.5f,
            ["scale_max"] = 3.0f,
            ["color"] = new Color(0.6f, 0.85f, 1.0f, 0.5f),
        });
        inbound.Position = new Vector2(960.0f, 580.0f);
        root.AddChild(inbound);
        // 充能峰值（结尾 0.4s）：两道错位同心冲击环掠过观察者（曲率引擎点火感）
        Once(root, dur - 0.4f, Callable.From(() =>
        {
            var wave1 = CinematicFx.Shockwave(new Godot.Collections.Dictionary
            {
                ["radius"] = 700.0f,
                ["time"] = 0.5f,
                ["ry_ratio"] = 0.6f,
                ["color"] = new Color(0.0f, 0.83f, 1.0f, 0.35f),
                ["core_color"] = new Color(0.7f, 0.97f, 1.0f, 0.7f),
                ["width"] = 12.0f,
            });
            wave1.Position = new Vector2(960.0f, 570.0f);
            root.AddChild(wave1);
            Once(root, 0.14f, Callable.From(() =>
            {
                var wave2 = CinematicFx.Shockwave(new Godot.Collections.Dictionary
                {
                    ["radius"] = 900.0f,
                    ["time"] = 0.5f,
                    ["ry_ratio"] = 0.6f,
                    ["color"] = new Color(0.0f, 0.83f, 1.0f, 0.3f),
                    ["core_color"] = new Color(0.6f, 0.95f, 1.0f, 0.6f),
                    ["width"] = 10.0f,
                });
                wave2.Position = new Vector2(960.0f, 570.0f);
                root.AddChild(wave2);
            }));
        }));
        // 结尾 0.4s：画面中心同心细环微缩脉动（空间扭曲）
        for (var k = 0; k < 3; k++)
        {
            var ringPoints = new Vector2[40];
            for (var i = 0; i < 40; i++)
            {
                var a = Mathf.Tau * i / 40.0f;
                ringPoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (40.0f + 30.0f * k);
            }

            var ring = Line(ringPoints, new Color(0.6f, 0.9f, 1.0f, 0.0f), 2.0f);
            ring.Closed = true;
            ring.Position = new Vector2(960.0f, 480.0f);
            var ringMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            ring.Material = ringMat;
            root.AddChild(ring);
            var warpIn = root.CreateTween();
            warpIn.TweenInterval(dur - 0.4f + 0.08f * k);
            warpIn.TweenProperty(ring, "modulate:a", 0.55f, 0.15f);
            warpIn.Parallel().TweenProperty(ring, "scale", Vector2.One * 0.85f, 0.25f);
        }

        GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -6.0f, 0.6f); // 0.6 倍速拉长为充能上升感
        return root;
    }

    // ---------------- 镜头 2：传送端口撕裂（1.2s） ----------------

    /// <summary>竖直长圆环从一点撕开扩到全尺寸（0.5s），环缘 12 个 glow 点快速游走；
    /// 环内：口腔软辉光垫 + 双旋涡弧反向搅动 + 环缘粒子内流（环形发射域 + 负径向加速度）；
    /// 环内显现虚影站模糊景象（站体 α0.1 + 水平弥散抖动）；镜头稍推 1.0→1.06。</summary>
    private Node2D BuildShot2()
    {
        var dur = _shotDurations[1];
        var root = new ReturnCinematicPortalShot { Name = "Shot2" };
        root.AddChild(new Starfield()); // M1 起 Starfield 为 C#，typed 实例化
        var push = new Node2D(); // 推镜容器
        root.AddChild(push);
        var pushTween = root.CreateTween();
        pushTween.TweenProperty(push, "scale", Vector2.One * 1.06f, dur).SetTrans(Tween.TransitionType.Sine);
        var center = new Vector2(1150.0f, 430.0f);
        root._center = center;
        // 端口口腔软辉光垫（椭圆压扁，垫在虚影站之下，随撕裂同步张开）
        var mouth = SoftGlow(32.0f, new Color(0.0f, 0.7f, 1.0f, 0.15f));
        mouth.Position = center;
        var mouthScale = new Vector2(root._rx * 0.95f / 32.0f, root._ry * 0.95f / 32.0f);
        mouth.Scale = mouthScale * 0.02f;
        push.AddChild(mouth);
        // 内部景象先露（环张开时透出）：虚影站极简版（小比例 + 低 alpha + 弥散抖动）
        var inner = DawnStation.Build(1); // DawnStation.Mode.PHANTOM（0=DESTROYED, 1=PHANTOM）
        inner.Scale = Vector2.One * 0.28f;
        inner.Position = center;
        var innerMod = inner.Modulate;
        innerMod.A = 0.35f;
        inner.Modulate = innerMod;
        push.AddChild(inner);
        root._inner_station = inner;
        root._inner_base_x = center.X;
        // 端口环：亮芯 + 叠加态外晕，从一点撕开（scale 0.02 → 1，0.5s）
        var ringPoints = new Vector2[64];
        for (var i = 0; i < 64; i++)
        {
            var a = Mathf.Tau * i / 64.0f;
            ringPoints[i] = new Vector2(Mathf.Cos(a) * root._rx, Mathf.Sin(a) * root._ry);
        }

        var ringGlow = Line(ringPoints, new Color(0.0f, 0.83f, 1.0f, 0.25f), 12.0f);
        ringGlow.Closed = true;
        ringGlow.Position = center;
        var glowMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        ringGlow.Material = glowMat;
        push.AddChild(ringGlow);
        var ring = Line(ringPoints, new Color(0.0f, 0.9f, 1.0f, 0.9f), 4.0f);
        ring.Closed = true;
        ring.Position = center;
        push.AddChild(ring);
        foreach (var r in new[] { ringGlow, ring })
        {
            r.Scale = Vector2.One * 0.02f;
        }

        // 环内旋涡弧 ×2（各约 200° 弧段，反向旋转，随撕裂同步张开；_process 仅累加 rotation）
        for (var k = 0; k < 2; k++)
        {
            var rr = 0.55f + 0.25f * k;
            var swirlPoints = new Vector2[28];
            for (var i = 0; i < 28; i++)
            {
                var a = -Mathf.Tau * 100.0f / 360.0f + Mathf.Tau * 200.0f / 360.0f * i / 27.0f;
                swirlPoints[i] = new Vector2(Mathf.Cos(a) * root._rx * rr, Mathf.Sin(a) * root._ry * rr);
            }

            var swirl = Line(swirlPoints, new Color(0.3f, 0.9f, 1.0f, 0.3f), 3.0f);
            swirl.Position = center;
            swirl.Scale = Vector2.One * 0.02f;
            var swirlMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            swirl.Material = swirlMat;
            push.AddChild(swirl);
            root._swirls.Add(swirl);
            root._swirl_speeds.Add(k == 0 ? 2.4f : -1.7f);
        }

        var tear = root.CreateTween().SetParallel(true);
        tear.TweenProperty(ring, "scale", Vector2.One, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tear.TweenProperty(ringGlow, "scale", Vector2.One, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tear.TweenProperty(mouth, "scale", mouthScale, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        foreach (var swirl in root._swirls)
        {
            tear.TweenProperty(swirl, "scale", Vector2.One, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }

        // 环缘能量翻涌：12 个小 glow 点（本镜头唯一 _process 逐帧换位）
        for (var i = 0; i < 12; i++)
        {
            var dot = Glow(3.5f, new Color(0.5f, 0.95f, 1.0f, 0.9f));
            push.AddChild(dot);
            root._dots.Add(dot);
        }

        // 环缘内流粒子：环形发射域（参考 DawnStation 数据流配方）+ 负径向加速度拉向环心；
        // 节点 y 压扁成椭圆贴合端口；撕裂近完成（0.45s）才开始发射
        var inflow = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 40,
            ["lifetime"] = 0.9f,
            ["direction"] = new Vector3(0.0f, -1.0f, 0.0f),
            ["spread"] = 180.0f,
            ["vel_min"] = 0.0f,
            ["vel_max"] = 15.0f,
            ["scale_min"] = 2.0f,
            ["scale_max"] = 4.0f,
            ["color"] = new Color(0.5f, 0.9f, 1.0f, 0.45f),
        });
        var inflowMat = (ParticleProcessMaterial)inflow.ProcessMaterial;
        inflowMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring;
        inflowMat.EmissionRingAxis = new Vector3(0.0f, 0.0f, 1.0f);
        inflowMat.EmissionRingRadius = root._rx;
        inflowMat.EmissionRingInnerRadius = root._rx * 0.8f;
        inflowMat.EmissionRingHeight = 0.0f;
        inflowMat.RadialAccelMin = -140.0f;
        inflowMat.RadialAccelMax = -90.0f;
        inflow.Position = center;
        inflow.Scale = new Vector2(1.0f, root._ry / root._rx);
        inflow.Emitting = false;
        push.AddChild(inflow);
        Once(root, 0.45f, Callable.From(() => inflow.Emitting = true));
        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION, -12.0f, 0.5f); // 0.5 倍速低沉撕裂感
        return root;
    }

    // ---------------- 镜头 3：跃迁匹配剪辑（1.4s） ----------------
    /// <summary>前半原星域：战机加速冲入端口（scale 1→0.2 入环心），端口急缩成一点 → 白闪 0.10s
    /// → 后半虚影站星域：同一位置端口再度张开，战机减速飞出（scale 0.2→1，ease_out），
    /// 端口闭合消散。飞行方向两段保持一致（向右上）；远景先露远处虚影站剪影为镜头 4 铺垫。</summary>
    private Node2D BuildShot3()
    {
        var dur = _shotDurations[2];
        var u = dur / 2.0f; // 内部关键帧按基准时长等比缩放（测试短时长表兼容）
        var root = new Node2D { Name = "Shot3" };
        var portalPos = new Vector2(1180.0f, 400.0f);
        // ---- 前半：原星域冲入 ----
        var partA = new Node2D();
        root.AddChild(partA);
        partA.AddChild(new Starfield()); // M1 起 Starfield 为 C#，typed 实例化
        // 跃迁隧道放射条纹（以端口为中心，白闪切镜时随 part_a 隐藏）
        var streaks = CinematicFx.RadialStreaks(new Godot.Collections.Dictionary
        {
            ["count"] = 26,
            ["max_radius"] = 1000.0f,
            ["color"] = new Color(0.5f, 0.85f, 1.0f, 0.45f),
            ["cycle"] = 1.0f,
        });
        streaks.Position = portalPos;
        var streaksMod = streaks.Modulate;
        streaksMod.A = 0.0f;
        streaks.Modulate = streaksMod;
        partA.AddChild(streaks);
        var streaksIn = root.CreateTween();
        streaksIn.TweenProperty(streaks, "modulate:a", 1.0f, 0.3f * u);
        var ringAPoints = new Vector2[48];
        for (var i = 0; i < 48; i++)
        {
            var a = Mathf.Tau * i / 48.0f;
            ringAPoints[i] = new Vector2(Mathf.Cos(a) * 90.0f, Mathf.Sin(a) * 150.0f);
        }

        var ringA = Line(ringAPoints, new Color(0.0f, 0.9f, 1.0f, 0.9f), 4.0f);
        ringA.Closed = true;
        ringA.Position = portalPos;
        partA.AddChild(ringA);
        var shipA = new Sprite2D { Texture = _playerShip };
        shipA.Position = new Vector2(700.0f, 720.0f);
        shipA.Rotation = (portalPos - shipA.Position).Angle() + Mathf.Pi * 0.5f; // 贴图机头朝上，+PI/2 对准航向
        partA.AddChild(shipA);
        var flameA = Glow(18.0f, new Color(1.0f, 0.95f, 0.85f, 0.9f));
        flameA.Position = new Vector2(-30.0f, 0.0f); // 机尾（局部 -x）
        shipA.AddChild(flameA);
        var dive = root.CreateTween().SetParallel(true);
        dive.TweenProperty(shipA, "position", portalPos, 0.8f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        dive.TweenProperty(shipA, "scale", Vector2.One * 0.2f, 0.8f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        dive.TweenProperty(flameA, "scale", Vector2.One * 2.0f, 0.5f * u); // 尾焰骤亮
        // 端口急缩成一点（白闪前）
        var closeA = root.CreateTween();
        closeA.TweenInterval(0.82f * u);
        closeA.TweenProperty(ringA, "scale", Vector2.One * 0.02f, 0.08f * u);
        // ---- 后半：虚影站星域飞出（初始隐藏，白闪后揭示） ----
        var partB = new Node2D { Visible = false };
        root.AddChild(partB);
        partB.AddChild(new Starfield()); // M1 起 Starfield 为 C#，typed 实例化
        var neb = Glow(420.0f, new Color(0.08f, 0.2f, 0.45f, 0.06f));
        neb.Position = new Vector2(420.0f, 780.0f);
        partB.AddChild(neb);
        // 远处虚影站剪影（α0.15，为镜头 4 铺垫）
        var farStation = DawnStation.Build(1); // DawnStation.Mode.PHANTOM
        farStation.Scale = Vector2.One * 0.3f;
        farStation.Position = new Vector2(1560.0f, 300.0f);
        var farMod = farStation.Modulate;
        farMod.A = 0.5f;
        farStation.Modulate = farMod;
        partB.AddChild(farStation);
        var ringB = Line(ringAPoints, new Color(0.0f, 0.9f, 1.0f, 0.9f), 4.0f);
        ringB.Closed = true;
        ringB.Position = portalPos;
        ringB.Scale = Vector2.One * 0.02f;
        partB.AddChild(ringB);
        var shipB = new Sprite2D
        {
            Texture = _playerShip,
            Scale = Vector2.One * 0.2f,
            Position = portalPos,
        };
        shipB.Rotation = (new Vector2(1520.0f, 230.0f) - portalPos).Angle() + Mathf.Pi * 0.5f;
        partB.AddChild(shipB);
        // 减速拖短粒子尾迹（挂在机尾随 tween 同行）
        var trail = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 40,
            ["lifetime"] = 0.35f,
            ["direction"] = new Vector3(-1.0f, 0.4f, 0.0f),
            ["spread"] = 20.0f,
            ["vel_min"] = 120.0f,
            ["vel_max"] = 220.0f,
            ["scale_min"] = 2.0f,
            ["scale_max"] = 4.0f,
            ["color"] = new Color(0.6f, 0.9f, 1.0f, 0.7f),
        });
        trail.Position = new Vector2(-26.0f, 0.0f);
        shipB.AddChild(trail);
        // 白闪转场件（镜头内部，复用开场 1→2 差异化白闪）
        var flash = BgRect(new Color(1.0f, 1.0f, 1.0f, 0.0f));
        root.AddChild(flash);
        Once(root, 0.9f * u, Callable.From(() =>
        {
            partA.Visible = false;
            partB.Visible = true;
            // 白闪瞬间端口中心扩散一道冲击环（跃迁能量释放）
            var wave = CinematicFx.Shockwave(new Godot.Collections.Dictionary
            {
                ["radius"] = 520.0f,
                ["time"] = 0.4f,
                ["ry_ratio"] = 0.7f,
                ["color"] = new Color(0.6f, 0.95f, 1.0f, 0.5f),
                ["core_color"] = new Color(1.0f, 1.0f, 1.0f, 0.85f),
                ["width"] = 14.0f,
            });
            wave.Position = portalPos;
            root.AddChild(wave);
            var ft = root.CreateTween();
            ft.TweenProperty(flash, "color:a", 1.0f, 0.05f);
            ft.TweenProperty(flash, "color:a", 0.0f, 0.25f);
            GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH); // 白闪瞬间正常速
            var emerge = root.CreateTween().SetParallel(true);
            emerge.TweenProperty(ringB, "scale", Vector2.One, 0.2f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            emerge.TweenProperty(shipB, "scale", Vector2.One, 0.7f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            emerge.TweenProperty(shipB, "position", new Vector2(1520.0f, 230.0f), 0.7f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            var dissolve = root.CreateTween();
            dissolve.TweenInterval(0.7f * u);
            dissolve.TweenProperty(ringB, "modulate:a", 0.0f, 0.2f * u); // 端口闭合消散
        }));
        Once(root, 1.2f * u, Callable.From(() => GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -10.0f))); // 飞出段尾音
        return root;
    }

    // ---------------- 镜头 4：虚影站全貌 + 捕获轨道（2.2s） ----------------

    /// <summary>虚影站「曙光·残响」全貌首次完整亮相（§1.1 四层虚影全开）；
    /// 半透明能量捕获轨道（CinematicFx.beam 分层能量束：辉光层 + 亮芯层 + 3 个循环流光软点）牵引战机滑向停机坪入口；
    /// 站体环缘 8 盏航行灯慢速追逐明灭；镜头缓慢侧跟（正弦平移 60px + scale 1.0→1.12 缓推）。</summary>
    private Node2D BuildShot4()
    {
        var dur = _shotDurations[3];
        var root = new ReturnCinematicCaptureShot { Name = "Shot4" };
        root.AddChild(new Starfield()); // M1 起 Starfield 为 C#，typed 实例化
        var cam = new Node2D(); // 侧跟推镜容器
        root.AddChild(cam);
        var camTween = root.CreateTween().SetParallel(true);
        camTween.TweenProperty(cam, "scale", Vector2.One * 1.12f, dur).SetTrans(Tween.TransitionType.Sine);
        camTween.TweenProperty(cam, "position", new Vector2(-60.0f, 0.0f), dur).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        // 虚影站全貌（与开场镜头 1 同位同构）
        var station = DawnStation.Build(1); // DawnStation.Mode.PHANTOM
        station.Position = new Vector2(960.0f, 470.0f);
        cam.AddChild(station);
        // 站体环缘航行灯 ×8（慢速追逐明灭，预建后由 _CaptureShot 相位驱动 alpha）
        var ringRadius = DawnStation.RingRadius;
        for (var i = 0; i < 8; i++)
        {
            var a = Mathf.Tau * i / 8.0f;
            var lamp = SoftGlow(5.0f, new Color(0.5f, 0.95f, 1.0f, 1.0f));
            lamp.Position = new Vector2(960.0f, 470.0f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (ringRadius + 4.0f);
            var lampMod = lamp.Modulate;
            lampMod.A = 0.12f;
            lamp.Modulate = lampMod;
            cam.AddChild(lamp);
            root._lights.Add(lamp);
        }

        // 捕获轨道：站体边缘 → 战机的二次贝塞尔弧（构建期采样 24 点，_process 零分配）
        var p0 = new Vector2(960.0f, 470.0f) + new Vector2(Mathf.Cos(-0.2f), Mathf.Sin(-0.2f)) * ringRadius;
        var p2 = new Vector2(1530.0f, 660.0f);
        var p1 = new Vector2(1420.0f, 470.0f);
        var samples = new List<Vector2>(24);
        for (var i = 0; i < 24; i++)
        {
            var t = i / 23.0f;
            samples.Add(p0.Lerp(p1, t).Lerp(p1.Lerp(p2, t), t));
        }

        root._samples = samples.ToArray();
        // 分层能量束（辉光层 + 亮芯层 + 3 个循环流光软点，内部零分配 _process）
        var beam = CinematicFx.Beam(root._samples, new Godot.Collections.Dictionary
        {
            ["color"] = new Color(0.0f, 0.83f, 1.0f),
            ["width"] = 14.0f,
            ["dot_count"] = 3,
            ["dot_speed"] = 0.5f,
            ["dot_radius"] = 8.0f,
            ["dot_color"] = new Color(0.6f, 0.98f, 1.0f),
        });
        cam.AddChild(beam);
        // 战机沿轨道弧线缓速滑向停机坪入口（TRANS_SINE 吸附感）
        var ship = new Sprite2D
        {
            Texture = _playerShip,
            Scale = Vector2.One * 0.9f,
        };
        cam.AddChild(ship);
        root._ship = ship;
        // 机尾短距拖尾（镜头 3 飞出段同款配方，减速滑行弱一档）
        var shipTrail = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 24,
            ["lifetime"] = 0.35f,
            ["direction"] = new Vector3(-1.0f, 0.4f, 0.0f),
            ["spread"] = 20.0f,
            ["vel_min"] = 100.0f,
            ["vel_max"] = 180.0f,
            ["scale_min"] = 2.0f,
            ["scale_max"] = 3.5f,
            ["color"] = new Color(0.6f, 0.9f, 1.0f, 0.55f),
        });
        shipTrail.Position = new Vector2(-26.0f, 0.0f);
        ship.AddChild(shipTrail);
        var pull = root.CreateTween();
        pull.TweenProperty(root, "_ship_u", 0.06f, dur).SetTrans(Tween.TransitionType.Sine); // _ship_u 保持原名：tween 按 ClassDB 属性名驱动
        GameState.Instance.PlaySfx(GameState.Instance.SFX_RESUPPLY, -8.0f); // 对接感
        return root;
    }

    // ---------------- 镜头 5：停机坪降落（1.8s） ----------------
    /// <summary>战机沿引导灯带中线垂直下落 40px 降落（ease_out + 落地下压回弹 + 细尘）；引擎熄火
    /// （喷口辉光 0.3s 缩没）；座舱盖上翻 0.4s；主角跃下落地（弧线 0.5s + 落地微尘）。
    /// 固定机位低角度仰拍（机位元素整体下移 ~60px）。
    /// 舰人比例按现实战机/驾驶员锚定：舰可见长约 420px（scale 2.8）≈ 人物 64px（scale 0.55）的 6.6 倍。</summary>
    private Node2D BuildShot5()
    {
        var dur = _shotDurations[4];
        var u = dur / 1.8f;
        var root = new Node2D { Name = "Shot5" };
        root.AddChild(BgRect(new Color(0.01f, 0.02f, 0.05f)));
        // 虚影站内部线框剖面（半透明青色调，沿用 X 光线框语言）
        for (var i = 0; i < 3; i++)
        {
            var y = 380.0f + 140.0f * i;
            root.AddChild(Line(new[] { new Vector2(150.0f, y), new Vector2(1770.0f, y) }, new Color(0.0f, 0.6f, 1.0f, 0.08f)));
        }

        for (var i = 0; i < 7; i++)
        {
            var x = 150.0f + 270.0f * i;
            root.AddChild(Line(new[] { new Vector2(x, 340.0f), new Vector2(x, 860.0f) }, new Color(0.0f, 0.6f, 1.0f, 0.06f)));
        }

        // 六边形甲板平台：深色实体底 + 青色发光边界 + 中线引导灯带（低角度：整体下移 60px）
        var deck = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(-320.0f, 0.0f),
                new Vector2(-220.0f, -70.0f),
                new Vector2(220.0f, -70.0f),
                new Vector2(320.0f, 0.0f),
                new Vector2(220.0f, 70.0f),
                new Vector2(-220.0f, 70.0f),
            },
            Color = new Color(0.05f, 0.07f, 0.10f),
            Position = new Vector2(960.0f, 780.0f),
        };
        root.AddChild(deck);
        var deckEdge = Line(deck.Polygon, new Color(0.0f, 0.83f, 1.0f, 0.5f), 2.0f);
        deckEdge.Closed = true;
        var deckEdgeMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        deckEdge.Material = deckEdgeMat;
        deck.AddChild(deckEdge);
        // 中线引导灯带 ×8：初始压暗，降落窗口内由两侧向落点依次追逐点亮
        var guides = new List<GlowDot>();
        for (var i = 0; i < 8; i++)
        {
            var guide = Glow(3.0f, new Color(0.0f, 0.83f, 1.0f, 0.7f));
            guide.Position = new Vector2(-210.0f + 60.0f * i, 0.0f);
            var guideMod = guide.Modulate;
            guideMod.A = 0.2f;
            guide.Modulate = guideMod;
            deck.AddChild(guide);
            guides.Add(guide);
        }

        var chaseOrder = new[] { 0, 7, 1, 6, 2, 5, 3, 4 }; // 由远及近指向甲板中心落点
        for (var j = 0; j < 8; j++)
        {
            var g = guides[chaseOrder[j]];
            var gt = root.CreateTween();
            gt.TweenInterval(0.05f * u + 0.075f * u * j);
            gt.TweenProperty(g, "modulate:a", 1.0f, 0.08f * u);
            gt.TweenProperty(g, "modulate:a", 0.7f, 0.3f * u);
        }

        // 甲板尽头通道闸门
        var gate = RectPoly(120.0f, 160.0f, new Color(0.06f, 0.08f, 0.12f));
        gate.Position = new Vector2(1450.0f, 730.0f);
        root.AddChild(gate);
        var gateFrame = Line(
            new[]
            {
                new Vector2(1390.0f, 650.0f),
                new Vector2(1510.0f, 650.0f),
                new Vector2(1510.0f, 810.0f),
                new Vector2(1390.0f, 810.0f),
            },
            new Color(0.0f, 0.83f, 1.0f, 0.35f),
            2.0f
        );
        gateFrame.Closed = true;
        root.AddChild(gateFrame);
        // 战机（载体含机身/喷口/座舱盖）：scale 2.8，尾部落于甲板顶面（y=710）
        var ship = new Node2D { Position = new Vector2(960.0f, 460.0f) };
        root.AddChild(ship);
        var hull = new Sprite2D { Texture = _playerShip, Scale = Vector2.One * 2.8f };
        ship.AddChild(hull);
        var engine = Glow(20.0f, new Color(1.0f, 0.95f, 0.85f, 0.9f));
        engine.Position = new Vector2(0.0f, 195.0f);
        ship.AddChild(engine);
        var canopy = new Polygon2D
        {
            Polygon = new[] { new Vector2(-20.0f, 0.0f), new Vector2(20.0f, 0.0f), new Vector2(12.0f, -36.0f), new Vector2(-12.0f, -36.0f) },
            Color = new Color(0.15f, 0.35f, 0.5f, 0.85f),
            Position = new Vector2(0.0f, -80.0f),
        };
        ship.AddChild(canopy);
        // 主角（scale 0.55 ≈ 64px 高，初始藏于座舱）
        var person = BuildPerson();
        var pnode = (Node2D)person["node"].AsGodotObject();
        pnode.Scale = Vector2.One * 0.55f;
        pnode.Position = new Vector2(960.0f, 425.0f);
        pnode.Visible = false;
        PoseStand(person);
        root.AddChild(pnode);
        // 降落：垂直下落 40px（ease_out）→ 下压回弹 + 细尘 + 熄火 + 开舱 + 跃下
        var land = root.CreateTween();
        land.TweenProperty(ship, "position:y", 500.0f, 0.9f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        land.TweenProperty(ship, "scale:y", 0.94f, 0.1f * u);
        land.TweenProperty(ship, "scale:y", 1.0f, 0.12f * u);
        Once(root, 0.9f * u, Callable.From(() =>
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION, -18.0f); // 落地极轻闷响
            var dust = Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 24,
                ["lifetime"] = 0.6f,
                ["one_shot"] = true,
                ["explosiveness"] = 0.9f,
                ["direction"] = new Vector3(0.0f, -1.0f, 0.0f),
                ["spread"] = 140.0f,
                ["vel_min"] = 40.0f,
                ["vel_max"] = 110.0f,
                ["scale_min"] = 2.0f,
                ["scale_max"] = 5.0f,
                ["color"] = new Color(0.5f, 0.7f, 0.9f, 0.3f),
            });
            dust.Position = new Vector2(960.0f, 705.0f);
            root.AddChild(dust);
            var off = root.CreateTween();
            off.TweenProperty(engine, "scale", Vector2.Zero, 0.3f * u); // 引擎熄火
        }));
        Once(root, 1.0f * u, Callable.From(() =>
        {
            var open = root.CreateTween();
            open.TweenProperty(canopy, "rotation", -1.3f, 0.4f * u); // 座舱盖上翻
            // 玻璃高光条随开舱滑过舱面（与上翻同程 0.4u）
            var shine = new Polygon2D
            {
                Polygon = new[] { new Vector2(-2.0f, -4.0f), new Vector2(4.0f, -4.0f), new Vector2(-2.0f, -32.0f), new Vector2(-8.0f, -32.0f) },
                Color = new Color(1.0f, 1.0f, 1.0f, 0.0f),
                Position = new Vector2(-10.0f, 0.0f),
            };
            var shineMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            shine.Material = shineMat;
            canopy.AddChild(shine);
            var slide = root.CreateTween();
            slide.TweenProperty(shine, "position:x", 12.0f, 0.4f * u);
            var fade = root.CreateTween();
            fade.TweenProperty(shine, "color:a", 0.4f, 0.08f * u);
            fade.TweenProperty(shine, "color:a", 0.0f, 0.12f * u).SetDelay(0.26f * u); // 滑到末段同步消隐
        }));
        Once(root, 1.15f * u, Callable.From(() =>
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -14.0f); // 跃下短促音
            pnode.Visible = true;
            var jump = root.CreateTween();
            jump.TweenProperty(pnode, "position", new Vector2(935.0f, 390.0f), 0.25f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            jump.TweenProperty(pnode, "position", new Vector2(905.0f, 686.0f), 0.25f * u).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            jump.TweenCallback(Callable.From(() =>
            {
                // 落地微尘 + 屈膝缓冲后站直
                var landDust = Particles(new Godot.Collections.Dictionary
                {
                    ["amount"] = 16,
                    ["lifetime"] = 0.5f,
                    ["one_shot"] = true,
                    ["explosiveness"] = 0.9f,
                    ["direction"] = new Vector3(0.0f, -1.0f, 0.0f),
                    ["spread"] = 150.0f,
                    ["vel_min"] = 30.0f,
                    ["vel_max"] = 80.0f,
                    ["scale_min"] = 1.5f,
                    ["scale_max"] = 3.5f,
                    ["color"] = new Color(0.5f, 0.7f, 0.9f, 0.25f),
                });
                landDust.Position = new Vector2(905.0f, 706.0f);
                root.AddChild(landDust);
                var knees = person["knees"].AsGodotArray();
                for (var i = 0; i < 2; i++)
                {
                    ((Node2D)knees[i].AsGodotObject()).Rotation = 0.9f;
                }

                var stand = root.CreateTween();
                for (var i = 0; i < 2; i++)
                {
                    stand.Parallel().TweenProperty((Node2D)knees[i].AsGodotObject(), "rotation", 0.05f, 0.3f * u);
                }
            }));
        }));
        return root;
    }

    // ---------------- 镜头 6：通道步行 + 舱门（1.6s） ----------------

    /// <summary>侧视走廊：天花板/地面透视线 + 舱壁管线；顶部感应灯带 12 节点随主角行进亮起；
    /// 尽头休息室舱门双扇滑开 + 门缝泄光。主角步行 ~90px/s，镜头跟随（主角固定左 1/3）。</summary>
    private Node2D BuildShot6()
    {
        var dur = _shotDurations[5];
        var root = new ReturnCinematicWalkShot { Name = "Shot6" };
        root._stop_scroll = 140.0f * (dur / 2.4f); // 步行距离按镜头时长等比压缩（步速 90px/s 不变）
        root._time_u = dur / 2.4f;
        root.AddChild(BgRect(new Color(0.02f, 0.02f, 0.05f)));
        var world = new Node2D();
        root.AddChild(world);
        root._world = world;
        // 天花板/地面透视线 + 舱壁管线（远景透出虚影站结构微光）
        world.AddChild(Line(new[] { new Vector2(0.0f, 340.0f), new Vector2(2600.0f, 340.0f) }, new Color(0.16f, 0.22f, 0.32f, 0.7f), 3.0f));
        world.AddChild(Line(new[] { new Vector2(0.0f, 820.0f), new Vector2(2600.0f, 820.0f) }, new Color(0.16f, 0.22f, 0.32f, 0.7f), 3.0f));
        // 舱壁肋板 ×12：宽窄/深浅交替，破除等距重复感
        for (var i = 0; i < 12; i++)
        {
            var wide = i % 2 == 0;
            var rib = RectPoly(wide ? 22.0f : 14.0f, 480.0f, wide ? new Color(0.07f, 0.09f, 0.13f) : new Color(0.05f, 0.065f, 0.10f));
            rib.Position = new Vector2(120.0f + 220.0f * i, 580.0f);
            world.AddChild(rib);
        }

        // 肋板间舱壁小壁板（带顶部刻线，通道纵深细节）
        for (var i = 0; i < 6; i++)
        {
            var px = 230.0f + 440.0f * i;
            var panel = RectPoly(84.0f, 56.0f, new Color(0.06f, 0.08f, 0.12f));
            panel.Position = new Vector2(px, 560.0f);
            world.AddChild(panel);
            world.AddChild(Line(new[] { new Vector2(px - 42.0f, 531.0f), new Vector2(px + 42.0f, 531.0f) }, new Color(0.2f, 0.3f, 0.42f, 0.5f), 1.5f));
        }

        foreach (var pipe in new[] { new[] { 360.0f, 6.0f }, new[] { 382.0f, 4.0f } })
        {
            world.AddChild(Line(new[] { new Vector2(0.0f, pipe[0]), new Vector2(2600.0f, pipe[0]) }, new Color(0.2f, 0.26f, 0.36f), pipe[1]));
        }

        // 顶灯光锥 ×3（叠加态低 alpha，挂在世界容器随滚动视差）
        foreach (var cx in new[] { 500.0f, 1200.0f, 1900.0f })
        {
            var lampCone = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(cx - 26.0f, 356.0f),
                    new Vector2(cx + 26.0f, 356.0f),
                    new Vector2(cx + 150.0f, 820.0f),
                    new Vector2(cx - 150.0f, 820.0f),
                },
                Color = new Color(0.6f, 0.9f, 1.0f, 0.05f),
            };
            var coneMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            lampCone.Material = coneMat;
            world.AddChild(lampCone);
        }

        // 地面反光提示条（灯带光在地板上的长条泛光）
        var floorHint = Line(new[] { new Vector2(0.0f, 812.0f), new Vector2(2600.0f, 812.0f) }, new Color(0.5f, 0.8f, 1.0f, 0.10f), 2.5f);
        var floorHintMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        floorHint.Material = floorHintMat;
        world.AddChild(floorHint);
        // 远处结构微光（虚影站内部）
        var farGlow = Glow(300.0f, new Color(0.0f, 0.5f, 1.0f, 0.06f));
        farGlow.Position = new Vector2(2300.0f, 540.0f);
        world.AddChild(farGlow);
        // 顶部感应灯带：12 节点分段（初始暗，随主角 x 阈值点亮）
        for (var i = 0; i < 12; i++)
        {
            var lx = 200.0f + 200.0f * i;
            var seg = Line(new[] { new Vector2(lx - 80.0f, 352.0f), new Vector2(lx + 80.0f, 352.0f) }, new Color(0.6f, 0.95f, 1.0f, 0.08f), 5.0f);
            var segMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            seg.Material = segMat;
            world.AddChild(seg);
            root._lights.Add(seg);
            root._light_x.Add(lx);
        }

        // 尽头休息室舱门：门框 + 左右双扇门片 + 门缝泄光（门高 280px，明显高于 185px 人物）
        var doorX = 920.0f;
        var frame = Line(
            new[]
            {
                new Vector2(doorX - 76.0f, 540.0f),
                new Vector2(doorX + 76.0f, 540.0f),
                new Vector2(doorX + 76.0f, 820.0f),
                new Vector2(doorX - 76.0f, 820.0f),
            },
            new Color(0.0f, 0.83f, 1.0f, 0.4f),
            2.5f
        );
        frame.Closed = true;
        world.AddChild(frame);
        var leak = new ColorRect
        {
            Color = new Color(0.6f, 0.95f, 1.0f, 0.0f),
            Position = new Vector2(doorX - 66.0f, 546.0f),
            Size = new Vector2(132.0f, 268.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        world.AddChild(leak);
        root._door_leak = leak;
        var doorL = RectPoly(76.0f, 272.0f, new Color(0.10f, 0.13f, 0.18f));
        doorL.Position = new Vector2(doorX - 39.0f, 676.0f);
        world.AddChild(doorL);
        root._door_l = doorL;
        var doorR = RectPoly(76.0f, 272.0f, new Color(0.10f, 0.13f, 0.18f));
        doorR.Position = new Vector2(doorX + 39.0f, 676.0f);
        world.AddChild(doorR);
        root._door_r = doorR;
        // 主角步行（固定画面左 1/3，世界反向平移）
        var person = BuildPerson();
        var pnode = (Node2D)person["node"].AsGodotObject();
        pnode.Scale = Vector2.One * 1.6f;
        pnode.Position = new Vector2(640.0f, 746.0f);
        PoseStand(person);
        root.AddChild(pnode);
        root._person = person;
        root._bob_base_y = pnode.Position.Y;
        // 脚步声 ×6（0.4s 间隔，极轻短促）
        var stepCount = 0;
        var stepTimer = new Godot.Timer { WaitTime = 0.4f, Autostart = true };
        root.AddChild(stepTimer);
        stepTimer.Timeout += () =>
        {
            stepCount += 1;
            if (stepCount > 6 || !root._walking)
            {
                stepTimer.Stop();
                return;
            }

            GameState.Instance.PlaySfx(GameState.Instance.SFX_BUFF_PICK, -20.0f);
        };
        return root;
    }

    // ---------------- 镜头 7：休息室入睡（2.0s） ----------------

    /// <summary>休眠床 + 床头全息屏微光 + 顶部暖调小灯（全场景唯一暖光源）+ 观察窗外环体缓转 + 漂移星点；
    /// 主角走入 → 坐下 → 躺下（三姿态 0.6s 间隔）→ 镜头推近面部特写（scale→1.6）→
    /// 眼睑 0.8s 闭合；闭眼瞬间画面渐暗（导演 0.9s 淡黑）+ BGM 淡出到 -40dB。</summary>
    private Node2D BuildShot7()
    {
        var dur = _shotDurations[6];
        var u = dur / 3.0f;
        var root = new ReturnCinematicRoomShot { Name = "Shot7" };
        root._time_u = u;
        root.AddChild(BgRect(new Color(0.02f, 0.02f, 0.04f)));
        var room = new Node2D(); // 推近面部特写的运镜容器
        root.AddChild(room);
        // 舱室结构：地面线 + 舱壁
        room.AddChild(Line(new[] { new Vector2(300.0f, 840.0f), new Vector2(1620.0f, 840.0f) }, new Color(0.16f, 0.22f, 0.32f, 0.7f), 3.0f));
        room.AddChild(Line(new[] { new Vector2(300.0f, 300.0f), new Vector2(300.0f, 840.0f) }, new Color(0.10f, 0.14f, 0.22f, 0.5f)));
        room.AddChild(Line(new[] { new Vector2(1620.0f, 300.0f), new Vector2(1620.0f, 840.0f) }, new Color(0.10f, 0.14f, 0.22f, 0.5f)));
        // 观察窗：窗外虚影环体缓慢旋转轮廓（提醒此处仍在虚影站内）+ 远景星点漂移
        root._star_bounds = new Rect2(392.0f, 412.0f, 316.0f, 156.0f); // 窗框内缘留边
        for (var i = 0; i < 3; i++)
        {
            var star = SoftGlow(2.0f, new Color(0.85f, 0.92f, 1.0f, 0.7f));
            star.Position = new Vector2(
                (float)GD.RandRange(root._star_bounds.Position.X, root._star_bounds.End.X),
                (float)GD.RandRange(root._star_bounds.Position.Y, root._star_bounds.End.Y)
            );
            room.AddChild(star);
            root._stars.Add(star);
            root._star_vel.Add(new Vector2((float)GD.RandRange(-14.0f, -6.0f), (float)GD.RandRange(-3.0f, 3.0f)));
        }

        var windowFrame = Line(
            new[]
            {
                new Vector2(380.0f, 400.0f),
                new Vector2(720.0f, 400.0f),
                new Vector2(720.0f, 580.0f),
                new Vector2(380.0f, 580.0f),
            },
            new Color(0.0f, 0.83f, 1.0f, 0.35f),
            2.5f
        );
        windowFrame.Closed = true;
        room.AddChild(windowFrame);
        var ringOutside = new Node2D { Position = new Vector2(550.0f, 490.0f) };
        room.AddChild(ringOutside);
        var outPoints = new Vector2[48];
        for (var i = 0; i < 48; i++)
        {
            var a = Mathf.Tau * i / 48.0f;
            outPoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 150.0f;
        }

        var outRing = Line(outPoints, new Color(0.0f, 0.6f, 1.0f, 0.12f), 14.0f);
        outRing.Closed = true;
        ringOutside.AddChild(outRing);
        for (var i = 0; i < 4; i++)
        {
            var a = Mathf.Tau * i / 4.0f;
            ringOutside.AddChild(Line(
                new[]
                {
                    new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 40.0f,
                    new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 140.0f,
                },
                new Color(0.0f, 0.6f, 1.0f, 0.10f),
                4.0f
            ));
        }

        var spin = root.CreateTween().SetLoops();
        spin.TweenProperty(ringOutside, "rotation", Mathf.Tau, 20.0f).SetTrans(Tween.TransitionType.Linear);
        // 顶部暖调小灯（全场景唯一暖光源，「家」的视觉锚点）
        var lamp = RectPoly(30.0f, 10.0f, new Color(0.2f, 0.16f, 0.1f));
        lamp.Position = new Vector2(1100.0f, 320.0f);
        room.AddChild(lamp);
        var warm = Glow(130.0f, new Color(1.0f, 0.75f, 0.45f, 0.22f));
        warm.Position = new Vector2(1100.0f, 340.0f);
        room.AddChild(warm);
        var cone = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(1060.0f, 330.0f),
                new Vector2(1140.0f, 330.0f),
                new Vector2(1240.0f, 840.0f),
                new Vector2(960.0f, 840.0f),
            },
            Color = new Color(1.0f, 0.8f, 0.5f, 0.05f),
        };
        var coneMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        cone.Material = coneMat;
        room.AddChild(cone);
        // 休眠床：圆角平台（260 长，可躺下 185px 人物）+ 床头全息小屏微光
        var pod = RectPoly(260.0f, 26.0f, new Color(0.08f, 0.10f, 0.14f));
        pod.Position = new Vector2(1080.0f, 786.0f);
        room.AddChild(pod);
        room.AddChild(Line(new[] { new Vector2(950.0f, 773.0f), new Vector2(1210.0f, 773.0f) }, new Color(0.0f, 0.83f, 1.0f, 0.4f), 2.0f));
        var pillow = RectPoly(50.0f, 12.0f, new Color(0.12f, 0.15f, 0.2f));
        pillow.Position = new Vector2(980.0f, 766.0f);
        room.AddChild(pillow);
        var holo = RectPoly(44.0f, 32.0f, new Color(0.0f, 0.83f, 1.0f, 0.15f));
        holo.Position = new Vector2(946.0f, 700.0f);
        var holoMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        holo.Material = holoMat;
        room.AddChild(holo);
        var holoGlow = Glow(40.0f, new Color(0.0f, 0.83f, 1.0f, 0.1f));
        holoGlow.Position = new Vector2(946.0f, 700.0f);
        room.AddChild(holoGlow);
        // 主角：步行循环走入（_RoomShot 驱动肢体，位置仍由 tween 推进）→ 床沿坐下 → 平躺
        var person = BuildPerson();
        var pnode = (Node2D)person["node"].AsGodotObject();
        pnode.Scale = Vector2.One * 1.6f;
        pnode.Position = new Vector2(560.0f, 766.0f);
        PoseStand(person);
        room.AddChild(pnode);
        root._person = person;
        root._bob_base_y = pnode.Position.Y;
        var walkIn = root.CreateTween();
        walkIn.TweenProperty(pnode, "position:x", 990.0f, 0.6f * u).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        Once(root, 0.7f * u, Callable.From(() =>
        {
            // 坐下（床沿）：关节 0.3s 换姿态 + 重心下移
            var sit = root.CreateTween().SetParallel(true);
            var hips = person["hips"].AsGodotArray();
            var knees = person["knees"].AsGodotArray();
            var shoulders = person["shoulders"].AsGodotArray();
            var elbows = person["elbows"].AsGodotArray();
            for (var i = 0; i < 2; i++)
            {
                sit.TweenProperty((Node2D)hips[i].AsGodotObject(), "rotation", -1.5f, 0.3f * u);
                sit.TweenProperty((Node2D)knees[i].AsGodotObject(), "rotation", 1.4f, 0.3f * u);
                sit.TweenProperty((Node2D)shoulders[i].AsGodotObject(), "rotation", 0.2f, 0.3f * u);
                sit.TweenProperty((Node2D)elbows[i].AsGodotObject(), "rotation", -0.8f, 0.3f * u);
            }

            sit.TweenProperty(pnode, "position", new Vector2(1010.0f, 756.0f), 0.3f * u);
        }));
        Once(root, 1.3f * u, Callable.From(() =>
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_RESUPPLY, -16.0f); // 躺下轻柔音
            // 躺下：整体后倒 -90° 卧上休眠床（床面 y≈762）+ 四肢舒展微调
            var lie = root.CreateTween().SetParallel(true);
            lie.TweenProperty(pnode, "rotation", -Mathf.Pi * 0.5f, 0.4f * u).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            lie.TweenProperty(pnode, "position", new Vector2(1080.0f, 762.0f), 0.4f * u).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            var hips = person["hips"].AsGodotArray();
            var knees = person["knees"].AsGodotArray();
            var shoulders = person["shoulders"].AsGodotArray();
            var elbows = person["elbows"].AsGodotArray();
            for (var i = 0; i < 2; i++)
            {
                lie.TweenProperty((Node2D)hips[i].AsGodotObject(), "rotation", 0.1f, 0.5f * u);
                lie.TweenProperty((Node2D)knees[i].AsGodotObject(), "rotation", 0.12f, 0.5f * u);
                lie.TweenProperty((Node2D)shoulders[i].AsGodotObject(), "rotation", 0.15f, 0.5f * u);
                lie.TweenProperty((Node2D)elbows[i].AsGodotObject(), "rotation", -0.2f, 0.5f * u);
            }
        }));
        // 面部特写：镜头推近至头部（scale→1.6，聚焦平躺后的头盔位置 ≈(981,744)）
        // C12 修复：set_parallel 下前置 tween_interval 不延迟并行成员（特写提前完成）；
        // 改顺序 tween + 前置 interval，scale/position 两属性经 parallel() 同时推进
        var pushIn = root.CreateTween();
        pushIn.TweenInterval(1.5f * u);
        pushIn.SetParallel(true);
        pushIn.TweenProperty(room, "scale", Vector2.One * 1.6f, 1.0f * u).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        pushIn.TweenProperty(room, "position", new Vector2(960.0f, 540.0f) - new Vector2(981.0f, 744.0f) * 1.6f, 1.0f * u)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        // 眼睑缓缓闭合（与渐暗重叠：闭眼完成时画面约五成暗）
        var blink = root.CreateTween();
        blink.TweenInterval(1.5f * u);
        blink.TweenProperty((Node2D)person["eyelid"].AsGodotObject(), "scale:y", 1.0f, 0.8f * u);
        // 渐暗期 BGM 同步淡出到 -40dB（skip 时由 skip() kill 并立即置位）
        if (BgmPlayer != null)
        {
            _bgmTween = CreateTween();
            _bgmTween.TweenInterval(dur - FadeOutTime()); // 与画面渐暗同步起淡
            _bgmTween.TweenProperty(BgmPlayer, "volume_db", -40.0, FadeOutTime());
        }

        return root;
    }

    /// <summary>GDScript 字符串 % 格式化语义（%s/%d/%f 占位 + %% 转义；tr() 文案补参用，
}

/// <summary>镜头 2 传送端口（原 return_cinematic.gd 内嵌类 _PortalShot；C# 源生成器不支持
/// 内嵌类，迁为同文件顶层类——BaseConsole 先例）。字段名保持原 snake 名（shot 构建/tween 依赖）。</summary>
public partial class ReturnCinematicPortalShot : Node2D
{
    public readonly List<Node2D> _dots = new(); // 环缘能量翻涌：12 个小 glow 点沿椭圆游走
    public Vector2 _center = Vector2.Zero;
    public float _rx = 130.0f;
    public float _ry = 240.0f;
    public Node2D? _inner_station; // 环内虚影站模糊景象（水平弥散抖动）
    public float _inner_base_x;
    public readonly List<Line2D> _swirls = new(); // 环内旋涡弧 ×2（反向旋转，空间搅动感）
    public readonly List<float> _swirl_speeds = new();
    public float _t;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _t += d;
        for (var i = 0; i < _dots.Count; i++)
        {
            var a = _t * 2.5f + Mathf.Tau * i / _dots.Count;
            _dots[i].Position = _center + new Vector2(Mathf.Cos(a) * _rx, Mathf.Sin(a) * _ry);
        }

        for (var i = 0; i < _swirls.Count; i++)
        {
            _swirls[i].Rotation += _swirl_speeds[i] * d;
        }

        if (_inner_station != null)
        {
            var pos = _inner_station.Position;
            pos.X = _inner_base_x + Mathf.Sin(_t * 25.0f) * 2.0f;
            _inner_station.Position = pos;
        }
    }
}

/// <summary>镜头 4 捕获轨道（原 return_cinematic.gd 内嵌类 _CaptureShot；C# 源生成器不支持
/// 内嵌类，迁为同文件顶层类——BaseConsole 先例）。字段名保持原 snake 名（shot 构建/tween 依赖）。</summary>
public partial class ReturnCinematicCaptureShot : Node2D
{
    public Vector2[] _samples = System.Array.Empty<Vector2>(); // 捕获轨道弧采样（构建期预计算，供战机定位）
    public Sprite2D? _ship;
    public float _ship_u = 1.0f; // 战机沿轨位置参数（1=远端 → 0=站体端，tween 驱动；须保持原名：shot4 以 "tween_property(root, "_ship_u", …)" 按 ClassDB 属性名驱动）
    public readonly List<Sprite2D> _lights = new(); // 站体环缘航行灯 ×8（慢速追逐明灭）
    public float _t;

    public Vector2 SampleAt(float t)
    {
        var f = Mathf.Clamp(t, 0.0f, 1.0f) * (_samples.Length - 1);
        var i = (int)f;
        if (i >= _samples.Length - 1)
        {
            return _samples[_samples.Length - 1];
        }

        return _samples[i].Lerp(_samples[i + 1], f - i);
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _t += d;
        // 航行灯慢追逐：相位绕环依次点亮（alpha 窄脉冲 + 低常亮底）
        for (var i = 0; i < _lights.Count; i++)
        {
            var w = Mathf.Sin(_t * 1.5f - Mathf.Tau * i / 8.0f);
            var modulate = _lights[i].Modulate;
            modulate.A = 0.12f + 0.78f * Mathf.Pow(Mathf.Max(w, 0.0f), 4.0f);
            _lights[i].Modulate = modulate;
        }

        if (_ship != null)
        {
            _ship.Position = SampleAt(_ship_u);
            var ahead = SampleAt(Mathf.Max(_ship_u - 0.02f, 0.0f));
            // 贴图机头朝 +y 上（player.gd 同款：rotation = 航向角 + PI/2）
            _ship.Rotation = (ahead - _ship.Position).Angle() + Mathf.Pi * 0.5f;
        }
    }
}

/// <summary>镜头 6 通道步行（原 return_cinematic.gd 内嵌类 _WalkShot；C# 源生成器不支持
/// 内嵌类，迁为同文件顶层类——BaseConsole 先例）。字段名保持原 snake 名（shot 构建/tween 依赖）。</summary>
public partial class ReturnCinematicWalkShot : Node2D
{
    public Node2D _world = null!; // 走廊世界容器（跟随主角 x 匀速平移，主角固定在画面左 1/3）
    public Godot.Collections.Dictionary _person = null!;
    public readonly List<Line2D> _lights = new(); // 顶部感应灯带分段（随主角位置逐节点亮起，身后缓灭）
    public readonly List<float> _light_x = new();
    public Polygon2D _door_l = null!;
    public Polygon2D _door_r = null!;
    public ColorRect _door_leak = null!;
    public bool _door_opened;
    public bool _walking = true;
    public float _scrolled;
    public float _stop_scroll = 140.0f; // 走 ~1.6s（90px/s）抵达舱门前停步（构建时按镜头时长等比缩放）
    public float _time_u = 1.0f; // 内部关键帧时间缩放（舱门滑开时长随镜头时长压缩）
    public float _phase;
    public float _bob_base_y;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        var personNode = (Node2D)_person["node"].AsGodotObject();
        if (_walking)
        {
            _scrolled += 90.0f * d;
            var worldPos = _world.Position;
            worldPos.X = -_scrolled;
            _world.Position = worldPos;
            _phase += d * 6.0f; // 步行循环（奔跑构件降频、步幅减半）
            if (_scrolled >= _stop_scroll)
            {
                _walking = false;
                OpenDoor();
            }
        }

        var personWorldX = 640.0f + _scrolled;
        for (var i = 0; i < _lights.Count; i++)
        {
            var target = 0.08f;
            if (_light_x[i] < personWorldX - 40.0f)
            {
                target = 0.85f; // 脚下已过：亮起
            }

            if (_light_x[i] < personWorldX - 400.0f)
            {
                target = 0.15f; // 身后 0.5s 级缓灭
            }

            var color = _lights[i].DefaultColor;
            color.A = Mathf.Lerp(color.A, target, 5.0f * d);
            _lights[i].DefaultColor = color;
        }

        // 肢体相位驱动（零堆分配）；停步后缓回直立
        var k = _walking ? 1.0f : 0.0f;
        var hips = _person["hips"].AsGodotArray();
        var knees = _person["knees"].AsGodotArray();
        var shoulders = _person["shoulders"].AsGodotArray();
        var elbows = _person["elbows"].AsGodotArray();
        for (var i = 0; i < 2; i++)
        {
            var p = _phase + Mathf.Pi * i;
            var hip = (Node2D)hips[i].AsGodotObject();
            var knee = (Node2D)knees[i].AsGodotObject();
            var shoulder = (Node2D)shoulders[i].AsGodotObject();
            var elbow = (Node2D)elbows[i].AsGodotObject();
            hip.Rotation = Mathf.Lerp(hip.Rotation, Mathf.Sin(p) * 0.5f * k, 12.0f * d);
            knee.Rotation = Mathf.Lerp(knee.Rotation, 0.05f + Mathf.Max(0.0f, Mathf.Sin(p - 1.8f)) * 0.7f * k, 12.0f * d);
            shoulder.Rotation = Mathf.Lerp(shoulder.Rotation, 0.1f - Mathf.Sin(p) * 0.35f * k, 12.0f * d);
            elbow.Rotation = Mathf.Lerp(elbow.Rotation, -(0.3f + 0.4f * k + Mathf.Sin(p + 0.8f) * 0.15f * k), 12.0f * d);
        }

        var nodePos = personNode.Position;
        nodePos.Y = _bob_base_y + 1.5f * (0.5f + 0.5f * Mathf.Cos(_phase * 2.0f)) * k;
        personNode.Position = nodePos;
    }

    public void OpenDoor()
    {
        if (_door_opened)
        {
            return;
        }

        _door_opened = true;
        GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -10.0f, 0.7f); // 舱门滑开 0.7 倍速
        var tween = CreateTween().SetParallel(true);
        tween.TweenProperty(_door_l, "position:x", _door_l.Position.X - 85.0f, 0.5f * _time_u);
        tween.TweenProperty(_door_r, "position:x", _door_r.Position.X + 85.0f, 0.5f * _time_u);
        tween.TweenProperty(_door_leak, "color:a", 0.6f, 0.5f * _time_u); // 门缝光线泄出
    }
}

/// <summary>镜头 7 休息室（原 return_cinematic.gd 内嵌类 _RoomShot；C# 源生成器不支持
/// 内嵌类，迁为同文件顶层类——BaseConsole 先例）。字段名保持原 snake 名（shot 构建/tween 依赖）。</summary>
public partial class ReturnCinematicRoomShot : Node2D
{
    public Godot.Collections.Dictionary _person = null!; // 人物关节（走入步行循环 + 躺后呼吸共用）
    public float _time_u = 1.0f; // u = dur/3.0（镜头内部关键帧缩放）
    public float _phase;
    public float _t;
    public float _bob_base_y;
    public float _breathe_w; // 呼吸权重（躺下完成后缓入，避免突变）
    public readonly List<Sprite2D> _stars = new(); // 观察窗外漂移星点（窗框内回卷）
    public readonly List<Vector2> _star_vel = new();
    public Rect2 _star_bounds;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _t += d;
        var u = _time_u;
        if (_t < 0.6f * u)
        {
            // 走入步行循环（窗口两端权重淡入淡出，结束自动缓回直立；肢体公式同 _WalkShot）
            _phase += d * 16.0f;
            var k = Mathf.Clamp(Mathf.Min(_t, 0.6f * u - _t) / Mathf.Max(0.12f * u, 0.001f), 0.0f, 1.0f); // H20：镜头时长 0 除零防御
            var personNode = (Node2D)_person["node"].AsGodotObject();
            var hips = _person["hips"].AsGodotArray();
            var knees = _person["knees"].AsGodotArray();
            var shoulders = _person["shoulders"].AsGodotArray();
            var elbows = _person["elbows"].AsGodotArray();
            for (var i = 0; i < 2; i++)
            {
                var p = _phase + Mathf.Pi * i;
                var hip = (Node2D)hips[i].AsGodotObject();
                var knee = (Node2D)knees[i].AsGodotObject();
                var shoulder = (Node2D)shoulders[i].AsGodotObject();
                var elbow = (Node2D)elbows[i].AsGodotObject();
                hip.Rotation = Mathf.Lerp(hip.Rotation, Mathf.Sin(p) * 0.5f * k, 12.0f * d);
                knee.Rotation = Mathf.Lerp(knee.Rotation, 0.05f + Mathf.Max(0.0f, Mathf.Sin(p - 1.8f)) * 0.7f * k, 12.0f * d);
                shoulder.Rotation = Mathf.Lerp(shoulder.Rotation, 0.1f - Mathf.Sin(p) * 0.35f * k, 12.0f * d);
                elbow.Rotation = Mathf.Lerp(elbow.Rotation, -(0.3f + 0.4f * k + Mathf.Sin(p + 0.8f) * 0.15f * k), 12.0f * d);
            }

            var nodePos = personNode.Position;
            nodePos.Y = _bob_base_y + 1.5f * (0.5f + 0.5f * Mathf.Cos(_phase * 2.0f)) * k;
            personNode.Position = nodePos;
        }
        else if (_t > 1.7f * u)
        {
            // 躺下后的呼吸起伏：躯干微小缩放/位移正弦
            _breathe_w = Mathf.Lerp(_breathe_w, 1.0f, 1.5f * d);
            var torso = (Node2D)_person["torso"].AsGodotObject();
            var b = Mathf.Sin(_t * 1.5f) * _breathe_w;
            torso.Scale = new Vector2(1.0f, 1.0f + 0.025f * b);
            torso.Position = new Vector2(0.0f, 0.6f * b);
        }

        // 观察窗外星点缓慢漂移（越界回卷，始终留在窗框内）
        for (var i = 0; i < _stars.Count; i++)
        {
            var s = _stars[i];
            var pos = s.Position + _star_vel[i] * d;
            if (pos.X < _star_bounds.Position.X)
            {
                pos.X += _star_bounds.Size.X;
            }
            else if (pos.X > _star_bounds.End.X)
            {
                pos.X -= _star_bounds.Size.X;
            }

            if (pos.Y < _star_bounds.Position.Y)
            {
                pos.Y += _star_bounds.Size.Y;
            }
            else if (pos.Y > _star_bounds.End.Y)
            {
                pos.Y -= _star_bounds.Size.Y;
            }

            s.Position = pos;
        }
    }
}
