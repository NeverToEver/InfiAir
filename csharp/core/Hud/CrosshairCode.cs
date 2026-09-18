using System.Text;

namespace InfiAir.Core.Hud;

/// <summary>准星码解码结果：Ok 之外的每档都要能单独判红（UI 报错文案按档区分）。</summary>
public enum CrosshairCodeResult
{
    Ok,
    /// <summary>前缀/长度/字符集不对（贴错内容）。</summary>
    Malformed,
    /// <summary>校验和不符（贴错一两位、截断）。</summary>
    BadChecksum,
    /// <summary>码的格式版本比当前程序新（旧程序导入未来码，语义不能猜）。</summary>
    UnsupportedVersion,
    /// <summary>载荷字段越界（校验和是对的、内容不是合法样式）。</summary>
    OutOfRange,
}

/// <summary>
/// 准星码编解码（纯逻辑，零 Godot 依赖）：跨设备复刻一份 <see cref="CrosshairProfile"/>。
/// 格式（DESIGN_BASELINE 定稿）：`INF1-` + 20 个 Crockford Base32 字符。载荷 12 字节＝
/// b0 版本、b1 标志位（bit0 T 形 / bit1 中心点 / bit2 描边 / bit3 交战变色 / bit4-5 形状，bit6-7 恒 0）、
/// b2 size×20（0.05 步）、b3 低半字节 thickness−1 高半字节 dot_size−1、b4 gap、b5 alpha×100、b6 rotation/15、
/// b7-b10 RGBA、b11 CRC-8(poly 0x07) 校验 b0-b10；尾部补 4 个零位凑满 100 位（20×5）。
/// 解码三道门按序：字符层 → 校验和 → 版本 → 域界；任何一档不过都不得落档。
/// </summary>
public static class CrosshairCode
{
    public const string Prefix = "INF1-";
    public const byte FormatVersion = 1;

    private const int DataBytes = 12;
    /// <summary>12 字节 96 位 + 4 个零尾位 = 100 位 = 20 个 5 位组。</summary>
    private const int PayloadChars = 20;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford：不含 I L O U

    public static string Encode(CrosshairProfile profile) => Prefix + EncodeBytes(PackBytes(profile));

    /// <summary>档案 → 12 字节载荷（样式先归一；编码器只写界内值）。</summary>
    public static byte[] PackBytes(CrosshairProfile profile)
    {
        var p = profile.Normalized();
        var flags = (byte)((int)p.Shape << 4);
        if (p.TShape)
        {
            flags |= 1;
        }

        if (p.CenterDot)
        {
            flags |= 2;
        }

        if (p.Outline)
        {
            flags |= 4;
        }

        if (p.StateTint)
        {
            flags |= 8;
        }

        var data = new byte[DataBytes];
        data[0] = FormatVersion;
        data[1] = flags;
        data[2] = (byte)MathF.Round(p.Size * 20.0f);  // ×20＝0.05 步（SizeStep），50..300 超字节故不取 ×100
        data[3] = (byte)(((p.DotSize - 1) << 4) | (p.Thickness - 1));
        data[4] = (byte)MathF.Round(p.Gap);
        data[5] = (byte)MathF.Round(p.Alpha * 100.0f);
        data[6] = (byte)(p.RotationDeg / CrosshairProfile.RotationStep);
        data[7] = p.R;
        data[8] = p.G;
        data[9] = p.B;
        data[10] = p.A;
        data[11] = Crc8(data.AsSpan(0, 11));
        return data;
    }

    /// <summary>12 字节载荷 → 码载荷字符（不含前缀）：MSB 在前逐位进 5 位组，尾部 4 位恒 0。</summary>
    public static string EncodeBytes(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(PayloadChars);
        for (var group = 0; group < PayloadChars; group++)
        {
            var value = 0;
            for (var bit = 0; bit < 5; bit++)
            {
                value = (value << 1) | BitAt(data, group * 5 + bit);
            }

            sb.Append(Alphabet[value]);
        }

        return sb.ToString();
    }

