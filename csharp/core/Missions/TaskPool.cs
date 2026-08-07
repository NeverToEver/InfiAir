namespace InfiAir.Core.Missions;

/// <summary>基地任务定义（id 为身份标识；goal/kind 供进度/展示逻辑消费）。</summary>
public sealed record TaskDef(string Id, int Goal, string Kind);

/// <summary>
/// 任务池无放回抽取核心（2026-08-07 自 scripts/task_pool.gd 迁移）：洗牌索引序列 + 游标
/// 推进。单次 <see cref="Draw"/> 内不重复；一次 draw 消耗完当前批次后若仍有名额且全池
/// 还有可用候选则重洗继续补足（跨 draw 尽量延迟复用；排除项导致批次提前耗尽不再截断——
/// Q05）。排除覆盖全池时安全返回空（不抛错不死循环）。
///
/// 分布语义与 GDScript shuffle 等价（Fisher–Yates）；RNG 独立于 GDScript 全局随机源，
/// xUnit 可注入种子复现序列。不做与旧实现逐序列等价的承诺（无外部依赖此语义）。
/// </summary>
public sealed class TaskPool
{
    private readonly TaskDef[] _defs;
    private readonly List<int> _order = [];
    private int _cursor;
    private readonly Random _random;

    public TaskPool(IEnumerable<TaskDef> defs, int? seed = null)
    {
        _defs = defs.ToArray();
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// 抽取 count 个任务定义（无放回）；excludeIds 排除这些 id（刷新时排除全部在场任务，
    /// 防重号/覆盖保留任务）。返回实际抽到的定义（顺序即抽取顺序；排除覆盖全池时为空）。
    /// </summary>
    public IReadOnlyList<TaskDef> Draw(int count, IReadOnlySet<string> excludeIds)
    {
        if (count <= 0)
        {
            return [];
        }
        int usable = 0;
        foreach (var def in _defs)
        {
            if (!excludeIds.Contains(def.Id))
            {
                usable++;
            }
        }
        if (usable == 0)
        {
            return [];  // 防呆：全池被排除，无可用任务
        }
        var result = new List<TaskDef>(Math.Min(count, usable));
        var drawnIds = new HashSet<string>();  // Q05：跨批补足时防单次 draw 内重复（新批次可能重含已抽 id）
        while (result.Count < count && result.Count < usable)
        {
            if (_cursor >= _order.Count)
            {
                if (result.Count < usable)
                {
                    Refill();  // Q05：批次耗尽但全池仍有可用候选 → 重洗继续补足
                }
                else
                {
                    break;
                }
            }
            var def = _defs[_order[_cursor]];
            _cursor++;
            if (excludeIds.Contains(def.Id) || !drawnIds.Add(def.Id))
            {
                continue;
            }
            result.Add(def);
        }
        return result;
    }

    /// <summary>追加一批全索引洗牌（池可循环复用；游标只增不减，序列无限长）。</summary>
    private void Refill()
    {
        var batch = new int[_defs.Length];
        for (int i = 0; i < batch.Length; i++)
        {
            batch[i] = i;
        }
        for (int i = batch.Length - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (batch[i], batch[j]) = (batch[j], batch[i]);
        }
        _order.AddRange(batch);
    }
}
