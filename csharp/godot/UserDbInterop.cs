using Godot;

namespace InfiAir;

/// <summary>
/// P0-2 绑定壳：UserDB 数据层的 C# 桥（InfiAir.Core.Storage.UserDb 纯逻辑）。
/// GDScript 侧 scripts/user_db.gd 薄壳转发（公开 API 不变），user:// 路径经 GlobalizePath
/// 转 OS 路径；密码派生逐字节等价迁移（固定向量对照 tests-csharp/UserDbPasswordTests.cs）。
/// </summary>
public partial class UserDbInterop : RefCounted
{
    private readonly Core.Storage.UserDb _core;

    public UserDbInterop()
    {
        _core = new Core.Storage.UserDb(ProjectSettings.GlobalizePath("user://users.json"));
    }

    public bool CreateUser(string name, string password, long iterations)
    {
        return _core.CreateUser(name, password, iterations);
    }

    public bool VerifyUser(string name, string password, long fallbackIterations)
    {
        return _core.VerifyUser(name, password, fallbackIterations);
    }

    public bool UserExists(string name)
    {
        return _core.UserExists(name);
    }

    /// <summary>users.json 损坏标志转发（2026-08-10；见 Core.UserDb.LastWasCorrupt）。</summary>
    public bool LastWasCorrupt
    {
        get => _core.LastWasCorrupt;
    }

    public Godot.Collections.Array ListUsernames()
    {
        var arr = new Godot.Collections.Array();
        foreach (var name in _core.ListUsernames())
        {
            arr.Add(name);
        }

        return arr;
    }

    public string GetLastLoginUser()
    {
        return _core.GetLastLoginUser();
    }

    public void RecordLogin(string name)
    {
        _core.RecordLogin(name);
    }

    public Godot.Collections.Dictionary GetUserData(string name)
    {
        return VariantBridge.ToVariant(_core.GetUserData(name)).AsGodotDictionary();
    }

    public void UpdateUserData(string name, Godot.Collections.Dictionary data)
    {
        if (VariantBridge.TryToClr(data, out var clr, out _) && clr is Dictionary<string, object?> d)
        {
            _core.UpdateUserData(name, d);
        }
    }

    public void UpdateHighScore(string name, long score)
    {
        _core.UpdateHighScore(name, score);
    }

    public Godot.Collections.Dictionary GetUserSettings(string name)
    {
        return VariantBridge.ToVariant(_core.GetUserSettings(name)).AsGodotDictionary();
    }

    public void UpdateUserSettings(string name, Godot.Collections.Dictionary settings)
    {
        if (VariantBridge.TryToClr(settings, out var clr, out _) && clr is Dictionary<string, object?> d)
        {
            _core.UpdateUserSettings(name, d);
        }
    }

    public Godot.Collections.Dictionary GetUserMeta(string name)
    {
        return VariantBridge.ToVariant(_core.GetUserMeta(name)).AsGodotDictionary();
    }

    public void UpdateUserMeta(string name, Godot.Collections.Dictionary meta)
    {
        if (VariantBridge.TryToClr(meta, out var clr, out _) && clr is Dictionary<string, object?> d)
        {
            _core.UpdateUserMeta(name, d);
        }
    }

    /// <summary>删除用户（先验密）；每用户存档文件 + .corrupt 连带清理。</summary>
    public bool DeleteUser(string name, string password)
    {
        var saveFile = ProjectSettings.GlobalizePath("user://" + _core.SaveFileName(name));
        return _core.DeleteUser(name, password, saveFile);
    }

    /// <summary>每用户存档文件名（不含 user:// 前缀；GDScript 壳补前缀返回）。</summary>
    public string SaveFileName(string name)
    {
        return _core.SaveFileName(name);
    }

    public long SubmitScore(string name, long score)
    {
        return _core.SubmitScore(name, score);
    }

    public Godot.Collections.Array GetLeaderboard()
    {
        var arr = new Godot.Collections.Array();
        foreach (var item in _core.GetLeaderboard())
        {
            arr.Add(VariantBridge.ToVariant(item));
        }

        return arr;
    }

    public void Reload()
    {
        _core.Reload();
    }
}
