extends Node
## M3d：Boss 迁 C#，默认模式表经脚本资源静态访问
var _boss_script := load("res://csharp/godot/Boss.cs")
var _boss_attacks_script := load("res://csharp/godot/BossAttacks.cs")
## A3 架构断言测试：Boss 攻击/移动/狂暴注册表与机型参数表完整性——
## 注册表覆盖全部机型与模式表（脚本默认 + balance.json 运行表）引用的攻击 id；
## 新增攻击/机型只需加注册行，不再改既有分发函数（O 原则达成）。

# M3d：Boss/BossAttacks/BossMovement/EnrageSequence 已迁 C#——本测试断言对象为纯 C# 类私有数据
# （组件注册表 + Boss 静态表），GDScript 不可实例化/跨语言调用。本文件按规则 2e 机械改写（load().new()），
# 但组件为纯 C# 类（非 GodotObject 派生，Boss.cs 亦无 GDScript 可见静态表），load 无法解析、方法不可调——
# 本测试无法经 GDScript 运行，需主代理决定：迁 tests-csharp/（xUnit 纯逻辑单测，组件为纯逻辑天然契合）
# 或请 M3d 提供 GDScript 桥（详见适配报告「无法处理」清单）。
var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 按点分路径导航解析后的 JSON 字典（"a.b.c" → data["a"]["b"]["c"]），任一环缺失返回 null
func _json_at(data: Variant, path: String) -> Variant:
	var node: Variant = data
	for key: String in path.split("."):
		if node is Dictionary and node.has(key):
			node = node[key]
		else:
			return null
	return node


func _ready() -> void:
	# 1. 攻击注册表：10 个已知攻击 id 全覆盖（homing2 于 2026-08-03 审计删除，弹数分档并入 homing；
	# 2026-08-04 新增 ring_burst——4 型「月蚀」环弹）
	var attacks = _boss_attacks_script.new()  # M3d：规则 2e 机械改写；BossAttacks 为纯 C# 类，load 实际不可解析（见报告）
	for id: StringName in [
		&"fan5",
		&"fan7",
		&"homing",
		&"sniper3",
		&"cross",
		&"charged_cannon",
		&"dash_sweep",
		&"minion_volley",
		&"bullet_wall",
		&"ring_burst",
	]:
		_check(attacks.has_attack(id), "攻击注册表包含 %s" % id)
	_check(attacks.attack_ids().size() == 10, "攻击注册表共 10 项")
	# B 梯队（fair plan §8）：每攻击独特 tell——注册的攻击 id 全部有 tell 配置
	# （新攻击漏配 tell 时此断言拦截；ATTACK_TELLS 缺失键 = 无 tell）
	# M3d：ATTACK_TELLS 已更名 BossAttacks 私有静态 AttackTells，GDScript 不可引用——随迁 tests-csharp/（见报告）
	var tell_missing: Array = []
	for id: StringName in attacks.attack_ids():
		if not _boss_attacks_script.GetAttackTells().has(id):  # M3d：C# 私有静态表经脚本资源
			tell_missing.append(id)
	_check(tell_missing.is_empty(), "全部攻击 id 有独特 tell 配置（缺失: %s）" % tell_missing)

	# 2. 模式表交叉验证：脚本默认表 + balance.json 运行表引用的攻击 id 全在注册表
	# M3d：Boss.DEFAULT_PATTERNS 为 Boss.cs 私有静态 DefaultPatterns，GDScript 不可引用——本段断言随迁 tests-csharp/（见报告）
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(GameState.BALANCE_PATH))
	var missing: Array = []
	for t in [1, 2, 3, 4]:
		var defaults: Dictionary = _boss_script.GetDefaultPatterns()[t]  # M3d：C# 静态经脚本资源
		var cfg_table: Variant = _json_at(parsed, "boss.phases.type%d" % t)
		for phase_key: String in defaults:
			for pattern: Dictionary in defaults[phase_key]:
				if not attacks.has_attack(StringName(pattern.get("attack", &""))):
					missing.append(String(pattern.get("attack", &"")))
		if cfg_table is Dictionary:
			for phase_key: String in cfg_table:
				var patterns: Variant = cfg_table[phase_key]
				if not (patterns is Array):
					continue  # 非模式段键（如 type3.summon_interval）跳过
				for pattern: Variant in patterns:
					if not attacks.has_attack(StringName(pattern.get("attack", &""))):
						missing.append(String(pattern.get("attack", &"")))
	_check(missing.is_empty(), "模式表引用的攻击 id 全部注册（缺失: " + str(missing) + "）")

	# 3. 移动器注册表覆盖 4 机型
	var movement = load("res://csharp/godot/BossMovement.cs").new()  # M3d：规则 2e 机械改写；BossMovement 为纯 C# 类，load 实际不可解析（见报告）
	for t in [1, 2, 3, 4]:
		_check(movement.has_mover(t), "移动器注册表包含机型 %d" % t)

	# 4. 狂暴三阶段注册表覆盖 4 机型
	var enrage = load("res://csharp/godot/EnrageSequence.cs").new()  # M3d：规则 2e 机械改写；EnrageSequence 为纯 C# 类，load 实际不可解析（见报告）
	for t in [1, 2, 3, 4]:
		_check(enrage.has_active_handler(t), "狂暴 ACTIVE 处理器包含机型 %d" % t)
		_check(enrage.has_release_handler(t), "狂暴 RELEASE 处理器包含机型 %d" % t)
		_check(enrage.has_release_begin_handler(t), "狂暴释放起手处理器包含机型 %d" % t)

	# 5. 机型参数表：召唤表仅 3 型启用；闪白时长表覆盖全部机型
	# M3d：Boss.SUMMONER_TYPES/HIT_FLASH_BY_TYPE 在 C# 中不存在（逻辑已内联）——本段断言随迁 tests-csharp/（见报告）
	_check(bool(_boss_script.GetSummonerTypes().get(3, false)), "召唤表：3 型启用独立召唤")
	_check(
		(
			not bool(_boss_script.GetSummonerTypes().get(1, false))
			and not bool(_boss_script.GetSummonerTypes().get(2, false))
			and not bool(_boss_script.GetSummonerTypes().get(4, false))
		),
		"召唤表：1/2/4 型不启用",
	)
	for t in [1, 2, 3, 4]:
		_check(_boss_script.GetHitFlashByType().has(t), "闪白时长表包含机型 %d" % t)  # M3d：C# 静态经脚本资源

	print("BOSS REGISTRY TEST DONE, failures = ", _failures)
	get_tree().quit(_failures)
