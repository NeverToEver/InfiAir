using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace InfiAir.Core.Storage;

/// <summary>
/// 本地用户数据库核心（P0-2，2026-08-07 落地）：用户 CRUD / 登录记录 / 本地排行榜 /
/// 名称校验 / 密码派生。逐条镜像原 GDScript UserDB（scripts/user_db.gd，2026-08-04 账户系统，
/// 规格 docs/archive/2026-08-04-local-accounts-plan.md + PORTING_PARITY 附录 B）；
/// 纯 .NET、零 Godot 依赖，xUnit 直测。持久化经 <see cref="SaveStore"/>（原子写 + 损坏隔离）。
///
/// 密码派生为「逐字节等价迁移」：原实现为自建 PBKDF2 变体（盐||INT32_BE(块号) 拼接进异或链、
/// 仅前 20 字节参与异或、输出截 32 字节）——非标准 PBKDF2-HMAC-SHA256，不与标准工具互通；
/// 固定向量对照见 tests-csharp/UserDbPasswordTests.cs（迁移前 GDScript 实测生成）。
/// 防御性收敛（仅手改/非 JSON 数据，原实现会崩溃处现为安全回退）：非法 hex → 空盐/空密文
/// （验密必然失败）、非数字数值字段 → 0、非字符串字段 → 空串；空密码派生在原 GDScript 侧
/// 越界崩溃（生产不可达——长度校验 ≥3），此处正常计算（行为严格更强）。
/// </summary>
public sealed class UserDb
{
    public const int NameMin = 3;
    public const int NameMax = 16;
    public const int PasswordMin = 3;
    public const int PasswordMax = 16;
    public const int LeaderboardCap = 10;
    public const int PlayerNameMax = 32;
    public const long Pbkdf2Iterations = 50_000;
    /// <summary>V 系列：迭代数防御性上限（正常 50k 的 20 倍）——手改 users.json iterations 为巨值
    /// 会单线程挂死登录（10^11 次 HMAC），钳制后派生必然失败 → 登录拒绝而非冻结。</summary>
    public const long MaxPbkdf2Iterations = 1_000_000;
    public static readonly string[] ReservedNames = ["_leaderboard", "Guest"];

    private readonly SaveStore _store = new();
    private readonly string _path;
    private Dictionary<string, object?> _db = new();
    private bool _loaded;

    /// <summary>上次 EnsureLoaded 是否检测到 users.json 损坏并被隔离备份（2026-08-10 健壮性审查）：
    /// 对齐 GameState.SaveCorrupt/ProfileCorrupt 口径——损坏静默重建空用户表会让玩家误以为账号
    /// 丢失（.corrupt 备份实际还在），供欢迎页提示。</summary>
    public bool LastWasCorrupt { get; private set; }

    public UserDb(string path)
    {
        _path = path;
    }

    /// <summary>强制重新加载磁盘状态（对齐 GDScript reload：测试 wipe 后刷新「空用户表」起点）。</summary>
    public void Reload()
    {
        _loaded = false;
        _db = new Dictionary<string, object?>();
        EnsureLoaded();
    }

