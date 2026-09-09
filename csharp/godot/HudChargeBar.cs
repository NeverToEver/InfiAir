using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 统一长按蓄力条（HUD 底部居中）：提示文案（%d 百分比占位）+ 280×10 填充条。
/// 全部长按触发功能共用本组件（Hud.ChargeChannel 注册表管理），差异仅限 提示文案 + 填充色
/// 两处——新增蓄力功能 = 加一个通道枚举 + 一行规格配置，不再各写一套 UI。
/// SetRatio(&lt;0) 隐藏；百分比文本仅在整数值变化时重写（每帧驱动下零冗余字符串分配）。
/// 视觉规格与全站蓄力语言一致：FontHud 标签 + 白 15% 底条 + 通道色填充（提前离舰先例）。
/// </summary>
public partial class HudChargeBar : VBoxContainer
{
    private const float BarWidth = 280.0f;
    private const float BarHeight = 10.0f;

    private Label _label = null!;
    private ColorRect _fill = null!;
    private string _promptFormat = "";
    private int _lastPercent = -1;


    /// <summary>工厂：promptFormat 走翻译串（%d 百分比占位），color 为通道色，slot 为底部居中锚下的槽位偏移。</summary>
    public static HudChargeBar Create(string promptFormat, Color color, Vector2 slot)
    {
        var bar = new HudChargeBar
        {
            Position = slot,
            CustomMinimumSize = new Vector2(BarWidth, 0.0f),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            _promptFormat = promptFormat,
        };
        bar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        bar.AddThemeConstantOverride("separation", 6);

        bar._label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar._label.AddThemeFontOverride("font", UITheme.Font);
        bar._label.AddThemeFontSizeOverride("font_size", 24);
        bar._label.AddThemeColorOverride("font_color", color);
        bar.AddChild(bar._label);

        var barBg = new ColorRect
        {
            Color = new Color(1.0f, 1.0f, 1.0f, 0.15f),
            CustomMinimumSize = new Vector2(BarWidth, BarHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar._fill = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar._fill.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        bar._fill.AnchorRight = 0.0f;
        barBg.AddChild(bar._fill);
        bar.AddChild(barBg);
        return bar;
    }

    /// <summary>蓄力进度：ratio &lt; 0 隐藏，否则百分比提示 + 填充条（钳 [0,1]）。</summary>
    public void SetRatio(float ratio)
    {
        if (ratio < 0.0f)
        {
            _lastPercent = -1;
            Visible = false;
            return;
        }

        var percent = (int)(Mathf.Clamp(ratio, 0.0f, 1.0f) * 100.0f);
        Visible = true;
        _fill.AnchorRight = percent / 100.0f;
        if (percent == _lastPercent)
        {
            return; // 每帧驱动：整百分比未变不重写文本（零冗余分配）
        }

        _lastPercent = percent;
        _label.Text = GdFormat.Format(_promptFormat, percent);
    }
}
