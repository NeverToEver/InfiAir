extends Node
## 用户会话测试（2026-08-04 账户系统 T2）：profile.json 迁移合并、登录/游客/登出会话、
## 每用户存档隔离与档主校验、排行榜/统计按会话路由。只操作 GameState autoload 与 user:// 文件。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 清空 user:// 全部游戏文件（含每用户存档与隔离备份），保证测试确定性
func _wipe_user_files() -> void:
	for f in [
		"user://profile.json",
		"user://profile.json.corrupt",
		"user://savegame.json",
		"user://savegame.json.corrupt",
		"user://users.json",
		"user://users.json.corrupt",
	]:
		if FileAccess.file_exists(f):
			DirAccess.remove_absolute(f)
	var dir := DirAccess.open("user://")
	if dir == null:
		return
	dir.list_dir_begin()
	var name := dir.get_next()
	while name != "":
		if name.begins_with("savegame_") or name.ends_with(".corrupt"):
			DirAccess.remove_absolute("user://" + name)
		name = dir.get_next()
	dir.list_dir_end()


func _reset_members() -> void:
	GameState.high_score = 0
	GameState.tutorial_done = false
	GameState.difficulty = &"medium"
	GameState.locale = "zh"
	GameState.view_zoom = &"small"
	GameState.window_size = &"large"
	GameState.aim_assist_level = &"medium"
	GameState.ctrl_toggle_mode = false
	GameState.shift_toggle_mode = false
	GameState.reduce_flash = false
	GameState.mouse_lock = true
	GameState.highscores.clear()
	GameState.key_bindings.clear()
	GameState._pending_legacy_profile = {}