    /// <summary>注册：拒绝保留名（B7-7）与长度不合规；成功写入并落盘。</summary>
    public bool CreateUser(string name, string password, long iterations)
    {
        if (!ValidName(name) || !ValidPassword(password))
        {
            return false;
        }

        EnsureLoaded();
        var users = Users;
        if (users.ContainsKey(name) || ReservedNames.Contains(name))
        {
            return false;
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        users[name] = new Dictionary<string, object?>
        {
            ["password"] = HexEncode(Derive(password, salt, iterations)),
            ["salt"] = HexEncode(salt),
            ["iterations"] = iterations,
            ["high_score"] = 0L,
            ["total_kills"] = 0L,
            ["games_played"] = 0L,
            ["last_login_order"] = 0L,
            ["settings"] = new Dictionary<string, object?>(),
            ["meta"] = new Dictionary<string, object?>
            {
                ["tech_points"] = 0L,
                ["upgrades"] = new Dictionary<string, object?>(),
            },
        };
        if (!Save())
        {
            // 2026-08-10 健壮性审查：落盘失败回滚内存条目——否则玩家重试注册被
            // ContainsKey 拒绝（提示已存在）而磁盘无此账号，重启后状态才恢复
            users.Remove(name);
            return false;
        }

        return true;
    }

    public bool VerifyUser(string name, string password, long fallbackIterations)
    {
        EnsureLoaded();
        if (!Users.ContainsKey(name))
        {
            return false;
        }

        var rec = UserRecord(name);
        if (rec.Count == 0)
        {
            return false; // 条目非 Dictionary（手改）：按用户不存在处理
        }

        var salt = HexDecode(ToStringSafe(rec.GetValueOrDefault("salt", "")));
        var iter = Math.Clamp(ToInt64(rec.GetValueOrDefault("iterations", fallbackIterations)), 1, MaxPbkdf2Iterations);
        var derived = Derive(password, salt, iter);
        // 常量时间比对（B4）：不区分「用户不存在」与「密码错误」，此处统一 false
        var stored = HexDecode(ToStringSafe(rec.GetValueOrDefault("password", "")));
        return CryptographicOperations.FixedTimeEquals(derived, stored);
    }

    public bool UserExists(string name)
    {
        EnsureLoaded();
        return Users.ContainsKey(name);
    }

    /// <summary>用户名列表：last_login_order 降序 + 名字典序（B4 last_login 为自增序号非时间戳）。</summary>
    public List<string> ListUsernames()
    {
        EnsureLoaded();
        var names = Users.Keys.ToList();
        names.Sort((a, b) =>
        {
            var oa = ToInt64(UserRecord(a).GetValueOrDefault("last_login_order", 0L));
            var ob = ToInt64(UserRecord(b).GetValueOrDefault("last_login_order", 0L));
            if (oa != ob)
            {
                return oa > ob ? -1 : 1;
            }

            return string.CompareOrdinal(a, b);
        });
        return names;
    }

    public string GetLastLoginUser()
    {
        var names = ListUsernames();
        return names.Count > 0 ? names[0] : "";
    }

    public void RecordLogin(string name)
    {
        EnsureLoaded();
        if (!Users.ContainsKey(name))
        {
            return;
        }

        var rec = UserRecord(name);
        if (rec.Count == 0)
        {
            return; // 条目非 Dictionary（手改）：跳过
        }

        long maxOrder = 0;
        foreach (var n in Users.Keys)
        {
            maxOrder = Math.Max(maxOrder, ToInt64(UserRecord(n).GetValueOrDefault("last_login_order", 0L)));
        }

        rec["last_login_order"] = maxOrder + 1;
        Save();
    }

    /// <summary>用户记录浅拷贝（对齐 GDScript duplicate()）。</summary>
    public Dictionary<string, object?> GetUserData(string name)
    {
        EnsureLoaded();
        return new Dictionary<string, object?>(UserRecord(name));
    }

    /// <summary>通用字段合并更新（统计类）；密码/盐/迭代数不可经此覆盖。</summary>
    public void UpdateUserData(string name, Dictionary<string, object?> data)
    {
        EnsureLoaded();
        if (!Users.ContainsKey(name))
        {
            return;
        }

        var rec = UserRecord(name);
        if (rec.Count == 0)
        {
            return;
        }

        foreach (var kv in data)
        {
            if (kv.Key is "password" or "salt" or "iterations")
            {
                continue;
            }

            rec[kv.Key] = kv.Value;
        }

        Save();
    }

    /// <summary>仅更高才写（B4 语义）；分数负钳 0。</summary>
    public void UpdateHighScore(string name, long score)
    {
        EnsureLoaded();
        if (!Users.ContainsKey(name))
        {
            return;
        }

        var rec = UserRecord(name);
        if (rec.Count == 0)
        {
            return;
        }

        var clamped = Math.Max(score, 0);
        if (clamped > ToInt64(rec.GetValueOrDefault("high_score", 0L)))
        {
            rec["high_score"] = clamped;
            Save();
        }
    }

    /// <summary>用户设置浅拷贝（手改非字典 settings 按空表处理——原实现 duplicate() 会崩溃）。</summary>
    public Dictionary<string, object?> GetUserSettings(string name)
    {
        EnsureLoaded();
        var rec = UserRecord(name);
        return rec.GetValueOrDefault("settings") is Dictionary<string, object?> settings
            ? new Dictionary<string, object?>(settings)
            : new Dictionary<string, object?>();
    }

    /// <summary>局外成长档案读取（2026-08-09 计划 M2；Q17 同款条目级守卫）：
    /// meta 缺失/非 Dictionary/用户不存在 → 默认空档案 { tech_points: 0, upgrades: {} }；
    /// 顶层浅拷贝（对齐 GetUserData duplicate 语义）。</summary>
    public Dictionary<string, object?> GetUserMeta(string name)
    {
        EnsureLoaded();
        var rec = UserRecord(name);
        if (rec.Count == 0)
        {
            return DefaultMeta();
        }

        return rec.GetValueOrDefault("meta") is Dictionary<string, object?> meta
            ? new Dictionary<string, object?>(meta)
            : DefaultMeta();
    }

    /// <summary>局外成长档案合并更新（对齐 UpdateUserData 语义，顶层浅合并）。
    /// 用户不存在 / 记录非法跳过；meta 非 Dictionary（手改）时按空表重建——不丢已有字段。</summary>
    public void UpdateUserMeta(string name, Dictionary<string, object?> meta)
    {
        EnsureLoaded();
        if (!Users.ContainsKey(name))
        {
            return;
        }

        var rec = UserRecord(name);
        if (rec.Count == 0)
        {
            return;
        }

        var merged = rec.GetValueOrDefault("meta") is Dictionary<string, object?> cur
            ? new Dictionary<string, object?>(cur)
            : new Dictionary<string, object?>();
        foreach (var kv in meta)
        {
            merged[kv.Key] = kv.Value;
        }

        rec["meta"] = merged;
        Save();
    }

    public void UpdateUserSettings(string name, Dictionary<string, object?> settings)
    {
        EnsureLoaded();
        if (!Users.ContainsKey(name))
        {
            return;
        }

        var rec = UserRecord(name);
        if (rec.Count == 0)
        {
            return;
        }

        var merged = rec.GetValueOrDefault("settings") is Dictionary<string, object?> cur
            ? new Dictionary<string, object?>(cur)
            : new Dictionary<string, object?>();
        foreach (var kv in settings)
        {
            merged[kv.Key] = kv.Value;
        }

        rec["settings"] = merged;
        Save();
    }

    /// <summary>删除用户（先验密）；连带清理该用户存档文件与损坏备份（B7-12 + 2026-08-06 审计口径）。</summary>
    public bool DeleteUser(string name, string password, string saveFilePath)
    {
        if (!VerifyUser(name, password, Pbkdf2Iterations))
        {
            return false;
        }

        var removed = Users[name];
        Users.Remove(name);
        _store.Delete(saveFilePath);
        _store.Delete(saveFilePath + ".corrupt");
        if (!Save())
        {
            // 2026-08-10 健壮性审查：落盘失败回滚内存条目——否则磁盘 users.json 仍有该账号
            // 而内存已删，重启前 UserExists/登录与磁盘状态不一致（对称 CreateUser 回滚口径）
            Users[name] = removed;
            return false;
        }

        return true;
    }

    /// <summary>每用户存档文件名：savegame_&lt;sanitized&gt;_&lt;sha256[:12]&gt;.json（B5；不含 user:// 前缀）。</summary>
    public string SaveFileName(string name)
    {
        var sb = new StringBuilder();
        foreach (var rune in name.ToLowerInvariant().EnumerateRunes())
        {
            var v = rune.Value;
            if ((v >= 'a' && v <= 'z') || (v >= '0' && v <= '9'))
            {
                sb.Append((char)v);
            }
        }

        var sanitized = sb.Length == 0 ? "user" : sb.ToString();
        var digest = HexEncode(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12];
        return $"savegame_{sanitized}_{digest}.json";
    }

    /// <summary>提交成绩：score 负钳 0（≤0 不入榜）；cap 10；排序 score 降序 + seq 升序（先到先得）；
    /// 返回 1-indexed 名次，0 = 未上榜（含落盘失败）。</summary>
    public long SubmitScore(string name, long score)
    {
        if (score <= 0)
        {
            return 0;
        }

        EnsureLoaded();
        var board = Leaderboard;
        var seq = ToInt64(_db.GetValueOrDefault("_seq", 0L)) + 1;
        _db["_seq"] = seq;
        var entry = new Dictionary<string, object?>
        {
            ["player_name"] = TruncateRunes(name, PlayerNameMax),
            ["score"] = Math.Max(score, 0),
            ["seq"] = seq,
            ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        };
        board.Add(entry);
        SortBoard(board);
        if (board.Count > LeaderboardCap)
        {
            board.RemoveRange(LeaderboardCap, board.Count - LeaderboardCap);
        }

        long rank = 0;
        for (var i = 0; i < board.Count; i++)
        {
            if (ReferenceEquals(board[i], entry))
            {
                rank = i + 1;
                break;
            }
        }

        return Save() ? rank : 0;
    }

    /// <summary>榜单读取（Q20 判型：非 Dictionary 条目/非数字 score 跳过），返回过滤后排序副本。</summary>
    public List<object?> GetLeaderboard()
    {
        EnsureLoaded();
        var board = new List<object?>();
        foreach (var item in Leaderboard)
        {
            if (item is Dictionary<string, object?> entry && entry.GetValueOrDefault("score") is long or double)
            {
                board.Add(item);
            }
        }

        SortBoard(board);
        return board;
    }

    /// <summary>自建 PBKDF2 变体（逐字节等价端口；勿当标准 PBKDF2-HMAC-SHA256 使用）：
    /// 块 = 盐 || INT32_BE(块号)（20 字节），T = 首块 ^ U1 ^ U2 …（仅前 20 字节参与异或），
    /// 输出 = 各块前段拼接后截 32 字节。固定向量对照 tests-csharp/UserDbPasswordTests.cs。</summary>
    public static byte[] Derive(string password, byte[] salt, long iterations)
    {
        var key = Encoding.UTF8.GetBytes(password);
        using var hmac = new HMACSHA256(key);
        var block = new List<byte>();
        var blockIndex = 1;
        while (block.Count < 32)
        {
            var u = new byte[salt.Length + 4];
            Array.Copy(salt, u, salt.Length);
            u[salt.Length] = (byte)((blockIndex >> 24) & 0xFF);
            u[salt.Length + 1] = (byte)((blockIndex >> 16) & 0xFF);
            u[salt.Length + 2] = (byte)((blockIndex >> 8) & 0xFF);
            u[salt.Length + 3] = (byte)(blockIndex & 0xFF);
            var t = (byte[])u.Clone();
            for (var n = 0; n < iterations; n++)
            {
                u = hmac.ComputeHash(u);
                // U15：异或循环越界钳制——盐 >28 字节（合法 hex、手改记录）时 u 短于 t，
                // 原实现越界崩溃，与头注"防御性收敛…安全回退"口径不符
                var bound = Math.Min(t.Length, u.Length);
                for (var j = 0; j < bound; j++)
                {
                    t[j] ^= u[j];
                }
            }

            block.AddRange(t);
            blockIndex++;
        }

        return block.Take(32).ToArray();
    }

    private static bool ValidName(string name)
    {
        var len = name.EnumerateRunes().Count();
        return len >= NameMin && len <= NameMax;
    }

    private static bool ValidPassword(string password)
    {
        var len = password.EnumerateRunes().Count();
        return len >= PasswordMin && len <= PasswordMax;
    }

    /// <summary>Q17 结构守卫：_users 非 Dictionary → 空库重建；_leaderboard 非 Array → 补空。</summary>
    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        LastWasCorrupt = false;
        var res = _store.Load(_path);
        LastWasCorrupt = res.Status == SaveLoadStatus.Corrupt;
        _db = res.Status == SaveLoadStatus.Ok && res.Tree is not null
            ? res.Tree
            : new Dictionary<string, object?>();
        if (_db.GetValueOrDefault("_users") is not Dictionary<string, object?>)
        {
            _db = new Dictionary<string, object?>
            {
                ["_users"] = new Dictionary<string, object?>(),
                ["_leaderboard"] = new List<object?>(),
            };
        }
        else if (_db.GetValueOrDefault("_leaderboard") is not List<object?>)
        {
            _db["_leaderboard"] = new List<object?>();
        }
    }

