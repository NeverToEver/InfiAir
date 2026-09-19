#!/usr/bin/env python3
"""InfiAir 平衡分析引擎（离线只读）。

把 data/balance.json 换算成可读的派生量：难度曲线、战斗节奏、经济与进度、增幅每层收益，
并对全表做一次取值体检（哪些值会被 C# 侧的判型钳制或触发整表回退）。

**这里不是第二份权威**：每条派生量都标了公式出处（`source` 字段，指向 C# 的纯函数或消费点），
取值范围的权威仍是代码里的钳制。改公式必须同时改出处指向的文件与 scripts/tests 的镜像算例
（算例抄自 csharp/tests 的对应测试，两侧锁同一组数字）。

纯函数、零依赖（仅标准库），可被 balance_editor.py 直接 import，也可单独跑：
    python3 scripts/tools/balance_analysis.py [--json]
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
META_PATH = Path(__file__).resolve().parent / "balance_meta.json"
PRESETS_PATH = Path(__file__).resolve().parent / "balance_presets.json"
BALANCE_PATH = ROOT / "data" / "balance.json"

# 公式出处（改公式前先改这里指向的文件，再改本文件的实现）
SRC_DIFFICULTY_CURVE = "csharp/core/Progression/DifficultyCurve.cs"
SRC_DIFFICULTY_SCALING = "csharp/core/Progression/DifficultyScaling.cs"
SRC_REWARD_SCALING = "csharp/core/Progression/RewardScaling.cs"
SRC_MILESTONE_CURVE = "csharp/core/Progression/MilestoneCurve.cs"
SRC_TALENT_ECONOMY = "csharp/core/Talent/TalentEconomy.cs"
SRC_PLAYER_FIRE = "csharp/godot/Player.cs"
SRC_SPAWNER = "csharp/godot/Spawner.cs"
SRC_MOTHERSHIP = "csharp/godot/Mothership.cs"
SRC_DAMAGE_MITIGATION = "csharp/core/Combat/DamageMitigation.cs"


# ---------------------------------------------------------------- 取值工具


def is_num(value: object) -> bool:
    """数值判定：bool 是 int 子类，必须先排除（与 C# 侧判型口径一致）。"""
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def is_num_array(value: object) -> bool:
    """数字数组判定：界面把这类值当一行逗号表编辑，故整组当一个叶子比较。"""
    return isinstance(value, list) and bool(value) and all(is_num(v) for v in value)


class Config:
    """balance 树的只读点路径视图；缺键/类型不符返回默认值（语义同 C# PathResolver）。"""

    def __init__(self, tree: dict):
        self.tree = tree

    def get(self, path: str, default: object = None) -> object:
        node: object = self.tree
        for part in path.split("."):
            if not isinstance(node, dict) or part not in node:
                return default
            node = node[part]
        return node

    def num(self, path: str, default: float = 0.0) -> float:
        value = self.get(path, default)
        return float(value) if is_num(value) else float(default)

    def as_int(self, path: str, default: int = 0) -> int:
        value = self.get(path, default)
        return int(value) if is_num(value) else int(default)

    def section(self, path: str) -> dict:
        value = self.get(path, {})
        return value if isinstance(value, dict) else {}


# ------------------------------------------------------- 派生公式（镜像 C#）


def linear_ramp(difficulty: float, factor: float) -> float:
    """×(1 + factor×(D−1))；D≤1 不反向削弱，非有限/非正 factor 回退 1.0。

    出处 csharp/core/Progression/DifficultyRamp.cs Linear()。
    """
    if not math.isfinite(difficulty) or not math.isfinite(factor) or factor <= 0.0:
        return 1.0
    d = 1.0 if difficulty <= 1.0 else difficulty
    return 1.0 + factor * (d - 1.0)


def soft_capped_time_term(time_term: float, start: float, factor: float) -> float:
    """时间项软上限：超出 start 的部分按 factor 折减；关闭条件原样返回。出处 DifficultyScaling.SoftCappedTimeTerm。"""
    if not math.isfinite(time_term) or time_term <= 0.0:
        return 0.0
    if (not math.isfinite(start) or not math.isfinite(factor)
            or start <= 0.0 or factor <= 0.0 or factor >= 1.0 or time_term <= start):
        return time_term
    return start + (time_term - start) * factor


def difficulty_at(run_time: float, boss_kills: int, cfg: Config) -> float:
    """本局难度乘数 D：1 + per_boss_kill×Boss击杀 + 软上限后的时间项。出处 DifficultyCurve.Compute + RunProgressionService。"""
    per_boss = cfg.num("progression.per_boss_kill", 0.6)
    per_ten = cfg.num("progression.per_ten_minutes", 1.5)
    step = cfg.num("progression.time_step_seconds", 30.0)
    if not math.isfinite(run_time) or run_time <= 0.0 or step <= 0.0 or not math.isfinite(step):
        return 1.0 + per_boss * boss_kills
    steps = math.floor(min(run_time, 1e6) / step)
    time_term = steps * step / 600.0 * per_ten
    time_term = soft_capped_time_term(
        time_term,
        cfg.num("progression.soft_cap_start", 6.0),
        cfg.num("progression.tail_speed_factor", 0.5),
    )
    return 1.0 + per_boss * boss_kills + time_term


def enemy_speed_ramp(difficulty: float, cfg: Config) -> float:
    """速度乘区（带上限）；上限非有限时整条乘区退回 1.0。出处 DifficultyScaling.EnemySpeedRamp。"""
    ramp = linear_ramp(difficulty, cfg.num("enemies.speed_ramp_factor", 0.1))
    cap = cfg.num("enemies.speed_ramp_cap", 1.8)
    if not math.isfinite(cap):
        return 1.0
    return min(ramp, max(cap, 1.0)) if cap > 0.0 else ramp


