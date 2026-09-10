using Godot;

namespace InfiAir;

/// <summary>
/// 全 UI 统一色板、字号阶梯与样式工厂（HUD / Sci-Fi FUI：细线、切角、全息青）。
/// 各 UI 一律从这里取色/取样式/取控件，不再散落硬编码色值与 Label/Button 样板。
/// RefCounted + 全静态工厂；C# 调用方经静态字段/方法 typed 直调。
/// </summary>
public partial class UITheme : RefCounted
{
    // ---------------- 色板（C# typed 直用） ----------------
    // 战术琥珀：深炭蓝黑底 + 琥珀主交互（按钮/焦点/进度/边框）+ 全息青降为数据通道
    // + 金=稀有 + 红=危险。金属钢面 tint 全部由这些 token 单源派生——改此一处 = 全站换色。
    public static readonly Color PanelBg = new(0.063f, 0.086f, 0.125f, 0.82f); // 面板底 暗钢蓝黑（提亮一档让拉丝可辨）
    public static readonly Color PanelBorder = new(1.0f, 0.624f, 0.110f, 0.45f); // 面板边框 琥珀 1px 细线
    public static readonly Color Accent = new(0xff9f1cff); // 主强调琥珀（交互/焦点/进度/边框）
    public static readonly Color AccentHot = new(0xffc14dff); // 受激琥珀（hover/焦点峰值/脉冲）
    public static readonly Color AccentBlue = new(0x38bdf8ff); // 数据全息青（次要通道：数据/次级弧）
    public static readonly Color AccentGold = new(0xe8c170ff); // 数值金（RP/最高分/新纪录等关键数值）
    public static readonly Color AccentDim = new(1.0f, 0.624f, 0.110f, 0.22f); // 装饰分隔线/页头短线
    public static readonly Color BgDeep = new(0.027f, 0.039f, 0.059f, 0.92f); // 更深面板底（欢迎页/满屏遮罩层）
    public static readonly Color Text = new(0xe6edf3ff); // 文字主
    public static readonly Color TextOnBright = new(0x17110aff); // 高亮暖钢面上的深暖字（hover 钢面近白，浅色字会洗白）
    public static readonly Color TextDim = new(0x8a97a6ff); // 文字次
    public static readonly Color Danger = new(0xff3b4eff); // 警报红
    public static readonly Color Success = new(0x3fd68cff); // 成功绿（降饱和）
    public static readonly Color BtnPrimaryBg = new(1.0f, 0.624f, 0.110f, 0.18f); // 主按钮底（ACCENT 18% alpha）

    // 金属钢面 token：按钮/底衬贴图为近白灰度 + 预烘焙倒角（assets/sprites/ui/button_plate*），
    // 色相/明度全部由这些 tint 单源派生；hover 允许 >1 的通道（乘贴图后 clamp，读作受激暖光）
    public static readonly Color SteelTint = new(0.66f, 0.62f, 0.56f); // 暖钢灰（按钮 normal）
    public static readonly Color SteelTintHover = new(0.98f, 0.88f, 0.72f); // 受激暖光提亮（hover/焦点）
    public static readonly Color SteelTintPressed = new(0.46f, 0.40f, 0.34f); // 按下凹陷（换凹陷贴图 + 压暗）
    public static readonly Color SteelAccentTint = new(1.0f, 0.72f, 0.30f); // 主按钮钢面透琥珀（ACCENT 系）
    public static readonly Color SteelAccentHover = new(1.12f, 0.86f, 0.46f);
    public static readonly Color SteelAccentPressed = new(0.66f, 0.46f, 0.18f);
    public static readonly Color PanelSteelTint = new(0.13f, 0.11f, 0.09f, 0.88f); // PanelContainer 暗钢底衬（名牌/下拉等）
    public static readonly Color DimBg = new(0.008f, 0.007f, 0.006f, 0.84f); // 全屏遮罩：暖黑强压暗
    /// <summary>暗钢空槽基色：仪表空槽/弹仓空格/扇形根圆的公共 RGB，各用途配不同 alpha。</summary>
    public static readonly Color SlotDark = new(0.060f, 0.055f, 0.050f);
    public static readonly Color CommBgDark = new(0.10f, 0.03f, 0.09f, 0.78f); // 通讯面板暗品红底（与 EventMagenta 同系不同值）
    public static readonly Color EventMagenta = new(1.0f, 0.25f, 0.75f); // 随机事件/通讯品红
    public static readonly Color WarnYellow = new(1.0f, 0.86f, 0.30f); // 蓄力/提示黄（比琥珀更黄更亮，与主交互色区分）
    public static readonly Color ChargeAccent = new(1.0f, 0.76f, 0.30f); // 蓄力琥珀（蓄力进度条）
    public static readonly Color BannerDangerBg = new(0.35f, 0.06f, 0.10f, 0.7f); // 警告横幅底

