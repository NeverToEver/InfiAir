class_name UserDB
extends RefCounted
## 本地用户数据库（2026-08-04 账户系统；规格 docs/archive/2026-08-04-local-accounts-plan.md + PORTING_PARITY 附录 B）。
## P0-2（2026-08-07）：数据层迁移 InfiAir.Core.Storage.UserDb（C#，见 csharp/core/Storage/UserDb.cs +
## csharp/godot/UserDbInterop.cs），本文件为薄壳转发——公开 API/常量/iterations 降档机制不变。
## 密码派生为逐字节等价迁移（自建 PBKDF2 变体，固定向量对照 tests-csharp/UserDbPasswordTests.cs）。
## 不移植：fcntl 文件锁（单进程桌面无并发）、远程排行榜（联机已砍）。

const USERS_PATH := "user://users.json"
const RESERVED_NAMES: Array[String] = ["_leaderboard", "Guest"]
const NAME_MIN := 3
const NAME_MAX := 16
const PASSWORD_MIN := 3
const PASSWORD_MAX := 16
const LEADERBOARD_CAP := 10
const PLAYER_NAME_MAX := 32
const PBKDF2_ITERATIONS := 50_000

## PBKDF2 迭代数（测试场景降档加速；生产默认 50_000——实测 100k 迭代 create/verify ≈330ms，
## 超过计划书 300ms 判定线，按 docs/2026-08-04-local-accounts-plan.md 约定降档至 ~165ms；
## 单机离线文件无在线暴力破解面，数据文件亦无需与 py 端互通，迭代数非兼容约束）
var iterations: int = PBKDF2_ITERATIONS

var _interop: Variant = null


func _init() -> void:
	_interop = load("res://csharp/godot/UserDbInterop.cs").new()


## 强制重新加载磁盘状态（2026-08-06 审计：GameState._ready 的迁移探测会提前缓存真实用户表
## ——测试 wipe user:// 文件后缓存仍非空，「空用户表」起点失效；显式重载供测试/诊断刷新。
## 生产无调用点）
func reload() -> void:
	_interop.call("Reload")


## 注册：拒绝保留名（B7-7：_leaderboard 与 Guest）与长度不合规；成功写入并落盘。
func create_user(name: String, password: String) -> bool:
	return bool(_interop.call("CreateUser", name, password, iterations))


func verify_user(name: String, password: String) -> bool:
	return bool(_interop.call("VerifyUser", name, password, iterations))


func user_exists(name: String) -> bool:
	return bool(_interop.call("UserExists", name))


## 用户名列表：last_login_order 降序 + 名字典序（B4 last_login 为自增序号非时间戳）
func list_usernames() -> Array[String]:
	var names: Array[String] = []
	for n in _interop.call("ListUsernames"):
		names.append(String(n))
	return names


func get_last_login_user() -> String:
	return String(_interop.call("GetLastLoginUser"))


func record_login(name: String) -> void:
	_interop.call("RecordLogin", name)


func get_user_data(name: String) -> Dictionary:
	var data: Variant = _interop.call("GetUserData", name)
	return data if data is Dictionary else {}


## 通用字段合并更新（统计类）；密码/盐/迭代数不可经此覆盖
func update_user_data(name: String, data: Dictionary) -> void:
	_interop.call("UpdateUserData", name, data)


## 仅更高才写（B4 update_high_score 语义）；分数负钳 0
func update_high_score(name: String, score: int) -> void:
	_interop.call("UpdateHighScore", name, score)


func get_user_settings(name: String) -> Dictionary:
	var data: Variant = _interop.call("GetUserSettings", name)
	return data if data is Dictionary else {}


func update_user_settings(name: String, settings: Dictionary) -> void:
	_interop.call("UpdateUserSettings", name, settings)


## 删除用户（先验密）；连带清理该用户存档文件与 .corrupt 备份（B7-12 + 2026-08-06 审计口径）
func delete_user(name: String, password: String) -> bool:
	return bool(_interop.call("DeleteUser", name, password))


## 每用户存档路径：user://savegame_<sanitized>_<sha256[:12]>.json（B5，对齐原作 _save_file_for_user）
func savefile_for_user(name: String) -> String:
	return "user://" + String(_interop.call("SaveFileName", name))


## 提交成绩：score 负钳 0（≤0 不入榜，对齐现 GameState 高分榜语义）；cap 10；
## 排序 score 降序 + 提交序（seq 自增，先到先得）；返回 1-indexed 名次，0 = 未上榜。
func submit_score(name: String, score: int) -> int:
	return int(_interop.call("SubmitScore", name, score))


func get_leaderboard() -> Array:
	var board: Array = []
	for entry in _interop.call("GetLeaderboard"):
		board.append(entry)
	return board
