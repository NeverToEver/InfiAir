using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 研究所升级列表（局外成长 UI，2026-08-09 计划 M4）：科技点余额 + 每项升级行
/// （名称 / 当前等级 / 升级费用按钮）。Welcome 主菜单模态与 BaseConsole 基地面板复用。
/// TechPointsChanged 信号驱动刷新（不每帧轮询）；升级点击 → GameState.SpendTechPoints
/// （未登录/余额不足/已满级由消费侧守卫，按钮禁用态与守卫一致）。
/// 升级名称复用 BUFF_&lt;ID&gt;_NAME 翻译键（id 即 buff id）。
/// </summary>
public sealed partial class ResearchLab : VBoxContainer
{
    private readonly Callable _pointsChanged;

    public ResearchLab()
    {
        _pointsChanged = Callable.From<long>(OnPointsChanged);
        AddThemeConstantOverride("separation", 8);
    }

    /// <summary>TechPointsChanged 信号回调（信号带 1 参，刷新时忽略入账值——全量重建）。</summary>
    private void OnPointsChanged(long _) => Refresh();

    public override void _Ready()
    {
        var gs = GameState.Instance;
        if (gs != null && !gs.IsConnected("TechPointsChanged", _pointsChanged))
        {
            gs.Connect("TechPointsChanged", _pointsChanged);
        }

        Refresh();
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs != null && gs.IsConnected("TechPointsChanged", _pointsChanged))
        {
            gs.Disconnect("TechPointsChanged", _pointsChanged);
        }

        base._ExitTree();
    }

    /// <summary>重建列表（清空重绘——项数少、刷新频率低：结算入账/升级消费时触发）</summary>
    public void Refresh()
    {
        var gs = GameState.Instance;
        if (gs == null)
        {
            return;
        }

        // 2026-08-10 健壮性审查：Free() 同步清理（对齐 BaseConsole/Hud U16 先例）——
        // QueueFree 帧末才删，与同帧 AddChild 的新行并存闪一帧
        foreach (var child in GetChildren())
        {
            child.Free();
        }

        var pointsLabel = UITheme.MakeLabel(GdFormat.Format(Tr("META_POINTS"), gs.TechPoints), 24, UITheme.AccentGold);
        AddChild(pointsLabel);
        AddChild(UITheme.MakeLabel(Tr("META_HINT"), UITheme.FontCaption, UITheme.TextDim));
        foreach (var id in gs.MetaUpgradeIds())
        {
            AddChild(BuildRow(gs, id));
        }
    }

    private Control BuildRow(GameState gs, StringName id)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);

        var name = UITheme.MakeLabel(Tr(BuffNameKey(id)), 20);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(name);

        var level = gs.MetaLevel(id);
        var levelLabel = UITheme.MakeLabel(GdFormat.Format(Tr("META_LEVEL"), level, gs.MetaMaxLevel(id)), 20, UITheme.TextDim);
        row.AddChild(levelLabel);

        var cost = gs.MetaUpgradeCost(id);
        var button = UITheme.MakeButton(cost > 0 ? GdFormat.Format(Tr("META_COST"), cost) : Tr("META_MAXED"));
        button.Disabled = cost <= 0 || gs.TechPoints < cost;
        button.Pressed += () =>
        {
            gs.SpendTechPoints(id); // 守卫失败静默（按钮禁用态已覆盖正常路径）
            Refresh();
        };
        row.AddChild(button);
        return row;
    }

    private static StringName BuffNameKey(StringName id) => $"BUFF_{id.ToString().ToUpperInvariant()}_NAME";
}
