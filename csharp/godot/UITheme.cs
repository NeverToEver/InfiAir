using Godot;

namespace InfiAir;

/// <summary>
/// 全 UI 统一色板、字号阶梯与样式工厂（HUD / Sci-Fi FUI：细线、切角、全息青）。
/// 各 UI 一律从这里取色/取样式/取控件，不再散落硬编码色值与 Label/Button 样板。
/// M5 全量迁移（2026-08-08 自 scripts/ui_theme.gd）：RefCounted + 全静态工厂。
/// C# 调用方经静态字段/方法 typed 直调；GDScript 调用方（M6 过场/测试）经脚本资源
/// 调静态方法（GDScript 不能访问 C# 静态字段/常量——GetAccent() 等访问器 + snake 桥）。
/// </summary>
public partial class UITheme : RefCounted
{
    // ---------------- 色板（C# typed 直用；GDScript 经 Get*() 访问器） ----------------
    public static readonly Color PanelBg = new(0.039f, 0.063f, 0.102f, 0.78f); // 面板底 藏青
    public static readonly Color PanelBorder = new(0.0f, 0.83f, 1.0f, 0.5f); // 面板边框 青 1px 细线
    public static readonly Color Accent = new(0x00d4ffff); // 主强调青
    public static readonly Color AccentBlue = new(0x0080ffff); // 辅助全息蓝
    public static readonly Color AccentGold = new(0xd8a868ff); // 数值金（RP/最高分/新纪录等关键数值）
    public static readonly Color AccentDim = new(0.0f, 0.83f, 1.0f, 0.22f); // 装饰分隔线/页头短线
    public static readonly Color BgDeep = new(0.024f, 0.039f, 0.067f, 0.92f); // 更深面板底（欢迎页/满屏遮罩层）
    public static readonly Color Text = new(0xe0e8f0ff); // 文字主
    public static readonly Color TextDim = new(0x8a9bb0ff); // 文字次
    public static readonly Color Danger = new(0xff3366ff); // 警报红
    public static readonly Color Success = new(0x00ff88ff); // 成功绿
    public static readonly Color BtnNormal = new(0.039f, 0.063f, 0.102f, 0.4f); // 透明底
    public static readonly Color BtnHover = new(0.0f, 0.83f, 1.0f, 0.12f);
    public static readonly Color BtnPressed = new(0.0f, 0.83f, 1.0f, 0.25f);
    public static readonly Color BtnPrimaryBg = new(0.0f, 0.83f, 1.0f, 0.18f); // 主按钮底（ACCENT 18% alpha）
    public static readonly Color DimBg = new(0.006f, 0.012f, 0.024f, 0.84f); // 全屏遮罩：深青黑强压暗
    public static readonly Color EventMagenta = new(1.0f, 0.25f, 0.75f); // 随机事件/通讯品红
    public static readonly Color WarnYellow = new(1.0f, 0.8f, 0.35f); // 蓄力/提示黄
    public static readonly Color ChargeCyan = new(0.5f, 0.9f, 1.0f); // 蓄力青
    public static readonly Color BannerDangerBg = new(0.35f, 0.06f, 0.10f, 0.7f); // 警告横幅底

    // 虚影基地皮肤 token（docs/RETURN_HOME_CINEMATIC.md §3.2）
    public static readonly Color PhantomBg = new(0.01f, 0.03f, 0.06f, 0.90f); // 基地全屏底
    public static readonly Color PhantomPanelBg = new(0.03f, 0.08f, 0.12f, 0.55f); // 虚影面板底
    public static readonly Color PhantomBorder = new(0.0f, 0.83f, 1.0f, 0.65f); // 虚影面板边框
    public static readonly Color PhantomScan = new(0.0f, 0.83f, 1.0f, 0.06f); // 扫描线/毛玻璃叠加层

    // ---------------- 字号阶梯（层级靠字号/颜色/透明度区分） ----------------
    public const int FontDisplay = 72; // 超大展示（主标题/结算大数字）
    public const int FontTitle = 40; // 页标题
    public const int FontScore = 32; // 大数值（得分等）
    public const int FontHeader = 28; // 卡片名/主按钮
    public const int FontBody = 24; // 正文/次按钮
    public const int FontHudL = 22; // HUD 大字（通讯字幕等）
    public const int FontHud = 20; // HUD 正文
    public const int FontCaption = 18; // 说明/分组标题/角落提示
    public const int FontSmall = 16; // 小字（芯片/标签）

    public static FontFile Font
    {
        get
        {
            // M5 定位：静态缓存 FontFile（Godot RefCounted）在引擎退出后被 .NET finalize 触碰
            // native → 退出 segfault（实测）；GD.Load 命中资源缓存，代价可接受，不做静态持有
            return GD.Load<FontFile>("res://assets/fonts/NotoSansSC.ttf");
        }
    }

