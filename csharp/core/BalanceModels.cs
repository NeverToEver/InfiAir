using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfiAir.Core;

/// <summary>
/// data/balance.json 的类型化模型（InfiAir.Core 首个样板模块）。
/// 纯 .NET、零 Godot 依赖 → 可在 xUnit 中直接单测；
/// 字段值语义对齐 docs/BALANCE_MAP.md 与 test/balance_test.gd 抽查项
///（U19：原"与 GDScript 侧 GameState.cfg() 并行存在"注记随 M7 全量迁移失效——现为唯一类型化入口）。
/// </summary>
public sealed class BalanceRoot
{
    // 复用同一实例：避免每次 Load 重建 JsonSerializerOptions（CA1869）
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public int Version { get; set; }

    public double WorldScale { get; set; }

    public PlayerBalance? Player { get; set; }

    public EnemyBalance? Enemies { get; set; }

    public BossBalance? Boss { get; set; }

    public SpawnerBalance? Spawner { get; set; }

    public MothershipBalance? Mothership { get; set; }

    /// <summary>未被类型化的 section（hud/buffs/milestones/effects/…）原样保留，供后续按需类型化。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>解析并校验 balance.json；失败返回 null 并给出原因（对齐 GDScript 侧"损坏回退"语义）。</summary>
    public static BalanceRoot? Load(string json, out string? error)
    {
        error = null;
        try
        {
            var root = JsonSerializer.Deserialize<BalanceRoot>(json, SerializerOptions);
            if (root is null)
            {
                error = "empty document";
                return null;
            }
            if (!root.TryValidate(out error))
            {
                return null;
            }
            return root;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }
        catch (Exception ex)
        {
            // U09（2026-08-09 审计）：损坏数据兜底——"失败返回 null"契约不得被
            // 非 JsonException 击穿（如 JSON null 数组属性在 TryValidate 的 NRE）
            error = ex.Message;
            return null;
        }
    }

    /// <summary>关键结构校验：缺失/越界即视为无效配置。</summary>
    public bool TryValidate(out string? error)
    {
        error = null;
        if (Version <= 0)
        {
            error = $"invalid version: {Version}";
            return false;
        }
        if (Player is null || Player.MaxSpeed <= 0 || Player.MaxHealth <= 0)
        {
            error = "player section invalid (max_speed/max_health must be positive)";
            return false;
        }
        if (Enemies is null || Enemies.CollisionDamage < 0)
        {
            error = "enemies section invalid (collision_damage must be >= 0)";
            return false;
        }
        if (Boss is null || Boss.HpMults is null || Boss.HpMults.Length != BossCount
            || Boss.FireIntervals is null || Boss.FireIntervals.Length != BossCount)
        {
            error = $"boss section invalid (hp_mults/fire_intervals must have {BossCount} entries)";
            return false;
        }
        if (Spawner is null || Spawner.UnlockScores is null || Spawner.UnlockScores.Length != MilestoneCount)
        {
            error = $"spawner section invalid (unlock_scores must have {MilestoneCount} entries)";
            return false;
        }
        if (Mothership is null || Mothership.MagCells <= 0)
        {
            error = "mothership section invalid (mag_cells must be positive)";
            return false;
        }
        return true;
    }

    /// <summary>旋转 Boss 数量（与 docs/BALANCE_MAP.md boss.hp_mults 长度一致）。</summary>
    public const int BossCount = 4;

    /// <summary>里程碑档数（与 spawner.unlock_scores 长度一致）。</summary>
    public const int MilestoneCount = 5;
}

/// <summary>player section 类型化（字段值示例见 test/balance_test.gd：max_speed 420、max_health 100、bullet_damage 10）。</summary>
public sealed class PlayerBalance
{
    [JsonPropertyName("max_speed")]
    public int MaxSpeed { get; set; }

    [JsonPropertyName("max_health")]
    public double MaxHealth { get; set; }

    [JsonPropertyName("bullet_damage")]
    public int BulletDamage { get; set; }

    [JsonPropertyName("base_fire_interval")]
    public double BaseFireInterval { get; set; }

    [JsonPropertyName("fuel")]
    public FuelBalance? Fuel { get; set; }
}

/// <summary>player.fuel section 类型化（drain 燃料消耗率，抽查见 test/balance_test.gd）。</summary>
public sealed class FuelBalance
{
    [JsonPropertyName("drain")]
    public double Drain { get; set; }
}

/// <summary>enemies section 类型化。</summary>
public sealed class EnemyBalance
{
    [JsonPropertyName("collision_damage")]
    public int CollisionDamage { get; set; }

    [JsonPropertyName("bullet_damage")]
    public BulletDamageBalance? BulletDamage { get; set; }

    [JsonPropertyName("hp_ramp_factor")]
    public double HpRampFactor { get; set; }

    [JsonPropertyName("damage_ramp_factor")]
    public double DamageRampFactor { get; set; }
}

/// <summary>enemies.bullet_damage section 类型化（single/spread/laser 三弹种伤害，抽查见 test/balance_test.gd）。</summary>
public sealed class BulletDamageBalance
{
    [JsonPropertyName("single")]
    public int Single { get; set; }

    [JsonPropertyName("spread")]
    public int Spread { get; set; }

    [JsonPropertyName("laser")]
    public int Laser { get; set; }
}

/// <summary>boss section 类型化（hp_mults 首档 1.3、collision_damage 30，抽查见 test/balance_test.gd）。</summary>
public sealed class BossBalance
{
    [JsonPropertyName("hp_mults")]
    public double[] HpMults { get; set; } = [];

    [JsonPropertyName("fire_intervals")]
    public double[] FireIntervals { get; set; } = [];

    [JsonPropertyName("collision_damage")]
    public int CollisionDamage { get; set; }
}

/// <summary>spawner section 类型化。</summary>
public sealed class SpawnerBalance
{
    [JsonPropertyName("wave_interval_start")]
    public int WaveIntervalStart { get; set; }

    [JsonPropertyName("wave_interval_end")]
    public int WaveIntervalEnd { get; set; }

    [JsonPropertyName("ramp_time")]
    public int RampTime { get; set; }

    [JsonPropertyName("difficulty_factor")]
    public double DifficultyFactor { get; set; }

    [JsonPropertyName("unlock_scores")]
    public double[] UnlockScores { get; set; } = [];
}

/// <summary>mothership section 类型化（depart_cooldown 60、mag_cells 10、missile.damage 80，抽查见 test/balance_test.gd）。</summary>
public sealed class MothershipBalance
{
    [JsonPropertyName("depart_cooldown")]
    public double DepartCooldown { get; set; }

    [JsonPropertyName("mag_cells")]
    public int MagCells { get; set; }

    [JsonPropertyName("missile")]
    public MissileBalance? Missile { get; set; }
}

/// <summary>mothership.missile section 类型化（damage 80，抽查见 test/balance_test.gd）。</summary>
public sealed class MissileBalance
{
    [JsonPropertyName("damage")]
    public int Damage { get; set; }
}
