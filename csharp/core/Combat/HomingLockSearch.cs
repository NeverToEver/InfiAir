namespace InfiAir.Core.Combat;

/// <summary>
/// homing 增幅的落靶搜索（纯逻辑，零 Godot 依赖）：锁定锥内、射程内的**最近**候选。
/// 扫描循环仍在引擎侧（要遍历 Godot 节点注册表并按契约判型），但「射程内 + 锥内 + 是否更近」
/// 的算式收在这里，可单测——原先内联在 Player.NearestAugHomingTarget 的逐候选循环里，
/// 判不出的坏法（射程比较反向、锁定锥漏判、与原点重合的候选被选中）只表现为
/// 「制导时有时无 / 锁错目标」，无任何运行信号。
///
/// 边界（与下沉前的引擎侧逐位同口径）：
///   - 射程闭区间：距离 == 上界算合格（原实现 <c>d &gt; best</c> 才跳过）；
///   - 同距后来者胜（同款 <c>&gt;</c> 口径）；
///   - 与原点重合（距离 0）**不合格**——此处刻意不同于 <see cref="AimTargeting.InCone"/> 的
///     「NaN 不排除」口径：那个口径服务于弱追踪（重合目标仍算锥内），而本搜索以距离为键，
///     距离 0 无意义；
///   - 非有限输入不排除（NaN 与任何值比较皆为假，会一路选中）——沿用原实现，
///     配置损坏的兜底在取值处，不在此重复判一遍。
/// 锥阈值语义（整角/半角）由调用方经 core <see cref="AimCone"/> 换算后注入，本类不解释角度。
/// </summary>
public struct HomingLockSearch
{
    private readonly float _originX;
    private readonly float _originY;
    private readonly float _aimX;
    private readonly float _aimY;
    private readonly float _coneCos;

    /// <summary>当前最优距离（初值 = 射程上界）。</summary>
    public float BestDistance { get; private set; }

    /// <summary><paramref name="aimX"/>/<paramref name="aimY"/> 须为单位向量（与引擎侧调用口径一致）；
    /// <paramref name="maxRange"/> 为射程上界（闭区间）。</summary>
    public HomingLockSearch(float originX, float originY, float aimX, float aimY, float coneCos, float maxRange)
    {
        _originX = originX;
        _originY = originY;
        _aimX = aimX;
        _aimY = aimY;
        _coneCos = coneCos;
        BestDistance = maxRange;
    }

    /// <summary>考察一个候选：合格且不比当前最优更远则更新最优距离并返回 true（调用方据此记住它）。</summary>
    public bool Consider(float targetX, float targetY)
    {
        var dx = targetX - _originX;
        var dy = targetY - _originY;
        var d = System.MathF.Sqrt(dx * dx + dy * dy);
        // 射程与重合点分两段判：合并成一个比较会同时改掉 NaN 与 0 的语义（见类注释边界）
        if (d > BestDistance || d <= 0.0f)
        {
            return false;
        }

        if (!AimTargeting.InCone(_aimX, _aimY, dx, dy, _coneCos))
        {
            return false;
        }

        BestDistance = d;
        return true;
    }
}
