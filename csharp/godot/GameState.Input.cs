using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：可改键系统 / 手柄装配。
/// 全部职责由 InputBindingsService（csharp/godot/InputBindingsService.cs，
/// 组合持有；REBINDABLE_ACTIONS/KeyBindings/JoyLayout/PS/XBOX_BUTTON_LABELS 状态与
/// CaptureDefaultBindings/ApplyKeyBindings/EnsureFireBinding/BindJoypadDefaults/DetectJoyLayout/IsPsGuid/JoyButtonLabel/
/// RebindAction/ResetKeyBindings/ActionKeysText/ActionKeyText/ActionBound 方法一并在此），本文件为门面对齐转发——公开 API
/// 签名/语义不变；JoyLayout/PS/XBOX_BUTTON_LABELS 在
/// GameState.State.cs 转发。
/// 信号：KeyBindingsChanged/JoyLayoutChanged 由 InputBindingsService 的 C# 事件经 GameState 订阅
/// 重发（发射点/次数/顺序固定；本门面不再直发，无双发）。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 可改键系统（门面转发 → InputBindingsService） ----------------

    /// <summary>可改键动作清单（restart/pause 固定不可改）。</summary>
    public Godot.Collections.Array<StringName> REBINDABLE_ACTIONS => _input.REBINDABLE_ACTIONS;

    /// <summary>action -> Array[int]（keycode，最多 2 个）；restart/pause 固定不可改</summary>
    public Godot.Collections.Dictionary KeyBindings
    {
        get => _input.KeyBindings;
        set => _input.KeyBindings = value;
    }

    /// <summary>用 KeyBindings（含 settings.json 覆盖）刷新 InputMap</summary>
    public void ApplyKeyBindings() => _input.ApplyKeyBindings();

    /// <summary>Sony 手柄 GUID 判定（SDL GUID：vendor 0x054c 小端序为 "4c05"；PS4/PS5/DualShock/DualSense）</summary>
    public bool IsPsGuid(string guid) => _input.IsPsGuid(guid);

    /// <summary>手柄按钮的物理标签（按当前布局）：PS 用 ✕○□△/L1/R1…，Xbox/SDL 用 A/B/X/Y/LB/RB…</summary>
    public string JoyButtonLabel(int button) => _input.JoyButtonLabel(button);

    /// <summary>改键：清除该动作现有键设新键；冲突键从占用者移除（允许交换）
    /// 冲突清理同时扫默认绑定——未自定义动作的默认键被占用时置空绑定覆盖默认，
    /// 避免 ApplyKeyBindings 从默认表重灌同键造成两动作冲突</summary>
    public bool RebindAction(StringName action, int keycode) => _input.RebindAction(action, keycode);

    public void ResetKeyBindings() => _input.ResetKeyBindings();

    /// <summary>键位占用者查询（改键提示「该键原属 XX」用）；无占用返回空 StringName。</summary>
    public StringName OccupiedBy(int keycode, StringName except) => _input.OccupiedBy(keycode, except);

    public string ActionKeysText(StringName action) => _input.ActionKeysText(action);

    /// <summary>动作的首个绑定键标签（一句提示里只放一个键时用；未绑定返回未绑定文案）</summary>
    public string ActionKeyText(StringName action) => _input.ActionKeyText(action);

    /// <summary>动作的单标签提示（按最近使用设备取档：键鼠＝键名，手柄＝按钮/扳机/摇杆标签）。
    /// 教程目标行与设置页操作速查共用本口——「教程跟设备、设置页不跟」的第二套事实不成立。</summary>
    public string ActionHintText(StringName action) => _input.ActionHintText(action);

    /// <summary>移动提示标签段（手柄档＝左摇杆；键鼠档＝四向首个键名拼段，默认 WASD）。</summary>
    public string MoveHintText() => _input.MoveHintText();

    /// <summary>瞄准提示标签（键鼠＝鼠标；手柄＝右摇杆）。</summary>
    public string AimHintText() => _input.AimHintText();

    /// <summary>提示标签当前是否手柄档（探针断「标签随设备变档」的读数口）。</summary>
    public bool HintsAreGamepad => _input.HintsAreGamepad;

    /// <summary>动作当前是否有生效绑定（提示文本要跳过未绑定方向时用）</summary>
    public bool ActionBound(StringName action) => _input.ActionBound(action);

    /// <summary>观察全部输入事件并按类型映射到设备档（键鼠事件 ⇄ 手柄事件，最近者胜）：
    /// 只观察不消费，档位判定在 core（LastInputDevice），切换经 InputDeviceChanged 广播。
    /// 逐事件只做类型分派——热路径无分配。</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventJoypadButton or InputEventJoypadMotion)
        {
            _input.NoteInputDevice(Core.Input.HintDevice.Gamepad);
        }
        else if (@event is InputEventKey or InputEventMouseButton or InputEventMouseMotion)
        {
            _input.NoteInputDevice(Core.Input.HintDevice.KeyboardMouse);
        }
        // 其余事件类型（触摸板手势等）不参与档位判定
    }
}
