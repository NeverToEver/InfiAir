using Godot;

namespace InfiAir;

/// <summary>
/// 手柄震动静态封装：Pulse 对已连接手柄广播一次定时脉冲
/// （Input.StartJoyVibration 的 duration 由引擎托管、到点自停，新脉冲天然覆盖旧脉冲，
/// 无需节点定时器）。强度/时长数值单源 balance.json player.rumble（hit/parry/dash 三档，
/// 每档 weak/strong/duration）；设置域 JoyVibration 关闭时全部调用直接返回。
/// </summary>
public static class RumbleService
{
    /// <summary>受击震动（PlayerDamage 扣血生效处调用）。</summary>
    public static void Hit() => Pulse(
        CfgF("player.rumble.hit.weak", 0.6f),
        CfgF("player.rumble.hit.strong", 0.8f),
        CfgF("player.rumble.hit.duration", 0.15f));

    /// <summary>弹反成功震动（Player 盾区反射结算处调用）。</summary>
    public static void Parry() => Pulse(
        CfgF("player.rumble.parry.weak", 0.3f),
        CfgF("player.rumble.parry.strong", 0.9f),
        CfgF("player.rumble.parry.duration", 0.2f));

    /// <summary>冲刺震动（PlayerDash.Start 调用）。</summary>
    public static void Dash() => Pulse(
        CfgF("player.rumble.dash.weak", 0.2f),
        CfgF("player.rumble.dash.strong", 0.4f),
        CfgF("player.rumble.dash.duration", 0.1f));

    /// <summary>单次脉冲：weak/strong 双马达强度 [0,1]，seconds 持续秒数（≤0 不触发）。
    /// 无手柄连接时 GetConnectedJoypads 为空，自然零开销。TryGetInstance 判活——
    /// Instance 取不到时抛异常而非返回 null，null 检查只有安全取值口才成立。</summary>
    public static void Pulse(float weak, float strong, float seconds)
    {
        var gs = GameState.TryGetInstance();
        if (gs == null || !gs.JoyVibration || seconds <= 0.0f)
        {
            return;
        }

        weak = Mathf.Clamp(weak, 0.0f, 1.0f);
        strong = Mathf.Clamp(strong, 0.0f, 1.0f);
        foreach (var device in Input.GetConnectedJoypads())
        {
            Input.StartJoyVibration(device, weak, strong, seconds);
        }
    }

    private static float CfgF(string path, float fallback)
    {
        var gs = GameState.TryGetInstance();
        return gs == null ? fallback : (float)gs.Cfg(path, fallback).AsDouble();
    }
}
