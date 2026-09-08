using Godot;
using InfiAir.Core.Talent;

namespace InfiAir;

/// <summary>
/// 天赋缓存域服务（天赋缓存系统重构，2026-09-07）：里程碑/Boss 击杀 → 点数入缓存池（LIFO 溢出衰减，
/// 不弹窗）→ 玩家经天赋面板自主加点（递增消耗/收益递减/互斥锁/专注惩罚/路线契约/风险加点）。
/// 替代旧「里程碑三选一 BuffSelect」：天赋节点 id = 既有 buff id，最终层级同步进
/// CombatStateService.Buffs——Player/Bullet/PlayerDamage/HUD 坞等全部效果消费端零改动；
/// 浮点有效层级（收益递减/路线加成/专注折扣后）经 EffLevel 供乘算类效果（Player.RefreshBuffFactors）。
/// Godot 绑定层：配置经 GameState.Instance.Cfg 缓存（LoadTalentConfig，ApplyBalance 调用）；
/// RP 消费经 Instance 跨域（与 MetaService 同构）。门面：GameState.Talent.cs 转发 + Talent 属性。
/// 信号：C# 事件 CacheChanged/TalentsChanged → GameState 订阅转发为 TalentCacheChanged/TalentsChanged；
/// 层级写入 Buffs 后经 Instance 直发 BuffsChanged（ChooseRoute 先例），驱动 Player/HUD 缓存刷新。
/// </summary>
public sealed partial class TalentService : RefCounted
{

    private readonly TalentConfig _config = new();
    private readonly TalentCache _cache;

    /// <summary>节点最终层级（含 Meta 研究所起始预置；shield 等消耗型 buff 由 ConsumeBuff 在
    /// Buffs 内扣减运行层，不影响本表的已购层级）。</summary>
    private readonly Dictionary<StringName, int> _levels = new();

    /// <summary>已风险加点节点（机制 D：永久锁定不可再升级）。</summary>
    private readonly HashSet<StringName> _overcharged = new();

    /// <summary>结构上限回退表（与旧 BuffSelect 池一致；balance.json buffs.&lt;id&gt;.max_stacks 为唯一权威）。</summary>
    private static readonly Dictionary<string, int> MaxLevelFallbacks = new()
    {
        ["power_shot"] = 5,
        ["rapid_fire"] = 4,
        ["spread_shot"] = 2,
        ["extra_life"] = 10,
        ["regen"] = 1,
        ["piercing"] = 2,
        ["explosive"] = 1,
        ["lifesteal"] = 1,
        ["armor"] = 1,
        ["evasion"] = 1,
        ["phase_dash"] = 3,
        ["slow_field"] = 1,
        ["efficient_boost"] = 2,
        ["laser_beam"] = 1,
        ["boost_recovery"] = 2,
        ["mothership_recall"] = 2,
        ["crit_shot"] = 3,
        ["shield"] = 2,
        ["bullet_speed"] = 3,
    };

    private readonly Dictionary<StringName, int> _maxLevels = new();
    private readonly Dictionary<StringName, int> _softcaps = new();

    /// <summary>路线契约（机制 C）："" = 未绑定；切换消耗重置代币。</summary>
    private string _route = "";

    private int _resetTokens;

    /// <summary>配置注入后面板/UI 可直接读取经济参数（阈值/衰减/消耗曲线）。</summary>
    public TalentConfig Config => _config;

    public TalentService()
    {
        _cache = new TalentCache(_config);
    }

    // ---------------- 配置缓存（ApplyBalance 调用；键走 Cfg 静态调用 → BALANCE_MAP 收录） ----------------

