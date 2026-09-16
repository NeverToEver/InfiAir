using System.Collections.Generic;

namespace InfiAir.Core;

/// <summary>
/// HUD 版式算式（纯逻辑，零 Godot 依赖）：各板块的落位与尺寸只在这里写一份，引擎侧 <c>Hud</c>
/// 只做取值与赋值。判据是**盒子之间的关系**（良构 / 相交 / 包含），不是字面量——改任一尺寸都会
/// 带动判据。理由：版式错（构件互压、越出背板、负宽盒）在无头冒烟里既不崩也不报错，
/// 截图探针又只兜渲染整体坏掉，只能靠这里的算式单测兜住。
/// <para>
/// 坐标口径：<see cref="AnchoredBox"/> 的四条边界就是 Control 的 offset_*（y 向下增、贴边为 0）。
/// 板块的原点由其锚决定（左下 / 右上 / 顶部居中），**跨锚的盒不可互比**，判据只在同一锚组内写。
/// </para>
/// </summary>
public static class HudLayout
{
    /// <summary>锚定版式盒＝一个 Control 的四条 offset：同一锚下的两个盒可直接比边界。</summary>
    public readonly record struct AnchoredBox(float OffsetLeft, float OffsetTop, float OffsetRight, float OffsetBottom)
    {
        public float Width => OffsetRight - OffsetLeft;

        public float Height => OffsetBottom - OffsetTop;
    }

    /// <summary>盒是否良构（正宽正高）：负宽/负高的盒会让相交与包含判定静默通过（把一条边界写反
    /// 时不崩也不报错），故所有版式判据先过这一关。</summary>
    public static bool IsWellFormed(AnchoredBox box) => box.Width > 0.0f && box.Height > 0.0f;

    /// <summary>两盒是否相交（共边不算相交，与 <see cref="BoxesOverlap"/> 同口径）。</summary>
    public static bool Intersects(AnchoredBox a, AnchoredBox b) =>
        a.OffsetLeft < b.OffsetRight && b.OffsetLeft < a.OffsetRight &&
        a.OffsetTop < b.OffsetBottom && b.OffsetTop < a.OffsetBottom;

    /// <summary>inner 是否落在 outer 之内（共边算落在之内）。</summary>
    public static bool Contains(AnchoredBox outer, AnchoredBox inner) =>
        inner.OffsetLeft >= outer.OffsetLeft && inner.OffsetRight <= outer.OffsetRight &&
        inner.OffsetTop >= outer.OffsetTop && inner.OffsetBottom <= outer.OffsetBottom;

    /// <summary>两纵向区间是否相交（边界相切不算相交）：只判纵向的场合用它（倒计时读数与背板的
    /// 「上下不许压」关系）；有横向概念的构件一律用 <see cref="Intersects"/> 判完整盒。</summary>
    public static bool BoxesOverlap(float aTop, float aHeight, float bTop, float bHeight)
        => aTop < bTop + bHeight && bTop < aTop + aHeight;

    // ---------------- Boss 头部板块（顶部居中锚）：背板包住血条带，逃跑倒计时压在背板之外 ----------------

    /// <summary>Boss 头部背板顶位（相对视野顶缘）。</summary>
    public const float BossPlateTop = 4.0f;

    /// <summary>背板高度：包住「名牌行 + 血条带 + 上下留白」。</summary>
    public const float BossPlateHeight = 88.0f;

    /// <summary>背板宽（名牌行与血条带各 600，背板左右各留 20 边）。</summary>
    public const float BossPlateWidth = 640.0f;

    /// <summary>名牌行与血条带的宽度（两者同宽同列，改一处必然带动另一处）。</summary>
    public const float BossBarWidth = 600.0f;

    /// <summary>背板上缘到名牌行上缘的留白。</summary>
    public const float BossNameRowInset = 8.0f;

    /// <summary>名牌行带高：血条整条按它下移。**这是预留量而非名牌控件的实际盒高**——名牌控件的
    /// 盒高由字形行盒 + 面板内边距决定（实测 46 = 行盒 30 + 内边距 16），比预留量高 16px，
    /// 故名牌底色压住血条带上沿 12px（名牌顶 12 + 46 = 58 &gt; 血条带顶 46）。此偏差是既成事实，
    /// 判据不假装两者不相交；修它要动像素（改下移量或名牌样式盒内边距），归人工定夺。</summary>
    public const float BossNameRowHeight = 30.0f;

    /// <summary>名牌行带与血条带之间的留白（仅对预留量成立，见上条）。</summary>
    public const float BossNameRowGap = 4.0f;

    /// <summary>血条带高（分段条本体 28 高）。</summary>
    public const float BossHealthBandHeight = 28.0f;

