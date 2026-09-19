namespace InfiAir.Core.Machines;

/// <summary>
/// 机型性格指纹的六条轴（顺序 ＝ 轮盘铭牌指纹图的绘制顺序：正上方起顺时针、每 60° 一个顶点）。
///
/// 与 <see cref="MachineTrait"/> / <see cref="MachineAxis"/> 的关键差别：那两层每条轴各有各的
/// **有利方向**（开火间隔、受到伤害、冷却、耗油都是越小越强），消费点必须逐个判方向；本层的算式
/// （除法与倒数）把方向就地抹平，六条轴一律**越大越有利**，绘制层因此不必再判方向。
/// </summary>
public enum MachineProfileAxis
{
    /// <summary>火力：攻击力乘区 ÷ 开火间隔乘区（同一段时间里既打得疼又打得密）。</summary>
    Fire,

    /// <summary>机动：移动速度乘区。</summary>
    Mobility,

    /// <summary>装甲：血量上限乘区 ÷ 受到伤害乘区（一份血能扛多少伤害）。</summary>
    Armor,

    /// <summary>弹反：盾的有效占空相对基准（窗越宽、循环越短，占空越高）。</summary>
    Parry,

    /// <summary>突进：冲刺冷却乘区的倒数（冷却越短，单位时间里能突进越多次）。</summary>
    Dash,

    /// <summary>续航：加速耗油乘区的倒数（耗油越低，一箱油推得越久）。</summary>
    Endurance,
}

/// <summary>指纹图上的一个顶点（屏幕坐标，y 向下；core 不引 Godot 类型，故用 BCL 类型自描述）。</summary>
public readonly record struct ProfilePoint(double X, double Y);

/// <summary>
/// 机型性格指纹（纯逻辑，零 Godot 依赖）：把两层乘区（数值层 <see cref="MachineModifiers"/> 与
/// 能力层 <see cref="MachineKit"/>）压成六条**同向**的轴，供轮盘铭牌画成一张六边形雷达图。
///
/// **为什么中心是基准而不是零**：六型是被刻意平衡过的（差异压在亚一层增幅之下），按绝对刻度
/// （0 → 取值的最大值）画，六个多边形会长得几乎一样——那就白做了一张图。故本图的口径是
/// **相对标准型的倍数**：中心 ＝ 标准型基准（<see cref="BaselineRatio"/>），基准落在一半半径处
/// （<see cref="BaselineStrength"/>），凸起读作强于基准、凹陷读作弱于基准。玩家要看的不是
/// 「这一型有多少火力」，而是「这一型跟标准型比，强在哪、弱在哪」——这正是形状能一眼交付的东西。
///
/// **刻度带为什么是 [0.60, 1.40]**（<see cref="ScaleMin"/> / <see cref="ScaleMax"/>）：当前
/// 六型 × 六轴的全体取值域是 0.870–1.400，取整做成对称带（±0.40）。带子越窄形状越夸张、越窄的
/// 差异会被放大成读不出的噪声，越宽则多边形越挤在中间；将来有型越界就**钳到边界**（不重排刻度带）
/// ——边界顶格是可见的读数（这条轴已到顶），重排刻度带会让同一张图在两个版本里长度不可比。
///
/// 非有限值一律回基准：NaN 顶点会让 Godot 的整块多边形不画且**不报错**（三角化直接失败），
/// 而本图是玩家唯一能读出六型性格的地方——静默空白比画错更难查（同 <see cref="MachineKitTable.Sanitize"/>
/// 的 NaN 口径）。
/// </summary>
public static class MachineProfileTable
{
    /// <summary>刻度带下限（相对标准型的倍数）：越界钳到这里。</summary>
    public const double ScaleMin = 0.60;

    /// <summary>刻度带上限。</summary>
    public const double ScaleMax = 1.40;

    /// <summary>基准倍数（标准型恒等）：外圈刻度带的中点。</summary>
    public const double BaselineRatio = 1.0;

    /// <summary>基准强度（半径比例）：基准环画在一半半径处——凸起与凹陷各占一半半径，
    /// 两个方向都留得出可读的行程。</summary>
    public const double BaselineStrength = 0.5;

    /// <summary>轴数（＝名册六型之外的第二个六：绘制顺序每 60° 一个顶点）。</summary>
    public const int AxisCount = 6;

    /// <summary>绘制顺序（＝本表全部方法的索引口径）：正上方起顺时针。改顺序只是换画的排布，
    /// 不影响任何取值——面板与图都只读本表，不各写一份轴序。</summary>
    public static readonly IReadOnlyList<MachineProfileAxis> DrawingOrder = new[]
    {
        MachineProfileAxis.Fire,
        MachineProfileAxis.Mobility,
        MachineProfileAxis.Armor,
        MachineProfileAxis.Parry,
        MachineProfileAxis.Dash,
        MachineProfileAxis.Endurance,
    };

    /// <summary>该轴在文案表里的键（<c>MACHINE_PROFILE_*</c>；键名字符串只有这一处）。</summary>
    public static string TextKey(MachineProfileAxis axis) => axis switch
    {
        MachineProfileAxis.Fire => "MACHINE_PROFILE_FIRE",
        MachineProfileAxis.Mobility => "MACHINE_PROFILE_MOBILITY",
        MachineProfileAxis.Armor => "MACHINE_PROFILE_ARMOR",
        MachineProfileAxis.Parry => "MACHINE_PROFILE_PARRY",
        MachineProfileAxis.Dash => "MACHINE_PROFILE_DASH",
        MachineProfileAxis.Endurance => "MACHINE_PROFILE_ENDURANCE",
        _ => "MACHINE_PROFILE_FIRE",
    };