    public void LoadTalentConfig()
    {
        var gs = GameState.Instance;
        _config.SafeThreshold = Math.Max((int)gs.Cfg("talent.cache.safe_threshold", 20).AsInt64(), 1);
        _config.DecayStep = Mathf.Clamp(gs.Cfg("talent.cache.decay_step", 0.10).AsDouble(), 0.0, 1.0);
        _config.DecayFloor = Mathf.Clamp(gs.Cfg("talent.cache.decay_floor", 0.10).AsDouble(), 0.01, 1.0);
        _config.PointsPerMilestone = Math.Max((int)gs.Cfg("talent.grant.points_per_milestone", 2).AsInt64(), 0);
        _config.PointsPerBoss = Math.Max((int)gs.Cfg("talent.grant.points_per_boss", 1).AsInt64(), 0);
        _config.CostBase = Math.Max((int)gs.Cfg("talent.cost.base", 2).AsInt64(), 1);
        _config.CostIncrement = Math.Max((int)gs.Cfg("talent.cost.increment", 1).AsInt64(), 0);
        var softcapDefault = Math.Max((int)gs.Cfg("talent.cost.softcap", 3).AsInt64(), 1);
        _config.DiminishingStep = Mathf.Clamp(gs.Cfg("talent.diminishing.step", 0.25).AsDouble(), 0.0, 1.0);
        _config.DiminishingFloor = Mathf.Clamp(gs.Cfg("talent.diminishing.floor", 0.25).AsDouble(), 0.01, 1.0);
        _config.MutexThreshold = Math.Max((int)gs.Cfg("talent.mutex.threshold", 5).AsInt64(), 1);
        _config.MutexCapReduction = Math.Max((int)gs.Cfg("talent.mutex.cap_reduction", 2).AsInt64(), 0);
        _config.FocusThreshold = Math.Max((int)gs.Cfg("talent.focus.threshold", 7).AsInt64(), 2);
        _config.FocusPenaltyPerLevel = Mathf.Clamp(gs.Cfg("talent.focus.penalty_per_level", 0.06).AsDouble(), 0.0, 1.0);
        _config.FocusPenaltyCap = Mathf.Clamp(gs.Cfg("talent.focus.penalty_cap", 0.4).AsDouble(), 0.0, 1.0);
        _config.RouteBonusLevels = Math.Max(gs.Cfg("talent.route.bonus_levels", 1.0).AsDouble(), 0.0);
        _config.RouteCapFloor = Math.Max((int)gs.Cfg("talent.route.cap_floor", 1).AsInt64(), 1);
        ResetTokenCost = Math.Max((int)gs.Cfg("talent.route.reset_token_cost", 6).AsInt64(), 1);
        _config.OverchargeCostMult = Math.Max(gs.Cfg("talent.overcharge.cost_mult", 2.0).AsDouble(), 1.0);
        _config.OverchargeMaxPerRun = Math.Max((int)gs.Cfg("talent.overcharge.max_per_run", 3).AsInt64(), 0);

        // 节点上限/软上限：balance.json 为唯一权威，回退默认须与 json 定稿值一致
        _maxLevels.Clear();
        _softcaps.Clear();
        foreach (var id in TalentTree.NodeIds())
        {
            var idSn = new StringName(id);
            var max = Math.Max((int)gs.Cfg("buffs." + id + ".max_stacks", MaxLevelFallbacks.GetValueOrDefault(id, 1)).AsInt64(), 1);
            _maxLevels[idSn] = max;
            var softcap = (int)gs.Cfg("talent.softcaps." + id, softcapDefault).AsInt64();
            _softcaps[idSn] = Mathf.Clamp(softcap, 1, max);
        }
    }

    /// <summary>重置代币 RP 售价（基地补给；LoadTalentConfig 缓存）。</summary>
    public int ResetTokenCost { get; private set; } = 6;

    // ---------------- C# 事件（GameState 订阅转发为信号） ----------------

    /// <summary>缓存池变化（Grant/Spend/Restore/Reset）。</summary>
    public event Action<double, int>? CacheChanged;

    /// <summary>天赋配置变化（加点/路线/代币/复位）。</summary>
    public event Action? TalentsChanged;

    // ---------------- 缓存池（第四章） ----------------

    public int RawCache => _cache.Raw;

