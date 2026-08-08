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
    /// <summary>等待 N 秒（主线程恢复；节点已释放则提前返回，不悬挂）。</summary>
    public static async Task WaitSeconds(Node node, double seconds)
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }

        var timer = node.GetTree().CreateTimer(seconds);
        await node.ToSignal(timer, Godot.Timer.SignalName.Timeout);
    }

    /// <summary>等待 N 个物理帧（PhysicsFrame 信号；节点已释放则提前返回）。</summary>
    public static async Task WaitPhysicsFrames(Node node, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!GodotObject.IsInstanceValid(node))
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
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var onSignal = Callable.From(() => tcs.TrySetResult(true));
        var timed = timeoutSeconds > 0.0;
        SceneTreeTimer? timer = null;        if (GodotObject.IsInstanceValid(source))
        {
            source.Connect(signal, onSignal);
        }

        if (timed)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                return await tcs.Task;
            }

            timer = node.GetTree().CreateTimer(timeoutSeconds);
            timer.Timeout += () => tcs.TrySetResult(false);        }

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
