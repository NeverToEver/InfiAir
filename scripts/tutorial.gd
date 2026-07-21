extends Node2D
## 新手教程（对齐原作 6 阶段）：独立场景，脚本驱动检查点，复用现有实体。
## 不启动正常 spawner 波次；进场 reset_run + 删档隔离，出场再 reset 并保证 time_scale=1。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")
const ENEMY_SCENE: PackedScene = preload("res://scenes/enemy.tscn")
const BOSS_SCENE: PackedScene = preload("res://scenes/boss.tscn")
const MOTHERSHIP_SCENE: PackedScene = preload("res://scenes/mothership.tscn")
const SPAWNER_SCRIPT: GDScript = preload("res://scripts/spawner.gd")
const HOME_CHARGE_TIME := 1.5

const STAGE_TITLES: Array[String] = [
	"阶段 1/6：移动与瞄准",
	"阶段 2/6：加速与相位突进",
	"阶段 3/6：战斗基础",
	"阶段 4/6：母舰停靠",
	"阶段 5/6：返航与基地",
	"阶段 6/6：首领遭遇",
]

var _stage: int = 0
var _advancing: bool = false
var _stage_kills: int = 0
var _boost_count: int = 0
var _dash_count: int = 0
var _prev_dashing: bool = false
var _home_charge: float = 0.0
var _base_ui: CanvasLayer = null
var _boss: Boss = null
var _mothership: Mothership = null
var _finished: bool = false

var _title_label: Label
var _objective_label: Label
var _complete_panel: PanelContainer
var _hud_layer: CanvasLayer

@onready var _player: Player = $Player


func _ready() -> void:
	# 存档隔离：教程不读写 savegame
	GameState.delete_save()
	GameState.reset_run()
	RenderingServer.set_default_clear_color(Color(0.02, 0.02, 0.06))
	_build_hud()
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
	_hud_layer.add_child(_title_label)
	_objective_label = Label.new()
	_objective_label.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_objective_label.position = Vector2(-500.0, 74.0)
	_objective_label.custom_minimum_size = Vector2(1000.0, 0.0)
	_objective_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_objective_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_objective_label.add_theme_font_override("font", FONT)
	_objective_label.add_theme_font_size_override("font_size", 22)
	_hud_layer.add_child(_objective_label)


func _set_objective(text: String) -> void:
	_objective_label.text = text


func _enter_stage(idx: int) -> void:
	_stage = idx
	_stage_kills = 0
	_title_label.text = STAGE_TITLES[idx]
	match idx:
		0:  # 移动与瞄准：3 个静止靶机
			_set_objective("WASD 移动，鼠标瞄准（自动开火）\n击杀 3 个训练靶 (0/3)")
			for i in 3:
				var e := _spawn_enemy(SPAWNER_SCRIPT.ENEMY_TYPES[0], &"straight")
				e.speed = 0.0  # 静止靶
				e.position = Vector2(600.0 + 360.0 * i, 280.0)
		1:  # 加速与相位突进
			GameState.add_buff(&"phase_dash")
			_boost_count = 0
			_dash_count = 0
			_prev_dashing = false
			_update_boost_objective()
		2:  # 战斗基础：5 只 straight，锁血下限
			_set_objective("击落全部 5 架敌机 (0/5)\n本阶段受伤不会阵亡")
			for i in 5:
				var e := _spawn_enemy(SPAWNER_SCRIPT.ENEMY_TYPES[0], &"straight")
				e.position = Vector2(300.0 + 330.0 * i, -60.0 - 120.0 * (i % 2))
		3:  # 母舰停靠
			_set_objective("飞入母舰下方对接区，完成一次完整对接补给\n（弹匣随驻留消耗，≤4 格会触发弹药警告）")
			_mothership = MOTHERSHIP_SCENE.instantiate() as Mothership
			_mothership.position = Vector2(960.0, -200.0)
			_mothership.departed.connect(_on_mothership_departed)
			add_child(_mothership)
		4:  # 返航与基地
			_home_charge = 0.0
			_set_objective("长按 B 1.5 秒返航，打开基地控制台\n（基地可用 RP 维修/充能、选择天赋路线、领取任务）")
		5:  # 首领遭遇：低 HP Boss-1，触发狂暴即过关
			_set_objective("攻击首领，逼它进入狂暴状态（血量 30%）")
			_player._invincible = 999.0  # 教程不判负
			_boss = BOSS_SCENE.instantiate() as Boss
			_boss.setup(1.0, 1)
			_boss.max_hp = 12.0
			_boss.hp = 12.0
			_boss.position = Vector2(960.0, -160.0)
			_boss.enraged.connect(_on_boss_enraged)
			add_child(_boss)


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
		_set_objective("WASD 移动，鼠标瞄准（自动开火）\n击杀 3 个训练靶 (%d/3)" % _stage_kills)
		if _stage_kills >= 3:
			_pass_stage()
	elif _stage == 2:
		_set_objective("击落全部 5 架敌机 (%d/5)\n本阶段受伤不会阵亡" % _stage_kills)
		if _stage_kills >= 5:
			_pass_stage()


func _update_boost_objective() -> void:
	_set_objective(
		(
			"按住 Shift 加速（耗燃料，松开恢复）：%d/2\n空格相位突进（需 25%% 燃料）：%d/2"
			% [_boost_count, _dash_count]
		)
	)


func _on_mothership_departed(_cooldown: float) -> void:
	if _stage == 3:
		_pass_stage()


func _on_boss_enraged() -> void:
	if _stage == 5 and not _finished:
		_finish()


func _pass_stage() -> void:
	if _advancing:
		return
	_advancing = true
	GameState.play_sfx(GameState.SFX_BUFF_PICK)
	await get_tree().create_timer(1.0).timeout
	_advancing = false
	if _stage < STAGE_TITLES.size() - 1:
		_enter_stage(_stage + 1)


func _physics_process(delta: float) -> void:
	if _finished:
		return
	match _stage:
		1:
			# 加速/冲刺输入计数（rising edge）
			if Input.is_action_just_pressed("boost"):
				_boost_count = mini(_boost_count + 1, 2)
				_update_boost_objective()
			if _player._dashing and not _prev_dashing:
				_dash_count = mini(_dash_count + 1, 2)
				_update_boost_objective()
			_prev_dashing = _player._dashing
			if _boost_count >= 2 and _dash_count >= 2:
				_pass_stage()
		2:
			# 锁血下限：每帧补足，受伤不死
			if GameState.lives < 3.0:
				GameState.lives = 3.0
		4:
			if Input.is_action_pressed("homecoming"):
				_home_charge += delta
				_set_objective("返航蓄力 %d%%…" % int(clampf(_home_charge / HOME_CHARGE_TIME, 0.0, 1.0) * 100.0))
				if _home_charge >= HOME_CHARGE_TIME:
					_open_base()
			elif _home_charge > 0.0:
				_home_charge = 0.0
				_set_objective("长按 B 1.5 秒返航，打开基地控制台\n（基地可用 RP 维修/充能、选择天赋路线、领取任务）")


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
	await get_tree().create_timer(1.2).timeout
	_close_base()


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
	_title_label.text = "教程完成！"
	_set_objective("你已掌握全部基础操作，祝武运昌隆！")
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
	button.text = "返回主菜单"
	button.add_theme_font_override("font", FONT)
	button.add_theme_font_size_override("font_size", 26)
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
