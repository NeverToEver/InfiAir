using InfiAir.Core.Storage;
using Xunit;

namespace InfiAir.Core.Tests.Storage;

/// <summary>SaveStore 契约测试：原子写/损坏隔离/判型读取。存档写错的表现是进度静默丢失，
/// 这些用例钉住「损坏必须被隔离、坏字段必须可判」的底线。</summary>
public sealed class SaveStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "infiair-tests-" + Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_dir, name);

    public SaveStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsNestedTree()
    {
        var store = new SaveStore();
        var path = PathFor("run.json");
        var tree = new Dictionary<string, object?>
        {
            ["score"] = 1234L,
            ["health"] = 87.5,
            ["name"] = "黎明站",
            ["flags"] = new List<object?> { true, false },
            ["nested"] = new Dictionary<string, object?> { ["level"] = 3L },
        };

        Assert.True(store.TrySave(path, tree, out var error));
        Assert.Null(error);

        var result = store.Load(path);
        Assert.Equal(SaveLoadStatus.Ok, result.Status);
        Assert.NotNull(result.Tree);
        Assert.Equal(1234L, result.Tree!["score"]);
        Assert.Equal(87.5, result.Tree["health"]);
        Assert.Equal("黎明站", result.Tree["name"]);
        var roundTripped = Assert.IsType<List<object?>>(result.Tree["flags"]);
        Assert.Equal(2, roundTripped.Count);
    }

    [Fact]
    public void Load_MissingFile_ReturnsMissing()
    {
        var result = new SaveStore().Load(PathFor("absent.json"));
        Assert.Equal(SaveLoadStatus.Missing, result.Status);
        Assert.Null(result.Tree);
    }

    [Fact]
    public void Load_BrokenJson_QuarantinesAsCorrupt()
    {
        var store = new SaveStore();
        var path = PathFor("broken.json");
        File.WriteAllText(path, "{ not valid json");

        var result = store.Load(path);

        Assert.Equal(SaveLoadStatus.Corrupt, result.Status);
        Assert.False(File.Exists(path));           // 正本已改名隔离
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Load_DuplicateKeys_QuarantinesAsCorrupt()
    {
        // System.Text.Json 对重复键在解析/枚举期抛 ArgumentException——
        // 必须与语法损坏同路径隔离，否则异常逃逸击穿「损坏回退」契约
        var store = new SaveStore();
        var path = PathFor("dup.json");
        File.WriteAllText(path, """{"a":1,"a":2}""");

        Assert.Equal(SaveLoadStatus.Corrupt, store.Load(path).Status);
    }

    [Fact]
    public void Load_OverflowNumber_BecomesNullInsteadOfThrowing()
    {
        // 手改 1e999：TryGetValue<double> 失败必须回退 null，不得抛异常击穿 Load 契约
        var store = new SaveStore();
        var path = PathFor("overflow.json");
        File.WriteAllText(path, """{"a":1e999}""");

        var result = store.Load(path);

        Assert.Equal(SaveLoadStatus.Ok, result.Status);
        Assert.Null(result.Tree!["a"]);
    }

    [Fact]
    public void Save_OverwritesExistingContent()
    {
        var store = new SaveStore();
        var path = PathFor("overwrite.json");
        Assert.True(store.TrySave(path, new Dictionary<string, object?> { ["v"] = 1L }, out _));
        Assert.True(store.TrySave(path, new Dictionary<string, object?> { ["v"] = 2L }, out _));

        var result = store.Load(path);
        Assert.Equal(2L, result.Tree!["v"]);
    }

    [Fact]
    public void Save_ToMissingDirectory_FailsWithErrorMessage()
    {
        var store = new SaveStore();
        var path = Path.Combine(_dir, "no-such-dir", "x.json");

        Assert.False(store.TrySave(path, new Dictionary<string, object?>(), out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Delete_RemovesFileAndToleratesAbsentTarget()
    {
        var store = new SaveStore();
        var path = PathFor("gone.json");
        File.WriteAllText(path, "{}");

        store.Delete(path);
        Assert.False(File.Exists(path));
        store.Delete(path); // 不存在时静默成功，不抛
    }

    [Fact]
    public void Quarantine_RenamesExistingBackup()
    {
        var store = new SaveStore();
        var path = PathFor("q.json");
        File.WriteAllText(path, "old");
        File.WriteAllText(path + ".corrupt", "older");

        Assert.True(store.Quarantine(path, out _));
        Assert.Equal("old", File.ReadAllText(path + ".corrupt")); // 旧备份先删，新损坏档顶替
    }
}