    public double EffectiveCache => _cache.Effective;

    /// <summary>溢出衰减压力预览（UI 悬停提示用）：满员后每点实际价值。</summary>
    public double TailValue()
    {
        var raw = _cache.Raw;
        if (raw <= _config.SafeThreshold)
        {
            return 1.0;
        }

        var depth = raw - _config.SafeThreshold;
        return Math.Max(_config.DecayFloor, 1.0 - _config.DecayStep * depth);
    }

    public void Grant(int points)
    {
        if (points <= 0)
        {
            return;
        }

        _cache.Grant(points);
        CacheChanged?.Invoke(_cache.Effective, _cache.Raw);
    }

    /// <summary>里程碑触达入账（GameState.OnScoreMilestoneReached 调用；不弹窗，仅亮指示器）。</summary>
    public void GrantForMilestone() => Grant(_config.PointsPerMilestone);

    /// <summary>Boss 击杀入账（GameState.AddBossKill 编排调用）。</summary>
    public void GrantForBoss() => Grant(_config.PointsPerBoss);

    // ---------------- 节点状态查询 ----------------

    public int Level(StringName id) => _levels.GetValueOrDefault(id, 0);

    public bool IsOvercharged(StringName id) => _overcharged.Contains(id);

    public int MaxLevel(StringName id) => _maxLevels.GetValueOrDefault(id, 1);

    public int Softcap(StringName id) => _softcaps.GetValueOrDefault(id, MaxLevel(id));

    public string Route => _route;

    public int ResetTokens => _resetTokens;

    public int OverchargeUsed => _overcharged.Count;

    /// <summary>大类投入总量（互斥锁阈值判定）。</summary>
    public int CategoryTotal(string categoryId)
    {
        var total = 0;
        foreach (var line in TalentTree.Category(categoryId).Lines)
        {
            foreach (var nodeId in line.NodeIds)
            {
                total += Level(nodeId);
            }
        }

        return total;
    }

    /// <summary>机制 A：对立派系投入达阈值时本节点上限扣减量。</summary>
    public int MutexReductionFor(StringName id)
    {
        var def = TalentTree.Find(id.ToString());
        if (def == null)
        {
            return 0;
        }

        var opposing = TalentTree.OpposingCategoryOf(def.CategoryId);
        return opposing == null ? 0 : TalentEconomy.MutexReduction(CategoryTotal(opposing), _config);
    }

    /// <summary>机制 C：路线已绑定且本节点大类非核心 → 上限减半。</summary>
    public bool RouteHalvedFor(StringName id)
    {
        if (_route == "")
        {
            return false;
        }

        var def = TalentTree.Find(id.ToString());
        var route = FindRoute(_route);
        return def != null && route != null && route.CoreCategoryId != def.CategoryId;
    }

    public bool RouteCoreFor(StringName id)
    {
        if (_route == "")
        {
            return false;
        }

        var def = TalentTree.Find(id.ToString());
        var route = FindRoute(_route);
        return def != null && route != null && route.CoreCategoryId == def.CategoryId;
    }

    /// <summary>节点生效上限：结构上限 → 互斥扣减 → 路线减半（下限钳制在 Economy 内）。</summary>
    public int CapFor(StringName id) =>
        TalentEconomy.EffectiveCap(_config, MaxLevel(id), MutexReductionFor(id), RouteHalvedFor(id));

    /// <summary>全局焦点超限档数（机制 B；未触发 0）。</summary>
    public int FocusOver()
    {
        var max = 0;
        foreach (var kv in _levels)
        {
            max = Math.Max(max, kv.Value);
        }

        return TalentEconomy.FocusOver(max, _config);
    }

    /// <summary>本节点是否处于专注折扣侧（焦点属性 = 当前最高层节点，并列时全部豁免）。</summary>
    public bool FocusDiscounted(StringName id)
    {
        var over = FocusOver();
        if (over <= 0)
        {
            return false;
        }

        var max = _config.FocusThreshold + over - 1;
        return Level(id) < max;
    }

