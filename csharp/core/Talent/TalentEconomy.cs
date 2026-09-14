namespace InfiAir.Core.Talent;

/// <summary>天赋经济参数（默认值与 balance.json talent 段一致；服务层加载时覆盖）。</summary>
public sealed class TalentConfig
{
    // ---- 缓存溢出衰减（第四章）----
    /// <summary>安全阈值：缓存前 N 点不衰减。</summary>
    public int SafeThreshold { get; set; } = 30;

    /// <summary>每超出 1 点，该点价值衰减比例。</summary>
    public double DecayStep { get; set; } = 0.10;

    /// <summary>单点价值下限（90% 衰减上限）。</summary>
    public double DecayFloor { get; set; } = 0.10;

    /// <summary>正向回补步进：每次花费后，已衰减点按此比例回补（0 = 关闭，维持「衰减不可逆」）。
    /// 引入理由：StS 的稀有度保底用「每掉一张普通牌，稀有概率 +1%」做正向补偿，
    /// 而原实现是「超阈值即不可逆衰减」——惩罚 + 隐藏入口叠加后对新手是纯负面。
    /// 回补让「花掉点数」本身成为缓解手段，玩家不必先知道规则也不至于持续亏损。</summary>
    public double RecoveryStep { get; set; } = 0.25;

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

    /// <summary>从尾部扣减 cost（LIFO）；有效点数不足返回 false 且不扣减。
    /// cost 非有限值（NaN/±inf）或 ≤0 直接拒绝：NaN 比较恒 false，会让扣减循环空转并以 true 放行
    /// （买了一级却没扣点）；cost≤0 是空转花费，放行会触发正向回补、白涨 Effective
    /// （破坏「Spend 不得抬高 Effective」）。</summary>
    public bool Spend(double cost)
    {
        if (!double.IsFinite(cost) || cost <= 0.0 || Effective + 1e-9 < cost)
        {
            return false;
        }

        var before = Effective;
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

        // 正向回补：花掉点数即缓解溢出压力（RecoveryStep=0 时维持原「不可逆」语义）。
        // 回补总额以本次实际扣减量为预算——否则消耗一个低值点（如 0.1）后按比例回补其余点，
        // 有效点数会净增（花 0.1 反倒涨点），破坏「Spend 不得抬高 Effective」。
        ApplyRecovery(before - Effective);
        return true;
    }

    /// <summary>正向回补：把每个已衰减点的价值朝 1.0 抬 <see cref="TalentConfig.RecoveryStep"/> 的比例，
    /// 总额不超过本次花费实际扣减的 <paramref name="budget"/>（见 <see cref="Spend"/>）。
    /// 只抬曾经被衰减过的点（值 &lt; 1），不会把正常点抬过 1.0。</summary>
    private void ApplyRecovery(double budget)
    {
        var step = _config.RecoveryStep;
        if (step <= 0.0 || budget <= 0.0)
        {
            return;
        }

        step = Math.Min(step, 1.0);
        var recovered = 0.0;
        for (var i = 0; i < _values.Count && recovered < budget; i++)
        {
            if (_values[i] >= 1.0)
            {
                continue;
            }

            var gain = (1.0 - _values[i]) * step;
            if (recovered + gain > budget)
            {
                gain = budget - recovered;
            }

            _values[i] += gain;
            recovered += gain;
        }
    }

