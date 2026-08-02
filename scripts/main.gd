extends Node2D
## 主场景：串联生成器、HUD 与各 UI 层，处理母舰召唤（H）、返航（B）、
## 开始面板（继续对局/新游戏）与常驻 BGM。Esc/手柄 B/Android 返回的全局路由
## 在 BackNavigator（process_mode=Always；本节点暂停时收不到 _unhandled_input）。

const BGM_PATH := "res://assets/audio/bgm_loop.wav"
const MOTHERSHIP_SCENE: PackedScene = preload("res://scenes/mothership.tscn")
const INTRO_SCENE: PackedScene = preload("res://scenes/intro_cinematic.tscn")
const RETURN_SCENE: PackedScene = preload("res://scenes/return_cinematic.tscn")
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
var _charge_fx: Node2D = null  # 蓄力特效容器（与 _charge_ghost 同位，随蓄力显隐）
var _charge_glow: Sprite2D = null  # 虚影背光
var _charge_rings: Array[Line2D] = []  # 收缩椭圆环 ×2
var _charge_inflow: GPUParticles2D = null  # 内吸粒子
var _home_charge_time: float = 0.0
var _give_up_charge: float = 0.0
# Boss 狂暴子弹时间状态（main 统一接管）。注意：time_scale 复位只覆盖 Boss 离场/逃跑路径，
# 返航/放弃/玩家死亡路径不复位（已知缺口，见 docs/AUDIT_VAULT.md B2），修前勿依赖全局保证
var _bullet_time_left: float = 0.0  # >0：子弹时间剩余（游戏秒，随 time_scale 缩放）
var _time_scale_ramp: float = -1.0  # >=0：恢复过渡进度 0..1
var _enrage_boss: Boss = null
## 播放中的开场过场（BackNavigator 据此路由 Esc=跳过；null = 未播放）
var _intro: IntroCinematic = null
## 播放中的返航过场（BackNavigator 据此路由 Esc=跳过；null = 未播放）
var _return: ReturnCinematic = null
## 播放中的轨道打击清场动画（继续出击时触发；null = 未播放）
var _strike: OrbitalStrike = null
## 播放中的母舰召唤机库小窗（蓄力完成后触发；null = 未播放）
var _summon_window: MothershipSummonWindow = null
## 精英炮塔事件编排节点（_ready 创建并登记给 spawner 互斥）
var _event: EliteTurretEvent = null
## 轰炸编队事件编排节点（_ready 创建并登记给 spawner；最低优先级随机事件）
var _formation: FormationStrikeEvent = null
## Meta HUD 血量/受击后处理层（_ready 创建；DYING 呼吸缩放经 _apply_camera_zoom 组合）
var _meta_fx: MetaHealthFX = null
## 辅助瞄准框覆盖层（_ready 创建；世界坐标单节点画全部标记敌框，登记 GameState.aim_frame_layer）
var _aim_frames: AimFrameLayer = null
var _breath_was_active: bool = false


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
	# 精英炮塔事件：编排节点挂 Main 下（清场/测试遍历可见），spawner 持引用做互斥
	_event = EliteTurretEvent.new()
	add_child(_event)
	_event.set_spawner(_spawner)  # A5：依赖注入，替代事件侧 group 现找
	_spawner.set_elite_event(_event)
	# 轰炸编队事件：同模式登记（最低优先级随机事件，不冻结 Boss/波次）
	_formation = FormationStrikeEvent.new()
	add_child(_formation)
	_spawner.set_formation_event(_formation)
	GameState.player_died.connect(_on_player_died)
	_start_panel.continue_chosen.connect(_on_continue_run)
	_start_panel.new_game_chosen.connect(_apply_new_run)
	_base_ui.resume_requested.connect(_resume_from_base)
	# 视角缩放：应用到相机（震动只写 offset，与 zoom 互不干扰）；注册供可见区域计算
	GameState.camera_ref = _camera
	# Meta HUD 血量/受击后处理层（layer=1，世界之上、HUD 之下；先于首次 zoom 组合创建）
	_meta_fx = MetaHealthFX.new()
	add_child(_meta_fx)
	# 辅助瞄准框覆盖层（P1-1）：世界坐标单节点，每帧统一画标记敌 bracket 框
	_aim_frames = AimFrameLayer.new()
	add_child(_aim_frames)
	_apply_camera_zoom()
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
	# C14：蓄力虚影居中取可见世界中心，不写死 960
	_charge_ghost.position = Vector2(GameState.view_world_rect().get_center().x, _charge_ghost.HOVER_Y)
	_charge_ghost.modulate = Color(1.0, 1.0, 1.0, 0.15)
	_charge_ghost.visible = false
	_build_charge_fx()
	# 有存档则显示开始面板；无存档时开始面板由自身逻辑自显（并非"直接开新局"）。
	if GameState.has_save():
		_start_panel.show_panel()


