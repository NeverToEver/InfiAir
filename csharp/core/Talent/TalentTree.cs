namespace InfiAir.Core.Talent;

/// <summary>天赋节点结构定义（id = 既有增幅 id，效果消费端零改动）。层级：大类 → 支线 → 节点，
/// 同一支线内按声明序构成前置链（后置节点需前置节点 Lv≥1）。数值上限不在结构内：
/// augments.&lt;id&gt;.max_stacks（balance.json）为唯一上限来源，由服务层注入。</summary>
public sealed class TalentNodeDef
{
    public required string Id { get; init; }

    public required string CategoryId { get; init; }

    public required string LineId { get; init; }

    /// <summary>支线内序号（0 = 无前置的根节点）。</summary>
    public required int Index { get; init; }
}

public sealed class TalentLineDef
{
    public required string Id { get; init; }

    /// <summary>本地化键（TALENT_LINE_*）。</summary>
    public required string NameKey { get; init; }

    public required IReadOnlyList<string> NodeIds { get; init; }
}

public sealed class TalentCategoryDef
{
    public required string Id { get; init; }

    /// <summary>本地化键（TALENT_CAT_*）。</summary>
    public required string NameKey { get; init; }

    public required IReadOnlyList<TalentLineDef> Lines { get; init; }
}

/// <summary>
/// 天赋路线契约（机制 C 路线绑定）：路线绑定一个大类为核心，其余大类节点上限减半；
/// 切换路线消耗重置代币。Special 无对应路线（始终属「非路线」侧）。
/// </summary>
public sealed class TalentRouteDef
{
    public required string Id { get; init; }

    public required string NameKey { get; init; }

    public required string CoreCategoryId { get; init; }
}

/// <summary>天赋树静态结构（结构不属于数值，不进 balance.json；同 ROUTE_LINES/MISSION_DEFS 先例）。</summary>
public static class TalentTree
{
    public static readonly IReadOnlyList<TalentCategoryDef> Categories = new List<TalentCategoryDef>
    {
        new()
        {
            Id = "offense", NameKey = "TALENT_CAT_OFFENSE",
            Lines = new List<TalentLineDef>
            {
                new() { Id = "gunnery", NameKey = "TALENT_LINE_GUNNERY", NodeIds = new[] { "power_shot", "bullet_speed", "piercing", "homing" } },
                new() { Id = "firecontrol", NameKey = "TALENT_LINE_FIRECONTROL", NodeIds = new[] { "rapid_fire", "crit_shot", "spread_shot", "salvo" } },
                new() { Id = "ordnance", NameKey = "TALENT_LINE_ORDNANCE", NodeIds = new[] { "explosive", "laser_beam" } },
            },
        },
        new()
        {
            Id = "defense", NameKey = "TALENT_CAT_DEFENSE",
            Lines = new List<TalentLineDef>
            {
                new() { Id = "armor", NameKey = "TALENT_LINE_ARMOR", NodeIds = new[] { "armor", "shield", "deflector" } },
                new() { Id = "vitality", NameKey = "TALENT_LINE_VITALITY", NodeIds = new[] { "extra_life", "regen", "lifesteal", "second_wind" } },
            },
        },
        new()
        {
            Id = "mobility", NameKey = "TALENT_CAT_MOBILITY",
            Lines = new List<TalentLineDef>
            {
                new() { Id = "assault", NameKey = "TALENT_LINE_ASSAULT", NodeIds = new[] { "phase_dash", "boost_recovery", "dash_strike" } },
                new() { Id = "field", NameKey = "TALENT_LINE_FIELD", NodeIds = new[] { "evasion", "slow_field", "graze_field" } },
            },
        },
        new()
        {
            Id = "special", NameKey = "TALENT_CAT_SPECIAL",
            Lines = new List<TalentLineDef>
            {
                new() { Id = "logistics", NameKey = "TALENT_LINE_LOGISTICS", NodeIds = new[] { "efficient_boost", "mothership_recall", "score_amp", "combo_guard" } },
            },
        },
    };

    public static readonly IReadOnlyList<TalentRouteDef> Routes = new List<TalentRouteDef>
    {
        new() { Id = "berserker", NameKey = "TALENT_ROUTE_BERSERKER", CoreCategoryId = "offense" },
        new() { Id = "guardian", NameKey = "TALENT_ROUTE_GUARDIAN", CoreCategoryId = "defense" },
        new() { Id = "ranger", NameKey = "TALENT_ROUTE_RANGER", CoreCategoryId = "mobility" },
    };

    /// <summary>机制 A 分支互斥锁的对立派系对（一方投入达阈值 → 对方上限永久降低）。</summary>
    public static readonly IReadOnlyList<(string A, string B)> MutexPairs = new[] { ("offense", "defense") };

    private static readonly Dictionary<string, TalentNodeDef> NodeIndex = BuildIndex();

    private static Dictionary<string, TalentNodeDef> BuildIndex()
    {
        var index = new Dictionary<string, TalentNodeDef>();
        foreach (var cat in Categories)
        {
            foreach (var line in cat.Lines)
            {
                for (var i = 0; i < line.NodeIds.Count; i++)
                {
                    var id = line.NodeIds[i];
                    index.Add(id, new TalentNodeDef { Id = id, CategoryId = cat.Id, LineId = line.Id, Index = i });
                }
            }
        }

        return index;
    }

    /// <summary>全部节点 id（声明序：大类 → 支线 → 层级）。</summary>
    public static IReadOnlyList<string> NodeIds()
    {
        var ids = new List<string>();
        foreach (var cat in Categories)
        {
            foreach (var line in cat.Lines)
            {
                ids.AddRange(line.NodeIds);
            }
        }

        return ids;
    }

    /// <summary>节点结构定义（未知 id 返回 null）。</summary>
    public static TalentNodeDef? Find(string id) => NodeIndex.GetValueOrDefault(id);

    /// <summary>前置节点 id（支线内上一节点；根节点/未知 id 返回 null）。</summary>
    public static string? Prerequisite(string id)
    {
        var def = Find(id);
        if (def is not { Index: > 0 })
        {
            return null;
        }

        foreach (var cat in Categories)
        {
            foreach (var line in cat.Lines)
            {
                if (line.Id == def.LineId)
                {
                    return line.NodeIds[def.Index - 1];
                }
            }
        }

        return null;
    }

    public static TalentCategoryDef Category(string categoryId)
    {
        foreach (var cat in Categories)
        {
            if (cat.Id == categoryId)
            {
                return cat;
            }
        }

        throw new KeyNotFoundException($"unknown talent category: {categoryId}");
    }

    public static TalentLineDef Line(string lineId)
    {
        foreach (var cat in Categories)
        {
            foreach (var line in cat.Lines)
            {
                if (line.Id == lineId)
                {
                    return line;
                }
            }
        }

        throw new KeyNotFoundException($"unknown talent line: {lineId}");
    }

    /// <summary>对立派系大类 id（互斥锁阈值判定用；无对立关系返回 null）。</summary>
    public static string? OpposingCategoryOf(string categoryId)
    {
        foreach (var (a, b) in MutexPairs)
        {
            if (a == categoryId)
            {
                return b;
            }

            if (b == categoryId)
            {
                return a;
            }
        }

        return null;
    }
}
