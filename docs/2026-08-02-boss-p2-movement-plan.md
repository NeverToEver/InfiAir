# Boss P2 阶段走位升级 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 BOSS_REDESIGN §5 的 P2 阶段走位升级（D05）：一型 P2 strafe 200 + 纵向正弦往复、二型 P2 冲刺 0.4s/0.5s、三型 P1 锚线下 200–280px 区间呼吸、三型 P2 strafe 100 + 纵向正弦往复。

**Architecture:** 走位逻辑在 `BossMovement`（RefCounted 策略类，经 `boss._movement.update(delta, self)` 每帧调用）。纵向分量复用项目既有模式：`Enemy.sin_fast()` 查表正弦（同 `EnemyMoveStrategy.SineMove`）+ `_update_press` 的增量式 y 施加（保存偏移、每帧加差值，不覆盖逃跑上飘）。新配置入 `balance.json boss.movement` 段，`Boss._ready` 缓存为公开实例字段（同 `STRAFE_SPEEDS` 模式，`_movement` 经 `boss.XXX` 读取）。

**Tech Stack:** Godot 4.6 + GDScript；测试为 `test/*.tscn` 无头场景自检（`[PASS]`/`[FAIL]` + 退出码）。

## Global Constraints

- 可调数值只改 `data/balance.json`；脚本同名默认值用于缺键/损坏回退，两处保持一致（AGENTS.md）。
- 新增数值键后跑 `python3 scripts/tools/gen_balance_map.py` 刷新 `docs/BALANCE_MAP.md`。
- 走位坐标为**游戏性范围族**（基于 `fight_anchor_y()` / `strafe_range()` view 基线），不乘 `world_scale`。
- 速度全部经 `slow_factor()` / `_enrage_speed_mult()` 乘区（与既有走位一致）。
- 改动仅影响 P1/P2 分支；**ENRAGE 阶段走位保持现状**（狂暴轨道由 `enrage_sequence` 接管，不得干扰）。
- 测试数值断言用实例常量/配置读值（C34 口径），不硬编码。
- 不新建走位原语、不加第三方依赖；热路径无分配（正弦用 `Enemy.sin_fast`）。

---

### Task 1: 配置键与 Boss 字段

**Files:**
- Modify: `data/balance.json`（`boss` 段新增 `movement` 子段）
- Modify: `scripts/boss.gd`（新增 11 个公开实例字段 + `_ready` 缓存）

**Interfaces:**
- Produces: `Boss.TYPE1_P2_STRAFE: int`、`Boss.TYPE1_P2_BOB_AMP: float`、`Boss.TYPE1_P2_BOB_PERIOD: float`、`Boss.TYPE2_P2_DASH_TIME: float`、`Boss.TYPE2_P2_REST_TIME: float`、`Boss.TYPE3_P1_BOB_MIN: float`、`Boss.TYPE3_P1_BOB_MAX: float`、`Boss.TYPE3_P1_BOB_PERIOD: float`、`Boss.TYPE3_P2_STRAFE: int`、`Boss.TYPE3_P2_BOB_AMP: float`、`Boss.TYPE3_P2_BOB_PERIOD: float`（均为公开实例字段，`boss_movement.gd` 经 `boss.XXX` 读取，同 `STRAFE_SPEEDS` 模式）

- [ ] **Step 1: balance.json 新增 `boss.movement` 段**

在 `data/balance.json` 的 `boss` 段 `phases` 键之后新增：

```json
	"movement": {
		"type1_p2_strafe": 200,
		"type1_p2_bob_amp": 40,
		"type1_p2_bob_period": 6,
		"type2_p2_dash_time": 0.4,
		"type2_p2_rest_time": 0.5,
		"type3_p1_bob_min": 200,
		"type3_p1_bob_max": 280,
		"type3_p1_bob_period": 9,
		"type3_p2_strafe": 100,
		"type3_p2_bob_amp": 50,
		"type3_p2_bob_period": 8
	}
```

