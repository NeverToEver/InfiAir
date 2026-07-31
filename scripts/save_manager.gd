class_name SaveManager
extends RefCounted
## A2 阶段 2：对局存档 / 局外档案的文件 IO 剥离（docs/AUDIT_VAULT.md A2）。
## 只做文件读写 + JSON 解析 + 损坏隔离；数据模型（序列化字段组装/回读）仍由 GameState 负责。
## 行为与原 game_state.gd 逐字节等价：损坏文件隔离为 <path>.corrupt 并置 last_was_corrupt。

## 最近一次 load 是否因损坏而隔离（GameState 据此设置 save_corrupt / profile_corrupt）
var last_was_corrupt: bool = false


func exists(path: String) -> bool:
	return FileAccess.file_exists(path)


func delete(path: String) -> void:
	if exists(path):
		DirAccess.remove_absolute(path)


## 写 JSON 文件；打开失败 push_warning 并返回 false（对齐原 save_run/save_profile 行为）
func save(path: String, data: Dictionary) -> bool:
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		push_warning("InfiAir: 无法写入 %s（错误 %d）" % [path, FileAccess.get_open_error()])
		return false
	f.store_string(JSON.stringify(data))
	f.close()
	return true


## 读 JSON 文件：不存在/读取失败返回 {}（不置损坏）；损坏则隔离备份并置 last_was_corrupt。
func load(path: String) -> Dictionary:
	last_was_corrupt = false
	if not exists(path):
		return {}
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return {}
	var text := f.get_as_text()
	f.close()
	# 用 JSON 实例解析（parse_string 会把损坏内容打成 ERROR 级日志，噪音大）
	var json := JSON.new()
	if json.parse(text) == OK and json.data is Dictionary:
		return json.data
	# 损坏存档：隔离备份后按无存档处理（否则「继续对局」每次点了都无反应，形成死路径）
	quarantine(path)
	last_was_corrupt = true
	return {}


## 损坏文件隔离：重命名为 <path>.corrupt（已有备份则先删），给玩家留排查余地
func quarantine(path: String) -> void:
	var backup := path + ".corrupt"
	if FileAccess.file_exists(backup):
		DirAccess.remove_absolute(backup)
	var err := DirAccess.rename_absolute(path, backup)
	if err != OK:
		push_warning("InfiAir: 无法备份损坏文件 %s（错误 %d）" % [path, err])


## 存档数值字段安全读取：手改存档的非法类型（字符串/数组/字典等）回默认值
func sanitize_num(v: Variant, default: float) -> float:
	return float(v) if v is int or v is float else default
