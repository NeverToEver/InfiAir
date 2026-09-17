namespace InfiAir.Core.Combat;

/// <summary>
/// dash_strike 增幅的触及判定（纯逻辑，零 Godot 依赖）：目标是否落在打击半径内。
/// 原先内联在 Player.TickDashStrike 的逐帧扫描里（<c>DistanceSquaredTo &lt;= radius²</c>），
/// 坏了只表现为「冲刺打不到贴脸的敌机」，无任何运行信号，故算式收在这里。
/// 契约准入（目标必须是可打的 <c>IAimTarget</c>）由引擎侧判型后短路，本类只管几何。
/// </summary>
public static class DashStrikeReach
{
    /// <summary>触及判定（半径闭区间）：距离² ≤ 半径²，免开方。</summary>
    public static bool Hits(float dx, float dy, float radius) => dx * dx + dy * dy <= radius * radius;
}