func _exit_tree() -> void:
	# 子弹时间内退出（重开/测试结束）也要保证全局速度复位
	Engine.time_scale = 1.0
	if GameState.camera_ref == _camera:
		GameState.camera_ref = null


## 对外公开接口（A1 修复）：BackNavigator/HUD 决策查询，禁止跨类直接读 _ 私有字段
func is_intro_playing() -> bool:
	return _intro != null


func is_return_playing() -> bool:
	return _return != null


func is_game_over() -> bool:
	return _game_over


func is_homecoming() -> bool:
	return _homecoming


func mothership() -> Mothership:
	return _mothership


## A7：测试/诊断白盒断言经公开接口（命名语义化）
func player() -> Player:
	return _player


func hud() -> CanvasLayer:
	return _hud


func base_ui() -> CanvasLayer:
	return _base_ui


func pause_ui() -> CanvasLayer:
	return _pause_ui


func meta_fx() -> MetaHealthFX:
	return _meta_fx


func event() -> EliteTurretEvent:
	return _event


func formation() -> FormationStrikeEvent:
	return _formation


func strike() -> OrbitalStrike:
	return _strike


func summon_window() -> MothershipSummonWindow:
	return _summon_window


func set_homecoming(v: bool) -> void:
	_homecoming = v


func set_game_over(v: bool) -> void:
	_game_over = v


func set_bullet_time(seconds: float) -> void:
	_bullet_time_left = seconds


func time_scale_ramp() -> float:
	return _time_scale_ramp


func play_intro() -> void:
	_play_intro_cinematic()


func skip_intro() -> void:
	_skip_intro()


func play_return() -> void:
	_play_return_cinematic()


func skip_return() -> void:
	_skip_return()


func start_homecoming() -> void:
	_start_homecoming()


func summon_mothership() -> void:
	_summon_mothership()


func resume_from_base() -> void:
	_resume_from_base()


## A7：测试/诊断经公开接口（动作包装）——开场/继续出击后的战机入场序列
func start_entry_sequence() -> void:
	_start_entry_sequence()


func stop_charging() -> void:
	_stop_charging()


func set_dock_cooldown(seconds: float) -> void:
	_dock_cooldown = seconds


func on_mothership_departed(seconds: float) -> void:
	_on_mothership_departed(seconds)


func charging() -> bool:
	return _charging


func charge_ghost() -> Mothership:
	return _charge_ghost


func give_up_charge() -> float:
	return _give_up_charge


func continue_run() -> void:
	_on_continue_run()


func bullet_time() -> float:
	return _bullet_time_left


func dock_cooldown() -> float:
	return _dock_cooldown


func set_charge_time(seconds: float) -> void:
	_charge_time = seconds


func intro() -> IntroCinematic:
	return _intro


func return_cinematic() -> ReturnCinematic:
	return _return


func _on_view_zoom_changed(_factor: float) -> void:
	_apply_camera_zoom()


## 相机 zoom 单点组合（D6）：视角档位 × DYING 呼吸缩放；震动只写 offset 不受影响
func _apply_camera_zoom() -> void:
	var breath := 1.0
	if _meta_fx != null and _meta_fx.breath_active():
		breath = _meta_fx.breath_scale()
	_camera.zoom = Vector2.ONE * GameState.view_zoom_factor() * breath


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
		var cp := clampf(_charge_time / DOCK_CHARGE_TIME, 0.0, 1.0)
		_charge_ghost.modulate.a = 0.15 + 0.25 * cp
		# 蓄力特效：背光渐亮 + 双环错峰收缩 + 内吸粒子（帧内仅属性写，零分配）
		_charge_fx.visible = true
		_charge_inflow.emitting = true
		_charge_glow.modulate.a = 0.35 * cp
		for i in _charge_rings.size():
			var rp := clampf(cp * 1.25 - 0.25 * float(i), 0.0, 1.0)
			var ring := _charge_rings[i]
			ring.scale = Vector2.ONE * lerpf(2.2, 0.7, rp)
			ring.modulate.a = 0.15 + 0.55 * rp
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
		and not _player.is_dead()
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
	# DYING 呼吸缩放（D6）：仅激活期逐帧组合；退出激活时复位一次到基础 zoom
	var breath_on := _meta_fx != null and _meta_fx.breath_active()
	if breath_on or _breath_was_active:
		_apply_camera_zoom()
	_breath_was_active = breath_on