    /// <summary>溢出衰减（每次入账后调用）：位置 ≥ <see cref="TalentConfig.SafeThreshold"/> 的点按超出深度衰减，取 min。
    /// 阈值负值（手改配置/坏档）视作 0 起点——直接拿负阈值当数组下标会抛 IndexOutOfRangeException
    /// 击穿入账路径，而负阈值语义上就是「无豁免」。
    /// 衰减本身不可逆（Grant 后不自动回涨），但 <see cref="Spend"/> 会触发正向回补
    /// （RecoveryStep）——花掉点数即缓解溢出，玩家有明确的自救手段。</summary>
    public void ApplyDecay()
    {
        var safeThreshold = Math.Max(_config.SafeThreshold, 0);
        for (var i = safeThreshold; i < _values.Count; i++)
        {
            var depth = i - safeThreshold + 1;
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

    /// <summary>整体还原点值序列（读档用）：覆盖式写入，不做衰减/回补/裁剪——
    /// 快照已是历史衰减后的真实状态，重算会二次衰减、回补会凭空加点。null/空 = 清空。
    /// 非有限或负值钳为 0：点值序列若混入 NaN，<see cref="Effective"/> 会变 NaN，
    /// 使花费判据恒假、<see cref="Spend"/> 空转放行——手改存档可借此白拿天赋。</summary>
    public void RestoreValues(IEnumerable<double>? values)
    {
        _values.Clear();
        if (values == null)
        {
            return;
        }

        foreach (var v in values)
        {
            _values.Add(double.IsFinite(v) && v > 0.0 ? v : 0.0);
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
    /// 路线核心大类中已投入（Lv≥1）的节点 +RouteBonusLevels；未投入节点有效层级恒为 0；
    /// 非焦点属性在专注惩罚触发时按 (1 - 惩罚) 折扣。
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

        // 路线加成只作用于已投入的节点：未投入（level 0）节点的有效层级必须是 0。
        // 否则绑定路线即凭空获得该大类全部节点的「一级效果」——未买 homing 也每发制导、
        // 未买 deflector 也有偏转冷却乘区，且面板同时显示「已投入 0 点」，自相矛盾。
        if (routeCore && level > 0)
        {
            eff += config.RouteBonusLevels;
        }

        if (focusDiscounted && focusOver > 0)
        {
            eff *= 1.0 - Math.Min(config.FocusPenaltyCap, config.FocusPenaltyPerLevel * focusOver);
        }

        return Math.Max(eff, 0.0);
    }

    /// <summary>专注惩罚超限档数：焦点属性最高等级超阈值 1 级起算一档（未触发返回 0）。
    ///
    /// **生效面是显式约束，不是意外**（人类已决策保留阈值 7，承认该设计约束）：
    /// 阈值 7 现网**只对 extra_life 生效**——其结构上限 10 是唯一可能达阈值的节点；
    /// 其余节点上限 ≤5，风险加点 +1 后 ≤6。即「专注惩罚」实质是「把 extra_life 点高的代价」。
    /// 若日后放宽任一节点等级上限，须同步重校 threshold 与惩罚曲线——该不变量由
    /// <c>FocusTriggerSurface_IsPinned</c> 单测护栏钉住（改动触发面即红，强制走一次显式决策）。</summary>
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

    /// <summary>
    /// 有效缓存是否已够点亮「当前任一可选节点」：返回其中最便宜的下一级价，0 = 尚不可购。
    /// 入参 eligibleNextCosts 由引擎侧给出——只含前置/互斥/上限/风险加点名额都通过、仅「点数够不够」
    /// 未定的节点（TalentService 逐节点筛后取 NextCost）；本函数只判价格与缓存的比较，
    /// 属可单测的纯判定。空表（无任何可选节点）返回 0——提示不应在没有可买项时亮。
    /// </summary>
    public static int CheapestAffordable(IReadOnlyList<int> eligibleNextCosts, double effectiveCache)
    {
        var cheapest = 0;
        for (var i = 0; i < eligibleNextCosts.Count; i++)
        {
            var cost = eligibleNextCosts[i];
            if (cost <= 0 || cost > effectiveCache + 1e-9)
            {
                continue;  // 0 = 已锁定/无价（NextCost 契约），负价非法；超出缓存跳过
            }

            if (cheapest == 0 || cost < cheapest)
            {
                cheapest = cost;
            }
        }

        return cheapest;
    }
}
