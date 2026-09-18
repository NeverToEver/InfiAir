using InfiAir.Core;
using Xunit;

namespace InfiAir.Core.Tests.Visual;

/// <summary>
/// 星云能量场的纯算判据。无缝性是经典静默坏点：贴图四向平铺滚动，回绕一丢，
/// 画面上只在贴图接缝处露出一道竖/横亮线，冒烟与截图都不判——判据只能钉在这里。
/// </summary>
public class NebulaFieldTests
{
    [Fact]
    public void Sample_四向无缝()
    {
        for (var i = 0; i < 16; i++)
        {
            var t = i / 16.0f;
            Assert.Equal(NebulaField.Sample(0.0f, t, 20260907), NebulaField.Sample(1.0f, t, 20260907));
            Assert.Equal(NebulaField.Sample(t, 0.0f, 20260907), NebulaField.Sample(t, 1.0f, 20260907));
        }
    }

    [Fact]
    public void Build_同种子逐位一致_异种子不同()
    {
        var a = NebulaField.Build(64, 20260907);
        var b = NebulaField.Build(64, 20260907);
        Assert.Equal(a.Length, b.Length);
        for (var i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i], b[i]);
        }

        var c = NebulaField.Build(64, 20260908);
        var differs = false;
        for (var i = 0; i < a.Length; i++)
        {
            if (Math.Abs(a[i] - c[i]) > 1e-4f)
            {
                differs = true;
                break;
            }
        }

        Assert.True(differs, "不同种子产出了相同的场——哈希没吃进种子");
    }

    [Fact]
    public void Build_值域钳在0到1且非全零()
    {
        var field = NebulaField.Build(64, 20260907);
        var max = 0.0f;
        foreach (var v in field)
        {
            Assert.True(v >= 0.0f && v <= 1.0f, $"场值 {v} 越界");
            max = Math.Max(max, v);
        }

        Assert.True(max > 0.4f, $"场峰值 {max} 过低——组合后云层塌没了");
    }
}
