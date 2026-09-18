namespace InfiAir.Core.Hud;

/// <summary>准星形状档：bracket＝四角括角（本作原生 FUI 语汇）、cross＝经典四线十字、
/// circle＝圆环、dot＝仅中心点。准星码字节层按枚举序号打包（2 位）。</summary>
public enum CrosshairShape
{
    Bracket,
    Cross,
    Circle,
    Dot,
}

/// <summary>
/// 准星档案（纯逻辑，零 Godot 依赖）：一份可复刻的准星样式。字段范围与默认值＝设计定稿
/// （DESIGN_BASELINE「准星自定义系统」字段表，两处改动必须同提交）。record 值语义——
/// 准星码往返、设置档读档、UI 预览比对都靠值相等。读入口（设置档/准星码）拿到的实例
/// 一律先 <see cref="Normalized"/> 钳回范围：坏数据静默回界内，不得污染绘制。
/// </summary>
public sealed record CrosshairProfile
{
    // ---------------- 范围常量（设置页滑杆与准星码域界共用；改值须同步字节层容量） ----------------

    public const float SizeMin = 0.5f;
    public const float SizeMax = 3.0f;
    public const float SizeStep = 0.05f;
    public const int ThicknessMin = 1;
    public const int ThicknessMax = 6;
    public const float GapMax = 20.0f;
    public const float AlphaMin = 0.2f;
    public const int RotationStep = 15;
    public const int RotationMax = 90;
    public const int DotSizeMin = 1;
    public const int DotSizeMax = 6;

    public CrosshairShape Shape { get; init; } = CrosshairShape.Bracket;
    public float Size { get; init; } = 1.0f;
    public int Thickness { get; init; } = 2;
    public float Gap { get; init; } = 0.0f;
    public float Alpha { get; init; } = 0.95f;
    public int RotationDeg { get; init; } = 0;
    public bool CenterDot { get; init; } = true;
    public int DotSize { get; init; } = 2;
    public bool TShape { get; init; }
    public bool Outline { get; init; }
    public bool StateTint { get; init; } = true;
    public byte R { get; init; } = 255;
    public byte G { get; init; } = 194;
    public byte B { get; init; } = 77;
    public byte A { get; init; } = 242;

    /// <summary>默认档＝现版观感（琥珀括角 + 中心点）。</summary>
    public static CrosshairProfile Default { get; } = new();

    /// <summary>种子预设：经典十字（白、无点、描边、gap 4）。</summary>
    public static CrosshairProfile PresetCross { get; } = new()
    {
        Shape = CrosshairShape.Cross,
        Alpha = 1.0f,
        Gap = 4.0f,
        CenterDot = false,
        Outline = true,
        R = 255,
        G = 255,
        B = 255,
        A = 255,
    };

    /// <summary>种子预设：圆环 + 中心点。</summary>
    public static CrosshairProfile PresetCircle { get; } = new() { Shape = CrosshairShape.Circle };

    /// <summary>种子预设：净点。</summary>
    public static CrosshairProfile PresetDot { get; } = new() { Shape = CrosshairShape.Dot };

    /// <summary>全部字段钳回范围（枚举越界回 Bracket、旋转吸附步长后钳界、浮点钳界；
    /// 颜色字节天然在界内原样保留）。</summary>
    public CrosshairProfile Normalized() => new()
    {
        Shape = (int)Shape >= 0 && (int)Shape <= (int)CrosshairShape.Dot ? Shape : CrosshairShape.Bracket,
        Size = Clamp(Size, SizeMin, SizeMax),
        Thickness = Math.Clamp(Thickness, ThicknessMin, ThicknessMax),
        Gap = Clamp(Gap, 0.0f, GapMax),
        Alpha = Clamp(Alpha, AlphaMin, 1.0f),
        RotationDeg = Math.Clamp(
            (int)MathF.Round(RotationDeg / (float)RotationStep) * RotationStep, 0, RotationMax),
        CenterDot = CenterDot,
        DotSize = Math.Clamp(DotSize, DotSizeMin, DotSizeMax),
        TShape = TShape,
        Outline = Outline,
        StateTint = StateTint,
        R = R,
        G = G,
        B = B,
        A = A,
    };

    /// <summary>形状 → 设置档存储名（snake_case 字符串，可读性优先于字节紧凑）。</summary>
    public static string ShapeName(CrosshairShape shape) => shape switch
    {
        CrosshairShape.Cross => "cross",
        CrosshairShape.Circle => "circle",
        CrosshairShape.Dot => "dot",
        _ => "bracket",
    };

    /// <summary>设置档存储名 → 形状：未知名（手改档）回 Bracket，不抛。</summary>
    public static CrosshairShape ShapeFromName(string? name) => name switch
    {
        "cross" => CrosshairShape.Cross,
        "circle" => CrosshairShape.Circle,
        "dot" => CrosshairShape.Dot,
        _ => CrosshairShape.Bracket,
    };

    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
}

/// <summary>档案簿条目：样式 + 本地名。名字刻意不进准星码（跨设备复刻的是样式，名字本地化）。</summary>
public sealed record CrosshairEntry(string Name, CrosshairProfile Profile);