def wave_interval(base_interval: float, difficulty: float, cfg: Config) -> float:
    """波间隔 = 基础间隔 ÷(1+spawner.difficulty_factor×(D−1))，钳下限。出处 DifficultyScaling.WaveInterval。"""
    floor = cfg.num("spawner.interval_min", 2.5)
    floor = floor if floor > 0.0 else 0.0
    if not math.isfinite(base_interval) or base_interval <= 0.0:
        return floor
    divisor = linear_ramp(difficulty, cfg.num("spawner.difficulty_factor", 0.15))
    interval = base_interval / (divisor if divisor > 0.0 else 1.0)
    return max(interval, floor)


def fire_interval(base_interval: float, difficulty: float, cfg: Config) -> float:
    """开火间隔 = 基础间隔 ÷ 难度项，钳下限（默认 1.2s）。出处 DifficultyScaling.FireInterval。"""
    floor = cfg.num("enemies.fire_interval_floor", 1.2)
    floor = floor if floor > 0.0 else 0.05
    if not math.isfinite(base_interval) or base_interval <= 0.0:
        return floor
    divisor = linear_ramp(difficulty, cfg.num("spawner.difficulty_factor", 0.15))
    interval = base_interval / (divisor if divisor > 0.0 else 1.0)
    return max(interval, floor)


def elite_count(difficulty: float, cfg: Config) -> int:
    """精英数量：每 elite_per_difficulty 点 D 增派一只，钳 [1, cap]；配置关闭恒为 1。出处 DifficultyScaling.EliteCount。"""
    per = cfg.num("spawner.elite_per_difficulty", 2.0)
    cap = max(cfg.as_int("spawner.elite_count_cap", 3), 1)
    if not math.isfinite(per) or per <= 0.0 or cap <= 1 or not math.isfinite(difficulty):
        return 1
    extra = math.floor(max((difficulty - 1.0) / per, 0.0))
    return min(1 + extra, cap)


def boss_density_bonus(difficulty: float, cfg: Config) -> int:
    """Boss 弹量密度追加量（0..上限）。出处 DifficultyScaling.BossDensityBonus。"""
    per = cfg.num("enemies.boss_density_per_difficulty", 3.0)
    cap = max(cfg.as_int("enemies.boss_density_bonus_cap", 4), 0)
    if not math.isfinite(per) or per <= 0.0 or cap <= 0 or not math.isfinite(difficulty):
        return 0
    if difficulty <= 1.0:
        return 0
    return min(math.floor((difficulty - 1.0) / per), cap)


def kill_score_factor(difficulty: float, cfg: Config) -> float:
    """击杀分难度乘区。出处 RewardScaling.KillScoreFactor。"""
    return linear_ramp(difficulty, cfg.num("reward_scaling.kill_score_ramp_factor", 0.15))


def graze_score(base: float, combo_mult: float, difficulty: float, cfg: Config) -> float:
    """擦弹得分 = 基础分 × 难度乘区 ×(1+(连击倍率−1)×权重)。出处 RewardScaling.GrazeScore。"""
    if not math.isfinite(base) or base <= 0.0:
        return 0.0
    mult = combo_mult if math.isfinite(combo_mult) and combo_mult >= 1.0 else 1.0
    weight = cfg.num("reward_scaling.graze_combo_weight", 1.0)
    weight = min(max(weight, 0.0), 1.0) if math.isfinite(weight) else 0.0
    value = base * linear_ramp(difficulty, cfg.num("reward_scaling.graze_difficulty_factor", 0.15))
    value *= 1.0 + (mult - 1.0) * weight
    return value if value > 0.0 else 0.0


def combo_mult(combo: int, cfg: Config) -> float:
    """连击倍率 = combo≤1 时 1，否则 min(1+(combo−1)×step, max_mult)。出处 ScoreService 连击乘区。"""
    if combo <= 1:
        return 1.0
    step = cfg.num("scoring.combo.step", 0.1)
    cap = cfg.num("scoring.combo.max_mult", 2.0)
    return min(1.0 + (combo - 1) * step, cap)


def milestone_threshold(index: int, cfg: Config, difficulty_mult: float = 1.0) -> int:
    """第 index 档里程碑阈值（8 档一循环、每循环按 cycle_mult 放大）。出处 MilestoneCurve.Threshold。"""
    base = cfg.get("milestones.base", [])
    if not isinstance(base, list) or not base:
        return 0
    values = [float(v) for v in base if is_num(v)]
    if not values:
        return 0
    n = len(values)
    cycle_mult = cfg.num("milestones.cycle_mult", 1.35)
    if not math.isfinite(cycle_mult) or cycle_mult <= 0.0:
        cycle_mult = 1.0
    diff = difficulty_mult if math.isfinite(difficulty_mult) else 1.0
    idx = max(index, 0)
    cycle, step = divmod(idx, n)
    if cycle_mult == 1.0:
        # 平坦曲线闭式求值：逐圈累加等价于 cycle×表末值 + 当前档（见 MilestoneCurve 的同款分支）
        return _to_int64((cycle * values[-1] + values[step]) * diff)
    total = 0.0
    for c in range(cycle + 1):
        mult = min(cycle_mult ** c, 1e15)
        if mult <= 0.0:
            break
        last = step if c == cycle else n - 1
        prev = 0.0
        for i in range(last + 1):
            total += (values[i] - prev) * mult
            prev = values[i]
        if total * diff >= 9.223372036854776e18:
            break
    return _to_int64(total * diff)