- [ ] **Step 2: boss.gd 新增公开字段（默认值 = json 同值）**

在 `scripts/boss.gd` 的 `PRESS_DEPTH`（约 :102）声明之后追加：

```gdscript
## D05 P2 走位（balance.json boss.movement，公开字段供 BossMovement 读取）
var TYPE1_P2_STRAFE := 200  # 一型 P2 strafe 速度（P1 = STRAFE_SPEEDS[0] 150）
var TYPE1_P2_BOB_AMP := 40.0  # 一型 P2 纵向正弦幅度（±px，围绕锚线）
var TYPE1_P2_BOB_PERIOD := 6.0  # 一型 P2 纵向正弦周期（s）
var TYPE2_P2_DASH_TIME := 0.4  # 二型 P2 冲刺持续（P1 = 0.5）
var TYPE2_P2_REST_TIME := 0.5  # 二型 P2 冲刺休息（P1 = 0.7）
var TYPE3_P1_BOB_MIN := 200.0  # 三型 P1 纵向呼吸下界（锚线下 px）
var TYPE3_P1_BOB_MAX := 280.0  # 三型 P1 纵向呼吸上界（锚线下 px）
var TYPE3_P1_BOB_PERIOD := 9.0  # 三型 P1 纵向呼吸周期（s，与模式循环错开）
var TYPE3_P2_STRAFE := 100  # 三型 P2 strafe 速度（P1 = STRAFE_SPEEDS[2] 60）
var TYPE3_P2_BOB_AMP := 50.0  # 三型 P2 纵向正弦幅度（±px，围绕锚线）
var TYPE3_P2_BOB_PERIOD := 8.0  # 三型 P2 纵向正弦周期（s）
```

- [ ] **Step 3: boss.gd `_ready` 缓存配置**

在 `scripts/boss.gd` `_ready` 中 `PRESS_DEPTH` 缓存行（约 :447）之后追加：

```gdscript
	TYPE1_P2_STRAFE = int(GameState.cfg("boss.movement.type1_p2_strafe", TYPE1_P2_STRAFE))
	TYPE1_P2_BOB_AMP = float(GameState.cfg("boss.movement.type1_p2_bob_amp", TYPE1_P2_BOB_AMP))
	TYPE1_P2_BOB_PERIOD = float(GameState.cfg("boss.movement.type1_p2_bob_period", TYPE1_P2_BOB_PERIOD))
	TYPE2_P2_DASH_TIME = float(GameState.cfg("boss.movement.type2_p2_dash_time", TYPE2_P2_DASH_TIME))
	TYPE2_P2_REST_TIME = float(GameState.cfg("boss.movement.type2_p2_rest_time", TYPE2_P2_REST_TIME))
	TYPE3_P1_BOB_MIN = float(GameState.cfg("boss.movement.type3_p1_bob_min", TYPE3_P1_BOB_MIN))
	TYPE3_P1_BOB_MAX = float(GameState.cfg("boss.movement.type3_p1_bob_max", TYPE3_P1_BOB_MAX))
	TYPE3_P1_BOB_PERIOD = float(GameState.cfg("boss.movement.type3_p1_bob_period", TYPE3_P1_BOB_PERIOD))
	TYPE3_P2_STRAFE = int(GameState.cfg("boss.movement.type3_p2_strafe", TYPE3_P2_STRAFE))
	TYPE3_P2_BOB_AMP = float(GameState.cfg("boss.movement.type3_p2_bob_amp", TYPE3_P2_BOB_AMP))
	TYPE3_P2_BOB_PERIOD = float(GameState.cfg("boss.movement.type3_p2_bob_period", TYPE3_P2_BOB_PERIOD))
```

- [ ] **Step 4: 验证解析**

Run: `godot --headless --import --path .`
Expected: exit 0，无脚本解析错误。

- [ ] **Step 5: Commit**

