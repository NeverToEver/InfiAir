using System;
using Godot;
using InfiAir.Core;
using InfiAir.Core.Machines;

namespace InfiAir;

/// <summary>
/// 机型性格指纹图（六轴雷达图，程序化绘制、零贴图零 shader）：把 core <see cref="MachineProfileTable"/>
/// 的六条**同向**轴（越大越有利）画成一张六边形——自外向内是刻度带上界外圈 / 基准环（虚线）/
/// 六条轴线 / 数据多边形 / 六个顶点，轴名画在顶点之外。
///
/// 读法：中心 ＝ 标准型基准、**基准环在一半半径处**（口径与理由见 <see cref="MachineProfileTable"/>），
/// 凸起 ＝ 强于基准、凹陷 ＝ 弱于基准。刻度带 [0.60, 1.40] 的越界值已在 core 钳到边界，
/// 本控件不再做任何取值判断。
///
/// 尺寸：栏板侧按 core <c>MachinePlateLayout.ChartBox</c> 给正方形盒，半径 = 「半边长 − 轴标签留白」。
/// 标签是顶点之外的**字**，留白算漏了就会画到板外，而被裁掉一半的字不报错——故控件还把标签矩形
/// 钳进自身边界（双保险），并把半径的下限做成整块不画（画半张图比不画更难认）。
///
/// **静态、无闪烁、无动画**（硬约束）：只在换焦点时重画一次（<see cref="SetProfile"/>），不逐帧推进、
/// 不脉冲、不受「减少闪光」影响——全屏脉冲预算（<c>FlashBudget</c>）与本控件无关，但本图也不该
/// 新增任何闪烁。
///
/// **无障碍**：本图是纯视觉编码（形状即读数），因此**不得是唯一通道**——面板底部的性格文案行
/// （<see cref="MachineSpec.BlurbKey"/> 的译文）是它的文本等价物，两者在版式里成对出现
/// （只留其一的改版 = 把「一眼看出性格」换成「只有看得见的人才读得到性格」）。
/// </summary>
public partial class MachineProfileChart : Control
{
    /// <summary>轴数（＝ core 的绘制顺序长度；缓冲区容量与循环上界共用它）。</summary>
    private const int Count = MachineProfileTable.AxisCount;

    /// <summary>半径下限（px）：小于它整块不画——六个轴名挤在一起时图已经不成为读数。</summary>
    private const float MinRadiusPx = 12.0f;

    /// <summary>标签中心距外圈顶点的距离（px）。与 core <c>MachinePlateLayout.ChartLabelPad</c>
    /// 的关系：留白 ≥ 本值 + 半个行高（标签才装得进图盒）。</summary>
    private const float LabelGapPx = 15.0f;

    /// <summary>顶点圆点半径（px）。</summary>
    private const float DotRadiusPx = 3.0f;

    /// <summary>基准环虚线的段长（px）：虚线与外圈的实线一眼分得开，不必靠颜色深浅去比。</summary>
    private const float BaselineDashPx = 5.0f;

    /// <summary>绘制强度的下限：顶点永不落在正中心。贴到刻度带下界的轴在观感上就是「到底了」，
    /// 但几何上留一点半径——六个顶点全部重合时填充多边形退化成一点，引擎会把整块填充**静默跳过**
    /// （实测：退化多边形不报错、只是什么都不画）。真正防自交的是 core 的固定角度顺序
    /// （相邻顶点方向角恒定递增，由 <c>MachineProfileTests</c> 钉住）——画序写错时引擎才打
    /// `Invalid polygon data, triangulation failed.` 并整块不画。</summary>
    private const double MinDrawStrength = 0.02;

    // 线宽与透明度：全部由 UITheme 的琥珀派生，取值服从「网格退后、数据向前」的层级——
    // 外圈最实（它给出量程）、基准环虚线次之、轴线最弱，数据多边形描边用满值。
    private const float RingWidth = 1.2f;
    private const float AxisWidth = 1.0f;
    private const float PolygonWidth = 2.0f;
    private const float OuterAlpha = 0.30f;
    private const float BaselineAlpha = 0.18f;
    private const float AxisAlpha = 0.14f;
    private const float FillAlpha = 0.20f;

    /// <summary>六条轴的归一强度（0..1，顺序同 <see cref="MachineProfileTable.DrawingOrder"/>）。</summary>
    private readonly double[] _strength = new double[Count];

    // ---- 绘制顶点缓冲（实例级一次分配）----
    // 本控件不逐帧重绘（只在换机型时 QueueRedraw），但 _Draw 内一律不 new 顶点数组是同款纪律：
    // Draw* 调用在入队时即复制数据，复用同一缓冲安全（同一缓冲不得跨两次 Draw 调用并存）。
    private readonly Vector2[] _outer = new Vector2[Count];           // 刻度带上界六边形
    private readonly Vector2[] _outerLoop = new Vector2[Count + 1];   // 其闭合描边（首点补到末尾）
    private readonly Vector2[] _baseline = new Vector2[Count];        // 基准六边形的六个顶点
    private readonly Vector2[] _polygon = new Vector2[Count];         // 数据多边形
    private readonly Vector2[] _polygonLoop = new Vector2[Count + 1]; // 其闭合描边

