extends Node
## 本地账户数据层测试（2026-08-04 账户系统 T1）：注册/验密/保留名/排序/统计/
## 删号连档清理/排行榜 cap 与名次/损坏隔离重置。只操作 user:// 文件，不加载 main 场景。

var _failures: int = 0
const UDB := preload("res://csharp/godot/UserDB.cs")
var _db


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## M6（2026-08-06 审计）：Q23 快照范式补漏——原 _cleanup 直接删除 user://users.json
## 且无还原，本地跑一次即永久销毁开发者全部账户与用户排行榜；开头备份、结尾还原
var _file_backups: Dictionary = {}


func _backup_user_files() -> void:
	_file_backups = {}
	var files := ["user://users.json", "user://users.json.corrupt"]
	var dir := DirAccess.open("user://")
	if dir != null:
		dir.list_dir_begin()
		var name := dir.get_next()
		while name != "":
			if name.begins_with("savegame_") or name.ends_with(".corrupt"):
				files.append("user://" + name)
			name = dir.get_next()
		dir.list_dir_end()
	for f in files:
		var exists := FileAccess.file_exists(f)
		_file_backups[f] = {"exists": exists, "content": FileAccess.get_file_as_string(f) if exists else ""}


func _restore_user_files() -> void:
	for f in _file_backups:
		var b: Dictionary = _file_backups[f]
		if b["exists"]:
			var fh := FileAccess.open(f, FileAccess.WRITE)
			fh.store_string(b["content"])
			fh.close()
		elif FileAccess.file_exists(f):
			DirAccess.remove_absolute(f)


func _cleanup() -> void:
	var paths := ["user://users.json", "user://users.json.corrupt"]
	for p in paths:
		if FileAccess.file_exists(p):
			DirAccess.remove_absolute(p)