def _round_away(value: float) -> int:
    """GDScript roundf 语义：half away from zero。"""
    return int(math.floor(value + 0.5)) if value >= 0 else -int(math.floor(-value + 0.5))


def _to_int64(value: float) -> int:
    limit = 9.223372036854776e18
    if value >= limit:
        return int(limit)
    if value <= -limit:
        return -int(limit)
    return _round_away(value)


def augmented_fire_interval(cfg: Config, level: int) -> float:
    """射击间隔 = base_fire_interval × rapid_fire.factor^层级；冷却再取 max(0.01)。出处 Player 射击段。"""
    interval = cfg.num("player.base_fire_interval", 0.15)
    factor = cfg.num("augments.rapid_fire.factor", 0.75)
    return max(interval * (factor ** level), 0.01)


def augmented_bullet_damage(cfg: Config, level: int) -> int:
    """单发伤害 = max(1, round(bullet_damage × power_shot.factor^层级))。出处 Player 射击段。"""
    damage = cfg.num("player.bullet_damage", 10)
    factor = cfg.num("augments.power_shot.factor", 1.25)
    return max(1, int(math.floor(damage * (factor ** level) + 0.5)))


def effective_stack_cap(cfg: Config, aug_id: str) -> int:
    """软上限层级：talent.softcaps.<id>（缺省 cost.softcap），钳 [1, 该增幅 max_stacks]。出处 TalentService。"""
    hard = max(cfg.as_int(f"augments.{aug_id}.max_stacks", 1), 1)
    soft = cfg.get(f"talent.softcaps.{aug_id}")
    if not is_num(soft):
        soft = cfg.num("talent.cost.softcap", 3)
    return min(max(int(soft), 1), hard)


def player_dps(cfg: Config, levels: dict[str, int]) -> dict:
    """玩家理论 DPS 及其乘区拆解（不含命中率与走位损耗）。

    口径：单发伤害 × power_shot，弹幕数 × spread_shot，暴击期望 × crit_shot，
    齐射平均乘区 × salvo，全部除以射击间隔。出处 Player 开火段 + Bullet 暴击段。
    """
    interval = augmented_fire_interval(cfg, levels.get("rapid_fire", 0))
    damage = augmented_bullet_damage(cfg, levels.get("power_shot", 0))
    count = 1 + 2 * levels.get("spread_shot", 0)
    chance = min(cfg.num("augments.crit_shot.chance", 0.12) * levels.get("crit_shot", 0), 1.0)
    crit_factor = 1.0 + chance * (cfg.num("augments.crit_shot.multiplier", 2.0) - 1.0)
    salvo_level = levels.get("salvo", 0)
    if salvo_level > 0:
        every = max(int(cfg.num("augments.salvo.every_base", 7))
                    - int(cfg.num("augments.salvo.every_step", 2)) * salvo_level, 3)
        salvo_factor = ((every - 1) + cfg.num("augments.salvo.damage_mult", 3.0)) / every
    else:
        every, salvo_factor = 0, 1.0
    dps = count * damage * crit_factor * salvo_factor / interval
    return {"dps": dps, "interval": interval, "damage": damage, "count": count,
            "crit_factor": crit_factor, "salvo_factor": salvo_factor, "salvo_every": every}


def expected_incoming_damage(cfg: Config, raw_damage: float) -> float:
    """结算后承伤期望 = 伤害 × 护甲倍率 ×(1−闪避概率)，闪避判定在护甲之前。出处 DamageMitigation。"""
    armor = cfg.num("augments.armor.multiplier", 0.85)
    armor = min(max(armor, 0.0), 1.0)
    evasion = cfg.num("augments.evasion.chance", 0.2)
    evasion = min(max(evasion, 0.0), 1.0)
    return raw_damage * armor * (1.0 - evasion)


def talent_cost(cfg: Config, level: int) -> float:
    """第 level 级（0 起）的天赋成本 = base + level×increment。出处 TalentEconomy.CostForLevel。"""
    return cfg.num("talent.cost.base", 2.0) + level * cfg.num("talent.cost.increment", 1.0)


# ---------------------------------------------------------------- 体检


def check_ranges(balance: dict, expanded_meta: dict) -> list[dict]:
    """按 meta 登记的区间扫全表：越界即列出（不是拒绝保存的理由，是「会被代码钳制/回退」的提示）。"""
    warnings: list[dict] = []
    for path, entry in sorted(expanded_meta.items()):
        rng = entry.get("range")
        if not rng or len(rng) != 2:
            continue
        value: object = balance
        for part in path.split("."):
            value = value[part] if isinstance(value, dict) and part in value else None
            if value is None:
                break
        if not is_num(value):
            continue
        low, high = rng
        if low is not None and value < low:
            warnings.append({"path": path, "value": value, "range": rng, "why": f"低于下限 {low}"})
        elif high is not None and value > high:
            warnings.append({"path": path, "value": value, "range": rng, "why": f"高于上限 {high}"})
    return warnings


