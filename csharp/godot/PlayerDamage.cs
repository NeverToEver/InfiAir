using Godot;
using InfiAir.Core.Combat;
using InfiAir.Core.GameFeel;

namespace InfiAir;

/// <summary>
/// 玩家受击减免 + 回血组件。
/// 持无敌/单帧守卫/受击延迟计时；减免顺序与归属判定下沉 <see cref="DamageMitigation"/>
/// （csharp/core/Combat，回归面在 csharp/tests/InfiAir.Core.Tests/Combat/DamageMitigationTests.cs），
/// 本类只做适配——注入 balance 数值、读写增幅 层与 GameState 副作用（扣血/震屏/音效/顿帧）。
/// 经 Player 属性转发（Player.Invincible 等）与 GameState 全局交互，
/// 不访问 Player 私有字段。
/// 纯 C# 逻辑类（无信号/导出）：由 C# Player 组合持有；GameState 经 Instance 门面访问。
/// </summary>
public class PlayerDamage
{
    // HealTick 每物理帧 AugmentLevel——增幅 名静态缓存防每帧 StringName 构造
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
    /// 顺序与归属在 DamageMitigation：拦截守卫（死/无敌/冲刺/本帧已结算）→ 20% 闪避 →
    /// 盾吸收（每层一次全额，不置无敌/不写单帧守卫/不掉血）→ 护甲 ×0.85 → 结算；对全部伤害源生效。
    /// fromPos：伤害源世界坐标（Meta HUD 定向波纹）；Vector2.INF = 无方向（均匀环）。
    /// </summary>
    public bool TakeDamage(float amount, Vector2 fromPos, Player player)
    {
        var physicsFrame = Engine.GetPhysicsFrames();
        var frame = (int)physicsFrame;
        if (DamageMitigation.Blocks(
                player.IsDead(), Invincible > 0.0f, player.IsDashing(), physicsFrame == (ulong)LastHitFrame))
        {
            return false;
        }

        // 掷签语序：闪避增幅在手才取随机数（判定侧是严格小于；先掷后判会让被拦下的每一发白耗一个随机数）
        var hasEvasion = GameState.Instance.AugmentLevel(EvasionId) > 0;
        var result = DamageMitigation.Resolve(
            hasEvasion,
            hasEvasion ? GD.Randf() : 0.0f,
            EvasionChance,
            GameState.Instance.AugmentLevel(ShieldId) > 0,
            GameState.Instance.AugmentLevel(ArmorId) > 0,
            ArmorMult,
            amount);
        if (result.Outcome == DamageOutcome.Evaded)
        {
            return false; // 闪避增幅：完全免伤（不置无敌、不清弹）
        }

        if (result.Outcome == DamageOutcome.ShieldAbsorbed)
        {
            // 护盾增幅：每层吸收一次全额伤害——扣层并销毁子弹，不置无敌/不清弹/不掉血
            //（盾碎后下一发照常结算）；吸收反馈轻震屏
            GameState.Instance.ConsumeAugment(ShieldId);
            GameState.Instance.Shake(2.0);
            return true;
        }

        LastHitFrame = DamageMitigation.RecordHitFrame(result.Outcome, frame, LastHitFrame);
        SinceDamage = 0.0f;
        Invincible = InvincibleTime;
        _secondWindTimer = SecondWindDuration;
        GameState.Instance.PlaySfx(SfxId.PlayerHit);
        GameState.Instance.Shake(ShakeHit);
        // 玩家被击中给最重档顿帧：受击是全局最需要「顿一下」确认的事件
        GameState.Instance.RequestHitStop(HitStopTier.Heavy);
        RumbleService.Hit(); // 受击震动（扣血生效才触发；闪避/盾吸收分支不震）
        GameState.Instance.LoseHealth(result.Amount);
        GameState.Instance.EmitSignal(GameState.SignalName.PlayerDamaged, result.Amount, fromPos); // SignalName 常量 + 类型链统一 float
        player.ClearNearbyEnemyBullets();
        if (GameState.Instance.Health <= 0.0)
        {
            player.Die();
        }

        return true;
    }

    /// <summary>回血 tick：regen 增幅 固定 +2 HP/s；无增幅 时被动回血——距上次受伤 delay 秒起按难度速率回复。</summary>
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
