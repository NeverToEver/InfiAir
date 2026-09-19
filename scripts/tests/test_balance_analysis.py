#!/usr/bin/env python3
"""平衡分析引擎（scripts/tools/balance_analysis.py）的测试。

抓的静默错误：
1. 派生公式与 C# 侧分叉——分析面板显示的是另一套曲线，而界面照常渲染、没人会怀疑数字；
   故算例直接抄自 csharp/tests 的对应测试（每组标注来源文件），C# 侧改公式而这里没跟，就能对上账；
2. 元数据（balance_meta.json）与数值表脱节——键改名后说明静默失效，编辑者失去取值护栏；
3. 体检不报——越界值与「整表回退」的形态从界面上看不出来。

数值调整（改 balance.json）不该让本文件变红：凡是断言具体数字的用例都用显式构造的配置，
只有「真实文件能不能跑通」这一类才读 data/balance.json。
"""

from __future__ import annotations

import copy
import json
import sys
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))
import balance_analysis as analysis  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
BALANCE = REPO / "data" / "balance.json"


def cfg_of(tree: dict) -> analysis.Config:
    return analysis.Config(tree)


def real_balance() -> dict:
    return json.loads(BALANCE.read_text(encoding="utf-8"))


class DifficultyScalingTests(unittest.TestCase):
    """算例来源：csharp/tests/InfiAir.Core.Tests/Progression/DifficultyScalingTests.cs。"""

    def setUp(self):
        self.cfg = cfg_of({
            "enemies": {"hp_ramp_factor": 0.4, "damage_ramp_factor": 0.2, "speed_ramp_factor": 0.1,
                        "speed_ramp_cap": 1.8, "fire_interval_floor": 1.2,
                        "boss_density_per_difficulty": 3.0, "boss_density_bonus_cap": 4},
            "spawner": {"difficulty_factor": 0.15, "interval_min": 2.5,
                        "elite_per_difficulty": 2.0, "elite_count_cap": 3},
            "progression": {"soft_cap_start": 6.0, "tail_speed_factor": 0.5},
        })

    def test_linear_ramp(self):
        self.assertEqual(1.0, analysis.linear_ramp(1.0, 0.4))
        self.assertAlmostEqual(1.8, analysis.linear_ramp(3.0, 0.4))
        self.assertEqual(1.0, analysis.linear_ramp(0.5, 0.4))    # D≤1 不反向削弱
        self.assertEqual(1.0, analysis.linear_ramp(5.0, 0.0))    # 关闭
        self.assertEqual(1.0, analysis.linear_ramp(5.0, -1.0))

    def test_wave_interval(self):
        self.assertAlmostEqual(4.0, analysis.wave_interval(4.0, 1.0, self.cfg))
        self.assertAlmostEqual(4.0 / 1.3, analysis.wave_interval(4.0, 3.0, self.cfg))
        self.assertAlmostEqual(2.5, analysis.wave_interval(4.0, 1000.0, self.cfg))   # 钳下限

    def test_fire_interval_floor(self):
        self.assertAlmostEqual(1.2, analysis.fire_interval(0.5, 1000.0, self.cfg))
        self.assertAlmostEqual(2.2 / 1.3, analysis.fire_interval(2.2, 3.0, self.cfg))

    def test_elite_count(self):
        self.assertEqual([1, 1, 2, 2, 3, 3],
                         [analysis.elite_count(d, self.cfg) for d in (1.0, 2.0, 3.0, 4.9, 5.0, 50.0)])

    def test_boss_density_bonus(self):
        self.assertEqual([0, 0, 1, 1, 2],
                         [analysis.boss_density_bonus(d, self.cfg) for d in (1.0, 3.9, 4.0, 6.9, 7.0)])
        self.assertEqual(4, analysis.boss_density_bonus(1000.0, self.cfg))   # 触顶

    def test_enemy_speed_ramp_cap(self):
        self.assertAlmostEqual(1.4, analysis.enemy_speed_ramp(5.0, self.cfg))
        self.assertAlmostEqual(1.8, analysis.enemy_speed_ramp(10.0, self.cfg))

    def test_soft_capped_time_term(self):
        self.assertAlmostEqual(5.0, analysis.soft_capped_time_term(5.0, 6.0, 0.5))
        self.assertAlmostEqual(11.0, analysis.soft_capped_time_term(16.0, 6.0, 0.5))
        self.assertAlmostEqual(7.0, analysis.soft_capped_time_term(7.0, 0.0, 0.5))    # 关闭软上限