func _stop_charging() -> void:
	_charging = false
	_charge_time = 0.0
	_charge_ghost.visible = false
	_charge_fx.visible = false
	_charge_inflow.emitting = false


## 蓄力特效（长按 H 期间随 _charge_ghost 显示）：虚影背光 + 双收缩椭圆环 + 内吸粒子。
## 与虚影同位（960, HOVER_Y），世界坐标；环半径 160 为设计值 × world_scale。
func _build_charge_fx() -> void:
	var ws: float = GameState.world_scale
	_charge_fx = Node2D.new()
	# C14：与虚影同位（复用 _charge_ghost.position.x，不写死 960）
	_charge_fx.position = Vector2(_charge_ghost.position.x, _charge_ghost.HOVER_Y)
	_charge_fx.visible = false
	add_child(_charge_fx)
	# 背光（衬在虚影之下：z -1）
	_charge_glow = CinematicFx.soft_glow(220.0 * ws, Color(0.35, 0.85, 1.0, 0.0))
	_charge_glow.z_index = -1
	_charge_fx.add_child(_charge_glow)
	# 收缩椭圆环 ×2（透视压扁，蓄力进度驱动 2.2→0.7 错峰收缩）
	for i in 2:
		var ring := Line2D.new()
		ring.width = 2.5
		ring.default_color = Color(0.4, 0.9, 1.0)
		ring.points = CinematicFx.ring_points(48, 160.0 * ws, 0.5)
		ring.material = CinematicFx.additive_material()
		_charge_fx.add_child(ring)
		_charge_rings.append(ring)
	# 内吸粒子：环上发射、负径向速度流向中心（蓄能汇聚感）
	_charge_inflow = CinematicFx.particles({
		"amount": 36, "lifetime": 0.7, "vel_min": 0.0, "vel_max": 0.0,
		"scale_min": 2.0, "scale_max": 4.0, "color": Color(0.5, 0.9, 1.0, 0.55),
	})
	var inflow_mat := _charge_inflow.process_material as ParticleProcessMaterial
	inflow_mat.direction = Vector3.ZERO
	inflow_mat.spread = 0.0
	inflow_mat.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_RING
	inflow_mat.emission_ring_axis = Vector3(0.0, 0.0, 1.0)
	inflow_mat.emission_ring_radius = 160.0 * ws
	inflow_mat.emission_ring_inner_radius = 150.0 * ws
	inflow_mat.emission_ring_height = 0.0
	inflow_mat.radial_velocity_min = -90.0 * ws
	inflow_mat.radial_velocity_max = -160.0 * ws
	_charge_inflow.emitting = false
	_charge_fx.add_child(_charge_inflow)


## BGM 延后到首帧之后启动：3.5MB WAV 解码不占首帧关键路径
func _start_bgm_async() -> void:
	await get_tree().process_frame
	# C15：await 后守卫——首帧前 main 被释放（无头测试同帧实例化释放）则不再操作 freed 实例
	if not is_inside_tree():
		return
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


## 新对局（无存档或开始面板选「新游戏」）：数据层已由 reset_run/读档就绪，无需额外处理。
## 仅正常启动入口播放开场过场（测试以子节点实例化 main.tscn 时 current_scene != self，不播）
func _apply_new_run() -> void:
	if get_tree().current_scene == self:
		_play_intro_cinematic()


## 播放开场过场：冻结对局帧 0（树暂停，过场 process_mode=Always 照常播放），
## 播完/跳过统一走 finished 恢复。测试可直接调用本函数触发。
func _play_intro_cinematic() -> void:
	if _intro != null:
		return
	_intro = INTRO_SCENE.instantiate() as IntroCinematic
	_intro.finished.connect(_on_intro_finished)
	add_child(_intro)
	get_tree().paused = true


## Esc 经 BackNavigator 路由至此；任意键/点击由过场自身 _unhandled_input 捕获
func _skip_intro() -> void:
	if _intro != null:
		_intro.skip()


func _on_intro_finished() -> void:
	_intro = null
	get_tree().paused = false
	_start_entry_sequence()  # 开场动画后播战机入场动画（替代原地无敌闪现）