    private Dictionary<string, object?> Users => (Dictionary<string, object?>)_db["_users"]!;

    private List<object?> Leaderboard => (List<object?>)_db["_leaderboard"]!;

    /// <summary>用户记录安全读取（Q17 条目级守卫：非 Dictionary 条目回空字典）。</summary>
    private Dictionary<string, object?> UserRecord(string name)
    {
        return Users.TryGetValue(name, out var rec) && rec is Dictionary<string, object?> d
            ? d
            : new Dictionary<string, object?>();
    }

    /// <summary>局外成长默认档案（每次调用返回新实例——调用方可能修改返回值）。</summary>
    private static Dictionary<string, object?> DefaultMeta() => new()
    {
        ["tech_points"] = 0L,
        ["upgrades"] = new Dictionary<string, object?>(),
    };

    private bool Save()
    {
        return _store.TrySave(_path, _db, out _);
    }

    /// <summary>GDScript int() 语义（JSON 域内；字符串数字前缀可解析，非法回 0——原 int() 对
    /// Array 等崩溃处防御性回退）。U15：对齐 GDScript 前缀解析——"7.5"→7、"0x10"→16、
    /// " 7 "→7（原 long.TryParse(Integer) 仅认十进制整数形式，三者均误回 0）。</summary>
    private static long ToInt64(object? v)
    {
        return v switch
        {
            long l => l,
            double d => unchecked((long)d),
            bool b => b ? 1 : 0,
            string s => ParseGdscriptInt(s),
            null => 0,
            _ => 0,
        };
    }