class DifficultyCurveTests(unittest.TestCase):
    """算例来源：csharp/tests/InfiAir.Core.Tests/Progression/ProgressionCurvesTests.cs（DifficultyCurve.Compute）。"""

    def setUp(self):
        self.cfg = cfg_of({"progression": {"per_boss_kill": 0.6, "per_ten_minutes": 1.5,
                                           "time_step_seconds": 30, "soft_cap_start": 6.0,
                                           "tail_speed_factor": 0.5}})

    def test_time_quantization(self):
        self.assertAlmostEqual(1.075, analysis.difficulty_at(30, 0, self.cfg))
        self.assertAlmostEqual(1.075, analysis.difficulty_at(59, 0, self.cfg))
        self.assertAlmostEqual(1.15, analysis.difficulty_at(60, 0, self.cfg))

    def test_boss_term_and_bad_time(self):
        self.assertAlmostEqual(2.2, analysis.difficulty_at(0, 2, self.cfg))
        self.assertAlmostEqual(2.2, analysis.difficulty_at(float("nan"), 2, self.cfg))

    def test_soft_cap_kicks_in_after_six(self):
        # 时间项 6.0 出现在 2400s；之后斜率减半（+0.075/30s → +0.0375/30s）
        self.assertAlmostEqual(6.0 + (7.5 - 6.0) * 0.5 + 1.0, analysis.difficulty_at(3000, 0, self.cfg), places=6)


class MilestoneCurveTests(unittest.TestCase):
    """算例来源：csharp/tests/InfiAir.Core.Tests/Progression/ProgressionCurvesTests.cs（MilestoneCurve.Threshold）。"""

    def setUp(self):
        base = [3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000]
        self.cfg = cfg_of({"milestones": {"base": base, "cycle_mult": 1.35}})

    def test_cycle_multiplication(self):
        self.assertEqual(84050, analysis.milestone_threshold(8, self.cfg))
        self.assertEqual(100250, analysis.milestone_threshold(10, self.cfg))
        self.assertEqual(188000, analysis.milestone_threshold(15, self.cfg))
        self.assertEqual(193468, analysis.milestone_threshold(16, self.cfg))

    def test_difficulty_multiplier(self):
        self.assertEqual(126075, analysis.milestone_threshold(8, self.cfg, 1.5))

    def test_flat_curve_closed_form(self):
        # 平坦曲线（cycle_mult=1）走闭式：3 圈完整 + 第 4 圈的当前档 = 3×200 + 200
        cfg = cfg_of({"milestones": {"base": [100, 200], "cycle_mult": 1.0}})
        self.assertEqual(800, analysis.milestone_threshold(7, cfg))   # cycle=3, step=1


class RewardScalingTests(unittest.TestCase):
    """算例来源：csharp/tests/InfiAir.Core.Tests/Progression/RewardScalingTests.cs。"""

    def setUp(self):
        self.cfg = cfg_of({
            "reward_scaling": {"kill_score_ramp_factor": 0.15, "graze_combo_weight": 1.0,
                               "graze_difficulty_factor": 0.15},
            "scoring": {"combo": {"window": 5.0, "step": 0.1, "max_mult": 2.0}},
        })

    def test_kill_score_factor(self):
        self.assertAlmostEqual(1.6, analysis.kill_score_factor(5.0, self.cfg))

    def test_graze_score(self):
        self.assertAlmostEqual(30.0, analysis.graze_score(30.0, 1.0, 1.0, self.cfg))
        self.assertAlmostEqual(60.0, analysis.graze_score(30.0, 2.0, 1.0, self.cfg))
        self.assertAlmostEqual(48.0, analysis.graze_score(30.0, 1.0, 5.0, self.cfg))
        self.assertEqual(0.0, analysis.graze_score(0.0, 1.0, 5.0, self.cfg))   # 非正基础分归零

    def test_combo_multiplier(self):
        self.assertAlmostEqual(1.0, analysis.combo_mult(1, self.cfg))
        self.assertAlmostEqual(1.4, analysis.combo_mult(5, self.cfg))
        self.assertAlmostEqual(2.0, analysis.combo_mult(20, self.cfg))   # 触顶


