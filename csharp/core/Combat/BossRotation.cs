namespace InfiAir.Core.Combat;

/// <summary>
/// Boss 轮换与离场结算（纯逻辑，零 Godot 依赖）：
/// 出场型号由击杀序数派生——第 N 只 Boss = 第 (N-1)%4+1 型（轮换扩 4 型含月蚀）；
/// 离场只认「本次是否逃跑」：逃跑既不推进轮换（击杀数不动，下一只仍是同一型，读作
/// 「这场没打完的仗重新来过」），也不给休整波次；只有击毁才推进轮换并追加休整。
///
/// 为什么下沉：这两件事原先只写在 Spawner 的击杀/逃跑回调与出场派生里，而逃跑路径要跑满
/// 50 秒模拟时长才到得了——常规冒烟碰不到，写坏的表现是「打完一只后又是同一型」或
/// 「逃跑后凭空少了休整波」，两者都不崩、不报错、画面也无异常。
/// 「逃跑后仍是同一型」这条不变式靠**击杀数不推进**实现（推进点在计分域：Boss 击毁时
/// AddBossKill，逃跑路径不调用），故型号派生与离场结算共看同一个击杀数——单测把两者串起来钉住。
/// </summary>
public static class BossRotation
{
    /// <summary>轮换型号数（1 重装 / 2 游击 / 3 母舰 / 4 月蚀）。</summary>
    public const int TypeCount = 4;

    /// <summary>出场型号：当前击杀数下**下一只** Boss 的型号（首只为 1 型）。
    /// 负值（手改存档/计数器错乱）按 0 计——负数取模会得到 0 或负型号，直传会让轮换表错位。</summary>
    public static int TypeForKills(int bossKills)
    {
        var kills = bossKills > 0 ? bossKills : 0;
        return (kills % TypeCount) + 1;
    }

    /// <summary>离场是否推进轮换：击毁推进（击杀数由计分域 +1）、逃跑不推进。</summary>
    public static bool RotationAdvances(bool escaped) => !escaped;

    /// <summary>离场是否给休整波次：只有击毁给（逃跑后按分数/时间门继续照常调度，不额外拉长间隔）。</summary>
    public static bool Rests(bool escaped) => !escaped;
}
