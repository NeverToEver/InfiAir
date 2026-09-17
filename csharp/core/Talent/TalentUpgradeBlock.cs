namespace InfiAir.Core.Talent;

/// <summary>
/// 天赋加点的拦截原因（判定侧与 UI 的唯一口径）。
///
/// 背景：这条返回码原先是一组裸字符串（`"CACHE"`/`"PREREQ"`/…，`""` 表示可升级），判定侧与 UI
/// 各自字面量比较。UI 侧以「else = 可升级」兜底：新增原因、写错字面量（`"PREREQ "` 之类）都会
/// 落进兜底分支亮起升级按钮，点下去再被判定侧拦下——按钮可点、点击无响应、无任何提示。
/// 改为枚举后拼写错误是编译错误；配合 <see cref="TalentUpgradeGate"/> 的失败关闭契约，
/// 未登记原因一律按「不可升级」处理，不再出现「亮着按钮但点不动」。
/// </summary>
public enum TalentUpgradeBlock
{
    /// <summary>可升级。</summary>
    None,

    /// <summary>天赋点不足。</summary>
    Cache,

    /// <summary>前置节点未满足。</summary>
    Prereq,

    /// <summary>已超载（本次不可再加）。</summary>
    Overcharged,

    /// <summary>已到上限且本局风险加点次数用满。</summary>
    OverchargeLimit,

    /// <summary>节点 id 不在天赋树里（配置/存档异常；UI 须按不可升级处理）。</summary>
    Unknown,
}

/// <summary>加点放行契约（纯逻辑，零 Godot 依赖）：**只有 <see cref="TalentUpgradeBlock.None"/> 放行**，
/// 其余原因一律失败关闭。UI 与判定侧共用它，避免「新增/写错原因」时静默变成可点。</summary>
public static class TalentUpgradeGate
{
    /// <summary>该拦截原因下是否允许加点。</summary>
    public static bool UpgradeEnabled(TalentUpgradeBlock reason) => reason == TalentUpgradeBlock.None;
}