class TalentAndCombatTests(unittest.TestCase):
    """算例来源：csharp/tests/InfiAir.Core.Tests/Talent/TalentEconomyTests.cs 与 csharp/godot/Player.cs 开火段。"""

    def test_talent_cost(self):
        cfg = cfg_of({"talent": {"cost": {"base": 2, "increment": 1, "softcap": 3}}})
        self.assertAlmostEqual(2.0, analysis.talent_cost(cfg, 0))
        self.assertAlmostEqual(5.0, analysis.talent_cost(cfg, 3))

    def test_player_dps_decomposition(self):
        cfg = cfg_of({
            "player": {"base_fire_interval": 0.15, "bullet_damage": 10},
            "augments": {"power_shot": {"factor": 1.25, "max_stacks": 5},
                         "rapid_fire": {"factor": 0.75, "max_stacks": 4},
                         "spread_shot": {"max_stacks": 2},
                         "crit_shot": {"chance": 0.12, "multiplier": 2.0, "max_stacks": 3},
                         "salvo": {"every_base": 7, "every_step": 2, "damage_mult": 3.0, "max_stacks": 2},
                         "armor": {"multiplier": 0.85}, "evasion": {"chance": 0.2}},
            "talent": {"cost": {"softcap": 3}, "softcaps": {"power_shot": 3, "rapid_fire": 2, "crit_shot": 2, "salvo": 1}},
        })
        bare = analysis.player_dps(cfg, {})
        self.assertAlmostEqual(10 / 0.15, bare["dps"], places=6)
        leveled = analysis.player_dps(cfg, {"power_shot": 1, "rapid_fire": 1, "spread_shot": 1, "salvo": 1})
        self.assertAlmostEqual(0.15 * 0.75, leveled["interval"], places=6)
        self.assertEqual(13, leveled["damage"])                     # round(10×1.25)
        self.assertEqual(3, leveled["count"])                       # 1 + 2×1
        self.assertEqual(5, leveled["salvo_every"])                 # max(7−2, 3)
        self.assertAlmostEqual((4 + 3.0) / 5, leveled["salvo_factor"], places=6)

    def test_incoming_damage_mitigation(self):
        cfg = cfg_of({"augments": {"armor": {"multiplier": 0.85}, "evasion": {"chance": 0.2}}})
        self.assertAlmostEqual(20 * 0.85 * 0.8, analysis.expected_incoming_damage(cfg, 20.0), places=6)


class MetaIntegrityTests(unittest.TestCase):
    """元数据与数值表的一致性：键改名/文件搬走后说明会静默失效，正是要在这里拦住。"""

    def setUp(self):
        self.balance = real_balance()
        self.meta = analysis.load_meta()

    def test_every_meta_template_resolves_on_real_balance(self):
        orphans = [key for key, entry in analysis.expand_meta(self.meta, self.balance).items()
                   if entry.get("orphan")]
        self.assertEqual([], orphans, f"元数据里有对不上数值表的键：{orphans}")

    def test_every_source_file_exists(self):
        missing = set()
        for entry in list(self.meta.get("keys", {}).values()) + list(self.meta.get("sections", {}).values()):
            source = entry.get("source")
            if source and not (REPO / source).exists():
                missing.add(source)
        self.assertEqual(set(), missing, f"元数据里的出处文件不存在：{sorted(missing)}")

    def test_every_top_level_key_has_a_section_entry(self):
        undocumented = [key for key in self.balance if key not in self.meta.get("sections", {})]
        self.assertEqual([], undocumented, f"新增分区缺少说明：{undocumented}")


class RangeCheckTests(unittest.TestCase):
    """体检必须能失败：这些用例就是「怎么破坏、红了什么」的存档。"""

    def test_real_balance_passes(self):
        report = analysis.build_report(real_balance())
        self.assertEqual([], report["warnings"])

    def test_out_of_range_value_is_reported(self):
        balance = real_balance()
        balance["player"]["max_speed"] = -1          # meta 登记 range 下限 0
        warnings = analysis.check_ranges(balance, analysis.expand_meta(analysis.load_meta(), balance))
        self.assertEqual(["player.max_speed"], [w["path"] for w in warnings])

    def test_structure_checks_fire(self):
        cases = [
            ("milestones.base", lambda b: b["milestones"].__setitem__("base", [100, 50])),
            ("milestones.cycle_mult", lambda b: b["milestones"].__setitem__("cycle_mult", 0.5)),
            ("boss.hp_mults", lambda b: b["boss"].__setitem__("hp_mults", [1.0, 0.0, 1.0, 1.0])),
            ("spawner.unlock_scores", lambda b: b["spawner"].__setitem__("unlock_scores", [0, 1])),
            ("boss.difficulty_scaling.interval_mult", lambda b: b["boss"]["difficulty_scaling"].__setitem__("interval_mult", [1.0])),
            ("progression.tier_thresholds", lambda b: b["progression"].__setitem__("tier_thresholds", [1.0, 0.5])),
            ("dda.factor", lambda b: b["dda"].__setitem__("factor", 0.5)),
            ("enemies.types[0].hp", lambda b: b["enemies"]["types"][0].__setitem__("hp", [1])),
        ]
        for expected_path, mutate in cases:
            with self.subTest(path=expected_path):
                balance = real_balance()
                mutate(balance)
                paths = [w["path"] for w in analysis.check_structures(balance)]
                self.assertIn(expected_path, paths)