def check_structures(balance: dict) -> list[dict]:
    """结构性体检：那些「值本身合法、组合起来会让代码整表回退或行为退化」的形态。"""
    cfg = Config(balance)
    out: list[dict] = []

    def warn(path: str, why: str, source: str) -> None:
        out.append({"path": path, "why": why, "source": source})

    base = cfg.get("milestones.base", [])
    if not isinstance(base, list) or len(base) < 2:
        warn("milestones.base", "少于 2 档：里程碑曲线无法推进", SRC_MILESTONE_CURVE)
    elif any(not is_num(v) for v in base):
        warn("milestones.base", "含非数值元素：该元素被跳过", SRC_MILESTONE_CURVE)
    elif any(base[i] >= base[i + 1] for i in range(len(base) - 1)):
        warn("milestones.base", "非严格升序：里程碑阈值会回退", SRC_MILESTONE_CURVE)
    if cfg.num("milestones.cycle_mult", 1.35) < 1.0:
        warn("milestones.cycle_mult", "<1 会让里程碑推进在第二圈收窄甚至死循环", SRC_MILESTONE_CURVE)

    tiers = cfg.get("progression.tier_thresholds", [])
    if isinstance(tiers, list):
        nums = [float(v) for v in tiers if is_num(v)]
        if any(v <= 0 for v in nums):
            warn("progression.tier_thresholds", "含非正数：该元素被跳过", "csharp/godot/GameState.State.cs")
        elif any(nums[i] >= nums[i + 1] for i in range(len(nums) - 1)):
            warn("progression.tier_thresholds", "非严格升序：难度档位判定会跳档", "csharp/godot/GameState.State.cs")

    mults = cfg.get("boss.hp_mults", [])
    if not isinstance(mults, list) or len(mults) < 4 or any(not is_num(v) or v <= 0 for v in mults):
        warn("boss.hp_mults", "需 ≥4 个正数元素，否则整数组回退脚本默认（0/负 = Boss 出生免伤）", "csharp/godot/Boss.cs")

    types = cfg.get("enemies.types", [])
    unlocks = cfg.get("spawner.unlock_scores", [])
    if isinstance(types, list) and isinstance(unlocks, list) and len(types) != len(unlocks):
        warn("spawner.unlock_scores",
             f"元素数（{len(unlocks)}）与 enemies.types（{len(types)}）不一致：按下标对应，多出的类型永不入池",
             SRC_SPAWNER)

    for key in ("interval_mult", "speed_mult"):
        arr = cfg.get(f"boss.difficulty_scaling.{key}", [])
        if not isinstance(arr, list) or len(arr) < 3:
            warn(f"boss.difficulty_scaling.{key}", "需 ≥3 个元素（easy/medium/hard）", "csharp/godot/Boss.cs")
    counts = cfg.get("boss.difficulty_scaling.counts", {})
    if isinstance(counts, dict):
        for name, arr in counts.items():
            if not isinstance(arr, list) or len(arr) < 3:
                warn(f"boss.difficulty_scaling.counts.{name}", "需 ≥3 个元素（easy/medium/hard）", "csharp/godot/Boss.cs")

    defs = [cfg.section(f"difficulty.{tier}") for tier in ("easy", "medium", "hard")]
    needed = {"hp", "speed", "spawn", "score", "spread_cap", "milestone", "regen_delay", "regen_rate"}
    for tier_name, body in zip(("easy", "medium", "hard"), defs):
        missing = needed - set(body)
        if missing:
            warn(f"difficulty.{tier_name}", f"缺键 {'/'.join(sorted(missing))}：整表回退脚本默认",
                 "csharp/godot/GameState.State.cs")

    if cfg.num("dda.factor", 1.3) < 1.0:
        warn("dda.factor", "<1 会缩短受击后的开火间隔（方向相反）", "csharp/godot/GameState.State.cs")

    weights = cfg.section("fog_events.weights")
    if weights and all(is_num(v) and v <= 0 for v in weights.values()):
        warn("fog_events.weights", "全为 0：抽取退化为均匀分布", "csharp/godot/GameEventManager.cs")

    for idx, entry in enumerate(types if isinstance(types, list) else []):
        if not isinstance(entry, dict):
            warn(f"enemies.types[{idx}]", "不是对象：整键回退默认", SRC_SPAWNER)
            continue
        for key in ("hp", "speed"):
            arr = entry.get(key)
            if not isinstance(arr, list) or len(arr) < 2 or any(not is_num(v) for v in arr):
                warn(f"enemies.types[{idx}].{key}", "需 ≥2 个数值元素：否则整键回退默认", SRC_SPAWNER)

    if not (cfg.num("spawner.boss_time_limit", 120) >= 5):
        warn("spawner.boss_time_limit", "<5 秒会被钳到 5", SRC_SPAWNER)
    return out


# ---------------------------------------------------------------- meta


def load_meta(path: Path = META_PATH) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def parse_template(template: str) -> list[tuple[str, str]]:
    """路径模板 → 操作序列。写法：`key` 精确键、`[]` 数组逐元素、`*` 任意一层键、`前缀*` 前缀匹配。

    前缀匹配是必需的：`boss.phases.type*.p1[]` 里的 type1..type4、`mothership.early_*` 这类
    一族键共用同一条说明，写成四条只是把同一句话抄四遍。
    """
    ops: list[tuple[str, str]] = []
    for token in template.split("."):
        if token.endswith("[]"):
            name = token[:-2]
            if name:
                ops.append(("key", name))
            ops.append(("index", ""))
        elif token == "*":
            ops.append(("any", ""))
        elif token.endswith("*"):
            ops.append(("prefix", token[:-1]))
        else:
            ops.append(("key", token))
    return ops


def _resolve(node: object, key: str) -> object:
    if isinstance(node, dict):
        return node.get(key)
    if isinstance(node, list) and key.isdigit():
        index = int(key)
        return node[index] if index < len(node) else None
    return None


