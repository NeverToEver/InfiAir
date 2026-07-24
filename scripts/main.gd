extends Node2D
## 主场景：串联生成器、HUD 与各 UI 层，处理母舰召唤（H）、返航（B）、
## 开始面板（继续对局/新游戏）与常驻 BGM。Esc/手柄 B/Android 返回的全局路由
## 在 BackNavigator（process_mode=Always；本节点暂停时收不到 _unhandled_input）。

const BGM_PATH := "res://assets/audio/bgm_loop.wav"
const MOTHERSHIP_SCENE: PackedScene = preload("res://scenes/mothership.tscn")
var DOCK_CHARGE_TIME := 3.0
var HOME_CHARGE_TIME := 1.5
var GIVE_UP_HOLD_TIME := 3.0
## Boss 狂暴子弹时间（对齐原作 ENRAGE_SLOW_FACTOR=0.24）：1.2s 全局慢速 → 0.3s 恢复 → 快照弹幕
## （子弹时间是狂暴序列 TRANSITION 的表现；序列编排/锁血/锁玩家移动由 boss.gd 接管）
var ENRAGE_SLOW_SCALE := 0.24
var ENRAGE_BULLET_TIME := 1.2
var ENRAGE_RAMP_TIME := 0.3

@onready var _spawner: Node = $Spawner
@onready var _hud: CanvasLayer = $HUD
@onready var _buff_ui: CanvasLayer = $BuffUI
@onready var _pause_ui: CanvasLayer = $PauseUI
@onready var _start_panel: CanvasLayer = $StartPanel
@onready var _base_ui: CanvasLayer = $BaseUI
@onready var _player: Player = $Player
@onready var _starfield: Starfield = $Starfield
@onready var _camera: Camera2D = $Camera2D

var _game_over: bool = false
var _homecoming: bool = false
var _bgm_player: AudioStreamPlayer
var _dock_cooldown: float = 0.0
var _mothership: Mothership = null
var _charging: bool = false
var _charge_time: float = 0.0
var _charge_ghost: Mothership
var _home_charge_time: float = 0.0
var _give_up_charge: float = 0.0
# Boss 狂暴子弹时间状态（main 统一接管：Boss 被杀/逃跑也保证 time_scale 回 1）
var _bullet_time_left: float = 0.0  # >0：子弹时间剩余（游戏秒，随 time_scale 缩放）
var _time_scale_ramp: float = -1.0  # >=0：恢复过渡进度 0..1
var _enrage_boss: Boss = null


func _ready() -> void:
	add_to_group("main")
	DOCK_CHARGE_TIME = GameState.cfg("mothership.dock_charge_time", DOCK_CHARGE_TIME)
	HOME_CHARGE_TIME = GameState.cfg("effects.home_charge_time", HOME_CHARGE_TIME)
	GIVE_UP_HOLD_TIME = GameState.cfg("effects.give_up_hold_time", GIVE_UP_HOLD_TIME)
	ENRAGE_SLOW_SCALE = GameState.cfg("boss.enrage.slow_scale", ENRAGE_SLOW_SCALE)
	ENRAGE_BULLET_TIME = GameState.cfg("boss.enrage.bullet_time", ENRAGE_BULLET_TIME)
	ENRAGE_RAMP_TIME = GameState.cfg("boss.enrage.ramp_time", ENRAGE_RAMP_TIME)
	# 防御：上一场对局若在子弹时间内结束（死亡重开），确保全局速度已复位
	Engine.time_scale = 1.0
	RenderingServer.set_default_clear_color(Color(0.02, 0.02, 0.06))
	_spawner.boss_spawned.connect(_hud.show_boss_bar)
	_spawner.boss_spawned.connect(_on_boss_spawned)
	_spawner.boss_warning.connect(_hud.show_boss_banner)
	GameState.player_died.connect(_on_player_died)
	_start_panel.continue_chosen.connect(_on_continue_run)
	_start_panel.new_game_chosen.connect(_apply_new_run)
	_base_ui.resume_requested.connect(_resume_from_base)
	# 视角缩放：应用到相机（震动只写 offset，与 zoom 互不干扰）；注册供可见区域计算
	GameState.camera_ref = _camera
	_camera.zoom = Vector2.ONE * GameState.view_zoom_factor()
	GameState.view_zoom_changed.connect(_on_view_zoom_changed)
	_start_bgm_async()
	if "--startup-time" in OS.get_cmdline_user_args():
		_report_startup_time()
	# 蓄力虚影（长按 H 蓄力期间显示）：复用真实母舰场景实例做半透明预告，
	# 禁用状态机（仅外观，不移动/不对接），停驻高度取实例配置 HOVER_Y
	_charge_ghost = MOTHERSHIP_SCENE.instantiate() as Mothership
	add_child(_charge_ghost)
	# 必须在入树后禁用：入树前调用 set_physics_process(false) 不生效（4.6 实测）
	_charge_ghost.set_physics_process(false)
	_charge_ghost.position = Vector2(960.0, _charge_ghost.HOVER_Y)
	_charge_ghost.modulate = Color(1.0, 1.0, 1.0, 0.15)
	_charge_ghost.visible = false
	# 有存档则先显示开始面板，否则直接开新局；欢迎页在显时由 dismiss() 补调 show_panel，
	# 不得在此抢显（GUI 焦点不看 layer 遮挡，Enter 会绕过欢迎页直接触发继续对局）
	if GameState.has_save() and not $WelcomeScreen.visible:
		_start_panel.show_panel()


