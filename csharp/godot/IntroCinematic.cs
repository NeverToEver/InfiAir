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
    // 差异化转场柔化：原纯白 alpha 1.0（0.10s 冲顶）闪感刺眼，改为暖白低峰值「过曝呼吸」——
    // 峰值降到 0.38、升/放斜率放缓，配合局部亮部特效保留「被光吞没」的叙事感而不刺目
    private static readonly Color FlashTint = new(1.0f, 0.93f, 0.82f);  // 暖白（爆燃/点火色温）
    private const float FlashPeak = 0.38f;
    private const float FlashRise = 0.24f;
    private const float FlashRelease = 0.55f;
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
    private float _subtitleBaseY;  // 字幕停靠 y：入场从 +8px 上浮，退场只动 alpha

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
        _subtitleBaseY = _subtitle.Position.Y;

        _skipHint.Text = (string)Tr("INTRO_SKIP");
        _skipHint.AddThemeFontOverride("font", UITheme.Font);
        _subtitle.AddThemeFontOverride("font", UITheme.Font);
        GetNode<Label>("TitleCard/Center/VBox/Title").AddThemeFontOverride("font", UITheme.Font);
        // 字幕/标题可读性与质感：软阴影把字从亮部画面里托出；标题加同色微辉光
        _subtitle.AddThemeColorOverride("font_shadow_color", new Color(0.0f, 0.0f, 0.0f, 0.7f));
        _subtitle.AddThemeConstantOverride("shadow_offset_x", 0);
        _subtitle.AddThemeConstantOverride("shadow_offset_y", 2);
        _subtitle.AddThemeConstantOverride("shadow_outline_size", 4);
        var title = GetNode<Label>("TitleCard/Center/VBox/Title");
        title.AddThemeColorOverride("font_shadow_color", new Color(UITheme.Accent, 0.35f));
        title.AddThemeConstantOverride("shadow_offset_x", 0);
        title.AddThemeConstantOverride("shadow_offset_y", 0);
        title.AddThemeConstantOverride("shadow_outline_size", 8);
        PolishTitleCard(title);
        PolishLetterbox();
        PolishGrain();
        // 跳过提示延迟 1.2s 淡入（开局不再与镜头 1 抢注意力）
        _skipHint.Modulate = new Color(_skipHint.Modulate, 0.0f);
        var hintTween = CreateTween();
        hintTween.TweenInterval(1.2);
        hintTween.TweenProperty(_skipHint, "modulate:a", 0.85f, 0.6);
        _shotTimer = new Godot.Timer { OneShot = true };
        _shotTimer.Timeout += OnShotTimeout;
        AddChild(_shotTimer);
        // 首镜头延后到帧末启动：测试可在 add_child 同帧替换 _shot_durations
        Play();
    }

    /// <summary>标题定格精修：字距拉开 + 标题背后呼吸辉光垫；缩放沉降与 accent 线扫入在 PlayTitleCard 触发。</summary>
    private void PolishTitleCard(Label title)
    {
        // 标题字距：FontVariation 只在此处实例化（一次性），glyph 间距把小字重标题撑出电影片头版式
        title.AddThemeFontOverride(
            "font",
            new FontVariation { BaseFont = UITheme.Font, SpacingGlyph = 14 });
        // 文字后方低频呼吸辉光：给纯黑底一点纵深（挂 Center 之前 = 垫在文字下；低峰值只做氛围不抢字）
        var pad = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pad.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var glow = CinematicFx.SoftGlow(240.0f, new Color(UITheme.Accent, 0.06f));
        glow.Position = new Vector2(960.0f, 540.0f);
        glow.Scale *= new Vector2(1.6f, 0.9f);  // 横向拉扁成片头光带，避免圆形光球感
        pad.AddChild(glow);
        var glowTween = glow.CreateTween().SetLoops();
        glowTween.TweenProperty(glow, "modulate:a", 0.55f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        glowTween.TweenProperty(glow, "modulate:a", 1.0f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _titleCard.AddChild(pad);
        _titleCard.MoveChild(pad, 0);
    }

    /// <summary>遮幅条开场展开：从更厚的收拢态缓释到工作位，模拟「画幅打开」的入场仪式感。</summary>
    private void PolishLetterbox()
    {
        var top = GetNode<Control>("LetterboxTop");
        var bottom = GetNode<Control>("LetterboxBottom");
        top.OffsetBottom = 202.0f;
        bottom.OffsetTop = 878.0f;
        var tween = CreateTween().SetParallel(true);
        tween.TweenProperty(top, "offset_bottom", 132.0f, 1.5).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(bottom, "offset_top", 948.0f, 1.5).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>胶片颗粒：程序化 value-noise 片元着色器（additive 低强度，只给暗部铺一层动态细颗粒质感）。</summary>
    private void PolishGrain()
    {
        const string Code = @"
            shader_type canvas_item;
            render_mode blend_add;
            uniform float intensity : hint_range(0.0, 0.2) = 0.035;
            float grain_hash(vec2 p) {
                p = fract(p * vec2(443.897, 441.423));
                p += dot(p, p + 19.19);
                return fract((p.x + p.y) * p.x);
            }
            void fragment() {
                vec2 cell = UV * vec2(480.0, 270.0);
                float g = grain_hash(cell + vec2(fract(TIME * 7.13) * 91.0, fract(TIME * 3.71) * 57.0));
                COLOR = vec4(vec3(g), intensity);
            }";
        var grain = GetNode<ColorRect>("Grain");
        grain.Material = new ShaderMaterial { Shader = new Shader { Code = Code } };
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

    /// <summary>导演级手持漂移：共享容器低频正弦位移/微旋转，零堆分配。
    /// Position 同时补偿镜头切入缩放（scale 绕原点，把画面中心拉回 (960,540)）。</summary>
    public override void _Process(double delta)
    {
        _driftT += (float)delta;
        var zoom = _shotRoot.Scale.X;
        var drift = new Vector2(
            Mathf.Sin(_driftT * 0.45f) * 3.0f,
            Mathf.Cos(_driftT * 0.38f) * 2.5f);
        _shotRoot.Position = drift + new Vector2(960.0f, 540.0f) * (1.0f - zoom);
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
        // 镜头切入沉降：从轻微放大缓释回 1.0（白闪承接的镜头起幅更大，模拟冲击余波）；Position 补偿在 _Process
        var entryScale = 1.02f;
        if (_whiteTransition)
        {
            entryScale = 1.05f;
            // 白闪承接：黑层保持透明，暖白纱从当前峰值缓释（不再从全白硬切）
            _whiteTransition = false;
            _fade.Color = new Color(_fade.Color, 0.0f);
            var flashTween = CreateTween();
            flashTween.TweenProperty(_flash, "color:a", 0.0f, FlashRelease);
        }
        else
        {
            // 黑场淡入（镜头 1 从全黑起，其余承接上一镜头的淡出）
            _fade.Color = new Color(_fade.Color, 1.0f);
            var fadeTween = CreateTween();
            fadeTween.TweenProperty(_fade, "color:a", 0.0f, Mathf.Min(Transition, dur * 0.5f));
        }

        _shotRoot.Scale = Vector2.One * entryScale;
        var settleTween = CreateTween();
        settleTween.TweenProperty(_shotRoot, "scale", Vector2.One, 0.9).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
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
            _flash.Color = FlashTint;
            var flashTween = CreateTween();
            flashTween.TweenProperty(_flash, "color:a", FlashPeak, FlashRise);
            flashTween.TweenCallback(Callable.From(Advance));
        }
        else
        {
            var fadeTween = CreateTween();
            fadeTween.TweenProperty(_fade, "color:a", 1.0f, FadeOutTime());
            fadeTween.TweenCallback(Callable.From(Advance));
        }
    }

    /// <summary>收尾标题定格：淡入 → 停留 → 淡出 → skip() 统一出口。
    /// 入场带轻微缩放沉降 + accent 线从零展宽（片头版式仪式感）。</summary>
    private void PlayTitleCard()
    {
        if (_subTween != null && _subTween.IsValid())
        {
            _subTween.Kill();
        }

        _subtitle.Modulate = new Color(_subtitle.Modulate, 0.0f);
        var center = GetNode<CenterContainer>("TitleCard/Center");
        center.PivotOffset = new Vector2(960.0f, 540.0f);
        center.Scale = Vector2.One * 1.06f;
        var accentLine = GetNode<ColorRect>("TitleCard/Center/VBox/AccentLine");
        accentLine.CustomMinimumSize = new Vector2(0.0f, 3.0f);
        var tween = CreateTween().SetParallel(true);
        tween.TweenProperty(_titleCard, "modulate:a", 1.0f, TitleCardIn);
        tween.TweenProperty(center, "scale", Vector2.One, 1.1).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(accentLine, "custom_minimum_size:x", 140.0f, 0.6).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out).SetDelay(0.15);
        tween.Chain().TweenInterval(TitleCardHold);
        tween.Chain().TweenProperty(_titleCard, "modulate:a", 0.0f, TitleCardOut);
        tween.Chain().TweenCallback(Callable.From(Skip));
    }

    /// <summary>叙事字幕卡：设置文本并从 +8px 上浮淡入（淡出由 _on_shot_timeout 随转场处理）</summary>
    private void SetSubtitle(string key)
    {
        if (_subTween != null && _subTween.IsValid())
        {
            _subTween.Kill();
        }

        _subtitle.Text = (string)Tr(key);
        _subtitle.Modulate = new Color(_subtitle.Modulate, 0.0f);
        _subtitle.Position = new Vector2(_subtitle.Position.X, _subtitleBaseY + 8.0f);
        _subTween = CreateTween().SetParallel(true);
        _subTween.TweenProperty(_subtitle, "modulate:a", 1.0f, 0.3);
        _subTween.TweenProperty(_subtitle, "position:y", _subtitleBaseY, 0.45).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
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

    // ---------------- 构图辅助（实现收敛至 CinematicFx 单源，本侧保留一行转发） ----------------

    private static GlowDot Glow(float radius, Color color, bool additive = true) => CinematicFx.Glow(radius, color, additive);

    private static Polygon2D RectPoly(float w, float h, Color color) => CinematicFx.RectPoly(w, h, color);

    private static ColorRect BgRect(Color color) => CinematicFx.BgRect(color);

    private static Line2D Line(Vector2[] points, Color color, float width = 2.0f) => CinematicFx.Line(points, color, width);

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
}
