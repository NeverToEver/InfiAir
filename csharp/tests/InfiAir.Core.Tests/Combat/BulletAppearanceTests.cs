using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>弹体外观档判定测试。钉住三条契约：
/// ① 高对比只作用于敌弹（玩家弹恒为原档——给玩家弹加描边会与敌弹同形）；
/// ② 开关关闭时敌弹恒为原档（「中性态逐位等于原实现」这条表现层硬要求的判定半边）；
/// ③ 开关开启时敌弹换档（接线断掉的表现是弹幕观感变化，不崩不报错，只有这里能判）。</summary>
public sealed class BulletAppearanceTests
{
    [Fact]
    public void SkinFor_PlayerBullet_IgnoresContrast()
    {
        Assert.Equal(BulletSkin.Player, BulletAppearance.SkinFor(true, false));
        Assert.Equal(BulletSkin.Player, BulletAppearance.SkinFor(true, true));
    }

    [Fact]
    public void SkinFor_EnemyWithoutContrast_IsPlainEnemySkin()
    {
        Assert.Equal(BulletSkin.Enemy, BulletAppearance.SkinFor(false, false));
    }

    [Fact]
    public void SkinFor_EnemyWithContrast_IsOutlinedSkin()
    {
        Assert.Equal(BulletSkin.EnemyContrast, BulletAppearance.SkinFor(false, true));
    }
}