    // 虚影基地皮肤 token（基地控制台全息青身份，与琥珀主交互刻意区分）
    public static readonly Color PhantomPanelBg = new(0.03f, 0.08f, 0.12f, 0.55f); // 虚影面板底
    public static readonly Color PhantomBorder = new(new Color(0x38bdf8ff), 0.65f); // 虚影面板边框
    public static readonly Color PhantomScan = new(new Color(0x38bdf8ff), 0.06f); // 扫描线/毛玻璃叠加层

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
            // 静态缓存 FontFile（Godot RefCounted）在引擎退出后被 .NET finalize 触碰
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
        button.AddThemeStyleboxOverride("normal", MakeBtnStyle(SteelAccentTint));
        button.AddThemeStyleboxOverride("hover", MakeBtnStyle(SteelAccentHover));
        button.AddThemeStyleboxOverride("pressed", MakeBtnStyle(SteelAccentPressed, inset: true));
        button.AddThemeStyleboxOverride("hover_pressed", MakeBtnStyle(SteelAccentPressed, inset: true));
        button.AddThemeStyleboxOverride("disabled", MakeBtnStyle(new Color(SteelAccentTint, 0.4f)));
        // 焦点 = 描边环：focus 样式盒叠画在最上层，整板高亮会把浅色字洗白（焦点+按下态实测不可读）
        button.AddThemeStyleboxOverride("focus", MakeFocusRing());
        button.AddThemeColorOverride("font_color", AccentHot);
        button.AddThemeColorOverride("font_hover_color", TextOnBright);
        button.AddThemeColorOverride("font_pressed_color", Text);
        button.AddThemeColorOverride("font_hover_pressed_color", Text);
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
    public static Control MakeAugmentSocket(StringName id, float tilePx = 46.0f)
    {
        var color = AugmentIcons.ColorFor(id);
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
        center.AddChild(AugmentIcons.MakeGlyph(id, color, tilePx * 0.57f)); // 46→26px / 76→43px，留白一致
        panel.AddChild(center);
        return panel;
    }

