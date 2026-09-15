using Godot;
using InfiAir.Core.Input;

namespace InfiAir;

/// <summary>
/// 鼠标锁定窗口内（mouse_lock 设置项运行组件，挂 Main）：
/// 本局准星活跃（未暂停且系统光标隐藏）且窗口聚焦时，鼠标移出内容区即被
/// Input.warp_mouse() 拉回边缘内侧，从根上消除"鼠标出框 → get_global_mouse_position
/// 冻结 → 准星失控"的前提；暂停/增幅/基地/结算/过场/开始页等非准星态（AimCrosshair
/// 恢复系统光标）与窗口失焦一律放行——暂停后鼠标可自由移出窗口（如点系统标题栏
/// 关闭按钮退出游戏）。
/// Godot 4 的 Input.warp_mouse 收**窗口相对坐标**（GodotSharp.xml：relative to an origin at the
/// upper left corner of the currently focused Window Manager game window）——warp 目标就是内容区
/// clamp 点本身，**不得叠加窗口的屏幕位置**：叠上去会把光标弹开该偏移量（窗口不在 (0,0) 时准星瞬跳，
/// 窗口左/上边带成死区）。钳制算式单源在 WarpClamp（core 纯逻辑层，配单测）。
/// warp 目标恒取"出框前最后窗口内位置"（_last_known_pos），位移 ≤ 1-2px；且鼠标在窗口外时
/// get_global_mouse_position() 本就冻结在最后内部位置，warp 后读值连续——不引入准星跳变，
/// 反而把"移回窗口时的位置跳变"钳在边缘内侧（无 confine 时可有数十 px 跳变）。
/// 已知取舍：拖动标题栏时鼠标位于 OS 装饰区会触发 mouse_exited 被拉回（固定尺寸窗口下
/// 可接受，用户可在设置中关闭本功能规避）。
/// 聚焦判定必须实时查询（TrapActive 内 win.HasFocus()），不能缓存 FocusEntered/FocusExited
/// 信号——Windows 下焦点事件可能被 OS 级抢占（通知/UAC/虚拟桌面切换）吞掉，缓存一旦滞留
/// true 会在窗口实际失焦后继续 warp，鼠标被钉死在窗口内、永远无法切换焦点。
/// </summary>
public partial class MouseTrap : Node
{
    /// <summary>headless 判定缓存一次（同 SfxPlayer.IsHeadless）：DisplayServer.GetName() 是原生调用，
    /// 平台的 DisplayServer 一旦选定不会中途更换，逐帧比较字符串纯属浪费。</summary>
    private static readonly bool IsHeadless = DisplayServer.GetName() == "headless";

    /// <summary>最后已知窗口内容区内的鼠标位置（每帧缓存；移出后 get_mouse_position() 不再更新，
    /// 供 mouse_exited 时生成 warp 目标；从未进入窗口内时为负，此时不拉回）</summary>
    private Vector2 _lastKnownPos = new(-1.0f, -1.0f);

    public override void _Ready()
    {
        ProcessMode = Node.ProcessModeEnum.Always; // 暂停时也维持位置缓存与防御；放行判定在 _trap_active
        var win = GetWindow();
        win.MouseExited += OnMouseExited;
    }

    public override void _ExitTree()
    {
        // Window 信号断开——节点未 free 重入树防双连回调；场景重载后防移出窗口回调已释放实例
        var win = GetWindow();
        if (win != null
            && win.IsConnected(Window.SignalName.MouseExited, Callable.From(OnMouseExited)))
        {
            win.MouseExited -= OnMouseExited;
        }
    }

    public override void _Process(double delta)
    {
        if (IsHeadless)
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

    /// <summary>生效条件：设置开启 + 本局准星态（未暂停且系统光标隐藏）+ 窗口可见 + 聚焦 + 有内容尺寸。
    /// 暂停/增幅/基地/结算/过场/开始页（AimCrosshair 均恢复系统光标）与失焦一律放行，
    /// 鼠标可自由移出窗口（如点系统标题栏关闭按钮退出游戏）。
    /// 聚焦用 HasFocus() 实时查询而非缓存信号（焦点事件可能被 OS 抢占吞掉，见文件头注释）。</summary>
    private bool TrapActive()
    {
        var win = GetWindow();
        return TrapEnabled(
            GameState.Instance.MouseLock,
            win.Visible,
            win.HasFocus(),
            win.Size.X > 0 && win.Size.Y > 0,
            !GetTree().Paused,
            Input.MouseMode == Input.MouseModeEnum.Hidden);
    }

    /// <summary>confine 放行判定纯函数：仅本局准星活跃（未暂停 + 系统光标隐藏）时生效；
    /// 暂停/非准星态必须放行，否则暂停后鼠标无法移出窗口点系统关闭按钮退出游戏
    /// 纯函数无状态，公开供诊断/复用（TrapActive 内部同用）。</summary>
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
        // warp_mouse 收窗口相对坐标：结果原样交出，不加窗口屏幕位置（见文件头注释）
        var (wx, wy) = WarpTarget(_lastKnownPos.X, _lastKnownPos.Y, win.Size.X, win.Size.Y);
        Input.WarpMouse(new Vector2(wx, wy));
    }

    /// <summary>每帧防御：已知位置经 clamp 改变（窗口尺寸/位置变化等偶发越界）时即时拉回</summary>
    private void TrapIfOutOfBounds()
    {
        if (!TrapActive() || _lastKnownPos.X < 0.0f || _lastKnownPos.Y < 0.0f)
        {
            return;
        }

        var win = GetWindow();
        var (tx, ty) = WarpTarget(_lastKnownPos.X, _lastKnownPos.Y, win.Size.X, win.Size.Y);
        if (tx != _lastKnownPos.X || ty != _lastKnownPos.Y)
        {
            Input.WarpMouse(new Vector2(tx, ty));
        }
    }

    /// <summary>warp 目标：已知窗口内位置 clamp 到内容区边缘内侧 1px（窗口相对坐标）。
    /// 钳制算式单源在 core 层 WarpClamp.Target（坐标系语义与退化窗口/非有限输入边界由单测钉住；
    /// 本方法只做 Godot 类型适配）。</summary>
    public static (float X, float Y) WarpTarget(float knownX, float knownY, float winWidth, float winHeight)
    {
        return WarpClamp.Target(knownX, knownY, winWidth, winHeight);
    }
}
