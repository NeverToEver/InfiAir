using InfiAir.Core.Storage;
using Xunit;

namespace InfiAir.Core.Tests.Storage;

/// <summary>
/// 本局存档字段规范化测试（core 侧唯一判定口径）。
/// 原实现把「int 子表裸 (int)AsDouble()」写死在 GameState.RunSave 里——手改存档的超大/负数
/// 会静默回绕，且没有任何用例钉住「旧档缺新字段仍能读入且不丢已存进度」这条不变式。
/// </summary>
public sealed class RunFieldNormalizeTests
{
    [Fact]
    public void ReadInt_ClampsIntoNonNegativeIntDomain()
    {
        var data = new Dictionary<string, object?>
        {
            ["neg"] = -5L,
            ["huge"] = 9_000_000_000L,
            ["nan"] = double.NaN,
            ["inf"] = double.PositiveInfinity,
            ["frac"] = 3.9,
            ["text"] = "12",
        };

        Assert.Equal(0, RunFieldNormalize.ReadInt(data, "neg", 7));
        Assert.Equal(int.MaxValue, RunFieldNormalize.ReadInt(data, "huge", 7));
        Assert.Equal(7, RunFieldNormalize.ReadInt(data, "nan", 7));   // 非有限回退默认值
        Assert.Equal(7, RunFieldNormalize.ReadInt(data, "inf", 7));
        Assert.Equal(3, RunFieldNormalize.ReadInt(data, "frac", 7));  // 向零截断（对齐 SaveInt）
        Assert.Equal(7, RunFieldNormalize.ReadInt(data, "text", 7));  // 判型不符回退
        Assert.Equal(7, RunFieldNormalize.ReadInt(data, "absent", 7));
    }

    [Fact]
    public void ReadNum_NonFiniteFallsBackInsteadOfPoisoningState()
    {
        var data = new Dictionary<string, object?>
        {
            ["nan"] = double.NaN,
            ["inf"] = double.NegativeInfinity,
            ["ok"] = 12.5,
            ["int"] = 3L,
        };

        Assert.Equal(1.0, RunFieldNormalize.ReadNum(data, "nan", 1.0));
        Assert.Equal(1.0, RunFieldNormalize.ReadNum(data, "inf", 1.0));
        Assert.Equal(12.5, RunFieldNormalize.ReadNum(data, "ok", 1.0));
        Assert.Equal(3.0, RunFieldNormalize.ReadNum(data, "int", 1.0));
        Assert.Equal(1.0, RunFieldNormalize.ReadNum(data, "absent", 1.0));
    }

    [Fact]
    public void ReadIntMap_ClampsValuesAndSkipsNonNumeric()
    {
        var raw = new Dictionary<string, object?>
        {
            ["homing"] = 3L,
            ["extra_life"] = 2.9,
            ["broken"] = "many",
            ["nul"] = null,
            ["over"] = 8_000_000_000L,
            ["neg"] = -4L,
        };

        var map = RunFieldNormalize.ReadIntMap(raw);

        Assert.Equal(3, map["homing"]);
        Assert.Equal(2, map["extra_life"]);
        Assert.Equal(int.MaxValue, map["over"]);
        Assert.Equal(0, map["neg"]);          // 负数钳 0，不产负层级
        Assert.False(map.ContainsKey("broken")); // 非数值整条跳过
        Assert.False(map.ContainsKey("nul"));
    }

    [Fact]
    public void ReadIntMap_NonDictionary_ReturnsEmpty()
    {
        Assert.Empty(RunFieldNormalize.ReadIntMap(null));
        Assert.Empty(RunFieldNormalize.ReadIntMap("nope"));
        Assert.Empty(RunFieldNormalize.ReadIntMap(new List<object?> { 1L }));
    }

    [Fact]
    public void NormalizeAugments_KeepsPositiveLevelsOnly()
    {
        var raw = new Dictionary<string, object?>
        {
            ["homing"] = 2L,
            ["deflector"] = 0L,
            ["extra_life"] = -1L,
            ["broken"] = "x",
        };

        var augments = RunFieldNormalize.NormalizeAugments(raw);

        Assert.Equal(2, augments["homing"]);
        Assert.Single(augments);
    }

