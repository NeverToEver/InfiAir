using Godot;
using InfiAir.Core;

namespace InfiAir;

/// <summary>
/// 航电式警示灯（annunciator lamp）：灯座 + 状态色亮芯 + 外环，用于「母舰停靠」这类
/// 枚举状态读数。配色语义取自 14 CFR 29.1322 的三级分类（一手法规）：
/// 红＝warning（须立即处置的危险，如此处弹仓不足）、琥珀＝caution（注意/进行中，如充能、冷却、对接）、
/// 绿＝safe operation（就绪待命可安全操作）。本项目主色即琥珀，故琥珀态与全站语系天然一致。
///
/// 进行中态（caution）按 0.9Hz 占空呼吸，低于 WCAG 2.3.1 的每秒 3 次闪烁上限；
/// <c>reduce_flash</c> 时转常亮（不做亮度泵动）。否认脉冲（§2.14）与之并行且不改灯态：
/// 召唤键被门控挡下时给一次「按到了、是时机不对」的回绝读数。
/// 标签文字由调用方的既有 Label 承载（本组件只管灯，不引入新文案）。
/// Control 子类，_Draw 程序化绘制（文件名与类型同名）。
/// </summary>
public partial class AnnunciatorLamp : Control
{
    /// <summary>灯语态：与 DockStatusText 的分支同源，只表达「灯该怎么亮」，不含文案。</summary>
    public enum Lamp
    {
        /// <summary>就绪待命（安全态，绿）。</summary>
        Ready,

        /// <summary>进行中/注意（琥珀，呼吸）。</summary>
        Busy,

        /// <summary>立即处置（红，呼吸）。</summary>
        Warning,

        /// <summary>熄灭（无状态）。</summary>
        Off,
    }

    /// <summary>进行中态呼吸频率（Hz）：低于闪烁阈值，读作「工作进行中」而非告警。</summary>
    private const float PulseHz = 0.9f;

    /// <summary>灯半径（px）：小灯座 + 亮芯，不抢主读数。</summary>
    private const float LampRadius = 5.0f;

    /// <summary>否认脉冲时长（秒）与向内收拢行程（px）：门控拒绝时的一次性反馈（§2.14，
    /// 与能力槽同一手势——向内收拢读「进不去/还差着」，与常态呼吸明确区分）。</summary>
    private const float DenyPulseTime = 0.24f;
    private const float DenyPulseShrink = 6.0f;

    private Lamp _state = Lamp.Off;
    private bool _reduceFlash;
    private float _phase;

    /// <summary>否认脉冲进度 0..1；-1 ＝ 未在播（同 AbilitySocket 口径）。</summary>
    private float _deny = -1.0f;

    public override void _Ready() => SetProcess(false);

    /// <summary>设置灯态。呼吸只在 Busy/Warning 且非 reduce_flash 时进行。</summary>
    public void SetLamp(Lamp state)
    {
        if (state == _state)
        {
            return;
        }

        _state = state;
        _phase = 0.0f;
        RefreshProcess();
        QueueRedraw();
    }

    /// <summary>否认脉冲（§2.14）：H 被冷却/遭遇事件门控挡下时由 HUD 调起——一圈向内收拢的
    /// 危险色弧。一次性、不改灯态；减闪门控与能力槽同源（<see cref="FlashBudget.AllowsOneShot"/>），
    /// 抑制后「为什么按不成」仍由坞态文案回答。</summary>
    public void PlayDeny()
    {
        if (!FlashBudget.AllowsOneShot(OneShotFlashId.DockDenyPulse, _reduceFlash))
        {
            return;
        }

        _deny = 0.0f;
        SetProcess(true);
        QueueRedraw();
    }

    /// <summary>无障碍：关呼吸，进行中态转常亮。</summary>
    public void SetReduceFlash(bool reduce)
    {
        if (reduce == _reduceFlash)
        {
            return;
        }

        _reduceFlash = reduce;
        RefreshProcess();
        QueueRedraw();
    }

    private bool NeedsPulse() => !_reduceFlash && _state is Lamp.Busy or Lamp.Warning;

    /// <summary>逐帧回调开关：呼吸与否认脉冲任一在动就得开着（关早了脉冲会冻在半途）。</summary>
    private void RefreshProcess() => SetProcess(NeedsPulse() || _deny >= 0.0f);

    public override void _Process(double delta)
    {
        var d = (float)delta;
        var animated = false;
        if (NeedsPulse())
        {
            _phase = Mathf.PosMod(_phase + d * PulseHz, 1.0f);
            animated = true;
        }

        if (_deny >= 0.0f)
        {
            _deny += d / DenyPulseTime;
            if (_deny >= 1.0f)
            {
                _deny = -1.0f;
            }

            animated = true;
        }

        if (!animated)
        {
            SetProcess(false);
            return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        var center = Size * 0.5f;
        var baseColor = ColorFor(_state);

        // 灯座：深色底 + 细环，未点亮时也是可见的「有这一盏灯」
        DrawCircle(center, LampRadius, new Color(UITheme.SlotDark, 0.92f));
        DrawArc(center, LampRadius, 0.0f, Mathf.Tau, 20, new Color(UITheme.PanelBorder, 0.75f), 1.0f, true);

        // 否认脉冲压在灯座上——Off 态也要能回绝（回绝读的是「按不成」，与灯态无关）
        if (_deny >= 0.0f)
        {
            DrawDenyPulse(center);
        }

        if (_state == Lamp.Off)
        {
            return;
        }

        var pulse = NeedsPulse() ? 0.5f + 0.5f * Mathf.Sin(_phase * Mathf.Tau) : 1.0f;
        var core = LampRadius - 2.0f;
        DrawCircle(center, core, new Color(baseColor, 0.35f + 0.65f * pulse));
        // 亮芯高光：小偏移的暖白点，读作「灯是发光的」而非一块色斑
        DrawCircle(center - new Vector2(core * 0.28f, core * 0.28f), core * 0.42f,
            new Color(UITheme.HoloPale, 0.55f * pulse));
    }

    /// <summary>否认脉冲：一圈自灯外缘向内收拢并淡出的危险色弧（一次性）。</summary>
    private void DrawDenyPulse(Vector2 center)
    {
        var radius = LampRadius + 1.5f - DenyPulseShrink * _deny;
        var alpha = (1.0f - _deny) * 0.85f;
        DrawArc(center, radius, 0.0f, Mathf.Tau, 20, new Color(UITheme.Danger, alpha), 1.5f, true);
    }

    private static Color ColorFor(Lamp state) => state switch
    {
        Lamp.Ready => UITheme.Success,
        Lamp.Busy => UITheme.Accent,
        Lamp.Warning => UITheme.Danger,
        _ => UITheme.SlotDark,
    };
}
