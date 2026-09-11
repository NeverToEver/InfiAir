using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：可改键系统 / 手柄装配。
/// 全部职责由 InputBindingsService（csharp/godot/InputBindingsService.cs，
/// 组合持有；REBINDABLE_ACTIONS/KeyBindings/JoyLayout/JOYPAD_ACTIONS/PS/XBOX_BUTTON_LABELS 状态与
/// CaptureDefaultBindings/ApplyKeyBindings/EnsureFireBinding/BindJoypadDefaults/DetectJoyLayout/IsPsGuid/JoyButtonLabel/
/// RebindAction/ResetKeyBindings/ActionKeysText 方法一并在此），本文件为门面对齐转发——公开 API
/// 签名/语义不变；JOYPAD_ACTIONS/JoyLayout/PS/XBOX_BUTTON_LABELS 在
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

    public string ActionKeysText(StringName action) => _input.ActionKeysText(action);
}