    /// <summary>Buff 图标格：46×46 socket 瓦片，层数 &gt;1 时右下角叠一枚切角 ×N 徽标芯片。</summary>
    public static Control MakeAugmentTile(StringName id, int stacks)
    {
        var panel = MakeAugmentSocket(id);
        if (stacks > 1)
        {
            var color = AugmentIcons.ColorFor(id);
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

        var panel = new ChamferedPanel { Brackets = true, EdgeRivets = true };
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

    /// <summary>统一按钮样式：金属钢板（贴图预烘焙凸起倒角）——normal 冷钢灰面 + 状态差异走 tint。</summary>
    public static void ApplyButton(Button button)
    {
        button.AddThemeStyleboxOverride("normal", MakeBtnStyle(SteelTint));
        button.AddThemeStyleboxOverride("hover", MakeBtnStyle(SteelTintHover));
        button.AddThemeStyleboxOverride("pressed", MakeBtnStyle(SteelTintPressed, inset: true));
        button.AddThemeStyleboxOverride("hover_pressed", MakeBtnStyle(SteelTintPressed, inset: true));
        button.AddThemeStyleboxOverride("disabled", MakeBtnStyle(new Color(SteelTint, 0.4f)));
        // 焦点 = 描边环（原因同 ApplyPrimaryButton）
        button.AddThemeStyleboxOverride("focus", MakeFocusRing());
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", TextOnBright);
        button.AddThemeColorOverride("font_pressed_color", Text);
        button.AddThemeColorOverride("font_hover_pressed_color", Text);
        button.AddThemeColorOverride("font_disabled_color", new Color(TextDim, 0.5f));
    }

    /// <summary>虚影面板材质（§3.2）：更透的全息底 + 亮一档边框（仅基地控制台使用）。</summary>
    public static void ApplyPhantomPanel(ChamferedPanel panel)
    {
        panel.BgColor = PhantomPanelBg;
        panel.BorderColor = PhantomBorder;
    }

    private const string BtnPlatePath = "res://assets/sprites/ui/button_plate.png";
    private const string BtnPlatePressedPath = "res://assets/sprites/ui/button_plate_pressed.png";

    /// <summary>焦点描边环（不填底——focus 样式盒叠画在状态样式之上，填底会盖住钢板）。</summary>
    private static StyleBoxFlat MakeFocusRing()
    {
        var ring = new StyleBoxFlat
        {
            DrawCenter = false,
            BorderColor = AccentHot,
        };
        ring.SetBorderWidthAll(2);
        ring.SetExpandMarginAll(2.0f);
        return ring;
    }

    /// <summary>按钮钢板样式：九宫格平铺 + 倒角已烘焙进贴图；状态差异全部走 ModulateColor
    /// （pressed 换凹陷贴图——倒角反转，受光方向不变）。inset=true 用凹陷钢板。</summary>
    private static StyleBoxTexture MakeBtnStyle(Color tint, bool inset = false)
    {
        var style = new StyleBoxTexture
        {
            Texture = GD.Load<Texture2D>(inset ? BtnPlatePressedPath : BtnPlatePath),
            ModulateColor = tint,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
        };
        style.TextureMarginLeft = 8;
        style.TextureMarginTop = 8;
        style.TextureMarginRight = 8;
        style.TextureMarginBottom = 8;
        style.SetContentMarginAll(8.0f);
        return style;
    }

    /// <summary>金属面板底衬（PanelContainer/Panel 的 "panel" 样式盒）：暗钢 tint，散落 Panel 统一入口。</summary>
    public static StyleBoxTexture MakeMetalPanelStyle(Color? tint = null)
        => MakeBtnStyle(tint ?? PanelSteelTint);

    /// <summary>输入框金属化（normal/focus 钢板 + 文字/光标/占位配色）。散落 LineEdit 统一入口。</summary>
    public static void ApplyMetalLineEdit(LineEdit edit)
    {
        var normal = MakeBtnStyle(new Color(0.26f, 0.33f, 0.46f, 0.80f));
        normal.ContentMarginLeft = 12.0f;
        normal.ContentMarginRight = 12.0f;
        var focus = MakeBtnStyle(new Color(0.32f, 0.42f, 0.58f, 0.88f));
        focus.ContentMarginLeft = 12.0f;
        focus.ContentMarginRight = 12.0f;
        edit.AddThemeStyleboxOverride("normal", normal);
        edit.AddThemeStyleboxOverride("focus", focus);
        edit.AddThemeColorOverride("font_color", Text);
        edit.AddThemeColorOverride("font_placeholder_color", new Color(TextDim, 0.7f));
        edit.AddThemeColorOverride("caret_color", Accent);
        edit.AddThemeColorOverride("selection_color", new Color(Accent, 0.30f));
    }

    /// <summary>滚动条金属化（深槽 + 钢质拉条）。ScrollContainer 两轴滚动条统一入口。</summary>
    public static void ApplyMetalScrollBar(ScrollBar bar)
    {
        var groove = new StyleBoxFlat { BgColor = new Color(0.012f, 0.020f, 0.036f, 0.85f) };
        groove.SetCornerRadiusAll(3);
        var grabber = new StyleBoxFlat { BgColor = new Color(0.42f, 0.50f, 0.64f, 0.90f) };
        grabber.SetCornerRadiusAll(3);
        var grabberHot = new StyleBoxFlat { BgColor = new Color(0.60f, 0.78f, 0.92f, 0.95f) };
        grabberHot.SetCornerRadiusAll(3);
        bar.AddThemeStyleboxOverride("scroll", groove);
        bar.AddThemeStyleboxOverride("grabber", grabber);
        bar.AddThemeStyleboxOverride("grabber_highlight", grabberHot);
        bar.AddThemeStyleboxOverride("grabber_pressed", grabberHot);
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

        // 互斥——快速进出按钮时旧 tween kill 再建，防同属性竞争抖动
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
}