    /// <summary>
    /// 浮点有效层级（效果桥）：收益递减 + 路线核心加成 + 专注折扣后的乘算层级。
    /// Player.RefreshBuffFactors/Main（母舰召回冷却）等乘算效果据此求值；
    /// 盾层/穿透等整数语义消费端仍读 Buffs（本表 SyncBuff 的整数层级）。
    /// </summary>
    public double EffLevel(StringName id) =>
        TalentEconomy.EffectiveLevel(_config, Level(id), Softcap(id), RouteCoreFor(id), FocusDiscounted(id), FocusOver());

    /// <summary>前置链检查：支线内上一节点 Lv≥1（根节点恒真）。</summary>
    public bool PrerequisiteMet(StringName id)
    {
        var prev = TalentTree.Prerequisite(id.ToString());
        return prev == null || Level(prev) > 0;
    }

    // ---------------- 加点（升级 / 风险加点） ----------------

    /// <summary>升级拦截原因（UI 禁用与提示文案键后缀）："" = 可升级。</summary>
    public string UpgradeBlockReason(StringName id)
    {
        if (TalentTree.Find(id.ToString()) == null)
        {
            return "UNKNOWN";
        }

        if (IsOvercharged(id))
        {
            return "OVERCHARGED";
        }

        if (!PrerequisiteMet(id))
        {
            return "PREREQ";
        }

        var level = Level(id);
        var cap = CapFor(id);
        if (level >= cap)
        {
            // 上限侧：可走风险加点（次数未满）否则顶满
            return _overcharged.Count < _config.OverchargeMaxPerRun ? "" : "OVERCHARGE_LIMIT";
        }

        if (_cache.Effective + 1e-9 < TalentEconomy.CostForLevel(_config, level))
        {
            return "CACHE";
        }

        return "";
    }

    /// <summary>下一级消耗（已到生效上限时为风险加点双倍价；已永久锁定返回 0）。</summary>
    public int NextCost(StringName id)
    {
        if (IsOvercharged(id))
        {
            return 0;
        }

        var level = Level(id);
        var cost = TalentEconomy.CostForLevel(_config, level);
        return level >= CapFor(id) ? TalentEconomy.OverchargeCost(_config, cost) : cost;
    }

    /// <summary>是否为风险加点购买（已达生效上限的最后一档）。</summary>
    public bool IsOverchargePurchase(StringName id)
    {
        var level = Level(id);
        return !IsOvercharged(id) && PrerequisiteMet(id) && level >= CapFor(id);
    }

    /// <summary>加点主入口：扣缓存 → 层级 +1 → 同步 Buffs；上限档自动走风险加点（双倍价 + 永久锁定 + 局限次）。
    /// 失败返回 false 且无副作用。</summary>
    public bool Upgrade(StringName id)
    {
        if (UpgradeBlockReason(id) != "")
        {
            return false;
        }

        var level = Level(id);
        var cost = NextCost(id);
        if (!_cache.Spend(cost))
        {
            return false;
        }

        var overcharge = level >= CapFor(id);
        _levels[id] = level + 1;
        if (overcharge)
        {
            _overcharged.Add(id);
        }

        SyncBuff(id);
        if (id == new StringName("extra_life"))
        {
            // 沿袭旧 pick 语义：购入即时回血（heal_on_pick，上限随层数自动生效）
            GameState.Instance.Heal(GameState.Instance.Cfg("buffs.extra_life.heal_on_pick", 30).AsDouble());
        }

        GameState.Instance.PlaySfx(SfxId.BuffPick);
        GameState.Instance.EmitSignal(GameState.SignalName.BuffsChanged);
        CacheChanged?.Invoke(_cache.Effective, _cache.Raw);
        TalentsChanged?.Invoke();
        return true;
    }

    // ---------------- 路线契约（机制 C） ----------------

