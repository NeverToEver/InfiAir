using System.Text.Json;
using InfiAir.Core;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// BalanceModels 解析/校验单测。抽查值对齐 test/balance_test.gd（同一数据源 data/balance.json）。
/// </summary>
public sealed class BalanceModelsTests
{
    private static readonly string RepoBalancePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../data/balance.json"));

    private static string LoadRepoBalanceJson()
    {
        Assert.True(File.Exists(RepoBalancePath), $"data/balance.json not found at {RepoBalancePath}");
        return File.ReadAllText(RepoBalancePath);
    }

    [Fact]
    public void Load_RealBalanceFile_Succeeds()
    {
        var root = BalanceRoot.Load(LoadRepoBalanceJson(), out var error);

        Assert.NotNull(root);
        Assert.Null(error);
        Assert.Equal(2, root!.Version);
    }

    [Fact]
    public void Load_RealBalanceFile_TypedSpotChecks()
    {
        var root = BalanceRoot.Load(LoadRepoBalanceJson(), out _);

        Assert.NotNull(root);
        // 抽查项与 test/balance_test.gd §2 保持一致
        Assert.Equal(420.0, root!.Player!.MaxSpeed);
        Assert.Equal(100.0, root.Player.MaxHealth);
        Assert.Equal(10, root.Player.BulletDamage);
        Assert.Equal(35.0, root.Player.Fuel!.Drain);
        Assert.Equal(20, root.Enemies!.CollisionDamage);
        Assert.Equal(20, root.Enemies.BulletDamage!.Laser);
        Assert.Equal(1.3, root.Boss!.HpMults[0]);
        Assert.Equal(30, root.Boss.CollisionDamage);
        Assert.Equal(60.0, root.Mothership!.DepartCooldown);
        Assert.Equal(10, root.Mothership.MagCells);
        Assert.Equal(80, root.Mothership.Missile!.Damage);
        Assert.Equal(5, root.Spawner!.UnlockScores.Length);
    }

    [Fact]
    public void Load_BrokenJson_ReturnsNullWithError()
    {
        var root = BalanceRoot.Load("{broken json!!!", out var error);

        Assert.Null(root);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Load_UnknownSections_PreservedInExtra()
    {
        var root = BalanceRoot.Load(LoadRepoBalanceJson(), out _);

        Assert.NotNull(root);
        Assert.True(root!.Extra!.ContainsKey("hud"));
        Assert.True(root.Extra.ContainsKey("buffs"));
        Assert.True(root.Extra.ContainsKey("fog_events"));
    }

    [Fact]
    public void Load_MissingPlayerSection_Invalid()
    {
        const string json = """{"version": 4, "enemies": {"collision_damage": 20}}""";

        var root = BalanceRoot.Load(json, out var error);

        Assert.Null(root);
        Assert.Contains("player", error);
    }

    [Fact]
    public void Load_WrongBossCount_Invalid()
    {
        const string json = """
        {"version": 4, "world_scale": 1.0,
         "player": {"max_speed": 420, "max_health": 100},
         "enemies": {"collision_damage": 20},
         "boss": {"hp_mults": [1.0, 2.0], "fire_intervals": [0.5, 1.0], "collision_damage": 30},
         "spawner": {"unlock_scores": [1, 2, 3, 4, 5]},
         "mothership": {"depart_cooldown": 60, "mag_cells": 10}}
        """;

        var result = BalanceRoot.Load(json, out var error);

        Assert.Null(result);
        Assert.Contains("hp_mults", error);
    }

    [Fact]
    public void Load_NullBossArrays_ReturnsNullNotCrash()
    {
        // U09（2026-08-09 审计）：JsonSerializer 无视 NRT，"hp_mults": null 会覆盖
        // = [] 初始化器置 null——TryValidate 必须判空而非 NRE 击穿 Load 契约
        const string json = """
        {"version": 4, "world_scale": 1.0,
         "player": {"max_speed": 420, "max_health": 100},
         "enemies": {"collision_damage": 20},
         "boss": {"hp_mults": null, "fire_intervals": null, "collision_damage": 30},
         "spawner": {"unlock_scores": null},
         "mothership": {"depart_cooldown": 60, "mag_cells": 10}}
        """;

        var result = BalanceRoot.Load(json, out var error);

        Assert.Null(result);
        Assert.Contains("boss", error);
    }

    [Fact]
    public void Load_EmptyDocument_Invalid()
    {
        var root = BalanceRoot.Load("{}", out var error);

        Assert.Null(root);
        Assert.Contains("version", error);
    }
}