## 播放返航过场：与 _play_intro_cinematic 同构（冻结对局，树暂停，process_mode=Always 播放）。
## BGM 引用交给过场做镜头 7 渐暗期淡出（_bgm_player 异步创建，取值判空）。测试可直接调用。
func _play_return_cinematic() -> void:
	if _return != null:
		return
	_return = RETURN_SCENE.instantiate() as ReturnCinematic
	_return.finished.connect(_on_return_finished)
	if _bgm_player != null:
		_return.bgm_player = _bgm_player
	add_child(_return)
	get_tree().paused = true


## Esc 经 BackNavigator 路由至此；任意键/点击由过场自身 _unhandled_input 捕获
func _skip_return() -> void:
	if _return != null:
		_return.skip()


## 跳过与自然结束同一出口：基地 UI 在黑场下淡入；树保持暂停（基地界面本就是暂停态 UI）。
## BGM 已在过场镜头 7 淡出到 -40dB（或 skip 时立即置位），此处以 -30dB 淡入恢复（基地氛围）
func _on_return_finished() -> void:
	_return = null
	_base_ui.show_base()
	if _bgm_player != null:
		var bgm_tween := create_tween()
		bgm_tween.set_pause_mode(Tween.TWEEN_PAUSE_PROCESS)  # 树保持暂停，tween 需照常推进
		bgm_tween.tween_property(_bgm_player, "volume_db", -30.0, 1.0)


func _on_continue_run() -> void:
	var data := GameState.load_run_data()
	if data.is_empty():
		# 存档损坏已被 GameState 隔离备份：回退为新对局（数据层本就在默认态），不留死路径
		_apply_new_run()
		return
	GameState.apply_run_save(data)
	_player.set_fuel(GameState.save_num(data.get("fuel", _player.fuel_max), _player.fuel_max))
	_spawner.set_elapsed(GameState.save_num(data.get("elapsed", 0.0), 0.0))


func _on_player_died() -> void:
	_game_over = true
	# 玩家死亡兜底：输入/狂暴移动锁立即解除（锁计时器随暂停冻结，不能依赖它解锁）
	_player.unlock_input()
	_player.movement_locked = false
	# 死亡终局冻结 _process：狂暴子弹时间不复位会卡在 0.24（B2 修复）
	_reset_global_time_scale()
	# C25：死亡路径清理蓄力特效残留（_give_up 经 player_died 覆盖到此）
	_stop_charging()


## 对局终态复位全局速度（B2 修复）：返航/死亡/放弃路径会冻结 _process，
## 狂暴子弹时间（time_scale=0.24）不显式复位会卡到下次场景重载
## （返航过场 4 倍慢速播放直到轨道打击才自愈）。
func _reset_global_time_scale() -> void:
	_bullet_time_left = 0.0
	_time_scale_ramp = -1.0
	_enrage_boss = null
	Engine.time_scale = 1.0


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
	if _summon_window != null:
		return tr("MS_DESCEND")
	if _mothership != null:
		return _mothership.state_text()
	if _dock_cooldown > 0.0:
		return tr("MS_COOLDOWN") % ceili(_dock_cooldown)
	return tr("MS_READY")


## 召唤序列（蓄力完成）：锁输入 + 事件驱动无敌（演出期对局不暂停，保护窗口与
## 对接期一致），弹出机库小窗演出；小窗 finished 后开穿梭门、母舰穿出
func _summon_mothership() -> void:
	if _summon_window != null:
		return
	# 成功路径保底隐藏蓄力特效（自然流程 _stop_charging 已处理；测试直调走此分支）
	_charge_fx.visible = false
	_charge_inflow.emitting = false
	_player.lock_input()
	_player.velocity = Vector2.ZERO
	_player.set_invincible(999.0)
	_summon_window = MothershipSummonWindow.new()
	_summon_window.finished.connect(_on_summon_window_finished)
	add_child(_summon_window)


## 小窗演出结束：在母舰停驻点打开穿梭门，母舰穿出减速入场（DESCEND 由母舰自驱；
## 到位后减速带 + 火力掩护 + 牵引回收进保护舱，均由母舰状态机接管）
func _on_summon_window_finished() -> void:
	_summon_window = null
	var gate_pos := Vector2(GameState.view_world_rect().get_center().x, _charge_ghost.HOVER_Y)
	var gate := WarpGate.new()
	gate.position = gate_pos
	add_child(gate)
	GameState.shake(GameState.cfg("effects.mothership_summon.shake_gate", 6.0))
	_mothership = MOTHERSHIP_SCENE.instantiate() as Mothership
	_mothership.begin_warp_in(gate_pos, gate)
	_mothership.departed.connect(_on_mothership_departed)
	_mothership.tree_exited.connect(func() -> void: _mothership = null)
	add_child(_mothership)


