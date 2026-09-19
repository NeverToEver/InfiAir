namespace InfiAir.Core.Config;

/// <summary>
/// 游戏与外部数值管理器（scripts/tools/balance_editor.py）之间的联动口径，纯逻辑、零 Godot 依赖。
///
/// 为什么下沉到 core：端口、身份串、启动参数是两边共用的**协议**——写散在 UI 代码里就只能靠人眼核对
/// （改了一边忘了另一边，表现为「点了没反应」）；而「文件变了没有」的重载判定承载安全语义
/// （只在标题屏重载，见 GameState.ReloadBalanceIfChanged），必须能被单测钉住边界。
/// </summary>
public static class BalanceEditorLink
{
    /// <summary>管理器默认端口（与 balance_editor.py 的 --port 默认值一致）。</summary>
    public const int DefaultPort = 8931;

    /// <summary>管理器身份串（/api/ping 应答里的 app 字段）：端口上若不是本工具，就不能当它复用。</summary>
    public const string AppId = "infiair-balance-editor";

    /// <summary>工具脚本在仓库内的相对路径——「能不能从这里启动」的唯一判据（导出包不含该文件）。</summary>
    public const string ToolScriptPath = "scripts/tools/balance_editor.py";

    /// <summary>管理器页面地址。</summary>
    public static string Url(int port = DefaultPort) => $"http://127.0.0.1:{port}/";

    /// <summary>身份探测端点（HTTP GET，应答 JSON 含 <see cref="AppId"/>）。</summary>
    public static string PingUrl(int port = DefaultPort) => $"http://127.0.0.1:{port}/api/ping";

    /// <summary>
    /// 按平台列出可能的 Python 解释器命令名，依次尝试。
    /// Windows 官方安装器装的是 py launcher 与 python.exe；Linux/macOS 惯例是 python3。
    /// 都找不到时由调用方给出可读提示，而不是静默失败。
    /// </summary>
    public static string[] PythonCandidates(string osName) => osName switch
    {
        "Windows" => new[] { "py", "python", "python3" },
        _ => new[] { "python3", "python" },
    };

    /// <summary>
    /// 启动参数：脚本绝对路径 + <c>--no-browser</c>。
    /// 刻意不让管理器自己开浏览器——由游戏在探测到就绪后再开，玩家的窗口焦点才不会先跳到
    /// 一个「页面打不开」的标签上（服务还没起来时浏览器只会显示连接失败）。
    /// </summary>
    public static string[] LaunchArguments(string scriptPath) => new[] { scriptPath, "--no-browser" };

    /// <summary>
    /// 是否需要重载数值：只有「记录值与当前值都是有效时间戳且不同」才算变化。
    /// 任一为 0/负（取不到时间戳：导出包里 res:// 不可写、或文件刚被删除）一律判否——
    /// 宁可不重载，也不要在拿不到判据时反复重载。
    /// </summary>
    public static bool NeedsReload(double recordedMtime, double currentMtime)
    {
        if (!double.IsFinite(recordedMtime) || !double.IsFinite(currentMtime))
        {
            return false;
        }

        return recordedMtime > 0.0 && currentMtime > 0.0 && !recordedMtime.Equals(currentMtime);
    }

    /// <summary>状态行的语义分级（配色由 UI 侧按主题决定）。</summary>
    public enum StatusTone
    {
        /// <summary>次要信息：不可用 / 未运行。</summary>
        Dim,

        /// <summary>正在工作：运行中。</summary>
        Active,

        /// <summary>出问题了：上一次启动或打开失败。</summary>
        Failure,
    }

    /// <summary>
    /// 状态 → 文案键。UI 只负责 Tr() 与上色，选哪个键由这里定——设置页那一行是不可自动点击的
    /// （Godot 侧 UI），把分支判断收进 core 才能被单测钉住。
    /// 优先级：不可用 &gt; 失败 &gt; 启动中 &gt; 运行中 &gt; 未运行。
    /// </summary>
    public static string StatusKey(bool available, bool running, bool busy, string failureKey)
    {
        if (!available)
        {
            return "SET_MANAGER_STATUS_UNAVAILABLE";
        }

        if (!string.IsNullOrEmpty(failureKey))
        {
            return failureKey;
        }

        if (busy)
        {
            return "SET_MANAGER_STATUS_BUSY";
        }

        return running ? "SET_MANAGER_STATUS_RUNNING" : "SET_MANAGER_STATUS_STOPPED";
    }

    /// <summary>状态行的语义色分级，与 <see cref="StatusKey"/> 同一套优先级。</summary>
    public static StatusTone ToneOf(bool available, bool running, bool busy, string failureKey)
    {
        if (!available)
        {
            return StatusTone.Dim;
        }

        if (!string.IsNullOrEmpty(failureKey))
        {
            return StatusTone.Failure;
        }

        return running && !busy ? StatusTone.Active : StatusTone.Dim;
    }
}
