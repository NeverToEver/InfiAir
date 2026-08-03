extends Node
## A3 架构断言测试：Boss 攻击/移动/狂暴注册表与机型参数表完整性——
## 注册表覆盖全部机型与模式表（脚本默认 + balance.json 运行表）引用的攻击 id；
## 新增攻击/机型只需加注册行，不再改既有分发函数（O 原则达成）。

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
	# 1. 攻击注册表：9 个已知攻击 id 全覆盖（homing2 于 2026-08-03 审计删除，弹数分档并入 homing）
	var attacks := BossAttacks.new()
	for id: StringName in [
		&"fan5", &"fan7", &"homing", &"sniper3", &"cross", &"charged_cannon", &"dash_sweep", &"minion_volley", &"bullet_wall"
	]:
		_check(attacks.has_attack(id), "攻击注册表包含 %s" % id)
	_check(attacks.attack_ids().size() == 9, "攻击注册表共 9 项")
	# B 梯队（fair plan §8）：每攻击独特 tell——注册的攻击 id 全部有 tell 配置
	# （新攻击漏配 tell 时此断言拦截；ATTACK_TELLS 缺失键 = 无 tell）
	var tell_missing: Array = []
	for id: StringName in attacks.attack_ids():
		if not attacks.ATTACK_TELLS.has(id):
			tell_missing.append(id)
	_check(tell_missing.is_empty(), "全部攻击 id 有独特 tell 配置（缺失: %s）" % tell_missing)

	# 2. 模式表交叉验证：脚本默认表 + balance.json 运行表引用的攻击 id 全在注册表
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(GameState.BALANCE_PATH))
	var missing: Array = []
	for t in [1, 2, 3]:
		var defaults: Dictionary = Boss.DEFAULT_PATTERNS[t]
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

	# 3. 移动器注册表覆盖 3 机型
	var movement := BossMovement.new()
	for t in [1, 2, 3]:
		_check(movement.has_mover(t), "移动器注册表包含机型 %d" % t)

	# 4. 狂暴三阶段注册表覆盖 3 机型
	var enrage := EnrageSequence.new()
	for t in [1, 2, 3]:
		_check(enrage.has_active_handler(t), "狂暴 ACTIVE 处理器包含机型 %d" % t)
		_check(enrage.has_release_handler(t), "狂暴 RELEASE 处理器包含机型 %d" % t)
		_check(enrage.has_release_begin_handler(t), "狂暴释放起手处理器包含机型 %d" % t)

	# 5. 机型参数表：召唤表仅 3 型启用；闪白时长表覆盖全部机型
	_check(bool(Boss.SUMMONER_TYPES.get(3, false)), "召唤表：3 型启用独立召唤")
	_check(
		not bool(Boss.SUMMONER_TYPES.get(1, false)) and not bool(Boss.SUMMONER_TYPES.get(2, false)),
		"召唤表：1/2 型不启用",
	)
	for t in [1, 2, 3]:
		_check(Boss.HIT_FLASH_BY_TYPE.has(t), "闪白时长表包含机型 %d" % t)

	print("BOSS REGISTRY TEST DONE, failures = ", _failures)
	get_tree().quit(_failures)
