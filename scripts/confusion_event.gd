class_name ConfusionEvent
extends FogEvent
## 精神错乱事件：全屏变色覆盖层呼吸脉冲（视觉部分）；玩家输入反转经 manager 统一
## fog_event_started/ended 信号在 player.gd 应用（事件类不触碰 Player）。
## 覆盖层节点由 manager 构建（_build_visual_layers），本事件经 FogEvent 访问器缓存
## 引用后驱动显隐与呼吸（tick 正弦，无 Tween/Timer——随事件 duration 与树暂停自然同步）。
## 健壮性：_on_start 缓存 layer/rect 并判空——context 缺键时降级空转（tick 判空保护，
## 不崩、不产生每帧 null 访问；事件生命周期仍由编排器统一驱动）。

## 呼吸包络（alpha 0.06..0.16，周期 1s；对齐原 Tween 幅度）
const BASE_ALPHA := 0.11
const PULSE_ALPHA := 0.05
const PULSE_PERIOD := 1.0

var _layer: CanvasLayer = null
var _rect: ColorRect = null
var _t := 0.0


func event_id() -> StringName:
	return &"mental_confusion"


func _on_start() -> void:
	_layer = overlay_layer()
	_rect = overlay_rect()
	_t = 0.0
	# P4（2026-08-05）：缺键降级改为空转——原实现自行 end() 使效果 0s 消失但
	# event_ended 延后 duration 才发（信号不同步）；空转保持生命周期由编排器统一驱动
	if _layer != null:
		_layer.visible = true
	if _rect != null:
		_rect.color = Color(0.45, 0.2, 0.9, 0.0)


func _on_tick(delta: float) -> void:
	if _rect == null:
		return  # 缺键空转
	_t += delta
	_rect.color.a = BASE_ALPHA + PULSE_ALPHA * sin(_t / PULSE_PERIOD * TAU)


func _on_end() -> void:
	if _rect != null:
		_rect.color = Color(0.45, 0.2, 0.9, 0.0)
	if _layer != null:
		_layer.visible = false
