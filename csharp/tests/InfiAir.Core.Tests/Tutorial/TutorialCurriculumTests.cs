using System;
using System.Collections.Generic;
using System.Text;
using InfiAir.Core.Tutorial;
using Xunit;

namespace InfiAir.Core.Tests.Tutorial;

/// <summary>教程课程表与进度判定的契约测试。钉住三件事：
/// ① 六阶段的顺序 / 目标数 / 补参形态（节奏与判定的单源，节点与文案共用）；
/// ② 目标行文案的占位符数与课程表补参个数逐位对齐——补参错位时玩家看到的是错位的数字或键名，
///    既不崩也不报错，引擎侧任何探针都判不到；
/// ③ 节点不留副本：目标数与键位提示必须经 core 与生产绑定取值，不得再写一份。</summary>
public sealed class TutorialCurriculumTests
{
    [Fact]
    public void Stages_AreOrderedWithExpectedGoals()
    {
        Assert.Equal(6, TutorialCurriculum.StageCount);
        Assert.Equal(
            new[]
            {
                TutorialGoalKind.Marksmanship,
                TutorialGoalKind.Maneuver,
                TutorialGoalKind.Combat,
                TutorialGoalKind.Dock,
                TutorialGoalKind.Homecoming,
                TutorialGoalKind.BossEnrage,
            },
            Array.ConvertAll(TutorialCurriculum.Stages, s => s.Goal));
        Assert.Equal(new[] { 3, 2, 5, 1, 1, 1 }, Array.ConvertAll(TutorialCurriculum.Stages, s => s.TargetCount));
    }

