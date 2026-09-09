using Godot;

using System;

namespace InfiAir;

/// <summary>
/// 一次性延时回调 Timer 工厂（节点化防协程泄漏：退出时挂起协程的状态会泄漏并连带持有
/// 其引用的资源，信号连接随 Timer 节点一并释放）。暂停语义两套显式选择——默认 Pausable
/// （树暂停冻结，战斗计时语义）；alwaysProcessing:true 时 Always（树暂停仍计时，教程推进/
/// 演出计时语义）。触发后不自动释放，随宿主生命周期释放；需精确自清理（触发后 QueueFree）
/// 或跨次复用（Stop/Start）的计时段自管，不经本工厂。
/// </summary>
public static class TimerFx
{
    public static Godot.Timer OneShot(Node host, double seconds, Action callback, bool alwaysProcessing = false)
    {
        var timer = new Godot.Timer { OneShot = true, WaitTime = seconds };
        if (alwaysProcessing)
        {
            timer.ProcessMode = Node.ProcessModeEnum.Always;
        }

        timer.Timeout += callback;
        host.AddChild(timer);
        timer.Start();
        return timer;
    }
}
