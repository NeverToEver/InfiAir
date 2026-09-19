namespace InfiAir.Core;

/// <summary>
/// 机型轮盘铭牌版式（纯逻辑，零 Godot 依赖）：右区那一列的**全部纵向关系**只在这里写一份——
/// 页题 / 机体停驻位 / 铭牌上下缘 / 板内文本区与指纹图盒 / 板下 note 与提示行——引擎侧
/// <c>MachineWheelPanel</c> 只做取值与赋值。放大铭牌 = 加高板，而板往上长就会压到机体、
/// 往下长就会压到 note 与提示行：这两条都**不崩不报错**，只是难看，故全部落成判据
/// （见 <c>MachinePlateLayoutTests</c>：板顶与机体实心像素不相交、板底与 note / 提示行不相交、
/// 图表盒 + 文本区装得进板内、文本区装得下最长一行并留余量）。
///
/// 尺寸链（自上而下，每一环都是常量或常量算式，测试断言的是**关系**而不是另一份硬编码）：
/// <code>
/// 页题 16 … 页题下缘 58
///   + ShipGap 15
/// 机体实心像素 73 … 557（贴图 254×254 × 2 倍，停在 ShipRestY）
///   + ShipGap 15
/// 铭牌 572 … 969（高 397）→ 内缩 26 / 10 → 内容 361 = 文本区 108 + 图表盒 253
///   + NoteGap 12
/// note 981 … 1008（一行）
///   + NoteGap 12
/// 提示行 1020 … 1047（离屏幕下缘 33 ≥ MinBottomMargin 24）
/// </code>
///
/// **为什么板必须往上长**：板下缘被 note 与提示行封住（它们是玩家读到「选定即保存」与操作方式
/// 的唯一一行），故加高只能吃上面的空间；而上面是机体——板顶一旦越过机体实心像素，机尾就被
/// 板子切掉。板宽 560 不动（板心因此仍正对机体停驻位，见 <see cref="PlateLeft"/>）。
///
/// 实测口径（本文件里所有 px 常量都是**量出来的**，不是估的；字体 / 字号 / 贴图 / 文案表任一改动
/// 都可能让它们变，变了就得重量并回来改数）：
/// <list type="bullet">
/// <item>字体度量：<c>assets/fonts/NotoSansSC.ttf</c>，行高（ascent + descent）＝ FontSmall 16px → 24、
/// FontCaption 18px → 27、FontHeader 28px → 42。字号单源在引擎侧 <c>UITheme</c>（core 不引 Godot，
/// 故这里只写由字号推出的行高）。</item>
/// <item>贴图：六型 <c>assets/sprites/player_ship*.png</c> 一律 254×254（<see cref="ShipSpritePx"/>），
/// 常态帧里**实心像素**（alpha ≥ 200）的最浅行 ＝ 7（连弩的机首炮管贴到画布顶）、最深行 ＝ 249
/// （壁垒的尾喷口）；再往下只剩尾焰余辉（实测最大 alpha ＝ 18，约 7%），故判据取实心像素而非画布下缘。</item>
/// <item>文案：最长一行 ＝ 280px（巨像「Longest endurance, slowest cycles」，FontCaption 18px，
/// 中英两列取大者，见 <see cref="LongestRowPx"/>）。</item>
/// </list>
/// </summary>
public static class MachinePlateLayout
{
    // ---- 屏幕与机体（板顶判据的输入）----

    /// <summary>设计分辨率高度（判据「底部提示行不出屏」用）。</summary>
    public const double ScreenHeight = 1080.0;

    /// <summary>机体停驻位的 x（引擎侧 <c>MachineWheelPanel.ShipRest</c>）：板心与它对齐，
    /// 铭牌因此始终正对机体。</summary>
    public const double ShipCenterX = 1160.0;

    /// <summary>机体停驻位的 y。**加了指纹图之后上移了 107px**（原 420）：板要长高，而板下的
    /// note 与提示行封住了下缘，只能吃上方的空间——机体让位是这一批唯一的空间来源。</summary>
    public const double ShipRestY = 313.0;

