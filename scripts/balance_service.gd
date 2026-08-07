class_name BalanceService
extends RefCounted
## A2 阶段 1：数值配置读操作剥离（docs/AUDIT_VAULT.md A2）。
## 持有 balance.json 解析字典，提供 cfg 路径查询与纯数值查询；由 GameState 组合并委托。
## 非 autoload、非节点：只承载"配置字典加载与查询"单一职责，不持有游戏状态。
##
## 既有约定不变：热路径在实体 _ready 一次性读入并缓存，禁止每帧 cfg 查询；
## 缺失/损坏 JSON 回退脚本默认值（脚本内同名 var 是回退默认值，需与 JSON 一致）。
## P1-1（2026-08-07）：点路径解析核心迁移 InfiAir.Core.Config.PathResolver（C# 纯函数，
## 见 csharp/core/Config/PathResolver.cs）——load() 装载 C# 侧配置树、cfg() 转发解析；
## 公开签名与语义不变（469 处调用点零改动，BALANCE_MAP M8 零影响）。

var _balance: Dictionary = {}
## G09：ramp 因子 load() 时缓存一次——热路径（每发敌弹创建）免 path.split/字典遍历 JSON 查询
var _hp_ramp_factor := 0.25
var _damage_ramp_factor := 0.20
## P1-1：C# 点路径解析壳（PathResolverInterop，load 时 SetData 一次）
var _interop: Variant = null


func _init() -> void:
	_interop = load("res://csharp/godot/PathResolverInterop.cs").new()


func load(path: String) -> void:
	_balance = {}
	if not FileAccess.file_exists(path):
		# 缺失：保持原语义（ramp 缓存不刷新），但 C# 壳须同步为空树
		_interop.call("SetData", _balance)
		return
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	if parsed is Dictionary:
		_balance = parsed
	# P1-1：配置树同步到 C# 解析壳（损坏/缺失时为空字典，全部回退默认）
	_interop.call("SetData", _balance)
	# G09：缓存 ramp 因子（缺键回退脚本默认，与 cfg 语义一致）
	_hp_ramp_factor = float(cfg("enemies.hp_ramp_factor", 0.25))
	_damage_ramp_factor = float(cfg("enemies.damage_ramp_factor", 0.20))


## 配置字典是否为空（缺失/损坏 JSON 时为空，全部回退脚本默认值）
func is_empty() -> bool:
	return _balance.is_empty()


## 统一配置访问：路径如 "player.fuel.drain"。缺键/类型不符回退 default。
## P1-1：语义（数值宽容/容器拷贝/typeof 相等）由 PathResolver 保证，本壳只做转发。
func cfg(path: String, default: Variant) -> Variant:
	return _interop.call("Resolve", path, default)


## 敌方 HP 对局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))，随 Boss 击杀线性成长。
## 纯查询：难度乘数作参数传入，不依赖调用方状态。
func enemy_hp_ramp(difficulty_multiplier: float) -> float:
	return 1.0 + _hp_ramp_factor * (difficulty_multiplier - 1.0)


## 敌方伤害对局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))，
## 统一作用于全部敌方伤害源（敌弹/Boss 弹/撞体/编队炸弹；2026-07-29 无限段修订）。
## 纯查询：难度乘数作参数传入。
func enemy_damage_ramp(difficulty_multiplier: float) -> float:
	return 1.0 + _damage_ramp_factor * (difficulty_multiplier - 1.0)
