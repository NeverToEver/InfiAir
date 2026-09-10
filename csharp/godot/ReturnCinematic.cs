using System.Collections.Generic;
using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 返航过场导演：7 镜头时序串联、黑场转场、跳过与整树清理。
/// 架构与 IntroCinematic 同构；无标题定格——镜头 7 渐暗停在全黑后直接走统一出口，
/// 让基地 UI 在黑场下淡入。严禁 await create_timer 协程（退出时协程状态泄漏）。
/// CanvasLayer 子类；UITheme/Starfield/CinematicFx/DawnStation 均为 C# typed 直调；
/// 各镜头类为独立顶层类（ReturnCinematicPortalShot.cs 等；C# 源生成器不支持内嵌类）。
/// </summary>
public partial class ReturnCinematic : CanvasLayer
{
    /// <summary>过场播完（skip 与自然结束同一出口发出；由 Main 连接）。</summary>
    [Signal]
    public delegate void FinishedEventHandler();

    private const float Transition = 0.3f; // 镜头间黑场淡入淡出（含在各镜头时长内）
    private const float OutroFade = 0.9f; // 镜头 7 末尾渐暗到全黑（与闭眼重叠，BGM 同步淡出）

    // 战机贴图（static 禁止持有 Godot Resource——退出 segfault 实测；实例字段持有）
    private readonly Texture2D _playerShip = GD.Load<Texture2D>("res://assets/sprites/player_ship.png");

    /// <summary>输入宽限：开播前 SKIP_GRACE 秒内忽略跳过（防实战中 WASD/Shift/Space 持续按键瞬间误触；
    /// 任意键/点击/Esc 路由统一收敛在 skip() 内受控；effects.return_skip_grace 可调）</summary>
    public float SKIP_GRACE = 1.2f;

    /// <summary>每镜头时长（§2 分镜表；七镜头 11.8s = 总和，转场含在内）。</summary>
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
        // 首镜头延后到帧末启动（_Ready 同帧不抢首帧，帧末开播）
        Callable.From(Advance).CallDeferred();
    }

    /// <summary>任意键/鼠标点击跳过；Esc（ui_cancel）放行给 BackNavigator 路由到 Main.SkipReturn()</summary>
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
    /// 输入宽限只门控"输入跳过"，不门控"程序化自然结束"（_advance 走 _do_skip(true)，
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
            DoSkip(true); // 自然结束：绕过输入宽限，无标题定格，渐暗已停在全黑，直接走统一出口
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

    // ---------------- 人物构件 ----------------

    /// <summary>多段式飞行服驾驶员（视觉细节收敛至共享构件工厂 CrewFigure；关节契约逐位不变）。
    /// 返回 {node, hips[2], knees[2], shoulders[2], elbows[2], torso, eyelid}，
    /// 步行循环由镜头 _process 相位驱动，姿态关键帧直接写关节 rotation。</summary>
    private static Godot.Collections.Dictionary BuildPerson()
    {
        // 返航段整体冷调，乘员状态灯走冷青（与开场琥珀区分）
        return CrewFigure.Build(new Color(0.35f, 0.78f, 0.95f));
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


    /// <summary>一次性触发 Timer（随镜头节点销毁，跳过/切镜不残留迟发回调）</summary>
    private static void Once(Node parent, float wait, Callable cb)
    {
        var t = new Godot.Timer
        {
            OneShot = true,
            WaitTime = Mathf.Max(wait, CfgFx.IntervalFloor),
            Autostart = true,
        };
        parent.AddChild(t);
        t.Connect(Godot.Timer.SignalName.Timeout, cb);
    }
}
