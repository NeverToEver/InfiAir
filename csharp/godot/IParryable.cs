namespace InfiAir;

/// <summary>
/// 可弹反投射物契约：弧光弹反盾按此契约分派，与具体类型解耦。
/// 敌弹（Bullet）与非子弹类威胁（轰炸编队的下落炸弹）都实现本接口——
/// 盾只做「判定 + 调 Reflect」，各自的反射语义（弹体镜面反弹 / 炸弹反向上升）由实现方决定。
/// </summary>
public interface IParryable
{
    /// <summary>执行弹反：按各自语义改写运动/阵营/伤害。返回 false 表示当前不可弹反（已反射/已失活）。</summary>
    bool Reflect();
}
