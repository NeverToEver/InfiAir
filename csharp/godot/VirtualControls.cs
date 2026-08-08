using Godot;

namespace InfiAir;

/// <summary>
/// 触屏虚拟输入层（mobile touch，docs/archive/2026-08-07-deferred-restart-plan.md §3）：
/// 左虚拟摇杆 → move_*，右虚拟摇杆 → aim_*（增量，与手柄右摇杆虚拟准星同语义），
/// 虚拟按钮 → boost / fine_move / dash / parry。
/// 注入路径：Input.action_press/release（等价 InputEventAction）——player 的
/// Input.get_vector / is_action_pressed 读取路径零改动；仅输入目标为 action 状态，
/// 与手柄/键鼠事件互不覆盖（无摇杆输入时零注入，桌面零回归）。
/// 启用：设置「触控控件」开关（GameState.touch_controls，profile 持久化），
/// Main._ready 创建本层并联动开关（touch_controls_changed 信号）。
/// M5 全量迁移（2026-08-08 自 scripts/virtual_controls.gd）。
/// </summary>
public partial class VirtualControls : CanvasLayer
{
    /// <summary>HUD(2) 之下、世界之上：不遮挡 HUD，半透明</summary>
    private const int LayerId = 1;

    /// <summary>布局（1920×1080 设计坐标；canvas_items stretch 下随窗口缩放）</summary>
    private static readonly Vector2 MoveCenter = new(240.0f, 860.0f);
    private const float MoveRadius = 150.0f;
    private static readonly Vector2 AimCenter = new(1680.0f, 860.0f);
    private const float AimRadius = 150.0f;
    /// <summary>摇杆命中放宽：基座半径 ×1.4（手指比鼠标粗）</summary>
    private const float ZoneFudge = 1.4f;
    /// <summary>摇杆死区（归一化，按下起点周围静止区）</summary>
    private const float Deadzone = 0.15f;

    /// <summary>虚拟按钮：action(StringName) -&gt; {center, radius}（屏幕位置常量）</summary>
    private static readonly Godot.Collections.Dictionary Buttons = new()
    {
        [new StringName("boost")] = new Godot.Collections.Dictionary { ["center"] = new Vector2(520.0f, 560.0f), ["radius"] = 60.0f },
        [new StringName("fine_move")] = new Godot.Collections.Dictionary { ["center"] = new Vector2(660.0f, 700.0f), ["radius"] = 52.0f },
        [new StringName("dash")] = new Godot.Collections.Dictionary { ["center"] = new Vector2(1500.0f, 620.0f), ["radius"] = 62.0f },
        [new StringName("parry")] = new Godot.Collections.Dictionary { ["center"] = new Vector2(1780.0f, 620.0f), ["radius"] = 62.0f },
    };

    private static readonly StringName[] MoveActions =
    {
        new StringName("move_left"), new StringName("move_right"),
        new StringName("move_up"), new StringName("move_down"),
    };

    private static readonly StringName[] AimActions =
    {
        new StringName("aim_left"), new StringName("aim_right"),
        new StringName("aim_up"), new StringName("aim_down"),
    };

    // 区类型/按钮 action 的 StringName 常量（GDScript &"..." 等价，避免逐调用 new）
    private static readonly StringName ZoneMoveName = new("move");
    private static readonly StringName ZoneAimName = new("aim");
    private static readonly StringName ZoneButtonName = new("button");
    private static readonly StringName BoostName = new("boost");
    private static readonly StringName FineMoveName = new("fine_move");
    private static readonly StringName DashName = new("dash");
    private static readonly StringName ParryName = new("parry");
    private static readonly StringName MoveLeftName = new("move_left");
    private static readonly StringName MoveRightName = new("move_right");
    private static readonly StringName MoveUpName = new("move_up");
    private static readonly StringName MoveDownName = new("move_down");
    private static readonly StringName AimLeftName = new("aim_left");
    private static readonly StringName AimRightName = new("aim_right");
    private static readonly StringName AimUpName = new("aim_up");
    private static readonly StringName AimDownName = new("aim_down");

    private bool _enabled;
    private int _moveTouch = -1;
    private Vector2 _moveBase = Vector2.Zero;
    private Vector2 _moveVec = Vector2.Zero;
    private int _aimTouch = -1;
    private Vector2 _aimBase = Vector2.Zero;
    private Vector2 _aimVec = Vector2.Zero;
    private readonly Godot.Collections.Dictionary _buttons = new(); // action(StringName) -> touch index
    private Control _ui = null!;

