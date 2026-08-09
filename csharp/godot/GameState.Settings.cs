using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：设置项（Ctrl/Shift/视角/窗口/瞄准/语言）。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 设置项（Ctrl/Shift 模式） ----------------

    /// <summary>Ctrl 微调模式：false=按住生效，true=按一下切换；持久化到 profile</summary>
    public void SetCtrlToggleMode(bool enabled)
    {
        CtrlToggleMode = enabled;
        SaveProfile();
    }

    /// <summary>Shift 加速模式：false=按住生效，true=按一下切换；持久化到 profile</summary>
    public void SetShiftToggleMode(bool enabled)
    {
        ShiftToggleMode = enabled;
        SaveProfile();
    }

    /// <summary>触屏虚拟控件开关（mobile touch）：持久化 + 广播（Main 联动 VirtualControls.set_enabled）</summary>
    public void SetTouchControls(bool enabled)
    {
        TouchControls = enabled;
        SaveProfile();
        EmitSignal(SignalName.TouchControlsChanged, enabled);
    }

    // ---------------- 视角缩放 ----------------

    /// <summary>视角档位表（设置页三选，profile 持久化；值为相机 zoom 倍率）。
    /// zoom&gt;1 时可见世界区域 = 视口 ÷ zoom（以相机位置为中心收窄），
    /// 所有"屏幕边缘/出屏"逻辑统一走 view_world_rect() 适配。</summary>
    public Godot.Collections.Dictionary VIEW_ZOOM_LEVELS { get; } = new()
    {
        [new StringName("small")] = 1.0,
        [new StringName("medium")] = 1.35,
        [new StringName("large")] = 1.7,
    };

    public Godot.Collections.Array<StringName> VIEW_ZOOM_ORDER { get; } = new()
    {
        new StringName("small"),
        new StringName("medium"),
        new StringName("large"),
    };

    /// <summary>main 场景相机注册表（main.gd 在 _ready/_exit_tree 维护），供可见区域计算</summary>
    public Camera2D? CameraRef
    {
        get => _registry.CameraRef;
        set
        {
            _registry.CameraRef = value;
            InvalidateViewRectCache();
        }
    }

    /// <summary>生效 zoom 倍率缓存（set_view_zoom/load_profile 同步；热路径免查表，须与 small 档一致）</summary>
    private double _viewZoomFactor = 1.0;

    /// <summary>view_world_rect 物理帧缓存：同帧多次调用免重复视口查询（子弹/敌机/玩家每帧共用一帧结果）。
    /// zoom 因子或相机注册变更时置 -1 强制重算；相机位置固定 (960,540)，帧内语义不变。</summary>
    private long _viewRectFrame = -1;

    private Rect2 _viewRectCached = new();

    /// <summary>切换视角档位（非法/同档忽略），持久化到 profile 并广播</summary>
    public void SetViewZoom(StringName level)
    {
        if (!VIEW_ZOOM_LEVELS.ContainsKey(level) || level == ViewZoom)
        {
            return;
        }

        ViewZoom = level;
        _viewZoomFactor = (double)VIEW_ZOOM_LEVELS[level].AsDouble();
        InvalidateViewRectCache();
        SaveProfile();
        EmitSignal(SignalName.ViewZoomChanged, _viewZoomFactor);
    }

    public double ViewZoomFactor() => _viewZoomFactor;

    public void SetViewZoomFactor(double factor)
    {
        _viewZoomFactor = factor;
        InvalidateViewRectCache();
    }

    // ---------------- 窗口大小 ----------------

    /// <summary>窗口尺寸档位表（设置页三选，profile 持久化；stretch 等比缩放，仅改窗口物理尺寸）。
    /// 非 const：Vector2i 构造为非常量表达式（同 spawner.ENEMY_TYPES 先例）。</summary>
    public Godot.Collections.Dictionary WINDOW_SIZE_LEVELS { get; set; } = new()
    {
        [new StringName("small")] = new Vector2I(1280, 720),
        [new StringName("medium")] = new Vector2I(1600, 900),
        [new StringName("large")] = new Vector2I(1920, 1080),
    };

    public Godot.Collections.Array<StringName> WINDOW_SIZE_ORDER { get; } = new()
    {
        new StringName("small"),
        new StringName("medium"),
        new StringName("large"),
    };

    /// <summary>切换窗口尺寸档位（非法/同档忽略）：立即应用窗口，持久化到 profile 并广播</summary>
    public void SetWindowSize(StringName level)
    {
        if (!WINDOW_SIZE_LEVELS.ContainsKey(level) || level == WindowSize)
        {
            return;
        }

        WindowSize = level;
        ApplyWindowSize();
        SaveProfile();
        EmitSignal(SignalName.WindowSizeChanged, WindowSize);
    }

    /// <summary>应用当前档位到窗口：仅窗口模式生效；headless 为 dummy 渲染直接跳过。
    /// 档位尺寸按逻辑点定义：高分屏（Retina 等 content scale&gt;1）乘屏幕缩放换算物理像素，
    /// 否则 1920×1080 档位在 2x 屏上只显示为 960×540 点的小窗；超出当前屏可用区域时等比收缩并居中。</summary>
    private void ApplyWindowSize()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        var win = GetWindow();
        if (win == null || win.Mode != Window.ModeEnum.Windowed)
        {
            return;
        }

        var screen = win.CurrentScreen;
        var scale = DisplayServer.ScreenGetScale(screen);
        var phys = (Vector2I)((Vector2)WINDOW_SIZE_LEVELS[WindowSize].AsVector2I() * scale);
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        if (phys.X > usable.Size.X || phys.Y > usable.Size.Y)
        {
            var fit = Mathf.Min((float)usable.Size.X / phys.X, (float)usable.Size.Y / phys.Y);
            phys = (Vector2I)((Vector2)phys * fit);
        }

        win.Size = phys;
        win.Position = usable.Position + (usable.Size - phys) / 2;
    }


    // ---------------- 瞄准辅助强度 ----------------

    /// <summary>强度档位表（设置页三选，profile 持久化；辅助瞄准常驻、刻意不提供关闭档）。
    /// 各档数值（辅助框内边距 frame_pad/追踪转向速率 homing_turn_rate）在 balance.json player.aim_assist.levels。</summary>
    public Godot.Collections.Array<StringName> AIM_ASSIST_ORDER { get; } = new()
    {
        new StringName("low"),
        new StringName("medium"),
        new StringName("high"),
    };

    /// <summary>切换瞄准辅助强度档位（非法/同档忽略），持久化到 profile 并广播</summary>
    public void SetAimAssistLevel(StringName level)
    {
        if (!AIM_ASSIST_ORDER.Contains(level) || level == AimAssistLevel)
        {
            return;
        }

        AimAssistLevel = level;
        SaveProfile();
        EmitSignal(SignalName.AimAssistChanged, level);
    }

    /// <summary>无障碍·减少闪光：开关持久化到 profile 并广播（Meta HUD 据此折算色差/禁脉冲）</summary>
    public void SetReduceFlash(bool enabled)
    {
        if (enabled == ReduceFlash)
        {
            return;
        }

        ReduceFlash = enabled;
        SaveProfile();
        EmitSignal(SignalName.ReduceFlashChanged, enabled);
    }

    /// <summary>鼠标锁定窗口内：开关持久化到 profile 并广播（MouseTrap 据此决定是否拉回出框鼠标）</summary>
    public void SetMouseLock(bool enabled)
    {
        if (enabled == MouseLock)
        {
            return;
        }

        MouseLock = enabled;
        SaveProfile();
        EmitSignal(SignalName.MouseLockChanged, enabled);
    }

    /// <summary>P0-1 手柄设置 setter：右摇杆瞄准灵敏度（200..4000 px/s）。
    /// K06：只更新内存 + 广播（灵敏度不影响 InputMap 死区）；持久化由设置页 drag_ended 统一
    /// 提交——原实现每步全量原子写盘，滑杆拖动（数十次 value_changed）放大为磁盘写风暴</summary>
    public void SetJoyAimSpeed(double value)
    {
        JoyAimSpeed = Mathf.Clamp(value, 200.0, 4000.0);
        EmitSignal(SignalName.JoySettingsChanged, JoyAimSpeed, JoyDeadzone);
    }

    /// <summary>P0-1 手柄设置 setter：摇杆死区（0.05..0.90，应用至全部手柄动作的 InputMap deadzone）。
    /// K06：立即应用死区（InputMap 全局生效，base_system_test 契约）+ 广播；不自动写盘</summary>
    public void SetJoyDeadzone(double value)
    {
        JoyDeadzone = Mathf.Clamp(value, 0.05, 0.9);
        foreach (var a in JOYPAD_ACTIONS)
        {
            if (InputMap.HasAction(a))
            {
                InputMap.ActionSetDeadzone(a, (float)JoyDeadzone);
            }
        }

        EmitSignal(SignalName.JoySettingsChanged, JoyAimSpeed, JoyDeadzone);
    }

    /// <summary>手柄设置持久化：设置页滑杆 drag_ended 调用一次（setter 不再自动写盘，防拖动写风暴）</summary>
    public void PersistJoySettings() => SaveProfile();

    /// <summary>当前可见世界区域（相机未注册时以 (960,540) 为心），margin 向外扩张。
    /// 屏幕边缘钳制 / 出屏销毁 / 刷怪位置统一以此为准；zoom=1 时即全屏 1920×1080。
    /// 物理帧内缓存（P0-1）：同一物理帧内多次调用（每弹/每敌/玩家/Boss）共享一次视口查询。</summary>
    public Rect2 ViewWorldRect(double margin = 0.0)
    {
        if (margin == 0.0)
        {
            return CachedViewRect();
        }

        return CachedViewRect().Grow((float)margin);
    }

    private void InvalidateViewRectCache() => _viewRectFrame = -1;

    private Rect2 CachedViewRect()
    {
        var frame = (long)Engine.GetPhysicsFrames();
        if (frame != _viewRectFrame)
        {
            _viewRectFrame = frame;
            var center = new Vector2(960.0f, 540.0f);
            if (CameraRef != null && GodotObject.IsInstanceValid(CameraRef))
            {
                center = CameraRef.GlobalPosition;
            }

            var size = new Vector2(1920.0f, 1080.0f);
            var viewport = GetViewport();
            if (viewport != null)
            {
                size = viewport.GetVisibleRect().Size;
            }

            size /= (float)_viewZoomFactor;
            _viewRectCached = new Rect2(center - size * 0.5f, size);
        }

        return _viewRectCached;
    }

    public void AddKill()
    {
        Kills += 1;
        SetKindProgress("kill", Kills);
    }

    public void AddBossKill(double scoreScale = 1.0)
    {
        BossKills += 1;
        // G012：加分基准入 balance.json（milestones.boss_kill_base；击杀低频，非热路径可直查）
        AddScore((int)(Cfg("milestones.boss_kill_base", 500.0).AsDouble() * scoreScale));
        AddRp(RpBossKillValue);
        SetKindProgress("boss", BossKills);
        if (RecomputeDifficultyInternal())
        {
            EmitSignal(SignalName.DifficultyChanged, DifficultyMultiplier);
        }
    }

    /// <summary>难度乘数对局进程曲线（2026-07-29 无限段修订，D1=必死曲线，docs/ENDLESS_BALANCE_PLAN.md）：
    /// 1 + per_boss_kill×Boss击杀 + 时间轴累进（每 time_step_seconds 量化一档，每 10 分钟 +per_ten_minutes）。
    /// 线性无封顶：敌方 HP/伤害 ramp 随之无限增长，最终超过玩家固定成长上限。
    /// 返回乘数是否变化；变化时由调用方广播 difficulty_changed（apply_run_save 统一在末尾广播）。</summary>
    private bool RecomputeDifficultyInternal()
    {
        var step = (int)Mathf.Floor(RunTime / _progTimeStepSeconds);
        // 2026-08-07：曲线公式迁移 InfiAir.Core.Progression.DifficultyCurve（C#，运算顺序逐位等价）
        var newMult = _progression.DifficultyMultiplier(RunTime, _progTimeStepSeconds, _progPerTenMinutes, _progPerBossKill, BossKills);
        _difficultyTimeStep = step;
        if (Mathf.IsEqualApprox(newMult, DifficultyMultiplier))
        {
            return false;
        }

        DifficultyMultiplier = newMult;
        return true;
    }

    /// <summary>生命上限：基础 100 + extra_life 每层 +50（对齐原作 EXTRA_LIFE_BONUS_HP）
    /// P0-2：基础值 _apply_balance 缓存，热路径免 cfg 路径解析（extra_life 层数查询 O(1)）</summary>
    public double MaxHealth() => _maxHpBase + _maxHpBonus * BuffCount("extra_life");

    public void LoseHealth(double amount = 1.0)
    {
        Health = Mathf.Max(Health - amount, 0.0);
        EmitSignal(SignalName.HealthChanged, Health);
        if (Health <= 0.0)
        {
            EmitSignal(SignalName.PlayerDied);
        }
    }

    /// <summary>治疗（单点封顶 max_health，调用侧不再各自判断）</summary>
    public void Heal(double amount)
    {
        Health = Mathf.Min(Health + amount, MaxHealth());
        EmitSignal(SignalName.HealthChanged, Health);
    }

    /// <summary>吸血 buff：击杀回复 int(上限 × 10%)（对齐原作 LIFESTEAL_FRACTION），每帧至多结算一次</summary>
    private long _lifestealFrame = -1;

    /// <summary>吸血比例缓存（P0-2 同款：_apply_balance 刷新，击杀帧免 cfg 路径解析）</summary>
    private double _lifestealFraction = 0.1;

    public void TryLifesteal()
    {
        if (BuffCount("lifesteal") <= 0)
        {
            return;
        }

        var frame = (long)Engine.GetPhysicsFrames();
        if (frame == _lifestealFrame)
        {
            return;
        }

        _lifestealFrame = frame;
        Heal(Mathf.Max(1, (int)(MaxHealth() * _lifestealFraction)));
    }

    public int BuffCount(StringName id) => (int)Buffs.GetValueOrDefault(id, 0).AsInt64();

    public void AddBuff(StringName id)
    {
        Buffs[id] = BuffCount(id) + 1;
        EmitSignal(SignalName.BuffsChanged);
    }

    /// <summary>消耗一层 buff（护盾等一次性层；无剩余层返回 false；层数变动广播 buffs_changed）</summary>
    public bool ConsumeBuff(StringName id)
    {
        if (BuffCount(id) <= 0)
        {
            return false;
        }

        Buffs[id] = BuffCount(id) - 1;
        EmitSignal(SignalName.BuffsChanged);
        return true;
    }


    // ---------------- 语言（中英双语） ----------------

    /// <summary>当前语言（"zh"/"en"，profile 持久化）。
    /// 存储承载于 snake 字段 locale（GDScript 直读写；桥，M7 过渡，删除前）——属性转发字段。</summary>
    public string Locale { get; set; } = "zh";

    public void SetLocale(string pLocale)
    {
        if (pLocale != "zh" && pLocale != "en")
        {
            return;
        }

        Locale = pLocale;
        TranslationServer.SetLocale(pLocale);
        SaveProfile();
        EmitSignal(SignalName.LocaleChanged);
    }
}