    /// <summary>绑定路线：首次免费；已有路线时切换消耗 1 枚重置代币。非核心大类上限减半随绑定即时生效。</summary>
    public bool ChooseRoute(string routeId)
    {
        if (FindRoute(routeId) == null || routeId == _route)
        {
            return false;
        }

        if (_route != "")
        {
            if (_resetTokens <= 0)
            {
                return false;
            }

            _resetTokens -= 1;
        }

        _route = routeId;
        GameState.Instance.PlaySfx(SfxId.BuffPick);
        // 有效层级与上限同时变化：BuffsChanged 驱动 Player.RefreshBuffFactors 重算乘算缓存
        GameState.Instance.EmitSignal(GameState.SignalName.BuffsChanged);
        TalentsChanged?.Invoke();
        return true;
    }

    /// <summary>基地补给购置重置代币（RP 结算经 Instance 跨域；余额不足返回 false）。</summary>
    public bool BuyResetToken()
    {
        if (!GameState.Instance.SpendRp(ResetTokenCost))
        {
            return false;
        }

        _resetTokens += 1;
        TalentsChanged?.Invoke();
        return true;
    }

    private static InfiAir.Core.Talent.TalentRouteDef? FindRoute(string routeId)
    {
        foreach (var route in TalentTree.Routes)
        {
            if (route.Id == routeId)
            {
                return route;
            }
        }

        return null;
    }

    // ---------------- Buffs 同步（效果桥） ----------------

    /// <summary>已购层级写入 CombatStateService.Buffs（0 层移除键）；消耗型盾层的运行扣减不回写本表。</summary>
    private void SyncBuff(StringName id)
    {
        var buffs = GameState.Instance.Buffs;
        var level = Level(id);
        if (level > 0)
        {
            buffs[id] = level;
        }
        else
        {
            buffs.Remove(id);
        }
    }

    private void SyncAllBuffs()
    {
        var buffs = GameState.Instance.Buffs;
        buffs.Clear();
        foreach (var kv in _levels)
        {
            if (kv.Value > 0)
            {
                buffs[kv.Key] = kv.Value;
            }
        }
    }

    // ---------------- 生命周期 / 存档 ----------------

    /// <summary>Meta 研究所起始预置（Main.ApplyNewRun 经门面调用）：升级项 → 起始层级。
    /// 与旧 ApplyMetaLoadout 语义一致（直接落 Buffs），但归口本服务保持单一事实源。</summary>
    public void ApplyStartingLoadout(Godot.Collections.Dictionary metaUpgrades)
    {
        var applied = false;
        foreach (var key in metaUpgrades.Keys)
        {
            var level = (int)metaUpgrades[key].AsInt64();
            var id = key.AsStringName();
            if (level > 0 && _maxLevels.ContainsKey(id))
            {
                _levels[id] = Mathf.Clamp(Mathf.Max(Level(id), level), 0, MaxLevel(id));
                applied = true;
            }
        }

        if (applied)
        {
            SyncAllBuffs();
            GameState.Instance.EmitSignal(GameState.SignalName.BuffsChanged);
            TalentsChanged?.Invoke();
        }
    }

    /// <summary>对局复位（ResetRun 调用）：缓存/层级/路线/代币/超载全部清空（不发信号——
    /// BuffsChanged 由 ResetRun 末尾直发，CacheChanged/TalentsChanged 由本方法尾播发一次）。</summary>
    public void ResetAll()
    {
        _cache.Clear();
        _levels.Clear();
        _overcharged.Clear();
        _route = "";
        _resetTokens = 0;
        CacheChanged?.Invoke(0.0, 0);
        TalentsChanged?.Invoke();
    }

