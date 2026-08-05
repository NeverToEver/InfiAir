class_name TaskPool
extends RefCounted
## 基地任务池（TaskPool，任务轮换核心）：无放回随机抽取任务定义。
## 实现：洗牌索引序列 + 游标推进。单次 draw 内不重复；一次 draw 消耗完当前批次后
## 若仍有名额且全池还有可用候选则重洗继续补足（跨 draw 尽量延迟复用，刷新后短期内
## 不会抽回旧任务；排除在场任务导致的提前耗尽不再截断——Q05）。
## 与 GameState 的 missions 字典解耦：本类只负责「从定义池抽定义」，不做状态。

var _defs: Array[Dictionary] = []
var _order: Array[int] = []
var _cursor: int = 0


func _init(defs: Array[Dictionary]) -> void:
	_defs = defs.duplicate()


## 抽取 count 个任务定义（无放回：单次 draw 内不重复、耗尽跨批补足直到可用候选抽完）。
## exclude_ids：排除这些 id（刷新时排除全部在场任务，防重号/覆盖保留任务）。
## 返回实际抽到的定义（排除覆盖全池时安全返回空，不抛错不死循环）。
func draw(count: int, exclude_ids: Array[StringName] = []) -> Array[Dictionary]:
	if count <= 0:
		return []
	var usable := 0
	for def in _defs:
		if not exclude_ids.has(def["id"]):
			usable += 1
	if usable == 0:
		return []  # 防呆：全池被排除，无可用任务
	var out: Array[Dictionary] = []
	var drawn_ids: Dictionary = {}  # Q05：跨批补足时防单次 draw 内重复（新批次可能重含已抽 id）
	while out.size() < count and out.size() < usable:
		if _cursor >= _order.size():
			if out.size() < usable:
				_refill()  # Q05：批次耗尽但全池仍有可用候选 → 重洗继续补足（原实现提前 break）
			else:
				break
		var def: Dictionary = _defs[_order[_cursor]]
		_cursor += 1
		if exclude_ids.has(def["id"]) or drawn_ids.has(def["id"]):
			continue
		drawn_ids[def["id"]] = true
		out.append(def)
	return out


## 追加一批全索引洗牌（池可循环复用；游标只增不减，序列无限长）
func _refill() -> void:
	var batch: Array[int] = []
	for i in _defs.size():
		batch.append(i)
	batch.shuffle()
	_order.append_array(batch)
