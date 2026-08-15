namespace InfiAir;

/// <summary>
/// 可被召唤减速场影响的实体契约（可扩展减速管线）：
/// 新增敌机/Boss 类实现本接口即可被母舰入场减速场统一命中，
/// 无需在 <c>Mothership.DeploySlowField</c> 中追加类型分支。
/// </summary>
public interface ISlowable
{
    /// <summary>施加持续 duration 秒、速度乘 factor 的召唤减速。</summary>
    void ApplySlow(float duration, float factor);
}
