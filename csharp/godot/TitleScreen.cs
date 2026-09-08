using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 标题屏（2026-09-08 开机流程「过场 → 标题屏 → 任意键」）：黑底 + InfiAir 标题 + 呼吸辉光 +
/// 闪烁「按任意键开始」+ T 教程入口。任意键/点击 → main.tscn 开局；T → tutorial.tscn。
/// 0.5s 输入守卫：过场跳过键/入场期残留按键不误触发开局。
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    private const ulong InputGuardMs = 500;

    private ulong _readyMs;

    public override void _Ready()
    {
        var bg = new ColorRect { Color = new Color(0.018f, 0.03f, 0.055f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        // 标题后方低频呼吸辉光（IntroCinematic 片头光带同款，纯黑底上给一点纵深）
        var glowPad = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        glowPad.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var glow = CinematicFx.SoftGlow(240.0f, new Color(UITheme.Accent, 0.06f));
        glow.Position = new Vector2(960.0f, 540.0f);
        glow.Scale *= new Vector2(1.6f, 0.9f); // 横向拉扁成片头光带，避免圆形光球感
        glowPad.AddChild(glow);
        AddChild(glowPad);
        var glowTween = glow.CreateTween().SetLoops();
        glowTween.TweenProperty(glow, "modulate:a", 0.55f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        glowTween.TweenProperty(glow, "modulate:a", 1.0f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);
        var vbox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        center.AddChild(vbox);

        var title = new Label { Text = "InfiAir", HorizontalAlignment = HorizontalAlignment.Center };
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
        };
        vbox.AddChild(accentLine);

        var spacer = new Control { CustomMinimumSize = new Vector2(0.0f, 28.0f) };
        vbox.AddChild(spacer);

        var hint = UITheme.MakeLabel((string)Tr("TITLE_PRESS_ANY_KEY"), UITheme.FontBody, UITheme.TextDim, HorizontalAlignment.Center);
        vbox.AddChild(hint);
        var blink = hint.CreateTween().SetLoops();
        blink.TweenProperty(hint, "modulate:a", 0.25f, 0.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        blink.TweenProperty(hint, "modulate:a", 0.9f, 0.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        var tutorialHint = UITheme.MakeLabel((string)Tr("TITLE_TUTORIAL_HINT"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Center);
        tutorialHint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        tutorialHint.OffsetTop = -72.0f;
        tutorialHint.OffsetBottom = -40.0f;
        tutorialHint.OffsetLeft = -400.0f;
        tutorialHint.OffsetRight = 400.0f;
        AddChild(tutorialHint);

        _readyMs = Time.GetTicksMsec();
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