```bash
git add data/balance.json scripts/boss.gd
git commit -m "feat: Boss P2 走位配置键——boss.movement 段 11 键（strafe/bob/band/dash 节奏）（2026-08-02）"
```

---

### Task 2: 写失败测试（P2 走位行为断言）

**Files:**
- Modify: `test/boss_phase_test.gd`（场景 1 扩展 P2 走位断言；场景 3 扩展 P1 区间呼吸断言）

**Interfaces:**
- Consumes: Task 1 的 `Boss.TYPE1_P2_BOB_AMP` 等 11 个字段
- Produces: 走位存在性/幅度断言（C34 口径：读实例常量，不硬编码）

- [ ] **Step 1: 场景 1（一型）P2 段追加走位采样断言**

在 `test/boss_phase_test.gd` 的 C11「P2 段切换后机身回到战斗锚线」断言之后、`P2→ENRAGE` 的 take_damage 之前，插入：

```gdscript
	# D05：P2 走位——strafe 提速 200 + 纵向正弦往复（采样 1s 物理帧）
	# C11 断言已保证切换瞬间回锚线（sin 0 = 0 无跳变）；此处验证 bob 摆动存在且幅度受控
	var y_min := INF
	var y_max := -INF
	var x_min := INF
	var x_max := -INF
	for i in 60:
		await get_tree().physics_frame
		if not is_instance_valid(boss):
			break
		y_min = minf(y_min, boss.position.y)
		y_max = maxf(y_max, boss.position.y)
		x_min = minf(x_min, boss.position.x)
		x_max = maxf(x_max, boss.position.x)
	var anchor_y: float = boss.fight_anchor_y()
	var amp: float = boss.TYPE1_P2_BOB_AMP
	_check(
		y_max - y_min > 20.0,
		"场景1：P2 纵向正弦往复（采样期 y 波动 ≥20px，实测 %.1f）" % (y_max - y_min)
	)
	_check(
		y_max <= anchor_y + amp + 4.0 and y_min >= anchor_y - amp - 4.0,
		"场景1：P2 纵向振幅在 ±amp 内（amp=%.0f）" % amp
	)
	_check(
		x_max - x_min > 30.0,
		"场景1：P2 横向 strafe 持续移动（采样期 x 位移 %.1fpx）" % (x_max - x_min)
	)
```

同时把 C11 断言（约 :116-121）的容差注释更新为允许 bob 摆动（实现后 sin 0 = 0 仍回锚线，断言本身不变）：

```gdscript
	# C11：段切换归零纵向下压偏移——若切换恰在下压窗口内，机身不得残留 80px 级偏移。
	# D05：P2 起加入纵向正弦（sin 0 = 0，切换瞬间仍回锚线）；容差 4px 兼容相位未推进时的逼近残差。
```

- [ ] **Step 2: 场景 3（三型）P1 段追加区间呼吸断言**

在 `test/boss_phase_test.gd` 场景 3 的 P1 模式循环断言之后（三型仍在 P1 且未打到 P2 时）插入：

```gdscript
	# D05：三型 P1 纵向区间呼吸——机身 y 在锚线下 [200, 280] 区间正弦（采样 0.5s）
	var t3_y_min := INF
	var t3_y_max := -INF
	for i in 30:
		await get_tree().physics_frame
		if not is_instance_valid(boss3):
			break
		t3_y_min = minf(t3_y_min, boss3.position.y)
		t3_y_max = maxf(t3_y_max, boss3.position.y)
	var t3_anchor: float = boss3.fight_anchor_y()
	_check(
		t3_y_max - t3_y_min > 15.0,
		"场景3：P1 纵向区间呼吸（采样期 y 波动 ≥15px，实测 %.1f）" % (t3_y_max - t3_y_min)
	)
	_check(
		t3_y_min >= t3_anchor + boss3.TYPE3_P1_BOB_MIN - 6.0 and t3_y_max <= t3_anchor + boss3.TYPE3_P1_BOB_MAX + 6.0,
		"场景3：P1 纵向区间在锚线下 [min, max] 内（min=%.0f max=%.0f）" % [boss3.TYPE3_P1_BOB_MIN, boss3.TYPE3_P1_BOB_MAX]
	)
```

