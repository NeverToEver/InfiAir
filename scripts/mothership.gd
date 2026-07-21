class_name Mothership
extends Area2D
## 母舰补给平台（对齐原作）：长按 H 蓄力召唤（main 管理蓄力）→
## DESCEND 缓动降入 → HOVER 悬停（加特林 80° 扫射压制）→ DOCKING 牵引对接 →
## RESUPPLY 补给 → STAY 驻留 20s（弹匣 10 格，2s/格，≤4 警告，可长按 H 2s 提前离舰，
## 按剩余弹匣比例冷却打折）→ RELEASE 释放 → DEPART 加速离场。
## 母舰弹丸击毁只给 1/3 分（score_scale 标记，结算时向下取整）。

signal departed(cooldown: float)

enum State { DESCEND, HOVER, DOCKING, RESUPPLY, STAY, RELEASE, DEPART }

var HOVER_Y := 270.0
var DOCK_INVINCIBLE := 2.0
var LIVES_CAP := 6.0
var DOCK_TWEEN_TIME := 0.8
var RESUPPLY_DELAY := 0.5
var RELEASE_TIME := 0.5
var RELEASE_DROP := 90.0
# 弹匣：10 格 × 2s = 20s 驻留
var MAG_CELLS := 10
var MAG_CELL_TIME := 2.0
var MAG_WARN_CELLS := 4
var EARLY_HOLD_TIME := 2.0
var EARLY_MAX_DISCOUNT := 0.4  # 冷却最多打 6 折
var DEPART_COOLDOWN := 90.0
# 加特林扫射
var GATLING_INTERVAL := 0.2
var GATLING_BULLET_SPEED := 800.0
var GATLING_DAMAGE := 1
var GATLING_SWEEP_DEG := 40.0  # 单侧摆幅，扇面共 80°
var GATLING_SCORE_SCALE := 1.0 / 3.0
const GATLING_SFX: AudioStream = preload("res://assets/audio/bullet_fire_b.wav")

var _state: State = State.DESCEND
var _state_timer: float = 0.0
var _depart_speed: float = 60.0
var _player: Player = null
# 加特林
var _gatling_timer: float = 0.0
var _sweep_time: float = 0.0
# 驻留弹匣
var _mag_cells: int = MAG_CELLS
var _mag_cell_timer: float = 0.0
var _mag_warned: bool = false
# 提前离舰
var _early_timer: float = 0.0
var _cooldown_factor: float = 1.0

@onready var _beam: Polygon2D = $TractorBeam
@onready var _dock_zone: Area2D = $DockZone
@onready var _turrets: Array[Node2D] = [$TurretL, $TurretR]


func _ready() -> void:
	_dock_zone.area_entered.connect(_on_dock_area_entered)
	# 数值配置缓存（启动一次读入）
	HOVER_Y = GameState.cfg("mothership.hover_y", HOVER_Y)
	DOCK_INVINCIBLE = GameState.cfg("mothership.dock_invincible", DOCK_INVINCIBLE)
	LIVES_CAP = GameState.cfg("mothership.lives_cap", LIVES_CAP)
	DOCK_TWEEN_TIME = GameState.cfg("mothership.dock_tween_time", DOCK_TWEEN_TIME)
	RESUPPLY_DELAY = GameState.cfg("mothership.resupply_delay", RESUPPLY_DELAY)
	RELEASE_TIME = GameState.cfg("mothership.release_time", RELEASE_TIME)
	RELEASE_DROP = GameState.cfg("mothership.release_drop", RELEASE_DROP)
	MAG_CELLS = GameState.cfg("mothership.mag_cells", MAG_CELLS)
	MAG_CELL_TIME = GameState.cfg("mothership.mag_cell_time", MAG_CELL_TIME)
	MAG_WARN_CELLS = GameState.cfg("mothership.mag_warn_cells", MAG_WARN_CELLS)
	EARLY_HOLD_TIME = GameState.cfg("mothership.early_hold_time", EARLY_HOLD_TIME)
	EARLY_MAX_DISCOUNT = GameState.cfg("mothership.early_max_discount", EARLY_MAX_DISCOUNT)
	DEPART_COOLDOWN = GameState.cfg("mothership.depart_cooldown", DEPART_COOLDOWN)
	GATLING_INTERVAL = GameState.cfg("mothership.gatling.interval", GATLING_INTERVAL)
	GATLING_BULLET_SPEED = GameState.cfg("mothership.gatling.bullet_speed", GATLING_BULLET_SPEED)
	GATLING_DAMAGE = GameState.cfg("mothership.gatling.damage", GATLING_DAMAGE)
	GATLING_SWEEP_DEG = GameState.cfg("mothership.gatling.sweep_deg", GATLING_SWEEP_DEG)
	GATLING_SCORE_SCALE = GameState.cfg("mothership.gatling.score_scale", GATLING_SCORE_SCALE)


func state_text() -> String:
	match _state:
		State.DESCEND:
			return "母舰召唤中"
		State.HOVER:
			return "待命补给中"
		State.DOCKING, State.RESUPPLY:
			return "对接中"
		State.STAY:
			return "驻留 %ds" % ceili(_mag_cells * MAG_CELL_TIME - _mag_cell_timer)
		State.RELEASE, State.DEPART:
			return "母舰离场"
	return ""


func _enter_state(p_state: State) -> void:
	_state = p_state
	_state_timer = 0.0