    // ---------------- 控件工厂 ----------------

    public static Label MakeLabel(string text)
        => MakeLabel(text, FontBody, Text, HorizontalAlignment.Center);

    public static Label MakeLabel(string text, int size)
        => MakeLabel(text, size, Text, HorizontalAlignment.Center);

    public static Label MakeLabel(string text, int size, Color color)
        => MakeLabel(text, size, color, HorizontalAlignment.Center);

    /// <summary>统一 Label 工厂：字号走阶梯常量，颜色走色板。</summary>
    public static Label MakeLabel(string text, int size, Color color, HorizontalAlignment align)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = align,
        };
        label.AddThemeFontOverride("font", Font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>统一按钮工厂。primary=true：ACCENT 底（18% alpha）+ 亮边框 + 较大字号（主操作）。</summary>
    public static Button MakeButton(string text) => MakeButton(text, false);

    public static Button MakeButton(string text, bool primary)
    {
        var button = new Button { Text = text };
        button.AddThemeFontOverride("font", Font);
        if (primary)
        {
            ApplyPrimaryButton(button);
        }
        else
        {
            button.AddThemeFontSizeOverride("font_size", FontBody);
            ApplyButton(button);
        }

        AddButtonMotion(button);
        return button;
    }

    /// <summary>主按钮样式（动态切换主次层级时可重复调用）。</summary>
    public static void ApplyPrimaryButton(Button button)
    {
        button.AddThemeFontSizeOverride("font_size", FontHeader);
        button.AddThemeStyleboxOverride("normal", MakeBtnStyle(BtnPrimaryBg, Accent));
        button.AddThemeStyleboxOverride("hover", MakeBtnStyle(new Color(Accent, 0.3f), Accent));
        button.AddThemeStyleboxOverride("pressed", MakeBtnStyle(new Color(Accent, 0.42f), Accent));
        button.AddThemeStyleboxOverride("disabled", MakeBtnStyle(new Color(BtnPrimaryBg, 0.5f), new Color(Accent, 0.4f)));
        // 焦点样式与 hover 一致：键盘导航时焦点可见
        button.AddThemeStyleboxOverride("focus", MakeBtnStyle(new Color(Accent, 0.3f), Accent));
        button.AddThemeColorOverride("font_color", Accent);
        button.AddThemeColorOverride("font_hover_color", Text);
        button.AddThemeColorOverride("font_pressed_color", Text);
        button.AddThemeColorOverride("font_disabled_color", new Color(TextDim, 0.5f));
    }

    /// <summary>互斥选项按钮（设置页档位列：toggle + ButtonGroup）。</summary>
    public static Button MakeToggleButton(string text, ButtonGroup group)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            ButtonGroup = group,
            CustomMinimumSize = new Vector2(110.0f, 48.0f),
        };
        ApplyButton(button);
        button.AddThemeFontOverride("font", Font);
        button.AddThemeFontSizeOverride("font_size", FontBody);
        AddButtonMotion(button);
        return button;
    }

    /// <summary>Buff 字形槽（socket）：ChamferedPanel 瓦片，分类色描边 + 同色内框 + 淡色底。</summary>
    public static Control MakeBuffSocket(StringName id, float tilePx = 46.0f)
    {
        var color = BuffIcons.ColorFor(id);
        var panel = new ChamferedPanel
        {
            Chamfer = Mathf.Max(tilePx * 0.15f, 4.0f),
            Padding = 0.0f,
            CustomMinimumSize = new Vector2(tilePx, tilePx),
            BgColor = PanelBg.Lerp(new Color(color, PanelBg.A), 0.16f),
            BorderColor = new Color(color, 0.7f),
            InnerFrame = true,
            InnerFrameColor = new Color(color, 0.28f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var center = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.AddChild(BuffIcons.MakeGlyph(id, color, tilePx * 0.57f)); // 46→26px / 76→43px，留白一致
        panel.AddChild(center);
        return panel;
    }

    /// <summary>Buff 图标格：46×46 socket 瓦片，层数 &gt;1 时右下角叠一枚切角 ×N 徽标芯片。</summary>
    public static Control MakeBuffTile(StringName id, int stacks)
    {
        var panel = MakeBuffSocket(id);
        if (stacks > 1)
        {
            var color = BuffIcons.ColorFor(id);
            var chip = new ChamferedPanel
            {
                Chamfer = 4.0f,
                Padding = 0.0f,
                CustomMinimumSize = new Vector2(24.0f, 16.0f),
                BgColor = new Color(BgDeep, 0.95f),
                BorderColor = new Color(color, 0.6f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            chip.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
            chip.Position = new Vector2(-26.0f, -18.0f); // 右下 2px 内缩，芯片留在瓦片内
            var badge = MakeLabel($"×{stacks}", 12, AccentGold, HorizontalAlignment.Center);
            badge.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.MouseFilter = Control.MouseFilterEnum.Ignore;
            chip.AddChild(badge);
            panel.AddChild(chip);
        }

        return panel;
    }

    /// <summary>分组标题：小号 CAPTION 标题（左对齐）+ 下方 1px 分隔线。</summary>
    public static Control MakeSectionHeader(string text)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(MakeLabel(text, FontCaption, Accent, HorizontalAlignment.Left));
        var line = new ColorRect
        {
            Color = AccentDim,
            CustomMinimumSize = new Vector2(0.0f, 1.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        box.AddChild(line);
        return box;
    }

    /// <summary>页面骨架：遮罩 dim + CenterContainer + ChamferedPanel(brackets) + 页头 + 内容 VBox。
    /// 返回键：root/dim/panel/margin/title/content。</summary>
    public static Godot.Collections.Dictionary MakePageShell(string titleKey)
    {
        var dim = new ColorRect
        {
            Color = DimBg,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.AddChild(center);

        var panel = new ChamferedPanel { Brackets = true };
        center.AddChild(panel);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 16);
        margin.AddChild(vbox);

        var header = new VBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(header);
        var title = MakeLabel(TranslationServer.Translate(titleKey), FontTitle, Accent);
        header.AddChild(title);
        var accentLineWrap = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        header.AddChild(accentLineWrap);
        var accentLine = new ColorRect
        {
            Color = Accent,
            CustomMinimumSize = new Vector2(64.0f, 3.0f),
        };
        accentLineWrap.AddChild(accentLine);
        var divider = new ColorRect
        {
            Color = AccentDim,
            CustomMinimumSize = new Vector2(0.0f, 1.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        header.AddChild(divider);

        var content = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 16);
        vbox.AddChild(content);

        var shell = new Godot.Collections.Dictionary
        {
            ["root"] = Variant.From(dim),
            ["dim"] = Variant.From(dim),
            ["panel"] = Variant.From(panel),
            ["margin"] = Variant.From(margin),
            ["title"] = Variant.From(title),
            ["content"] = Variant.From(content),
        };
        return shell;
    }

    // ---------------- 动效 ----------------

    /// <summary>模态统一打开动效：遮罩 150ms 淡入 + 面板 200ms 淡入 + 内容错峰淡入（可选）。</summary>
    public static void AnimateModalOpen(Control dim, Control panel, Control? content = null)
    {
        dim.Modulate = new Color(dim.Modulate, 0.0f);
        var dimTween = dim.CreateTween();
        dimTween.TweenProperty(dim, "modulate:a", 1.0f, 0.15);
        AnimateOpen(panel);
        if (content != null)
        {
            StaggerOpen(content);
        }
    }

    /// <summary>子项依次 60ms 间隔淡入（只动 modulate.a，不动 position——容器布局会覆盖 position）。</summary>
    public static void StaggerOpen(Control container)
    {
        var i = 0;
        foreach (var child in container.GetChildren())
        {
            if (child is not Control c || !c.Visible)
            {
                continue;
            }

            c.Modulate = new Color(c.Modulate, 0.0f);
            var tween = c.CreateTween();
            tween.TweenInterval(0.06 * i);
            tween.TweenProperty(c, "modulate:a", 1.0f, 0.18);
            i += 1;
        }
    }

    // ---------------- 基础样式 ----------------

    /// <summary>统一按钮样式：切角系（直角）——normal 透明底+青边框。</summary>
    public static void ApplyButton(Button button)
    {
        button.AddThemeStyleboxOverride("normal", MakeBtnStyle(BtnNormal, PanelBorder));
        button.AddThemeStyleboxOverride("hover", MakeBtnStyle(BtnHover, Accent));
        button.AddThemeStyleboxOverride("pressed", MakeBtnStyle(BtnPressed, Accent));
        button.AddThemeStyleboxOverride("disabled", MakeBtnStyle(new Color(BtnNormal, 0.5f), new Color(PanelBorder, 0.4f)));
        // 焦点样式与 hover 一致：键盘导航（Tab/方向键 + Enter）时焦点可见
        button.AddThemeStyleboxOverride("focus", MakeBtnStyle(BtnHover, Accent));
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Accent);
        button.AddThemeColorOverride("font_pressed_color", Text);
        button.AddThemeColorOverride("font_disabled_color", new Color(TextDim, 0.5f));
    }

    /// <summary>虚影面板材质（§3.2）：更透的全息底 + 亮一档边框（仅基地控制台使用）。</summary>
    public static void ApplyPhantomPanel(ChamferedPanel panel)
    {
        panel.BgColor = PhantomPanelBg;
        panel.BorderColor = PhantomBorder;
    }

    private static StyleBoxFlat MakeBtnStyle(Color bg, Color border)
    {
        var style = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(0);
        style.SetContentMarginAll(8.0f);
        return style;
    }

    /// <summary>面板打开微动效：200ms 淡入（不做位移动画——容器布局会覆盖 position）。</summary>
    public static void AnimateOpen(Control control)
    {
        control.Modulate = new Color(control.Modulate, 0.0f);
        var tween = control.CreateTween();
        tween.TweenProperty(control, "modulate:a", 1.0f, 0.2);
    }

    /// <summary>按钮微动效：hover/焦点 1.02 倍放大、按下 0.98 回弹。由 MakeButton/MakeToggleButton 统一挂载。</summary>
    public static void AddButtonMotion(Button button)
    {
        button.Resized += () => button.PivotOffset = button.Size * 0.5f;
        var updatePivot = Callable.From(() => button.PivotOffset = button.Size * 0.5f);
        updatePivot.Call();
        button.MouseEntered += () => MotionTween(button, 1.02f);
        button.MouseExited += () => MotionTween(button, 1.0f);
        button.FocusEntered += () => MotionTween(button, 1.02f);
        button.FocusExited += () => MotionTween(button, 1.0f);
        button.ButtonDown += () => MotionTween(button, 0.98f);
        button.ButtonUp += () => MotionTween(button, 1.0f);
    }

    private static void MotionTween(Button button, float target)
    {
        if (!GodotObject.IsInstanceValid(button))
        {
            return;
        }

        // H20（健壮性审核）：互斥——快速进出按钮时旧 tween kill 再建，防同属性竞争抖动
        if (button.HasMeta("motion_tween"))
        {
            var old = button.GetMeta("motion_tween").AsGodotObject() as Tween;
            if (old != null && old.IsValid())
            {
                old.Kill();
            }
        }

        var tween = button.CreateTween();
        button.SetMeta("motion_tween", Variant.From(tween));
        tween.TweenProperty(button, "scale", new Vector2(target, target), 0.08);
    }

    // ---------------- GDScript 鸭子调用兼容桥（M 批次过渡，M7 删除） ----------------
    // GDScript 调用方（M6 过场/测试）不能以类名引用 C# 类、不能访问 C# 静态字段/常量，
    // 经脚本资源调用静态方法；色板常量转 Get*() 访问器。

    public static Color GetPanelBg() => PanelBg;

    public static Color GetPanelBorder() => PanelBorder;

    public static Color GetAccent() => Accent;

    public static Color GetAccentBlue() => AccentBlue;

    public static Color GetAccentGold() => AccentGold;

    public static Color GetAccentDim() => AccentDim;

    public static Color GetBgDeep() => BgDeep;

    public static Color GetText() => Text;

    public static Color GetTextDim() => TextDim;

    public static Color GetDanger() => Danger;

    public static Color GetSuccess() => Success;

    public static Color GetBtnNormal() => BtnNormal;

    public static Color GetBtnHover() => BtnHover;

    public static Color GetBtnPressed() => BtnPressed;

    public static Color GetBtnPrimaryBg() => BtnPrimaryBg;

    public static Color GetDimBg() => DimBg;

    public static Color GetEventMagenta() => EventMagenta;

    public static Color GetWarnYellow() => WarnYellow;

    public static Color GetChargeCyan() => ChargeCyan;

    public static Color GetBannerDangerBg() => BannerDangerBg;

    public static Color GetPhantomBg() => PhantomBg;

    public static Color GetPhantomPanelBg() => PhantomPanelBg;

    public static Color GetPhantomBorder() => PhantomBorder;

    public static Color GetPhantomScan() => PhantomScan;

    public static int GetFontDisplay() => FontDisplay;

    public static int GetFontTitle() => FontTitle;

    public static int GetFontScore() => FontScore;

    public static int GetFontHeader() => FontHeader;

    public static int GetFontBody() => FontBody;

    public static int GetFontHudL() => FontHudL;

    public static int GetFontHud() => FontHud;

    public static int GetFontCaption() => FontCaption;

    public static int GetFontSmall() => FontSmall;

    public static FontFile GetFont() => Font;












    public static Control make_section_header(string text) => MakeSectionHeader(text);




    public static void stagger_open(Control container) => StaggerOpen(container);



    public static void animate_open(Control control) => AnimateOpen(control);

}