    /// <summary>GDScript int(String) 前缀解析：可选符号 + 十进制数字前缀（"7.5"→7），
    /// 0x/0X 十六进制前缀（"0x10"→16）；无可解析数字回 0。</summary>
    private static long ParseGdscriptInt(string s)
    {
        s = s.Trim();
        if (s.Length == 0)
        {
            return 0;
        }

        var neg = false;
        var i = 0;
        if (s[i] == '-' || s[i] == '+')
        {
            neg = s[i] == '-';
            i++;
        }

        if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X'))
        {
            var hex = s[(i + 2)..];
            var hexEnd = 0;
            while (hexEnd < hex.Length && Uri.IsHexDigit(hex[hexEnd]))
            {
                hexEnd++;
            }

            if (hexEnd == 0)
            {
                return 0;
            }

            // V 系列：超长十六进制溢出抛 OverflowException 违背「防御性收敛」头注契约（U15 引入回归）——
            // 手改 users.json 数值字段为超长数字须静默回 0（原 TryParse 语义），不中断登录/榜单流程。
            if (!long.TryParse(hex[..hexEnd], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hval))
            {
                return 0;
            }

            return neg ? -hval : hval;
        }

        var start = i;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            i++;
        }

        if (i == start)
        {
            return 0;
        }

        // V 系列：同十六进制路径——十进制超长数字溢出回 0（TryParse 语义，不中断流程）
        if (!long.TryParse(s[start..i], NumberStyles.None, CultureInfo.InvariantCulture, out var val))
        {
            return 0;
        }

