namespace InfiAir.Core;

/// <summary>
/// HUD 版式算式（纯逻辑，零 Godot 依赖）：Boss 头部板块的纵向堆叠——背板包住名牌行与血条带，
/// 逃跑倒计时读数压在背板下缘之外。引擎侧 <c>Hud</c> 只做取值与赋值。
/// 下沉的理由：倒计时原先写死 y=78，而背板是 y 4..92，数字下半截越过背板下边框；「两个盒子
/// 相交」这类布局错在无头冒烟里既不崩也不报错（结算页 dim 只压暗、不清除），
/// 只能靠布局算式单测兜住——故背板与倒计时的顶位都由这里推出，改一处必然带动另一处。
/// </summary>
public static class HudLayout
{
    /// <summary>Boss 头部背板顶位（相对视野顶缘；与血条同属顶部居中锚）。</summary>
    public const float BossPlateTop = 4.0f;

    /// <summary>背板高度：包住「名牌行 + 血条带 + 上下留白」（名牌行与血条带的高由场景偏移决定，
    /// 这里只钉住总高，避免两处各存一份带高）。</summary>
    public const float BossPlateHeight = 88.0f;

    /// <summary>倒计时读数行的盒高量级（字号 <c>UITheme.FontHudL</c> 22 的行盒偏高取整）。</summary>
    public const float BossCountdownLineHeight = 30.0f;

    /// <summary>倒计时盒与背板下缘的留白：贴着边框会读成背板的一部分。</summary>
    public const float BossCountdownGap = 6.0f;

    /// <summary>背板下缘。</summary>
    public static float BossPlateBottom => BossPlateTop + BossPlateHeight;

    /// <summary>逃跑倒计时标签的顶位：恒在背板下缘之外（含留白）——读数不许压在背板边框上。</summary>
    public static float BossCountdownTop => BossPlateBottom + BossCountdownGap;

    /// <summary>两纵向盒子是否相交（边界相切不算相交）。布局算式唯一的判据：单测断的是
    /// 「倒计时盒与背板不相交」，不是某个字面量——盒子尺寸或字体行高变化时这里即红。</summary>
    public static bool BoxesOverlap(float aTop, float aHeight, float bTop, float bHeight)
        => aTop < bTop + bHeight && bTop < aTop + aHeight;
}