（若场景 3 现有代码无 `boss3` 变量名，按该场景现有变量命名调整；三型须处于 P1 阶段且模式在播。）

- [ ] **Step 3: 运行测试确认失败（TDD 红）**

Run: `godot --headless --path . res://test/boss_phase_test.tscn`
Expected: 新增的「P2 纵向正弦往复」「P2 纵向振幅」「P1 纵向区间呼吸」断言 FAIL（走位未实现，y 恒定锚线）；既有断言全 PASS。

- [ ] **Step 4: Commit**

```bash
git add test/boss_phase_test.gd
git commit -m "test: Boss P2 走位断言——一型 P2 正弦往复/振幅、三型 P1 区间呼吸（TDD 红）（2026-08-02）"
```

---

### Task 3: 实现走位（boss_movement.gd）

**Files:**
- Modify: `scripts/boss_movement.gd`

**Interfaces:**
- Consumes: Task 1 的 11 个 `Boss.TYPE*` 字段、`boss.fight_phase()`、`boss.fight_anchor_y()`、`boss.slow_factor()`、`boss.STRAFE_SPEEDS`
- Produces: `BossMovement._move_bob(delta, boss, amp, period, y_center)`（正弦往复）、`reset_press()` 增加相位归零

- [ ] **Step 1: 新增 `_bob_phase`/`_bob_offset` 状态与 `_move_bob`**

在 `scripts/boss_movement.gd` 的状态字段区（`_press_offset` 之后）追加：

```gdscript
var _bob_phase: float = 0.0  # 纵向正弦相位累计（段切换归零，sin 0 = 0 无跳变）
var _bob_offset: float = 0.0  # 纵向正弦增量偏移（同 _update_press 增量式施加模式）
```

在 `_update_press` 函数之后追加：

```gdscript
## 纵向正弦（P2 通用，D05）：围绕锚线 ±amp 正弦往复；y_center 为锚线下附加偏移
## （三型 P1 区间呼吸用：center=(lo+hi)/2、amp=(hi-lo)/2）。增量式施加（同 _update_press，
## 不覆盖逃跑上飘/入场下移）。相位累计驱动，Enemy.sin_fast 查表零分配。
func _move_bob(delta: float, boss, amp: float, period: float, y_center: float = 0.0) -> void:
	_bob_phase += TAU * delta / maxf(period, 0.01)
	var target := boss.fight_anchor_y() + y_center + Enemy.sin_fast(_bob_phase) * amp
	boss.position.y += target - _bob_offset
	_bob_offset = target
```

- [ ] **Step 2: `reset_press()` 归零 bob 相位与偏移**

```gdscript
func reset_press() -> void:
	_press_offset = 0.0
	_press_timer = _press_timer  # 保留下压周期相位，仅清偏移
	_bob_phase = 0.0  # D05：段切换归零纵向正弦（sin 0 = 0 平滑衔接锚线）
	_bob_offset = 0.0
```

- [ ] **Step 3: `update()` 按阶段分发**

替换 `update()` 为：

