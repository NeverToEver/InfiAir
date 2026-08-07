class_name TaskPool
extends RefCounted
## 基地任务池（TaskPool，任务轮换核心）：无放回随机抽取任务定义。
## 2026-08-07：抽取算法核心迁移 InfiAir.Core.Missions.TaskPool（csharp/core/Missions/TaskPool.cs），
## 本壳保留公开签名（_init/draw）转发 C# 绑定壳 TaskPoolInterop，行为语义不变：
## 单次 draw 内不重复；一次 draw 消耗完当前批次后若仍有名额且全池还有可用候选则重洗
## 继续补足（跨 draw 尽量延迟复用，排除在场任务导致的提前耗尽不再截断——Q05）；
## 排除覆盖全池时安全返回空。RNG 独立于 GDScript 全局随机源（性质等价、序列不等价——
## 原 shuffle 依赖全局种子，无外部依赖具体序列）。

var _interop: Variant = null


func _init(defs: Array[Dictionary]) -> void:
	_interop = load("res://csharp/godot/TaskPoolInterop.cs").new()
	_interop.call("SetDefs", defs)


## 抽取 count 个任务定义（无放回：单次 draw 内不重复、耗尽跨批补足直到可用候选抽完）。
## exclude_ids：排除这些 id（刷新时排除全部在场任务，防重号/覆盖保留任务）。
## 返回实际抽到的定义（排除覆盖全池时安全返回空，不抛错不死循环）。
func draw(count: int, exclude_ids: Array[StringName] = []) -> Array[Dictionary]:
	var drawn: Array = _interop.call("Draw", count, exclude_ids)
	var out_defs: Array[Dictionary] = []
	for def in drawn:
		out_defs.append(def)
	return out_defs
