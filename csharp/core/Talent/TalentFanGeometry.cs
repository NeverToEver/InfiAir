namespace InfiAir.Core.Talent;

/// <summary>
/// 天赋面板右区扇形几何（纯逻辑，零 Godot 依赖）：装配尺寸与卡位收口的唯一来源。
///
/// 宿主（`TalentPanel` 的控件装配）与构件（`TalentFanView` 的布局域、卡片边长）都从这里取值，
/// 单测也以同一组数当定义域——三处各写一份的结果是测试在一个实机不存在的宽度上验证
/// （曾发生：布局自持宽 1240 而控件装配宽 900，右端卡越界压进详情卡、点击被详情卡吃掉，
/// 而无头冒烟与截图探针都不判段界，测试还在 1240 的定义域上判绿）。
///
/// 卡位收口：节点中心必须使**整张卡片**落在扇形视口内（[half, 视口 − half]）。扇形是径向展开，
/// 支线长短不一时足迹不对称，右端卡会越过视口右缘进入详情栏、左端卡会探出视口左缘——
/// 越界部分被详情卡遮挡（`ChamferedPanel` 的 `MouseFilter` 默认 Stop）即「看得见、点不到」。
/// </summary>
public static class TalentFanGeometry
{
    /// <summary>扇形视口宽（px，1080p 设计坐标）：TalentPanel 装配给 TalentFanView 的宽度。</summary>
    public const double ViewportWidth = 900.0;

    /// <summary>扇形视口高（px）。</summary>
    public const double ViewportHeight = 720.0;

    /// <summary>节点卡边长（px，正方形；TalentFanView 的卡片尺寸）。</summary>
    public const double CardSize = 128.0;

    /// <summary>详情卡左缘（px，同一局部坐标系的右区坐标）：扇形卡的右缘不得越过此线，
    /// 否则被详情卡压住并吃掉点击。</summary>
    public const double DetailColumnLeft = 910.0;

    /// <summary>按生产几何把节点中心钳进可用区（卡片完整落在视口内）。</summary>
    public static (double X, double Y) ClampCardCenter(double x, double y)
        => ClampCardCenter(x, y, ViewportWidth, ViewportHeight, CardSize);

    /// <summary>把节点中心钳进可用区 [half, 视口 − half]（half = 卡边长一半）。
    /// 视口窄于一张卡时收敛到中线（退化布局不产出负区间）；非有限输入同样回中线。</summary>
    public static (double X, double Y) ClampCardCenter(double x, double y, double width, double height, double cardSize)
    {
        var half = cardSize > 0.0 ? cardSize * 0.5 : 0.0;
        return (ClampAxis(x, half, width), ClampAxis(y, half, height));
    }

    /// <summary>卡片右缘（自视口左缘量起）：单测据它与 <see cref="DetailColumnLeft"/> 比。</summary>
    public static double CardRight(double centerX) => centerX + CardSize * 0.5;

    /// <summary>卡片左缘（自视口左缘量起）。</summary>
    public static double CardLeft(double centerX) => centerX - CardSize * 0.5;

    private static double ClampAxis(double value, double half, double extent)
    {
        var lo = half;
        var hi = extent - half;
        if (!double.IsFinite(value) || !double.IsFinite(extent) || hi < lo)
        {
            return extent > 0.0 && double.IsFinite(extent) ? extent * 0.5 : 0.0;
        }

        return value < lo ? lo : value > hi ? hi : value;
    }
}
