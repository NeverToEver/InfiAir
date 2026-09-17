#if DEBUG || TOOLS
using Godot;
using InfiAir.Core.Practice;

namespace InfiAir;

/// <summary>
/// 标题屏手柄可达性探针（挂在 `--practice-probe` 尾段）：经生产单口 `ExitToTitle` 回标题屏，
/// 用**手柄事件注入**（`Input.ParseInputEvent`，生产输入面的另一路）走两个底部入口——
/// 摇杆/dpad 导航聚焦教程入口 → A 确认进入教程场景；再回标题 → dpad 焦点链移到练习入口 →
/// A 打开练习面板。两个入口都可达后才交棒 `EnterPractice`（真实练习入口，原收尾语义不变）；
/// 导航任一步失败即 `PushError` 且**不交棒**——练习宿主的完成标记缺席，本趟判红。
///
/// 为什么挂在**根**上：本段跨两次场景切换（练习局 → 标题 → 教程 → 标题），随场景挂的驱动
/// 会在第一次切换时被释放（与 TutorialProbeDriver 同款约束）；Always 态保证结算页树暂停期间
/// 也能推进。dpad/摇杆导航首拍聚焦教程入口、A 激活聚焦项的实现都在 TitleScreen 生产路径上，
/// 探针只注入事件与断言焦点落位——不白盒调内部方法。
/// </summary>
public partial class TitleNavProbeDriver : Node
{
    private readonly PracticeSetup _setup;
    private int _phase;
    private int _waitFrames;
    /// <summary>标题屏首次就绪的真实时刻（ms）：输入守卫是 0.5s 真实时间窗，无头 fixed-fps 下
    /// 帧数与真实时间脱钩（45 帧可能只过 100ms），等待必须按墙钟。</summary>
    private ulong _titleSeenMs;

    /// <summary>单步等待上限（帧）：切场景 + 确认动效（0.2s）+ 余量。</summary>
    private const int StepBudgetFrames = 120;

    public TitleNavProbeDriver(PracticeSetup setup)
    {
        _setup = setup;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GameState.Instance.ExitToTitle();
    }

    /// <summary>输入守卫的墙钟等待（ms）：0.5s 守卫 + 最小余量——fixed-fps 下墙钟等待同样烧帧，
    /// 趟预算按两段守卫各 ~0.6s 估（无头约 150–250 帧/秒）。</summary>
    private const ulong InputGuardWaitMs = 600;

    public override void _Process(double delta)
    {
        _waitFrames++;
        var title = GetTree().CurrentScene as TitleScreen;
        if (title != null && _titleSeenMs == 0)
        {
            _titleSeenMs = Time.GetTicksMsec();
        }

        switch (_phase)
        {
            case 0: // 等标题屏就绪 + 输入守卫（真实时间窗）过去，摇杆导航聚焦教程入口
                if (title == null)
                {
                    OverBudget("ExitToTitle 后标题屏未就绪");
                    return;
                }

                if (Time.GetTicksMsec() - _titleSeenMs < InputGuardWaitMs)
                {
                    OS.DelayMsec(16); // 等真实守卫窗但不烧帧预算：阻塞主循环，墙钟走、帧不走
                    return;
                }

                InjectStickDown();
                Next();
                return;

            case 1:
                if (FocusName(title) != "tutorial")
                {
                    Fail($"注入摇杆导航后焦点不在教程入口（实得 «{FocusName(title)}»）");
                    return;
                }

                InjectConfirm();
                Next();
                return;

            case 2: // A 确认 → 教程场景（确认动效 0.2s 后切场景）
                if (GetTree().CurrentScene is Tutorial)
                {
                    GD.Print("[practice-probe] 手柄导航经教程入口进入教程场景成立");
                    GameState.Instance.ExitToTitle();
                    Next();
                    return;
                }

                OverBudget("手柄确认后未进入教程场景（入口激活链断线）");
                return;

            case 3: // 第二次回标题：dpad 首拍聚焦教程入口（守卫按新实例的真实就绪时刻重算）
                if (title == null)
                {
                    OverBudget("第二次回标题未就绪");
                    return;
                }

                if (Time.GetTicksMsec() - _titleSeenMs < InputGuardWaitMs)
                {
                    OS.DelayMsec(16); // 同上：守卫是真实时间窗，帧数与它脱钩
                    return;
                }

                InjectDpadRight();
                Next();
                return;

            case 4:
                if (FocusName(title) != "tutorial")
                {
                    Fail($"dpad 首拍焦点不在教程入口（实得 «{FocusName(title)}»）");
                    return;
                }

                InjectDpadRight();
                Next();
                return;

            case 5: // 第二拍：引擎焦点链把焦点移到练习入口
                if (FocusName(title) != "practice")
                {
                    Fail($"dpad 第二拍焦点未移到练习入口（实得 «{FocusName(title)}»）");
                    return;
                }

                InjectConfirm();
                Next();
                return;

            case 6: // A 确认 → 练习面板打开；两个入口都可达，交棒真实练习入口
                if (title == null || !title.PracticePanelOpen())
                {
                    OverBudget("手柄确认后练习面板未打开（练习入口激活链断线）");
                    return;
                }

                GD.Print("[practice-probe] 手柄导航经练习入口打开练习面板成立（两个入口都可达）");
                Input.ActionPress(new StringName("ui_cancel")); // 关面板：交棒前恢复标题态
                Input.ActionRelease(new StringName("ui_cancel"));
                GameState.Instance.EnterPractice(_setup);
                QueueFree();
                return;
        }
    }

    /// <summary>焦点落位读数（标题屏未就绪返回空串）。</summary>
    private static string FocusName(TitleScreen? title) => title?.FocusedEntryName() ?? "";

    private void Next()
    {
        _phase++;
        _waitFrames = 0;
        _titleSeenMs = 0; // 新标题实例的就绪时刻重记
    }

    private void OverBudget(string what)
    {
        if (_waitFrames > StepBudgetFrames)
        {
            Fail(what);
        }
    }

    /// <summary>失败即不交棒（练习宿主的完成标记缺席 → 本趟门禁红）。</summary>
    private void Fail(string what)
    {
        GD.PushError($"[practice-probe] 标题屏手柄可达性不成立：{what}");
        QueueFree();
    }

    /// <summary>注入左摇杆向下（导航事件；轴立即归零——ParseInputEvent 的轴状态不会自动复位）。</summary>
    private static void InjectStickDown()
    {
        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = 0.8f });
        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = 0.0f });
    }

    /// <summary>注入 dpad 右（引擎 ui_right 的默认绑定，与真实手柄同路）。</summary>
    private static void InjectDpadRight()
    {
        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.DpadRight, Pressed = true });
        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.DpadRight, Pressed = false });
    }

    /// <summary>注入手柄 A（确认键，两拍事件＝按下沿 + 释放沿）。</summary>
    private static void InjectConfirm()
    {
        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = true });
        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = false });
    }
}
#endif
