namespace InfiAir.Core.Input;

/// <summary>
/// 摇杆整形器（纯逻辑，零 Godot 依赖）：径向死区 + 重标定 + 指数响应曲线。
/// 逐轴死区在斜推时会单独掐零分量（十字吸附感），故按向量长度判定；
/// 用户死区在读取侧生效（InputMap 侧的摇杆 deadzone 只保留滤硬件噪声的小常数）。
/// </summary>
public static class StickShaper
{
    /// <summary>外死区：长度达到即视为满推（部分手柄物理上推不到 1.0，防永远差一截）。</summary>
    public const float OuterDeadzone = 0.98f;

    /// <summary>
    /// 摇杆输入整形：长度 &lt; deadzone 输出零；有效行程按 (len - dz) / (Outer - dz) 重标定回
    /// [0,1]（出死区从 0 连续爬升，消除跨死区瞬间的速度跳变），再经 pow(t, expo) 响应曲线
    /// 压弯（expo=1 线性，&gt;1 轻推更细、推满仍能到满速），方向保持不变。
    /// 零向量恒输出零——dz=0 时 0 &lt; 0 不成立，不拦截会走 x/0 归一化得 NaN。
    /// 非有限输入/调参（NaN/±∞）一律输出零：NaN 经归一化直接穿出灌进机体位置，
    /// 而 Mathf.Clamp 对 NaN 不设防（NaN 通过与任何数的比较恒假）。
    /// </summary>
    public static (float X, float Y) Shape(float x, float y, float deadzone, float expo)
    {
        // 轴值非有限无安全方向可言，直接输出零；调参非有限回退安全值（死区 0 = 不吞输入、
        // 指数 1 = 线性）——否则 Math.Clamp/MathF.Pow 会把 NaN 原样传下去。
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            return (0.0f, 0.0f);
        }

        var len = MathF.Sqrt((x * x) + (y * y));
        var dz = float.IsFinite(deadzone) ? Math.Clamp(deadzone, 0.0f, OuterDeadzone - 0.01f) : 0.0f;
        if (len <= 0.0f || len < dz)
        {
            return (0.0f, 0.0f);
        }

        var t = Math.Clamp((len - dz) / (OuterDeadzone - dz), 0.0f, 1.0f);
        var shaped = MathF.Pow(t, float.IsFinite(expo) ? Math.Max(expo, 0.01f) : 1.0f);
        return (x / len * shaped, y / len * shaped);
    }
}