    [Fact]
    public void Stages_HaveDistinctTitleAndObjectiveKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stage in TutorialCurriculum.Stages)
        {
            Assert.StartsWith("TUT_", stage.TitleKey, StringComparison.Ordinal);
            Assert.StartsWith("TUT_", stage.ObjectiveKey, StringComparison.Ordinal);
            Assert.True(keys.Add(stage.TitleKey), $"标题键重复：{stage.TitleKey}");
            Assert.True(keys.Add(stage.ObjectiveKey), $"目标键重复：{stage.ObjectiveKey}");
        }
    }

    [Fact]
    public void OnlyChargingStages_DeclareChargeLineWithMatchingArgs()
    {
        foreach (var stage in TutorialCurriculum.Stages)
        {
            var charging = stage.Goal is TutorialGoalKind.Dock or TutorialGoalKind.Homecoming;
            Assert.Equal(charging, stage.ChargeKey.Length > 0);
            Assert.Equal(stage.ChargeKey.Length > 0, stage.ChargeArgs != null);
        }
    }

    /// <summary>占位符数 == 补参个数：两侧任一改动而另一侧忘改时（改文案忘改表、或加补参忘加
    /// 占位符），玩家看到的是 %s/%d 原样残留或错位取值——这条是唯一能在秒级判出它的地方。</summary>
    [Fact]
    public void ObjectiveAndChargeCopy_PlaceholderCountsMatchArgPlans()
    {
        var copy = TranslationTable();
        foreach (var stage in TutorialCurriculum.Stages)
        {
            AssertPlaceholders(copy, stage.ObjectiveKey, stage.ObjectiveArgs.Length);
            if (stage.ChargeKey.Length > 0)
            {
                AssertPlaceholders(copy, stage.ChargeKey, stage.ChargeArgs!.Length);
            }
        }

        AssertPlaceholders(copy, TutorialCurriculum.SkipHintKey, TutorialCurriculum.SkipHintArgs.Length);
        foreach (var stage in TutorialCurriculum.Stages)
        {
            if (stage.FollowUpKey.Length > 0)
            {
                AssertPlaceholders(copy, stage.FollowUpKey, expected: 0);
            }
        }
    }

    [Fact]
    public void StageCopyKeys_ExistInBothLocales()
    {
        var copy = TranslationTable();
        foreach (var stage in TutorialCurriculum.Stages)
        {
            foreach (var key in new[] { stage.TitleKey, stage.ObjectiveKey, stage.ChargeKey, stage.FollowUpKey })
            {
                if (key.Length == 0)
                {
                    continue;
                }

                Assert.True(copy.ContainsKey(key), $"文案表缺键：{key}");
                Assert.False(string.IsNullOrWhiteSpace(copy[key].Zh), $"{key} 缺中文");
                Assert.False(string.IsNullOrWhiteSpace(copy[key].En), $"{key} 缺英文");
            }
        }
    }

    [Fact]
    public void ClampAndResume_NormalizeOutOfRangeInputs()
    {
        Assert.Equal(0, TutorialCurriculum.ClampStage(-3));
        Assert.Equal(5, TutorialCurriculum.ClampStage(99));
        Assert.Equal(2, TutorialCurriculum.ResumeStage(2));
        Assert.Equal(0, TutorialCurriculum.ResumeStage(-1));
        Assert.Equal(5, TutorialCurriculum.ResumeStage(7));
        Assert.True(TutorialCurriculum.IsLast(5));
        Assert.False(TutorialCurriculum.IsLast(4));
        Assert.Equal(5, TutorialCurriculum.Next(5));
        Assert.Equal(4, TutorialCurriculum.Next(3));
    }

    // ---------------- 阶段进度判定 ----------------

    [Fact]
    public void Marksmanship_CompletesAtTargetCountAndCapsReadings()
    {
        var progress = new TutorialProgress();
        progress.EnterStage(TutorialCurriculum.At(0));
        Assert.Equal(3, progress.Remaining);
        progress.AddKill();
        progress.AddKill();
        Assert.False(progress.IsComplete);
        Assert.Equal(2, progress.KillCount);
        Assert.Equal(1, progress.Remaining);
        progress.AddKill();
        Assert.True(progress.IsComplete);
        Assert.Equal(0, progress.Remaining);
        progress.AddKill();
        Assert.Equal(3, progress.KillCount); // 文案读数封顶，不出现 4/3
    }

    [Fact]
    public void Maneuver_NeedsBothChannelsSeparately()
    {
        var progress = new TutorialProgress();
        progress.EnterStage(TutorialCurriculum.At(1));
        progress.AddBoost();
        progress.AddBoost();
        Assert.False(progress.IsComplete); // 只加速不突进不算达成
        progress.AddDash();
        Assert.False(progress.IsComplete);
        progress.AddDash();
        Assert.True(progress.IsComplete);
        Assert.Equal(2, progress.BoostGoal);
        Assert.Equal(2, progress.DashGoal);
    }

    [Fact]
    public void ChargeStages_CompleteOnChargeOnly()
    {
        var progress = new TutorialProgress();
        progress.EnterStage(TutorialCurriculum.At(3));
        Assert.False(progress.IsComplete);
        progress.MarkCharged();
        Assert.True(progress.IsComplete);
        Assert.Equal(0, progress.Remaining);
    }

    [Fact]
    public void BossStage_CompletesOnEnrage()
    {
        var progress = new TutorialProgress();
        progress.EnterStage(TutorialCurriculum.At(5));
        Assert.False(progress.IsComplete);
        progress.MarkEnraged();
        Assert.True(progress.IsComplete);
    }

    [Fact]
    public void Restart_ClearsCountersButKeepsStage()
    {
        var progress = new TutorialProgress();
        progress.EnterStage(TutorialCurriculum.At(2));
        progress.AddKill();
        progress.AddKill();
        progress.Restart();
        Assert.Equal(5, progress.Stage.TargetCount);
        Assert.Equal(0, progress.Kills);
        Assert.Equal(5, progress.Remaining);
        Assert.False(progress.IsComplete);
    }

    [Fact]
    public void EnterStage_ResetsPreviousStageProgress()
    {
        var progress = new TutorialProgress();
        progress.EnterStage(TutorialCurriculum.At(0));
        progress.AddKill();
        progress.AddKill();
        progress.AddKill();
        Assert.True(progress.IsComplete);
        progress.EnterStage(TutorialCurriculum.At(1));
        Assert.Equal(0, progress.Kills);
        Assert.False(progress.IsComplete);
    }

    // ---------------- 结构性判定：节点不留副本 ----------------

    /// <summary>节点必须经 core 取目标数与补参来源：把目标数再声明成局部常量、或把键名写死回
    /// 文案，都会让课程表与运行期脱钩，而编译与冒烟都不报。</summary>
    [Fact]
    public void TutorialNode_UsesCurriculumWithoutLocalCopies()
    {
        var src = RepoFiles.Read("csharp/godot/Tutorial.cs");
        Assert.Contains("TutorialCurriculum", src, StringComparison.Ordinal);
        Assert.Contains("TutorialProgress", src, StringComparison.Ordinal);
        Assert.DoesNotContain("AimTargetKillGoal", src, StringComparison.Ordinal);
        Assert.DoesNotContain("CombatKillGoal", src, StringComparison.Ordinal);
        // 键位提示必须取自实际绑定（改键后文案跟变），不得把键名写死回节点
        Assert.Contains("ActionKeyText", src, StringComparison.Ordinal);
        // 阶段定义与达成判据必须经 core：节点自己判「击杀数 >= 3」这类比较即分叉
        Assert.DoesNotContain("_stageKills", src, StringComparison.Ordinal);
    }

    // ---------------- 文案表读取 ----------------

    private sealed record CopyRow(string Zh, string En);

    private static void AssertPlaceholders(IReadOnlyDictionary<string, CopyRow> copy, string key, int expected)
    {
        Assert.True(copy.ContainsKey(key), $"文案表缺键：{key}");
        Assert.Equal(expected, CountPlaceholders(copy[key].Zh));
        Assert.Equal(expected, CountPlaceholders(copy[key].En));
    }

    /// <summary>数占位符个数：`%%` 是转义的字面百分号，不计；其余 `%[.0-9]*[a-z]` 各计一个。</summary>
    private static int CountPlaceholders(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '%')
            {
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '%')
            {
                i += 1; // 字面百分号
                continue;
            }

            count += 1;
        }

        return count;
    }

    /// <summary>读 `data/translations.csv`（RFC4180 子集：双引号包裹、字段内换行、`""` 转义）。
    /// 取不到即抛——取不到判据必须显式失败，不得静默跳过。</summary>
    private static Dictionary<string, CopyRow> TranslationTable()
    {
        var text = RepoFiles.Read("data/translations.csv");
        var fields = ParseCsv(text);
        var rows = new Dictionary<string, CopyRow>(StringComparer.Ordinal);
        for (var i = 1; i < fields.Count; i++) // 第 0 行是表头
        {
            var row = fields[i];
            if (row.Count < 3 || row[0].Length == 0)
            {
                continue;
            }

            rows[row[0]] = new CopyRow(row[1], row[2]);
        }

        Assert.NotEmpty(rows);
        return rows;
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 1;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