    /// <summary>
    /// 该轴相对标准型的倍数（1.0 ＝ 基准，越大越有利）。**已含刻度带钳制**：越界取边界值、
    /// 非有限值回基准——绘制层拿到的一定是 [<see cref="ScaleMin"/>, <see cref="ScaleMax"/>] 内的有限值，
    /// 不得再乘任何系数（再乘一次就越出了刻度带的语义）。
    ///
    /// 弹反轴读的是**盾的有效占空**（激活窗 ÷ 弹反循环），不是窗本身：窗 ×1.40 只在循环不变时
    /// 才等于占空 ×1.40（壁垒就是这一档），而循环一变窗的边际价值也跟着变——两条轴分开画会把
    /// 「窗宽但循环长」读成两个独立优点。0.8 与 3.0 的单源在 <c>player.parry.duration</c> /
    /// <c>player.parry.cooldown</c>（见 <see cref="MachineKitTable.ParryDurationBase"/> 等），
    /// 3.8 复用它俩之和，不新写常量。
    /// </summary>
    public static double Ratio(MachineProfileAxis axis, MachineModifiers mods, MachineKit kit)
    {
        // 先收口两层乘区（域钳 + NaN 回基准）再相除：NaN 或 0 分母会一路渗进顶点坐标，
        // 而钳制函数本身不拦 NaN（同 MachineKitTable.Sanitize 的口径）
        var m = MachineRoster.Sanitize(mods);
        var k = MachineKitTable.Sanitize(kit);
        var value = axis switch
        {
            MachineProfileAxis.Fire => m.DamageMult / m.FireIntervalMult,
            MachineProfileAxis.Mobility => m.MoveSpeedMult,
            MachineProfileAxis.Armor => m.MaxHpMult / m.DamageTakenMult,
            MachineProfileAxis.Parry =>
                k.ParryWindowMult * MachineKitTable.ParryCycleBase
                / (MachineKitTable.ParryDurationBase + MachineKitTable.ParryCooldownBase * k.ParryCooldownMult),
            MachineProfileAxis.Dash => 1.0 / k.DashCooldownMult,
            MachineProfileAxis.Endurance => 1.0 / k.FuelDrainMult,
            _ => BaselineRatio,
        };

        return double.IsFinite(value) ? Math.Clamp(value, ScaleMin, ScaleMax) : BaselineRatio;
    }

    /// <summary>该轴的归一强度（0 ＝ 刻度带下限、<see cref="BaselineStrength"/> ＝ 基准、1 ＝ 上限），
    /// 即顶点到中心的距离占半径的比例。钳制同 <see cref="Ratio"/>（倍数额外钳一次，防止
    /// 将来改刻度带时强度越出 [0,1] 把顶点画到圆外）。</summary>
    public static double Strength(MachineProfileAxis axis, MachineModifiers mods, MachineKit kit)
    {
        var ratio = Ratio(axis, mods, kit);
        return Math.Clamp(
            (ratio - ScaleMin) / (ScaleMax - ScaleMin),
            0.0,
            1.0);
    }

    /// <summary>第 <paramref name="index"/> 条轴的方向角（弧度）：索引 0 ＝ 正上方，顺时针每 60°。
    /// 屏幕坐标 y 向下，故 -90° 是正上方（<c>Cos/Sin</c> 直接可用，无需翻转 y）。</summary>
    public static double AxisAngle(int index) => -Math.PI / 2.0 + Math.Tau * index / AxisCount;

    /// <summary>
    /// 一个顶点（屏幕坐标，y 向下）：方向取第 <paramref name="index"/> 条轴，距离 ＝ 半径 ×
    /// 对应强度。<paramref name="strength"/> 同样收口（非有限值回基准、钳 [0,1]），
    /// 且半径非有限或非正时退回中心——NaN 顶点会让整块多边形不画且不报错，这一层是最后一道拦。
    /// </summary>
    public static ProfilePoint Vertex(double centerX, double centerY, double radius, int index, double strength)
    {
        var s = double.IsFinite(strength) ? Math.Clamp(strength, 0.0, 1.0) : BaselineStrength;
        var r = double.IsFinite(radius) && radius > 0.0 ? radius * s : 0.0;
        var angle = AxisAngle(index);
        return new ProfilePoint(centerX + r * Math.Cos(angle), centerY + r * Math.Sin(angle));
    }

    /// <summary>数据多边形（六点，按 <see cref="DrawingOrder"/> 逐轴取 <see cref="Strength"/>）。</summary>
    public static ProfilePoint[] Polygon(double centerX, double centerY, double radius, MachineModifiers mods, MachineKit kit)
    {
        var points = new ProfilePoint[AxisCount];
        for (var i = 0; i < AxisCount; i++)
        {
            points[i] = Vertex(centerX, centerY, radius, i, Strength(DrawingOrder[i], mods, kit));
        }

        return points;
    }

    /// <summary>刻度环 / 基准环（六点同强度）：外圈传强度 1.0、基准环传
    /// <see cref="BaselineStrength"/>，与数据多边形共用同一套顶点算式——环与数据各画一套角度，
    /// 早晚会在某次改动后错开半格，而错开不报错、只是看起来「不对齐」。</summary>
    public static ProfilePoint[] Ring(double centerX, double centerY, double radius, double strength)
    {
        var points = new ProfilePoint[AxisCount];
        for (var i = 0; i < AxisCount; i++)
        {
            points[i] = Vertex(centerX, centerY, radius, i, strength);
        }

        return points;
    }
}
