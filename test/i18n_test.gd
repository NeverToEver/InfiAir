extends Node
## i18n 测试：locale 切换生效、10 个 key 中英对照、profile 往返、HUD 英文刷新、缺 key 回退。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	GameState.delete_save()
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.set_locale("zh")

	# 1. 默认中文
	_check(GameState.locale == "zh", "默认语言 zh")
	_check(tr("UI_SCORE") == "分数：%d", "zh 列生效")

	# 2. 切换英文
	GameState.set_locale("en")
	_check(GameState.locale == "en", "set_locale 切换 en")
	_check(tr("UI_SCORE") == "Score: %d", "en 列生效")

	# 3. 抽查 10 个 key 中英对照（均非 key 本身且互不相同）
	var keys := [
		"UI_SCORE",
		"BUFF_POWER_SHOT_NAME",
		"BASE_TITLE",
		"GO_TITLE",
		"PAUSE_TITLE",
		"START_CONTINUE",
		"SET_CONTROLS",
		"ACT_DASH",
		"TUT_S1_TITLE",
		"WARN_BOSS",
	]
	var all_ok := true
	for k in keys:
		GameState.set_locale("zh")
		var zh_text := tr(k)
		GameState.set_locale("en")
		var en_text := tr(k)
		if zh_text == k or en_text == k or zh_text == en_text:
			all_ok = false
			printerr("  key 异常: ", k, " zh=", zh_text, " en=", en_text)
	_check(all_ok, "10 个 key 中英对照齐全")

	# 4. profile 往返
	GameState.set_locale("en")
	GameState.locale = "zh"  # 内存改回，验证读档覆盖
	GameState.load_profile()
	_check(GameState.locale == "en", "locale 从 profile 恢复 en")
	TranslationServer.set_locale(GameState.locale)

	# 5. HUD 刷新：en 下分数标签显示 Score
	add_child(load("res://scenes/main.tscn").instantiate())
	await get_tree().process_frame
	await get_tree().process_frame
	var score_label: Label = get_node("Main/HUD/ScoreLabel")
	_check(score_label.text.begins_with("Score"), "HUD 分数标签 en 刷新")
	GameState.set_locale("zh")
	_check(score_label.text.begins_with("分数"), "HUD 分数标签切回 zh 刷新")

	# 6. 缺失 key 回退 key 名本身
	_check(tr("I18N_NO_SUCH_KEY") == "I18N_NO_SUCH_KEY", "缺失 key 回退 key 名")

	# 收尾：恢复 zh 并落盘
	GameState.set_locale("zh")

	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("I18N TEST DONE, failures = ", _failures)
	get_tree().quit(_failures)
