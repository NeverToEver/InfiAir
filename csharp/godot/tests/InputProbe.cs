using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 输入丢失态探针（ROADMAP「间歇性全站输入丢失态」调查用，临时场景，用完即删）：
/// 实例化 welcome，全量打印 _Input 事件流 + 指纹量（窗口焦点/GUI 焦点/掩码态），
/// 依次注入合成鼠标按下/抬起与合成字符键，校验事件是否到达 _Input、引擎掩码是否置位、
/// 焦点 LineEdit 是否录入——对应故障指纹①②③。故障态下合成事件同样在 _Input 前被吞，
/// 因此探针无需真实 OS 输入即可检出故障态。
/// 注意 ParseInputEvent 是异步入队（次帧才到 _Input），所有校验都在注入的下一步执行。
/// INPUT_PROBE_HOLD=1 时不自动退出（留窗供真实输入对照）。
/// </summary>
public partial class InputProbe : Node
{
    private const int SyntheticDevice = -77;

    private double _t;
    private int _step;
    private bool _sawSynthPress;
    private bool _sawSynthRelease;
    private bool _sawSynthKey;
    private bool _maskDuringPress;
    private bool _recordedInLineEdit;
    private int _lineLenBefore;
    private Welcome? _welcome;

    public override void _Ready()
    {
        Log("READY booting welcome");
        _welcome = GD.Load<PackedScene>("res://scenes/welcome.tscn").Instantiate<Welcome>();
        AddChild(_welcome);
        var win = GetWindow();
        win.FocusEntered += () => Log("WINDOW FocusEntered");
        win.FocusExited += () => Log("WINDOW FocusExited");
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mb:
                Log(
                    $"EV MouseButton btn={mb.ButtonIndex} pressed={mb.Pressed} pos={mb.Position} dev={mb.Device}"
                );
                if (mb.Device == SyntheticDevice && mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.Pressed)
                    {
                        _sawSynthPress = true;
                    }
                    else
                    {
                        _sawSynthRelease = true;
                    }
                }

                break;
            case InputEventMouseMotion mm:
                Log($"EV MouseMotion pos={mm.Position} dev={mm.Device}");
                break;
            case InputEventKey k when k.Pressed && !k.Echo:
                Log($"EV Key keycode={k.Keycode} unicode='{(char)k.Unicode}' dev={k.Device}");
                if (k.Device == SyntheticDevice)
                {
                    _sawSynthKey = true;
                }

                break;
        }
    }

    public override void _Process(double delta)
    {
        _t += delta;
        switch (_step)
        {
            case 0 when _t >= 0.5:
                _step = 1;
                Fingerprint("baseline");
                break;
            case 1 when _t >= 1.0:
                _step = 2;
                Log("INJECT mouse left press (320,400)");
                Input.ParseInputEvent(
                    new InputEventMouseButton
                    {
                        ButtonIndex = MouseButton.Left,
                        Pressed = true,
                        Position = new Vector2(320.0f, 400.0f),
                        GlobalPosition = new Vector2(320.0f, 400.0f),
                        Device = SyntheticDevice,
                    }
                );
                break;
            case 2 when _t >= 1.2:
                _step = 3;
                _maskDuringPress = Input.IsMouseButtonPressed(MouseButton.Left);
                Log($"STATE mask during press={_maskDuringPress} sawPress={_sawSynthPress}");
                Log("INJECT mouse left release");
                Input.ParseInputEvent(
                    new InputEventMouseButton
                    {
                        ButtonIndex = MouseButton.Left,
                        Pressed = false,
                        Position = new Vector2(320.0f, 400.0f),
                        GlobalPosition = new Vector2(320.0f, 400.0f),
                        Device = SyntheticDevice,
                    }
                );
                break;
            case 3 when _t >= 1.4:
                _step = 4;
                _welcome!.PasswordLine().GrabFocus();
                _lineLenBefore = _welcome.PasswordLine().Text.Length;
                Log("INJECT key 'z'");
                Input.ParseInputEvent(
                    new InputEventKey
                    {
                        Pressed = true,
                        Keycode = Key.Z,
                        Unicode = (int)'z',
                        Device = SyntheticDevice,
                    }
                );
                break;
            case 4 when _t >= 1.7:
                _step = 5;
                _recordedInLineEdit = _welcome!.PasswordLine().Text.Length > _lineLenBefore;
                Log(
                    $"STATE lineedit recorded={_recordedInLineEdit} (len {_lineLenBefore}->{_welcome.PasswordLine().Text.Length})"
                );
                break;
            case 5 when _t >= 2.2:
                _step = 6;
                Fingerprint("final");
                Log(
                    $"VERDICT press_input={B(_sawSynthPress)} release_input={B(_sawSynthRelease)} mask={B(_maskDuringPress)} "
                    + $"key_input={B(_sawSynthKey)} lineedit={B(_recordedInLineEdit)}"
                );
                if (OS.GetEnvironment("INPUT_PROBE_HOLD") != "1")
                {
                    Log("DONE quitting");
                    GetTree().Quit(0);
                }

                break;
        }
    }

    private void Fingerprint(string tag)
    {
        var win = GetWindow();
        var owner = GetViewport().GuiGetFocusOwner();
        var ownerTag = "<null>";
        if (owner != null)
        {
            ownerTag = owner == _welcome!.PasswordLine() ? "password"
                : owner == _welcome.UsernameLine() ? "username"
                : owner.GetPath().ToString();
        }

        Log(
            $"FINGERPRINT[{tag}] winFocused={DisplayServer.WindowIsFocused()} hasFocus={win.HasFocus()} "
            + $"mode={win.Mode} size={win.Size} screenScale={DisplayServer.ScreenGetScale(win.CurrentScreen)} "
            + $"paused={GetTree().Paused} timeScale={Engine.TimeScale} "
            + $"maskL={Input.IsMouseButtonPressed(MouseButton.Left)} focusOwner={ownerTag}"
        );
    }

    private static string B(bool v) => v ? "1" : "0";

    private static void Log(string msg) => GD.Print($"[PROBE] {Time.GetTicksMsec()} {msg}");
}
