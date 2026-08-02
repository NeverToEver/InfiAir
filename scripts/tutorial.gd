extends Node2D
## 新手教程（对齐原作 6 阶段）：独立场景，脚本驱动检查点，复用现有实体。
## 不启动正常 spawner 波次；进场 reset_run + 删档隔离，出场再 reset 并保证 time_scale=1。

const FONT: FontFile = preload("res://assets/fonts/NotoSansSC.ttf")
const ENEMY_SCENE: PackedScene = preload("res://scenes/enemy.tscn")
const BOSS_SCENE: PackedScene = preload("res://scenes/boss.tscn")
const MOTHERSHIP_SCENE: PackedScene = preload("res://scenes/mothership.tscn")
const SPAWNER_SCRIPT: GDScript = preload("res://scripts/spawner.gd")
var HOME_CHARGE_TIME := 1.5
var DOCK_CHARGE_TIME := 3.0  # 母舰召唤蓄力（mothership.dock_charge_time，对齐正局）

const STAGE_TITLES: Array[String] = [
	"TUT_S1_TITLE",
	"TUT_S2_TITLE",
	"TUT_S3_TITLE",
	"TUT_S4_TITLE",
	"TUT_S5_TITLE",
	"TUT_S6_TITLE",
]

var _stage: int = 0
var _advancing: bool = false
var _stage_kills: int = 0
var _boost_count: int = 0
var _dash_count: int = 0
var _prev_dashing: bool = false
var _home_charge: float = 0.0
var _dock_charge: float = 0.0
var _max_hp: float = 100.0  # G05：阶段 2 锁血每物理帧用，_ready 缓存一次（教程内 buffs 不变）
var _base_ui: CanvasLayer = null
var _boss: Boss = null
var _mothership: Mothership = null
var _finished: bool = false
var _failed: bool = false

var _title_label: Label
var _objective_label: Label
var _objective_key: String = ""
var _objective_args: Array = []
var _complete_panel: PanelContainer
var _hud_layer: CanvasLayer

@onready var _player: Player = $Player


## A7：测试/诊断白盒断言经公开接口
func stage() -> int:
	return _stage


func boss() -> Boss:
	return _boss


func mothership() -> Mothership:
	return _mothership


func stage_kills() -> int:
	return _stage_kills


func boost_count() -> int:
	return _boost_count


func dash_count() -> int:
	return _dash_count


func base_ui() -> CanvasLayer:
	return _base_ui


func finished() -> bool:
	return _finished


func failed() -> bool:
	return _failed


func title_label() -> Label:
	return _title_label


func objective_label() -> Label:
	return _objective_label


func complete_panel() -> PanelContainer:
	return _complete_panel


func _ready() -> void:
	# 存档隔离：教程不读写 savegame
	GameState.delete_save()
	GameState.reset_run()
	_max_hp = GameState.max_health()  # G05：热路径缓存（阶段 2 锁血每物理帧读）
	RenderingServer.set_default_clear_color(Color(0.02, 0.02, 0.06))
	GameState.locale_changed.connect(_on_locale_changed)
	GameState.player_died.connect(_on_player_died)
	# 辅助瞄准框覆盖层：与 main.gd 同款运行时创建（登记 GameState.aim_frame_layer），
	# 教程内标记框与追踪弹行为与正局一致；随场景切换自动注销
	add_child(AimFrameLayer.new())
	_build_hud()
	HOME_CHARGE_TIME = GameState.cfg("effects.home_charge_time", HOME_CHARGE_TIME)
	DOCK_CHARGE_TIME = GameState.cfg("mothership.dock_charge_time", DOCK_CHARGE_TIME)
	_enter_stage(0)