func _ready() -> void:
	GameState.logout_user()
	_wipe_user_files()
	_reset_members()
	# 1. profile.json 退役迁移：存在旧档案且无用户 → 首个注册用户合并后删除
	var legacy := FileAccess.open("user://profile.json", FileAccess.WRITE)
	(
		legacy
		. store_string(
			(
				JSON
				. stringify(
					{
						"version": 2,
						"high_score": 5000,
						"difficulty": "hard",
						"locale": "en",
						"tutorial_done": true,
						"view_zoom": "large",
						"window_size": "large",
						"aim_assist": "medium",
						"reduce_flash": false,
						"mouse_lock": true,
						"key_bindings": {},
						"highscores": [],
					}
				)
			)
		)
	)
	legacy.close()
	GameState._maybe_migrate_legacy_profile()
	_check(not GameState._pending_legacy_profile.is_empty(), "迁移缓存旧 profile")
	_check(GameState.create_user("migrator", "pass123"), "迁移后注册用户成功")
	_check(not FileAccess.file_exists("user://profile.json"), "迁移后 profile.json 删除")
	var migrator_settings := GameState.get_user_settings("migrator")
	_check(migrator_settings.get("difficulty") == &"hard", "迁移：难度并入新用户设置")
	_check(migrator_settings.get("locale") == "en", "迁移：locale 并入新用户设置")
	_check(migrator_settings.get("tutorial_done") == true, "迁移：教程标记并入新用户设置")
	_check(int(GameState.get_user_data("migrator").get("high_score", 0)) == 5000, "迁移：最高分并入新用户统计")
	_check(GameState._pending_legacy_profile.is_empty(), "迁移后缓存清空")

	# 2. 登录会话：载入用户设置并即时生效（B7-11 locale）
	_check(GameState.create_user("alice", "pass123"), "注册 alice")
	GameState.update_user_settings("alice", {"difficulty": &"hard", "locale": "en"})
	GameState.login_user("alice")
	_check(GameState.current_user == "alice", "登录置位当前用户")
	_check(GameState.difficulty == &"hard", "登录载入用户难度")
	_check(GameState.locale == "en" and TranslationServer.get_locale() == "en", "登录 locale 即时生效")
	GameState.set_locale("zh")  # 复位语言（设置写入 alice 档案）
	_check(GameState.is_guest() == false, "登录态非游客")
	# 设置变更经成员落盘：登出写档案 → 重登载入
	GameState.difficulty = &"easy"
	GameState.logout_user()
	GameState.login_user("alice")
	_check(GameState.difficulty == &"easy", "登出重登载入最新设置")
	_check(GameState.current_user == "alice", "重登后会话正确")

	# 3. 游客会话：仅内存、不落盘、不入表
	GameState.login_guest()
	_check(GameState.is_guest() and GameState.current_user == "Guest", "游客会话置位")
	GameState.save_profile()
	_check(not FileAccess.file_exists("user://profile.json"), "游客 save_profile 不落盘")
	_check(not GameState.user_exists("Guest"), "游客不入用户表")
	GameState.logout_user()
	_check(GameState.current_user == "", "登出复位未登录")

	# 4. 每用户存档隔离：登录写用户路径；档主不匹配隔离；游客不存档；未登录走旧路径
	GameState.login_user("alice")
	GameState.save_run(50.0, 10.0)
	var save_path := GameState.user_db_savefile_for("alice")
	_check(FileAccess.file_exists(save_path), "登录用户存档写入每用户路径")
	_check(GameState.has_save(), "登录用户 has_save 命中用户档")
	_check(String(GameState.load_run_data().get("username", "")) == "alice", "存档含档主用户名")
	# 手改档主 → 隔离备份按无存档处理（B5）
	var tampered := FileAccess.open(save_path, FileAccess.WRITE)
	tampered.store_string(JSON.stringify({"username": "bob", "score": 1}))
	tampered.close()
	_check(GameState.load_run_data().is_empty() and GameState.save_corrupt, "档主不匹配按无存档处理")
	_check(FileAccess.file_exists(save_path + ".corrupt"), "不匹配档隔离备份")
	GameState.delete_save()
	_check(not GameState.has_save(), "登录用户 delete_save 清除用户档")
	GameState.login_guest()
	GameState.save_run(50.0, 10.0)
	_check(not GameState.has_save(), "游客不存档、has_save 恒 false")
	GameState.logout_user()
	GameState.save_run(50.0, 10.0)
	_check(GameState.has_save(), "未登录存档走旧单文件路径（兼容）")
	GameState.delete_save()
	_check(not FileAccess.file_exists("user://savegame.json"), "未登录 delete_save 清除旧档")

	# 5. 排行榜与统计按会话路由
	GameState.login_user("alice")
	_check(GameState.submit_highscore(100) == 1, "登录用户成绩入 user_db 榜")
	_check(GameState.highscores_text(3) == "1. 100", "登录用户榜单文本走 user_db")
	GameState.score = 200
	_check(GameState.record_score(), "登录用户破纪录")
	_check(int(GameState.get_user_data("alice").get("high_score", 0)) == 200, "登录用户纪录写入 user_db")
	GameState.score = 0
	GameState.login_guest()
	GameState.high_score = 0
	GameState.score = 300
	_check(GameState.record_score(), "游客破纪录（仅内存）")
	_check(GameState.high_score == 300, "游客纪录仅存内存")
	_check(int(GameState.get_user_data("alice").get("high_score", 0)) == 200, "游客纪录不落盘")
	# 排行榜条目：alice 榜上为 100（record_score 只写最高分统计，不入榜）→ Guest 150 应排第 1
	_check(GameState.submit_highscore(150) == 1, "游客以 Guest 提交入榜")
	var board := GameState.get_leaderboard()
	_check(String(board[0]["player_name"]) == "Guest" and int(board[0]["score"]) == 150, "榜首为 Guest（150 高于 alice 100）")
	_check(String(board[1]["player_name"]) == "alice", "次席为 alice")

	print("USER SESSION TEST DONE, failures = ", _failures)
	GameState.logout_user()
	GameState.delete_save()
	_wipe_user_files()
	get_tree().quit(_failures)