```gdscript
func update(delta: float, boss) -> void:
	var phase: int = boss.fight_phase()
	match int(boss.boss_type):
		1:
			if phase == FIGHT_P1:
				_move_strafe(delta, boss, float(boss.STRAFE_SPEEDS[0]))
				_update_press(delta, boss)
			elif phase == 1:  # FightPhase.P2
				# D05：strafe 提速 + 纵向正弦往复
				_move_strafe(delta, boss, float(boss.TYPE1_P2_STRAFE))
				_move_bob(delta, boss, float(boss.TYPE1_P2_BOB_AMP), float(boss.TYPE1_P2_BOB_PERIOD))
			else:  # ENRAGE：狂暴轨道由 enrage_sequence 接管，走位维持现状
				_move_strafe(delta, boss, float(boss.STRAFE_SPEEDS[0]))
		2:
			_move_dash(delta, boss)
		3:
			if phase == FIGHT_P1:
				# D05：三型 P1 纵向区间呼吸（锚线下 [lo, hi] 正弦）
				_move_strafe(delta, boss, float(boss.STRAFE_SPEEDS[2]))
				var lo := float(boss.TYPE3_P1_BOB_MIN)
				var hi := float(boss.TYPE3_P1_BOB_MAX)
				_move_bob(delta, boss, (hi - lo) * 0.5, float(boss.TYPE3_P1_BOB_PERIOD), (lo + hi) * 0.5)
			elif phase == 1:  # FightPhase.P2
				# D05：strafe 提速 + 纵向正弦往复
				_move_strafe(delta, boss, float(boss.TYPE3_P2_STRAFE))
				_move_bob(delta, boss, float(boss.TYPE3_P2_BOB_AMP), float(boss.TYPE3_P2_BOB_PERIOD))
			else:  # ENRAGE 现状
				_move_strafe(delta, boss, float(boss.STRAFE_SPEEDS[2]))
```

- [ ] **Step 4: `_move_dash` 按阶段取冲刺节奏**

```gdscript
func _move_dash(delta: float, boss) -> void:
	_move_timer -= delta
	if _move_timer <= 0.0:
		_dashing = not _dashing
		# D05：P2 冲刺更频（0.4s/0.5s）；P1 与 ENRAGE 维持现状（0.5s/0.7s）
		var dash_t: float = float(boss.TYPE2_P2_DASH_TIME) if boss.fight_phase() == 1 else 0.5
		var rest_t: float = float(boss.TYPE2_P2_REST_TIME) if boss.fight_phase() == 1 else 0.7
		_move_timer = dash_t if _dashing else rest_t
		if _dashing:
			# 偏向屏幕中心方向冲刺，避免长期贴边（C14：中心取可见世界，不写死 960）
			var center_x: float = GameState.view_world_rect().get_center().x
			_strafe_dir = signf(center_x - boss.position.x) if randf() < 0.6 else (-_strafe_dir)
			if _strafe_dir == 0.0:
				_strafe_dir = 1.0
	if _dashing:
		boss.position.x += _strafe_dir * float(boss.STRAFE_SPEEDS[1]) * float(boss.slow_factor()) * _enrage_speed_mult(boss) * delta
		var bounds: Vector2 = boss.strafe_range()
		if boss.position.x < bounds.x or boss.position.x > bounds.y:
			_strafe_dir = -_strafe_dir
			boss.position.x = clampf(boss.position.x, bounds.x, bounds.y)
```

- [ ] **Step 5: 运行测试确认通过（TDD 绿）**

Run: `godot --headless --path . res://test/boss_phase_test.tscn`
Expected: 全部断言 PASS（含 Task 2 新增的走位断言）；exit 0。

- [ ] **Step 6: 跑关联测试防回归**

Run:
```bash
godot --headless --path . res://test/boss_pattern_test.tscn
godot --headless --path . res://test/boss_enrage_test.tscn
godot --headless --path . res://test/smoke_test.tscn
```
Expected: 0 FAIL（ENRAGE/狂暴/模式测试不涉走位断言，应稳定通过）。

- [ ] **Step 7: Commit**

```bash
git add scripts/boss_movement.gd
git commit -m "feat: Boss P2 走位升级——一型/三型 P2 strafe 提速 + 纵向正弦往复、三型 P1 区间呼吸、二型 P2 冲刺更频（D05）（2026-08-02）"
```

---

### Task 4: 回归验证 + 数值映射 + 档案回填

