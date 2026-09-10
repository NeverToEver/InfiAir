using Godot;

namespace InfiAir;

/// <summary>
/// 玩家受击减免 + 回血组件。
/// 持无敌/单帧守卫/受击延迟计时；受击结算与回血逻辑自本类。
/// 经 Player 属性转发（Player.Invincible 等）与 GameState 全局交互，
/// 不访问 Player 私有字段。
/// 纯 C# 逻辑类（无信号/导出）：由 C# Player 组合持有；GameState 经 Instance 门面访问。
/// </summary>
public class PlayerDamage
{
    // HealTick 每物理帧 AugmentLevel——buff 名静态缓存防每帧 StringName 构造
    private static readonly StringName RegenId = new("regen");
    private static readonly StringName EvasionId = new("evasion");
    private static readonly StringName ShieldId = new("shield");
    private static readonly StringName ArmorId = new("armor");
    private static readonly StringName SecondWindId = new("second_wind");



    /// <summary>受击无敌剩余秒数（Player._physics_process 每帧递减）。</summary>
    public float Invincible { get; set; }

    /// <summary>本帧已结算受击的物理帧号（单帧至多结算一次受击；-1 = 无）。</summary>
    public int LastHitFrame { get; set; } = -1;

    /// <summary>距上次受击秒数（被动回血延迟计时）。</summary>
    public float SinceDamage { get; set; } = 999.0f;

    // ---- 数值配置（Player._load_balance 经 Configure 注入；与脚本默认值一致） ----
    public float InvincibleTime { get; private set; } = 1.5f;
    public float ArmorMult { get; private set; } = 0.85f;
    public float EvasionChance { get; private set; } = 0.2f;
    public float RegenPerSec { get; private set; } = 2.0f;
    public float ShakeHit { get; private set; } = 12.0f;
    /// <summary>second_wind 逆境回血：受击后持续秒数 / 每秒每层回复量（Configure 注入）。</summary>
    public float SecondWindDuration { get; private set; } = 3.0f;
    public float SecondWindHealPerSec { get; private set; } = 3.0f;

    /// <summary>逆境回血剩余秒数（受击时置满；仅在窗口内按层数结算，归零后零开销）。</summary>
    private float _secondWindTimer;

    public void Configure(float invincibleTime, float armorMult, float evasionChance, float regenPerSec, float shakeHit,
        float secondWindDuration, float secondWindHealPerSec)
    {
        InvincibleTime = invincibleTime;
        ArmorMult = armorMult;
        EvasionChance = evasionChance;
        RegenPerSec = regenPerSec;
        ShakeHit = shakeHit;
        SecondWindDuration = Mathf.Max(secondWindDuration, 0.0f);
        SecondWindHealPerSec = Mathf.Max(secondWindHealPerSec, 0.0f);
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

        // 单帧至多结算一次受击（敌弹/敌机撞/Boss 撞共用）
        if (Engine.GetPhysicsFrames() == (ulong)LastHitFrame)
        {
            return false;
        }

        // 闪避 buff：20% 完全免伤（不置无敌、不清弹）
        if (GameState.Instance.AugmentLevel(EvasionId) > 0
            && GD.Randf() < EvasionChance)
        {
            return false;
        }

        // 护盾 buff：每层吸收一次全额伤害——扣层并销毁子弹，不置无敌/不清弹/
        // 不掉血（盾碎后下一发照常结算）；吸收反馈轻震屏。
        // 吸收分支有意不写 last_hit_frame——同帧多弹命中时每层吸收
        // 一发（「每层吸收一次」语义优先）；若计入单帧守卫则同帧第二弹被拦截免费，
        // 盾层数与弹数消耗不对称（hit_logic_test 同帧连打回归）。概率极低，维持现状
        if (GameState.Instance.AugmentLevel(ShieldId) > 0)
        {
            GameState.Instance.ConsumeAugment(ShieldId);
            GameState.Instance.Shake(2.0);
            return true;
        }

        // 护甲 buff：固定 ×0.85 减伤
        if (GameState.Instance.AugmentLevel(ArmorId) > 0)
        {
            amount *= ArmorMult;
        }

        LastHitFrame = (int)Engine.GetPhysicsFrames();
        SinceDamage = 0.0f;
        Invincible = InvincibleTime;
        _secondWindTimer = SecondWindDuration;
        GameState.Instance.PlaySfx(SfxId.PlayerHit);
        GameState.Instance.Shake(ShakeHit);
        GameState.Instance.LoseHealth(amount);
        GameState.Instance.EmitSignal(GameState.SignalName.PlayerDamaged, amount, fromPos); // SignalName 常量 + 类型链统一 float
        player.ClearNearbyEnemyBullets();
        if (GameState.Instance.Health <= 0.0)
        {
            player.Die();
        }

        return true;
    }

    /// <summary>回血 tick：regen buff 固定 +2 HP/s；无 buff 时被动回血——距上次受伤 delay 秒起按难度速率回复。</summary>
    public void HealTick(float delta)
    {
        SinceDamage += delta;
        // second_wind 逆境回血：受击后窗口内按层数线性回复（层查询只在窗口内发生）
        if (_secondWindTimer > 0.0f)
        {
            _secondWindTimer = Mathf.Max(_secondWindTimer - delta, 0.0f);
            var windLevel = GameState.Instance.AugmentLevel(SecondWindId);
            if (windLevel > 0)
            {
                GameState.Instance.Heal(SecondWindHealPerSec * windLevel * delta);
            }
        }

        if (GameState.Instance.AugmentLevel(RegenId) > 0)
        {
            GameState.Instance.Heal(RegenPerSec * delta);
        }
        else if (SinceDamage >= GameState.Instance.PassiveRegenDelay())
        {
            // (float) 截断会污染 health 的 float 精度（50+9.2→59.2000004798174），
            // 后续 base 维修 heal 差值不精确回满；健康全程 double
            GameState.Instance.Heal(GameState.Instance.PassiveRegenRate() * delta);
        }
    }
}