func _exit_tree() -> void:
	# 子弹时间内退出（重开/测试结束）也要保证全局速度复位
	Engine.time_scale = 1.0
	if GameState.camera_ref == _camera:
		GameState.camera_ref = null


func _on_view_zoom_changed(factor: float) -> void:
	_camera.zoom = Vector2(factor, factor)


func _process(delta: float) -> void:
	# Boss 狂暴子弹时间驱动（delta 已被 time_scale 缩放，计时为游戏秒）：
	# 0.24 慢速 1.2s → 0.3s 内线性恢复 1.0 → 恢复完成才发快照弹幕
	if _bullet_time_left > 0.0:
		_bullet_time_left -= delta
		if _bullet_time_left <= 0.0:
			_time_scale_ramp = 0.0
	if _time_scale_ramp >= 0.0:
		_time_scale_ramp += delta / ENRAGE_RAMP_TIME
		if _time_scale_ramp >= 1.0:
			_time_scale_ramp = -1.0
			Engine.time_scale = 1.0
			_fire_enrage_snapshot()
		else:
			Engine.time_scale = lerpf(ENRAGE_SLOW_SCALE, 1.0, _time_scale_ramp)
	if _dock_cooldown > 0.0:
		_dock_cooldown -= delta
	# 长按 H 蓄力召唤母舰（松手取消，不进冷却）
	var can_charge := (
		_mothership == null and _dock_cooldown <= 0.0 and not _game_over and not _homecoming
	)
	if can_charge and Input.is_action_pressed("dock"):
		_charging = true
		_charge_time += delta
		_charge_ghost.visible = true
		_charge_ghost.modulate.a = 0.15 + 0.25 * clampf(_charge_time / DOCK_CHARGE_TIME, 0.0, 1.0)
		if _charge_time >= DOCK_CHARGE_TIME:
			_stop_charging()
			_summon_mothership()
	elif _charging:
		_stop_charging()
	# 长按 B 蓄力返航（松手取消）
	if not _game_over and not _homecoming and Input.is_action_pressed("homecoming"):
		_home_charge_time += delta
		_hud.set_home_charge(_home_charge_time / HOME_CHARGE_TIME)
		if _home_charge_time >= HOME_CHARGE_TIME:
			_home_charge_time = 0.0
			_hud.set_home_charge(-1.0)
			_start_homecoming()
	elif _home_charge_time > 0.0:
		_home_charge_time = 0.0
		_hud.set_home_charge(-1.0)
	# 长按 K 蓄力放弃出击（自毁进死亡结算，松手取消；give_up 映射由 project.godot 提供）
	if (
		InputMap.has_action(&"give_up")
		and not _game_over
		and not _homecoming
		and not _player._dead
		and Input.is_action_pressed(&"give_up")
	):
		_give_up_charge += delta
		_hud.set_give_up_charge(_give_up_charge / GIVE_UP_HOLD_TIME)
		if _give_up_charge >= GIVE_UP_HOLD_TIME:
			_give_up_charge = 0.0
			_hud.set_give_up_charge(-1.0)
			_give_up()
	elif _give_up_charge > 0.0:
		_give_up_charge = 0.0
		_hud.set_give_up_charge(-1.0)


