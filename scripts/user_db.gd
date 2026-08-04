class_name UserDB
extends RefCounted
## 本地用户数据库（2026-08-04 账户系统；规格 docs/2026-08-04-local-accounts-plan.md + PORTING_PARITY 附录 B）。
## 单文件 user://users.json 共存用户表与本地排行榜；密码 PBKDF2-HMAC-SHA256（盐 16B hex，
## 迭代数随用户记录存档，注册时确定——测试可降档加速）；原子写 + 损坏隔离复用 SaveManager。
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
	_db = data if data.size() > 0 else {"_users": {}, "_leaderboard": []}


func _save_db() -> bool:
	return _save.save(USERS_PATH, _db)


# ---------------- PBKDF2 ----------------


## PBKDF2-HMAC-SHA256：U1 = PRF(P, S || INT_32_BE(i))，后续 Ui = PRF(P, U(i-1))，T = XOR 全部分块。
## 派生长度固定 32 字节（SHA-256 输出 = 单块，无拼接循环）。
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
	for i in range(0, hex.length(), 2):
		out.append(_HEX_DIGITS.find(hex[i]) << 4 | _HEX_DIGITS.find(hex[i + 1]))
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
	var users: Dictionary = _db.get("_users", {})
	if not users.has(name):
		return false
	var rec: Dictionary = users[name]
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
			var oa := int(users[a].get("last_login_order", 0))
			var ob := int(users[b].get("last_login_order", 0))
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
	var max_order := 0
	for n in users:
		max_order = maxi(max_order, int(users[n].get("last_login_order", 0)))
	users[name]["last_login_order"] = max_order + 1
	_save_db()


func get_user_data(name: String) -> Dictionary:
	_ensure_loaded()
	var rec: Dictionary = _db.get("_users", {}).get(name, {})
	return rec.duplicate()


## 通用字段合并更新（统计类）；密码/盐/迭代数不可经此覆盖
func update_user_data(name: String, data: Dictionary) -> void:
	_ensure_loaded()
	var users: Dictionary = _db.get("_users", {})
	if not users.has(name):
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
	if not users.has(name):
		return
	var clamped := maxi(score, 0)
	if clamped > int(users[name].get("high_score", 0)):
		users[name]["high_score"] = clamped
		_save_db()


func get_user_settings(name: String) -> Dictionary:
	_ensure_loaded()
	return _db.get("_users", {}).get(name, {}).get("settings", {}).duplicate()


func update_user_settings(name: String, settings: Dictionary) -> void:
	_ensure_loaded()
	var users: Dictionary = _db.get("_users", {})
	if not users.has(name):
		return
	var merged: Dictionary = users[name].get("settings", {}).duplicate()
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
	var board: Array = _db.get("_leaderboard", []).duplicate()
	_sort_board(board)
	return board


func _sort_board(board: Array) -> void:
	board.sort_custom(
		func(a: Dictionary, b: Dictionary) -> bool:
			var sa := int(a.get("score", 0))
			var sb := int(b.get("score", 0))
			if sa != sb:
				return sa > sb
			return int(a.get("seq", 0)) < int(b.get("seq", 0))
	)