**Files:**
- Modify: `docs/BOSS_REDESIGN.md`（§8.2 D05 登记更新）
- Modify: `docs/AUDIT_VAULT.md`（D05 状态回填）
- Modify: `docs/2026-08-02-audit-fix-plan.md`（D05 追踪表更新）
- Regenerate: `docs/BALANCE_MAP.md`（gen_balance_map.py）

- [ ] **Step 1: 重跑数值映射生成器**

Run: `python3 scripts/tools/gen_balance_map.py`
Expected: 输出含新键；双向反查"脚本引用但 json 缺失"节为空。

- [ ] **Step 2: 全量回归**

Run:
```bash
godot --headless --import --path .
godot --headless --path . --quit-after 300
```
Expected: exit 0，无错误；随后全量 30 断言场景逐个跑，0 FAIL（含 boss_phase/boss_pattern/boss_enrage/smoke）。

- [ ] **Step 3: BOSS_REDESIGN §8.2 D05 登记更新**

把 §8.2 中 D05 登记条目改为已实现，追加实现记录：

```markdown
- 走位升级（**2026-08-02 落地，D05 修复**）：§5.5 实现——一型 P2 strafe 200 + 纵向正弦 ±40/6s、
  二型 P2 冲刺 0.4s/0.5s、三型 P1 锚线下 200–280px 区间呼吸（9s）、三型 P2 strafe 100 + 正弦 ±50/8s；
  配置入 `balance.json boss.movement`（11 键，脚本回退同步）；`BossMovement._move_bob` 复用
  `Enemy.sin_fast` + `_update_press` 增量式 y 施加；ENRAGE 走位不受影响。验证：boss_phase_test
  新增 5 断言 + 全量 30 断言场景 0 FAIL。
```

- [ ] **Step 4: AUDIT_VAULT D05 状态回填**

把 D 系列修复起效记录表中 D05 行更新为 `✅ 已修复`，并补"修复起效记录"（改了什么/为什么起效/验证）。

- [ ] **Step 5: 审计计划文档 D05 追踪行更新**

`docs/2026-08-02-audit-fix-plan.md` 追踪表 D05 行改 `✅ 已修复` 并补验证。

- [ ] **Step 6: Commit**

```bash
git add docs/BOSS_REDESIGN.md docs/AUDIT_VAULT.md docs/2026-08-02-audit-fix-plan.md docs/BALANCE_MAP.md
git commit -m "docs: Boss P2 走位升级档案回填——BOSS_REDESIGN §5.5/§8.2、AUDIT_VAULT D05、BALANCE_MAP（2026-08-02）"
```

---

## Self-Review

- **规格覆盖**：§5.1 一型 P2（strafe 200 + 纵向往复）→ Task 3 一型分支 ✓；§5.2 二型 P2（0.4/0.5）→ Task 3 `_move_dash` ✓；§5.3 三型 P1（y 200-280 正弦）→ Task 3 三型 P1 band ✓；§5.3 三型 P2（strafe 100 + 纵向往复）→ Task 3 三型 P2 ✓；配置入 balance.json ✓；复用 sin_fast/增量式 ✓；ENRAGE 不受影响 ✓（各型 else 分支维持现状）。
- **占位符**：无 TBD/TODO；所有代码块为完整实现。
- **类型一致**：`Boss.TYPE1_P2_STRAFE`（int）/`TYPE1_P2_BOB_AMP`（float）等 11 字段在 Task 1 定义、Task 2 测试引用、Task 3 实现引用，命名一致；`_move_bob(delta, boss, amp, period, y_center=0.0)` 签名在 Task 3 Step 1 定义、Step 3 按 `(amp, period)` 与 `(amp, period, center)` 两种形态调用，一致。
- **已知风险**：boss_phase_test 场景 3 的 `boss3` 变量名需按现有代码核对；C11 断言在 bob 相位未推进时仍成立（sin 0 = 0），若 CI 偶发失配，将容差改为 `amp + 4`（测试代码中已按 ±amp+4 的新断言兜底）。
