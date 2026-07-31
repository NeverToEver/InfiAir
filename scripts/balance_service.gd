class_name BalanceService
extends RefCounted
## A2 阶段 1：数值配置读操作剥离（docs/AUDIT_VAULT.md A2）。
## 持有 balance.json 解析字典，提供 cfg 路径查询与纯数值查询；由 GameState 组合并委托。
## 非 autoload、非节点：只承载"配置字典加载与查询"单一职责，不持有游戏状态。
##
## 既有约定不变：热路径在实体 _ready 一次性读入并缓存，禁止每帧 cfg 查询；
## 缺失/损坏 JSON 回退脚本默认值（脚本内同名 var 是回退默认值，需与 JSON 一致）。

var _balance: Dictionary = {}


func load(path: String) -> void:
	_balance = {}
	if not FileAccess.file_exists(path):
		return
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	if parsed is Dictionary:
		_balance = parsed


## 配置字典是否为空（缺失/损坏 JSON 时为空，全部回退脚本默认值）
func is_empty() -> bool:
	return _balance.is_empty()


## 统一配置访问：路径如 "player.fuel.drain"。缺键/类型不符回退 default。
func cfg(path: String, default: Variant) -> Variant:
	var node: Variant = _balance
	for key in path.split("."):
		if node is Dictionary and node.has(key):
			node = node[key]
		else:
			return default
	# 数值宽容：JSON 整数/浮点互通
	if default is int or default is float:
		if node is int or node is float:
			return node
		return default
	if node is Array and default is Array:
		return node
	if typeof(node) == typeof(default):
		return node
	return default


## 敌方 HP 对局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))，随 Boss 击杀线性成长。
## 纯查询：难度乘数作参数传入，不依赖调用方状态。
func enemy_hp_ramp(difficulty_multiplier: float) -> float:
	return 1.0 + float(cfg("enemies.hp_ramp_factor", 0.12)) * (difficulty_multiplier - 1.0)


## 敌方伤害对局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))，
## 统一作用于全部敌方伤害源（敌弹/Boss 弹/撞体/编队炸弹；2026-07-29 无限段修订）。
## 纯查询：难度乘数作参数传入。
func enemy_damage_ramp(difficulty_multiplier: float) -> float:
	return 1.0 + float(cfg("enemies.damage_ramp_factor", 0.08)) * (difficulty_multiplier - 1.0)
