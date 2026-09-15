using System;
using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>精英炮塔贴图朝向契约：炮口世界方向必须等于弹道方向。
/// 贴图炮口朝上（-Y），原实现按「炮口朝下」的公式（aim - π/2）旋转，炮口正好反 180°——
/// 表现为炮口朝上、弹体从基座方向射出，无头下不崩不报错，只能靠方向断言抓住。</summary>
public sealed class TurretAimTests
{
    private static void AssertDirectionMatches(float aimAngle)
    {
        var (x, y) = TurretAim.MuzzleDirection(TurretAim.SpriteRotation(aimAngle));
        Assert.Equal(Math.Cos(aimAngle), x, 5);
        Assert.Equal(Math.Sin(aimAngle), y, 5);
    }

    [Fact]
    public void MuzzleDirection_RotationZero_PointsUp()
    {
        // 贴图轴向自证：rotation=0 时炮口朝上（-Y）——与生成器 turret() 的「炮口朝上」一致；
        // 若贴图其实朝下，本断言先红，就不该按 -π/2 推导公式
        var (x, y) = TurretAim.MuzzleDirection(0.0f);
        Assert.Equal(0.0f, x, 5);
        Assert.Equal(-1.0f, y, 5);
    }

    [Fact]
    public void SpriteRotation_MuzzleAlignsWithAimDirection()
    {
        AssertDirectionMatches(MathF.PI / 2.0f);  // 初始朝向：正对下方玩家
        AssertDirectionMatches(0.0f);
        AssertDirectionMatches(-MathF.PI / 2.0f);
        AssertDirectionMatches(MathF.PI);
        AssertDirectionMatches(2.3f);
    }

    [Fact]
    public void SpriteRotation_ConcreteValues_PinTextureAxis()
    {
        // 钉住具体取值（不只是自洽）：瞄准 +π/2（正下方）时贴图须转 π（把朝上的炮口翻向下）。
        // 若把 MuzzleAxis 改成 +π/2（误认为炮口朝下），或把换算改回 aim - π/2，本断言先红
        Assert.Equal(-MathF.PI / 2.0f, TurretAim.MuzzleAxis, 5);
        Assert.Equal(MathF.PI, TurretAim.SpriteRotation(MathF.PI / 2.0f), 5);
        Assert.Equal(MathF.PI / 2.0f, TurretAim.SpriteRotation(0.0f), 5);
        Assert.Equal(0.0f, TurretAim.SpriteRotation(-MathF.PI / 2.0f), 5); // 瞄准正上方＝贴图原朝向
    }

    [Fact]
    public void SpriteRotation_OldFormulaWouldReverseMuzzle()
    {
        // 回归靶心：按「炮口朝下」推导的旧公式（aim - π/2）让炮口与弹道反 180°。
        // 对照两式在 aim=+π/2（正对下方玩家）下的炮口方向，防止日后有人把符号改回去
        const float aim = MathF.PI / 2.0f;
        var (_, oldY) = TurretAim.MuzzleDirection(aim - MathF.PI / 2.0f);
        var (_, newY) = TurretAim.MuzzleDirection(TurretAim.SpriteRotation(aim));
        Assert.Equal(-1.0f, oldY, 5); // 旧式：炮口朝上（与弹道相反）
        Assert.Equal(1.0f, newY, 5);  // 新式：炮口朝下（与弹道同向）
        Assert.Equal(oldY, -newY, 5);
    }
}
