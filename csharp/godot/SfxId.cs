namespace InfiAir;

/// <summary>
/// 音效目录键（下标即 SfxPlayer 目录表下标）。资源/音量/抖动/冷却/复音的唯一权威在 SfxPlayer 目录表；
/// FireA..FireC 是同枪三采样变体，玩家射击轮换播放，其余音效各用其一。
/// 末段 5 枚是界面反馈族（Ui*）：音量明显低于战斗音——UI 反馈不该盖过战斗音效。
/// **新键只能追加在末尾**：四张表都按下标对齐枚举，插队即把所有既有音效错位。
/// </summary>
public enum SfxId
{
    FireA,
    FireB,
    FireC,
    Explosion,
    ExplosionBig,
    PlayerHit,
    AugmentPick,
    Dash,
    Resupply,
    Heartbeat,
    UiHover,
    UiConfirm,
    UiCancel,
    UiToggle,
    UiDeny,
}
