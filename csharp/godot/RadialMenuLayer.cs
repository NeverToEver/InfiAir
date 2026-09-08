using Godot;

using InfiAir.Core;

namespace InfiAir;

/// <summary>
/// 左缘轮盘菜单页骨架（2026-09-08 圆盘 UI 全覆盖）：dim 遮罩 + RadialWheel（左缘 1/4 弧）
/// + 右侧内容区的统一开合编排，与 TalentPanel 同一视觉语言——全站菜单/条目目录的导航
/// 面统一收敛为圆盘。入场：dim 淡入 + 轮盘过冲滑入；退场反序加速（内容层由子类自编排）。
/// 子类契约：_Ready 内先 BuildChrome() 再自建内容区；打开前 LoadMenu() 装配根级选项；
/// 轮盘叶子确认经 Wheel.Confirmed 订阅自处理。宿主页 Visible=false 期间必须同步
/// Wheel.Visible=false（Node2D._Input 只看自身可见性，不随 CanvasLayer 隐藏失效）。
/// </summary>
public abstract partial class RadialMenuLayer : CanvasLayer
{
    /// <summary>轮盘圆心静止位（1080p 设计坐标；UI 不走 world_scale）。
    /// y=480 与端点角钳 36°（RadialWheel.SlotAngleFor）配合：卡片旋转包络底缘 ≈927，
    /// 不压左下生命/状态 HUD 区（顶缘 ≈940）。</summary>
    protected static readonly Vector2 WheelRest = new(-160f, 480f);

    protected RadialWheel Wheel = null!;

    private Node2D _wheelHolder = null!;

    protected ColorRect Dim = null!;

    /// <summary>构建骨架（dim + 轮盘 holder）。子类 _Ready 先调本方法再挂内容区（绘制序：dim → 轮盘 → 内容）。</summary>
    protected void BuildChrome()
    {
        Dim = new ColorRect { Color = UITheme.DimBg };
        Dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(Dim);

        // holder 承载入场/退场位移：轮盘自身 _Process 视差会写自己的 Position，两者解耦（TalentPanel 先例）
        _wheelHolder = new Node2D { Position = WheelRest };
        Wheel = new RadialWheel();
        _wheelHolder.AddChild(Wheel);
        AddChild(_wheelHolder);
    }

    /// <summary>装载根级选项（每次打开重装即可复位导航态）。槽距按选项数自适应：
    /// 端点卡 |弧角| 钳在 ≈42° 内（更大的卡片会出屏/被可视半幅剔除）。</summary>
    protected void LoadMenu(IReadOnlyList<RadialWheelOption> roots, string backLabel)
    {
        Wheel.BackLabel = backLabel;
        Wheel.Load(roots, RadialWheel.SlotAngleFor(roots.Count));
    }

    /// <summary>把轮盘提到本层最上（宿主页在 chrome 之后还挂了带遮罩的 shell 时必须调用，
    /// 否则 shell 遮罩盖住轮盘卡片——结算/设置页实测）。</summary>
    protected void RaiseWheel()
    {
        _wheelHolder.MoveToFront();
    }

    /// <summary>入场编排：dim 淡入 + 轮盘过冲滑入（Back ease-out）。</summary>
    protected void PlayWheelEntrance()
    {
        Dim.Modulate = new Color(Dim.Modulate, 0f);
        _wheelHolder.Position = new Vector2(WheelRest.X - 620f, WheelRest.Y);
        var tw = CreateTween();
        tw.Parallel().TweenProperty(Dim, "modulate:a", 1.0f, 0.2);
        tw.Parallel().TweenProperty(_wheelHolder, "position", WheelRest, 0.5)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>退场编排：轮盘加速滑出 + dim 收尾，完成后回调（回调里再置 Visible=false）。</summary>
    protected void PlayWheelExit(Action finished, float total = 0.36f)
    {
        var tw = CreateTween();
        tw.TweenProperty(_wheelHolder, "position", new Vector2(WheelRest.X - 620f, WheelRest.Y), 0.26)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In).SetDelay(0.04);
        tw.Parallel().TweenProperty(Dim, "modulate:a", 0.0f, 0.24).SetDelay(0.1);
        tw.TweenInterval(Mathf.Max(total - 0.3f, 0.02f));
        tw.TweenCallback(Callable.From(finished));
    }

    /// <summary>轮盘可见性同步（宿主开/关时调用；Node2D._Input 不随 CanvasLayer 隐藏失效）。
    /// dimActive：chrome 遮罩的显隐（默认跟随轮盘）。非模态宿主页（welcome 自带不透明底、
    /// settings 自带 page shell 遮罩）必须显式传 false——chrome Dim 是最上层全屏 84% 黑罩，
    /// 常开会把整页压暗（welcome 实测整页发暗）。</summary>
    protected void SetWheelActive(bool active, bool? dimActive = null)
    {
        Wheel.Visible = active;
        Dim.Visible = dimActive ?? active;
    }

}
