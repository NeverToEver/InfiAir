using InfiAir.Core.Storage;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// SaveStore（P0-1）单测：原子写/覆盖、缺失三态、损坏隔离、非对象根隔离。
/// 临时目录运行，不触碰 user://；语义对齐原 GDScript SaveManager（scripts/save_manager.gd）。
/// </summary>
public sealed class SaveStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "infiair-savestore-" + Guid.NewGuid().ToString("N"));

    public SaveStoreTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Tmp(string name) => System.IO.Path.Combine(_dir, name);

    [Fact]
    public void Save_NewFile_WritesJsonContent()
    {
        var store = new SaveStore();
        var tree = new Dictionary<string, object?>
        {
            ["version"] = 2L,
            ["score"] = 500L,
            ["drain"] = 35.0,
            ["nested"] = new Dictionary<string, object?> { ["k"] = "v" },
            ["arr"] = new List<object?> { 1L, 2.5, true, null },
        };

        Assert.True(store.TrySave(Tmp("a.json"), tree, out var error));
        Assert.Null(error);
        var text = File.ReadAllText(Tmp("a.json"));
        Assert.Contains("\"version\":2", text);
        Assert.Contains("\"score\":500", text);
        Assert.Contains("\"nested\":", text);
        Assert.False(File.Exists(Tmp("a.json.tmp")), "原子写后不留 tmp 孤立文件");
    }

    [Fact]
    public void Save_Overwrite_ReplacesContent()
    {
        var store = new SaveStore();
        Assert.True(store.TrySave(Tmp("b.json"), new Dictionary<string, object?> { ["v"] = 1L }, out _));
        Assert.True(store.TrySave(Tmp("b.json"), new Dictionary<string, object?> { ["v"] = 999L }, out _));

        var res = store.Load(Tmp("b.json"));
        Assert.Equal(SaveLoadStatus.Ok, res.Status);
        Assert.Equal(999L, res.Tree!["v"]);
    }

    [Fact]
    public void Save_TargetIsDirectory_ReturnsFalseWithError()
    {
        var store = new SaveStore();
        Directory.CreateDirectory(Tmp("dir"));

        Assert.False(store.TrySave(Tmp("dir"), new Dictionary<string, object?> { ["v"] = 1L }, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Load_MissingFile_ReturnsMissing()
    {
        var res = new SaveStore().Load(Tmp("nope.json"));

        Assert.Equal(SaveLoadStatus.Missing, res.Status);
        Assert.Null(res.Tree);
    }

    [Fact]
    public void Load_ValidJson_NumericFidelity()
    {
        File.WriteAllText(Tmp("c.json"), """{"i": 42, "f": 1.5, "e": 1e3, "s": "x", "b": true, "n": null}""");

        var res = new SaveStore().Load(Tmp("c.json"));

        Assert.Equal(SaveLoadStatus.Ok, res.Status);
        Assert.Equal(42L, res.Tree!["i"]);
        Assert.Equal(1.5, res.Tree["f"]);
        Assert.Equal(1000.0, res.Tree["e"]); // 指数记法 → 浮点（GDScript JSON.parse 同语义）
        Assert.Equal("x", res.Tree["s"]);
        Assert.Equal(true, res.Tree["b"]);
        Assert.Null(res.Tree["n"]);
    }

    [Fact]
    public void Load_CorruptJson_Quarantines()
    {
        File.WriteAllText(Tmp("d.json"), "{broken json!!!");

        var res = new SaveStore().Load(Tmp("d.json"));

        Assert.Equal(SaveLoadStatus.Corrupt, res.Status);
        Assert.True(File.Exists(Tmp("d.json.corrupt")), "损坏文件隔离为 .corrupt");
        Assert.False(File.Exists(Tmp("d.json")), "隔离后正本消失");
    }

    [Fact]
    public void Load_NonObjectRoot_Quarantines()
    {
        File.WriteAllText(Tmp("e.json"), "[1, 2, 3]");

        var res = new SaveStore().Load(Tmp("e.json"));

        Assert.Equal(SaveLoadStatus.Corrupt, res.Status);
        Assert.True(File.Exists(Tmp("e.json.corrupt")));
    }

    [Fact]
    public void Load_Quarantine_ReplacesExistingBackup()
    {
        File.WriteAllText(Tmp("f.json"), "old content");
        File.WriteAllText(Tmp("f.json.corrupt"), "previous backup");
        File.WriteAllText(Tmp("f.json"), "{bad");

        var res = new SaveStore().Load(Tmp("f.json"));

        Assert.Equal(SaveLoadStatus.Corrupt, res.Status);
        Assert.Equal("{bad", File.ReadAllText(Tmp("f.json.corrupt")));
    }

    [Fact]
    public void Quarantine_MovesFileAndCleansOldBackup()
    {
        File.WriteAllText(Tmp("g.json"), "data");
        File.WriteAllText(Tmp("g.json.corrupt"), "stale");

        var store = new SaveStore();
        Assert.True(store.Quarantine(Tmp("g.json"), out _));
        Assert.False(File.Exists(Tmp("g.json")));
        Assert.Equal("data", File.ReadAllText(Tmp("g.json.corrupt")));
    }

    [Fact]
    public void Delete_RemovesFile_AndIsIdempotent()
    {
        var store = new SaveStore();
        File.WriteAllText(Tmp("h.json"), "x");

        store.Delete(Tmp("h.json"));
        Assert.False(File.Exists(Tmp("h.json")));
        store.Delete(Tmp("h.json")); // 不存在时静默成功
    }

    [Fact]
    public void Exists_DetectsFile()
    {
        var store = new SaveStore();
        Assert.False(store.Exists(Tmp("i.json")));
        File.WriteAllText(Tmp("i.json"), "x");
        Assert.True(store.Exists(Tmp("i.json")));
    }
}
