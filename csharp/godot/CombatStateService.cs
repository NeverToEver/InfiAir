using Godot;

namespace InfiAir;

/// <summary>
/// 战斗状态域服务（第五轮拆域，2026-08-11）：原 GameState.Settings.cs C 簇健康/Buff 域——
/// Health/Buffs 状态、生命上限/受击/治疗/吸血/选 buff 逻辑迁入本服务。
/// Godot 绑定层：本域无跨域状态依赖——MaxHealth/BuffCount 均为本域直调（难度域 regen 缓存等
/// 跨域数值如需访问经 GameState.Instance 门面，当前 C 簇无此访问）；PlayerDied 信号经
/// GameState.Instance 直发（RefCounted 非 Node 无法 EmitSignal，与 MissionsService.ChooseRoute 的
/// BuffsChanged 直发先例同构——发射点/次数/顺序不变）；健康配置（MaxHpBase/MaxHpBonus/
/// _lifestealFraction）经 ApplyHealthConfig 注入（Cfg 调用留在 GameState 侧）。
/// 门面转发先例：与 MetaService/MissionsService/ScoreService/RunProgressionService 同构——
/// GameState 组合持有本服务，GameState.Settings.cs/State.cs 为门面对齐转发（签名/语义不变），
/// 保持唯一 autoload：GameState 约定。信号：本服务以 C# 事件 HealthChanged/BuffsChanged 通知；
/// GameState 订阅后转发为同名信号（发射点/次数/顺序与拆域前逐位一致——AddBuff/ConsumeBuff 经
/// 本事件重发；ResetRun/ApplyRunSave/ChooseRoute/Meta 的直发路径在 GameState/其他服务侧直发
/// 同名信号，不经本事件，不造成双发）。
/// </summary>
public sealed partial class CombatStateService : RefCounted
{

    // ---------------- 健康/Buff 域（2026-08-11 自 GameState.Settings.cs/State.cs 迁入） ----------------

    /// <summary>玩家当前 HP（100 制，对齐原作 MAX_HEALTH；上限见 max_health()）。
    /// double（GDScript float 64 位逐位等价——BaseConsole smoke flake 根因）。</summary>
    public double Health { get; set; } = 100.0;

    /// <summary>buff id -> 已选层数</summary>
    public Godot.Collections.Dictionary Buffs { get; set; } = new();

    /// <summary>回血链热路径缓存（P0-2）：max_health 基础值 _apply_balance 缓存，热路径免 cfg
    /// 路径解析（extra_life 层数查询 O(1)）。默认值须与 balance.json 默认一致（player.max_health=100）。</summary>
    public double MaxHpBase { get; set; } = 100.0;

    /// <summary>2026-08-03 审计：与 _max_hp_base 钳制对称——负值使 extra_life 叠层反而降血上限
    /// （生存轴收紧意图相悖）；ApplyBalance 注入钳制后的值。</summary>
    public double MaxHpBonus { get; set; } = 50.0;

    /// <summary>吸血比例缓存（P0-2 同款：_apply_balance 刷新，击杀帧免 cfg 路径解析）。</summary>
    private double _lifestealFraction = 0.1;

    /// <summary>吸血 buff：击杀回复 int(上限 × 10%)（对齐原作 LIFESTEAL_FRACTION），每帧至多结算一次</summary>
    private long _lifestealFrame = -1;

    /// <summary>生命变化（LoseHealth/Heal）；GameState 订阅后转发为 HealthChanged 信号。</summary>
    public event Action<double>? HealthChanged;

    /// <summary>buff 层数变动（AddBuff/ConsumeBuff）；GameState 订阅后转发为 BuffsChanged 信号
    /// （ResetRun/ApplyRunSave/ChooseRoute/Meta 的直发路径在 GameState/其他服务侧直发同名信号，
    /// 不经本事件——无双发）。</summary>
    public event Action? BuffsChanged;

    /// <summary>健康配置注入（ApplyBalance 调用；Cfg 调用留在 GameState 侧，钳制注释随迁）。
    /// H15 同款：baseHp ≤0 使 max_health 归零/负值，玩家秒死——钳制下限；
    /// bonusHp 负值使 extra_life 叠层反而降血上限——钳制 ≥0；lifestealFraction 负值使吸血变扣血——钳制 ≥0。</summary>
    public void ApplyHealthConfig(double baseHp, double bonusHp, double lifestealFraction)
    {
        MaxHpBase = baseHp;
        MaxHpBonus = bonusHp;
        _lifestealFraction = lifestealFraction;
    }