func _build_hud() -> void:
	_hud_layer = CanvasLayer.new()
	add_child(_hud_layer)
	_title_label = Label.new()
	_title_label.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_title_label.position = Vector2(-400.0, 24.0)
	_title_label.custom_minimum_size = Vector2(800.0, 0.0)
	_title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_title_label.add_theme_font_override("font", FONT)
	_title_label.add_theme_font_size_override("font_size", 34)
	_title_label.add_theme_color_override("font_color", UITheme.ACCENT_GOLD)
	_hud_layer.add_child(_title_label)
	_objective_label = Label.new()
	_objective_label.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_objective_label.position = Vector2(-500.0, 74.0)
	_objective_label.custom_minimum_size = Vector2(1000.0, 0.0)
	_objective_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_objective_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_objective_label.add_theme_font_override("font", FONT)
	_objective_label.add_theme_font_size_override("font_size", 22)
	_objective_label.add_theme_color_override("font_color", UITheme.TEXT_DIM)
	_hud_layer.add_child(_objective_label)


func _set_objective_tr(key: String, args: Array = []) -> void:
	_objective_key = key
	_objective_args = args
	_objective_label.text = tr(key) % args if not args.is_empty() else tr(key)


func _on_locale_changed() -> void:
	_title_label.text = tr(STAGE_TITLES[_stage])
	_set_objective_tr(_objective_key, _objective_args)


func _enter_stage(idx: int) -> void:
	_stage = idx
	_stage_kills = 0
	_title_label.text = tr(STAGE_TITLES[idx])
	match idx:
		0:  # 移动与瞄准：3 个辅助瞄准标记训练靶（正常速度，对齐正局追踪弹体验）
			_set_objective_tr("TUT_S1_OBJ", [0])
			for i in 3:
				var e := _spawn_enemy(SPAWNER_SCRIPT.ENEMY_TYPES[0], &"straight")
				e.aim_marked = true  # 教学演示：强制标记（setup 已按比率掷点，此处覆盖保证确定性）
				e.position = Vector2(600.0 + 360.0 * i, 280.0)
		1:  # 加速与相位突进
			GameState.add_buff(&"phase_dash")
			_boost_count = 0
			_dash_count = 0
			_prev_dashing = false
			_update_boost_objective()
		2:  # 战斗基础：5 只 straight，锁血下限
			_set_objective_tr("TUT_S3_OBJ", [0])
			_spawn_combat_wave(5)
		3:  # 母舰召唤与停靠（对齐正局：长按 H 蓄力 → 穿梭门 → 母舰穿出 → 对接补给）
			_dock_charge = 0.0
			_set_objective_tr("TUT_S4_OBJ")
		4:  # 返航与基地
			_home_charge = 0.0
			_set_objective_tr("TUT_S5_OBJ")
		5:  # 首领遭遇：低 HP Boss-1，触发狂暴即过关
			_set_objective_tr("TUT_S6_OBJ")
			_player.set_invincible(999.0)  # 教程不判负
			_boss = BOSS_SCENE.instantiate() as Boss
			_boss.setup(1.0, 1)
			_boss.max_hp = GameState.cfg("tutorial.boss_hp", 120.0)
			_boss.hp = _boss.max_hp
			_boss.position = Vector2(960.0, -160.0)
			_boss.enraged.connect(_on_boss_enraged)
			_boss.died.connect(_on_boss_gone)
			add_child(_boss)


## 玩家死亡：教程无法推进（阶段 4/5 依赖玩家存活操作），提示失败并等待 Esc 退出
func _on_player_died() -> void:
	if _finished or _failed:
		return
	_failed = true
	_title_label.text = tr("TUT_FAIL_TITLE")
	_set_objective_tr("TUT_FAIL_DESC")


## 阶段 6 软锁兜底：Boss 未触发狂暴即被击杀/逃跑离场（died 两种离场都会发）→ 重置阶段重刷
func _on_boss_gone() -> void:
	if _stage == 5 and not _finished and not _failed:
		_enter_stage(5)


## 阶段 3 战斗波次：刷 count 只 straight（过关补刷复用同一布局）
func _spawn_combat_wave(count: int) -> void:
	for i in count:
		var e := _spawn_enemy(SPAWNER_SCRIPT.ENEMY_TYPES[0], &"straight")
		e.position = Vector2(300.0 + 330.0 * i, -60.0 - 120.0 * (i % 2))