    /// <summary>存盘快照（SaveRun 调用；点值序列直存，恢复侧判型钳制）。</summary>
    public Godot.Collections.Dictionary SaveState()
    {
        var cacheArr = new Godot.Collections.Array<double>();
        foreach (var v in _cache.Snapshot())
        {
            cacheArr.Add(Math.Round(v, 4));
        }

        var levels = new Godot.Collections.Dictionary();
        foreach (var kv in _levels)
        {
            if (kv.Value > 0)
            {
                levels[kv.Key] = kv.Value;
            }
        }

        var overcharged = new Godot.Collections.Array<StringName>();
        foreach (var id in _overcharged)
        {
            overcharged.Add(id);
        }

        return new Godot.Collections.Dictionary
        {
            ["cache"] = cacheArr,
            ["levels"] = levels,
            ["overcharged"] = overcharged,
            ["route"] = _route,
            ["tokens"] = _resetTokens,
        };
    }

    /// <summary>存档恢复（ApplyRunSave 调用；缺键 = 旧版存档 → 全新天赋态，无兼容层）。
    /// 手改防御：层级钳 [0, MaxLevel(+1 超载档)]、路线/超载集合校验成员资格、点值钳 [0,1]。</summary>
    public void RestoreState(Godot.Collections.Dictionary data)
    {
        var gs = GameState.Instance;
        _cache.Clear();
        var cacheV = data.GetValueOrDefault("cache", new Variant());
        if (cacheV.VariantType == Variant.Type.Array)
        {
            var restored = new List<double>();
            foreach (var v in cacheV.AsGodotArray())
            {
                var num = gs.SaveNum(v, 0.0);
                if (num > 0.0)
                {
                    restored.Add(Mathf.Clamp(num, 0.0, 1.0));
                }
            }

            _cache.Restore(restored);
        }

        _levels.Clear();
        var levelsV = data.GetValueOrDefault("levels", new Variant());
        if (levelsV.VariantType == Variant.Type.Dictionary)
        {
            foreach (var key in levelsV.AsGodotDictionary().Keys)
            {
                var id = key.AsStringName();
                if (!_maxLevels.ContainsKey(id))
                {
                    continue;
                }

                var v = levelsV.AsGodotDictionary()[key];
                if (v.VariantType is Variant.Type.Int or Variant.Type.Float)
                {
                    _levels[id] = Mathf.Clamp((int)v.AsInt64(), 0, MaxLevel(id) + 1);
                }
            }
        }

        _overcharged.Clear();
        var overV = data.GetValueOrDefault("overcharged", new Variant());
        if (overV.VariantType == Variant.Type.Array)
        {
            foreach (var v in overV.AsGodotArray())
            {
                if (v.VariantType is Variant.Type.String or Variant.Type.StringName)
                {
                    var id = new StringName(v.ToString());
                    if (_maxLevels.ContainsKey(id))
                    {
                        _overcharged.Add(id);
                    }
                }
            }
        }

        var routeV = data.GetValueOrDefault("route", "");
        var route = routeV.VariantType is Variant.Type.String or Variant.Type.StringName ? routeV.ToString() : "";
        _route = FindRoute(route)?.Id ?? "";

        _resetTokens = gs.SaveInt(data.GetValueOrDefault("tokens", 0), 0);

        SyncAllBuffs();
        CacheChanged?.Invoke(_cache.Effective, _cache.Raw);
        TalentsChanged?.Invoke();
    }

    // ---------------- A7：测试/诊断公开端口 ----------------

    public void TestGrant(int points) => Grant(points);

    /// <summary>层级直写（教程关卡授予/测试垫层）：含 Buffs 同步与广播，与加点同口径但不扣缓存。</summary>
    public void GrantLevel(StringName id, int level)
    {
        if (!_maxLevels.ContainsKey(id))
        {
            return;
        }

        _levels[id] = Mathf.Clamp(level, 0, MaxLevel(id));
        SyncBuff(id);
        GameState.Instance.EmitSignal(GameState.SignalName.BuffsChanged);
        TalentsChanged?.Invoke();
    }

    public void TestSetLevel(StringName id, int level) => GrantLevel(id, level);

    public void TestSetTokens(int tokens) => _resetTokens = Math.Max(tokens, 0);
}
