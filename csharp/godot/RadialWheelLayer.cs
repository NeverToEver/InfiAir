using Godot;

namespace InfiAir;

/// <summary>
/// RadialWheel 的绘制分层子层。轮盘即时模式下一次 QueueRedraw 会重铺全部几何
/// （面包屑/环带/轮毂/卡片/特效），把重绘频率差异大的部分拆成独立 CanvasItem 后，
/// 悬停/滚动只需重铺卡片层、确认特效只重铺特效层，静态钢构在层深/收缩缩放不变时零重绘。
/// </summary>
public partial class RadialWheelLayer : Node2D
{
    /// <summary>
    /// 绘制委托（宿主 wheel 装配；参数是本子层，所有 Draw* 必须打在这个实例上——
    /// Godot 只在 CanvasItem 自身 _Draw 相位放行它的绘制调用）。绘制走宿主静态缓冲，零托管分配。
    /// </summary>
    public Action<RadialWheelLayer>? Painter { get; set; }

    public void Repaint() => QueueRedraw();

    public override void _Draw() => Painter?.Invoke(this);
}