class ReportShapeTests(unittest.TestCase):
    def test_report_renders_for_real_balance(self):
        report = analysis.build_report(real_balance())
        ids = [section["id"] for section in report["sections"]]
        self.assertEqual(["difficulty", "enemy", "combat", "economy", "augments"], ids)
        for section in report["sections"]:
            self.assertTrue(section.get("source"), f"{section['id']} 缺公式出处")
            for key in ("table", "table2", "table3"):
                table = section.get(key)
                if table:
                    self.assertTrue(table["rows"], f"{section['id']}.{key} 空表")
                    for row in table["rows"]:
                        self.assertEqual(len(table["head"]), len(row), f"{section['id']}.{key} 列数与表头不符")
        series = report["sections"][0]["series"]
        self.assertEqual(len(series["x"]), len(series["y"]))
        self.assertLessEqual(series["y"][0], series["y"][-1])   # 难度曲线单调不减

    def test_analysis_survives_degenerate_input(self):
        # 分析失败不能连累编辑：极端但结构合法的值也要能算出一个报告
        balance = real_balance()
        balance["milestones"]["cycle_mult"] = 3.0
        balance["progression"]["per_ten_minutes"] = 500.0
        report = analysis.build_report(balance)
        self.assertTrue(report["sections"])


class PresetTests(unittest.TestCase):
    """预设：给不熟悉单个键的人用的入口，一旦路径写错或结果越界就是「点了没反应/悄悄改坏」。"""

    def setUp(self):
        self.balance = real_balance()
        self.meta = analysis.load_meta()
        self.presets = analysis.load_presets()

    def test_presets_are_well_formed(self):
        self.assertTrue(self.presets, "预设文件为空或格式变了")
        ids = [p["id"] for p in self.presets]
        self.assertEqual(len(ids), len(set(ids)), "预设 id 重复")
        for preset in self.presets:
            self.assertTrue(preset.get("name"), preset["id"])
            self.assertTrue(preset.get("desc"), f"{preset['id']} 缺面向玩家的说明")
            self.assertTrue(preset.get("ops"), f"{preset['id']} 没有任何操作")

    def test_every_op_path_resolves_and_applies(self):
        for preset in self.presets:
            plan = analysis.plan_preset(preset, self.balance)
            self.assertEqual([], plan["skipped"], f"{preset['id']} 有对不上的路径：{plan['skipped']}")
            self.assertTrue(plan["changes"], f"{preset['id']} 在默认数值上没有任何改动")

    def test_results_stay_inside_registered_ranges(self):
        for preset in self.presets:
            changed, _ = analysis.apply_preset(preset, self.balance)
            warnings = analysis.check_ranges(changed, analysis.expand_meta(self.meta, changed))
            self.assertEqual([], warnings, f"{preset['id']} 把值改出了登记范围：{warnings}")

    def test_apply_is_relative_and_leaves_input_alone(self):
        preset = next(p for p in self.presets if p["id"] == "relaxed")
        before = copy.deepcopy(self.balance)
        once, _ = analysis.apply_preset(preset, self.balance)
        twice, _ = analysis.apply_preset(preset, once)
        self.assertEqual(before, self.balance, "apply_preset 不该改动传入的树")
        # 相对变换：第二次是在第一次结果上再乘一次（这正是「连点两次会叠加」的语义，界面有说明）
        self.assertNotAlmostEqual(analysis.read_path(once, "difficulty.medium.hp"),
                                  analysis.read_path(twice, "difficulty.medium.hp"))

    def test_bool_keys_only_take_bool_set(self):
        preset = next(p for p in self.presets if p["id"] == "calm")
        plan = analysis.plan_preset(preset, self.balance)
        entry = next(c for c in plan["changes"] if c["path"] == "fog_events.enabled")
        self.assertIs(False, entry["to"])

    def test_scale_operations_leave_bools_and_ints_unsupported(self):
        # scale 落在布尔键上必须被跳过而不是算成数字（否则会写出 0.75 这种非法 bool）
        plan = analysis.plan_preset({"id": "t", "ops": [{"scale": 0.5, "paths": ["fog_events.enabled"]}]},
                                    self.balance)
        self.assertEqual([], plan["changes"])
        self.assertEqual(1, len(plan["skipped"]))


if __name__ == "__main__":
    unittest.main()
