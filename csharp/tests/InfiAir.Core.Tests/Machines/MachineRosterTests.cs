using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using InfiAir.Core.Machines;
using Xunit;

namespace InfiAir.Core.Tests.Machines;

/// <summary>
/// 机型名册契约测试：把「有哪几型 / 每型动哪一项 / 叫什么 / 用哪套贴图」钉住，
/// 并顺手把三条本来靠人工核对的跨文件判据接到本测试里（AGENTS §3 清单项）：
/// 代码默认乘区 vs <c>data/balance.json machines.*</c>、文案键在 <c>data/translations.csv</c> 中英双列存在、
/// 每型四张贴图（本体 / 发光遮罩 / 两个受击帧）真的躺在 <c>assets/sprites/</c>。
/// 三条都属「不报错、只会悄悄坏」：数值两侧分叉时 json 完整即看不出、文案缺键玩家直接看到键名、
/// 贴图缺文件时引擎侧只是静默保持上一张图。
/// </summary>
public sealed class MachineRosterTests
{
    [Fact]
    public void RosterIsStandardFirstWithUniqueIds()
    {
        Assert.Equal(MachineRoster.StandardId, MachineRoster.All[0].Id);
        Assert.Equal(MachineRoster.All.Count, MachineRoster.Count);
        Assert.Same(MachineRoster.All[0], MachineRoster.Default);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in MachineRoster.All)
        {
            Assert.True(seen.Add(spec.Id), $"机型 id 重复：{spec.Id}");
            Assert.False(string.IsNullOrWhiteSpace(spec.Id));
            Assert.False(string.IsNullOrWhiteSpace(spec.NameKey));
        }