## 场上存活敌机数（教程实体均为本节点子节点）
func _alive_enemy_count() -> int:
	var n := 0
	for child in get_children():
		if child is Enemy:
			n += 1
	return n


func _spawn_enemy(config: Dictionary, strategy: StringName) -> Enemy:
	var e := ENEMY_SCENE.instantiate() as Enemy
	e.setup(config, strategy, 1.0)
	e.can_shoot = _stage == 2  # 仅战斗阶段敌机开火
	e.position = Vector2(960.0, -60.0)
	e.died.connect(_on_enemy_died)
	add_child(e)
	return e


func _on_enemy_died(_enemy: Enemy) -> void:
	if _stage != 0 and _stage != 2:
		return
	_stage_kills += 1
	if _stage == 0:
		_set_objective_tr("TUT_S1_OBJ", [_stage_kills])
		if _stage_kills >= 3:
			_pass_stage()
	elif _stage == 2:
		_set_objective_tr("TUT_S3_OBJ", [_stage_kills])
		if _stage_kills >= 5:
			_pass_stage()


func _update_boost_objective() -> void:
	_set_objective_tr("TUT_S2_OBJ", [_boost_count, _dash_count])


## 母舰召唤（对齐 main._on_summon_window_finished 的实体路径：穿梭门 + begin_warp_in；
## 略去机库小窗演出保持教程节奏）
func _summon_mothership() -> void:
	var gate_pos := Vector2(
		GameState.view_world_rect().get_center().x, GameState.cfg("mothership.hover_y", 270.0)
	)
	var gate := WarpGate.new()
	gate.position = gate_pos
	add_child(gate)
	_mothership = MOTHERSHIP_SCENE.instantiate() as Mothership
	_mothership.begin_warp_in(gate_pos, gate)
	_mothership.departed.connect(_on_mothership_departed)
	add_child(_mothership)
	_set_objective_tr("TUT_S4_DOCK")


func _on_mothership_departed(_cooldown: float) -> void:
	if _stage == 3:
		_pass_stage()


func _on_boss_enraged() -> void:
	if _stage == 5 and not _finished:
		_boss.abort_enrage_sequence()  # 教程触发即过关：中止序列，不冻结玩家移动
		_finish()


func _pass_stage() -> void:
	if _advancing or _failed:
		return
	_advancing = true
	GameState.play_sfx(GameState.SFX_BUFF_PICK)
	# 一次性 Timer 节点 + 信号回调（AGENTS：禁止 await create_timer 协程，退出时协程状态泄漏）
	var timer := Timer.new()
	timer.one_shot = true
	timer.process_mode = Node.PROCESS_MODE_ALWAYS  # 对齐原 SceneTreeTimer 暂停树仍计时
	timer.wait_time = 1.0
	timer.timeout.connect(_finish_pass_stage)
	add_child(timer)
	timer.start()


## C01 修复：_pass_stage 的延迟推进改为 Timer 回调（原 await create_timer 在教程被释放时协程悬死）
func _finish_pass_stage() -> void:
	_advancing = false
	if _stage < STAGE_TITLES.size() - 1:
		_enter_stage(_stage + 1)