    public void SetEnabled(bool v)
    {
        if (_enabled == v)
        {
            return;
        }

        _enabled = v;
        if (_ui != null)
        {
            _ui.Visible = v;
            _ui.QueueRedraw();
        }

        if (!v)
        {
            ReleaseAll();
        }
    }

    public bool IsEnabled() => _enabled;

    /// <summary>触屏瞄准基准（无鼠标时瞄准点起点）：可见世界中心。
    /// player.aim_point() 在虚拟控件启用时以它为 raw 基准，右摇杆增量偏移（同手柄语义）。</summary>
    public Vector2 BaseAimPosition() => GameStateBridge.Call("view_world_rect").AsRect2().GetCenter();

    /// <summary>当前左摇杆向量（0..1，测试/诊断）</summary>
    public Vector2 MoveVec() => _moveVec;

    /// <summary>当前右摇杆向量（0..1，测试/诊断）</summary>
    public Vector2 AimVec() => _aimVec;

    public override void _Ready()
    {
        Layer = LayerId;
        ProcessMode = Node.ProcessModeEnum.Always; // 暂停菜单打开时也能接收/清理触摸
        // 注入目标 action 必须存在（aim_* 由手柄装配运行时注册，无手柄环境不存在）
        foreach (var a in MoveActions)
        {
            if (!InputMap.HasAction(a))
            {
                InputMap.AddAction(a);
            }
        }

        foreach (var a in AimActions)
        {
            if (!InputMap.HasAction(a))
            {
                InputMap.AddAction(a);
            }
        }

        foreach (var key in Buttons.Keys)
        {
            var a = key.AsStringName();
            if (!InputMap.HasAction(a))
            {
                InputMap.AddAction(a);
            }
        }

        _ui = new Control();
        _ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.MouseFilter = Control.MouseFilterEnum.Ignore; // 不拦截下方 GUI/世界
        _ui.Draw += OnUiDraw;
        _ui.Visible = false;
        AddChild(_ui);
    }

    public override void _ExitTree()
    {
        if (_enabled)
        {
            ReleaseAll();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!_enabled)
        {
            return;
        }

        if (@event is InputEventScreenTouch touch)
        {
            OnTouch(touch.Index, touch.Pressed, touch.Position);
        }
        else if (@event is InputEventScreenDrag drag)
        {
            OnDrag(drag.Index, drag.Position);
        }
    }

    private void OnTouch(int idx, bool pressed, Vector2 pos)
    {
        if (pressed)
        {
            var zone = HitZone(pos);
            if (zone.Count == 0)
            {
                return;
            }

            var type = zone["type"].AsStringName();
            if (type == ZoneMoveName)
            {
                if (_moveTouch == -1)
                {
                    _moveTouch = idx;
                    _moveBase = pos;
                    UpdateMoveVec(pos);
                }
            }
            else if (type == ZoneAimName)
            {
                if (_aimTouch == -1)
                {
                    _aimTouch = idx;
                    _aimBase = pos;
                    UpdateAimVec(pos);
                }
            }
            else
            {
                var action = zone["action"].AsStringName();
                if (!_buttons.ContainsKey(action))
                {
                    _buttons[action] = idx;
                    Inject(action, 1.0f);
                    _ui.QueueRedraw();
                }
            }
        }
        else
        {
            if (idx == _moveTouch)
            {
                _moveTouch = -1;
                _moveVec = Vector2.Zero;
                InjectMove(Vector2.Zero); // 清残留
                _ui.QueueRedraw();
            }
            else if (idx == _aimTouch)
            {
                _aimTouch = -1;
                _aimVec = Vector2.Zero;
                InjectAim(Vector2.Zero);
                _ui.QueueRedraw();
            }
            else
            {
                // Keys 返回副本数组：遍历中 Remove 安全（等价 GDScript _buttons.duplicate() 遍历）
                foreach (var key in _buttons.Keys)
                {
                    var action = key.AsStringName();
                    if (_buttons[action].AsInt64() == idx)
                    {
                        _buttons.Remove(action);
                        Inject(action, 0.0f);
                        _ui.QueueRedraw();
                    }
                }
            }
        }
    }