    /// <summary>展示缩放（引擎侧 sprites 的 <c>Scale</c>，2 倍 ＝ 与标题屏悬挂展示同档）。
    /// 单源在这里而不是面板里：机体占多大地方是版式判据的输入，面板只取值。
    /// **改它会同时改特性演出的观感**——那些演出的局部偏移按 2 倍写死（见 <c>MachineWheelPanel</c>
    /// 的演出段注释），改这里须同批换算。</summary>
    public const double ShipDisplayScale = 2.0;

    /// <summary>机体贴图边长（六型共 254×254；单源是生成器 <c>generate_player_ship.py</c> 的头部注释）。</summary>
    public const double ShipSpritePx = 254.0;

    /// <summary>常态帧实心像素的最浅行（贴图坐标，y 向下；连弩的机首炮管贴到画布顶 ＝ 7）。</summary>
    public const double ShipHullTopPx = 7.0;

    /// <summary>常态帧实心像素的最深行（贴图坐标；壁垒的尾喷口 ＝ 249）。</summary>
    public const double ShipHullBottomPx = 249.0;

    /// <summary>机体实心像素的上缘（面板坐标）。</summary>
    public const double ShipHullTopY = ShipRestY + (ShipHullTopPx - ShipSpritePx / 2.0) * ShipDisplayScale;

    /// <summary>机体实心像素的下缘（面板坐标）：板顶的封顶依据。</summary>
    public const double ShipHullBottomY = ShipRestY + (ShipHullBottomPx - ShipSpritePx / 2.0) * ShipDisplayScale;

    /// <summary>机体与相邻元素之间的最小间距（页题下方、板顶上方各一处）。15px 是「贴不上、
    /// 也离不远」的下限——判据用它，观感留白由它保证。</summary>
    public const double ShipGap = 15.0;

    // ---- 页题 ----

    /// <summary>页题 y：压在右区顶带。原为 96——机体上移后顶带只剩最上一档，否则机体机首会压到题字。</summary>
    public const double TitleY = 16.0;

    /// <summary>页题行高（FontHeader 28px → 42）。</summary>
    public const double TitleRowPx = 42.0;

    // ---- 铭牌 ----

    /// <summary>板宽（不动）：板心恒对机体停驻位，加宽只加在左侧（见 <see cref="PlateLeft"/>）。
    /// 它现在由**指纹图盒 + 两侧留白**决定，不再由最长一行文案决定（最长一行只有 280px）。</summary>
    public const double PlateWidth = 560.0;

    /// <summary>板高：168 → **397**（两行数字换成一张六维指纹图 + 一行性格文案之后）。加高的行程
    /// 全部来自机体让位（<see cref="ShipRestY"/> 上移 107）与 note / 提示行让位（下移 115）。</summary>
    public const double PlateHeight = 397.0;

    /// <summary>板顶：机体实心像素下缘 + 间距。板往上长到它为止，再高就切到机尾。</summary>
    public const double PlateTop = ShipHullBottomY + ShipGap;

    /// <summary>板底。</summary>
    public const double PlateBottom = PlateTop + PlateHeight;

    /// <summary>板左上角 x＝板心对齐 <see cref="ShipCenterX"/>（只加宽不挪位会让板心右偏半个增量，
    /// 铭牌与机体错开——这正是加宽时容易漏掉的一步）。</summary>
    public const double PlateLeft = ShipCenterX - PlateWidth / 2.0;

    /// <summary>内容框左右内缩合计（引擎侧 vbox 的 <c>Position.X</c> 18 × 2）。</summary>
    public const double ContentInsetX = 36.0;

    /// <summary>内容框上内缩：让开板顶那道琥珀细线（线的 y 与厚度见引擎侧；这里是线下方 8px）。</summary>
    public const double PlateTopInset = 26.0;

    /// <summary>内容框下内缩（vbox 已按内容居中，这只是底缘留白）。</summary>
    public const double PlateBottomInset = 10.0;

    /// <summary>内容框宽度：任何一行文案都必须装得下它。</summary>
    public const double ContentWidth = PlateWidth - ContentInsetX;

    /// <summary>内容框高度：文本区 + 指纹图盒之和的上界。</summary>
    public const double ContentHeight = PlateHeight - PlateTopInset - PlateBottomInset;

    // ---- 板内文本区（现役/候选行 + 机型名行 + 性格文案行）----

    /// <summary>现役 / 候选标签行的行高（FontSmall 16px → 24，含 3px 余量）。</summary>
    public const double RoleRowPx = 27.0;

