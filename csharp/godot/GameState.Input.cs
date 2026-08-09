using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：可改键系统 / 手柄装配。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 可改键系统 ----------------

    /// <summary>可改键动作清单（restart/pause 固定不可改）。</summary>
    public Godot.Collections.Array<StringName> REBINDABLE_ACTIONS { get; } = new()
    {
        new StringName("move_up"),
        new StringName("move_down"),
        new StringName("move_left"),
        new StringName("move_right"),
        new StringName("boost"),
        new StringName("fine_move"),
        new StringName("dash"),
        new StringName("dock"),
        new StringName("homecoming"),
        new StringName("give_up"),
        new StringName("buff_panel"),
        new StringName("parry"),
    };

    /// <summary>action -> Array[int]（keycode，最多 2 个）；restart/pause 固定不可改</summary>
    public Godot.Collections.Dictionary KeyBindings { get; set; } = new();

    private readonly Godot.Collections.Dictionary _defaultBindings = new();

    private void CaptureDefaultBindings()
    {
        _defaultBindings.Clear();
        foreach (var a in REBINDABLE_ACTIONS)
        {
            _defaultBindings[a] = GetActionKeycodes(a);
        }
    }

    private Godot.Collections.Array<int> GetActionKeycodes(StringName action)
    {
        var outArr = new Godot.Collections.Array<int>();
        foreach (var ev in InputMap.ActionGetEvents(action))
        {
            if (ev is InputEventKey keyEvent)
            {
                var k = keyEvent.Keycode != Key.None ? (int)keyEvent.Keycode : (int)keyEvent.PhysicalKeycode;
                outArr.Add(k);
                if (outArr.Count >= 2)
                {
                    break;
                }
            }
        }

        return outArr;
    }

    /// <summary>用 key_bindings（含 profile 覆盖）刷新 InputMap</summary>
    public void ApplyKeyBindings()
    {
        // H02（健壮性审核）：只擦除键盘事件，保留手柄事件——action_erase_events 会连
        // _bind_joypad_defaults 装配的手柄绑定一起清掉（改键后本会话手柄失效）
        foreach (var a in REBINDABLE_ACTIONS)
        {
            foreach (var ev in InputMap.ActionGetEvents(a))
            {
                if (ev is InputEventKey)
                {
                    InputMap.ActionEraseEvent(a, ev);
                }
            }

            var bindings = KeyBindings.GetValueOrDefault(a, _defaultBindings.GetValueOrDefault(a, new Variant())).AsGodotArray();
            foreach (var k in bindings)
            {
                var ev = new InputEventKey { Keycode = (Key)(int)k.AsInt64() };
                InputMap.ActionAddEvent(a, ev);
            }
        }
    }

    /// <summary>P0-1（竞品调研）：手柄默认绑定运行时装配——project.godot 保持键盘单一事实源，
    /// 手柄左摇杆移动/动作键/右摇杆瞄准在此追加（InputMap.action_add_event），
    /// 与 keybind 改键系统（只改键盘事件）互不覆盖；一次装配幂等。</summary>
    private void BindJoypadDefaults()
    {
        if (_joypadBound)
        {
            return;
        }

        _joypadBound = true;
        // 左摇杆移动（轴 0=x、1=y；axis_value 负=上/左）
        AddJoyAxis("move_up", 1, -1.0);
        AddJoyAxis("move_down", 1, 1.0);
        AddJoyAxis("move_left", 0, -1.0);
        AddJoyAxis("move_right", 0, 1.0);
        // 动作键（B=ui_cancel 已被引擎默认占用，返航让位 Y）
        AddJoyButton("dash", 0); // A
        AddJoyButton("boost", 5); // RB
        AddJoyButton("fine_move", 4); // LB
        AddJoyButton("dock", 2); // X
        AddJoyButton("homecoming", 3); // Y（长按返航）
        AddJoyButton("give_up", 7); // R3（长按放弃）
        AddJoyButton("buff_panel", 6); // L3（展开/收起 buff 栏）
        AddJoyButton("restart", 0); // A（结算/暂停重开）
        AddJoyAxis("parry", 4, -1.0); // LT 左扳机（弧光弹反盾，轴 4 负向按下；阈值经 deadzone）
        // 右摇杆瞄准（player.aim_point 经 Input.get_vector 读取四向动作，虚拟准星）。
        // H01（健壮性审核）：必须装配正负两个独立动作——get_vector(pos, neg) 取 strength 差值，
        // 同一动作正负双向传会恒为零（右摇杆瞄准完全失效）
        AddJoyAxis("aim_left", 2, -1.0);
        AddJoyAxis("aim_right", 2, 1.0);
        AddJoyAxis("aim_up", 3, -1.0);
        AddJoyAxis("aim_down", 3, 1.0);
        // 应用已持久化的摇杆死区（不触发 save/广播，启动装配专用）
        foreach (var a in JOYPAD_ACTIONS)
        {
            if (InputMap.HasAction(a))
            {
                InputMap.ActionSetDeadzone(a, (float)JoyDeadzone);
            }
        }
    }

    private void AddJoyAxis(StringName action, int axis, double value)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }

        var ev = new InputEventJoypadMotion { Axis = (JoyAxis)axis, AxisValue = (float)value };
        InputMap.ActionAddEvent(action, ev);
    }

    private void AddJoyButton(StringName action, int button)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }

        var ev = new InputEventJoypadButton { ButtonIndex = (JoyButton)button };
        InputMap.ActionAddEvent(action, ev);
    }

    /// <summary>PS 布局适配：手柄插拔时重检布局</summary>
    private void OnJoyConnectionChanged(long device, bool connected) => DetectJoyLayout();

    /// <summary>检测已连接手柄的布局：SDL GUID vendor = 0x054c（LE "4c05"）为 Sony（DualShock/DualSense），
    /// 名称含 PlayStation 特征词兜底；其余保持 Xbox/SDL 标准布局（位置语义一致）。</summary>
    private void DetectJoyLayout()
    {
        var found = new StringName();
        foreach (var d in Input.GetConnectedJoypads())
        {
            if (IsPsGuid(Input.GetJoyGuid(d)))
            {
                found = new StringName("ps");
                break;
            }

            var name = Input.GetJoyName(d).ToLowerInvariant();
            if (name.Contains("dualshock") || name.Contains("dualsense") || name.Contains("playstation"))
            {
                found = new StringName("ps");
                break;
            }
        }

        if (found != new StringName() && found != JoyLayout)
        {
            JoyLayout = found;
            EmitSignal(SignalName.JoyLayoutChanged, JoyLayout);
        }
        else if (found == new StringName() && JoyLayout != new StringName("xbox"))
        {
            // 2026-08-03 审计：全部手柄拔出时回落 Xbox/SDL 布局，防 PS 标签残留误导设置页
            JoyLayout = new StringName("xbox");
            EmitSignal(SignalName.JoyLayoutChanged, JoyLayout);
        }
    }

    /// <summary>Sony 手柄 GUID 判定（SDL GUID：vendor 0x054c 小端序为 "4c05"；PS4/PS5/DualShock/DualSense）</summary>
    public bool IsPsGuid(string guid) => guid.StartsWith("030000004c05");

    /// <summary>手柄按钮的物理标签（按当前布局）：PS 用 ✕○□△/L1/R1…，Xbox/SDL 用 A/B/X/Y/LB/RB…</summary>
    public string JoyButtonLabel(int button)
    {
        if (JoyLayout == new StringName("ps"))
        {
            return PS_BUTTON_LABELS.GetValueOrDefault(button, XBOX_BUTTON_LABELS.GetValueOrDefault(button, button.ToString())).ToString();
        }

        return XBOX_BUTTON_LABELS.GetValueOrDefault(button, button.ToString()).ToString();
    }

    /// <summary>改键：清除该动作现有键设新键；冲突键从占用者移除（允许交换）
    /// G04：冲突清理同时扫默认绑定——未自定义动作的默认键被占用时置空绑定覆盖默认，
    /// 避免 apply_key_bindings 从默认表重灌同键造成两动作冲突</summary>
    public bool RebindAction(StringName action, int keycode)
    {
        if (!REBINDABLE_ACTIONS.Contains(action))
        {
            return false;
        }

        foreach (var a in REBINDABLE_ACTIONS)
        {
            if (a == action)
            {
                continue;
            }

            var effective = KeyBindings.GetValueOrDefault(a, _defaultBindings.GetValueOrDefault(a, new Variant())).AsGodotArray();
            if (effective.Count == 0)
            {
                continue; // 空绑定 = 该动作无键，不占用任何键
            }

            if (effective.Contains(keycode))
            {
                if (KeyBindings.ContainsKey(a))
                {
                    KeyBindings[a].AsGodotArray().Remove(keycode);
                }
                else
                {
                    KeyBindings[a] = new Godot.Collections.Array(); // 默认键被占用：空绑定覆盖默认，解除占用
                }
            }
        }

        KeyBindings[action] = new Godot.Collections.Array { keycode };
        ApplyKeyBindings();
        SaveProfile();
        EmitSignal(SignalName.KeyBindingsChanged);
        return true;
    }

    public void ResetKeyBindings()
    {
        KeyBindings = (Godot.Collections.Dictionary)_defaultBindings.Duplicate(true);
        ApplyKeyBindings();
        SaveProfile();
        EmitSignal(SignalName.KeyBindingsChanged);
    }

    public string ActionKeysText(StringName action)
    {
        var keys = KeyBindings.GetValueOrDefault(action, _defaultBindings.GetValueOrDefault(action, new Variant())).AsGodotArray();
        if (keys.Count == 0)
        {
            return (string)Tr("SET_UNBOUND");
        }

        var parts = new List<string>();
        foreach (var k in keys)
        {
            parts.Add(OS.GetKeycodeString((Key)(int)k.AsInt64()));
        }

        return string.Join(" / ", parts);
    }
}