    public static CrosshairCodeResult TryDecode(string code, out CrosshairProfile profile)
    {
        profile = CrosshairProfile.Default;
        var trimmed = code.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return CrosshairCodeResult.Malformed;
        }

        var bytes = DecodeBytes(trimmed[Prefix.Length..]);
        if (bytes == null)
        {
            return CrosshairCodeResult.Malformed;
        }

        if (Crc8(bytes.AsSpan(0, 11)) != bytes[11])
        {
            return CrosshairCodeResult.BadChecksum;
        }

        if (bytes[0] != FormatVersion)
        {
            return CrosshairCodeResult.UnsupportedVersion;
        }

        // 域界：编码器只写界内值，越界＝手构载荷，复刻必须精确不得钳入
        var size = bytes[2];
        var thickness = bytes[3] & 0x0F;
        var dotSize = bytes[3] >> 4;
        var gap = bytes[4];
        var alpha = bytes[5];
        var rotation = bytes[6];
        if (bytes[1] >> 6 != 0
            || size < (int)(CrosshairProfile.SizeMin * 20.0f) || size > (int)(CrosshairProfile.SizeMax * 20.0f)
            || thickness > CrosshairProfile.ThicknessMax - 1 || dotSize > CrosshairProfile.DotSizeMax - 1
            || gap > (int)CrosshairProfile.GapMax
            || alpha < (int)(CrosshairProfile.AlphaMin * 100.0f) || alpha > 100
            || rotation > CrosshairProfile.RotationMax / CrosshairProfile.RotationStep)
        {
            return CrosshairCodeResult.OutOfRange;
        }

        profile = new CrosshairProfile
        {
            Shape = (CrosshairShape)((bytes[1] >> 4) & 3),
            Size = size / 20.0f,
            Thickness = thickness + 1,
            DotSize = dotSize + 1,
            Gap = gap,
            Alpha = alpha / 100.0f,
            RotationDeg = rotation * CrosshairProfile.RotationStep,
            TShape = (bytes[1] & 1) != 0,
            CenterDot = (bytes[1] & 2) != 0,
            Outline = (bytes[1] & 4) != 0,
            StateTint = (bytes[1] & 8) != 0,
            R = bytes[7],
            G = bytes[8],
            B = bytes[9],
            A = bytes[10],
        };
        return CrosshairCodeResult.Ok;
    }

    /// <summary>码载荷字符 → 12 字节：长度/字符集不对返回 null；I/L→1、O→0 为 Crockford
    /// 宽容映射，小写收大写；尾部 4 个填充位不校验。</summary>
    public static byte[]? DecodeBytes(string payload)
    {
        if (payload.Length != PayloadChars)
        {
            return null;
        }

        var bytes = new byte[DataBytes];
        var bitIndex = 0;
        foreach (var raw in payload)
        {
            if (!TryCharToValue(raw, out var value))
            {
                return null;
            }

            for (var bit = 4; bit >= 0; bit--)
            {
                if (bitIndex < DataBytes * 8 && ((value >> bit) & 1) != 0)
                {
                    bytes[bitIndex >> 3] |= (byte)(0x80 >> (bitIndex & 7));
                }

                bitIndex++;
            }
        }

        return bytes;
    }

    /// <summary>CRC-8（poly 0x07，初值 0x00，无反射）：格式规格的公开面，校验对象是前 11 字节。</summary>
    public static byte Crc8(ReadOnlySpan<byte> data)
    {
        var crc = (byte)0;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x07) : (byte)(crc << 1);
            }
        }

        return crc;
    }

    private static int BitAt(ReadOnlySpan<byte> data, int index)
        => index < data.Length * 8 && (data[index >> 3] & (0x80 >> (index & 7))) != 0 ? 1 : 0;

    private static bool TryCharToValue(char raw, out int value)
    {
        value = 0;
        var ch = raw is >= 'a' and <= 'z' ? (char)(raw - 32) : raw;
        if (ch is 'I' or 'L')
        {
            ch = '1';
        }
        else if (ch == 'O')
        {
            ch = '0';
        }

        var index = Alphabet.IndexOf(ch);
        if (index < 0)
        {
            return false;
        }

        value = index;
        return true;
    }
}
