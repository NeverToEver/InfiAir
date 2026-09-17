using InfiAir.Core.Practice;
using Xunit;

namespace InfiAir.Core.Tests.Practice;

/// <summary>PracticeSetup 契约测试：练习面板三行的「索引 → 文案键 / 事件 id」映射与起始分数门槛补足。
/// 这两件事实是「按钮上写着什么」与「点下去启动什么」的唯一来源——映射分叉的表现是
/// 面板显示「轰炸编队」而启动的是精英炮塔，编译、单测、冒烟都不会有任何信号。</summary>
public sealed class PracticeSetupTests
{
    [Fact]
    public void Default_IsMediumDifficultyWithoutBossOrEncounter()
    {
        var setup = PracticeSetup.Default;
        Assert.Equal(0, setup.BossType);
        Assert.Equal("medium", setup.DifficultyName);
        Assert.False(setup.HasBoss);
        Assert.False(setup.HasEncounter);
        Assert.Equal(string.Empty, setup.EncounterId);
        Assert.Equal("PRACTICE_NONE", setup.BossLabelKey);
        Assert.Equal("PRACTICE_NONE", setup.EncounterLabelKey);
        Assert.Equal("DIFF_MEDIUM", setup.DifficultyLabelKey);
    }

    [Fact]
    public void BossLabelKey_MapsZeroToNoneAndOthersToBossTypeKeys()
    {
        Assert.Equal("BOSS_TYPE_1", new PracticeSetup(1, 1, 0).BossLabelKey);
        Assert.Equal("BOSS_TYPE_4", new PracticeSetup(4, 1, 0).BossLabelKey);
        Assert.Equal("PRACTICE_NONE", new PracticeSetup(0, 1, 0).BossLabelKey);
    }

    [Fact]
    public void DifficultyIndex_MapsToSettingsKeyAndLabelKey()
    {
        Assert.Equal("easy", new PracticeSetup(0, 0, 0).DifficultyName);
        Assert.Equal("hard", new PracticeSetup(0, 2, 0).DifficultyName);
        Assert.Equal("DIFF_EASY", new PracticeSetup(0, 0, 0).DifficultyLabelKey);
        Assert.Equal("DIFF_HARD", new PracticeSetup(0, 2, 0).DifficultyLabelKey);
    }

    [Fact]
    public void EncounterSelection_MapsToProductionIdsAndExistingLabelKeys()
    {
        // 显示键复用事件自己的标题键（不新造一份叫法），id 必须与生产注册表一致
        Assert.Equal(PracticeSetup.EliteTurretId, new PracticeSetup(0, 1, 1).EncounterId);
        Assert.Equal("ETV_TITLE", new PracticeSetup(0, 1, 1).EncounterLabelKey);
        Assert.Equal(PracticeSetup.FormationStrikeId, new PracticeSetup(0, 1, 2).EncounterId);
        Assert.Equal("FBQ_HUD_TITLE", new PracticeSetup(0, 1, 2).EncounterLabelKey);
        Assert.Equal(PracticeSetup.FogEncounterId, new PracticeSetup(0, 1, 3).EncounterId);
        Assert.Equal("FOG_EVENT_FAKE_ENEMIES_NAME", new PracticeSetup(0, 1, 3).EncounterLabelKey);
        Assert.True(new PracticeSetup(0, 1, 3).EncounterIsFog);
        Assert.False(new PracticeSetup(0, 1, 1).EncounterIsFog);
    }

    [Fact]
    public void Cyclers_WrapAroundInBothDirections()
    {
        // 正向跨过末尾回 0，负向跨过 0 回末尾（负数取模在 C# 得负值，故环形口径单独钉住）
        Assert.Equal(0, new PracticeSetup(4, 1, 0).CycleBoss(1).BossType);
        Assert.Equal(4, new PracticeSetup(0, 1, 0).CycleBoss(-1).BossType);
        Assert.Equal(0, new PracticeSetup(0, 2, 0).CycleDifficulty(1).DifficultyIndex);
        Assert.Equal(2, new PracticeSetup(0, 0, 0).CycleDifficulty(-1).DifficultyIndex);
        Assert.Equal(0, new PracticeSetup(0, 1, 3).CycleEncounter(1).EncounterIndex);
        Assert.Equal(3, new PracticeSetup(0, 1, 0).CycleEncounter(-1).EncounterIndex);
        // 切换一行不动另两行
        var cycled = new PracticeSetup(2, 0, 1).CycleEncounter(2);
        Assert.Equal(2, cycled.BossType);
        Assert.Equal(0, cycled.DifficultyIndex);
        Assert.Equal(3, cycled.EncounterIndex);
    }

    [Fact]
    public void Constructor_NormalizesOutOfRangeIndexes()
    {
        // 越界索引来自命令行/外部输入时必须回正——不设非法态，就不存在「显示与启动脱钩」的第二套语义
        var setup = new PracticeSetup(5, 3, 4);
        Assert.Equal(0, setup.BossType);
        Assert.Equal(0, setup.DifficultyIndex);
        Assert.Equal(0, setup.EncounterIndex);
        var negative = new PracticeSetup(-1, -1, -1);
        Assert.Equal(4, negative.BossType);
        Assert.Equal(2, negative.DifficultyIndex);
        Assert.Equal(3, negative.EncounterIndex);
    }

    [Fact]
    public void ScoreSeed_CoversSelectedThresholdsOnly()
    {
        // 不选任何内容：不补分（练习局的分数仍是本局的一件事实，不白送）
        Assert.Equal(0, new PracticeSetup(0, 1, 0).ScoreSeed(800, 1500));
        // 选 Boss：补到第一只的分数门（时间门由直选请求替下，见 Spawner.RequestBossForPractice）
        Assert.Equal(1500, new PracticeSetup(3, 1, 0).ScoreSeed(800, 1500));
        // 选精英炮塔/轰炸编队：补到各自生产门槛，取较大者
        Assert.Equal(800, new PracticeSetup(0, 1, 1).ScoreSeed(800, 1500));
        Assert.Equal(1500, new PracticeSetup(2, 1, 1).ScoreSeed(800, 1500));
        Assert.Equal(500, new PracticeSetup(0, 1, 2).ScoreSeed(500, 1500));
        // 迷雾组无分数门槛（启动链走迷雾门控）：选了也不补分
        Assert.Equal(0, new PracticeSetup(0, 1, 3).ScoreSeed(800, 1500));
        // 坏配置（负门槛）不得让起始分数为负
        Assert.Equal(0, new PracticeSetup(0, 1, 1).ScoreSeed(-100, -100));
    }

    [Fact]
    public void RecordEquality_IsValueBased()
    {
        // 面板与探针按值比较「设置有没有变」，引用相等会让「切了一圈回到原值」判成变了
        Assert.Equal(new PracticeSetup(1, 2, 3), new PracticeSetup(1, 2, 3));
        Assert.NotEqual(new PracticeSetup(1, 2, 3), new PracticeSetup(1, 2, 0));
    }
}