        return neg ? -val : val;
    }

    /// <summary>GDScript String() 语义（仅接受字符串，其余按空串——手改非字符串字段验密必然失败）。</summary>
    private static string ToStringSafe(object? v)
    {
        return v as string ?? "";
    }

    private static string HexEncode(byte[] bytes)
    {
        // 自实现小写 hex（对齐 GDScript _hex_encode；Convert.ToHexString 大写为 .NET 9 API，目标 .NET 8）
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append("0123456789abcdef"[b >> 4]);
            sb.Append("0123456789abcdef"[b & 0xF]);
        }

        return sb.ToString();
    }

    /// <summary>hex 解码（对齐原实现：奇数长度 / 非法字符（含大写——find 白名单仅小写）→ 空数组，
    /// 验密必然失败，杜绝越界与异常值）。</summary>
    private static byte[] HexDecode(string hex)
    {
        if (hex.Length % 2 != 0)
        {
            return [];
        }

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var hi = HexVal(hex[i * 2]);
            var lo = HexVal(hex[i * 2 + 1]);
            if (hi < 0 || lo < 0)
            {
                return [];
            }

            bytes[i] = (byte)((hi << 4) | lo);
        }

        return bytes;
    }

    private static int HexVal(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => -1,
        };
    }

    /// <summary>按 code point 截断（对齐 GDScript substr(0, max)，CJK/代理对安全）。</summary>
    private static string TruncateRunes(string s, int max)
    {
        var sb = new StringBuilder();
        var count = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            if (count >= max)
            {
                break;
            }

            sb.Append(rune.ToString());
            count++;
        }

        return sb.ToString();
    }

    /// <summary>排序：score 降序 + seq 升序（Q20 兜底：非 Dictionary 对按相等处理，不抛类型错误）。</summary>
    private static void SortBoard(List<object?> board)
    {
        board.Sort((a, b) =>
        {
            if (a is not Dictionary<string, object?> da || b is not Dictionary<string, object?> db)
            {
                return 0;
            }

            var sa = ToInt64(da.GetValueOrDefault("score", 0L));
            var sb = ToInt64(db.GetValueOrDefault("score", 0L));
            if (sa != sb)
            {
                return sa > sb ? -1 : 1;
            }

            var seqA = ToInt64(da.GetValueOrDefault("seq", 0L));
            var seqB = ToInt64(db.GetValueOrDefault("seq", 0L));
            if (seqA != seqB)
            {
                return seqA < seqB ? -1 : 1;
            }

            return 0;
        });
    }
}
