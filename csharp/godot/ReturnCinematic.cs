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

    // ---------------- 构图辅助（实现收敛至 CinematicFx 单源，本侧保留一行转发） ----------------

    private static GlowDot Glow(float radius, Color color, bool additive = true) => CinematicFx.Glow(radius, color, additive);

    private static Polygon2D RectPoly(float w, float h, Color color) => CinematicFx.RectPoly(w, h, color);

    private static ColorRect BgRect(Color color) => CinematicFx.BgRect(color);

    private static Line2D Line(Vector2[] points, Color color, float width = 2.0f) => CinematicFx.Line(points, color, width);

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
}