def expand_paths(template: str, tree: object) -> list[str]:
    """把路径模板展开成树里真实存在的具体路径。

    展开而不是让前端自己匹配，是要让「meta 里写了但 balance 里没有」在测试里可判定（孤儿条目）。
    """
    paths: list[list[str]] = [[]]
    for op, arg in parse_template(template):
        nxt: list[list[str]] = []
        for prefix in paths:
            node: object = tree
            for key in prefix:
                node = _resolve(node, key)
                if node is None:
                    break
            if op == "index":
                if isinstance(node, list):
                    nxt.extend(prefix + [str(i)] for i in range(len(node)))
            elif op == "any":
                if isinstance(node, dict):
                    nxt.extend(prefix + [key] for key in node)
            elif op == "prefix":
                if isinstance(node, dict):
                    nxt.extend(prefix + [key] for key in node if key.startswith(arg))
            elif isinstance(node, dict) and arg in node:
                nxt.append(prefix + [arg])
        paths = nxt
        if not paths:
            break
    return [".".join(p) for p in paths]


def read_path(tree: object, path: str) -> object:
    """按展开后的点路径取值（数字段走下标记）；任一段不存在返回 None。"""
    node = tree
    for part in path.split("."):
        if isinstance(node, dict) and part in node:
            node = node[part]
        elif isinstance(node, list) and part.isdigit() and int(part) < len(node):
            node = node[int(part)]
        else:
            return None
    return node


# ---------------------------------------------------------------- 预设

# 预设操作 → 目标值的适用性：数值键吃 scale/add/set（set 须给数值），布尔键只吃 set（且须给布尔）。
# 「为什么是相对变换」见 balance_presets.json 的 _about：写死绝对值就等于第二份数值单源。


def load_presets(path: Path = PRESETS_PATH) -> list[dict]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    presets = payload.get("presets", [])
    return presets if isinstance(presets, list) else []


def _op_value(value: object, op: dict) -> tuple[bool, object]:
    """返回 (是否适用, 新值)。不适用的一律跳过而不是报错：预设要能对着任意当前值跑。"""
    is_bool = isinstance(value, bool)
    if is_bool:
        if "set" in op and isinstance(op["set"], bool):
            return True, op["set"]
        return False, None
    if not is_num(value):
        return False, None
    if "scale" in op and is_num(op["scale"]):
        return True, value * op["scale"]
    if "add" in op and is_num(op["add"]):
        return True, value + op["add"]
    if "set" in op and is_num(op["set"]):
        return True, op["set"]
    return False, None


def plan_preset(preset: dict, balance: dict) -> dict:
    """把预设展开成变更清单（不改动任何东西）。

    返回 {changes: [{path, op, from, to}], skipped: [{path, why}]}。
    skipped 要显式带出来而不是静默丢弃：路径写错、键被改名、类型对不上时，
    界面上「点了没反应」是最难查的一类故障。
    """
    changes: list[dict] = []
    skipped: list[dict] = []
    for op in preset.get("ops", []):
        for template in op.get("paths", []):
            paths = expand_paths(template, balance)
            if not paths:
                skipped.append({"path": template, "why": "路径在数值表里不存在"})
                continue
            for path in paths:
                current = read_path(balance, path)
                ok, new = _op_value(current, op)
                if not ok:
                    skipped.append({"path": path, "why": f"当前值 {current!r} 不适用该操作"})
                    continue
                if new != current:
                    changes.append({"path": path, "op": next(iter(op)), "from": current, "to": new})
    return {"changes": changes, "skipped": skipped}


def apply_preset(preset: dict, balance: dict) -> tuple[dict, list[dict]]:
    """返回 (改好的新树, 变更清单)；输入不被修改（调用方可能还要拿它做撤销）。"""
    plan = plan_preset(preset, balance)
    out = json.loads(json.dumps(balance))
    for item in plan["changes"]:
        parts = item["path"].split(".")
        node = out
        for part in parts[:-1]:
            node = node[int(part)] if isinstance(node, list) else node[part]
        last = parts[-1]
        if isinstance(node, list):
            node[int(last)] = item["to"]
        else:
            node[last] = item["to"]
    return out, plan["changes"]


def expand_meta(meta: dict, balance: dict) -> dict:
    """{具体路径: 条目}；模板展开失败（树里不存在）时保留原模板键，供体检发现孤儿。"""
    out: dict[str, dict] = {}
    for template, entry in meta.get("keys", {}).items():
        concrete = expand_paths(template, balance)
        if not concrete:
            out[template] = dict(entry, orphan=True)
            continue
        for path in concrete:
            out[path] = dict(entry, template=template)
    return out


# ---------------------------------------------------------------- 报告


def _fmt(value: float, digits: int = 2) -> str:
    if value is None:
        return "—"
    text = f"{value:,.{digits}f}"
    return text


