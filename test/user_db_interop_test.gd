extends Node
## P0-2 断言场景：UserDbInterop（C# 绑定壳）——注册/验密/登录序/排行榜/删号，
## 并含「存量 GDScript 账号固定向量验密」（引擎环境内证明密码派生逐字节等价）与
## GDScript UserDB 壳的生产转发路径。只操作 user:// 文件；M6 快照范式备份/还原。

var _failures: int = 0
const UDB := preload("res://csharp/godot/UserDB.cs")
var _file_backups: Dictionary = {}


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


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
	for p in ["user://users.json", "user://users.json.corrupt"]:
		if FileAccess.file_exists(p):
			DirAccess.remove_absolute(p)


func _ready() -> void:
	_backup_user_files()
	_cleanup()

	# 0. C# 绑定壳可加载
	var cls: Variant = load("res://csharp/godot/UserDbInterop.cs")
	_check(cls != null, "UserDbInterop 脚本资源可加载")
	var interop = cls.new()

	# 1. 存量 GDScript 账号固定向量验密（哈希由迁移前 GDScript _derive 生成：
	#    alice/s3cret/盐 deadbeef…/1000 次迭代）
	var fixture := FileAccess.open("user://users.json", FileAccess.WRITE)
	(
		fixture
		. store_string(
			(
				JSON
				. stringify(
					{
						"_users":
						{
							"alice":
							{
								"password": "76a4bc7172651d55d0139b2622619de3ce63d54dec94d0598021db6ad0b9d03d",
								"salt": "deadbeefdeadbeefdeadbeefdeadbeef",
								"iterations": 1000,
								"high_score": 0,
								"last_login_order": 0,
								"settings": {},
							}
						},
						"_leaderboard": [],
						"_seq": 0,
					}
				)
			)
		)
	)
	fixture.close()
	_check(interop.call("VerifyUser", "alice", "s3cret", 1000) == true, "存量 GDScript 账号验密通过（固定向量）")
	_check(interop.call("VerifyUser", "alice", "wrong", 1000) == false, "存量账号错误密码验密失败")

	# 2. 注册/存在性/降档迭代数
	_check(interop.call("CreateUser", "bob", "pass123", 1000) == true, "注册 bob 成功")
	_check(interop.call("UserExists", "alice") == true and interop.call("UserExists", "bob") == true, "user_exists 命中")
	_check(interop.call("CreateUser", "bob", "other", 1000) == false, "重名注册被拒绝")
	_check(interop.call("VerifyUser", "bob", "pass123", 1000) == true, "新注册用户验密通过")

	# 3. 登录序（last_login_order 降序）
	interop.call("RecordLogin", "bob")
	interop.call("RecordLogin", "alice")
	_check(String(interop.call("GetLastLoginUser")) == "alice", "最近登录用户为 alice")
	interop.call("RecordLogin", "bob")
	_check(String(interop.call("GetLastLoginUser")) == "bob", "record_login 推进排序")

	# 4. 统计与设置
	_check(int((interop.call("GetUserData", "alice") as Dictionary).get("high_score", -1)) == 0, "初始最高分 0")
	interop.call("UpdateHighScore", "alice", 150)
	_check(int((interop.call("GetUserData", "alice") as Dictionary).get("high_score", -1)) == 150, "update_high_score 写入")
	interop.call("UpdateUserSettings", "alice", {"difficulty": &"hard"})
	_check((interop.call("GetUserSettings", "alice") as Dictionary).get("difficulty") == "hard", "设置合并（StringName → string）")

	# 5. 排行榜：排序/名次/cap
	_check(interop.call("SubmitScore", "alice", 100) == 1, "排行榜：首条排第 1")
	_check(interop.call("SubmitScore", "bob", 50) == 2, "排行榜：低分排第 2")
	_check(interop.call("SubmitScore", "alice", 100) == 2, "排行榜：同分新条目排后")
	var board: Array = interop.call("GetLeaderboard")
	_check(board.size() == 3, "排行榜：条目数正确")
	_check(int((board[0] as Dictionary).get("score", -1)) == 100, "排行榜：榜首为最高分")
	_check(String((board[0] as Dictionary).get("player_name", "")) == "alice", "排行榜：榜首玩家名正确")

	# 6. 每用户存档文件名（B5 路径语义）
	var alice_save: String = String(interop.call("SaveFileName", "alice"))
	_check(alice_save.begins_with("savegame_alice_") and alice_save.ends_with(".json"), "存档文件名含清洗后用户名")
	_check(String(interop.call("SaveFileName", "Alice")) != alice_save, "大小写不同的用户路径不同")

	# 7. 删号：验密 + 连带清理存档文件（B7-12）
	_check(interop.call("DeleteUser", "bob", "wrongpw") == false, "删号错误密码被拒")
	var bob_save := FileAccess.open("user://" + String(interop.call("SaveFileName", "bob")), FileAccess.WRITE)
	bob_save.store_string("{}")
	bob_save.close()
	_check(interop.call("DeleteUser", "bob", "pass123") == true, "删号正确密码成功")
	_check(interop.call("UserExists", "bob") == false, "删号后用户消失")
	_check(not FileAccess.file_exists("user://" + String(interop.call("SaveFileName", "bob"))), "删号连带删除该用户存档文件")

	# 8. 持久化往返（Reload 重读磁盘）
	interop.call("Reload")
	_check(interop.call("UserExists", "alice") == true, "Reload 后用户保留")
	_check(interop.call("VerifyUser", "alice", "s3cret", 1000) == true, "Reload 后验密一致")

	# 9. GDScript UserDB 壳（生产转发路径：GameState/welcome 同款调用）
	var udb = UDB.new()
	udb.iterations = 1000
	_check(udb.user_exists("alice"), "GDScript 壳 user_exists 转发")
	_check(udb.verify_user("alice", "s3cret"), "GDScript 壳验密转发（迭代数降档参数透传）")
	_check(udb.get_leaderboard().size() == 3, "GDScript 壳排行榜转发（榜单与用户表独立，删号不清理条目）")
	_check(udb.savefile_for_user("alice").begins_with("user://savegame_alice_"), "GDScript 壳存档路径转发")

	_cleanup()
	_restore_user_files()
	print("USER DB INTEROP TEST DONE, failures = ", _failures)
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