    [Fact]
    public void LegacyRunSave_MissingNewField_LoadsWithoutLosingStoredProgress()
    {
        // 旧档（version 0，字段表少 difficulty_time_step / talent_cache_values 之外的任何新字段）：
        // 可读入 = 已存进度逐项还原，缺的新字段走默认值回退，绝不因为缺键丢掉既有进度。
        var legacy = new Dictionary<string, object?>
        {
            ["score"] = 1234L,
            ["kills"] = 12L,
            ["boss_kills"] = 1L,
            ["milestone_count"] = 3L,
            ["health"] = 80.5,
            ["run_time"] = 420.0,
            ["talent_levels"] = new Dictionary<string, object?> { ["extra_life"] = 2L, ["homing"] = 1L },
            ["augments"] = new Dictionary<string, object?> { ["homing"] = 1L },
        };

        // 已存进度：逐项还原
        Assert.Equal(1234, RunFieldNormalize.ReadInt(legacy, "score", 0));
        Assert.Equal(12, RunFieldNormalize.ReadInt(legacy, "kills", 0));
        Assert.Equal(3, RunFieldNormalize.ReadInt(legacy, "milestone_count", 0));
        Assert.Equal(80.5, RunFieldNormalize.ReadNum(legacy, "health", 100.0));
        Assert.Equal(420.0, RunFieldNormalize.ReadNum(legacy, "run_time", 0.0));
        var levels = RunFieldNormalize.ReadIntMap(legacy["talent_levels"]);
        Assert.Equal(2, levels["extra_life"]);
        Assert.Equal(1, levels["homing"]);
        Assert.Equal(1, RunFieldNormalize.NormalizeAugments(legacy["augments"])["homing"]);

        // 新字段缺失：回退默认值，不抛、不影响上面已还原的进度
        Assert.Equal(0, RunFieldNormalize.ReadInt(legacy, "difficulty_time_step", 0));
        Assert.Equal(1.0, RunFieldNormalize.ReadNum(legacy, "difficulty_multiplier", 1.0));
        Assert.Empty(RunFieldNormalize.ReadIntMap(legacy.GetValueOrDefault("last_kind_value")));
    }

    [Fact]
    public void CorruptedRunSave_OutOfRangeProgress_IsClampedNotWrapped()
    {
        // 手改超大分数：裸 (int) 转换会回绕成负数（统计错乱）
        var corrupted = new Dictionary<string, object?> { ["score"] = 3_000_000_000L };
        Assert.Equal(int.MaxValue, RunFieldNormalize.ReadInt(corrupted, "score", 0));
    }

    [Fact]
    public void LegacyRunSave_AllNumericFields_MissingNewOnesFallBackWithoutLosingStored()
    {
        // Godot 侧 ApplyRunDict 读的每个数值字段都经 RunFieldNormalize（接线后不再有裸转换）。
        // 这条用例逐项对应 ApplyRunDict 的读法：已存字段逐项还原，缺失的新字段走默认值，
        // 二者互不影响——「旧档可读入且不丢进度」不变式在接线后的完整字段面上成立。
        var legacy = new Dictionary<string, object?>
        {
            ["score"] = 900L,
            ["kills"] = 7L,
            ["boss_kills"] = 2L,
            ["combo"] = 5L,
            ["milestone_count"] = 4L,
            ["run_time"] = 300.0,
            ["difficulty_multiplier"] = 1.6,
            ["dda_timer"] = 12.5,
            ["difficulty_time_step"] = 2L,
            ["health"] = 66.0,
            ["talent_reset_tokens"] = 1L,
            ["talent_bonus_overcharge_slots"] = 1L,
            ["rp"] = 6L,
            ["refresh_points"] = 2L,
        };

        // 已存字段：逐项还原（值不被缺键影响）
        Assert.Equal(900, RunFieldNormalize.ReadInt(legacy, "score", 0));
        Assert.Equal(7, RunFieldNormalize.ReadInt(legacy, "kills", 0));
        Assert.Equal(2, RunFieldNormalize.ReadInt(legacy, "boss_kills", 0));
        Assert.Equal(5, RunFieldNormalize.ReadInt(legacy, "combo", 0));
        Assert.Equal(4, RunFieldNormalize.ReadInt(legacy, "milestone_count", 0));
        Assert.Equal(300.0, RunFieldNormalize.ReadNum(legacy, "run_time", 0.0));
        Assert.Equal(1.6, RunFieldNormalize.ReadNum(legacy, "difficulty_multiplier", 1.0));
        Assert.Equal(12.5, RunFieldNormalize.ReadNum(legacy, "dda_timer", 0.0));
        Assert.Equal(2, RunFieldNormalize.ReadInt(legacy, "difficulty_time_step", 0));
        Assert.Equal(66.0, RunFieldNormalize.ReadNum(legacy, "health", 100.0));
        Assert.Equal(1, RunFieldNormalize.ReadInt(legacy, "talent_reset_tokens", 0));
        Assert.Equal(1, RunFieldNormalize.ReadInt(legacy, "talent_bonus_overcharge_slots", 0));
        Assert.Equal(6, RunFieldNormalize.ReadInt(legacy, "rp", 0));
        Assert.Equal(2, RunFieldNormalize.ReadInt(legacy, "refresh_points", 0));

        // 未来新增字段（旧档必缺）：默认值回退，不抛且不回头影响上面已还原的进度
        var futureFields = new Dictionary<string, object?>
        {
            ["new_int_field"] = 0,
            ["new_num_field"] = 0.0,
        };
        Assert.Equal(11, RunFieldNormalize.ReadInt(legacy, "new_int_field", 11));
        Assert.Equal(2.5, RunFieldNormalize.ReadNum(legacy, "new_num_field", 2.5));
        // 缺键字段统一落默认值（用同一默认查询，证明缺键不产生异常副作用）
        Assert.Equal(11, RunFieldNormalize.ReadInt(futureFields, "another_new_field", 11));
    }
}
