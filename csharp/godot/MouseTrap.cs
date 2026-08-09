using Godot;

namespace InfiAir;

/// <summary>
/// 鼠标锁定窗口内（mouse_lock 设置项运行组件，挂 Main）：
/// 对局准星活跃（未暂停且系统光标隐藏）且窗口聚焦时，鼠标移出内容区即被
/// Input.warp_mouse() 拉回边缘内侧，从根上消除"鼠标出框 → get_global_mouse_position
/// 冻结 → 准星失控"的前提；暂停/Buff/基地/结算/过场/开始页等非准星态（AimCrosshair
/// 恢复系统光标）与窗口失焦一律放行——暂停后鼠标可自由移出窗口（如点系统标题栏
/// 关闭按钮退出游戏）。
/// Godot 4 的 Input.warp_mouse 接受屏幕坐标：warp 目标 = 窗口左上角屏幕坐标 + 内容区 clamp 点。
/// warp 目标恒取"出框前最后窗口内位置"（_last_known_pos），位移 ≤ 1-2px；且鼠标在窗口外时
/// get_global_mouse_position() 本就冻结在最后内部位置，warp 后读值连续——不引入准星跳变，
/// 反而把"移回窗口时的位置跳变"钳在边缘内侧（原无 confine 时可有数十 px 跳变）。
/// 已知取舍：拖动标题栏时鼠标位于 OS 装饰区会触发 mouse_exited 被拉回（固定尺寸窗口下
/// 可接受，用户可在设置中关闭本功能规避）。
/// M5 全量迁移（2026-08-08 自 scripts/mouse_trap.gd）。
/// </summary>
public partial class MouseTrap : Node
{
    private bool _focused;
    /// <summary>最后已知窗口内容区内的鼠标位置（每帧缓存；移出后 get_mouse_position() 不再更新，
    /// 供 mouse_exited 时生成 warp 目标；从未进入窗口内时为负，此时不拉回）</summary>
    private Vector2 _lastKnownPos = new(-1.0f, -1.0f);

    public override void _Ready()
    {
        ProcessMode = Node.ProcessModeEnum.Always; // 暂停时也维持位置缓存与防御；放行判定在 _trap_active
        var win = GetWindow();
        win.MouseExited += OnMouseExited;
        win.FocusExited += OnFocusExited;
        win.FocusEntered += OnFocusEntered;
        _focused = win.HasFocus();
    }

    public override void _ExitTree()
    {
        // 2026-08-03 审计（C22 模式）：Window 信号断开——节点未 free 重入树防双连回调
        // U04（2026-08-09 审计）：移除 M5 调试临时 return（原使断开代码成为不可达死代码，
        // Window 三信号永不断开，场景重载后移出窗口回调已释放实例）
        var win = GetWindow();
        if (win != null)
        {
            if (win.IsConnected(Window.SignalName.MouseExited, Callable.From(OnMouseExited)))
            {
                win.MouseExited -= OnMouseExited;
            }

            if (win.IsConnected(Window.SignalName.FocusExited, Callable.From(OnFocusExited)))
            {
                win.FocusExited -= OnFocusExited;
            }

            if (win.IsConnected(Window.SignalName.FocusEntered, Callable.From(OnFocusEntered)))
            {
                win.FocusEntered -= OnFocusEntered;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return; // headless 无真实鼠标/窗口事件，confine 逻辑全部跳过
        }

        var win = GetWindow();
        var mp = win.GetMousePosition();
        if (mp.X >= 0.0f && mp.Y >= 0.0f && mp.X < win.Size.X && mp.Y < win.Size.Y)
        {
            _lastKnownPos = mp;
        }

        TrapIfOutOfBounds();
    }

    private void OnMouseExited() => Trap();

    private void OnFocusExited() => _focused = false; // 失焦放行：鼠标可自由移出切换应用

    private void OnFocusEntered() => _focused = true;

    /// <summary>生效条件：设置开启 + 对局准星态（未暂停且系统光标隐藏）+ 窗口可见 + 聚焦 + 有内容尺寸。
    /// 暂停/Buff/基地/结算/过场/开始页（AimCrosshair 均恢复系统光标）与失焦一律放行，
    /// 鼠标可自由移出窗口（如点系统标题栏关闭按钮退出游戏）。</summary>
    private bool TrapActive()
    {
        var win = GetWindow();
        return TrapEnabled(
            GameState.Instance.MouseLock,
            win.Visible,
            _focused,
            win.Size.X > 0 && win.Size.Y > 0,
            !GetTree().Paused,
            Input.MouseMode == Input.MouseModeEnum.Hidden);
    }

    /// <summary>confine 放行判定纯函数（公开供测试）：仅对局准星活跃（未暂停 + 系统光标隐藏）时生效；
    /// 暂停/非准星态必须放行，否则暂停后鼠标无法移出窗口点系统关闭按钮退出游戏
    /// K16：K8 审计——A7 清理后测试白盒直调 _ 前缀函数再现，公开化（纯函数无状态，公开测试接口合法）</summary>
    public static bool TrapEnabled(
        bool mouseLock,
        bool windowVisible,
        bool focused,
        bool hasSize,
        bool notPaused,
        bool mouseHidden)
    {
        return mouseLock && windowVisible && focused && hasSize && notPaused && mouseHidden;
    }

    private void Trap()
    {
        if (!TrapActive() || _lastKnownPos.X < 0.0f || _lastKnownPos.Y < 0.0f)
        {
            return;
        }

        var win = GetWindow();
        Input.WarpMouse((Vector2)win.GetPosition() + WarpTarget(_lastKnownPos, win.Size));
    }

    /// <summary>每帧防御：已知位置经 clamp 改变（窗口尺寸/位置变化等偶发越界）时即时拉回</summary>
    private void TrapIfOutOfBounds()
    {
        if (!TrapActive() || _lastKnownPos.X < 0.0f || _lastKnownPos.Y < 0.0f)
        {
            return;
        }

        var win = GetWindow();
        var target = WarpTarget(_lastKnownPos, win.Size);
        if (target != _lastKnownPos)
        {
            Input.WarpMouse((Vector2)win.GetPosition() + target);
        }
    }

    /// <summary>warp 目标：已知窗口内位置 clamp 到内容区边缘内侧 1px（窗口相对坐标）。
    /// 避免系统判定鼠标仍在窗外造成 exited/warp 循环；窗口最小边假设 ≥ 2px。
    /// K16：公开供测试（纯函数，见 TrapEnabled）</summary>
    public static Vector2 WarpTarget(Vector2 knownPos, Vector2I winSize)
    {
        return knownPos.Clamp(Vector2.One, (Vector2)(winSize - Vector2I.One));
    }

    // ---------------- GDScript 鸭子调用兼容桥（M5 过渡，M7 删除） ----------------
    // mouse_lock_test 经 preload("res://scripts/mouse_trap.gd") 调用静态纯函数 warp_target/
    // trap_enabled——改 preload 为 res://csharp/godot/MouseTrap.cs 后经同名静态 snake 桥调用。


}