func _stop_charging() -> void:
	_charging = false
	_charge_time = 0.0
	_charge_ghost.visible = false


## BGM 延后到首帧之后启动：3.5MB WAV 解码不占首帧关键路径
func _start_bgm_async() -> void:
	await get_tree().process_frame
	_start_bgm()


## 启动计时（--startup-time 传入时）：打印 boot → 首帧 / → 首面板就绪 的分段耗时
func _report_startup_time() -> void:
	await get_tree().process_frame
	print("[startup] boot → first frame: %d ms" % (Time.get_ticks_msec() - GameState.boot_ticks_msec))


func _start_bgm() -> void:
	var stream := ResourceLoader.load(BGM_PATH, "AudioStreamWAV", ResourceLoader.CACHE_MODE_IGNORE) as AudioStreamWAV
	# 只设 loop_mode 即可整段循环；显式写 loop_begin/loop_end 会在退出时泄漏播放实例
	stream.loop_mode = AudioStreamWAV.LOOP_FORWARD
	_bgm_player = AudioStreamPlayer.new()
	_bgm_player.stream = stream
	_bgm_player.volume_db = -18.0
	add_child(_bgm_player)
	_bgm_player.play()


## 新对局（无存档或开始面板选「新游戏」）：数据层已由 reset_run/读档就绪，无需额外处理
func _apply_new_run() -> void:
	pass


func _on_continue_run() -> void:
	var data := GameState.load_run_data()
	if data.is_empty():
		# 存档损坏已被 GameState 隔离备份：回退为新对局（数据层本就在默认态），不留死路径
		_apply_new_run()
		return
	GameState.apply_run_save(data)
	_player._fuel = GameState.save_num(data.get("fuel", _player.fuel_max), _player.fuel_max)
	_spawner._elapsed = GameState.save_num(data.get("elapsed", 0.0), 0.0)


func _on_player_died() -> void:
	_game_over = true
	# 玩家死亡兜底：输入/狂暴移动锁立即解除（锁计时器随暂停冻结，不能依赖它解锁）
	_player._input_locked = false
	_player.movement_locked = false


## Boss 入场时挂接狂暴信号（狂暴弹幕/子弹时间由 main 统一编排）
func _on_boss_spawned(boss: Boss) -> void:
	boss.enraged.connect(_on_boss_enraged.bind(boss))


## 狂暴触发：1.2s 子弹时间（全局 0.24，玩家同样减速——与原作一致）+ 泛红演出。
## 既有震动/警告音在 boss._enrage() 内；快照弹幕等子弹时间结束后才发。
func _on_boss_enraged(boss: Boss) -> void:
	if _game_over or _homecoming:
		return
	_enrage_boss = boss
	_bullet_time_left = ENRAGE_BULLET_TIME
	_time_scale_ramp = -1.0
	Engine.time_scale = ENRAGE_SLOW_SCALE
	_enrage_vignette()


## 子弹时间结束：Boss 仍在场则发快照弹幕（作为 TRANSITION 收尾的一波；
## 玩家移动冻结/锁血由 Boss 狂暴序列自行管理）
func _fire_enrage_snapshot() -> void:
	var boss := _enrage_boss
	_enrage_boss = null
	if boss == null or not is_instance_valid(boss) or boss.is_queued_for_deletion():
		return  # Boss 在子弹时间内已被击杀/逃跑：time_scale 已恢复，无需弹幕
	boss.fire_enrage_snapshot()