func _physics_process(delta: float) -> void:
	if _finished or _failed:
		return
	match _stage:
		1:
			# 加速/冲刺输入计数（rising edge）
			if Input.is_action_just_pressed("boost"):
				_boost_count = mini(_boost_count + 1, 2)
				_update_boost_objective()
			if _player.is_dashing() and not _prev_dashing:
				_dash_count = mini(_dash_count + 1, 2)
				_update_boost_objective()
			_prev_dashing = _player.is_dashing()
			if _boost_count >= 2 and _dash_count >= 2:
				_pass_stage()
		2:
			# 锁血下限：每帧补足，受伤不死
			if GameState.health < _max_hp:
				GameState.heal(_max_hp - GameState.health)
			# 补刷兜底：敌机飞出屏幕自毁不计击杀，场上无敌机且未达标时补足剩余数
			if not _advancing and _stage_kills < 5 and _alive_enemy_count() == 0:
				_spawn_combat_wave(5 - _stage_kills)
		3:
			# 长按 H 蓄力召唤母舰（对齐正局 dock_charge_time；母舰已在场不再重复触发）
			if _mothership == null and not _advancing:
				if Input.is_action_pressed("dock"):
					_dock_charge += delta
					_set_objective_tr("TUT_S4_CHARGE", [int(clampf(_dock_charge / DOCK_CHARGE_TIME, 0.0, 1.0) * 100.0)])
					if _dock_charge >= DOCK_CHARGE_TIME:
						_summon_mothership()
				elif _dock_charge > 0.0:
					_dock_charge = 0.0
					_set_objective_tr("TUT_S4_OBJ")
		4:
			if Input.is_action_pressed("homecoming"):
				_home_charge += delta
				_set_objective_tr("TUT_S5_CHARGE", [int(clampf(_home_charge / HOME_CHARGE_TIME, 0.0, 1.0) * 100.0)])
				if _home_charge >= HOME_CHARGE_TIME:
					_open_base()
			elif _home_charge > 0.0:
				_home_charge = 0.0
				_set_objective_tr("TUT_S5_OBJ")


func _open_base() -> void:
	if _base_ui != null:
		return
	_base_ui = CanvasLayer.new()
	_base_ui.process_mode = Node.PROCESS_MODE_ALWAYS
	_base_ui.set_script(load("res://scripts/base_console.gd"))
	add_child(_base_ui)
	_base_ui.resume_requested.connect(_on_base_resume)
	_base_ui.show_base()
	get_tree().paused = true
	# 打开即过关：1s 后自动关闭进入下一阶段（玩家点继续出击同样推进）
	_pass_stage()
	# 一次性 Timer 节点 + 信号回调（AGENTS：禁止 await create_timer 协程）
	var timer := Timer.new()
	timer.one_shot = true
	timer.process_mode = Node.PROCESS_MODE_ALWAYS  # 树暂停（基地 UI）中仍需计时
	timer.wait_time = 1.2
	timer.timeout.connect(_close_base)
	add_child(timer)
	timer.start()


func _close_base() -> void:
	if _base_ui == null:
		return
	get_tree().paused = false
	_base_ui.queue_free()
	_base_ui = null


func _on_base_resume() -> void:
	_close_base()


func _finish() -> void:
	_finished = true
	GameState.tutorial_done = true
	GameState.save_profile()
	GameState.play_sfx(GameState.SFX_BUFF_PICK)
	# 清场
	for child in get_children():
		if child is Enemy or child is Boss or child is Bullet or child is Mothership:
			child.queue_free()
	_title_label.text = tr("TUT_DONE")
	_set_objective_tr("TUT_DONE_DESC")
	_complete_panel = PanelContainer.new()
	_complete_panel.set_anchors_preset(Control.PRESET_CENTER)
	_complete_panel.position = Vector2(-160.0, -40.0)
	_complete_panel.custom_minimum_size = Vector2(320.0, 0.0)
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.10, 0.13, 0.22, 0.95)
	style.border_color = Color(0.3, 0.8, 0.9)
	style.set_border_width_all(2)
	style.set_corner_radius_all(8)
	style.set_content_margin_all(20.0)
	_complete_panel.add_theme_stylebox_override("panel", style)
	var button := Button.new()
	button.text = tr("TUT_BACK")
	button.add_theme_font_override("font", FONT)
	button.add_theme_font_size_override("font_size", 26)
	UITheme.apply_button(button)
	button.pressed.connect(_exit_tutorial)
	_complete_panel.add_child(button)
	_hud_layer.add_child(_complete_panel)


func _exit_tutorial() -> void:
	Engine.time_scale = 1.0  # 防御性复位
	get_tree().paused = false
	GameState.reset_run()  # 不污染正常对局
	get_tree().change_scene_to_file("res://scenes/main.tscn")


func _unhandled_input(event: InputEvent) -> void:
	# 教程中按 Esc 直接退出回开始面板（无暂停菜单）
	if event.is_action_pressed("ui_cancel"):
		_exit_tutorial()
