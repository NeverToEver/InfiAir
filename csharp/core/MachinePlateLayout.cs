namespace InfiAir.Core;

/// <summary>
/// 机型轮盘铭牌版式（纯逻辑，零 Godot 依赖）：板的尺寸与板心落位只在这里写一份，引擎侧
/// <c>MachineWheelPanel</c> 只做取值与赋值。
/// <para>
/// 判据落在两处「改了尺寸常量既不崩也不报错」的静默坏法上：**内容框装不下最长一行**（超宽串
/// 会被 Label 折行或裁掉，而折行会把四行版式挤成五行、溢出板外）与**板心偏离机体停驻位**
/// （铭牌与机体错开，纯观感问题，任何自动判据都看不见）。最长行的宽度用字体实测，
/// 是这条判据里唯一的「外部读数」——字体或字号一改必须重量（见 <see cref="LongestRowPx"/>）。
/// </para>
/// </summary>
public static class MachinePlateLayout
{
    /// <summary>机体停驻位的 x（引擎侧 <c>MachineWheelPanel.ShipRest</c>）：板心与它对齐，
    /// 铭牌因此始终正对机体。</summary>
    public const double ShipCenterX = 1160.0;

    /// <summary>板宽。**460 → 560**（能力项并入加成 / 代价两行之后加宽）：最长的英文行
    /// （壁垒「Damage taken -15% · Parry window 0.70s」，FontBody 24px、`assets/fonts/NotoSansSC.ttf`）
    /// 实测 446px，已贴死旧内容框的 424px；560 让内容框到 524px、余量约 78px。
    /// 宽度只加在左侧（见 <see cref="PlateLeft"/>），板心不动。</summary>
    public const double PlateWidth = 560.0;

    /// <summary>板高（不动）：加高会把下方 note 与提示行挤掉，且四行版式本来就有余量。</summary>
    public const double PlateHeight = 168.0;

    /// <summary>内容框左右内缩合计（引擎侧 vbox 的 <c>Position.X</c> 18 × 2）。</summary>
    public const double ContentInsetX = 36.0;

    /// <summary>内容框宽度：任何一行文案都必须装得下它。</summary>
    public const double ContentWidth = PlateWidth - ContentInsetX;

    /// <summary>板左上角 x＝板心对齐 <see cref="ShipCenterX"/>（只加宽不挪位会让板心右偏半个增量，
    /// 铭牌与机体错开——这正是加宽时容易漏掉的一步）。</summary>
    public const double PlateLeft = ShipCenterX - PlateWidth / 2.0;

    /// <summary>实测的最长一行像素宽（中英两列取大者，口径见 <see cref="PlateWidth"/>）。
    /// 它是**观感留白的下界依据**，不是运行时读数：字体 / 字号 / 文案表任一改动都可能让它变，
    /// 变了就得重量并回来改这个数。</summary>
    public const double LongestRowPx = 446.0;

    /// <summary>一行文案与内容框之间要求的最小余量。差值为零或很小说明「刚好装下」，
    /// 字体度量微差或再补一个字就会开始折行，而折行不报错。</summary>
    public const double MinRowMarginPx = 40.0;
}