## 狂暴演出：全屏短暂泛红（tween 挂在 Always 层上，暂停时也能播完并自清）
func _enrage_vignette() -> void:
	var layer := CanvasLayer.new()
	layer.layer = 30
	layer.process_mode = Node.PROCESS_MODE_ALWAYS
	add_child(layer)
	var rect := ColorRect.new()
	rect.color = Color(0.85, 0.05, 0.05, 0.0)
	rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	layer.add_child(rect)
	var tween := layer.create_tween()
	tween.tween_property(rect, "color:a", 0.35, 0.15)
	tween.tween_property(rect, "color:a", 0.0, 0.55)
	tween.tween_callback(layer.queue_free)


## 母舰状态文本（HUD 轮询）
func dock_status_text() -> String:
	if _charging:
		return tr("MS_CHARGING") % int(_charge_time / DOCK_CHARGE_TIME * 100.0)
	if _mothership != null:
		return _mothership.state_text()
	if _dock_cooldown > 0.0:
		return tr("MS_COOLDOWN") % ceili(_dock_cooldown)
	return tr("MS_READY")


func _summon_mothership() -> void:
	_mothership = MOTHERSHIP_SCENE.instantiate() as Mothership
	_mothership.position = Vector2(960.0, GameState.view_world_rect().position.y - 200.0)
	_mothership.departed.connect(_on_mothership_departed)
	_mothership.tree_exited.connect(func() -> void: _mothership = null)
	add_child(_mothership)


func _on_mothership_departed(cooldown: float) -> void:
	# mothership_recall buff：每层冷却 ×0.5（60s→30s→15s）
	_dock_cooldown = cooldown * pow(GameState.cfg("buffs.mothership_recall.cooldown_factor", 0.5), GameState.buff_count(&"mothership_recall"))


## 放弃出击（长按 K 3s）：自毁，走正常死亡结算（删档/最高分/结算面板）
func _give_up() -> void:
	if _player._dead or GameState.health <= 0.0:
		return
	GameState.lose_health(GameState.health)
	_player._die()


## 返航（局内中场整备）：锁输入、星光拉伸 + 白屏闪，进入基地控制台。
## 对局继续：不删档（反而更新存档）、Boss 保留、死亡才是唯一终局。
func _start_homecoming() -> void:
	_homecoming = true
	_home_charge_time = 0.0
	_hud.set_home_charge(-1.0)
	_player._input_locked = true
	_player.velocity = Vector2.ZERO
	_spawner.set_process(false)
	# 母舰若在对接/驻留中，直接收回——按基础冷却进冷却（防"补给→返航→再召唤"无限循环）
	if _mothership != null:
		_mothership.queue_free()
		_on_mothership_departed(GameState.cfg("mothership.depart_cooldown", 60.0))
	# 返航后存档保留更新，供「继续对局」使用
	GameState.save_run(_player._fuel, _spawner._elapsed)
	_starfield.warp(18.0)
	var flash := await _flash_white(0.5, 0.5)
	flash.queue_free()
	_base_ui.show_base()
	get_tree().paused = true


## 继续出击：轨道打击清屏（Boss 保留，清小怪与全部弹丸）→ 短白屏 → 恢复同一局
func _resume_from_base() -> void:
	for child in get_children():
		if child is Enemy or child is Bullet:
			child.queue_free()
	_player._input_locked = false
	# 驻留期无敌可能是 999，恢复时统一重置为短无敌
	_player._invincible = 1.5
	_spawner.set_process(true)
	_homecoming = false
	get_tree().paused = false
	var flash := await _flash_white(0.15, 0.25)
	flash.queue_free()


func _flash_white(fade_in: float, hold: float) -> CanvasLayer:
	var flash_layer := CanvasLayer.new()
	flash_layer.layer = 40
	add_child(flash_layer)
	var flash := ColorRect.new()
	flash.color = Color(1.0, 1.0, 1.0, 0.0)
	flash.set_anchors_preset(Control.PRESET_FULL_RECT)
	flash_layer.add_child(flash)
	var tween := create_tween()
	tween.tween_property(flash, "color:a", 1.0, fade_in)
	tween.tween_interval(hold)
	tween.tween_property(flash, "color:a", 0.0, 0.3)
	await tween.finished
	return flash_layer
