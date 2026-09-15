using Godot;
using InfiAir.Core.Input;

namespace InfiAir;

/// <summary>
/// 键位+手柄域服务：可改键系统/手柄装配与
/// PS/XBOX_BUTTON_LABELS/JoyLayout。
/// 状态：REBINDABLE_ACTIONS/KeyBindings/_defaultBindings/JoyLayout/_joypadBound；
/// 方法：CaptureDefaultBindings/GetActionKeycodes/ApplyKeyBindings/EnsureFireBinding/BindJoypadDefaults/
/// AddJoyAxis/AddJoyButton/OnJoyConnectionChanged/DetectJoyLayout/IsPsGuid/JoyButtonLabel/RebindAction/
/// ResetKeyBindings/ActionKeysText。
/// Godot 绑定层：跨域访问统一经 GameState.Instance——SaveSettings（RebindAction/ResetKeyBindings
/// 持久化）经门面；Tr 为 GodotObject 实例方法（RefCounted 继承链可用），ActionKeysText 保持直调。
/// GameState 组合持有本服务并做门面对齐转发（签名/语义不变），保持唯一 autoload：GameState 约定。
/// 信号：本服务以 C# 事件 KeyBindingsChanged/JoyLayoutChanged 通知；GameState 订阅后转发为
/// 同名信号（发射点/次数/顺序恒定——RebindAction/ResetKeyBindings
/// 各 1 处 KeyBindingsChanged，DetectJoyLayout 2 处 JoyLayoutChanged；GameState 侧门面不再
/// 直发，无双发）。
/// </summary>
public sealed partial class InputBindingsService : RefCounted
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
        new StringName("augment_panel"),
        new StringName("parry"),
    };

    /// <summary>action -> Array[int]（keycode，最多 2 个）；restart/pause 固定不可改</summary>
    public Godot.Collections.Dictionary KeyBindings { get; set; } = new();

    private readonly Godot.Collections.Dictionary _defaultBindings = new();

    /// <summary>手柄默认绑定装配标志（幂等，避免重载重复追加）。</summary>
    private bool _joypadBound;

    /// <summary>手柄布局（默认 Xbox/SDL 标准名；检测到 Sony 手柄切 &amp;"ps"）。</summary>
    public StringName JoyLayout { get; set; } = new StringName("xbox");

    /// <summary>Xbox/SDL 布局手柄按钮物理标签（SDL 标准位置）。</summary>
    public Godot.Collections.Dictionary XBOX_BUTTON_LABELS { get; } = new()
    {
        [0] = "A",
        [1] = "B",
        [2] = "X",
        [3] = "Y",
        [4] = "LB",
        [5] = "RB",
        [6] = "LS",
        [7] = "RS",
    };

    /// <summary>PS 布局手柄按钮物理标签。</summary>
    public Godot.Collections.Dictionary PS_BUTTON_LABELS { get; } = new()
    {
        [0] = "✕",
        [1] = "○",
        [2] = "□",
        [3] = "△",
        [4] = "L1",
        [5] = "R1",
        [6] = "L3",
        [7] = "R3",
    };

    /// <summary>摇杆 action（左杆移动 + 右杆瞄准）：InputMap deadzone 恒为滤噪小常数——
    /// 用户死区改由读取侧 StickShaper 径向生效（GetVector 输出即近似 raw）。</summary>
    private static readonly StringName[] StickActions =
    {
        new("move_up"),
        new("move_down"),
        new("move_left"),
        new("move_right"),
        new("aim_left"),
        new("aim_right"),
        new("aim_up"),
        new("aim_down"),
    };

    /// <summary>扳机 action（轴当按钮用，触发阈值 = InputMap deadzone）：轻扣即触发，
    /// 与摇杆死区解耦、不受设置页死区滑杆影响。</summary>
    private static readonly StringName[] TriggerActions =
    {
        new("parry"),
        new("fire"),
    };

    /// <summary>摇杆 InputMap 死区常数（只滤硬件噪声；用户死区在读取侧径向生效）。</summary>
    private const float StickDeadzone = 0.05f;

    /// <summary>扳机触发阈值常数（轴强度越过即触发，业界手柄射击惯例）。</summary>
    private const float TriggerDeadzone = 0.2f;

    /// <summary>开火动作名（不在 REBINDABLE_ACTIONS——开火无键盘绑定，只装配鼠标左键/手柄扳机）。</summary>
    private static readonly StringName FireAction = new("fire");

    // ---------------- 信号 C# 事件 ----------------

    /// <summary>键位变更（改键/恢复默认）；GameState 订阅后转发为 KeyBindingsChanged 信号。</summary>
    public event Action? KeyBindingsChanged;

    /// <summary>手柄布局变更（检测/拔出回落）；GameState 订阅后转发为 JoyLayoutChanged 信号。</summary>
    public event Action<StringName>? JoyLayoutChanged;

    // ---------------- 可改键系统方法 ----------------

    /// <summary>启动默认键位快照（_ready 首调；改键冲突/恢复默认的数据源）</summary>
    public void CaptureDefaultBindings()
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

    /// <summary>用 KeyBindings（含 settings.json 覆盖）刷新 InputMap</summary>
    public void ApplyKeyBindings()
    {
        // 只擦除键盘事件，保留手柄事件——action_erase_events 会连
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

    /// <summary>开火动作装配（幂等）：鼠标左键事件在运行时注册——开火无键盘绑定，
    /// project.godot 只承载可改键的键盘默认值，鼠标/手柄两路同层装配
    /// （Player 读 Input.is_action_pressed(&amp;"fire") 的路径因此零特判）。
    /// 幂等判定按「已存在左键事件」而非布尔标志——重进树/热重载不重复追加。</summary>
    public void EnsureFireBinding()
    {
        if (!InputMap.HasAction(FireAction))
        {
            InputMap.AddAction(FireAction);
        }

        foreach (var ev in InputMap.ActionGetEvents(FireAction))
        {
            if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                return;
            }
        }

        InputMap.ActionAddEvent(FireAction, new InputEventMouseButton { ButtonIndex = MouseButton.Left });
    }

    /// <summary>手柄默认绑定运行时装配——project.godot 保持键盘单一事实源，
    /// 手柄左摇杆移动/动作键/右摇杆瞄准在此追加（InputMap.action_add_event），
    /// 与 keybind 改键系统（只改键盘事件）互不覆盖；一次装配幂等。</summary>
    public void BindJoypadDefaults()
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
        AddJoyButton("augment_panel", 6); // L3（展开/收起增幅栏）
        AddJoyButton("restart", 0); // A（结算/暂停重开）
        AddJoyAxis("parry", 4, -1.0); // LT 左扳机（弧光弹反盾，轴 4 负向按下；阈值经 deadzone）
        AddJoyAxis("fire", 5, 1.0); // RT 右扳机（轴 5 正向；与 LT 弹反同族，阈值经 deadzone）
        // 右摇杆瞄准（player.aim_point 经 Input.get_vector 读取四向动作，虚拟准星）。
        // 必须装配正负两个独立动作——get_vector(pos, neg) 取 strength 差值，
        // 同一动作正负双向传会恒为零（右摇杆瞄准完全失效）
        AddJoyAxis("aim_left", 2, -1.0);
        AddJoyAxis("aim_right", 2, 1.0);
        AddJoyAxis("aim_up", 3, -1.0);
        AddJoyAxis("aim_down", 3, 1.0);
        // 左摇杆导航全部引擎焦点链 UI（ui_* 追加轴绑定；只追加，引擎默认键盘/dpad 绑定保留）
        AddJoyAxis("ui_up", 1, -1.0);
        AddJoyAxis("ui_down", 1, 1.0);
        AddJoyAxis("ui_left", 0, -1.0);
        AddJoyAxis("ui_right", 0, 1.0);
        // 分域死区（常数，与设置页死区滑杆解耦）：摇杆只滤硬件噪声，用户死区由读取侧
        // StickShaper 径向整形生效；扳机 0.2 轻扣即触发。按钮维持引擎默认。
        foreach (var a in StickActions)
        {
            if (InputMap.HasAction(a))
            {
                InputMap.ActionSetDeadzone(a, StickDeadzone);
            }
        }

        foreach (var a in TriggerActions)
        {
            if (InputMap.HasAction(a))
            {
                InputMap.ActionSetDeadzone(a, TriggerDeadzone);
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

    /// <summary>PS 布局适配：手柄插拔时重检布局（GameState._ready 订阅 Input.JoyConnectionChanged）</summary>
    public void OnJoyConnectionChanged(long device, bool connected) => DetectJoyLayout();

    /// <summary>检测已连接手柄的布局：SDL GUID vendor = 0x054c（LE "4c05"）为 Sony（DualShock/DualSense），
    /// 名称含 PlayStation 特征词兜底；其余保持 Xbox/SDL 标准布局（位置语义一致）。</summary>
    public void DetectJoyLayout()
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
            JoyLayoutChanged?.Invoke(JoyLayout);
        }
        else if (found == new StringName() && JoyLayout != new StringName("xbox"))
        {
            // 全部手柄拔出时回落 Xbox/SDL 布局，防 PS 标签残留误导设置页
            JoyLayout = new StringName("xbox");
            JoyLayoutChanged?.Invoke(JoyLayout);
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

    /// <summary>改键：清除该动作现有键设新键；冲突键从占用者移除（允许交换），占用者其余绑定保留。
    /// 固定动作（talent_panel/restart）与引擎 ui_* 保留键在此拒绝（返回 false）——这些动作无
    /// 可改入口，被抢占后对应功能静默按不出来。冲突清理的口径单源在 core 层 KeyBindingConflict：
    /// 占用者的生效绑定来自默认表时，写回的是「默认表减去该键」——写空数组会连带打掉同一动作的
    /// 另一个默认键（移动动作均为双键默认值），而不写覆盖又会让 apply_key_bindings 从默认表重灌同键。</summary>
    public bool RebindAction(StringName action, int keycode)
    {
        if (!REBINDABLE_ACTIONS.Contains(action) || ReservedBy(keycode) != new StringName())
        {
            return false;
        }

        // 快照成纯 .NET 表喂 core 纯函数（Godot 容器不参与判定；两张源表不被就地修改）
        var overrides = KeyBindings.ToDictionary(
            kv => kv.Key.AsStringName().ToString(),
            kv => kv.Value.AsGodotArray().Select(k => (int)k.AsInt64()).ToArray());
        var defaults = new Dictionary<string, int[]>();
        var actionNames = new List<string>();
        foreach (var a in REBINDABLE_ACTIONS)
        {
            var name = a.ToString();
            actionNames.Add(name);
            defaults[name] = _defaultBindings.GetValueOrDefault(a, new Variant()).AsGodotArray()
                .Select(k => (int)k.AsInt64()).ToArray();
        }

        foreach (var (a, keys) in KeyBindingConflict.Cleanup(defaults, overrides, actionNames, action.ToString(), keycode))
        {
            var arr = new Godot.Collections.Array();
            foreach (var k in keys)
            {
                arr.Add(k);
            }

            KeyBindings[new StringName(a)] = arr;
        }

        ApplyKeyBindings();
        GameState.Instance.SaveSettings();
        KeyBindingsChanged?.Invoke();
        return true;
    }

    public void ResetKeyBindings()
    {
        KeyBindings = (Godot.Collections.Dictionary)_defaultBindings.Duplicate(true);
        ApplyKeyBindings();
        GameState.Instance.SaveSettings();
        KeyBindingsChanged?.Invoke();
    }

    /// <summary>键位占用者查询（改键前提示用）：返回当前已绑该键的**可改键**动作，无占用返回空 StringName。
    /// 遍历口径与 RebindAction 的冲突清理一致（有效绑定 = KeyBindings 覆盖，否则默认表）。
    /// 固定/ui_* 动作的占用另经 <see cref="ReservedBy"/> 判定（不可抢占，只能拒绝）。</summary>
    public StringName OccupiedBy(int keycode, StringName except)
    {
        foreach (var a in REBINDABLE_ACTIONS)
        {
            if (a == except)
            {
                continue;
            }

            var effective = KeyBindings.GetValueOrDefault(a, _defaultBindings.GetValueOrDefault(a, new Variant())).AsGodotArray();
            if (effective.Contains(keycode))
            {
                return a;
            }
        }

        return new StringName();
    }

    /// <summary>
    /// 保留键查询：返回占用该键的不可改动作（固定动作 talent_panel/restart 等，或引擎 ui_*），
    /// 无占用返回空 StringName。这些动作没有改键入口，键被改键系统抢走后对应功能静默失效
    /// （表现为「按不出来」，既不崩也不报错），故 RebindAction 直接拒绝。
    /// ui_* 与游戏动作**共用**的键（引擎默认方向键/空格，project.godot 里移动与冲刺本就用它们）
    /// 不算保留——按设计允许重叠，一刀切会把「把移动键改回方向键」也挡掉。
    /// </summary>
    public StringName ReservedBy(int keycode)
    {
        if (keycode == 0)
        {
            return new StringName();
        }

        foreach (var (key, action) in ReservedKeycodes())
        {
            if (key == keycode)
            {
                return action;
            }
        }

        return new StringName();
    }

    /// <summary>保留键表缓存（首次查询时从 InputMap 实际绑定构建；固定动作与 ui_* 的键集合
    /// 运行期不再变化——改键只动可改键动作）。</summary>
    private List<(int Key, StringName Action)>? _reservedKeycodes;

    private List<(int Key, StringName Action)> ReservedKeycodes()
    {
        if (_reservedKeycodes != null)
        {
            return _reservedKeycodes;
        }

        // 游戏动作已按设计占用的键（默认绑定）——ui_* 与它们的重叠是既有约定，不算保留
        var gameplayKeys = new HashSet<int>();
        foreach (var a in REBINDABLE_ACTIONS)
        {
            foreach (var k in _defaultBindings.GetValueOrDefault(a, new Variant()).AsGodotArray())
            {
                gameplayKeys.Add((int)k.AsInt64());
            }
        }

        var reserved = new List<(int, StringName)>();
        foreach (var action in InputMap.GetActions())
        {
            if (REBINDABLE_ACTIONS.Contains(action) || action == FireAction)
            {
                continue;
            }

            var isUi = action.ToString().StartsWith("ui_", StringComparison.Ordinal);
            foreach (var ev in InputMap.ActionGetEvents(action))
            {
                if (ev is not InputEventKey keyEvent)
                {
                    continue;
                }

                var kc = keyEvent.Keycode != Key.None ? (int)keyEvent.Keycode : (int)keyEvent.PhysicalKeycode;
                if (kc == 0 || (isUi && gameplayKeys.Contains(kc)))
                {
                    continue; // ui_* 与游戏动作共用键（方向键/空格）：按设计允许重叠
                }

                reserved.Add((kc, action));
            }
        }

        _reservedKeycodes = reserved;
        return reserved;
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