    private void OnDrag(int idx, Vector2 pos)
    {
        if (idx == _moveTouch)
        {
            UpdateMoveVec(pos);
        }
        else if (idx == _aimTouch)
        {
            UpdateAimVec(pos);
        }
    }

    private Godot.Collections.Dictionary HitZone(Vector2 pos)
    {
        foreach (var key in Buttons.Keys)
        {
            var action = key.AsStringName();
            var b = (Godot.Collections.Dictionary)Buttons[action];
            if (pos.DistanceTo(b["center"].AsVector2()) <= (float)b["radius"].AsDouble())
            {
                return new Godot.Collections.Dictionary
                {
                    ["type"] = (Variant)ZoneButtonName,
                    ["action"] = (Variant)action,
                };
            }
        }

        if (pos.DistanceTo(MoveCenter) <= MoveRadius * ZoneFudge)
        {
            return new Godot.Collections.Dictionary { ["type"] = (Variant)ZoneMoveName };
        }

        if (pos.DistanceTo(AimCenter) <= AimRadius * ZoneFudge)
        {
            return new Godot.Collections.Dictionary { ["type"] = (Variant)ZoneAimName };
        }

        return new Godot.Collections.Dictionary();
    }

    private void UpdateMoveVec(Vector2 pos)
    {
        _moveVec = StickVec(_moveBase, pos, MoveRadius);
        InjectMove(_moveVec);
        _ui.QueueRedraw();
    }

    private void UpdateAimVec(Vector2 pos)
    {
        _aimVec = StickVec(_aimBase, pos, AimRadius);
        InjectAim(_aimVec);
        _ui.QueueRedraw();
    }

    /// <summary>摇杆向量：位移/半径归一化（死区截断、限幅 1）；超半径时基座跟随手指由调用方
    /// _update_* 的 base 重锚处理，此处只算当前向量。</summary>
    private Vector2 StickVec(Vector2 basePos, Vector2 pos, float radius)
    {
        var v = pos - basePos;
        var dist = v.Length();
        var nv = dist > 1.0f ? v / radius : Vector2.Zero;
        if (nv.Length() < Deadzone)
        {
            return Vector2.Zero;
        }

        return nv.LimitLength(1.0f);
    }

    private void InjectMove(Vector2 v)
    {
        // 静止零注入（触屏设备无键盘，player 读到的即本层注入；桌面不开本层零回归）
        if (v == Vector2.Zero)
        {
            foreach (var a in MoveActions)
            {
                Inject(a, 0.0f);
            }

            return;
        }

        Inject(MoveLeftName, Mathf.Max(-v.X, 0.0f));
        Inject(MoveRightName, Mathf.Max(v.X, 0.0f));
        Inject(MoveUpName, Mathf.Max(-v.Y, 0.0f));
        Inject(MoveDownName, Mathf.Max(v.Y, 0.0f));
    }

    private void InjectAim(Vector2 v)
    {
        if (v == Vector2.Zero)
        {
            foreach (var a in AimActions)
            {
                Inject(a, 0.0f);
            }

            return;
        }

        Inject(AimLeftName, Mathf.Max(-v.X, 0.0f));
        Inject(AimRightName, Mathf.Max(v.X, 0.0f));
        Inject(AimUpName, Mathf.Max(-v.Y, 0.0f));
        Inject(AimDownName, Mathf.Max(v.Y, 0.0f));
    }

    private void Inject(StringName action, float strength)
    {
        if (strength > 0.0f)
        {
            Input.ActionPress(action, strength);
        }
        else
        {
            Input.ActionRelease(action);
        }
    }

    /// <summary>禁用兜底：全部动作释放 + 触摸状态复位（防止残留注入状态污染后续会话）</summary>
    private void ReleaseAll()
    {
        foreach (var a in MoveActions)
        {
            Input.ActionRelease(a);
        }

        foreach (var a in AimActions)
        {
            Input.ActionRelease(a);
        }

        foreach (var key in Buttons.Keys)
        {
            Input.ActionRelease(key.AsStringName());
        }

        _buttons.Clear();
        _moveVec = Vector2.Zero;
        _aimVec = Vector2.Zero;
        _moveTouch = -1;
        _aimTouch = -1;
    }

