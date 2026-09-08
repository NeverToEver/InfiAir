namespace InfiAir;

/// <summary>
/// 音效目录键（下标即 SfxPlayer 目录表下标）。资源/音量/抖动/冷却/复音的唯一权威在 SfxPlayer 目录表；
/// FireA..FireC 是同枪三采样变体，玩家射击轮换播放，其余音效各用其一。
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
}
