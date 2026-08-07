class_name UserDB
extends RefCounted
## 本地用户数据库（2026-08-04 账户系统；规格 docs/archive/2026-08-04-local-accounts-plan.md + PORTING_PARITY 附录 B）。
## 单文件 user://users.json 共存用户表与本地排行榜；密码派生为自建 PBKDF2 变体
## （盐 16B hex + 迭代数随用户记录存档，注册时确定——测试可降档加速；2026-08-06 审计：
## 实现为「盐||块号拼接进异或链」的双块结构，非标准 PBKDF2-HMAC-SHA256，不与标准工具
## 互通；本地自建自验无实际弱化，保持实现不动以免破坏既有账号，口径以本注释为准）；
## 原子写 + 损坏隔离复用 SaveManager。
## 非 autoload 服务，由 GameState 持有转发（A2 组合模式，保持"唯一 autoload"约定）。
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

var _save := SaveManager.new()
var _crypto := Crypto.new()
var _db: Dictionary = {}
var _loaded := false


func _ensure_loaded() -> void:
	if _loaded:
		return
	_loaded = true
	var data := _save.load(USERS_PATH)
	# Q17（2026-08-05）：结构守卫——用户表非 Dictionary 时按空库重建
	# （原实现直接沿用，users.has()/rec.get 在非 Dictionary 上运行时报错；
	# 对齐 GameState 层元素级守卫口径，与损坏 JSON 隔离同语义）。
	# 榜单键缺失/非 Array（旧数据或手改）单独补空，不连带丢弃有效用户表
	if data.get("_users") is Dictionary:
		_db = data
		if not _db.get("_leaderboard") is Array:
			_db["_leaderboard"] = []
	else:
		_db = {"_users": {}, "_leaderboard": []}


## 用户记录安全读取（2026-08-06 审计：Q17 只守顶层用户表，条目级非 Dictionary
## 手改值在 .get()/索引写处抛运行时类型错误；非 Dictionary 条目回空字典）
func _user_record(name: String) -> Dictionary:
	var rec: Variant = _db.get("_users", {}).get(name, {})
	return rec if rec is Dictionary else {}


func _save_db() -> bool:
	return _save.save(USERS_PATH, _db)


## 强制重新加载磁盘状态（2026-08-06 审计：GameState._ready 的迁移探测（
## _maybe_migrate_legacy_profile）会提前触发 _ensure_loaded，把真实用户表缓存进
## _db——测试 wipe user:// 文件后缓存仍非空，「空用户表」起点失效；显式重载
## 供测试/诊断在 wipe 后刷新。生产无调用点）
func reload() -> void:
	_loaded = false
	_db = {}
	_ensure_loaded()


# ---------------- PBKDF2 ----------------


## 自建 PBKDF2 变体（2026-08-06 审计口径修正，勿当标准 PBKDF2-HMAC-SHA256 使用）：
## 块 = 盐 || INT32_BE(块号)（20 字节），T = 块号首块 ^ U1 ^ U2 …（仅前 20 字节参与异或），
## 输出 = 各块前段拼接后截 32 字节——与标准 PBKDF2（T = U1 ^ … ^ Uc，整块 32 字节）不互通。
## 维持既有实现：改动会破坏全部现有账号的验密，本地自验无实际弱化。
func _derive(password: String, salt: PackedByteArray, iter: int) -> PackedByteArray:
	var key := password.to_utf8_buffer()
	var block := PackedByteArray()
	var block_index := 1
	while block.size() < 32:
		var u := salt.duplicate()
		u.append((block_index >> 24) & 0xFF)
		u.append((block_index >> 16) & 0xFF)
		u.append((block_index >> 8) & 0xFF)
		u.append(block_index & 0xFF)
		var t := u.duplicate()
		for _n in iter:
			u = _crypto.hmac_digest(HashingContext.HASH_SHA256, key, u)
			for j in t.size():
				t[j] = t[j] ^ u[j]
		block.append_array(t)
		block_index += 1
	return block.slice(0, 32)


func _valid_name(name: String) -> bool:
	return name.length() >= NAME_MIN and name.length() <= NAME_MAX


# ---------------- hex 编解码（PackedByteArray.hex/from_hex 非跨版本 API，自实现） ----------------

const _HEX_DIGITS := "0123456789abcdef"


func _hex_encode(bytes: PackedByteArray) -> String:
	var out := ""
	for b in bytes:
		out += _HEX_DIGITS[b >> 4] + _HEX_DIGITS[b & 0xF]
	return out