    /// <summary>生命上限：基础 100 + extra_life 每层 +50（对齐原作 EXTRA_LIFE_BONUS_HP）
    /// P0-2：基础值 _apply_balance 缓存，热路径免 cfg 路径解析（extra_life 层数查询 O(1)）</summary>
    public double MaxHealth() => MaxHpBase + MaxHpBonus * BuffCount("extra_life");

    public void LoseHealth(double amount = 1.0)
    {
        Health = Mathf.Max(Health - amount, 0.0);
        HealthChanged?.Invoke(Health);
        if (Health <= 0.0)
        {
            // PlayerDied 直发（RefCounted 非 Node 无法 EmitSignal；经 GameState.Instance 发射，
            // 与 MissionsService.ChooseRoute 的 BuffsChanged 直发先例同构——发射点/次数/顺序不变）
            GameState.Instance.EmitSignal(GameState.SignalName.PlayerDied);
        }
    }

    /// <summary>治疗（单点封顶 max_health，调用侧不再各自判断）</summary>
    public void Heal(double amount)
    {
        Health = Mathf.Min(Health + amount, MaxHealth());
        HealthChanged?.Invoke(Health);
    }

    public void TryLifesteal()
    {
        if (BuffCount("lifesteal") <= 0)
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

    public int BuffCount(StringName id) => (int)Buffs.GetValueOrDefault(id, 0).AsInt64();

    public void AddBuff(StringName id)
    {
        Buffs[id] = BuffCount(id) + 1;
        BuffsChanged?.Invoke();
    }

    /// <summary>消耗一层 buff（护盾等一次性层；无剩余层返回 false；层数变动广播 buffs_changed）</summary>
    public bool ConsumeBuff(StringName id)
    {
        if (BuffCount(id) <= 0)
        {
            return false;
        }

        Buffs[id] = BuffCount(id) - 1;
        BuffsChanged?.Invoke();
        return true;
    }

    /// <summary>健康/Buff 域复位（ResetRun 调用；Buffs.Clear 后 extra_life 归零 → MaxHealth 回基础值，
    /// Health=MaxHealth 为满血口径；不发事件——BuffsChanged 由 ResetRun 末尾直发保持顺序）。</summary>
    public void ResetAll()
    {
        Buffs.Clear();
        Health = MaxHealth();
    }

    /// <summary>存档 buffs 恢复（ApplyRunSave 调用；判型/钳制逻辑随迁，注释随迁）。
    /// G013：层数钳制 ≥0（手改存档负层数会破坏 buff_count 逻辑；超大值属手改作弊。
    /// 注：add_buff 本身无 max_stacks 钳制——上限约束在 buff_select 选取侧检查
    /// （buffs.&lt;id&gt;.max_stacks），此处仅保下限防负层数，不改存档恢复行为）。
    /// 非 Dictionary（手改）跳过恢复仅清空，与拆域前一致。不发事件——BuffsChanged 由
    /// ApplyRunSave 直发，顺序不变。</summary>
    public void RestoreBuffs(Variant savedBuffs)
    {
        Buffs.Clear();
        if (savedBuffs.VariantType == Variant.Type.Dictionary)
        {
            foreach (var key in savedBuffs.AsGodotDictionary().Keys)
            {
                var v = savedBuffs.AsGodotDictionary()[key];
                if (v.VariantType is Variant.Type.Int or Variant.Type.Float)
                {
                    Buffs[key.AsStringName()] = Mathf.Max((int)v.AsInt64(), 0);
                }
            }
        }
    }

    /// <summary>存档血量恢复（ApplyRunSave 调用；钳 [0, max_health]。调用须在 RestoreBuffs 之后——
    /// max_health 依赖 extra_life 层数。v1（3 命制）存档不回迁血量，由调用方传 MaxHealth() 即满血开。
    /// 不发事件——HealthChanged 由 ApplyRunSave 直发，顺序不变。</summary>
    public void RestoreHealth(double health)
    {
        Health = Mathf.Clamp(health, 0.0, MaxHealth());
    }
}