        // 五型特种型 + 标准型（用户可见的是六选一）
        Assert.Equal(6, MachineRoster.Count);
        Assert.Equal(5, CountSpecialists());
    }

    [Fact]
    public void EverySpecialistMovesExactlyItsOwnTrait()
    {
        foreach (var spec in MachineRoster.All)
        {
            var mods = spec.Defaults;
            var moved = new List<MachineTrait>();
            if (mods.MoveSpeedMult != 1.0) moved.Add(MachineTrait.MoveSpeed);
            if (mods.DamageMult != 1.0) moved.Add(MachineTrait.Damage);
            if (mods.FireIntervalMult != 1.0) moved.Add(MachineTrait.FireRate);
            if (mods.DamageTakenMult != 1.0) moved.Add(MachineTrait.Defense);
            if (mods.MaxHpMult != 1.0) moved.Add(MachineTrait.MaxHealth);

            if (spec.Trait == MachineTrait.None)
            {
                Assert.Equal(MachineModifiers.Baseline, mods); // 标准型 = 全 1.0，不得偷偷加成
                continue;
            }

            Assert.Equal(new[] { spec.Trait }, moved); // 恰好一项，且是它宣称的那一项
        }
    }

    [Fact]
    public void EverySpecialistBonusPointsTheStrongWay()
    {
        foreach (var spec in MachineRoster.All)
        {
            if (spec.Trait == MachineTrait.None)
            {
                continue;
            }

            // 加成项必须往「更强」的方向走：速度/伤害/血量递增，间隔/受伤递减。
            // 写反了（如 0.85 的加速或 1.15 的减伤）编译、冒烟全过，只有玩家会觉得「这机型是来惩罚我的」。
            var percent = MachineTraitText.Percent(spec.Trait, spec.Defaults);
            Assert.True(percent > 0, $"{spec.Id} 的加成方向写反了：{percent}%");
        }
    }

    [Fact]
    public void MissingUnknownOrEmptyIdFallsBackToStandard()
    {
        // 旧档（settings.json / run.json 无机型键）与手改档的未知 id 都走这一条：
        // 归一到标准型 = 行为与加机型之前逐位一致，不报错、不改档。
        Assert.Same(MachineRoster.Default, MachineRoster.ById(null));
        Assert.Same(MachineRoster.Default, MachineRoster.ById(string.Empty));
        Assert.Same(MachineRoster.Default, MachineRoster.ById("no_such_machine"));
        Assert.Same(MachineRoster.Default, MachineRoster.ById("Standard")); // 大小写不是白名单的一部分
        Assert.Equal(0, MachineRoster.IndexOf("no_such_machine"));

        foreach (var spec in MachineRoster.All)
        {
            Assert.Same(spec, MachineRoster.ById(spec.Id));
            Assert.Equal(spec.Id, MachineRoster.At(MachineRoster.IndexOf(spec.Id)).Id);
        }
    }

    [Fact]
    public void IndexWrapsBothWaysAndCycleSteps()
    {
        Assert.Equal(MachineRoster.All[0].Id, MachineRoster.At(0).Id);
        Assert.Equal(MachineRoster.All[0].Id, MachineRoster.At(MachineRoster.Count).Id);
        Assert.Equal(MachineRoster.All[^1].Id, MachineRoster.At(-1).Id);
        Assert.Equal(MachineRoster.All[1].Id, MachineRoster.Cycle(MachineRoster.StandardId, 1).Id);
        Assert.Equal(MachineRoster.All[^1].Id, MachineRoster.Cycle(MachineRoster.StandardId, -1).Id);
    }

    [Fact]
    public void SanitizeClampsDomainAndTreatsNonFiniteAsBaseline()
    {
        var clamped = MachineRoster.Sanitize(new MachineModifiers(0.0, -1.0, 0.0, double.PositiveInfinity, 99.0));
        Assert.Equal(MachineRoster.MinMult, clamped.MoveSpeedMult);
        Assert.Equal(MachineRoster.MinMult, clamped.DamageMult);
        Assert.Equal(MachineRoster.MinMult, clamped.FireIntervalMult);
        Assert.Equal(1.0, clamped.DamageTakenMult);   // 非有限值按基准，不是按上限
        Assert.Equal(MachineRoster.MaxMult, clamped.MaxHpMult);

        // NaN 是这里唯一的实际风险：钳制函数不拦 NaN，一路渗进玩家数值后 HUD 与判定一起 NaN 化
        var nan = MachineRoster.Sanitize(new MachineModifiers(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN));
        Assert.Equal(MachineModifiers.Baseline, nan);

        foreach (var spec in MachineRoster.All)
        {
            Assert.Equal(spec.Defaults, MachineRoster.Sanitize(spec.Defaults)); // 定稿值本身在域内
        }
    }

    [Fact]
    public void SpritePathsFollowTheExistingNamingContract()
    {
        // 标准型必须沿用既有文件名：改名即等于把已发布版本的机体贴图换掉，
        // 且 ShipEnergyFx 按「主贴图路径 → _glow.png」推导遮罩，改名会静默丢遮罩。
        Assert.Equal("res://assets/sprites/player_ship.png", MachineRoster.SpritePath("standard"));
        Assert.Equal("res://assets/sprites/player_ship_hit_1.png", MachineRoster.SpritePath("standard", HullDamageFrame.Light));
        Assert.Equal("res://assets/sprites/player_ship_hit_2.png", MachineRoster.SpritePath("standard", HullDamageFrame.Heavy));
        Assert.Equal("res://assets/sprites/player_ship_glow.png", MachineRoster.GlowSpritePath(MachineRoster.Default));

        foreach (var spec in MachineRoster.All)
        {
            if (spec.Id == MachineRoster.StandardId)
            {
                continue;
            }

            var stem = "res://assets/sprites/player_ship_" + spec.Id;
            Assert.Equal(stem + ".png", MachineRoster.SpritePath(spec));
            Assert.Equal(stem + "_hit_1.png", MachineRoster.SpritePath(spec, HullDamageFrame.Light));
            Assert.Equal(stem + "_hit_2.png", MachineRoster.SpritePath(spec, HullDamageFrame.Heavy));
            Assert.Equal(stem + "_glow.png", MachineRoster.GlowSpritePath(spec));
        }

        // 未知 id 回退标准型（同 ById 口径）
        Assert.Equal(MachineRoster.SpritePath("standard"), MachineRoster.SpritePath("no_such_machine"));
    }

    [Fact]
    public void TraitPercentFollowsTheResolvedModifiersNotTheTable()
    {
        // 面板上的百分比由**实际生效的乘区**反算：数值被调过之后面板必须跟着走
        Assert.Equal(15, MachineTraitText.Percent(MachineTrait.MoveSpeed, new MachineModifiers(1.15, 1, 1, 1, 1)));
        Assert.Equal(20, MachineTraitText.Percent(MachineTrait.Damage, new MachineModifiers(1, 1.2, 1, 1, 1)));
        Assert.Equal(18, MachineTraitText.Percent(MachineTrait.FireRate, new MachineModifiers(1, 1, 0.85, 1, 1))); // 1/0.85 = +17.6%
        Assert.Equal(15, MachineTraitText.Percent(MachineTrait.Defense, new MachineModifiers(1, 1, 1, 0.85, 1)));
        Assert.Equal(20, MachineTraitText.Percent(MachineTrait.MaxHealth, new MachineModifiers(1, 1, 1, 1, 1.2)));
        Assert.Equal(0, MachineTraitText.Percent(MachineTrait.None, MachineModifiers.Baseline));

        // 改了取值就得跟着变（这条是「文案不许写死」的判据）
        Assert.Equal(30, MachineTraitText.Percent(MachineTrait.MoveSpeed, new MachineModifiers(1.3, 1, 1, 1, 1)));
        Assert.Equal(-10, MachineTraitText.Percent(MachineTrait.Defense, new MachineModifiers(1, 1, 1, 1.1, 1)));

        Assert.Equal("MACHINE_TRAIT_STANDARD", MachineTraitText.Key(MachineTrait.None));
        foreach (var spec in MachineRoster.All)
        {
            Assert.StartsWith("MACHINE_TRAIT_", MachineTraitText.Key(spec.Trait), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CodeDefaultsMatchTheBalanceTable()
    {
        var machines = BalanceMachines();
        var knownFields = new HashSet<string>(StringComparer.Ordinal)
        {
            MachineRoster.MoveSpeedKey, MachineRoster.DamageKey, MachineRoster.FireIntervalKey,
            MachineRoster.DamageTakenKey, MachineRoster.MaxHpKey,
        };

        foreach (var (id, fields) in machines)
        {
            var spec = MachineRoster.ById(id);
            Assert.Equal(id, spec.Id); // 未知机型 id 写在数值表里：拼错时会被静默忽略

            foreach (var (field, value) in fields)
            {
                Assert.Contains(field, knownFields); // 未知字段名同理（move_speed_mul 会被静默忽略）
                Assert.Equal(Expected(spec, field), value, 6);
            }
        }

        // 每型加成的那一项必须真的在数值表里有键（否则调参界面里根本看不到它）
        foreach (var spec in MachineRoster.All)
        {
            if (spec.Trait == MachineTrait.None)
            {
                continue;
            }

            Assert.True(machines.ContainsKey(spec.Id), $"数值表缺少机型：{spec.Id}");
            Assert.True(machines[spec.Id].ContainsKey(FieldFor(spec.Trait)), $"数值表缺少加成键：{spec.Id}.{FieldFor(spec.Trait)}");
        }
    }

    [Fact]
    public void SpriteFilesExistForEveryMachineAndFrame()
    {
        foreach (var spec in MachineRoster.All)
        {
            foreach (var path in new[]
                     {
                         MachineRoster.SpritePath(spec),
                         MachineRoster.SpritePath(spec, HullDamageFrame.Light),
                         MachineRoster.SpritePath(spec, HullDamageFrame.Heavy),
                         MachineRoster.GlowSpritePath(spec),
                     })
            {
                var relative = path.Replace("res://", string.Empty, StringComparison.Ordinal);
                Assert.True(File.Exists(RepoFiles.PathOf(relative)), $"贴图缺失：{relative}（跑 scripts/tools/regenerate_all.sh）");
            }
        }
    }

    [Fact]
    public void CopyKeysExistInBothLanguages()
    {
        var table = TranslationTable();
        foreach (var spec in MachineRoster.All)
        {
            foreach (var key in new[] { spec.NameKey, MachineTraitText.Key(spec.Trait) })
            {
                Assert.True(table.TryGetValue(key, out var row), $"文案表缺键：{key}");
                Assert.False(string.IsNullOrWhiteSpace(row.Zh), $"{key} 缺中文");
                Assert.False(string.IsNullOrWhiteSpace(row.En), $"{key} 缺英文");
            }
        }
    }

    // ---- 取源工具 ----

    /// <summary>数值表里 <c>machines.&lt;id&gt;.&lt;field&gt;</c> 的实际取值。</summary>
    private static Dictionary<string, Dictionary<string, double>> BalanceMachines()
    {
        var root = JsonDocument.Parse(RepoFiles.Read("data/balance.json")).RootElement;
        var result = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        Assert.True(root.TryGetProperty("machines", out var machines), "balance.json 缺 machines 分区");
        foreach (var machine in machines.EnumerateObject())
        {
            var fields = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var field in machine.Value.EnumerateObject())
            {
                fields[field.Name] = field.Value.GetDouble();
            }

            result[machine.Name] = fields;
        }

        Assert.NotEmpty(result);
        return result;
    }

    private static double Expected(MachineSpec spec, string field) => field switch
    {
        MachineRoster.MoveSpeedKey => spec.Defaults.MoveSpeedMult,
        MachineRoster.DamageKey => spec.Defaults.DamageMult,
        MachineRoster.FireIntervalKey => spec.Defaults.FireIntervalMult,
        MachineRoster.DamageTakenKey => spec.Defaults.DamageTakenMult,
        MachineRoster.MaxHpKey => spec.Defaults.MaxHpMult,
        _ => double.NaN,
    };

    private static string FieldFor(MachineTrait trait) => trait switch
    {
        MachineTrait.MoveSpeed => MachineRoster.MoveSpeedKey,
        MachineTrait.Damage => MachineRoster.DamageKey,
        MachineTrait.FireRate => MachineRoster.FireIntervalKey,
        MachineTrait.Defense => MachineRoster.DamageTakenKey,
        MachineTrait.MaxHealth => MachineRoster.MaxHpKey,
        _ => string.Empty,
    };

    private static int CountSpecialists()
    {
        var count = 0;
        foreach (var spec in MachineRoster.All)
        {
            if (spec.Trait != MachineTrait.None)
            {
                count++;
            }
        }

        return count;
    }

    private readonly record struct CopyRow(string Zh, string En);

    /// <summary>读 `data/translations.csv`（RFC4180 子集：双引号包裹、字段内换行、`""` 转义）。
    /// 取不到即抛——取不到判据必须显式失败，不得静默跳过。</summary>
    private static Dictionary<string, CopyRow> TranslationTable()
    {
        var rows = new Dictionary<string, CopyRow>(StringComparer.Ordinal);
        foreach (var row in ParseCsv(RepoFiles.Read("data/translations.csv")))
        {
            if (row.Count >= 3 && row[0].Length > 0 && row[0] != "keys")
            {
                rows[row[0]] = new CopyRow(row[1], row[2]);
            }
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
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
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