func _hex_decode(hex: String) -> PackedByteArray:
	var out := PackedByteArray()
	# Q18（2026-08-05）：长度/白名单校验——奇数长度时 hex[i+1] 越界、非法字符 find 返回
	# -1 会被 append 进 PackedByteArray（手改 salt/password 触发）；非法输入回退空盐
	#（验密必然失败，杜绝异常值与越界）
	if hex.length() % 2 != 0:
		return out
	for i in range(0, hex.length(), 2):
		var hi := _HEX_DIGITS.find(hex[i])
		var lo := _HEX_DIGITS.find(hex[i + 1])
		if hi < 0 or lo < 0:
			return PackedByteArray()
		out.append(hi << 4 | lo)
	return out


## SHA-256 摘要（Crypto.hash 非跨版本 API，用 HashingContext 实现）
func _sha256(data: PackedByteArray) -> PackedByteArray:
	var ctx := HashingContext.new()
	ctx.start(HashingContext.HASH_SHA256)
	ctx.update(data)
	return ctx.finish()


func _valid_password(password: String) -> bool:
	return password.length() >= PASSWORD_MIN and password.length() <= PASSWORD_MAX


# ---------------- 用户 API ----------------


## 注册：拒绝保留名（B7-7：_leaderboard 与 Guest）与长度不合规；成功写入并落盘。
func create_user(name: String, password: String) -> bool:
	if not _valid_name(name) or not _valid_password(password):
		return false
	_ensure_loaded()
	var users: Dictionary = _db.get("_users", {})
	if users.has(name) or RESERVED_NAMES.has(name):
		return false
	var salt := _crypto.generate_random_bytes(16)
	users[name] = {
		"password": _hex_encode(_derive(password, salt, iterations)),
		"salt": _hex_encode(salt),
		"iterations": iterations,
		"high_score": 0,
		"total_kills": 0,
		"games_played": 0,
		"last_login_order": 0,
		"settings": {},
	}
	return _save_db()


func verify_user(name: String, password: String) -> bool:
	_ensure_loaded()
	if not _db.get("_users", {}).has(name):
		return false
	var rec := _user_record(name)
	if rec.is_empty():
		return false  # 条目非 Dictionary（手改）：按用户不存在处理（不刷运行期错误）
	var salt := _hex_decode(String(rec.get("salt", "")))
	var iter := int(rec.get("iterations", iterations))
	var derived := _derive(password, salt, iter)
	# 常量时间比对（B4）：不区分"用户不存在"与"密码错误"，此处统一 false
	return _crypto.constant_time_compare(derived, _hex_decode(String(rec.get("password", ""))))


func user_exists(name: String) -> bool:
	_ensure_loaded()
	return _db.get("_users", {}).has(name)


## 用户名列表：last_login_order 降序 + 名字典序（B4 last_login 为自增序号非时间戳）
func list_usernames() -> Array[String]:
	_ensure_loaded()
	var users: Dictionary = _db.get("_users", {})
	var names: Array[String] = []
	for n in users:
		names.append(String(n))
	names.sort_custom(
		func(a: String, b: String) -> bool:
			var oa := int(_user_record(a).get("last_login_order", 0))
			var ob := int(_user_record(b).get("last_login_order", 0))
			if oa != ob:
				return oa > ob
			return a < b
	)
	return names


func get_last_login_user() -> String:
	var names := list_usernames()
	return names[0] if not names.is_empty() else ""


func record_login(name: String) -> void:
	_ensure_loaded()
	var users: Dictionary = _db.get("_users", {})
	if not users.has(name):
		return
	if _user_record(name).is_empty():
		return  # 条目非 Dictionary（手改）：跳过，防索引写运行时类型错误
	var max_order := 0
	for n in users:
		max_order = maxi(max_order, int(_user_record(String(n)).get("last_login_order", 0)))
	users[name]["last_login_order"] = max_order + 1
	_save_db()


func get_user_data(name: String) -> Dictionary:
	_ensure_loaded()
	return _user_record(name).duplicate()


## 通用字段合并更新（统计类）；密码/盐/迭代数不可经此覆盖
func update_user_data(name: String, data: Dictionary) -> void:
	_ensure_loaded()
	var users: Dictionary = _db.get("_users", {})
	if not users.has(name) or _user_record(name).is_empty():
		return
	for k in data:
		if k == "password" or k == "salt" or k == "iterations":
			continue
		users[name][k] = data[k]
	_save_db()


