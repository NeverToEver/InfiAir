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

        Assert.True(store.Delete(path, out var error));
        Assert.Null(error);
        Assert.False(File.Exists(path));
        Assert.True(store.Delete(path, out _)); // 不存在时静默成功，不抛
    }

    [Fact]
    public void Delete_OccupiedByDirectory_FailsWithReason()
    {
        // 删除失败必须可判（旧实现吞异常返回 void）：死亡删档失败会被静默忽略，
        // 玩家仍能读回进度而无人知晓。目录占位是跨平台可复现的失败触发（Linux 上只读
        // 文件照样能 unlink，不能用文件权限构造确定性用例）。
        var store = new SaveStore();
        var path = PathFor("occupied.json");
        Directory.CreateDirectory(path);

        Assert.False(store.Delete(path, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Load_OccupiedByDirectory_ReturnsUnreadableNotMissing()
    {
        // 「读不出」不等于「没有」：按 Missing 处理会让上层把玩家进度覆盖成全新一局
        var store = new SaveStore();
        var path = PathFor("unreadable.json");
        Directory.CreateDirectory(path);

        var result = store.Load(path);

        Assert.Equal(SaveLoadStatus.Unreadable, result.Status);
        Assert.Null(result.Tree);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Load_OversizedFile_QuarantinesAsCorruptWithoutReadingItIn()
    {
        // 手改/写坏的巨型档不得整份读进内存（OOM）；超上限按损坏隔离
        var store = new SaveStore();
        var path = PathFor("huge.json");
        File.WriteAllText(path, new string('x', (int)SaveStore.MaxSaveBytes + 1024));

        var result = store.Load(path);

        Assert.Equal(SaveLoadStatus.Corrupt, result.Status);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Save_RefusesOversizedPayload_SoWrittenFileIsAlwaysReadable()
    {
        // 写读上限对称：读侧把超限档当损坏隔离，写侧若照写不误，就会产出「自己刚写出、
        // 自己读不回」的档——下次开机遇 Corrupt 改名隔离，进度静默丢失。触发源真实存在：
        // TalentCache.RestoreValues 接受任意长度点值序列（手改档可塞几十万项），读得进、
        // 再落盘就写出巨型档。判据是「写出的档必须能被自己读回」。
        var store = new SaveStore();
        var path = PathFor("oversized.json");
        var big = new Dictionary<string, object?> { ["pad"] = new string('x', (int)SaveStore.MaxSaveBytes + 1024) };

        Assert.False(store.TrySave(path, big, out var error));
        Assert.NotNull(error);
        Assert.False(File.Exists(path)); // 拒绝即不落盘，不留半成品

        // 对照：正常体积的档写得出、读得回（判据不得把正常路径一起拒掉）
        Assert.True(store.TrySave(path, new Dictionary<string, object?> { ["v"] = 1L }, out _));
        Assert.Equal(SaveLoadStatus.Ok, store.Load(path).Status);
    }

    [Fact]
    public void Save_FirstOverwrite_KeepsBackupOfPreviousContent()
    {
        // 首次覆盖既有档前必须留 .bak：覆盖写坏/写一半时旧进度仍可取回。
        // 回退路径（平台不支持原子覆盖）也复用同一备份名，不得先删正本。
        var store = new SaveStore();
        var path = PathFor("backed.json");
        Assert.True(store.TrySave(path, new Dictionary<string, object?> { ["v"] = 1L }, out _));
        Assert.True(store.TrySave(path, new Dictionary<string, object?> { ["v"] = 2L }, out _));

        Assert.Equal(2L, store.Load(path).Tree!["v"]);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(1L, store.Load(path + ".bak").Tree!["v"]);
    }

    [Fact]
    public void Load_TombstoneEmptyObject_ParsesOkButCarriesNoRunData()
    {
        // 删档失败时上层把正本覆写成墓碑 {}，靠「可解析且含本局数据」判否——
        // 这里钉住墓碑的解析语义：Ok 但无 version / 无字段（HasRunSave 因此返回 false）
        var store = new SaveStore();
        var path = PathFor("tombstone.json");
        Assert.True(store.TrySave(path, new Dictionary<string, object?>(), out _));

        var result = store.Load(path);

        Assert.Equal(SaveLoadStatus.Ok, result.Status);
        Assert.NotNull(result.Tree);
        Assert.Empty(result.Tree!);
        Assert.Equal(0, RunFieldNormalize.ReadInt(result.Tree, "version", 0));
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
