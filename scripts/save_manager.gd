class_name SaveManager
extends RefCounted
## A2 阶段 2：对局存档 / 局外档案的文件 IO（docs/AUDIT_VAULT.md A2）。
## P0-1（2026-08-07）：原子写 / 损坏隔离 / JSON 序列化迁移 InfiAir.Core.Storage.SaveStore
## （C#，见 csharp/core/Storage/SaveStore.cs + csharp/godot/SaveStoreInterop.cs），
## 本文件为薄壳转发——公开 API 与行为等价不变（损坏隔离 <path>.corrupt + last_was_corrupt）。
## 数据模型（序列化字段组装/回读）仍由 GameState 负责。

## 最近一次 load 是否因损坏而隔离（GameState 据此设置 save_corrupt / profile_corrupt）
var last_was_corrupt: bool = false

var _interop: Variant = null


func _init() -> void:
	_interop = load("res://csharp/godot/SaveStoreInterop.cs").new()


func exists(path: String) -> bool:
	return bool(_interop.call("Exists", path))


func delete(path: String) -> void:
	_interop.call("Delete", path)


## 写 JSON 文件：C# SaveStore 原子写（临时文件 + rename 回退，E12 审计口径：
## 先尝试原子 rename 覆盖，首次失败才删正本重试——回退路径才触发风险窗口）。
## 打开/写入失败 push_warning 并返回 false（对齐原 save_run/save_profile 行为）。
func save(path: String, data: Dictionary) -> bool:
	return bool(_interop.call("Save", path, data))


## 读 JSON 文件：不存在/读取失败返回 {}（不置损坏）；损坏则隔离备份并置 last_was_corrupt。
func load(path: String) -> Dictionary:
	last_was_corrupt = false
	var res: Variant = _interop.call("Load", path)
	if res is Dictionary:
		if res.get("corrupt") == true:
			last_was_corrupt = true
		var data: Variant = res.get("data")
		if data is Dictionary:
			return data
	return {}


## 损坏文件隔离：重命名为 <path>.corrupt（已有备份则先删），给玩家留排查余地
func quarantine(path: String) -> void:
	_interop.call("Quarantine", path)


## 存档数值字段安全读取：手改存档的非法类型（字符串/数组/字典等）回默认值
func sanitize_num(v: Variant, default: float) -> float:
	return float(v) if v is int or v is float else default