    /// <summary>机型名行（名字 FontHeader 28px + 性格标签 FontSmall 16px 同一行）的行高：
    /// 42 → 45（含 3px 余量）。</summary>
    public const double NameRowPx = 45.0;

    /// <summary>性格文案行的行高（FontCaption 18px → 27，含 3px 余量）。</summary>
    public const double BlurbRowPx = 30.0;

    /// <summary>行间距（引擎侧 vbox 的 <c>separation</c>）。</summary>
    public const double RowSeparation = 2.0;

    /// <summary>文本区高度（三行 + 两道行距）：指纹图盒拿不走的固定开销。</summary>
    public const double TextBoxHeight = RoleRowPx + NameRowPx + BlurbRowPx + RowSeparation * 3.0;

    // ---- 板内指纹图盒（正方形，含轴标签留白）----

    /// <summary>
    /// 指纹图盒边长（正方形）：内容框减掉文本区剩下的全部高度都给它——图是这一页的主角，
    /// 板加高加出来的空间应当全部落在图上（这也就是为什么文本区要按「够用即可」定高）。
    /// </summary>
    public const double ChartBox = ContentHeight - TextBoxHeight;

    /// <summary>
    /// 轴标签留白（每侧）：轴标签是六边形顶点**之外**的字，图盒必须把字也包进来，否则标签会画到
    /// 板外（切掉一半的字不报错）。留白 ＝ 标签中心距顶点 15px + 半个行高 12px ＝ 27，取 30 留 3px。
    /// 引擎侧控件的标签间距必须 ≤ 本值 − 半行高，否则标签出界（控件自己还会把标签矩形钳进边界，
    /// 本常量管的是「钳不到时才真出界」这一层）。
    /// </summary>
    public const double ChartLabelPad = 30.0;

    /// <summary>指纹图半径：图盒减掉两侧标签留白后的一半。</summary>
    public const double ChartRadius = (ChartBox - ChartLabelPad * 2.0) / 2.0;

    // ---- 板下两行（note 与操作提示）----

    /// <summary>板与 note、note 与提示行之间的间距。</summary>
    public const double NoteGap = 12.0;

    /// <summary>note 行高（FontCaption 18px → 27）。</summary>
    public const double NoteRowPx = 27.0;

    /// <summary>note 的行数上限：**一行**。note 说「选定即保存 / 继续上次出击按存档机型」，
    /// 两行就会顶到提示行（板下空间是按 12 + 27 + 12 封顶算的）。文案改长 = 这里红。</summary>
    public const int NoteMaxLines = 1;

    /// <summary>note 宽度（引擎侧 Label 的 <c>CustomMinimumSize.X</c>，居中在板心）：文案要装得下它。
    /// 比板宽出 100px 各一侧——note 是板下的一行辅助说明，不跟着板宽走（板宽由指纹图盒定）。</summary>
    public const double NoteWidthPx = PlateWidth + 200.0;

    /// <summary>note 的 y。</summary>
    public const double NoteY = PlateBottom + NoteGap;

    /// <summary>note 的最长一行像素宽（中英两列取大者；口径同 <see cref="LongestRowPx"/>）。</summary>
    public const double LongestNotePx = 680.0;

    /// <summary>操作提示行的行高（FontCaption 18px → 27）。</summary>
    public const double HintRowPx = 27.0;

    /// <summary>操作提示行的 y：note 下方一个间距。它是页内最靠下的一行，故由它判「不出屏」。</summary>
    public const double HintY = NoteY + NoteRowPx * NoteMaxLines + NoteGap;

    /// <summary>提示行与屏幕下缘之间的最小余量（低于它读起来就是贴边了）。</summary>
    public const double MinBottomMargin = 24.0;

    // ---- 文本适配 ----

    /// <summary>实测的最长一行像素宽（中英两列取大者，口径见类注释）。它是**观感留白的下界依据**，
    /// 不是运行时读数：字体 / 字号 / 文案表任一改动都可能让它变，变了就得重量并回来改这个数。</summary>
    public const double LongestRowPx = 280.0;

    /// <summary>一行文案与内容框之间要求的最小余量。差值为零或很小说明「刚好装下」，
    /// 字体度量微差或再补一个字就会开始折行，而折行不报错。</summary>
    public const double MinRowMarginPx = 40.0;
}