    /// <summary>倒计时读数行的盒高量级（<c>UITheme.FontHudL</c> 22 的 Label 实测行盒）。</summary>
    public const float BossCountdownLineHeight = 33.0f;

    /// <summary>倒计时读数行宽。</summary>
    public const float BossCountdownWidth = 200.0f;

    /// <summary>倒计时盒与背板下缘的留白：贴着边框会读成背板的一部分。</summary>
    public const float BossCountdownGap = 6.0f;

    /// <summary>背板下缘。</summary>
    public static float BossPlateBottom => BossPlateTop + BossPlateHeight;

    /// <summary>名牌行顶位（相对视野顶缘）。</summary>
    public static float BossNameRowTop => BossPlateTop + BossNameRowInset;

    /// <summary>血条带顶位：名牌行预留带 + 留白之后（名牌顶、血条带顶、背板底三者由这里的常量推出）。</summary>
    public static float BossHealthBandTop => BossNameRowTop + BossNameRowHeight + BossNameRowGap;

    /// <summary>血条带底位。</summary>
    public static float BossHealthBandBottom => BossHealthBandTop + BossHealthBandHeight;

    /// <summary>名牌与血条的左缘（顶部居中锚：以视野中线为 0，故为负）。</summary>
    public static float BossBarLeft => -BossBarWidth * 0.5f;

    /// <summary>名牌顶位在血条局部坐标下的值：名牌挂为血条子节点，写的是血条局部坐标
    /// （负值＝在血条带上方）。</summary>
    public static float BossNameRowTopInBar => BossNameRowTop - BossHealthBandTop;

    /// <summary>逃跑倒计时标签的顶位：恒在背板下缘之外（含留白）——读数不许压在背板边框上。</summary>
    public static float BossCountdownTop => BossPlateBottom + BossCountdownGap;

    /// <summary>背板盒：血条带必须落在它之内。</summary>
    public static AnchoredBox BossPlateBox =>
        new(-BossPlateWidth * 0.5f, BossPlateTop, BossPlateWidth * 0.5f, BossPlateBottom);

    /// <summary>血条带盒。</summary>
    public static AnchoredBox BossHealthBarBox =>
        new(-BossBarWidth * 0.5f, BossHealthBandTop, BossBarWidth * 0.5f, BossHealthBandBottom);

    /// <summary>逃跑倒计时盒（背板下缘之外，故与背板、血条带都不相交）。</summary>
    public static AnchoredBox BossCountdownBox => new(
        -BossCountdownWidth * 0.5f,
        BossCountdownTop,
        BossCountdownWidth * 0.5f,
        BossCountdownTop + BossCountdownLineHeight);

    // ---------------- 左下集成仪表盘（§1.9.1）：446×136 单面板，上排生命 / 下排仪表带 ----------------

    /// <summary>面板左缘与底缘（左下角锚）。</summary>
    public const float InstrumentPanelLeft = 10.0f;

    public const float InstrumentPanelBottom = 14.0f;

    /// <summary>面板尺寸（改宽高即带动面板内全部构件的包含判定）。</summary>
    public const float InstrumentPanelWidth = 446.0f;

    public const float InstrumentPanelHeight = 136.0f;

    /// <summary>面板内左右留白：上排生命条左缘、下排首件左缘、横向分隔线左右缘共用同一条内边线。</summary>
    public const float InstrumentEdgeInset = 14.0f;

    /// <summary>上下排之间的横向分隔线厚（分区结构感）。</summary>
    public const float InstrumentDividerHeight = 1.0f;

    /// <summary>面板盒（由左缘/底缘与尺寸推出；面板内所有构件都必须落在它之内）。</summary>
    public static AnchoredBox InstrumentPanelBox => new(
        InstrumentPanelLeft,
        -InstrumentPanelBottom - InstrumentPanelHeight,
        InstrumentPanelLeft + InstrumentPanelWidth,
        -InstrumentPanelBottom);

    /// <summary>上排：生命分段条（主读数；右段让给数值读数）。</summary>
    public static AnchoredBox HpBarBox => new(
        InstrumentPanelLeft + InstrumentEdgeInset, -144.0f, 300.0f, -116.0f);

    /// <summary>上排：生命数值读数（与分段条同排，不许压到条上）。</summary>
    public static AnchoredBox LivesLabelBox => new(312.0f, -146.0f, 436.0f, -112.0f);

    /// <summary>上下排之间的分隔线：横跨面板内宽。</summary>
    public static AnchoredBox InstrumentDividerBox => new(
        InstrumentPanelLeft + InstrumentEdgeInset,
        -112.0f,
        InstrumentPanelBox.OffsetRight - InstrumentEdgeInset,
        -112.0f + InstrumentDividerHeight);

    /// <summary>下排三件仪表的上下基线（燃料量槽与两枚充能槽同高，共用一条基线）。</summary>
    public const float InstrumentRowTop = -104.0f;

