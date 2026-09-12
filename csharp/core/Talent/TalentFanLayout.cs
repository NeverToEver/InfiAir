namespace InfiAir.Core.Talent;

/// <summary>扇形展开节点坐标（1080p 设计坐标，UI 不走 world_scale）。</summary>
public sealed class TalentFanPosition
{
    public required string NodeId { get; init; }

    public required int LineIndex { get; init; }

    /// <summary>支线内层级（1 = 离根最近）。</summary>
    public required int Depth { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }
}

/// <summary>
/// 树状扇形布局（第三章）：根节点位于扇形底部中心，支线以「竖直向上为 0°、右偏为正」
/// 在 SpreadDeg 总张角内均分辐射，节点沿支线方向由内向外逐级排布（低级靠根、高级靠外）。
/// 纯几何，无 Godot 依赖。
/// </summary>
public sealed class TalentFanLayout
{
    /// <summary>扇形总张角（度）。</summary>
    public double SpreadDeg { get; init; } = 135.0;

    public double Width { get; init; } = 1200.0;

    public double Height { get; init; } = 760.0;

    /// <summary>根节点到第一级节点的径向距离（三线 ±45° 内圈弦距须容下 128px 卡片：2r·sin22.5° ≥ 136 → r ≥ 178）。</summary>
    public double RootGap { get; init; } = 190.0;

    /// <summary>层级间径向步距。140：四节点支线末端 r=RootGap+3×140=610，
    /// 竖直中线卡片中心 Y=74（卡 128px 全入面板）；同线卡间距 12px。</summary>
    public double RadiusStep { get; init; } = 140.0;

    /// <summary>根节点（大类入口）坐标。纵向系数 0.95：四节点支线全程须留在面板内
    ///（根圆 30px 贴底不越界，末端卡片中心 ≥ 卡高一半 64px）。</summary>
    public (double X, double Y) Root => (Width * 0.5, Height * 0.95);

    /// <summary>按支线排布全部节点：lines[i] = 第 i 条支线的节点 id 列表（由内向外）。</summary>
    public IReadOnlyList<TalentFanPosition> Compute(IReadOnlyList<IReadOnlyList<string>> lines)
    {
        var (cx, cy) = Root;
        var positions = new List<TalentFanPosition>();
        var lineCount = Math.Max(lines.Count, 1);
        for (var i = 0; i < lines.Count; i++)
        {
            var angleDeg = lineCount == 1
                ? 0.0
                : -SpreadDeg / 2.0 + SpreadDeg * (i + 0.5) / lineCount;
            var rad = angleDeg * Math.PI / 180.0;
            var (sin, cos) = (Math.Sin(rad), Math.Cos(rad));
            for (var j = 0; j < lines[i].Count; j++)
            {
                var r = RootGap + RadiusStep * j;
                positions.Add(new TalentFanPosition
                {
                    NodeId = lines[i][j],
                    LineIndex = i,
                    Depth = j + 1,
                    X = cx + sin * r,
                    Y = cy - cos * r,
                });
            }
        }

        return positions;
    }
}
