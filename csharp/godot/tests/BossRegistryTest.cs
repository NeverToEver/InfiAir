using Godot;

namespace InfiAir.Tests;

/// <summary>
/// A3 架构断言测试：Boss 攻击/移动/狂暴注册表与机型参数表完整性——
/// 注册表覆盖全部机型与模式表（脚本默认 + balance.json 运行表）引用的攻击 id；
/// 新增攻击/机型只需加注册行，不再改既有分发函数（O 原则达成）。
/// M3d：Boss/BossAttacks/BossMovement/EnrageSequence 已迁 C#——本测试断言对象为纯 C# 类
/// （组件注册表 + Boss 静态表），C# 侧直接实例化/静态访问（GDScript 无法经 load 解析纯 C# 类）。
/// </summary>
public partial class BossRegistryTest : Node
{
    private int _failures;

    private void Check(bool cond, string label)
    {
        if (cond)
        {
            GD.Print("[PASS] " + label);
        }
        else
        {
            _failures++;
            GD.PushError("[FAIL] " + label);
        }
    }

    /// <summary>按点分路径导航解析后的 JSON 字典（"a.b.c" → data["a"]["b"]["c"]），任一环缺失返回 null</summary>
    private static Variant JsonAt(Variant data, string path)
    {
        Variant node = data;
        foreach (var key in path.Split('.'))
        {
            if (node.VariantType == Variant.Type.Dictionary)
            {
                var dict = node.AsGodotDictionary();
                if (dict.ContainsKey(key))
                {
                    node = dict[key];
                }
                else
                {
                    return default;
                }
            }
            else
            {
                return default;
            }
        }

        return node;
    }

    /// <summary>GDScript str(Array[StringName]) 格式（如 [&amp;"fan5", &amp;"homing"]）</summary>
    private static string FormatStringNameArray(System.Collections.Generic.List<string> items)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var item in items)
        {
            parts.Add("&\"" + item + "\"");
        }

        return "[" + string.Join(", ", parts) + "]";
    }

    /// <summary>GDScript str(Array[String]) 格式（如 ["fan5", "homing"]）</summary>
    private static string FormatStringArray(System.Collections.Generic.List<string> items)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var item in items)
        {
            parts.Add("\"" + item + "\"");
        }

        return "[" + string.Join(", ", parts) + "]";
    }

    public override void _Ready()
    {
        Run();
    }

    private void Run()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            var attacks = new BossAttacks();
            var boss = new Boss(); // U07：GetDefaultPatterns 改实例方法后经实例访问

            // 1. 攻击注册表：10 个已知攻击 id 全覆盖（homing2 于 2026-08-03 审计删除，弹数分档并入 homing；
            // 2026-08-04 新增 ring_burst——4 型「月蚀」环弹）
            string[] attackIds = { "fan5", "fan7", "homing", "sniper3", "cross", "charged_cannon", "dash_sweep", "minion_volley", "bullet_wall", "ring_burst" };
            foreach (var id in attackIds)
            {
                Check(attacks.HasAttack(new StringName(id)), "攻击注册表包含 " + id);
            }

            Check(attacks.AttackIds().Count == 10, "攻击注册表共 10 项");
            // B 梯队（fair plan §8）：每攻击独特 tell——注册的攻击 id 全部有 tell 配置
            // （新攻击漏配 tell 时此断言拦截；ATTACK_TELLS 缺失键 = 无 tell）
            var tellMissing = new System.Collections.Generic.List<string>();
            var attackTells = BossAttacks.GetAttackTells();
            foreach (var idVar in attacks.AttackIds())
            {
                if (!attackTells.ContainsKey(idVar))
                {
                    tellMissing.Add(idVar.AsStringName().ToString());
                }
            }

            Check(tellMissing.Count == 0, "全部攻击 id 有独特 tell 配置（缺失: " + FormatStringNameArray(tellMissing) + "）");

            // 2. 模式表交叉验证：脚本默认表 + balance.json 运行表引用的攻击 id 全在注册表
            var parsed = Json.ParseString(Godot.FileAccess.GetFileAsString(gs.BALANCE_PATH));
            var missing = new System.Collections.Generic.List<string>();
            for (int t = 1; t <= 4; t++)
            {
                var defaults = boss.GetDefaultPatterns()[t].AsGodotDictionary();
                var cfgTable = JsonAt(parsed, "boss.phases.type" + t);
                foreach (var phaseKv in defaults)
                {
                    var patterns = phaseKv.Value.AsGodotArray();
                    foreach (var patternVar in patterns)
                    {
                        var attack = patternVar.AsGodotDictionary().GetValueOrDefault("attack", new StringName("")).AsStringName();
                        if (!attacks.HasAttack(attack))
                        {
                            missing.Add(attack.ToString());
                        }
                    }
                }

                if (cfgTable.VariantType == Variant.Type.Dictionary)
                {
                    foreach (var phaseKv in cfgTable.AsGodotDictionary())
                    {
                        if (phaseKv.Value.VariantType != Variant.Type.Array)
                        {
                            continue;  // 非模式段键（如 type3.summon_interval）跳过
                        }

                        foreach (var patternVar in phaseKv.Value.AsGodotArray())
                        {
                            var attack = patternVar.AsGodotDictionary().GetValueOrDefault("attack", new StringName("")).AsStringName();
                            if (!attacks.HasAttack(attack))
                            {
                                missing.Add(attack.ToString());
                            }
                        }
                    }
                }
            }

            Check(missing.Count == 0, "模式表引用的攻击 id 全部注册（缺失: " + FormatStringArray(missing) + "）");

            // 3. 移动器注册表覆盖 4 机型
            var movement = new BossMovement();
            for (int t = 1; t <= 4; t++)
            {
                Check(movement.HasMover(t), "移动器注册表包含机型 " + t);
            }

            // 4. 狂暴三阶段注册表覆盖 4 机型
            var enrage = new EnrageSequence();
            for (int t = 1; t <= 4; t++)
            {
                Check(enrage.HasActiveHandler(t), "狂暴 ACTIVE 处理器包含机型 " + t);
                Check(enrage.HasReleaseHandler(t), "狂暴 RELEASE 处理器包含机型 " + t);
                Check(enrage.HasReleaseBeginHandler(t), "狂暴释放起手处理器包含机型 " + t);
            }

            // 5. 机型参数表：召唤表仅 3 型启用；闪白时长表覆盖全部机型
            Check(Boss.GetSummonerTypes().GetValueOrDefault(3, false).AsBool(), "召唤表：3 型启用独立召唤");
            Check(
                !Boss.GetSummonerTypes().GetValueOrDefault(1, false).AsBool()
                && !Boss.GetSummonerTypes().GetValueOrDefault(2, false).AsBool()
                && !Boss.GetSummonerTypes().GetValueOrDefault(4, false).AsBool(),
                "召唤表：1/2/4 型不启用");
            for (int t = 1; t <= 4; t++)
            {
                Check(Boss.GetHitFlashByType().ContainsKey(t), "闪白时长表包含机型 " + t);
            }
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BOSS REGISTRY TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BOSS REGISTRY TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
