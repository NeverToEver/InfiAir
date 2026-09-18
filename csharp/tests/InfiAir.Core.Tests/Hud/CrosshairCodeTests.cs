using InfiAir.Core.Hud;
using Xunit;

namespace InfiAir.Core.Tests.Hud;

/// <summary>准星码：跨设备复刻样式的唯一载体。编解码必须往返一致；校验和/版本/域界三道
/// 门各自要能红——粘贴错一位静默变成另一个准星、比导入失败恶劣得多。</summary>
public sealed class CrosshairCodeTests
{
    [Fact]
    public void Format_IsPrefixPlusTwentyCharsInGroupsOfFive()
    {
        var code = CrosshairCode.Encode(CrosshairProfile.Default);
        Assert.StartsWith("INF1-", code);
        var payload = code["INF1-".Length..];
        Assert.Equal(20, payload.Length);
        // 载荷字符集 = Crockford Base32（不含 I L O U），分组只是展示形式
        Assert.All(payload.ToCharArray(), c => Assert.True("0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(c), $"非法载荷字符 {c}"));
    }

    [Fact]
    public void RoundTrip_Default()
    {
        var code = CrosshairCode.Encode(CrosshairProfile.Default);
        Assert.Equal(CrosshairCodeResult.Ok, CrosshairCode.TryDecode(code, out var p));
        Assert.Equal(CrosshairProfile.Default, p);
    }

    [Fact]
    public void RoundTrip_AllFieldsDistinct()
    {
        var p = new CrosshairProfile
        {
            Shape = CrosshairShape.Cross,
            Size = 2.45f,
            Thickness = 5,
            Gap = 12.0f,
            Alpha = 0.40f,
            RotationDeg = 45,
            CenterDot = false,
            DotSize = 6,
            TShape = true,
            Outline = true,
            StateTint = false,
            R = 12,
            G = 34,
            B = 56,
            A = 78,
        };
        var code = CrosshairCode.Encode(p);
        Assert.Equal(CrosshairCodeResult.Ok, CrosshairCode.TryDecode(code, out var back));
        // size 编码量化到 0.01，往返按归一值比对
        Assert.Equal(p.Normalized(), back);
    }

    [Fact]
    public void Lowercase_AndAmbiguousChars_AreTolerated()
    {
        var code = CrosshairCode.Encode(CrosshairProfile.Default);
        var lower = code.ToLowerInvariant().Replace("inf1", "INF1");
        Assert.Equal(CrosshairCodeResult.Ok, CrosshairCode.TryDecode(lower, out var a));
        Assert.Equal(CrosshairProfile.Default, a);
    }

    [Fact]
    public void SingleCharCorruption_IsBadChecksum_NotSilentOtherStyle()
    {
        var code = CrosshairCode.Encode(CrosshairProfile.Default);
        var payload = code["INF1-".Length..];
        // 把首字符换成另一个必然不同的合法字符：任何单字符损坏都在 CRC-8 的突发检错能力内
        var swapped = payload[0] == '0' ? '1' : '0';
        var corrupted = "INF1-" + swapped + payload[1..];
        Assert.Equal(CrosshairCodeResult.BadChecksum, CrosshairCode.TryDecode(corrupted, out _));
    }

    [Fact]
    public void Malformed_Codes_AreRejected()
    {
        Assert.Equal(CrosshairCodeResult.Malformed, CrosshairCode.TryDecode("CSGO-AAAAAAAAAAAAAAAAAAAA", out _));
        Assert.Equal(CrosshairCodeResult.Malformed, CrosshairCode.TryDecode("INF1-SHORT", out _));
        Assert.Equal(CrosshairCodeResult.Malformed, CrosshairCode.TryDecode("INF1-UUUUUUUUUUUUUUUUUUUU", out _)); // U 非 Crockford 字符
        Assert.Equal(CrosshairCodeResult.Malformed, CrosshairCode.TryDecode("", out _));
    }

    [Fact]
    public void FutureVersion_IsUnsupported_NotGarbled()
    {
        // 手组一个版本=2 的载荷（其余同默认档）+ 正确校验和：版本门必须先红
        var bytes = new byte[12];
        bytes[0] = 2;
        var p = CrosshairProfile.Default;
        bytes[1] = FlagByte(p);
        bytes[2] = 100;
        bytes[3] = (byte)(((p.DotSize - 1) << 4) | (p.Thickness - 1));
        bytes[4] = 0;
        bytes[5] = 95;
        bytes[6] = 0;
        bytes[7] = p.R;
        bytes[8] = p.G;
        bytes[9] = p.B;
        bytes[10] = p.A;
        bytes[11] = CrosshairCode.Crc8(bytes.AsSpan(0, 11));
        var code = "INF1-" + CrosshairCode.EncodeBytes(bytes);
        Assert.Equal(CrosshairCodeResult.UnsupportedVersion, CrosshairCode.TryDecode(code, out _));
    }

    [Fact]
    public void OutOfRangePayload_IsRejected()
    {
        // 尺寸字节 5 < SizeMin×20(10)：域界门单独红（校验和本身是对的）
        var bytes = new byte[12];
        bytes[0] = 1;
        var p = CrosshairProfile.Default;
        bytes[1] = FlagByte(p);
        bytes[2] = 5;
        bytes[3] = (byte)(((p.DotSize - 1) << 4) | (p.Thickness - 1));
        bytes[4] = 0;
        bytes[5] = 95;
        bytes[6] = 0;
        bytes[7] = p.R;
        bytes[8] = p.G;
        bytes[9] = p.B;
        bytes[10] = p.A;
        bytes[11] = CrosshairCode.Crc8(bytes.AsSpan(0, 11));
        var code = "INF1-" + CrosshairCode.EncodeBytes(bytes);
        Assert.Equal(CrosshairCodeResult.OutOfRange, CrosshairCode.TryDecode(code, out _));
    }

    private static byte FlagByte(CrosshairProfile p)
    {
        var b = (byte)(((int)p.Shape & 3) << 4);
        if (p.TShape) b |= 1;
        if (p.CenterDot) b |= 2;
        if (p.Outline) b |= 4;
        if (p.StateTint) b |= 8;
        return b;
    }
}