func _on_mothership_departed(cooldown: float) -> void:
	# mothership_recall buff：每层冷却 ×0.5（60s→30s→15s）
	_dock_cooldown = cooldown * pow(GameState.cfg("buffs.mothership_recall.cooldown_factor", 0.5), GameState.buff_count(&"mothership_recall"))


## 放弃出击（长按 K 3s）：自毁，走正常死亡结算（删档/最高分/结算面板）
func _give_up() -> void:
	if _player.is_dead() or GameState.health <= 0.0:
		return
	GameState.lose_health(GameState.health)
	_player.die()


## 返航（局内中场整备）：锁输入、星光拉伸 + 返航过场，过场结束后进入基地控制台。
## 对局继续：不删档（反而更新存档）、Boss 保留、死亡才是唯一终局。
func _start_homecoming() -> void:
	_homecoming = true
	# 返航冻结对局：狂暴子弹时间若在播先复位，避免过场以慢速播放（B2 修复）
	_reset_global_time_scale()
	# C25：返航路径清理蓄力特效残留（蓄力中按 B 返航时虚影/特效不再残留）
	_stop_charging()
	_home_charge_time = 0.0
	_hud.set_home_charge(-1.0)
	_player.lock_input()
	_player.velocity = Vector2.ZERO
	_spawner.set_process(false)
	# 召唤小窗在播则断开回调后关闭（避免 finished 触发穿梭门/母舰创建）
	if _summon_window != null:
		_summon_window.finished.disconnect(_on_summon_window_finished)
		_summon_window.skip()
		_summon_window = null
	# 母舰若在对接/驻留中，直接收回——按基础冷却进冷却（防"补给→返航→再召唤"无限循环）
	if _mothership != null:
		_mothership.queue_free()
		_on_mothership_departed(GameState.cfg("mothership.depart_cooldown", 60.0))
	# 轰炸编队进行中则打断：编队解散离场，无结算，冷却照计
	if _formation != null:
		_formation.abort()
	# 精英炮塔事件同样中止：清炮塔、隐藏事件条、航母完整撤离，Boss 解冻走 BOSS_DELAY
	if _event != null:
		_event.abort()
	# 返航后存档保留更新，供「继续对局」使用
	GameState.save_run(_player.fuel_amount(), _spawner.elapsed())
	_starfield.warp(18.0)  # 保留：过场镜头 1 的充能与星光拉伸自然衔接
	_play_return_cinematic()


## 继续出击：播放轨道打击清场动画（对齐原作 ORBITAL_STRIKE 阶段；树保持暂停）。
## 命中帧（struck）清场并恢复对局，动画结束（finished）仅释放引用。
func _resume_from_base() -> void:
	if _strike != null:
		return
	_strike = OrbitalStrike.new()
	_strike.struck.connect(_on_orbital_struck)
	_strike.finished.connect(_on_orbital_strike_finished)
	add_child(_strike)


## 轨道打击命中：注册表驱动清场——Enemy（含池化）/FormationCraft/事件残留逐机触发爆炸
## 后移除（Boss 保留），再清全部弹丸与编队炸弹，恢复同一局
func _on_orbital_struck() -> void:
	for e in GameState.enemies.duplicate():
		if e is Boss or not is_instance_valid(e):
			continue
		if e is Node2D:
			Explosion.spawn_at(self, (e as Node2D).global_position)
		e.queue_free()
	for child in get_children():
		if child is Bullet or child is FormationBomb:
			child.queue_free()
	_player.unlock_input()
	_homecoming = false
	get_tree().paused = false
	# 继续出击后播战机入场动画：无敌与敌机延迟由入场序列接管（替代原地无敌闪现）
	_start_entry_sequence()


## 入场衔接（开场/继续出击后）：播战机入场动画，敌机生成延迟到动画结束才恢复
func _start_entry_sequence() -> void:
	_spawner.set_process(false)
	if not _player.entry_finished.is_connected(_on_entry_finished):
		_player.entry_finished.connect(_on_entry_finished)
	_player.play_entry_animation()


func _on_entry_finished() -> void:
	_spawner.set_process(true)


func _on_orbital_strike_finished() -> void:
	_strike = null