func _physics_process(delta: float) -> void:
	if _beam.visible:
		_beam.modulate.a = 0.7 + 0.3 * sin(Time.get_ticks_msec() / 1000.0 * 8.0)
	_state_timer += delta
	match _state:
		State.DESCEND:
			var remaining := HOVER_Y - position.y
			position.y += clampf(remaining * 2.0, 40.0, 160.0) * delta
			if remaining <= 2.0:
				position.y = HOVER_Y
				_enter_state(State.HOVER)
		State.HOVER:
			_update_gatling(delta)
		State.DOCKING:
			_update_gatling(delta)
			if _state_timer >= DOCK_TWEEN_TIME:
				_enter_state(State.RESUPPLY)
		State.RESUPPLY:
			_update_gatling(delta)
			if _state_timer >= RESUPPLY_DELAY:
				_do_resupply()
				_enter_state(State.STAY)
				# 驻留期间无敌（锁输入期间无敌帧不会衰减）
				if is_instance_valid(_player) and not _player._dead:
					_player._invincible = 999.0
		State.STAY:
			_update_gatling(delta)
			# 弹匣消耗
			_mag_cell_timer += delta
			if _mag_cell_timer >= MAG_CELL_TIME:
				_mag_cell_timer -= MAG_CELL_TIME
				_mag_cells -= 1
				if _mag_cells == MAG_WARN_CELLS and not _mag_warned:
					_mag_warned = true
					var hud := get_tree().get_first_node_in_group("hud")
					if hud != null:
						hud.show_magazine_warning()
			# 提前离舰：长按 H 2s
			if Input.is_action_pressed("dock"):
				_early_timer += delta
			else:
				_early_timer = 0.0
			if _early_timer >= EARLY_HOLD_TIME:
				_early_depart()
			elif _mag_cells <= 0:
				_start_release()
		State.RELEASE:
			_update_gatling(delta)
			if _state_timer >= RELEASE_TIME:
				if is_instance_valid(_player) and not _player._dead:
					_player._input_locked = false
					_player._invincible = DOCK_INVINCIBLE
				_enter_state(State.DEPART)
				departed.emit(DEPART_COOLDOWN * _cooldown_factor)
		State.DEPART:
			_depart_speed += 400.0 * delta
			position.y -= _depart_speed * delta
			if position.y < -350.0:
				queue_free()


## 加特林扫射压制：双炮塔各自按 80° 扇面周期扫掠，非精准点射。
func _update_gatling(delta: float) -> void:
	_sweep_time += delta
	_gatling_timer -= delta
	if _gatling_timer > 0.0:
		return
	_gatling_timer = GATLING_INTERVAL
	for i in _turrets.size():
		var turret := _turrets[i]
		var phase := 0.0 if i == 0 else PI
		var angle := sin(_sweep_time * 2.0 + phase) * deg_to_rad(GATLING_SWEEP_DEG)
		var dir := Vector2.DOWN.rotated(angle)
		turret.global_rotation = dir.angle()
		var b: Bullet = GameState.bullet_pool.fire(dir, GATLING_BULLET_SPEED, GATLING_DAMAGE, true)
		b.score_scale = GATLING_SCORE_SCALE
		b.position = turret.global_position
		# 比玩家弹更细更亮
		b.scale = Vector2(0.6, 0.6)
		b.modulate = Color(1.4, 1.4, 1.1)
		(turret.get_node("MuzzleFlash") as GPUParticles2D).restart()
	GameState.play_sfx(GATLING_SFX, -8.0)


func _on_dock_area_entered(area: Area2D) -> void:
	if _state != State.HOVER or not area.is_in_group("player_hitbox"):
		return
	_start_docking(area.get_parent() as Player)


func _start_docking(player: Player) -> void:
	_player = player
	_enter_state(State.DOCKING)
	_player._input_locked = true
	_player.velocity = Vector2.ZERO
	_beam.visible = true
	# 牵引光束吸附到舰腹正中
	var dock_pos := global_position + Vector2(0.0, 140.0)
	var tween := create_tween()
	tween.tween_property(_player, "global_position", dock_pos, DOCK_TWEEN_TIME) \
		.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)


func _do_resupply() -> void:
	if not is_instance_valid(_player) or _player._dead:
		return
	# 回满生命（基础 3 + 额外生命 buff 层数，封顶 6）
	var full := minf(3.0 + GameState.buff_count(&"extra_life"), LIVES_CAP)
	if GameState.lives < full:
		GameState.heal(full - GameState.lives)
	_player.refill_fuel()
	GameState.play_sfx(GameState.SFX_RESUPPLY)
	GameState.shake(GameState.cfg("effects.shake.mothership", 4.0))
	var hud := get_tree().get_first_node_in_group("hud")
	if hud != null:
		hud.show_popup("补给完成", global_position + Vector2(0.0, 120.0))


## 提前离舰：按剩余弹匣比例返还冷却（最多 -40%）
func _early_depart() -> void:
	var ratio := float(_mag_cells) / float(MAG_CELLS)
	_cooldown_factor = 1.0 - EARLY_MAX_DISCOUNT * ratio
	var hud := get_tree().get_first_node_in_group("hud")
	if hud != null:
		hud.show_popup("提前脱离，冷却 -%d%%" % int(EARLY_MAX_DISCOUNT * ratio * 100.0), global_position)
	_start_release()


func _start_release() -> void:
	_beam.visible = false
	_enter_state(State.RELEASE)
	if not is_instance_valid(_player) or _player._dead:
		return
	var tween := create_tween()
	tween.tween_property(
		_player, "global_position", _player.global_position + Vector2(0.0, RELEASE_DROP), RELEASE_TIME
	)
