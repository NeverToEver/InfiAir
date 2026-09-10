using Godot;

namespace InfiAir;

/// <summary>
/// 战斗状态域服务：Health/Augments 状态、生命上限/受击/治疗/吸血/选增幅 逻辑。
/// Godot 绑定层：本域无跨域状态依赖——MaxHealth/AugmentLevel 均为本域直调（难度域 regen 缓存等
/// 跨域数值如需访问经 GameState.Instance 门面，本域无此访问）；PlayerDied 信号不在本域——
/// 由 Player.DieInternal 在 _dead 置位、死亡结算完成后经 GameState.Instance 发射
/// （LoseHealth 内 Health<=0 不得发射：此时先于 player.Die()，回调内 IsDead()==false 是
/// 订阅者时序陷阱）；健康配置（MaxHpBase/MaxHpBonus/
/// _lifestealFraction）经 ApplyHealthConfig 注入（Cfg 调用留在 GameState 侧）。
/// GameState 组合持有本服务并做门面对齐转发（签名/语义不变），保持唯一 autoload：GameState 约定。
/// 信号：本服务以 C# 事件 HealthChanged/AugmentsChanged 通知；
/// GameState 订阅后转发为同名信号（发射点/次数/顺序恒定——AddBuff/ConsumeAugment 经
/// 本事件重发；ResetRun/天赋路线（TalentService 层级写入）的直发路径在 GameState/其他服务侧直发
/// 同名信号，不经本事件，不造成双发）。
/// </summary>
public sealed partial class CombatStateService : RefCounted
{

    // ---------------- 健康/增幅 域 ----------------

    /// <summary>玩家当前 HP（100 制，对齐原作 MAX_HEALTH；上限见 max_health()）。
    /// double（GDScript float 64 位逐位等价）。</summary>
    public double Health { get; set; } = 100.0;

    /// <summary>增幅 id -> 已选层数</summary>
    public Godot.Collections.Dictionary Augments { get; set; } = new();

    /// <summary>回血链热路径缓存：max_health 基础值 _apply_balance 缓存，热路径免 cfg
    /// 路径解析（extra_life 层数查询 O(1)）。默认值须与 balance.json 默认一致（player.max_health=100）。</summary>
    public double MaxHpBase { get; set; } = 100.0;

    /// <summary>与 _max_hp_base 钳制对称——负值使 extra_life 叠层反而降血上限
    /// （生存轴收紧意图相悖）；ApplyBalance 注入钳制后的值。</summary>
    public double MaxHpBonus { get; set; } = 50.0;

    /// <summary>吸血比例缓存（_apply_balance 刷新，击杀帧免 cfg 路径解析）。</summary>
    private double _lifestealFraction = 0.1;

    /// <summary>吸血增幅：击杀回复 int(上限 × 10%)（对齐原作 LIFESTEAL_FRACTION），每帧至多结算一次</summary>
    private long _lifestealFrame = -1;

    /// <summary>生命变化（LoseHealth/Heal）；GameState 订阅后转发为 HealthChanged 信号。</summary>
    public event Action<double>? HealthChanged;

    /// <summary>增幅 层数变动（AddBuff/ConsumeAugment）；GameState 订阅后转发为 AugmentsChanged 信号
    /// （ResetRun/天赋路线（TalentService 层级写入）的直发路径在 GameState/其他服务侧直发同名信号，
    /// 不经本事件——无双发）。</summary>
    public event Action? AugmentsChanged;

    /// <summary>健康配置注入（ApplyBalance 调用；Cfg 调用留在 GameState 侧，钳制注释随迁）。
    /// baseHp ≤0 使 max_health 归零/负值，玩家秒死——钳制下限；
    /// bonusHp 负值使 extra_life 叠层反而降血上限——钳制 ≥0；lifestealFraction 负值使吸血变扣血——钳制 ≥0。</summary>
    public void ApplyHealthConfig(double baseHp, double bonusHp, double lifestealFraction)
    {
        MaxHpBase = baseHp;
        MaxHpBonus = bonusHp;
        _lifestealFraction = lifestealFraction;
    }

    /// <summary>生命上限：基础 100 + extra_life 每层 +50（对齐原作 EXTRA_LIFE_BONUS_HP）
    /// 基础值 _apply_balance 缓存，热路径免 cfg 路径解析（extra_life 层数查询 O(1)）</summary>
    public double MaxHealth() => MaxHpBase + MaxHpBonus * AugmentLevel("extra_life");

    public void LoseHealth(double amount = 1.0)
    {
        var before = Health;
        Health = Mathf.Max(Health - amount, 0.0);
        if (Health == before)
        {
            return; // 值未变（已为 0 的重复掉血）不重复发射，幂等
        }

        HealthChanged?.Invoke(Health);
        // PlayerDied 不在此发射：Health<=0 即发会先于 player.Die()，
        // 回调内 IsDead()==false 是订阅者时序陷阱；由 Player.DieInternal 在 _dead 置位后发射，
        // 两处致死路径（PlayerDamage 弹击 / Main.GiveUp 自毁）均在 LoseHealth 后调 Die()。
    }

    /// <summary>治疗（单点封顶 max_health，调用侧不再各自判断）</summary>
    public void Heal(double amount)
    {
        var before = Health;
        Health = Mathf.Min(Health + amount, MaxHealth());
        if (Health == before)
        {
            return; // 值未变（满血被动回血逐帧调用）不重复发射，幂等（LoseHealth 同款守卫）
        }

        HealthChanged?.Invoke(Health);
    }

    public void TryLifesteal()
    {
        if (AugmentLevel("lifesteal") <= 0)
        {
            return;
        }

        var frame = (long)Engine.GetPhysicsFrames();
        if (frame == _lifestealFrame)
        {
            return;
        }

        _lifestealFrame = frame;
        Heal(Mathf.Max(1, (int)(MaxHealth() * _lifestealFraction)));
    }

    public int AugmentLevel(StringName id) => (int)Augments.GetValueOrDefault(id, 0).AsInt64();

    /// <summary>消耗一层增幅（护盾等一次性层；无剩余层返回 false；层数变动广播 augments_changed）</summary>
    public bool ConsumeAugment(StringName id)
    {
        if (AugmentLevel(id) <= 0)
        {
            return false;
        }

        Augments[id] = AugmentLevel(id) - 1;
        AugmentsChanged?.Invoke();
        return true;
    }

    /// <summary>健康/增幅 域复位（ResetRun 调用；Augments.Clear 后 extra_life 归零 → MaxHealth 回基础值，
    /// Health=MaxHealth 为满血口径；不发事件——AugmentsChanged 由 ResetRun 末尾直发保持顺序）。</summary>
    public void ResetAll()
    {
        Augments.Clear();
        Health = MaxHealth();
    }
}
