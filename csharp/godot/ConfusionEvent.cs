using Godot;

namespace InfiAir;

/// <summary>
/// 精神错乱事件：全屏变色覆盖层呼吸脉冲（视觉部分）；玩家输入反转经 manager 统一
/// fog_event_started/ended 信号在 Player 侧应用（事件类不触碰 Player）。
/// 覆盖层节点由 manager 构建（_build_visual_layers），本事件经 FogEvent 访问器缓存
/// 引用后驱动显隐与呼吸（tick 正弦，无 Tween/Timer——随事件 duration 与树暂停自然同步）。
/// 健壮性：_on_start 缓存 layer/rect 并判空——context 缺键时降级空转（tick 判空保护，
/// 不崩、不产生每帧 null 访问；事件生命周期仍由编排器统一驱动）。
/// </summary>
public partial class ConfusionEvent : FogEvent
{
    /// <summary>呼吸包络（alpha 0.06..0.16，周期 1s；对齐原 Tween 幅度）。</summary>
    private const float BaseAlpha = 0.11f;

    private const float PulseAlpha = 0.05f;

    private const float PulsePeriod = 1.0f;

    private CanvasLayer? _layer;
    private ColorRect? _rect;
    private float _t;

    public override StringName EventId() => new StringName("mental_confusion");

    protected override void OnStart()
    {
        _layer = OverlayLayer();
        _rect = OverlayRect();
        _t = 0.0f;
        // P4（2026-08-05）：缺键降级改为空转——原实现自行 end() 使效果 0s 消失但
        // event_ended 延后 duration 才发（信号不同步）；空转保持生命周期由编排器统一驱动
        if (_layer != null)
        {
            _layer.Visible = true;
        }

        if (_rect != null)
        {
            _rect.Color = new Color(0.45f, 0.2f, 0.9f, 0.0f);
        }
    }

    protected override void OnTick(float delta)
    {
        if (_rect == null)
        {
            return; // 缺键空转
        }

        _t += delta;
        var c = _rect.Color;
        c.A = BaseAlpha + PulseAlpha * Mathf.Sin(_t / PulsePeriod * Mathf.Tau);
        _rect.Color = c;
    }

    protected override void OnEnd()
    {
        if (_rect != null)
        {
            _rect.Color = new Color(0.45f, 0.2f, 0.9f, 0.0f);
        }

        if (_layer != null)
        {
            _layer.Visible = false;
        }
    }
}
