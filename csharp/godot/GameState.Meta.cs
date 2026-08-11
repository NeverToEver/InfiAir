using Godot;
using InfiAir.Core.Meta;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（局外成长 Meta Progression，2026-08-09 计划 M3）：
/// 科技点死亡结算 / 升级消费 / 新局开局预置 buff 层数。
/// 数据模型与公式在 InfiAir.Core.Meta（纯逻辑，xUnit 直测）；本文件为 Godot 绑定层：
/// UserDb meta 档案读写 + balance.json meta 节配置缓存 + Buffs 预置应用。
/// 仅登录用户（游客不持久化，B7-8 口径延伸）；结算唯一入口 = SettleRun（死亡）——
/// AC26（2026-08-11 审计订正）：ExitConfirm 删档不结算；K 键自毁（give_up）经 PlayerDied
/// 按死亡正常结算——防刷点口径仅限删档退出（与实现/DESIGN_BASELINE 一致）。
/// 详见 docs/archive/2026-08-09-meta-progression-plan.md。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 局外成长（2026-08-09） ----------------

    /// <summary>科技点余额（登录用户会话态；每次变更即落盘 UserDb）</summary>
    public long TechPoints { get; private set; }

    /// <summary>已购升级：buff id → 等级（登录用户会话态）</summary>
    private readonly Godot.Collections.Dictionary _metaUpgrades = new();

    // meta 节配置缓存（ApplyBalance 加载；热路径禁 cfg 约定）
    private long _metaScoreDivisor = 1000;
    private long _metaBossKillBonus = 2;
    private long _metaMissionBonus = 1;
    private readonly Dictionary<StringName, UpgradeDef> _metaDefs = new();

    /// <summary>会话 meta 档案加载（LoadSessionSettings/登出/游客切换调用；非登录用户清空内存态）</summary>
    private void LoadMeta()
    {
        TechPoints = 0;
        _metaUpgrades.Clear();
        if (CurrentUser == "" || IsGuest())
        {
            return;
        }

        var meta = _userDb.GetUserMeta(CurrentUser);
        TechPoints = Math.Max((long)SaveNum(meta.GetValueOrDefault("tech_points", 0), 0.0), 0);
        var upgrades = meta.GetValueOrDefault("upgrades", new Variant());
        if (upgrades.VariantType == Variant.Type.Dictionary)
        {
            foreach (var key in upgrades.AsGodotDictionary().Keys)
            {
                var v = upgrades.AsGodotDictionary()[key];
                if (v.VariantType is not Variant.Type.Int and not Variant.Type.Float)
                {
                    continue;
                }

                var id = key.AsStringName();
                if (_metaDefs.TryGetValue(id, out var def))
                {
                    // 手改超上限钳制到 max_level（防御；正常路径由 SpendTechPoints 保证）
                    _metaUpgrades[id] = Math.Clamp((int)v.AsInt64(), 0, def.MaxLevel);
                }
            }
        }

        EmitSignal(SignalName.TechPointsChanged, TechPoints);
    }

    /// <summary>meta 节配置缓存（ApplyBalance 调用；键走 Cfg 静态调用 → BALANCE_MAP 收录）</summary>
    private void LoadMetaConfig()
    {
        _metaScoreDivisor = Math.Max((long)Cfg("meta.points.score_divisor", _metaScoreDivisor).AsInt64(), 1);
        _metaBossKillBonus = Math.Max((long)Cfg("meta.points.boss_kill_bonus", _metaBossKillBonus).AsInt64(), 0);
        _metaMissionBonus = Math.Max((long)Cfg("meta.points.mission_bonus", _metaMissionBonus).AsInt64(), 0);
        _metaDefs.Clear();
        var upgrades = Cfg("meta.upgrades", new Godot.Collections.Dictionary());
        if (upgrades.VariantType != Variant.Type.Dictionary)
        {
            return;
        }

        foreach (var key in upgrades.AsGodotDictionary().Keys)
        {
            var v = upgrades.AsGodotDictionary()[key];
            if (v.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            var d = v.AsGodotDictionary();
            var id = key.AsStringName();
            // AC14（2026-08-11 健壮性审查）：max_level 钳 [1, int.MaxValue]——裸 (int) 截断超 2^31
            // 值回绕为负 → 升级恒判满级/显示错乱（静默限 Lv1 语义）；先钳 long 域再转 int（SaveInt 同款）
            var maxLevel = (int)Math.Max(Math.Min((long)SaveNum(d.GetValueOrDefault("max_level", 1), 1.0), int.MaxValue), 1);
            var baseCost = SaveNum(d.GetValueOrDefault("base_cost", 0), 0.0);
            var growth = SaveNum(d.GetValueOrDefault("cost_growth", 1.5), 1.5);
            _metaDefs[id] = new UpgradeDef(id.ToString(), maxLevel, baseCost, growth);
        }
    }

    /// <summary>死亡结算科技点（SettleRun 内调用，本局唯一结算入口；游客/未登录/零收益跳过）。
    /// 公式见 core MetaProgression.PointsForRun；任务按已领取（claimed）计。</summary>
    public void SettleTechPoints()
    {
        if (CurrentUser == "" || IsGuest() || !_userDb.UserExists(CurrentUser))
        {
            return;
        }

        var claimed = 0;
        foreach (var id in Missions.Keys)
        {
            if (IsMissionClaimed(id.AsStringName()))
            {
                claimed += 1;
            }
        }

        var points = MetaProgression.PointsForRun(
            Score, BossKills, claimed, _metaScoreDivisor, _metaBossKillBonus, _metaMissionBonus);
        if (points <= 0)
        {
            return;
        }

        // 溢出钳制（对齐 core 语义；正常路径不可达）
        TechPoints = TechPoints > long.MaxValue - points ? long.MaxValue : TechPoints + points;
        PersistMeta();
        EmitSignal(SignalName.TechPointsChanged, TechPoints);
    }

    /// <summary>升级项 id 列表（UI 列表构建；顺序 = balance.json meta.upgrades 声明序）。</summary>
    public Godot.Collections.Array<StringName> MetaUpgradeIds()
    {
        var arr = new Godot.Collections.Array<StringName>();
        foreach (var id in _metaDefs.Keys)
        {
            arr.Add(id);
        }

        return arr;
    }

    /// <summary>升级项满级等级（UI 显示 Lv.x/y；未知项回 0）。</summary>
    public int MetaMaxLevel(StringName id) => _metaDefs.TryGetValue(id, out var def) ? def.MaxLevel : 0;

    /// <summary>当前等级（未购/未知项回 0）。</summary>
    public int MetaLevel(StringName id) => (int)_metaUpgrades.GetValueOrDefault(id, 0).AsInt64();

    /// <summary>升到下一级的费用（已满级/未知项回 0）。</summary>
    public long MetaUpgradeCost(StringName id)
    {
        var def = _metaDefs.GetValueOrDefault(id);
        return def != null ? MetaProgression.CostForLevel(def, MetaLevel(id) + 1) : 0;
    }

    /// <summary>是否可升级（未登录用户仅读余额/等级，消费走 SpendTechPoints 守卫）。</summary>
    public bool CanUpgradeMeta(StringName id) =>
        MetaProgression.CanUpgrade(_metaDefs.GetValueOrDefault(id), MetaLevel(id));

    /// <summary>消费科技点升级（未登录/余额不足/已满级返回 false）；成功即时落盘 UserDb。</summary>
    public bool SpendTechPoints(StringName id)
    {
        if (CurrentUser == "" || IsGuest())
        {
            return false;
        }

        var def = _metaDefs.GetValueOrDefault(id);
        var level = MetaLevel(id);
        if (!MetaProgression.CanUpgrade(def, level))
        {
            return false;
        }

        var cost = MetaProgression.CostForLevel(def, level + 1);
        if (TechPoints < cost)
        {
            return false;
        }

        TechPoints -= cost;
        _metaUpgrades[id] = level + 1;
        PersistMeta();
        EmitSignal(SignalName.TechPointsChanged, TechPoints);
        return true;
    }

    /// <summary>新局开局预置：已购升级 → Buffs 初始层数（Main.ApplyNewRun 调用；
    /// tutorial/存档恢复路径不经过——教程隔离、继续对局 buffs 从存档恢复，均不预置）。</summary>
    public void ApplyMetaLoadout()
    {
        if (CurrentUser == "" || IsGuest())
        {
            return;
        }

        var applied = false;
        foreach (var key in _metaUpgrades.Keys)
        {
            var level = (int)_metaUpgrades[key].AsInt64();
            if (level > 0)
            {
                Buffs[key.AsStringName()] = level;
                applied = true;
            }
        }

        if (applied)
        {
            // 2026-08-10 审查修复：实际写入层数后须广播 buffs_changed——Player.RefreshBuffFactors/
            // Hud.RebuildBuffDock 为缓存+信号驱动且 _Ready 阶段已跑过，直写 Buffs 不发信号会让
            // meta 预置 buff（如 crit_shot）整局不生效、HUD 不显示，直到首次里程碑选 buff 自愈；
            // 与 AddBuff/ConsumeBuff/ApplyRunSave 口径一致（仅实际写入时发，无升级账户不空广播）
            EmitSignal(SignalName.BuffsChanged);
        }
    }

    /// <summary>meta 档案落盘（每次变更即时写，防掉档）。</summary>
    private void PersistMeta()
    {
        if (CurrentUser == "" || IsGuest())
        {
            return;
        }

        _userDb.UpdateUserMeta(CurrentUser, new Godot.Collections.Dictionary
        {
            ["tech_points"] = TechPoints,
            ["upgrades"] = _metaUpgrades.Duplicate(),
        });
    }
}
