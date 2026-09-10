namespace InfiAir.Core.Talent;

/// <summary>天赋经济参数（默认值与 balance.json talent 段一致；服务层加载时覆盖）。</summary>
public sealed class TalentConfig
{
    // ---- 缓存溢出衰减（第四章）----
    /// <summary>安全阈值：缓存前 N 点不衰减。</summary>
    public int SafeThreshold { get; set; } = 20;

    /// <summary>每超出 1 点，该点价值衰减比例。</summary>
    public double DecayStep { get; set; } = 0.10;

    /// <summary>单点价值下限（90% 衰减上限）。</summary>
    public double DecayFloor { get; set; } = 0.10;

    // ---- 点数来源 ----
    public int PointsPerMilestone { get; set; } = 2;

    public int PointsPerBoss { get; set; } = 1;

    // ---- 递增消费（5.3）----
    /// <summary>下一级消耗 = Base + 当前等级 × Increment。</summary>
    public int CostBase { get; set; } = 2;

    public int CostIncrement { get; set; } = 1;

    // ---- 收益递减（5.2）：软上限后每级效率线性衰减（分段线性）----
    public double DiminishingStep { get; set; } = 0.25;

    /// <summary>软上限后单级效率下限。</summary>
    public double DiminishingFloor { get; set; } = 0.25;

    // ---- 机制 A 分支互斥锁 ----
    /// <summary>派系总投入达到阈值 → 对立派系上限降低。</summary>
    public int MutexThreshold { get; set; } = 5;

    public int MutexCapReduction { get; set; } = 2;

    // ---- 机制 B 专注惩罚 ----
    /// <summary>任一属性达到该等级触发全局折扣（其他属性有效层级按比例折扣）。</summary>
    public int FocusThreshold { get; set; } = 7;

    /// <summary>焦点属性每超出阈值一级，惩罚加深一档。</summary>
    public double FocusPenaltyPerLevel { get; set; } = 0.06;

    public double FocusPenaltyCap { get; set; } = 0.4;

    // ---- 机制 C 路线绑定 ----
    /// <summary>路线核心大类节点有效层级加成。</summary>
    public double RouteBonusLevels { get; set; } = 1.0;

    /// <summary>非路线大类上限减半后的下限（防 1 级节点被削到 0）。</summary>
    public int RouteCapFloor { get; set; } = 1;

    // ---- 机制 D 风险加点 ----
    public double OverchargeCostMult { get; set; } = 2.0;

    /// <summary>每局限次。</summary>
    public int OverchargeMaxPerRun { get; set; } = 3;
}

/// <summary>
/// 天赋点缓存池（第四章）：有序点值序列，尾部为新点；溢出安全阈值后按位置衰减
/// （LIFO——越新的点衰减越深，保护早期点数），衰减不可逆（花费后剩余部分不回满）。
/// 花费从尾部扣减（LIFO），不足整点的尾点保留部分价值。
/// </summary>
public sealed class TalentCache
{
    private readonly TalentConfig _config;
    private readonly List<double> _values = new();

    public TalentCache(TalentConfig config)
    {
        _config = config;
    }

    /// <summary>原始点数（含已衰减点）。</summary>
    public int Raw => _values.Count;

    /// <summary>有效点数（衰减后实际可用值）。</summary>
    public double Effective
    {
        get
        {
            var sum = 0.0;
            foreach (var v in _values)
            {
                sum += v;
            }

            return sum;
        }
    }

    /// <summary>入账 n 点（追加点值 1.0 后按位置重算衰减）。</summary>
    public void Grant(int n)
    {
        for (var i = 0; i < n; i++)
        {
            _values.Add(1.0);
        }

        ApplyDecay();
    }

    /// <summary>从尾部扣减 cost（LIFO）；有效点数不足返回 false 且不扣减。</summary>
    public bool Spend(double cost)
    {
        if (cost < 0.0 || Effective + 1e-9 < cost)
        {
            return false;
        }

        var remaining = cost;
        while (remaining > 1e-9 && _values.Count > 0)
        {
            var tail = _values[^1];
            if (tail > remaining)
            {
                _values[^1] = tail - remaining;
                remaining = 0.0;
            }
            else
            {
                _values.RemoveAt(_values.Count - 1);
                remaining -= tail;
            }
        }

        return true;
    }