func _ready() -> void:
	_backup_user_files()  # M6：快照须在任何删除/覆写之前（_cleanup 会删 users.json）
	_cleanup()
	_db = UDB.new()
	_db.iterations = 1000  # 测试降档加速（生产 100_000）

	# 1. 注册 / 验密 / 存在性
	_check(_db.create_user("alice", "s3cret"), "注册 alice 成功")
	_check(_db.create_user("bob", "pass123"), "注册 bob 成功")
	_check(not _db.create_user("alice", "other"), "重名注册被拒绝")
	_check(_db.user_exists("alice") and _db.user_exists("bob"), "user_exists 命中")
	_check(_db.verify_user("alice", "s3cret"), "正确密码验密通过")
	_check(not _db.verify_user("alice", "wrong"), "错误密码验密失败")
	_check(not _db.verify_user("nobody", "s3cret"), "不存在用户验密失败")

	# 2. 长度与保留名约束（B3 16 上限 / B7-7 保留名）
	_check(not _db.create_user("ab", "pass123"), "用户名 <3 拒绝")
	_check(not _db.create_user("a".repeat(17), "pass123"), "用户名 >16 拒绝")
	_check(not _db.create_user("carol", "pw"), "密码 <3 拒绝")
	_check(not _db.create_user("carol", "p".repeat(17)), "密码 >16 拒绝")
	_check(not _db.create_user("_leaderboard", "pass123"), "保留名 _leaderboard 拒绝")
	_check(not _db.create_user("Guest", "pass123"), "保留名 Guest 拒绝")
	_check(not _db.user_exists("_leaderboard"), "保留名未入库")

	# 3. last_login 排序：序号降序 + 名字典序
	_db.record_login("bob")
	_db.record_login("alice")
	var names = _db.list_usernames()
	_check(names == ["alice", "bob"], "list_usernames 按最近登录降序")
	_check(_db.get_last_login_user() == "alice", "get_last_login_user 取最近登录")
	_db.record_login("bob")
	_check(_db.list_usernames()[0] == "bob", "record_login 推进排序")

	# 4. 统计更新：合并写 / 最高分仅更高才写
	_check(_db.get_user_data("alice")["high_score"] == 0, "初始最高分 0")
	_db.update_high_score("alice", 100)
	_db.update_high_score("alice", 50)
	_check(int(_db.get_user_data("alice")["high_score"]) == 100, "update_high_score 仅更高才写")
	_db.update_user_data("alice", {"total_kills": 5})
	_db.update_user_data("alice", {"total_kills": 9})
	_check(int(_db.get_user_data("alice")["total_kills"]) == 9, "update_user_data 合并累加")
	_db.update_user_data("alice", {"password": "hacked"})
	_check(_db.verify_user("alice", "s3cret"), "update_user_data 不可覆盖密码")

	# 5. 设置隔离
	_db.update_user_settings("alice", {"difficulty": &"hard"})
	_db.update_user_settings("alice", {"locale": "en"})
	var settings = _db.get_user_settings("alice")
	_check(settings.get("difficulty") == &"hard" and settings.get("locale") == "en", "update_user_settings 合并")
	_check(_db.get_user_settings("bob").is_empty(), "用户设置互不泄漏")

	# 6. 每用户存档路径
	var alice_save = _db.savefile_for_user("alice")
	_check(alice_save.begins_with("user://savegame_alice_"), "存档路径含清洗后用户名")
	_check(alice_save.ends_with(".json") and alice_save.length() == len("user://savegame_alice_.json") + 12, "存档路径含 sha256[:12]")
	_check(_db.savefile_for_user("Alice") != alice_save, "大小写不同的用户路径不同")
	_check(_db.savefile_for_user("@@@").begins_with("user://savegame_user_"), "纯符号用户名回退 user 前缀")

	# 7. 删号：验密 + 连带清理存档（B7-12）
	_check(not _db.delete_user("bob", "wrongpw"), "删号错误密码被拒")
	var bob_save = _db.savefile_for_user("bob")
	var f := FileAccess.open(bob_save, FileAccess.WRITE)
	f.store_string("{}")
	f.close()
	_check(_db.delete_user("bob", "pass123"), "删号正确密码成功")
	_check(not _db.user_exists("bob"), "删号后用户消失")
	_check(not FileAccess.file_exists(bob_save), "删号连带删除该用户存档文件")
	_check(not _db.verify_user("bob", "pass123"), "删号后验密失败")

	# 8. 排行榜：0 分不入榜 / 排序 / 同分后 / cap / 名次
	_check(_db.submit_score("alice", 0) == 0, "排行榜：0 分不入榜")
	_check(_db.submit_score("alice", 100) == 1, "排行榜：首条排第 1")
	_check(_db.submit_score("bob", 50) == 2, "排行榜：低分排第 2")
	_check(_db.submit_score("alice", 80) == 2, "排行榜：中间分插入第 2")
	_check(_db.submit_score("alice", 100) == 2, "排行榜：同分新条目排后")
	_check(_db.submit_score("carol", -5) == 0, "排行榜：负分不入榜")
	var board = _db.get_leaderboard()
	_check(board.size() == 4, "排行榜：条目数正确")
	_check(int(board[0]["score"]) == 100 and int(board[1]["score"]) == 100, "排行榜：榜首与同分先到先得")
	_check(String(board[0]["player_name"]) == "alice", "排行榜：榜首玩家名正确")
	for i in range(100):
		_db.submit_score("alice", 200 - i)
	board = _db.get_leaderboard()
	_check(board.size() == UDB.GetLeaderboardCap(), "排行榜：上限截断")
	_check(int(board[0]["score"]) == 200, "排行榜：截断后榜首不变")
	_check(_db.submit_score("alice", 1) == 0, "排行榜：超出上限的分数不入榜")

	# 9. 持久化往返：重载后数据一致
	_db = UDB.new()
	_db.iterations = 1000
	_check(_db.user_exists("alice"), "持久化往返：用户保留")
	_check(_db.verify_user("alice", "s3cret"), "持久化往返：验密一致")
	board = _db.get_leaderboard()
	_check(board.size() == UDB.GetLeaderboardCap() and int(board[0]["score"]) == 200, "持久化往返：榜单一致")

	# 10. 损坏隔离重置（B4：备份 .corrupt + 按空库处理）
	_cleanup()
	var corrupted := FileAccess.open("user://users.json", FileAccess.WRITE)
	corrupted.store_string("{not valid json")
	corrupted.close()
	_db = UDB.new()
	_db.iterations = 1000
	_check(_db.create_user("dave", "pass123"), "损坏后按空库重建可注册")
	_check(FileAccess.file_exists("user://users.json.corrupt"), "损坏文件隔离为 .corrupt 备份")
	_check(not _db.user_exists("alice"), "损坏库不残留旧用户")
	_check(_db.get_leaderboard().is_empty(), "损坏库不残留旧榜单")

	# 11. Q17/Q18/Q20（2026-08-05）：结构守卫 / hex 校验 / 榜单判型
	_cleanup()
	_db = UDB.new()
	_db.iterations = 1000
	_check(_db.create_user("alice", "s3cret"), "Q17：重建后注册 alice")
	_check(_db.submit_score("alice", 100) == 1, "Q17：提交榜单条目")
	# 用户表非 Dictionary → 空库重建不崩溃（原 users.has() 运行时错误）
	var q17_fh := FileAccess.open("user://users.json", FileAccess.WRITE)
	q17_fh.store_string(JSON.stringify({"_users": "not-a-dict", "_leaderboard": [1, 2]}))
	q17_fh.close()
	_db = UDB.new()
	_db.iterations = 1000
	_check(not _db.user_exists("alice"), "Q17：用户表非 Dictionary → 空库重建（不崩溃）")
	_check(_db.get_leaderboard().is_empty(), "Q17：非法榜单结构重建为空")
	_check(_db.create_user("bob", "pass123"), "Q17：重建后可注册")
	# 榜单条目判型：非 Dictionary 条目/字符串 score 跳过（原渲染与排序崩溃/静默转 0）
	var q20_fh := FileAccess.open("user://users.json", FileAccess.WRITE)
	(
		q20_fh
		. store_string(
			(
				JSON
				. stringify(
					{
						"_users": {"bob": {"last_login_order": 0}},
						"_leaderboard":
						[{"player_name": "bob", "score": 50, "seq": 1}, "junk", {"player_name": "bad", "score": "100", "seq": 2}],
					}
				)
			)
		)
	)
	q20_fh.close()
	_db = UDB.new()
	_db.iterations = 1000
	var q20_board = _db.get_leaderboard()
	_check(q20_board.size() == 1 and int(q20_board[0]["score"]) == 50, "Q20：非 Dictionary/字符串 score 条目被过滤（保留 1 条）")
	# Q18：手改奇数长度/非法 hex salt → 验密安全失败（原 hex[i+1] 越界 / -1 append 崩溃）
	var q18_fh := FileAccess.open("user://users.json", FileAccess.WRITE)
	q18_fh.store_string(JSON.stringify({"_users": {"bob": {"password": "00", "salt": "abc", "iterations": 1000, "last_login_order": 0}}}))
	q18_fh.close()
	_db = UDB.new()
	_db.iterations = 1000
	_check(not _db.verify_user("bob", "pass123"), "Q18：奇数长度 salt 验密安全失败（无越界崩溃）")
	q18_fh = FileAccess.open("user://users.json", FileAccess.WRITE)
	q18_fh.store_string(JSON.stringify({"_users": {"bob": {"password": "zz", "salt": "zz", "iterations": 1000, "last_login_order": 0}}}))
	q18_fh.close()
	_db = UDB.new()
	_db.iterations = 1000
	_check(not _db.verify_user("bob", "pass123"), "Q18：非法 hex 盐/密文验密安全失败（不 append -1）")

	print("USER DB TEST DONE, failures = ", _failures)
	_cleanup()
	_restore_user_files()  # M6：还原开发者原始用户表/排行榜/存档
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
