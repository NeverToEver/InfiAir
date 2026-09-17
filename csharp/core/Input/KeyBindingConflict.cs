namespace InfiAir.Core.Input;

/// <summary>
/// 改键冲突清理（纯逻辑，零 Godot 依赖）：把目标动作改到某键后，算出需要写回设置覆盖表的条目。
/// 规格单源（DESIGN_BASELINE §1.14）：冲突键**只从占用者移除该键**，占用者其余绑定保留。
/// 占用者的生效绑定若来自默认表，就必须以「默认表减去该键」写入覆盖表——写空数组会连带打掉
/// 同一动作的另一个默认键（project.godot 的 4 个移动动作都是双键默认值），设置页显示「未绑定」；
/// 而不写覆盖又会让 InputMap 重灌时从默认表把冲突键带回来，两个动作抢同一键。
/// </summary>
public static class KeyBindingConflict
{
    /// <summary>
    /// 计算改键后的覆盖条目集合（action → 新绑定键列表）。返回集合只含发生变化的两类动作：
    /// 被抢占的动作（移除 keycode，来源是覆盖表就改覆盖表，来源是默认表就写「默认表减该键」）
    /// 与目标动作（单键绑定）。两张入参表均不被就地修改；无任何绑定的动作不产生条目。
    /// </summary>
    public static Dictionary<string, int[]> Cleanup(
        IReadOnlyDictionary<string, int[]> defaults,
        IReadOnlyDictionary<string, int[]> overrides,
        IReadOnlyList<string> actions,
        string targetAction,
        int keycode)
    {
        var changes = new Dictionary<string, int[]>();
        foreach (var a in actions)
        {
            if (a == targetAction)
            {
                continue;
            }

            // 生效绑定 = 覆盖表优先，否则默认表（与 ApplyKeyBindings / OccupiedBy 同口径）
            var isOverridden = overrides.TryGetValue(a, out var overridden);
            var source = isOverridden
                ? overridden!
                : defaults.TryGetValue(a, out var defaultsForAction) ? defaultsForAction : Array.Empty<int>();
            if (Array.IndexOf(source, keycode) < 0)
            {
                continue;
            }

            changes[a] = Array.FindAll(source, k => k != keycode);
        }

        changes[targetAction] = new[] { keycode };
        return changes;
    }
}
