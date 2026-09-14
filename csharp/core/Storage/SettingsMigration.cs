namespace InfiAir.Core.Storage;

/// <summary>设置档版本决策（旧档可迁移 / 同版直读 / 更高版保守拒绝）。</summary>
public enum SaveVersionDecision
{
    /// <summary>版本与当前一致：按现状字段直读。</summary>
    Accepted,

    /// <summary>版本低于当前：按旧键名迁移后再读。</summary>
    Migrate,

    /// <summary>版本高于当前（降级安装 / 手改）：不猜未来字段语义，按默认值继续。</summary>
    RejectNewer,
}

/// <summary>
/// 设置档迁移与版本决策的纯逻辑（零 Godot 依赖，可单测）。
///
/// 存在理由：这两条真实旧档迁移（buff_panel→augment_panel、window_size→resolution）原先只写在
/// Godot 侧 ApplySettingsDict 里，判定与 Variant 判型、白名单查表、窗口应用搅在一起，
/// 零回归面——改错了表现为老玩家升级后键位/分辨率静默回默认，既不崩也不报错。
/// 输入统一用 <see cref="IReadOnlyDictionary{TKey,TValue}"/>（与 BalanceService 的 CLR JSON 树同形），
/// 不依赖 Godot 的 Dictionary / Variant。
///
/// 合法分辨率集合由调用方传入（唯一来源是 Godot 侧 RESOLUTION_LEVELS）：本类不复制档位表。
/// </summary>
public static class SettingsMigration
{
    /// <summary>旧增幅面板动作名（键位迁移源）。</summary>
    public const string LegacyBuffPanelAction = "buff_panel";

    /// <summary>增幅面板动作名（键位迁移目标）。</summary>
    public const string AugmentPanelAction = "augment_panel";

    /// <summary>旧窗口尺寸档键（三档 small/medium/large）。</summary>
    public const string LegacyWindowSizeKey = "window_size";

    /// <summary>窗口模式键（新格式标记：存在即不再走 window_size 迁移）。</summary>
    public const string WindowModeKey = "window_mode";

    /// <summary>分辨率档键（新格式）。</summary>
    public const string ResolutionKey = "resolution";

    /// <summary>自定义分辨率档键（与 Godot 侧 RESOLUTION_LEVELS 之外的单列档）。</summary>
    public const string CustomResolution = "custom";

    /// <summary>默认分辨率档（旧档无法映射时的落点，与 Godot 侧默认一致）。</summary>
    public const string DefaultResolution = "1920x1080";

    /// <summary>当前设置档版本号（单源：写档方与版本判定方都引用这里，避免两处各写一份数字）。</summary>
    public const int CurrentVersion = 4;

    /// <summary>旧档版本判定：同版直读、旧版迁移、更高版保守拒绝。</summary>
    public static SaveVersionDecision DecideVersion(long savedVersion, int currentVersion)
    {
        if (savedVersion == currentVersion)
        {
            return SaveVersionDecision.Accepted;
        }

        return savedVersion < currentVersion ? SaveVersionDecision.Migrate : SaveVersionDecision.RejectNewer;
    }

    /// <summary>
    /// 键位动作名迁移决策。返回 true = 按 <paramref name="mappedAction"/> 写入该项，
    /// 返回 false = 该项应丢弃（旧名目标已经绑过键，不得覆盖真值——「新键存在则不迁移」）。
    /// </summary>
    public static bool TryMapKeyBindingAction(
        IReadOnlyCollection<string> existingActions, string action, out string mappedAction)
    {
        mappedAction = action;
        if (action != LegacyBuffPanelAction)
        {
            return true;
        }

        if (Contains(existingActions, AugmentPanelAction))
        {
            return false;
        }

        mappedAction = AugmentPanelAction;
        return true;
    }

    /// <summary>旧窗口尺寸三档 → 16:9 分辨率档；未知/缺失值落默认档（旧档位仅窗口化生效，模式取默认 windowed）。</summary>
    public static string MapLegacyWindowSize(string? legacy) => legacy switch
    {
        "small" => "1280x720",
        "medium" => "1600x900",
        _ => DefaultResolution,
    };

    /// <summary>
    /// 分辨率档决策，按优先级取第一命中：
    /// 1) <c>resolution</c> 为字符串且命中合法档（含 <paramref name="customKey"/>）；2) 新格式键
    /// （<c>window_mode</c> / 合法 <c>resolution</c>）均缺失且存在 <c>window_size</c> → 旧档映射；
    /// 3) 其余保持 <paramref name="defaultResolution"/>（手改非法值不得静默改档）。
    /// </summary>
    public static string ResolveResolution(
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyCollection<string> validResolutions,
        string customKey,
        string defaultResolution)
    {
        if (data.TryGetValue(ResolutionKey, out var raw) && raw is string saved
            && (saved == customKey || Contains(validResolutions, saved)))
        {
            return saved;
        }

        // 新格式一旦声明（window_mode 存在），非法 resolution 不得回落到旧档迁移路径
        if (!data.ContainsKey(WindowModeKey)
            && data.TryGetValue(LegacyWindowSizeKey, out var legacy)
            && legacy is string legacyName)
        {
            return MapLegacyWindowSize(legacyName);
        }

        return defaultResolution;
    }

    private static bool Contains(IReadOnlyCollection<string> values, string target)
    {
        foreach (var value in values)
        {
            if (value == target)
            {
                return true;
            }
        }

        return false;
    }
}