## 仅更高才写（B4 update_high_score 语义）；分数负钳 0
func update_high_score(name: String, score: int) -> void:
	_ensure_loaded()
	var users: Dictionary = _db.get("_users", {})
	if not users.has(name) or _user_record(name).is_empty():
		return
	var clamped := maxi(score, 0)
	if clamped > int(_user_record(name).get("high_score", 0)):
		users[name]["high_score"] = clamped
		_save_db()


func get_user_settings(name: String) -> Dictionary:
	_ensure_loaded()
	return _user_record(name).get("settings", {}).duplicate()


func update_user_settings(name: String, settings: Dictionary) -> void:
	_ensure_loaded()
	var users: Dictionary = _db.get("_users", {})
	if not users.has(name) or _user_record(name).is_empty():
		return
	var merged: Dictionary = _user_record(name).get("settings", {}).duplicate()
	for k in settings:
		merged[k] = settings[k]
	users[name]["settings"] = merged
	_save_db()


## 删除用户（先验密）；连带清理该用户存档文件（B7-12：同名再注册不得复活旧进度）
func delete_user(name: String, password: String) -> bool:
	if not verify_user(name, password):
		return false
	var users: Dictionary = _db.get("_users", {})
	users.erase(name)
	var save_path := savefile_for_user(name)
	if FileAccess.file_exists(save_path):
		DirAccess.remove_absolute(save_path)
	# 2026-08-06 审计：连带清理损坏隔离备份（原遗留 <save>.corrupt 磁盘残留）
	if FileAccess.file_exists(save_path + ".corrupt"):
		DirAccess.remove_absolute(save_path + ".corrupt")
	return _save_db()


## 每用户存档路径：user://savegame_<sanitized>_<sha256[:12]>.json（B5，对齐原作 _save_file_for_user）
func savefile_for_user(name: String) -> String:
	var sanitized := ""
	for ch in name.to_lower():
		if ch in "abcdefghijklmnopqrstuvwxyz0123456789":
			sanitized += ch
	if sanitized.is_empty():
		sanitized = "user"
	var digest := _hex_encode(_sha256(name.to_utf8_buffer())).substr(0, 12)
	return "user://savegame_%s_%s.json" % [sanitized, digest]


# ---------------- 本地排行榜 ----------------


## 提交成绩：score 负钳 0（≤0 不入榜，对齐现 GameState 高分榜语义）；cap 10；
## 排序 score 降序 + 提交序（seq 自增，先到先得——同一秒内 timestamp 无区分度）；返回 1-indexed 名次，0 = 未上榜。
func submit_score(name: String, score: int) -> int:
	if score <= 0:
		return 0
	_ensure_loaded()
	var board: Array = _db.get("_leaderboard", [])
	var seq := int(_db.get("_seq", 0)) + 1
	_db["_seq"] = seq
	var entry := {
		"player_name": name.substr(0, PLAYER_NAME_MAX),
		"score": maxi(score, 0),
		"seq": seq,
		"timestamp": Time.get_datetime_string_from_system(true),
	}
	board.append(entry)
	_sort_board(board)
	if board.size() > LEADERBOARD_CAP:
		board.resize(LEADERBOARD_CAP)
	var rank := 0
	for k in board.size():
		if board[k] == entry:
			rank = k + 1
			break
	_db["_leaderboard"] = board
	if not _save_db():
		return 0
	return rank


func get_leaderboard() -> Array:
	_ensure_loaded()
	# Q20（2026-08-05）：条目判型——手改 users.json 的非 Dictionary 条目/非数字 score 跳过
	#（原实现原样返回，渲染与排序时类型错误崩溃/字符串 score 静默转 0）
	var board: Array = []
	for entry: Variant in _db.get("_leaderboard", []):
		if not (entry is Dictionary):
			continue
		var score: Variant = (entry as Dictionary).get("score", 0)
		if not (score is int or score is float):
			continue
		board.append(entry)
	_sort_board(board)
	return board


func _sort_board(board: Array) -> void:
	board.sort_custom(
		func(a: Variant, b: Variant) -> bool:
			# Q20：排序回调防御（get_leaderboard 已过滤，submit_score 内部数组全合法——
			# 此处兜底手改数据在排序时不再报类型错误）
			if not (a is Dictionary and b is Dictionary):
				return false
			var sa := int((a as Dictionary).get("score", 0))
			var sb := int((b as Dictionary).get("score", 0))
			if sa != sb:
				return sa > sb
			return int((a as Dictionary).get("seq", 0)) < int((b as Dictionary).get("seq", 0))
	)