    /// <summary>测试/诊断口：以设计坐标（1920×1080 系）直接驱动触摸状态机。
    /// 绕过窗口→视口坐标变换（Input.parse_input_event 注入的真实事件经变换，headless 下
    /// 窗口与设计分辨率不同、不可移植；真实设备的视口坐标变换是 Godot 标准行为，
    /// 区域判定语义与真实 _input 一致）。遵守启用状态（禁用时零注入，桌面零回归）。</summary>
    public void SimulateTouch(int idx, bool pressed, Vector2 pos)
    {
        if (!_enabled)
        {
            return;
        }

        OnTouch(idx, pressed, pos);
    }

    /// <summary>测试/诊断口：设计坐标拖动（见 SimulateTouch 注释）</summary>
    public void SimulateDrag(int idx, Vector2 pos)
    {
        if (!_enabled)
        {
            return;
        }

        OnDrag(idx, pos);
    }

    /// <summary>半透明绘制：摇杆基座/手柄 + 按钮圆 + 首字母标签（ASCII，不依赖字体资源）</summary>
    private void OnUiDraw()
    {
        if (!_enabled)
        {
            return;
        }

        DrawStick(MoveCenter, MoveRadius, _moveVec, new Color(0.3f, 0.8f, 1.0f, 0.35f));
        DrawStick(AimCenter, AimRadius, _aimVec, new Color(1.0f, 0.6f, 0.2f, 0.35f));
        foreach (var key in Buttons.Keys)
        {
            var action = key.AsStringName();
            var b = (Godot.Collections.Dictionary)Buttons[action];
            var c = b["center"].AsVector2();
            var r = (float)b["radius"].AsDouble();
            var lit = _buttons.ContainsKey(action);
            var col = lit ? new Color(0.3f, 0.8f, 1.0f, 0.5f) : new Color(0.3f, 0.8f, 1.0f, 0.28f);
            _ui.DrawCircle(c, r, col);
            _ui.DrawArc(c, r, 0.0f, Mathf.Tau, 32, new Color(0.7f, 0.95f, 1.0f, 0.6f), 2.0f);
            _ui.DrawString(
                ThemeDB.FallbackFont,
                c + new Vector2(-7.0f, 6.0f),
                ButtonLabel(action),
                HorizontalAlignment.Center,
                -1.0f,
                18,
                new Color(1.0f, 1.0f, 1.0f, 0.85f));
        }
    }

    private void DrawStick(Vector2 center, float radius, Vector2 vec, Color col)
    {
        _ui.DrawCircle(center, radius, new Color(col, col.A * 0.6f));
        _ui.DrawArc(center, radius, 0.0f, Mathf.Tau, 48, new Color(col, col.A * 1.4f), 2.0f);
        var knob = center + vec * radius * 0.7f;
        _ui.DrawCircle(knob, radius * 0.35f, new Color(0.9f, 0.98f, 1.0f, 0.7f));
    }

    private static string ButtonLabel(StringName action)
    {
        if (action == BoostName)
        {
            return "B";
        }

        if (action == FineMoveName)
        {
            return "F";
        }

        if (action == DashName)
        {
            return "D";
        }

        if (action == ParryName)
        {
            return "P";
        }

        return "";
    }

    // ---------------- GDScript 鸭子调用兼容桥（M5 过渡，M7 删除） ----------------
    // main.gd（_virtual_controls.set_enabled 经 Callable 直连）、Player.cs（Call is_enabled/
    // base_aim_position）与 virtual_controls_test（simulate_touch/simulate_drag/move_vec/aim_vec/
    // set_enabled/is_enabled）调用；布局常量（原 VirtualControls.MOVE_CENTER 等）GDScript 不可
    // 访问静态常量，经下列静态 snake 访问器（测试改 preload 脚本资源调用）。

    public void set_enabled(bool v) => SetEnabled(v);

    public bool is_enabled() => IsEnabled();

    public Vector2 base_aim_position() => BaseAimPosition();

    public Vector2 move_vec() => MoveVec();

    public Vector2 aim_vec() => AimVec();

    public void simulate_touch(int idx, bool pressed, Vector2 pos) => SimulateTouch(idx, pressed, pos);

    public void simulate_drag(int idx, Vector2 pos) => SimulateDrag(idx, pos);

    public static Vector2 move_center() => MoveCenter;

    public static float move_radius() => MoveRadius;

    public static Vector2 aim_center() => AimCenter;

    public static float aim_radius() => AimRadius;

    public static Godot.Collections.Dictionary buttons() => Buttons;
}