    /// <summary>溢出衰减（每次增减后调用）：位置 ≥ SafeThreshold 的点按超出深度衰减，
    /// 取 min（历史衰减不可恢复——花费使总点数下降时，已衰减点不回满，维持溢出压力）。</summary>
    public void ApplyDecay()
    {
        for (var i = _config.SafeThreshold; i < _values.Count; i++)
        {
            var depth = i - _config.SafeThreshold + 1;
            var cap = Math.Max(_config.DecayFloor, 1.0 - _config.DecayStep * depth);
            if (_values[i] > cap)
            {
                _values[i] = cap;
            }
        }
    }

    public void Clear() => _values.Clear();

    /// <summary>缓存点值序列快照（读档用）：返回内部逐点值的副本（含已衰减尾值）。
    /// 缓存是衰减型 LIFO 点值序列，Grant/Spend 都无法回到某个历史状态，故读档必须整体还原原始序列。</summary>
    public List<double> Snapshot() => new(_values);

    /// <summary>整体还原点值序列（读档用）：覆盖式写入，不做衰减/裁剪——
    /// 快照已是历史衰减后的真实状态，重算会二次衰减。null/空 = 清空。</summary>
    public void RestoreValues(IEnumerable<double>? values)
    {
        _values.Clear();
        if (values == null)
        {
            return;
        }

        foreach (var v in values)
        {
            _values.Add(v);
        }
    }
}

/// <summary>天赋经济纯规则（消耗/上限/有效层级/惩罚；服务层与 UI 共用单一事实源）。</summary>
public static class TalentEconomy
{
    /// <summary>升到 level+1 级的消耗 = Base + level × Increment（5.3 递增消费）。</summary>
    public static int CostForLevel(TalentConfig config, int level) =>
        config.CostBase + Math.Max(level, 0) * config.CostIncrement;

    /// <summary>节点生效上限：结构上限 → 互斥锁扣减（机制 A）→ 路线契约减半（机制 C），下限钳 1。</summary>
    public static int EffectiveCap(TalentConfig config, int maxLevel, int mutexReduction, bool routeHalved)
    {
        var cap = maxLevel;
        if (mutexReduction > 0)
        {
            cap -= mutexReduction;
        }

        if (routeHalved)
        {
            cap /= 2;
        }

        return Math.Max(Math.Min(cap, maxLevel), config.RouteCapFloor);
    }

    /// <summary>
    /// 有效层级（收益递减 5.2 + 路线加成 6.3 + 专注惩罚 6.2）：
    /// 软上限内 1:1；超出后每级效率线性衰减到下限（分段线性）；
    /// 路线核心大类整体 +RouteBonusLevels；非焦点属性在专注惩罚触发时按 (1 - 惩罚) 折扣。
    /// </summary>
    public static double EffectiveLevel(
        TalentConfig config, int level, int softcap, bool routeCore, bool focusDiscounted, int focusOver)
    {
        var eff = (double)Math.Max(level, 0);
        for (var k = softcap + 1; k <= level; k++)
        {
            // 第 k 级相对第 k-1 级的效率：满级差 1.0 → 衰减到 DiminishingFloor
            eff -= 1.0 - Math.Max(config.DiminishingFloor, 1.0 - config.DiminishingStep * (k - softcap));
        }

        if (routeCore)
        {
            eff += config.RouteBonusLevels;
        }

        if (focusDiscounted && focusOver > 0)
        {
            eff *= 1.0 - Math.Min(config.FocusPenaltyCap, config.FocusPenaltyPerLevel * focusOver);
        }

        return Math.Max(eff, 0.0);
    }

    /// <summary>专注惩罚超限档数：焦点属性最高等级超阈值 1 级起算一档（未触发返回 0）。</summary>
    public static int FocusOver(int maxNodeLevel, TalentConfig config)
    {
        if (maxNodeLevel < config.FocusThreshold)
        {
            return 0;
        }

        return maxNodeLevel - config.FocusThreshold + 1;
    }

    /// <summary>机制 A：对立派系投入达阈值时，本派系节点的上限扣减量（未触发返回 0）。</summary>
    public static int MutexReduction(int opposingCategoryTotal, TalentConfig config) =>
        opposingCategoryTotal >= config.MutexThreshold ? config.MutexCapReduction : 0;

    /// <summary>机制 D 风险加点消耗：下一级消耗 × 倍率。</summary>
    public static int OverchargeCost(TalentConfig config, int baseCost) =>
        (int)Math.Ceiling(baseCost * config.OverchargeCostMult);
}