    public const float InstrumentRowBottom = -52.0f;

    /// <summary>燃料量槽宽（窄高量槽，液位即读数）。</summary>
    public const float FuelTankWidth = 34.0f;

    /// <summary>充能槽宽（环 + 字形；两枚同构件，只换字形与身份色）。</summary>
    public const float SocketWidth = 48.0f;

    /// <summary>下排三件仪表的件间距（改小到重叠即被面板判据抓住）。</summary>
    public const float SocketGap = 8.0f;

    /// <summary>燃料量槽（下排首件）。</summary>
    public static AnchoredBox FuelTankBox => new(
        InstrumentPanelLeft + InstrumentEdgeInset,
        InstrumentRowTop,
        InstrumentPanelLeft + InstrumentEdgeInset + FuelTankWidth,
        InstrumentRowBottom);

    /// <summary>第 index 枚充能槽的左缘（0＝冲刺、1＝弹反）：由前一件右缘 + 间距推出，
    /// 故三件共用一条基线时也压不到彼此（间距改成负数即红）。</summary>
    public static float SocketLeft(int index) =>
        InstrumentPanelLeft + InstrumentEdgeInset + FuelTankWidth + SocketGap + (index * (SocketWidth + SocketGap));

    /// <summary>第 index 枚充能槽。</summary>
    public static AnchoredBox SocketBox(int index) =>
        new(SocketLeft(index), InstrumentRowTop, SocketLeft(index) + SocketWidth, InstrumentRowBottom);

    /// <summary>仪表小标题的行盒高（<c>UITheme.FontSmall</c> 16 的 Label 实测行盒，原实现按 16 留位）。</summary>
    public const float GaugeCaptionHeight = 24.0f;

    /// <summary>仪表小标题贴在其仪表底边之下的留白。</summary>
    public const float GaugeCaptionGap = 2.0f;

    /// <summary>仪表小标题盒：贴在其仪表正下方、宽与仪表对齐——由该仪表的盒推出，改仪表宽即跟着动。</summary>
    public static AnchoredBox CaptionBox(AnchoredBox gauge) => new(
        gauge.OffsetLeft,
        gauge.OffsetBottom + GaugeCaptionGap,
        gauge.OffsetRight,
        gauge.OffsetBottom + GaugeCaptionGap + GaugeCaptionHeight);

    /// <summary>弹仓格（母舰驻留时才显；与仪表带同排，落在两枚充能槽右侧）。</summary>
    public static AnchoredBox MagStripBox => new(184.0f, -90.0f, 284.0f, -76.0f);

    /// <summary>坞态指示灯（16×16 枚举灯，弹仓格右侧）。</summary>
    public static AnchoredBox DockLampBox => new(300.0f, -98.0f, 316.0f, -82.0f);

    /// <summary>坞态文本（灯右侧；与仪表带同排，不与三件仪表压字）。</summary>
    public static AnchoredBox DockTagBox => new(322.0f, -104.0f, 444.0f, -76.0f);

    /// <summary>面板内全部构件盒（上排、分隔线、下排仪表与三枚小标题）：版式判据的枚举面——
    /// 单测按它断「良构 + 全在面板内 + 两两不相交」；新增构件不登记进这里就落在判据之外。</summary>
    public static readonly IReadOnlyList<AnchoredBox> InstrumentPanelItems = new[]
    {
        HpBarBox,
        LivesLabelBox,
        InstrumentDividerBox,
        FuelTankBox,
        SocketBox(0),
        SocketBox(1),
        CaptionBox(FuelTankBox),
        CaptionBox(SocketBox(0)),
        CaptionBox(SocketBox(1)),
        MagStripBox,
        DockLampBox,
        DockTagBox,
    };

    /// <summary>上排（生命主读数）。</summary>
    public static readonly IReadOnlyList<AnchoredBox> InstrumentTopRowItems = new[] { HpBarBox, LivesLabelBox };

    /// <summary>下排（仪表带 + 坞态灯与文本 + 三枚小标题）：与上排之间必须留出分隔线那一行——
    /// 两排挤在一起会读成一排。</summary>
    public static readonly IReadOnlyList<AnchoredBox> InstrumentBottomRowItems = new[]
    {
        FuelTankBox,
        SocketBox(0),
        SocketBox(1),
        CaptionBox(FuelTankBox),
        CaptionBox(SocketBox(0)),
        CaptionBox(SocketBox(1)),
        MagStripBox,
        DockLampBox,
        DockTagBox,
    };

    // ---------------- 右上角：难度块与其下的缓存芯片列（§4.2） ----------------

    /// <summary>难度背板（右缘 10px、宽 390：须装下「难度 x2.50 · 第四档 · 危险 · 中」三段文案）。</summary>
    public static AnchoredBox DifficultyPlateBox => new(-400.0f, 24.0f, -10.0f, 68.0f);

