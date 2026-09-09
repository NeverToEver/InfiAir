using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 标题屏（2026-09-09 深空机库改版）：程序化星空 + 远景实况战场（敌机编队/远处爆炸/Boss 剪影，
/// 见 Warzone 部分）+ 玩家机自远处跃迁飞入悬挂展示（轮廓背光/尾焰怠速/机体铭牌，见 ShipDisplay 部分）+
/// 左侧标题区。开场演出全程不阻塞输入，任意时刻可开局。
/// 输入路由不变：任意键/点击 → main.tscn 开局；T → tutorial.tscn；Esc 不消费。
/// 0.5s 输入守卫：过场跳过键/入场期残留按键不误触发开局。
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    private const ulong InputGuardMs = 500;

    // 悬挂展示位与跃迁远点（1920×1080 设计坐标：机体停驻右三分之一，自右上远处飞入）
    private static readonly Vector2 ShipAnchorPos = new(1360.0f, 470.0f);
    private static readonly Vector2 ShipFarPos = new(1560.0f, 230.0f);

    private ulong _readyMs;

    public override void _Ready()
    {
        // 深空底色 + 程序化星空（IntroCinematic Shot1/6 同款直接 new）
        // Starfield._Ready 自置 ZIndex=-10，底色须再低一层否则星点被底色盖住
        var bg = CinematicFx.BgRect(new Color(0.012f, 0.02f, 0.04f));
        bg.ZIndex = -20;
        AddChild(bg);
        AddChild(new Starfield());

        BuildWarzone();
        BuildShipDisplay();
        BuildTitleUi();

        // 开场黑场淡出：掩盖过场/主场景 → 标题屏的场景硬切（0.5s）
        var fadeIn = CinematicFx.BgRect(new Color(0.0f, 0.0f, 0.0f, 1.0f));
        AddChild(fadeIn);
        var fadeTween = fadeIn.CreateTween();
        fadeTween.TweenProperty(fadeIn, "color:a", 0.0f, 0.5).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        fadeTween.TweenCallback(Callable.From(fadeIn.QueueFree));

        _readyMs = Time.GetTicksMsec();
    }

    private void BuildTitleUi()
    {
        // 标题后方呼吸辉光（原版保留，随标题区左移；外包一层节点做延迟淡入，与呼吸循环不抢同一属性）
        var glowWrap = new Node2D { Position = new Vector2(440.0f, 470.0f), Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f) };
        var glow = CinematicFx.SoftGlow(240.0f, new Color(UITheme.Accent, 0.06f));
        glow.Scale *= new Vector2(1.6f, 0.9f); // 横向拉扁成光带，避免圆形光球感
        glowWrap.AddChild(glow);
        AddChild(glowWrap);
        var glowTween = glow.CreateTween().SetLoops();
        glowTween.TweenProperty(glow, "modulate:a", 0.55f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        glowTween.TweenProperty(glow, "modulate:a", 1.0f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        var glowIn = glowWrap.CreateTween();
        glowIn.TweenProperty(glowWrap, "modulate:a", 1.0f, 0.5).SetDelay(1.0).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        // 标题区（左三分之一列，竖排居中）
        var vbox = new VBoxContainer
        {
            Position = new Vector2(140.0f, 330.0f),
            Size = new Vector2(600.0f, 330.0f),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f),
        };
        AddChild(vbox);

        var title = new Label { Text = "InfiAir", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        // 字距拉开（FontVariation 一次性实例化）+ 同色软辉光，片头版式质感
        title.AddThemeFontOverride("font", new FontVariation { BaseFont = UITheme.Font, SpacingGlyph = 14 });
        title.AddThemeFontSizeOverride("font_size", 92);
        title.AddThemeColorOverride("font_color", UITheme.Accent);
        title.AddThemeColorOverride("font_shadow_color", new Color(UITheme.Accent, 0.35f));
        title.AddThemeConstantOverride("shadow_offset_x", 0);
        title.AddThemeConstantOverride("shadow_offset_y", 0);
        title.AddThemeConstantOverride("shadow_outline_size", 8);
        vbox.AddChild(title);

        var accentLine = new ColorRect
        {
            Color = new Color(UITheme.Accent, 0.4f),
            CustomMinimumSize = new Vector2(140.0f, 3.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        vbox.AddChild(accentLine);

        var spacer = new Control { CustomMinimumSize = new Vector2(0.0f, 28.0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        vbox.AddChild(spacer);

        var hint = UITheme.MakeLabel((string)Tr("TITLE_PRESS_ANY_KEY"), UITheme.FontBody, UITheme.TextDim, HorizontalAlignment.Center);
        hint.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f); // 落位后（2.2s）才启动闪烁
        vbox.AddChild(hint);

        // 标题块滑入淡入（1.0s 起，与机体飞入并行）
        var titleIn = vbox.CreateTween().SetParallel(true);
        titleIn.TweenProperty(vbox, "modulate:a", 1.0f, 0.5).SetDelay(1.0).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        titleIn.TweenProperty(vbox, "position:x", 140.0f, 0.5).From(104.0f).SetDelay(1.0).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

        // 底部教程入口（随铭牌 1.8s 淡入）
        var tutorialHint = UITheme.MakeLabel((string)Tr("TITLE_TUTORIAL_HINT"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Center);
        tutorialHint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        tutorialHint.OffsetTop = -72.0f;
        tutorialHint.OffsetBottom = -40.0f;
        tutorialHint.OffsetLeft = -400.0f;
        tutorialHint.OffsetRight = 400.0f;
        tutorialHint.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        AddChild(tutorialHint);
        var tutIn = tutorialHint.CreateTween();
        tutIn.TweenProperty(tutorialHint, "modulate:a", 1.0f, 0.4).SetDelay(1.8).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        // 「按任意键」闪烁：演出落位后（2.2s）启动
        var blinkStarter = new Godot.Timer { OneShot = true, WaitTime = 2.2, Autostart = true };
        AddChild(blinkStarter);
        blinkStarter.Timeout += () =>
        {
            var blink = hint.CreateTween().SetLoops();
            blink.TweenProperty(hint, "modulate:a", 0.25f, 0.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            blink.TweenProperty(hint, "modulate:a", 0.9f, 0.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        };
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Time.GetTicksMsec() - _readyMs < InputGuardMs)
        {
            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            return; // 标题屏无返回目标，Esc 不消费
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            var kc = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
            if (kc == Key.T)
            {
                GetTree().ChangeSceneToFile("res://scenes/tutorial.tscn");
            }
            else
            {
                StartGame();
            }

            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton { Pressed: true } or InputEventJoypadButton { Pressed: true })
        {
            StartGame();
            GetViewport().SetInputAsHandled();
        }
    }

    private void StartGame()
    {
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }
}
