using InfiAir.Core.Storage;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// 密码派生兼容测试（P0-2）：固定向量由迁移前的 GDScript UserDB._derive 实测生成
/// （生成脚本注意：盐解析必须用 String.hex_to_int()——GDScript 的 int("0x" + s) 按十进制
/// 解析 "0x11" → 11，首次生成曾因此产出污染向量，C#/Python 独立复算后纠正）。
/// C# 端逐字节等价即「既有账号验密不破坏」的硬约束。
/// </summary>
public sealed class UserDbPasswordTests
{
    [Theory]
    [InlineData("alice", "00112233445566778899aabbccddeeff", 3, "b8d1b06778ab126f3f5a57aa15e5b19eecff51ed9f2595a2f69edb950b1605e3")]
    [InlineData("s3cret", "deadbeefdeadbeefdeadbeefdeadbeef", 1000, "76a4bc7172651d55d0139b2622619de3ce63d54dec94d0598021db6ad0b9d03d")]
    [InlineData("密码测试pwd", "00000000000000000000000000000000", 5, "ab875395d354e81c040cbad03b2a0e775e1b4b66f7f83ed5de0a50c83e2543b2")]
    [InlineData("Passw0rd!", "0123456789abcdef0123456789abcdef", 50000, "c551b39248018ed0c143ac18cc6b96c68584afe83c4354c61c8c1a536fef5da6")]
    [InlineData("bob", "a1b2c3d4e5f60718293a4b5c6d7e8f90", 50000, "8a52162f21f4c53eee6f6deb2e6b19fa7718256aa07f4995bf5a96f6405a8728")]
    public void Derive_MatchesGdscriptReferenceVector(string password, string saltHex, long iterations, string expectedHex)
    {
        var derived = UserDb.Derive(password, Convert.FromHexString(saltHex), iterations);

        // 小写 hex 对照（ToHexStringLower 为 .NET 9 API，目标 .NET 8）
        var hex = Convert.ToHexString(derived).ToLowerInvariant();
        Assert.Equal(expectedHex, hex);
    }

    [Fact]
    public void Verify_ExistingAccountWithGdscriptGeneratedRecord_Succeeds()
    {
        // 存量账号夹具：盐/迭代数/哈希全部由迁移前 GDScript 生成（alice/s3cret/1000 次迭代）
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "users.json");
            File.WriteAllText(
                path,
                """{"_users": {"alice": {"password": "76a4bc7172651d55d0139b2622619de3ce63d54dec94d0598021db6ad0b9d03d", "salt": "deadbeefdeadbeefdeadbeefdeadbeef", "iterations": 1000, "high_score": 0, "last_login_order": 0, "settings": {}}}, "_leaderboard": [], "_seq": 0}""");
            var db = new UserDb(path);

            Assert.True(db.VerifyUser("alice", "s3cret", UserDb.Pbkdf2Iterations), "存量 GDScript 账号验密必须通过");
            Assert.False(db.VerifyUser("alice", "wrong", UserDb.Pbkdf2Iterations), "错误密码验密失败");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Create_ThenVerify_RoundTrip()
    {
        var db = NewTempDb(out var dir);
        try
        {
            Assert.True(db.CreateUser("alice", "s3cret", 1000));
            Assert.True(db.VerifyUser("alice", "s3cret", UserDb.Pbkdf2Iterations));
            Assert.False(db.VerifyUser("alice", "wrong", UserDb.Pbkdf2Iterations));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Verify_StoredIterationsWin_OverFallback()
    {
        var db = NewTempDb(out var dir);
        try
        {
            db.CreateUser("alice", "s3cret", 1000);
            // 降档参数只作缺键回退——记录已存档的迭代数优先（生产/测试混用不破坏既有账号）
            Assert.True(db.VerifyUser("alice", "s3cret", UserDb.Pbkdf2Iterations));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Verify_InvalidHexInRecord_FailsSafely()
    {
        // Q18 同款：奇数长度 / 非法字符 / 大写（原 GDScript find 白名单仅小写）→ 空盐/空密文 → 安全失败
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "users.json");
            File.WriteAllText(
                path,
                """{"_users": {"bob": {"password": "00", "salt": "abc", "iterations": 1000, "last_login_order": 0}}}""");
            var db = new UserDb(path);
            Assert.False(db.VerifyUser("bob", "pass123", 1000), "奇数长度 salt 验密安全失败");

            File.WriteAllText(
                path,
                """{"_users": {"bob": {"password": "zz", "salt": "zz", "iterations": 1000, "last_login_order": 0}}}""");
            db.Reload();
            Assert.False(db.VerifyUser("bob", "pass123", 1000), "非法 hex 盐/密文验密安全失败");

            File.WriteAllText(
                path,
                """{"_users": {"bob": {"password": "AA", "salt": "AA", "iterations": 1000, "last_login_order": 0}}}""");
            db.Reload();
            Assert.False(db.VerifyUser("bob", "pass123", 1000), "大写 hex（原实现白名单拒绝）验密安全失败");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static UserDb NewTempDb(out string dir)
    {
        dir = TempDir();
        return new UserDb(Path.Combine(dir, "users.json"));
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "infiair-userdb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