    /// <summary>难度读数在背板内左右各留的边。</summary>
    public const float DifficultyLabelInset = 12.0f;

    /// <summary>难度读数盒（与背板同高、上下不出边）。</summary>
    public static AnchoredBox DifficultyLabelBox => new(
        DifficultyPlateBox.OffsetLeft + DifficultyLabelInset,
        DifficultyPlateBox.OffsetTop,
        DifficultyPlateBox.OffsetRight - DifficultyLabelInset,
        DifficultyPlateBox.OffsetBottom);

    /// <summary>缓存列的右缘留白：芯片、悬停提示、可升级提示、目标行右对齐同一列。</summary>
    public const float CacheColumnInset = 20.0f;

    /// <summary>芯片与目标行的列宽（两者同宽同列）。</summary>
    public const float CacheColumnWidth = 172.0f;

    /// <summary>小字单行行盒高（<c>UITheme.FontSmall</c> 16 的 Label 实测行盒）：这几行都不给固定
    /// 高度、由字形撑开，判据按实测行盒留位——改字号须同步这里，否则留位小于字形就会静默互压。</summary>
    public const float CacheLineHeight = 24.0f;

    /// <summary>芯片顶位：难度块（下缘 68）之下，不许压到难度读数。</summary>
    public const float CacheChipTop = 118.0f;

    public const float CacheChipHeight = 56.0f;

    /// <summary>悬停提示顶位（芯片下缘 174 之下）。</summary>
    public const float CacheTooltipTop = 178.0f;

    public const float CacheTooltipWidth = 340.0f;

    /// <summary>「可升级」提示顶位：让开悬停提示那一行（两者可同屏）。</summary>
    public const float CacheHintTop = 212.0f;

    /// <summary>目标进度行顶位（芯片列最下一行，常驻）。</summary>
    public const float CacheGoalTop = 236.0f;

    /// <summary>右对齐行：右缘钉在列右缘、向左展开给定宽度。</summary>
    private static AnchoredBox RightAlignedRow(float top, float width, float height) =>
        new(-CacheColumnInset - width, top, -CacheColumnInset, top + height);

    /// <summary>缓存芯片盒（点数 + 状态光晕）。</summary>
    public static AnchoredBox CacheChipBox => RightAlignedRow(CacheChipTop, CacheColumnWidth, CacheChipHeight);

    /// <summary>悬停提示盒（点数与衰减说明，比列宽更宽但同右缘）。</summary>
    public static AnchoredBox CacheTooltipBox => RightAlignedRow(CacheTooltipTop, CacheTooltipWidth, CacheLineHeight);

    /// <summary>「可升级」提示盒。</summary>
    public static AnchoredBox CacheHintBox => RightAlignedRow(CacheHintTop, CacheColumnWidth, CacheLineHeight);

    /// <summary>目标进度行盒（向左生长，右缘与列同）。</summary>
    public static AnchoredBox CacheGoalBox => RightAlignedRow(CacheGoalTop, CacheColumnWidth, CacheLineHeight);

    /// <summary>缓存列四行：判据断「两两不相交 + 右缘对齐同一列 + 不越出右缘」。</summary>
    public static readonly IReadOnlyList<AnchoredBox> CacheColumnItems = new[]
    {
        CacheChipBox,
        CacheTooltipBox,
        CacheHintBox,
        CacheGoalBox,
    };

    // ---------------- 顶部居中横幅栈（警告横幅在上、信息横幅在下） ----------------

    /// <summary>两块横幅同宽同列（居中）。</summary>
    public const float BannerWidth = 600.0f;

    public const float WarningBannerTop = 140.0f;

    public const float WarningBannerHeight = 80.0f;

    /// <summary>两横幅之间的留白：两者可同屏（警告闪烁期间里程碑达成），叠字就读不出告警。</summary>
    public const float BannerStackGap = 12.0f;

    public const float InfoBannerHeight = 64.0f;

    /// <summary>居中行（顶部居中锚：x 以视野中线为 0）。</summary>
    private static AnchoredBox CenterTopRow(float top, float width, float height) =>
        new(-width * 0.5f, top, width * 0.5f, top + height);

    /// <summary>警告横幅（Boss 出场 / 弹仓见底）。</summary>
    public static AnchoredBox WarningBannerBox => CenterTopRow(WarningBannerTop, BannerWidth, WarningBannerHeight);

    /// <summary>信息横幅（母舰到达 / 里程碑 / 可升级）：紧接警告横幅之下。</summary>
    public static AnchoredBox InfoBannerBox =>
        CenterTopRow(WarningBannerBox.OffsetBottom + BannerStackGap, BannerWidth, InfoBannerHeight);
}
