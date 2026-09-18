using System;
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
    // 战术琥珀：暖炭黑底 + 琥珀主交互（按钮/焦点/进度/边框）+ 金=稀有 + 红=危险。
    // 面板/金属/文字灰全部暖偏（冷蓝灰会与琥珀主色互相打架）；金属钢面 tint 全部由这些
    // token 单源派生——改此一处 = 全站换色。
    public static readonly Color PanelBg = new(0.086f, 0.070f, 0.058f, 0.82f); // 面板底 暖炭黑（提亮一档让拉丝可辨）
    public static readonly Color PanelBorder = new(1.0f, 0.624f, 0.110f, 0.45f); // 面板边框 琥珀 1px 细线
    public static readonly Color Accent = new(0xff9f1cff); // 主强调琥珀（交互/焦点/进度/边框）
    public static readonly Color AccentHot = new(0xffc14dff); // 受激琥珀（hover/焦点峰值/脉冲）
    public static readonly Color Holo = new(0xffc861ff); // 全息线光（仪表/舱段/站体 holo 线，取代旧全息青）
    public static readonly Color HoloPale = new(0xffe6bfff); // 全息高光/读数峰值（暖白，用于热读数与光晕芯）
    public static readonly Color AccentGold = new(0xe8c170ff); // 数值金（RP/最高分/新纪录等关键数值）
    public static readonly Color AccentDim = new(1.0f, 0.624f, 0.110f, 0.22f); // 装饰分隔线/页头短线
    public static readonly Color BgDeep = new(0.043f, 0.034f, 0.027f, 0.92f); // 更深面板底（欢迎页/满屏遮罩层）
    /// <summary>最深面板底（母舰召唤小窗）：比 BgDeep 再暗一档，让长条面板贴屏幕左缘时与背景分明。</summary>
    public static readonly Color BgDeepest = new(0.038f, 0.032f, 0.026f, 0.92f);
    public static readonly Color Text = new(0xeee7dcff); // 文字主（暖白）
    public static readonly Color TextOnBright = new(0x17110aff); // 高亮暖钢面上的深暖字（hover 钢面近白，浅色字会洗白）
    public static readonly Color TextDim = new(0x9c9184ff); // 文字次（暖灰）
    public static readonly Color Danger = new(0xff3b4eff); // 警报红
    public static readonly Color Success = new(0xc2d16bff); // 成功色（暖橄榄金，替代冷绿——冷绿与琥珀互斥）
    public static readonly Color BtnPrimaryBg = new(1.0f, 0.624f, 0.110f, 0.18f); // 主按钮底（ACCENT 18% alpha）

    // 金属钢面 token：按钮/底衬贴图为近白灰度 + 预烘焙倒角（assets/sprites/ui/button_plate*），
    // 色相/明度全部由这些 tint 单源派生；hover 允许 >1 的通道（乘贴图后 clamp，读作受激暖光）
    public static readonly Color SteelTint = new(0.60f, 0.52f, 0.42f); // 暖青铜灰（按钮 normal；冷灰会读成白塑料）
    public static readonly Color SteelTintHover = new(0.98f, 0.88f, 0.72f); // 受激暖光提亮（hover/焦点）
    public static readonly Color SteelTintPressed = new(0.46f, 0.40f, 0.34f); // 按下凹陷（换凹陷贴图 + 压暗）
    public static readonly Color SteelAccentTint = new(1.0f, 0.72f, 0.30f); // 主按钮钢面透琥珀（ACCENT 系）
    public static readonly Color SteelAccentHover = new(1.12f, 0.86f, 0.46f);
    public static readonly Color SteelAccentPressed = new(0.66f, 0.46f, 0.18f);
    public static readonly Color PanelSteelTint = new(0.13f, 0.11f, 0.09f, 0.88f); // PanelContainer 暗钢底衬（名牌/下拉等）
    public static readonly Color DimBg = new(0.008f, 0.007f, 0.006f, 0.84f); // 全屏遮罩：暖黑强压暗
    /// <summary>暗钢空槽基色：仪表空槽/弹仓空格/扇形根圆的公共 RGB，各用途配不同 alpha。</summary>
    public static readonly Color SlotDark = new(0.060f, 0.055f, 0.050f);
    public static readonly Color CommBgDark = new(0.13f, 0.05f, 0.05f, 0.78f); // 通讯面板暗暖红底（与 EventMagenta 同系不同值）
    public static readonly Color EventMagenta = new(1.0f, 0.25f, 0.75f); // 随机事件/通讯品红（敌对通讯身份色，唯一非琥珀强调）
    public static readonly Color WarnYellow = new(1.0f, 0.86f, 0.30f); // 蓄力/提示黄（比琥珀更黄更亮，与主交互色区分）
    public static readonly Color ChargeAccent = new(1.0f, 0.76f, 0.30f); // 蓄力琥珀（蓄力进度条）
    public static readonly Color BannerDangerBg = new(0.35f, 0.06f, 0.10f, 0.7f); // 警告横幅底

    // HUD 专用 token（原先散落在 Hud/SegmentedBar/AimCrosshair 的硬编码色，收归单源）
    public static readonly Color HudBossHp = new(0.98f, 0.80f, 0.42f); // Boss 血条一阶段
    public static readonly Color HudBossHpP2 = new(1.0f, 0.46f, 0.14f); // Boss 血条二阶段
    public static readonly Color DangerVignette = new(1.0f, 0.20f, 0.30f); // 受击暗角内圈（比 Danger 更暗，压屏幕边缘）
    public static readonly Color TickWhite = new(1.0f, 1.0f, 1.0f, 0.55f); // 刻度线（Boss 分段）
    public static readonly Color TrackWhite = new(1.0f, 1.0f, 1.0f, 0.15f); // 仪表空槽高光
    public static readonly Color ShadowBlack = new(0.0f, 0.0f, 0.0f, 0.50f); // 仪表外框投影
    public static readonly Color SheenWhite = new(1.0f, 1.0f, 1.0f, 0.40f); // 仪表顶缘受光
    public static readonly Color AimAmber = new(1.0f, 0.76f, 0.30f, 0.95f); // 准星十字
    public static readonly Color AimOnTarget = new(1.0f, 0.36f, 0.46f, 0.95f); // 准星可攻击态（压住敌机碰撞圆，出弹即命中；与敌弹/敌机轮廓同族红粉）
    public static readonly Color AimAmberLit = new(1.0f, 0.72f, 0.30f); // 瞄准框常态
    public static readonly Color AimAmberHot = new(1.0f, 0.90f, 0.45f); // 瞄准框锁定峰值（准星锁定态同用此色：同一状态一种颜色）
    public static readonly Color HostileCool = new(0.52f, 0.64f, 0.80f, 0.50f); // 标题战场远处敌机冷色（暖族中的刻意冷色例外）
    public static readonly Color MothershipCool = new(0.35f, 0.85f, 1.0f); // 母舰能量青（能量层 tint / 召唤蓄力背光；与 HostileCool 同族的刻意冷色例外）

    // 虚影基地皮肤 token（基地控制台暖琥珀全息身份，靠亮度/扫描线区别于主交互色，不另起色相）
    public static readonly Color PhantomPanelBg = new(0.085f, 0.062f, 0.040f, 0.55f); // 虚影面板底（暖）
    public static readonly Color PhantomBorder = new(Holo, 0.65f); // 虚影面板边框（全息琥珀）
    public static readonly Color PhantomScan = new(Holo, 0.06f); // 扫描线/毛玻璃叠加层

    // ---------------- 字号阶梯（层级靠字号/颜色/透明度区分） ----------------
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

    /// <summary>增幅明细行（HUD 滚动栏与教程基地段触点共用的一份装配）：字形 + 名称 + 层数
    /// （&gt;1 时右侧 ×N）。名称由调用方取译文传入——取值口留在调用方，`Tr($"AUG_{id}_NAME")`
    /// 这类动态拼接键才会落在文案门禁的键族判定里（搬进本类就出了判定面）。
    /// 鼠标过滤不在此处置：由调用方按自己的容器语义设置（HUD 滚动栏整层 Ignore）。</summary>
    public static HBoxContainer MakeAugmentRow(StringName id, string displayName, int stacks)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(AugmentIcons.MakeGlyph(id, AugmentIcons.ColorFor(id), 24.0f));
        var nameLabel = MakeLabel(displayName, FontHud, Text, HorizontalAlignment.Left);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);
        if (stacks > 1)
        {
            row.AddChild(MakeLabel(
                Core.Text.GdFormat.Format("×%d", stacks), FontHud, AccentGold, HorizontalAlignment.Right));
        }

        return row;
    }

    /// <summary>焦点描边环外挂：给不走钢板样式的按钮（标题屏的扁平文字入口）补上与
    /// <see cref="ApplyButton"/> 同一枚环——焦点态必须有可见反馈，且全库只该有一种焦点语汇
    /// （引擎默认焦点盒是直角细框，与切角语汇不同族）。</summary>
    public static void ApplyFocusRing(Button button)
    {
        button.AddThemeStyleboxOverride("focus", MakeFocusRing());
        // 扁平文字入口（标题屏的教程/练习）也是按钮，交互音与钢板按钮同源
        AttachButtonSfx(button);
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
        AttachButtonSfx(button);
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

    /// <summary>增幅 字形槽（socket）：ChamferedPanel 瓦片，分类色描边 + 同色内框 + 淡色底。</summary>
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

    /// <summary>增幅 图标格：46×46 socket 瓦片，层数 &gt;1 时右下角叠一枚切角 ×N 徽标芯片。</summary>
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

    // ---------------- 切角几何（仪表盘与面板共用的形状语汇） ----------------

    /// <summary>切角矩形点集（顺时针，起点上左切角）：八边形，四角按 chamfer 切掉。
    /// 与 ChamferedPanel 的切角几何同源——仪表盘瓦片/量槽一律用它绘制，
    /// 保证「所有方形构件都是同一个切角」而非各处自行拼多边形。
    /// 尺寸或切角过小（放不下切角）时返回空集，调用方跳过绘制。</summary>
    public static Vector2[] ChamferPoints(Vector2 size, float chamfer)
    {
        var dst = new Vector2[8];
        if (!FillChamferPoints(dst, size, chamfer))
        {
            return System.Array.Empty<Vector2>();
        }

        return dst;
    }

    /// <summary>切角矩形点集写入调用方缓冲（零分配重载，容量须 ≥8）：逐帧绘制路径用它，
    /// 避免每帧新建数组。几何唯一来源在此——<see cref="ChamferPoints(Vector2, float)"/>
    /// 也经本方法产出，两处不得各写一份点序。放不下切角时返回 false（不写 dst），调用方跳过绘制。</summary>
    public static bool FillChamferPoints(Vector2[] dst, Vector2 size, float chamfer)
    {
        var c = Mathf.Max(chamfer, 0.0f);
        var w = size.X;
        var h = size.Y;
        if (c <= 0.0f || w < c * 2.0f || h < c * 2.0f)
        {
            return false;
        }

        dst[0] = new Vector2(c, 0.0f);
        dst[1] = new Vector2(w - c, 0.0f);
        dst[2] = new Vector2(w, c);
        dst[3] = new Vector2(w, h - c);
        dst[4] = new Vector2(w - c, h);
        dst[5] = new Vector2(c, h);
        dst[6] = new Vector2(0.0f, h - c);
        dst[7] = new Vector2(0.0f, c);
        return true;
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
        AttachButtonSfx(button);
    }

    /// <summary>虚影面板材质（§2.15，全息投影语汇）：更透的全息底（关拉丝钢——投影是光不是金属面）
    /// + HoloEdge 自发光边缘 + 裁剪内容（开机显影扫描带限制在面板内）。仅基地控制台使用。</summary>
    public static void ApplyPhantomPanel(ChamferedPanel panel)
    {
        panel.MetalFill = false;
        panel.HoloEdge = true;
        panel.ClipContents = true;
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

    /// <summary>滚动条金属化（深槽 + 钢质拉条）。ScrollContainer 两轴滚动条统一入口。</summary>
    public static void ApplyMetalScrollBar(ScrollBar bar)
    {
        var groove = new StyleBoxFlat { BgColor = new Color(0.022f, 0.017f, 0.013f, 0.85f) };
        groove.SetCornerRadiusAll(3);
        var grabber = new StyleBoxFlat { BgColor = new Color(0.48f, 0.42f, 0.34f, 0.90f) };
        grabber.SetCornerRadiusAll(3);
        var grabberHot = new StyleBoxFlat { BgColor = new Color(0.72f, 0.60f, 0.44f, 0.95f) };
        grabberHot.SetCornerRadiusAll(3);
        bar.AddThemeStyleboxOverride("scroll", groove);
        bar.AddThemeStyleboxOverride("grabber", grabber);
        bar.AddThemeStyleboxOverride("grabber_highlight", grabberHot);
        bar.AddThemeStyleboxOverride("grabber_pressed", grabberHot);
    }

    // ---------------- 设置页控件（滑杆 / 下拉 / 输入框；滚动条见 ApplyMetalScrollBar） ----------------
    // 这三类控件此前一律吃引擎默认主题（直角细框、冷灰底、灰色手柄），与全站暖琥珀自绘界面不同族。
    // 色相一律由既有 token 派生，贴图仍只用既有素材（button_plate*.png 九宫格）；滑杆手柄由
    // ChamferSlider 就地自绘——引擎把滑杆手柄当**主题图标**画，图标既不可着色、也没有按下态，
    // 表达不了三态与切角语汇。

    /// <summary>切角滑杆：手柄自绘为切角八边形（与 <see cref="ChamferPoints"/> 同一套方形语汇）。
    /// 三态＝常态暖青铜 / 悬停受激提亮 / 拖动按下压暗，键盘焦点再补一圈暖琥珀 2px
    ///（与 <see cref="ApplyFocusRing"/> 同一焦点语汇，不另起一种焦点表达）。
    ///
    /// 手柄尺寸取「引擎主题里那一枚 grabber」与设计尺寸的较大者：引擎先画它自己的图标
    ///（默认主题是灰色圆角块），自绘手柄必须把它完整盖住。**不替换主题图标**是刻意的——
    /// 引擎的点击→取值映射按图标宽度换算（`Slider::gui_input` 的 `grab_width`），
    /// 换图标尺寸即改拖动手感，而本批只许改外观。</summary>
    public partial class ChamferSlider : HSlider
    {
        /// <summary>手柄设计尺寸（高 22 ≤ 设置行高，不外溢压到相邻行）。</summary>
        private const float HandleWidth = 14.0f;

        private const float HandleHeight = 22.0f;
        private const float HandleChamfer = 4.0f;

        /// <summary>几何缓冲：切角点集 + 闭合描边用的「首点收尾」副本（_Draw 期复用，免逐帧分配）。</summary>
        private readonly Vector2[] _pts = new Vector2[8];

        private readonly Vector2[] _loop = new Vector2[9];
        private Vector2 _grabberSize;
        private bool _hovered;
        private bool _dragging;

        public override void _Ready()
        {
            // 按需重绘：值/尺寸/交互态变化才重画（本仓库的按需重绘纪律）
            ValueChanged += _ => QueueRedraw();
            Resized += QueueRedraw;
            MouseEntered += () => SetHovered(true);
            MouseExited += () => SetHovered(false);
            FocusEntered += QueueRedraw;
            FocusExited += QueueRedraw;
            DragStarted += () => SetDragging(true);
            DragEnded += _ => SetDragging(false);
        }

        public override void _Draw()
        {
            var grabber = GrabberSize();
            var size = new Vector2(Mathf.Max(HandleWidth, grabber.X), Mathf.Max(HandleHeight, grabber.Y));
            var chamfer = Mathf.Min(HandleChamfer, Mathf.Min(size.X, size.Y) * 0.5f);
            if (!FillChamferPoints(_pts, size, chamfer))
            {
                return; // 控件被压到放不下切角：整块不画（不画半块手柄，也不报错）
            }

            var origin = HandleCenter(grabber) - size * 0.5f;
            for (var i = 0; i < _pts.Length; i++)
            {
                _pts[i] += origin;
                _loop[i] = _pts[i];
            }

            _loop[_pts.Length] = _pts[0]; // DrawPolyline 不自动闭合：末点回到首点
            var focused = HasFocus();
            DrawColoredPolygon(_pts, HandleFill());
            DrawPolyline(_loop, focused ? AccentHot : AccentDim, focused ? 2.0f : 1.0f, true);
        }

        /// <summary>手柄中心：算式逐项对齐引擎绘制 grabber 的那一份（`Slider::_notification`）——
        /// 比例、`center_grabber` 与 `grabber_offset` 三处若与引擎不同源，自绘手柄就会与引擎的
        /// 点击映射（同一套几何）错开若干像素。</summary>
        private Vector2 HandleCenter(Vector2 grabber)
        {
            var ratio = (float)GetAsRatio();
            var centered = GetThemeConstant("center_grabber") != 0;
            var offset = (float)GetThemeConstant("grabber_offset");
            var x = centered
                ? ratio * Size.X
                : ratio * (Size.X - grabber.X) + grabber.X * 0.5f;
            return new Vector2(x, Size.Y * 0.5f + offset);
        }

        /// <summary>引擎那一枚 grabber 的尺寸（三态图标取逐分量最大——悬停/置灰时引擎换图标，
        /// 自绘手柄须把两枚都盖住）。主题运行期不更换，故缓存在实例字段（不缓存 Godot 对象：
        /// 静态持有引擎对象会在退出期触发 native 触碰崩溃）。</summary>
        private Vector2 GrabberSize()
        {
            if (_grabberSize != Vector2.Zero)
            {
                return _grabberSize;
            }

            var normal = GetThemeIcon("grabber")?.GetSize() ?? Vector2.Zero;
            var hot = GetThemeIcon("grabber_highlight")?.GetSize() ?? Vector2.Zero;
            var off = GetThemeIcon("grabber_disabled")?.GetSize() ?? Vector2.Zero;
            _grabberSize = new Vector2(
                Mathf.Max(normal.X, Mathf.Max(hot.X, off.X)),
                Mathf.Max(normal.Y, Mathf.Max(hot.Y, off.Y)));
            return _grabberSize;
        }

        private Color HandleFill()
        {
            if (!Editable)
            {
                return new Color(SteelTint, 0.4f); // 置灰口径与按钮一致
            }

            if (_dragging)
            {
                return SteelTintPressed;
            }

            return _hovered ? SteelTintHover : SteelTint;
        }

        private void SetHovered(bool value)
        {
            _hovered = value;
            QueueRedraw();
        }

        private void SetDragging(bool value)
        {
            _dragging = value;
            QueueRedraw();
        }
    }

    /// <summary>滑杆统一入口：切角手柄（三态 + 焦点环）+ 深槽轨道 + 琥珀进度条。
    /// 取值语义/步进/范围/回调一概不动——只换外观；拖动收尾补一枚切换音（松手即落值＝一次状态变化，
    /// 与 OptionButton 选项落定、LineEdit 进入编辑态同一个音）。</summary>
    public static void ApplySlider(HSlider slider)
    {
        slider.AddThemeStyleboxOverride("slider", MakeSliderTrackStyle(SlotDark, Accent));
        slider.AddThemeStyleboxOverride("grabber_area", MakeSliderTrackStyle(new Color(Accent, 0.80f), Accent));
        slider.AddThemeStyleboxOverride("grabber_area_highlight", MakeSliderTrackStyle(new Color(AccentHot, 0.92f), AccentHot));
        slider.DragEnded += _ => PlayUiSfx(SfxId.UiToggle);
    }

    /// <summary>滑杆槽轨/进度条样式：深槽 + 1px 边，纵向内边距即槽高（10px）。</summary>
    private static StyleBoxFlat MakeSliderTrackStyle(Color bg, Color border)
    {
        var style = new StyleBoxFlat { BgColor = bg, BorderColor = new Color(border, 0.5f) };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        style.ContentMarginTop = 5.0f;
        style.ContentMarginBottom = 5.0f;
        return style;
    }

    /// <summary>下拉控件（OptionButton）主题化：与按钮同一套钢板（normal/hover/pressed/disabled
    /// 三态齐全）+ 既有焦点描边环；弹出的 PopupMenu 一并收编——引擎默认弹窗是冷灰直角面板，
    /// 且它是独立 Window，不套皮时下拉一打开就露出另一套视觉语言。</summary>
    public static void ApplyOptionButton(OptionButton option)
    {
        option.AddThemeStyleboxOverride("normal", MakeBtnStyle(SteelTint));
        option.AddThemeStyleboxOverride("hover", MakeBtnStyle(SteelTintHover));
        option.AddThemeStyleboxOverride("pressed", MakeBtnStyle(SteelTintPressed, inset: true));
        option.AddThemeStyleboxOverride("hover_pressed", MakeBtnStyle(SteelTintPressed, inset: true));
        option.AddThemeStyleboxOverride("disabled", MakeBtnStyle(new Color(SteelTint, 0.4f)));
        option.AddThemeStyleboxOverride("focus", MakeFocusRing());
        option.AddThemeFontOverride("font", Font);
        option.AddThemeFontSizeOverride("font_size", FontBody);
        option.AddThemeColorOverride("font_color", Text);
        option.AddThemeColorOverride("font_hover_color", TextOnBright);
        option.AddThemeColorOverride("font_pressed_color", Text);
        option.AddThemeColorOverride("font_hover_pressed_color", Text);
        option.AddThemeColorOverride("font_focus_color", Text);
        option.AddThemeColorOverride("font_disabled_color", new Color(TextDim, 0.5f));
        ApplyPopupMenu(option.GetPopup());
        // 交互音：下拉也是按钮（悬停/按下走 ApplyOptionButton 自己的样式盒），选项落定另发切换音
        AttachButtonSfx(option);
        option.ItemSelected += _ => PlayUiSfx(SfxId.UiToggle);
    }

    /// <summary>下拉弹窗：金属面板底（内边距压回引擎默认的 4px——套皮不该改弹窗的行距与内缩）
    /// + 琥珀 16% 悬停行 + 与页内同源的字号与字色。</summary>
    private static void ApplyPopupMenu(PopupMenu? menu)
    {
        if (menu == null)
        {
            return;
        }

        var panel = MakeBtnStyle(PanelSteelTint);
        panel.SetContentMarginAll(4.0f);
        menu.AddThemeStyleboxOverride("panel", panel);
        var hover = new StyleBoxFlat { BgColor = new Color(Accent, 0.16f) };
        hover.SetContentMarginAll(4.0f);
        menu.AddThemeStyleboxOverride("hover", hover);
        menu.AddThemeFontOverride("font", Font);
        menu.AddThemeFontSizeOverride("font_size", FontBody);
        menu.AddThemeColorOverride("font_color", Text);
        menu.AddThemeColorOverride("font_hover_color", AccentHot);
        menu.AddThemeColorOverride("font_disabled_color", new Color(TextDim, 0.5f));
        menu.AddThemeColorOverride("font_accelerator_color", TextDim);
    }

    /// <summary>输入框（LineEdit）主题化：常态暗钢槽 / 悬停提亮一档 / 焦点补暖琥珀 2px 环
    ///（引擎的 LineEdit 聚焦时是「normal 之上叠画 focus」，故焦点环沿用按钮那枚空心环，
    /// 输入槽底色不丢）。悬停态引擎主题里没有对应样式盒（只有 normal/focus/read_only），
    /// 故用进出事件在 normal 上换档——三态由此齐全。</summary>
    public static void ApplyLineEdit(LineEdit edit)
    {
        var normal = MakeBtnStyle(PanelSteelTint);
        var hover = MakeBtnStyle(SteelTint);
        edit.AddThemeStyleboxOverride("normal", normal);
        edit.AddThemeStyleboxOverride("read_only", MakeBtnStyle(new Color(PanelSteelTint, 0.6f)));
        edit.AddThemeStyleboxOverride("focus", MakeFocusRing());
        edit.AddThemeFontOverride("font", Font);
        edit.AddThemeFontSizeOverride("font_size", FontBody);
        edit.AddThemeColorOverride("font_color", Text);
        edit.AddThemeColorOverride("font_uneditable_color", new Color(TextDim, 0.6f));
        edit.AddThemeColorOverride("font_placeholder_color", new Color(TextDim, 0.75f));
        edit.AddThemeColorOverride("font_selected_color", TextOnBright);
        edit.AddThemeColorOverride("caret_color", AccentHot);
        edit.AddThemeColorOverride("selection_color", new Color(Accent, 0.35f));
        edit.MouseEntered += () => edit.AddThemeStyleboxOverride("normal", hover);
        edit.MouseExited += () => edit.AddThemeStyleboxOverride("normal", normal);
        // 进入编辑态＝一次状态变化，与下拉选项落定、滑杆松手同一个音（无文案，纯听觉反馈）
        edit.FocusEntered += () => PlayUiSfx(SfxId.UiToggle);
    }

    // ---------------- 界面音效（只经既有播放通道；音量受设置页「音效」滑杆控制） ----------------

    /// <summary>界面音效统一出口：走 <see cref="GameState.PlaySfx"/>（既有 SFX 总线 + 目录表口径，
    /// 音量/最小间隔/复音全在 SfxPlayer 的目录表里，不新增播放通道）。autoload 不可用
    ///（退出期/场景重载的非常规时序）时静默——UI 反馈不该把异常抛给调用方。</summary>
    public static void PlayUiSfx(SfxId id) => GameState.TryGetInstance()?.PlaySfx(id);

    /// <summary>置灰按钮的受阻反馈：引擎在 `_gui_input` 里对 disabled 直接早退，按钮信号
    ///（pressed/button_down…）一个都不发，但 **gui_input 信号在其之前投递**（`Control::_call_gui_input`），
    /// 受阻动作（RP 不足 / 槽位满 / 领奖条件未达）的听觉反馈只能从这里出——没有它，
    /// 灰按钮按下去是全静音的，玩家分不清「按不动」与「没反应」。</summary>
    private static void PlayDenyIfDisabled(Button button, InputEvent @event)
    {
        if (!button.Disabled)
        {
            return;
        }

        var pressed = @event switch
        {
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } => true,
            _ => @event.IsActionPressed("ui_accept"),
        };
        if (pressed)
        {
            PlayUiSfx(SfxId.UiDeny);
        }
    }

    /// <summary>按钮交互音：悬停/焦点 = 轻点（hover 音自带 80ms 最小间隔，扫过一排按钮不会连发），
    /// 按下 = 确认；切换类按钮（ToggleMode）改发切换音——同一按钮不会被两种「按下」语义同时覆盖。
    /// meta 守卫保证幂等：一个按钮可能先后经过 ApplyButton 与 MakeButton 两条套皮路径。</summary>
    public static void AttachButtonSfx(Button button)
    {
        if (button.HasMeta(ButtonSfxMeta))
        {
            return;
        }

        button.SetMeta(ButtonSfxMeta, true);
        button.MouseEntered += () => PlayUiSfx(SfxId.UiHover);
        button.FocusEntered += () => PlayUiSfx(SfxId.UiHover);
        button.ButtonDown += () => PlayUiSfx(button.ToggleMode ? SfxId.UiToggle : SfxId.UiConfirm);
        button.GuiInput += @event => PlayDenyIfDisabled(button, @event);
    }

    /// <summary>套皮幂等标记（音效只挂一次）。</summary>
    private const string ButtonSfxMeta = "ui_sfx_hooked";

    /// <summary>面板打开微动效：200ms 淡入（不做位移动画——容器布局会覆盖 position）。</summary>
    public static void AnimateOpen(Control control)
    {
        control.Modulate = new Color(control.Modulate, 0.0f);
        var tween = control.CreateTween();
        tween.TweenProperty(control, "modulate:a", 1.0f, 0.2);
    }

    /// <summary>淡入（可延迟）：只动 modulate.a。容器布局会覆盖 position，故入场一律走透明度而非位移。</summary>
    public static void FadeIn(Control control, float time = 0.18f, float delay = 0.0f)
    {
        control.Modulate = new Color(control.Modulate, 0.0f);
        var tween = control.CreateTween();
        if (delay > 0.0f)
        {
            tween.TweenInterval(delay);
        }

        tween.TweenProperty(control, "modulate:a", 1.0f, time).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>淡出后回调（默认不隐藏，由调用方决定）。控件已失效时直接回调，不建 tween。</summary>
    public static void AnimateClose(Control control, float time = 0.16f, Action? onDone = null)
    {
        if (!GodotObject.IsInstanceValid(control))
        {
            onDone?.Invoke();
            return;
        }

        var tween = control.CreateTween();
        tween.TweenProperty(control, "modulate:a", 0.0f, time).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        if (onDone != null)
        {
            tween.TweenCallback(Callable.From(onDone));
        }
    }

    /// <summary>模态退出编排：面板与遮罩同时淡出后隐藏根节点。
    /// 交互与输入在调用当帧立即断开（整棵子树鼠标命中摘除 + 停用输入处理），因此退场动画期间
    /// 不会截获已交还给下一层的输入——退回/暂停链的焦点交接保持同步，仅有视觉残影渐隐。
    /// 鼠标命中摘除必须递归：Viewport 命中先递归子节点、后判父级 mouse_filter，父级置 Ignore
    /// 只让父节点自己不返回，子按钮照旧被命中（而 Button 走 GUI 相位，不经被停用的
    /// _input/_unhandled_input）。退场结束（根已隐藏）时按快照还原，故各 Show*/重开路径
    /// 只需照旧复位根与遮罩即可。</summary>
    public static void AnimateModalClose(Node root, Control dim, Control panel, Action? onClosed = null)
    {
        if (!GodotObject.IsInstanceValid(root) || !GodotObject.IsInstanceValid(panel))
        {
            onClosed?.Invoke();
            return;
        }

        SuppressMouseInput(root);
        root.SetProcessInput(false);
        root.SetProcessUnhandledInput(false);

        var tw = panel.CreateTween().SetParallel(true);
        tw.TweenProperty(panel, "modulate:a", 0.0f, 0.15).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        var dimTw = dim.CreateTween();
        dimTw.TweenProperty(dim, "modulate:a", 0.0f, 0.15).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.Chain().TweenCallback(Callable.From(() =>
        {
            RestoreMouseInput(root);
            if (GodotObject.IsInstanceValid(root))
            {
                // 根可能是 CanvasLayer（无 Visible 属性，只有 visible 成员）或 Control——统一走属性名写入
                root.Set("visible", false);
            }

            onClosed?.Invoke();
        }));
    }

    /// <summary>退场期鼠标命中快照（存在被摘除子树的根节点 meta 上）。</summary>
    private const string MouseFilterSnapshotMeta = "modal_close_mouse_filter_snapshot";

    /// <summary>递归摘除子树鼠标命中（含根自身），原值按 (节点, 值) 平铺快照挂在根 meta 上。
    /// Control 没有「上一值」可查，而子树里装饰件多为 Ignore、按钮是 Stop——统一硬编码一个
    /// 默认值必然改坏另一类，故一律快照后还原。</summary>
    private static void SuppressMouseInput(Node root)
    {
        var snapshot = new Godot.Collections.Array();
        CollectMouseFilters(root, snapshot);
        if (snapshot.Count == 0)
        {
            // 空摘除＝退场仍可能截获点击（调用方把根节点传成了不含任何 Control 的容器）。
            // 无头下不崩不报错，只有这条日志能指认，故不静默放过。
            GD.PushWarning("InfiAir: AnimateModalClose 的根子树里没有任何 Control——退场期鼠标命中摘除是空操作");
            return;
        }

        root.SetMeta(MouseFilterSnapshotMeta, snapshot);
    }

    private static void CollectMouseFilters(Node node, Godot.Collections.Array snapshot)
    {
        if (node is Control control)
        {
            snapshot.Add(Variant.From(control));
            snapshot.Add((int)control.MouseFilter);
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        foreach (var child in node.GetChildren())
        {
            CollectMouseFilters(child, snapshot);
        }
    }

    /// <summary>还原退场前摘除的鼠标命中。快照中的节点可能已被重建（基地页/轮盘选项按次重建），
    /// 失效引用跳过——新节点保留构造函数给的默认值，不会被还原成上一批节点的残值。</summary>
    private static void RestoreMouseInput(Node root)
    {
        if (!GodotObject.IsInstanceValid(root) || !root.HasMeta(MouseFilterSnapshotMeta))
        {
            return;
        }

        var snapshot = root.GetMeta(MouseFilterSnapshotMeta).AsGodotArray();
        root.RemoveMeta(MouseFilterSnapshotMeta);
        for (var i = 0; i + 1 < snapshot.Count; i += 2)
        {
            if (snapshot[i].AsGodotObject() is Control control && GodotObject.IsInstanceValid(control))
            {
                control.MouseFilter = (Control.MouseFilterEnum)snapshot[i + 1].AsInt32();
            }
        }
    }

    /// <summary>一次性缩放冲击（pivot 居中）：入场/受激/数值变化时的「弹一下」。
    /// 只动 scale，不改布局；容器内控件缩放不参与布局计算，无回流。</summary>
    public static void PunchScale(Control control, float amount = 1.06f, float time = 0.14f)
    {
        if (!GodotObject.IsInstanceValid(control))
        {
            return;
        }

        control.PivotOffset = control.Size * 0.5f;
        var tween = control.CreateTween();
        tween.TweenProperty(control, "scale", new Vector2(amount, amount), time * 0.35f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(control, "scale", Vector2.One, time * 0.65f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
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
