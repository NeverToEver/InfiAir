using Godot;
using InfiAir.Core.Config;

namespace InfiAir;

/// <summary>
/// 设置页里那个「数值管理器」入口的行为：探测本机管理器在不在跑、一键启动它、打开它的页面。
/// 协议口径（端口 / 身份串 / 参数 / 脚本路径）在 core 的 <see cref="BalanceEditorLink"/>，本类只做引擎适配。
///
/// 可用性判据（<see cref="Available"/>）两条同时成立：工具脚本存在于 res://（导出包不含
/// scripts/tools/，AGENTS §1「构建工具不得进发布包」）、且不是 headless（无头冒烟里点不到按钮，
/// 更不该因为设置页构建就拉起一个本机进程——那会污染门禁语义）。
///
/// 线程与时机纪律：全部走 HTTPRequest 异步回调，绝不在 _Ready/页面构建期发同步请求或起进程；
/// 探测失败一律静默降级为「未运行」，不 PushError（ERROR 会被冒烟门禁判红）。
/// </summary>
public partial class BalanceEditorLauncher : Node
{
    /// <summary>等待「刚启动的管理器」就绪的轮询间隔与次数（约 4.5 秒后放弃）。</summary>
    private const double LaunchProbeInterval = 0.3;
    private const int LaunchProbeAttempts = 15;

    private HttpRequest _probe = null!;
    private double _timer;
    private int _attemptsLeft;
    private bool _probeInFlight;
    private bool _openWhenReady;

    /// <summary>工具可用（开发检出且非无头）。</summary>
    public bool Available { get; private set; }

    /// <summary>管理器当前是否在跑（最近一次探测结果）。</summary>
    public bool Running { get; private set; }

    /// <summary>正在启动并等待就绪。</summary>
    public bool Busy { get; private set; }

    /// <summary>失败原因的文案键（空 = 无话可说），由 UI 用 Tr() 渲染。</summary>
    public string FailureKey { get; private set; } = "";

    /// <summary>状态变化（探测结果 / 忙碌 / 失败）：UI 订阅后刷新那一行状态与按钮。</summary>
    [Signal]
    public delegate void ChangedEventHandler();

    public override void _Ready()
    {
        _probe = new HttpRequest { Timeout = 1.0 };
        AddChild(_probe);
        _probe.RequestCompleted += OnProbeCompleted;
        Available = Godot.FileAccess.FileExists(BalanceEditorLink.ToolScriptPath)
                    && DisplayServer.GetName() != "headless";
        SetProcess(false);
    }

    /// <summary>探测一次（异步；结果经 <see cref="Changed"/> 通知）。</summary>
    public void Probe()
    {
        if (!Available || _probeInFlight)
        {
            return;
        }

        _probeInFlight = true;
        if (_probe.Request(BalanceEditorLink.PingUrl()) != Error.Ok)
        {
            _probeInFlight = false;
            ApplyProbeResult(false);
        }
    }

    /// <summary>点按钮：在跑就直接开页面；没跑就先启动、等它就绪再开。</summary>
    public void LaunchOrOpen()
    {
        if (!Available || Busy)
        {
            return;
        }

        if (Running)
        {
            OpenPage();
            return;
        }

        StartProcess();
    }

    /// <summary>在浏览器里打开管理器页面。</summary>
    public void OpenPage()
    {
        if (!Available)
        {
            return;
        }

        if (OS.ShellOpen(BalanceEditorLink.Url()) != Error.Ok)
        {
            FailureKey = "SET_MANAGER_OPEN_FAILED";
        }

        EmitSignal(SignalName.Changed);
    }

    private void StartProcess()
    {
        FailureKey = "";
        var scriptPath = ProjectSettings.GlobalizePath($"res://{BalanceEditorLink.ToolScriptPath}");
        foreach (var candidate in BalanceEditorLink.PythonCandidates(OS.GetName()))
        {
            // CreateProcess 只回 -1（找不到该命令）而不抛异常；逐个候选试到能起为止
            if (OS.CreateProcess(candidate, BalanceEditorLink.LaunchArguments(scriptPath)) > 0)
            {
                Busy = true;
                _openWhenReady = true;
                _attemptsLeft = LaunchProbeAttempts;
                _timer = 0.0;
                SetProcess(true);
                EmitSignal(SignalName.Changed);
                return;
            }
        }

        FailureKey = "SET_MANAGER_NO_PYTHON";
        EmitSignal(SignalName.Changed);
    }

    public override void _Process(double delta)
    {
        if (_attemptsLeft <= 0)
        {
            FinishLaunch(false);
            return;
        }

        _timer -= delta;
        if (_timer > 0.0)
        {
            return;
        }

        _timer = LaunchProbeInterval;
        _attemptsLeft--;
        Probe();
    }

    private void OnProbeCompleted(long result, long responseCode, string[] headers, byte[] body)
    {
        _probeInFlight = false;
        var running = result == (long)HttpRequest.Result.Success && responseCode == 200 && LooksLikeEditor(body);
        if (running && _openWhenReady)
        {
            FinishLaunch(true);
            OpenPage();
            return;
        }

        ApplyProbeResult(running);
        if (Busy && _attemptsLeft <= 0)
        {
            FinishLaunch(false);
        }
    }

    private void FinishLaunch(bool ok)
    {
        Busy = false;
        _openWhenReady = false;
        SetProcess(false);
        if (!ok)
        {
            FailureKey = "SET_MANAGER_LAUNCH_FAILED";
            Running = false;
        }

        EmitSignal(SignalName.Changed);
    }

    private void ApplyProbeResult(bool running)
    {
        Running = running;
        if (running)
        {
            FailureKey = "";
        }

        // 首次探测即便结果就是「没在跑」也要通知：UI 要从「未知」落到确定态
        EmitSignal(SignalName.Changed);
    }

    /// <summary>应答必须自称本工具：端口上蹲着别的程序时不能当它复用（同编辑器侧 probe_existing 口径）。</summary>
    private static bool LooksLikeEditor(byte[] body)
    {
        var parsed = Godot.Json.ParseString(System.Text.Encoding.UTF8.GetString(body));
        return parsed.VariantType == Variant.Type.Dictionary
               && parsed.AsGodotDictionary().TryGetValue("app", out var app)
               && app.AsString() == BalanceEditorLink.AppId;
    }
}