def build_report(balance: dict, meta: dict | None = None) -> dict:
    """生成分析面板数据：每个块自带出处；不产出结论性判断，只呈现代数与事实。"""
    cfg = Config(balance)
    meta = meta if meta is not None else load_meta()

    # 时间线：D 随本局时长（无 Boss 击杀 = 纯时间线口径）；终点取到软上限起点之后，
    # 否则 tail_speed_factor 与 soft_cap_start 的效果在图上完全看不出来（默认参数下 6.0 出现在第 40 分钟）
    times = list(range(0, 2701, 60))
    curve = [round(difficulty_at(t, 0, cfg), 4) for t in times]
    marks = []
    for target in (1.5, 2.0, 3.0, 4.0, 6.0, 8.0):
        hit = next((t for t, d in zip(times, curve) if d >= target - 1e-9), None)
        if hit is not None:
            marks.append({"d": target, "seconds": hit})
    boss_marks = [{"boss_kills": k, "d": round(difficulty_at(0, k, cfg), 2)} for k in (1, 2, 4, 6, 10)]

    diff_rows = []
    for d in (1.0, 2.0, 3.0, 5.0, 8.0, 12.0):
        diff_rows.append([
            _fmt(d, 1),
            _fmt(linear_ramp(d, cfg.num("enemies.hp_ramp_factor", 0.4))),
            _fmt(linear_ramp(d, cfg.num("enemies.damage_ramp_factor", 0.2))),
            _fmt(enemy_speed_ramp(d, cfg)),
            _fmt(fire_interval(2.2, d, cfg)),
            _fmt(wave_interval(4.0, d, cfg)),
            str(elite_count(d, cfg)),
            str(boss_density_bonus(d, cfg)),
        ])

    # 敌人血量：区间均值 × 难度血量乘区 × 难度档
    enemy_rows = []
    enemy_types = cfg.get("enemies.types", [])
    if isinstance(enemy_types, list):
        for idx, entry in enumerate(enemy_types):
            if not isinstance(entry, dict) or not isinstance(entry.get("hp"), list):
                continue
            hp = [float(v) for v in entry["hp"] if is_num(v)]
            if not hp:
                continue
            avg = sum(hp) / len(hp)
            enemy_rows.append([f"敌机 #{idx + 1}", _fmt(min(hp), 0), _fmt(max(hp), 0)] + [
                _fmt(avg * linear_ramp(d, cfg.num("enemies.hp_ramp_factor", 0.4)) * cfg.num("difficulty.hard.hp", 1.5), 0)
                for d in (1.0, 3.0, 6.0)])

    boss_rows = []
    mults = cfg.get("boss.hp_mults", [])
    if isinstance(mults, list):
        ramp = cfg.num("boss.hp_ramp_factor", 0.55)
        base = cfg.num("boss.hp_base", 800)
        for idx, mult in enumerate(mults):
            if not is_num(mult):
                continue
            boss_rows.append([f"Boss #{idx + 1}"] + [
                _fmt(base * float(mult) * linear_ramp(d, ramp) * cfg.num("difficulty.hard.hp", 1.5), 0)
                for d in (1.0, 3.0, 6.0)])

    # 战斗节奏：裸机 / 软上限 / 满层三档
    def levels_at(cap: str) -> dict[str, int]:
        ids = ("power_shot", "rapid_fire", "spread_shot", "crit_shot", "salvo")
        if cap == "none":
            return {i: 0 for i in ids}
        if cap == "soft":
            return {i: effective_stack_cap(cfg, i) for i in ids}
        return {i: max(cfg.as_int(f"augments.{i}.max_stacks", 1), 1) for i in ids}

    combat_rows = []
    for label, cap in (("裸机", "none"), ("软上限满点", "soft"), ("硬上限满点", "hard")):
        stats = player_dps(cfg, levels_at(cap))
        combat_rows.append([
            label, _fmt(stats["interval"], 3), str(stats["damage"]), str(stats["count"]),
            _fmt(stats["crit_factor"], 3),
            _fmt(stats["salvo_factor"], 3),
            _fmt(stats["dps"], 0),
        ])

    ttk_rows = []
    if isinstance(enemy_types, list):
        soft_dps = player_dps(cfg, levels_at("soft"))["dps"]
        none_dps = player_dps(cfg, levels_at("none"))["dps"]
        for idx, entry in enumerate(enemy_types):
            if not isinstance(entry, dict) or not isinstance(entry.get("hp"), list):
                continue
            hp = [float(v) for v in entry["hp"] if is_num(v)]
            if not hp:
                continue
            avg = sum(hp) / len(hp)
            ttk_rows.append([f"敌机 #{idx + 1}"] + [
                f"{_fmt(avg * linear_ramp(d, cfg.num('enemies.hp_ramp_factor', 0.4)) * cfg.num('difficulty.hard.hp', 1.5) / dps, 2)}s"
                for d, dps in ((1.0, none_dps), (3.0, soft_dps), (6.0, soft_dps))])

    # 生存：裸机 / 带护甲闪避
    player_hp = cfg.num("player.max_health", 100)
    survival_rows = []
    for name, raw in (("单发弹", cfg.num("enemies.bullet_damage.single", 12)),
                      ("散射弹", cfg.num("enemies.bullet_damage.spread", 10)),
                      ("激光弹", cfg.num("enemies.bullet_damage.laser", 20)),
                      ("撞体", cfg.num("enemies.collision_damage", 20)),
                      ("Boss 撞体", cfg.num("boss.collision_damage", 30))):
        after = expected_incoming_damage(cfg, raw)
        survival_rows.append([name, _fmt(raw, 0), _fmt(raw * linear_ramp(6.0, cfg.num("enemies.damage_ramp_factor", 0.2)), 1),
                              _fmt(player_hp / max(after, 0.01), 1)])

    # 经济与进度
    ms_rows = []
    for idx in range(0, min(len(cfg.get("milestones.base", [])) + 3, 12)):
        ms_rows.append([str(idx + 1),
                        _fmt(milestone_threshold(idx, cfg, cfg.num("difficulty.medium.milestone", 1.0)), 0),
                        _fmt(milestone_threshold(idx, cfg, cfg.num("difficulty.hard.milestone", 1.5)), 0),
                        _fmt(milestone_threshold(idx, cfg, 1.0), 0)])

    score_rows = []
    for combo in (1, 5, 10, 20):
        mult = combo_mult(combo, cfg)
        for name, base in (("敌机 #1", cfg.num("enemies.types[0].score", 100)),
                           ("精英机 #1", cfg.num("elites.types[0].score", 400)),
                           ("炮塔事件奖励", cfg.num("elite_turret_event.reward_score", 900))):
            score_rows.append([name, str(combo), _fmt(mult, 2),
                               _fmt(base * mult * kill_score_factor(6.0, cfg), 0)])

    # 天赋点数累计：里程碑点 + 等量 Boss 点；「可满点节点数」按把节点点到软上限的成本折算
    soft = max(cfg.as_int("talent.cost.softcap", 3), 1)
    full_node_cost = sum(talent_cost(cfg, level) for level in range(soft))
    talent_points = []
    acc = 0.0
    for idx in range(1, 9):
        acc += cfg.num("talent.grant.points_per_milestone", 2.0)
        total = acc + idx * cfg.num("talent.grant.points_per_boss", 1.0)
        talent_points.append([str(idx), _fmt(acc, 0), _fmt(total, 0),
                              _fmt(full_node_cost, 1), str(int(total // full_node_cost)) if full_node_cost > 0 else "—"])

    # 增幅每层收益
    aug_rows = []
    ps = cfg.num("augments.power_shot.factor", 1.25)
    rf = cfg.num("augments.rapid_fire.factor", 0.75)
    aug_rows.append(["火力", _fmt(ps, 3), f"每层 ×{_fmt(ps, 3)}", f"软上限 {effective_stack_cap(cfg, 'power_shot')}"])
    aug_rows.append(["速射", _fmt(rf, 3), f"每层射击间隔 ×{_fmt(rf, 3)}（DPS ÷{_fmt(rf, 3)}）",
                     f"软上限 {effective_stack_cap(cfg, 'rapid_fire')}"])
    aug_rows.append(["散射", "—", "每层 +2 发", f"软上限 {effective_stack_cap(cfg, 'spread_shot')}"])
    crit = cfg.num("augments.crit_shot.chance", 0.12) * (cfg.num("augments.crit_shot.multiplier", 2.0) - 1.0)
    aug_rows.append(["暴击", _fmt(1.0 + crit, 3), f"每层期望 ×{_fmt(1.0 + crit, 3)}", f"软上限 {effective_stack_cap(cfg, 'crit_shot')}"])
    aug_rows.append(["护甲", _fmt(cfg.num("augments.armor.multiplier", 0.85), 3),
                     f"等效生命 ÷{_fmt(cfg.num('augments.armor.multiplier', 0.85), 3)}", "1 层"])
    ev = cfg.num("augments.evasion.chance", 0.2)
    aug_rows.append(["闪避", _fmt(ev, 3), f"等效生命 ÷{_fmt(1.0 - ev, 3)}", "1 层"])
    bonus = cfg.num("augments.extra_life.max_hp_bonus", 50)
    aug_rows.append(["额外生命", _fmt(bonus, 0), f"每层 +{_fmt(bonus, 0)} 生命", f"{cfg.as_int('augments.extra_life.max_stacks', 10)} 层"])
    aug_rows.append(["回血", _fmt(cfg.num("augments.regen.heal_per_sec", 2.0), 2), "每秒回血", "1 层"])
    aug_rows.append(["得分增幅", _fmt(cfg.num("augments.score_amp.factor", 1.08), 3), "每层 ×1.08（服务侧取 max(factor,1)）",
                     f"软上限 {effective_stack_cap(cfg, 'score_amp')}"])
    graze = cfg.num("player.graze_score", 30)
    aug_rows.append(["擦弹场", _fmt(cfg.num("augments.graze_field.score_per_level", 5), 0),
                     f"每层擦弹分 +{_fmt(cfg.num('augments.graze_field.score_per_level', 5), 0)}（基础 {_fmt(graze, 0)}）",
                     f"软上限 {effective_stack_cap(cfg, 'graze_field')}"])
    laser_duty = cfg.num("augments.laser_beam.duration", 3.0) / max(
        cfg.num("augments.laser_beam.duration", 3.0) + cfg.num("augments.laser_beam.cooldown", 8.0), 1e-6)
    aug_rows.append(["激光", _fmt(laser_duty, 3),
                     f"占空比 {_fmt(laser_duty, 3)}，期间 {_fmt(cfg.num('augments.laser_beam.tick_damage', 16), 0)}/"
                     f"{_fmt(cfg.num('augments.laser_beam.tick_interval', 0.1), 2)}s", "1 层"])
    ms_dmg = cfg.num("mothership.gatling.damage", 8) * cfg.num("mothership.upgrade.damage_mult", 1.5)
    ms_interval = cfg.num("mothership.gatling.interval", 0.1333) * cfg.num("mothership.upgrade.interval_mult", 0.8)
    aug_rows.append(["母舰加特林", _fmt(cfg.num("mothership.gatling.damage", 8) / cfg.num("mothership.gatling.interval", 0.1333), 1),
                     f"基础 DPS；升级后 {_fmt(ms_dmg / ms_interval, 1)}", f"里程碑 {cfg.as_int('mothership.upgrade.threshold', 5)} 起"])

    report = {
        "warnings": check_ranges(balance, expand_meta(meta, balance)) + check_structures(balance),
        "sections": [
            {
                "id": "difficulty", "title": "难度与时间线",
                "note": "D = 1 + per_boss_kill×Boss击杀 + 时间项；时间项按 30s 步长量化并受软上限折减。",
                "source": f"{SRC_DIFFICULTY_CURVE} / {SRC_DIFFICULTY_SCALING}",
                "series": {"title": "D 随本局时长（无 Boss 击杀）", "x": times, "y": curve,
                           "x_label": "秒", "y_label": "D"},
                "metrics": [
                    {"label": "D 达到 2.0", "value": next((f"{m['seconds']}s" for m in marks if m["d"] == 2.0), "未达到")},
                    {"label": "D 达到 3.0", "value": next((f"{m['seconds']}s" for m in marks if m["d"] == 3.0), "未达到")},
                    {"label": "D 达到 6.0（软上限起点）", "value": next((f"{m['seconds']}s" for m in marks if m["d"] == 6.0), "未达到")},
                    {"label": "软上限后斜率保留", "value": _fmt(cfg.num("progression.tail_speed_factor", 0.5), 2)},
                    {"label": "Boss 项（每个 Boss）", "value": f"+{_fmt(cfg.num('progression.per_boss_kill', 0.6), 2)} D"},
                    {"label": "满配 Boss 击杀 10 次时 D", "value": _fmt(difficulty_at(0, 10, cfg), 2)},
                ],
                "table": {
                    "title": "各 D 下的敌方量（血量乘区按杂兵口径；开火间隔基准 2.2s、波间隔基准 4.0s）",
                    "head": ["D", "HP 乘区", "伤害乘区", "速度乘区", "开火间隔", "波间隔", "精英数", "Boss 密度"],
                    "rows": diff_rows,
                },
            },
            {
                "id": "enemy", "title": "敌人血量",
                "note": "按区间均值换算，均取最高难度档（hard）的 hp 倍率。",
                "source": "csharp/godot/Enemy.cs / csharp/godot/Boss.cs",
                "table": {"title": "敌机血量（均值）", "head": ["类型", "区间下限", "区间上限", "D=1", "D=3", "D=6"],
                          "rows": enemy_rows},
                "table2": {"title": "Boss 血量", "head": ["类型", "D=1", "D=3", "D=6"], "rows": boss_rows},
            },
            {
                "id": "combat", "title": "战斗节奏",
                "note": "理论 DPS 不含命中率与走位损耗，只作量级对照；软上限满点取 talent.softcaps 的层级。",
                "source": SRC_PLAYER_FIRE,
                "table": {"title": "玩家输出（三档配置）",
                          "head": ["配置", "射击间隔", "单发伤害", "弹幕数", "暴击期望", "齐射乘区", "DPS"],
                          "rows": combat_rows},
                "table2": {"title": "击杀用时（早期＝裸机 D=1；中期／后期＝软上限满点 D=3、D=6）",
                           "head": ["类型", "早期", "中期", "后期"], "rows": ttk_rows},
                "table3": {"title": "生存容错（可承受的命中数）",
                           "head": ["来源", "基础伤害", "D=6 伤害", "容错发数（含护甲闪避）"], "rows": survival_rows},
                "source2": SRC_DAMAGE_MITIGATION,
            },
            {
                "id": "economy", "title": "经济与进度",
                "note": "里程碑阈值按 cycle_mult 循环放大；得分 = 基础分 × 连击倍率 × D=6 击杀缩放 × 难度档 score 倍率。",
                "source": f"{SRC_MILESTONE_CURVE} / {SRC_REWARD_SCALING}",
                "table": {"title": "里程碑阈值（medium / hard / 无难度倍率）",
                          "head": ["档", "medium", "hard", "基准"], "rows": ms_rows},
                "table2": {"title": "单次击杀得分（D=6，未计难度档 score 倍率）",
                           "head": ["来源", "连击", "连击倍率", "得分"], "rows": score_rows},
                "table3": {"title": "天赋点数累计（里程碑点数 + 等量 Boss 点数）",
                           "head": ["达成里程碑数", "里程碑点数", "含同等 Boss 数", "满点单节点成本", "可满点节点数"],
                           "rows": talent_points},
            },
            {
                "id": "augments", "title": "增幅每层收益",
                "note": "软上限来自 talent.softcaps；超出后按 talent.diminishing 递减，此处只列软上限内的名义收益。",
                "source": f"{SRC_TALENT_ECONOMY} / {SRC_PLAYER_FIRE}",
                "table": {"title": "增幅单层收益", "head": ["增幅", "单层倍率", "说明", "层级上限"], "rows": aug_rows},
            },
        ],
    }
    return report


def main() -> None:
    ap = argparse.ArgumentParser(description="InfiAir 平衡分析（离线只读）")
    ap.add_argument("--json", action="store_true", help="输出原始 JSON 而非摘要")
    args = ap.parse_args()
    balance = json.loads(BALANCE_PATH.read_text(encoding="utf-8"))
    report = build_report(balance)
    if args.json:
        json.dump(report, sys.stdout, ensure_ascii=False, indent="\t")
        print()
        return
    for section in report["sections"]:
        print(f"== {section['title']} ==")
        for metric in section.get("metrics", []):
            print(f"  {metric['label']}: {metric['value']}")
        for key in ("table", "table2", "table3"):
            table = section.get(key)
            if not table:
                continue
            print(f"  -- {table['title']}")
            print("     " + " | ".join(table["head"]))
            for row in table["rows"]:
                print("     " + " | ".join(row))
    print(f"== 体检：{len(report['warnings'])} 条提示 ==")
    for item in report["warnings"][:20]:
        print(f"  {item['path']}: {item['why']}")


if __name__ == "__main__":
    main()