    public override void _Ready()
    {
        // 只换机型时重画：不逐帧推进（无动画即无闪烁，也不吃帧预算）
        SetProcess(false);
        MouseFilter = MouseFilterEnum.Ignore;
        for (var i = 0; i < Count; i++)
        {
            _strength[i] = MachineProfileTable.BaselineStrength;
        }

        Resized += QueueRedraw;
        QueueRedraw();
    }

    /// <summary>换机型：重算六条轴的强度并重画一次。入口只收两层乘区（数值层 + 能力层），
    /// 强度与钳制全在 core <see cref="MachineProfileTable"/>——绘制层不得自己乘系数或判方向。</summary>
    public void SetProfile(MachineModifiers mods, MachineKit kit)
    {
        for (var i = 0; i < Count; i++)
        {
            _strength[i] = MachineProfileTable.Strength(MachineProfileTable.DrawingOrder[i], mods, kit);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        var radius = RadiusPx();
        if (radius < MinRadiusPx)
        {
            return; // 控件被压到画不下整张图：整块不画（不画半张图，也不报错）
        }

        var center = Size * 0.5f;
        for (var i = 0; i < Count; i++)
        {
            _outer[i] = Vertex(center, radius, i, 1.0);
            _baseline[i] = Vertex(center, radius, i, MachineProfileTable.BaselineStrength);
            _polygon[i] = Vertex(center, radius, i, Math.Max(_strength[i], MinDrawStrength));
        }

        CloseLoop(_outer, _outerLoop);
        CloseLoop(_polygon, _polygonLoop);

        DrawPolyline(_outerLoop, new Color(UITheme.Accent, OuterAlpha), RingWidth, true);

        // 基准环画虚线：它是「基准」这条参照线，不能读成又一个刻度环
        for (var i = 0; i < Count; i++)
        {
            DrawDashedLine(_baseline[i], _baseline[(i + 1) % Count],
                new Color(UITheme.Accent, BaselineAlpha), AxisWidth, BaselineDashPx, true);
        }

        for (var i = 0; i < Count; i++)
        {
            DrawLine(center, _outer[i], new Color(UITheme.Accent, AxisAlpha), AxisWidth, true);
        }

        DrawColoredPolygon(_polygon, new Color(UITheme.Accent, FillAlpha));
        DrawPolyline(_polygonLoop, UITheme.Accent, PolygonWidth, true);
        for (var i = 0; i < Count; i++)
        {
            DrawCircle(_polygon[i], DotRadiusPx, UITheme.HoloPale);
        }

        DrawAxisLabels(center, radius);
    }

    /// <summary>半径 = 半边长 − 轴标签留白（口径与常量单源在 core
    /// <c>MachinePlateLayout.ChartRadius</c>：那边算「盒里该留多少」，这里算「盒有多大」）。</summary>
    private float RadiusPx() => Mathf.Min(Size.X, Size.Y) * 0.5f - (float)MachinePlateLayout.ChartLabelPad;

    /// <summary>六条轴名：以顶点方向为轴心居中排在顶点之外。逐条把**标签矩形**钳进控件边界——
    /// 越界的那一半字会被静默裁掉，而字号 / 文案一改就可能越界（core 的留白常量是第一道，
    /// 这里是第二道）。</summary>
    private void DrawAxisLabels(Vector2 center, float radius)
    {
        var font = UITheme.Font;
        for (var i = 0; i < Count; i++)
        {
            var text = (string)Tr(MachineProfileTable.TextKey(MachineProfileTable.DrawingOrder[i]));
            if (text.Length == 0)
            {
                continue;
            }

            var anchor = Vertex(center, radius + LabelGapPx, i, 1.0);
            var size = font.GetStringSize(text, HorizontalAlignment.Left, -1.0f, UITheme.FontSmall);
            var pos = anchor - size * 0.5f;
            pos.X = Mathf.Clamp(pos.X, 0.0f, Mathf.Max(Size.X - size.X, 0.0f));
            pos.Y = Mathf.Clamp(pos.Y, 0.0f, Mathf.Max(Size.Y - size.Y, 0.0f));
            // DrawString 的原点是基线（不是左上角）：不加 ascent 的话整圈轴名会各偏半行
            DrawString(
                font,
                new Vector2(pos.X, pos.Y + font.GetAscent(UITheme.FontSmall)),
                text,
                HorizontalAlignment.Left,
                -1.0f,
                UITheme.FontSmall,
                UITheme.TextDim);
        }
    }

    /// <summary>一个顶点：core 算坐标（BCL 类型），这里转成 Godot 的 Vector2。</summary>
    private static Vector2 Vertex(Vector2 center, float radius, int index, double strength)
    {
        var point = MachineProfileTable.Vertex(center.X, center.Y, radius, index, strength);
        return new Vector2((float)point.X, (float)point.Y);
    }

    /// <summary>闭合描边：DrawPolyline 不自动闭合，末点回到首点。</summary>
    private static void CloseLoop(Vector2[] source, Vector2[] loop)
    {
        Array.Copy(source, loop, source.Length);
        loop[^1] = source[0];
    }
}
