using System.Threading.Tasks;
using Godot;

namespace InfiAir;

/// <summary>
/// 协程/异步工具层（M1 全量迁移建立，2026-08-08）。
/// 规则（同步登记 .agents/csharp-conventions.md §Async）：
/// - 游戏内计时一律 SceneTree.CreateTimer + ToSignal，禁止裸 Task.Delay（恢复在线程池，访问 Godot API 线程不安全）；
/// - 挂起 await 无法取消（SignalAwaiter 无内置取消，proposal #11909 未落地）→ 所有等待都以
///   SceneTree 所属计时器为兜底（必触发），配合恢复后 GodotObject.IsInstanceValid 判活，从根上消灭永久挂起/对象图泄漏；
/// - 禁止裸 async void 生命周期方法；await 段异常统一 try/catch。
/// </summary>
public static partial class Coroutine
{
    /// <summary>无超时模式的反悬挂兜底时长（2026-08-10 健壮性审查）：源对象在等待期间被释放时
    /// Godot 自动断开信号连接，tcs 永不完成 → await 永久挂起；兜底计时器保证 tcs 必完成
    /// （返回 false = 超时/释放），对齐类头「所有等待都以 SceneTree 计时器为兜底（必触发）」。</summary>
    private const double FallbackTimeoutSeconds = 60.0;

    /// <summary>等待 N 秒（主线程恢复；节点已释放/离树则提前返回，不悬挂）。
    /// 2026-08-09 审计：补 IsInsideTree 判活——节点有效但离树时 GetTree() 返回 null → NRE。</summary>
    public static async Task WaitSeconds(Node node, double seconds)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
        {
            return;
        }

        var timer = node.GetTree().CreateTimer(seconds);
        await node.ToSignal(timer, Godot.Timer.SignalName.Timeout);
    }

    /// <summary>等待 N 个物理帧（PhysicsFrame 信号；节点已释放/离树则提前返回）。</summary>
    public static async Task WaitPhysicsFrames(Node node, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
            {
                return;
            }

            await node.ToSignal(node.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>
    /// 等待源信号或超时（timeoutSeconds &gt; 0 时），先到先返回。
    /// 返回 true = 信号已触发；false = 超时或源/节点已释放。
    /// 信号触发/超时后自动断开连接，不残留 handler。
    /// </summary>
    public static async Task<bool> WaitSignal(Node node, GodotObject source, StringName signal, double timeoutSeconds = -1.0)
    {
        // 2026-08-09 审计：源/节点已失效（或节点离树取不到 tree）时等待已无意义——
        // 无超时模式原实现会跳过 Connect/超时兜底，使 tcs 永不完成 → await 永久挂起，
        // 违背类头"所有等待都以 SceneTree 计时器为兜底（必触发）"承诺；统一提前返回 false。
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || !GodotObject.IsInstanceValid(source))
        {
            return false;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var onSignal = Callable.From(() => tcs.TrySetResult(true));
        var timed = timeoutSeconds > 0.0;
        source.Connect(signal, onSignal);
        // 2026-08-10：无超时模式也挂兜底计时器（FallbackTimeoutSeconds）——见类头常量注释
        var timer = node.GetTree().CreateTimer(timed ? timeoutSeconds : FallbackTimeoutSeconds);
        timer.Timeout += () => tcs.TrySetResult(false);

        try
        {
            return await tcs.Task;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(source) && source.IsConnected(signal, onSignal))
            {
                source.Disconnect(signal, onSignal);
            }
        }
    }
}
