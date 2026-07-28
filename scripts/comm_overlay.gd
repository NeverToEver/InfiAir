class_name CommOverlay
extends CanvasLayer
## 精英炮塔事件·通讯浮层（docs/ELITE_TURRET_EVENT.md 第 4 节）：
## 屏幕左下角六边切角通讯框（品红描边）+ 打字机字幕，显示 3.5s 后淡出；
## 不暂停游戏（process_mode 跟随对局）；新台词顶掉未播完的旧台词。

const COMM_SFX: AudioStream = preload("res://assets/audio/bullet_fire_c.wav")
const CHAR_INTERVAL := 0.03  # 打字机字间隔
const HOLD_TIME := 3.5
const FADE_TIME := 0.5

var _panel: ChamferedPanel
var _label: Label
var _full_text: String = ""
var _char_t: float = 0.0
var _shown_chars: int = 0
var _hold_left: float = -1.0  # <0：打字中


func _init() -> void:
	layer = 12
	_panel = ChamferedPanel.new()
	_panel.position = Vector2(24.0, 760.0)
	_panel.size = Vector2(760.0, 96.0)
	_panel.bg_color = Color(0.10, 0.03, 0.09, 0.78)
	_panel.border_color = Color(1.0, 0.25, 0.75, 0.6)  # 精英品红描边
	_panel.bracket_color = Color(1.0, 0.25, 0.75)
	_panel.brackets = true
	_panel.visible = false
	add_child(_panel)
	_label = UITheme.make_label("", UITheme.FONT_BODY - 2, UITheme.TEXT, HORIZONTAL_ALIGNMENT_LEFT)
	_label.position = Vector2(20.0, 14.0)
	_label.custom_minimum_size = Vector2(720.0, 68.0)
	_label.size = Vector2(720.0, 68.0)
	_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_panel.add_child(_label)


## 播放一句台词（翻译键）：新台词顶掉未播完的旧台词
func show_line(key: String) -> void:
	_full_text = tr(key)
	_shown_chars = 0
	_char_t = 0.0
	_hold_left = -1.0
	_label.text = ""
	_panel.modulate.a = 1.0
	_panel.visible = true
	GameState.play_sfx(COMM_SFX, -10.0)


func _process(delta: float) -> void:
	if not _panel.visible:
		return
	if _hold_left < 0.0:
		# 打字机
		_char_t += delta
		while _char_t >= CHAR_INTERVAL and _shown_chars < _full_text.length():
			_char_t -= CHAR_INTERVAL
			_shown_chars += 1
		_label.text = _full_text.left(_shown_chars)
		if _shown_chars >= _full_text.length():
			_hold_left = HOLD_TIME
	else:
		_hold_left -= delta
		if _hold_left <= 0.0:
			_hold_left = FADE_TIME + 1.0  # 进入淡出段（复用同一计时）
			var tween := create_tween()
			tween.tween_property(_panel, "modulate:a", 0.0, FADE_TIME)
			tween.tween_callback(_panel.hide)
