using InfiAir.Core.Storage;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// UserDb 局外成长档案（meta 字段，2026-08-09 局外成长 M2）单测：
/// 创建默认档案 / 更新往返 / 手改非法类型回默认 / 旧记录兼容 / 不存在用户守卫。
/// 判型防御对齐既有 Q17 条目级守卫风格。
/// </summary>
public sealed class UserDbMetaTests
{
    private static UserDb NewDb(out string dir, out string usersPath)
    {
        dir = Path.Combine(Path.GetTempPath(), "infiair-userdb-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        usersPath = Path.Combine(dir, "users.json");
        return new UserDb(usersPath);
    }

    [Fact]
    public void CreateUser_MetaDefaultsToEmpty()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            Assert.True(db.CreateUser("alice", "s3cret", 1000));
            var meta = db.GetUserMeta("alice");
            Assert.Equal(0L, meta.GetValueOrDefault("tech_points"));
            var upgrades = meta.GetValueOrDefault("upgrades");
            Assert.IsType<Dictionary<string, object?>>(upgrades);
            Assert.Empty((Dictionary<string, object?>)upgrades);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UpdateAndRead_RoundTrip()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            Assert.True(db.CreateUser("bob", "pass123", 1000));
            db.UpdateUserMeta("bob", new Dictionary<string, object?>
            {
                ["tech_points"] = 25L,
                ["upgrades"] = new Dictionary<string, object?>
                {
                    ["rapid_fire"] = 2L,
                    ["armor"] = 1L,
                },
            });

            var meta = db.GetUserMeta("bob");
            Assert.Equal(25L, meta.GetValueOrDefault("tech_points"));
            var upgrades = (Dictionary<string, object?>)meta.GetValueOrDefault("upgrades")!;
            Assert.Equal(2L, upgrades.GetValueOrDefault("rapid_fire"));
            Assert.Equal(1L, upgrades.GetValueOrDefault("armor"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Update_MergesNotReplaces()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            Assert.True(db.CreateUser("carol", "pass123", 1000));
            db.UpdateUserMeta("carol", new Dictionary<string, object?> { ["tech_points"] = 10L });
            db.UpdateUserMeta("carol", new Dictionary<string, object?> { ["tech_points"] = 7L });

            var meta = db.GetUserMeta("carol");
            Assert.Equal(7L, meta.GetValueOrDefault("tech_points")); // 顶层浅合并覆盖
            Assert.NotNull(meta.GetValueOrDefault("upgrades"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetMeta_MissingUser_ReturnsDefault()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            var meta = db.GetUserMeta("nobody");
            Assert.Equal(0L, meta.GetValueOrDefault("tech_points"));
            Assert.Empty((Dictionary<string, object?>)meta.GetValueOrDefault("upgrades")!);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UpdateMeta_MissingUser_Skips()
    {
        var db = NewDb(out var dir, out _);
        try
        {
            db.UpdateUserMeta("nobody", new Dictionary<string, object?> { ["tech_points"] = 99L });
            // 不抛异常且不产生用户
            Assert.False(db.UserExists("nobody"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetMeta_HandModifiedInvalidType_ReturnsDefault()
    {
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            Assert.True(db.CreateUser("dave", "pass123", 1000));
            // 手改 users.json：meta 写成字符串（判型守卫应回默认，不崩）
            db.UpdateUserData("dave", new Dictionary<string, object?> { ["meta"] = "garbage" });
            var meta = db.GetUserMeta("dave");
            Assert.Equal(0L, meta.GetValueOrDefault("tech_points"));
            Assert.Empty((Dictionary<string, object?>)meta.GetValueOrDefault("upgrades")!);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetMeta_LegacyRecordWithoutMeta_ReturnsDefault_AndUpdatePersists()
    {
        var db = NewDb(out var dir, out var usersPath);
        try
        {
            Assert.True(db.CreateUser("erin", "pass123", 1000));
            // 模拟旧版本记录：删掉 meta 字段
            var data = db.GetUserData("erin");
            Assert.True(data.Remove("meta"));
            db.UpdateUserData("erin", data);

            var meta = db.GetUserMeta("erin");
            Assert.Equal(0L, meta.GetValueOrDefault("tech_points")); // 旧记录兼容回默认

            // 首次结算落盘后字段恢复
            db.UpdateUserMeta("erin", new Dictionary<string, object?> { ["tech_points"] = 5L });
            Assert.Equal(5L, db.GetUserMeta("erin").GetValueOrDefault("tech_points"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
