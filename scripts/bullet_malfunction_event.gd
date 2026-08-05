class_name BulletMalfunctionEvent
extends FogEvent
## 子弹错误事件：效果全部在玩家侧（出膛弹角度偏移 / 慢速失误弹 / 射速扰动），
## 经 manager 统一 fog_event_started/ended 信号在 player.gd 应用（参数读 balance
## fog_events.bullet_malfunction.*）。
## 本类无自持效果——作为注册表条目占位；未来若需 HUD 提示等视觉，在 start/end 挂接即可。


func event_id() -> StringName:
	return &"bullet_malfunction"
