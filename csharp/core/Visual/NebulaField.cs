using System;

namespace InfiAir.Core;

/// <summary>
/// 星云能量场（纯算，零 Godot 依赖）：环面无缝值噪声 FBM 云 + 脊状能量细丝 + 暗尘带切割，
/// 输出 0..1 灰度场，颜色全部由渲染层染色。无缝性靠「每八度格点坐标按周期回绕」构造——
/// Sample(0,v)≡Sample(1,v)、Sample(u,0)≡Sample(u,1)（单测钉住，重构丢回绕即红）；
/// 场值由整数格点哈希决定，同种子逐位确定、跨平台一致（不用浮点敏感的 sin 哈希）。
/// 组合口径：细丝只长在云里（云 ×（底亮 + 细丝增益）），尘带按阈值平滑切割变暗——
/// 读作「能量在星云中走线、尘埃在前面挡光」，而不是旧版软斑叠加的棉花团。
/// </summary>
public static class NebulaField
{
    /// <summary>场值采样：u/v ∈ [0,1)（先回绕），返回 0..1。</summary>
    public static float Sample(float u, float v, int seed)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);
        var s = unchecked((uint)seed);
        var cloud = Fbm(u, v, s, basePeriod: 5, octaves: 4);
        cloud = MathF.Pow(cloud, 1.45f); // 拉开对比：暗部更暗，云体收成有形的一团团
        var veins = Ridged(u, v, s ^ 0x68bc21eb, basePeriod: 5, octaves: 4);
        veins = MathF.Pow(veins, 1.8f);
        var dustRaw = Fbm(u + 0.37f, v + 0.61f, s ^ 0x1d7f3ca9, basePeriod: 4, octaves: 3);
        var dust = Smoothstep(0.54f, 0.74f, dustRaw);
        var val = cloud * (0.55f + 0.85f * veins);
        val *= 1.0f - 0.6f * dust;
        val *= 1.35f; // 输出增益：对比拉开后大面积场值偏低，补一档存在感（钳 1.0 防溢出染色）
        return Math.Clamp(val, 0.0f, 1.0f);
    }

    /// <summary>按行优先采样 size² 网格（贴图构建用；采样点取像素中心）。</summary>
    public static float[] Build(int size, int seed)
    {
        var field = new float[size * size];
        for (var y = 0; y < size; y++)
        {
            var v = (y + 0.5f) / size;
            for (var x = 0; x < size; x++)
            {
                field[y * size + x] = Sample((x + 0.5f) / size, v, seed);
            }
        }

        return field;
    }

    /// <summary>分形值噪声：八度倍频累加，幅度减半，按总幅归一到 0..1。</summary>
    private static float Fbm(float u, float v, uint seed, int basePeriod, int octaves)
    {
        var sum = 0.0f;
        var amp = 1.0f;
        var total = 0.0f;
        var period = basePeriod;
        for (var o = 0; o < octaves; o++)
        {
            sum += amp * ValueNoise(u * period, v * period, period, seed + (uint)(o * 0x9e3779b9));
            total += amp;
            amp *= 0.5f;
            period *= 2;
        }

        return sum / total;
    }

    /// <summary>脊状噪声（能量细丝）：每八度取 1−|2n−1| 的平方——值噪声的等值脊线变亮丝。</summary>
    private static float Ridged(float u, float v, uint seed, int basePeriod, int octaves)
    {
        var sum = 0.0f;
        var amp = 1.0f;
        var total = 0.0f;
        var period = basePeriod;
        for (var o = 0; o < octaves; o++)
        {
            var n = ValueNoise(u * period, v * period, period, seed + (uint)(o * 0x85ebca6b));
            var ridge = 1.0f - MathF.Abs(2.0f * n - 1.0f);
            sum += amp * ridge * ridge;
            total += amp;
            amp *= 0.5f;
            period *= 2;
        }

        return sum / total;
    }

    /// <summary>值噪声：格点值取整数哈希，格内五次多项式插值；格点坐标按 period 回绕＝环面无缝。</summary>
    private static float ValueNoise(float x, float y, int period, uint seed)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var sx = Smoothstep01(fx);
        var sy = Smoothstep01(fy);
        var cx0 = Mod(x0, period);
        var cy0 = Mod(y0, period);
        var cx1 = Mod(x0 + 1, period);
        var cy1 = Mod(y0 + 1, period);
        var v00 = Corner(cx0, cy0, seed);
        var v10 = Corner(cx1, cy0, seed);
        var v01 = Corner(cx0, cy1, seed);
        var v11 = Corner(cx1, cy1, seed);
        return Lerp(Lerp(v00, v10, sx), Lerp(v01, v11, sx), sy);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Corner(int x, int y, uint seed)
    {
        unchecked
        {
            var h = (uint)x * 374761393u + (uint)y * 668265263u + seed;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / (float)uint.MaxValue;
        }
    }

    private static int Mod(int a, int m)
    {
        var r = a % m;
        return r < 0 ? r + m : r;
    }

    private static float Smoothstep01(float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }
}
