class_name PlayerDamage
extends RefCounted
## A8 拆分：玩家受击减免 + 回血（docs/AUDIT_VAULT.md A8）。
## 持无敌/单帧守卫/受击延迟计时；受击结算与回血逻辑自本类。
## 经 Player 属性转发（Player._invincible 等语法不变，测试白盒兼容）与 GameState 全局交互，
## 不访问 Player 私有字段（A1 约束）。

var invincible: float = 0.0
var last_hit_frame: int = -1
var since_damage: float = 999.0

var INVINCIBLE_TIME := 1.5
var ARMOR_MULT := 0.85
var EVASION_CHANCE := 0.2
var REGEN_PER_SEC := 2.0
var SHAKE_HIT := 12.0


func configure(invincible_time: float, armor_mult: float, evasion_chance: float, regen_per_sec: float, shake_hit: float) -> void:
	INVINCIBLE_TIME = invincible_time
	ARMOR_MULT = armor_mult
	EVASION_CHANCE = evasion_chance
	REGEN_PER_SEC = regen_per_sec
	SHAKE_HIT = shake_hit


func set_invincible(seconds: float) -> void:
	invincible = seconds


func invincible_remaining() -> float:
	return invincible


## 受击结算（100 HP 制）。返回 true = 本帧实际结算（调用方据此决定子弹是否销毁）。
## 减免两段式（去 bug 统一版）：先 20% 闪避，再护甲 ×0.85；对全部伤害源生效。
## from_pos：伤害源世界坐标（Meta HUD 定向波纹）；Vector2.INF = 无方向（均匀环）。
func take_damage(amount: float, from_pos: Vector2, player: Player) -> bool:
	if player.is_dead() or invincible > 0.0 or player.is_dashing():
		return false
	# A16：单帧至多结算一次受击（敌弹/敌机撞/Boss 撞共用）
	if Engine.get_physics_frames() == last_hit_frame:
		return false
	# 闪避 buff：20% 完全免伤（不置无敌、不清弹）
	if GameState.buff_count(&"evasion") > 0 and randf() < EVASION_CHANCE:
		return false
	# 护盾 buff（2026-08-04）：每层吸收一次全额伤害——扣层并销毁子弹，不置无敌/不清弹/
	# 不掉血（盾碎后下一发照常结算）；吸收反馈轻震屏。
	# 2026-08-06 审计登记：吸收分支有意不写 last_hit_frame——同帧多弹命中时每层吸收
	# 一发（「每层吸收一次」语义优先）；若计入 A16 单帧守卫则同帧第二弹被拦截免费，
	# 盾层数与弹数消耗不对称（hit_logic_test 同帧连打回归）。概率极低，维持现状登记
	if GameState.buff_count(&"shield") > 0:
		GameState.consume_buff(&"shield")
		GameState.shake(2.0)
		return true
	# 护甲 buff：固定 ×0.85 减伤
	if GameState.buff_count(&"armor") > 0:
		amount *= ARMOR_MULT
	last_hit_frame = Engine.get_physics_frames()
	since_damage = 0.0
	invincible = INVINCIBLE_TIME
	GameState.play_sfx(GameState.SFX_PLAYER_HIT)
	GameState.shake(SHAKE_HIT)
	GameState.lose_health(amount)
	GameState.player_damaged.emit(amount, from_pos)  # Meta HUD 受击层（减免后最终值）
	player.clear_nearby_enemy_bullets()
	if GameState.health <= 0.0:
		player.die()
	return true


## 回血 tick：regen buff 固定 +2 HP/s；无 buff 时被动回血——距上次受伤 delay 秒起按难度速率回复
func heal_tick(delta: float) -> void:
	since_damage += delta
	if GameState.buff_count(&"regen") > 0:
		GameState.heal(REGEN_PER_SEC * delta)
	elif since_damage >= GameState.passive_regen_delay():
		GameState.heal(GameState.passive_regen_rate() * delta)
