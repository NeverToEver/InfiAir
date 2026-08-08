using Godot;

namespace InfiAir;

/// <summary>
/// 玩家受击减免 + 回血组件（M3c 全量迁移，2026-08-08 自 scripts/player_damage.gd 迁移；A8 拆分，
/// docs/AUDIT_VAULT.md A8）。持无敌/单帧守卫/受击延迟计时；受击结算与回血逻辑自本类。
/// 经 Player 属性转发（Player.Invincible 等，测试白盒兼容）与 GameState 全局交互，
/// 不访问 Player 私有字段（A1 约束）。
/// 纯 C# 逻辑类（原 RefCounted、无信号/导出）：由 C# Player 组合持有；GameState（GDScript
/// autoload）经 GameStateBridge 动态访问（仅受击/回血 tick 低频，不涉每帧热路径）。
/// </summary>
public class PlayerDamage
{
    /// <summary>受击无敌剩余秒数（Player._physics_process 每帧递减）。</summary>
    public float Invincible { get; set; }

    /// <summary>本帧已结算受击的物理帧号（A16：单帧至多结算一次受击；-1 = 无）。</summary>
    public int LastHitFrame { get; set; } = -1;

    /// <summary>距上次受击秒数（被动回血延迟计时）。</summary>
    public float SinceDamage { get; set; } = 999.0f;

    // ---- 数值配置（Player._load_balance 经 Configure 注入；与脚本默认值一致） ----
    public float InvincibleTime { get; private set; } = 1.5f;
    public float ArmorMult { get; private set; } = 0.85f;
    public float EvasionChance { get; private set; } = 0.2f;
    public float RegenPerSec { get; private set; } = 2.0f;
    public float ShakeHit { get; private set; } = 12.0f;

    public void Configure(float invincibleTime, float armorMult, float evasionChance, float regenPerSec, float shakeHit)
    {
        InvincibleTime = invincibleTime;
        ArmorMult = armorMult;
        EvasionChance = evasionChance;
        RegenPerSec = regenPerSec;
        ShakeHit = shakeHit;
    }

    public void SetInvincible(float seconds) => Invincible = seconds;

    public float InvincibleRemaining() => Invincible;

    /// <summary>
    /// 受击结算（100 HP 制）。返回 true = 本帧实际结算（调用方据此决定子弹是否销毁）。
    /// 减免两段式（去 bug 统一版）：先 20% 闪避，再护甲 ×0.85；对全部伤害源生效。
    /// fromPos：伤害源世界坐标（Meta HUD 定向波纹）；Vector2.INF = 无方向（均匀环）。
    /// </summary>
    public bool TakeDamage(float amount, Vector2 fromPos, Player player)
    {
        if (player.IsDead() || Invincible > 0.0f || player.IsDashing())
        {
            return false;
        }

        // A16：单帧至多结算一次受击（敌弹/敌机撞/Boss 撞共用）
        if (Engine.GetPhysicsFrames() == (ulong)LastHitFrame)
        {
            return false;
        }

        // 闪避 buff：20% 完全免伤（不置无敌、不清弹）
        if (GameStateBridge.Call("buff_count", new StringName("evasion")).AsInt64() > 0
            && GD.Randf() < EvasionChance)
        {
            return false;
        }

        // 护盾 buff（2026-08-04）：每层吸收一次全额伤害——扣层并销毁子弹，不置无敌/不清弹/
        // 不掉血（盾碎后下一发照常结算）；吸收反馈轻震屏。
        // 2026-08-06 审计登记：吸收分支有意不写 last_hit_frame——同帧多弹命中时每层吸收
        // 一发（「每层吸收一次」语义优先）；若计入 A16 单帧守卫则同帧第二弹被拦截免费，
        // 盾层数与弹数消耗不对称（hit_logic_test 同帧连打回归）。概率极低，维持现状登记
        if (GameStateBridge.Call("buff_count", new StringName("shield")).AsInt64() > 0)
        {
            GameStateBridge.Call("consume_buff", new StringName("shield"));
            GameStateBridge.Call("shake", 2.0);
            return true;
        }

        // 护甲 buff：固定 ×0.85 减伤
        if (GameStateBridge.Call("buff_count", new StringName("armor")).AsInt64() > 0)
        {
            amount *= ArmorMult;
        }

        LastHitFrame = (int)Engine.GetPhysicsFrames();
        SinceDamage = 0.0f;
        Invincible = InvincibleTime;
        GameStateBridge.Call("play_sfx", GameStateBridge.Get("SFX_PLAYER_HIT"));
        GameStateBridge.Call("shake", ShakeHit);
        GameStateBridge.Call("lose_health", amount);
        GameStateBridge.Instance!.EmitSignal("player_damaged", amount, fromPos); // Meta HUD 受击层（减免后最终值）
        player.ClearNearbyEnemyBullets();
        if (GameStateBridge.Get("health").AsDouble() <= 0.0)
        {
            player.Die();
        }

        return true;
    }

    /// <summary>回血 tick：regen buff 固定 +2 HP/s；无 buff 时被动回血——距上次受伤 delay 秒起按难度速率回复。</summary>
    public void HealTick(float delta)
    {
        SinceDamage += delta;
        if (GameStateBridge.Call("buff_count", new StringName("regen")).AsInt64() > 0)
        {
            GameStateBridge.Call("heal", RegenPerSec * delta);
        }
        else if (SinceDamage >= GameStateBridge.Call("passive_regen_delay").AsDouble())
        {
            // M5：(float) 截断致 health 被 float 精度污染（50+9.2→59.2000004798174），
            // 后续 base 维修 heal 差值不精确回满（smoke flake 根因）；全程 double
            GameStateBridge.Call("heal", GameStateBridge.Call("passive_regen_rate").AsDouble() * delta);
        }
    }

    // ---------------- GDScript 鸭子调用兼容桥（M3c 过渡，M7 删除） ----------------
    // 原 GDScript 公开 API（snake_case / UPPER_SNAKE 配置 var）别名转发。纯 C# 类不可被
    // GDScript 动态派发，本桥仅供源码级 API 对等与 C# 侧迁移测试沿用；M7 全量迁移后删除。

    public bool take_damage(float amount, Vector2 fromPos, Player player) => TakeDamage(amount, fromPos, player);

    public void heal_tick(float delta) => HealTick(delta);

    public void configure(float invincibleTime, float armorMult, float evasionChance, float regenPerSec, float shakeHit)
        => Configure(invincibleTime, armorMult, evasionChance, regenPerSec, shakeHit);


    public float invincible_remaining() => InvincibleRemaining();

    public float invincible { get => Invincible; set => Invincible = value; }

    public int last_hit_frame { get => LastHitFrame; set => LastHitFrame = value; }

    public float since_damage { get => SinceDamage; set => SinceDamage = value; }

    public float INVINCIBLE_TIME { get => InvincibleTime; set => InvincibleTime = value; }

    public float ARMOR_MULT { get => ArmorMult; set => ArmorMult = value; }

    public float EVASION_CHANCE { get => EvasionChance; set => EvasionChance = value; }

    public float REGEN_PER_SEC { get => RegenPerSec; set => RegenPerSec = value; }

    public float SHAKE_HIT { get => ShakeHit; set => ShakeHit = value; }
}
