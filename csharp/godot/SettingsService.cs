using Godot;

namespace InfiAir;

/// <summary>
/// 设置+视图域服务（第六轮拆域收官，2026-08-12）：原 GameState.Settings.cs 设置/视图域——职责 A 设置
/// setter 簇（SetCtrlToggleMode/SetShiftToggleMode/SetTouchControls/SetViewZoom/SetWindowSize/
/// SetAimAssistLevel/SetReduceFlash/SetMouseLock/SetJoyAimSpeed/SetJoyDeadzone/SetLocale/
/// PersistJoySettings）与职责 B 视图簇（CameraRef/ViewWorldRect/CachedViewRect 物理帧缓存/
/// InvalidateViewRectCache/VIEW_ZOOM_LEVELS/WINDOW_SIZE_LEVELS/AIM_ASSIST_ORDER）及状态字段
/// （CtrlToggleMode/ShiftToggleMode/TouchControls/ViewZoom/WindowSize/AimAssistLevel/ReduceFlash/
/// MouseLock/Locale/JoyAimSpeed/JoyDeadzone/MetaFxLod）迁入本服务；持久化桥 ApplySettingsDict/
/// CollectSettingsDict 自 GameState.Save.cs 随迁（设置域持久化，SaveProfile 留在 GameState 侧）。
/// Godot 绑定层：跨域访问统一经 GameState.Instance——键位/难度域（KeyBindings/TutorialDone/
/// Difficulty/DIFFICULTY_DEFS）与 SaveProfile/RefreshRegenCache/Cfg/SaveBool/JOYPAD_ACTIONS 及
/// GetViewport/GetWindow 均经 Instance 门面访问（跨域键不迁入，保持单一事实源）；_registry
/// （EntityManager）经构造注入（CameraRef 转发，与 MetaService 构造注入 UserDB/RunProgressionService
/// 注入 BalanceService 同构）。
/// 门面转发先例：与 MetaService/MissionsService/ScoreService/RunProgressionService/CombatStateService
/// 同构——GameState 组合持有本服务，GameState.Settings.cs/State.cs 为门面对齐转发（签名/语义不变），
/// 保持唯一 autoload：GameState 约定。信号：本服务以 C# 事件 TouchControlsChanged/ViewZoomChanged/
/// WindowSizeChanged/AimAssistChanged/ReduceFlashChanged/MouseLockChanged/JoySettingsChanged/
/// LocaleChanged 通知；GameState 订阅后转发为同名信号（发射点/次数/顺序与拆域前逐位一致——
/// ApplyRunSave 的 TouchControlsChanged 直发路径在 GameState 侧直发同名信号，不经本事件，无双发）。
/// </summary>
public sealed partial class SettingsService : RefCounted
{

    /// <summary>组合注入：GameState 持有的实体注册表（CameraRef 转发；
    /// GameState.cs 构造器传入，与 MetaService 构造注入 UserDB 同构）。</summary>
    private readonly EntityManager _registry;

    public SettingsService(EntityManager registry)
    {
        _registry = registry;
    }

    // ---------------- 状态字段（2026-08-12 自 GameState.State.cs/Settings.cs 迁入） ----------------

    /// <summary>设置项：Ctrl 微调 / Shift 加速的模式（false=按住，true=切换；player.gd 侧接入由集成阶段完成）</summary>
    public bool CtrlToggleMode { get; set; } = false;

    public bool ShiftToggleMode { get; set; } = false;

    /// <summary>触屏虚拟控件开关（profile 持久化，默认关；Main 挂载 VirtualControls 联动）</summary>
    public bool TouchControls { get; set; } = false;

    /// <summary>视角档位（profile 持久化，默认 small=原始视角；相机 zoom = VIEW_ZOOM_LEVELS[view_zoom]）</summary>
    public StringName ViewZoom { get; set; } = new StringName("small");

    /// <summary>窗口尺寸档位（profile 持久化，默认 large=1920×1080；尺寸表见 WINDOW_SIZE_LEVELS）</summary>
    public StringName WindowSize { get; set; } = new StringName("large");

    /// <summary>瞄准辅助强度档位（profile 持久化，默认 medium；常驻不可关，无 off 档；数值见 AIM_ASSIST_ORDER 注释）</summary>
    public StringName AimAssistLevel { get; set; } = new StringName("medium");

    /// <summary>Meta HUD 当前 LOD（由 MetaHealthFX._ready 从 effects.meta_health.lod 写入；0=MetaFX 接管
    /// 低血晕影，hud 旧晕影恒 0；非 0=回退路径，hud 保留低血脉动。MetaFX 离场时置 1）</summary>
    public int MetaFxLod { get; set; } = 1;

    /// <summary>无障碍：减少闪光（profile 持久化；开启后色差 ×0.4、禁呼吸/抖动/心跳视觉脉冲，音效保留）</summary>
    public bool ReduceFlash { get; set; } = false;

    /// <summary>鼠标锁定窗口内（profile 持久化，默认开启；开启后窗口聚焦期间鼠标移出内容区即被拉回，
    /// 防止准星跟随鼠标出框后位置冻结/跳变；窗口失焦自动放行，不阻碍切换应用）</summary>
    public bool MouseLock { get; set; } = true;

    /// <summary>P0-1 手柄设置：右摇杆瞄准灵敏度 px/s（默认取 balance player.aim_assist.joy_speed）与摇杆死区。
    /// 存储承载于 snake 字段（GDScript 直读写；桥，M7 过渡，删除前）——属性转发字段。</summary>
    public double JoyAimSpeed { get; set; } = 1400.0;

    public double JoyDeadzone { get; set; } = 0.5;

    /// <summary>当前语言（"zh"/"en"，profile 持久化）。
    /// 存储承载于 snake 字段 locale（GDScript 直读写；桥，M7 过渡，删除前）——属性转发字段。</summary>
    public string Locale { get; set; } = "zh";

    // ---------------- 信号 C# 事件（2026-08-12 自 GameState.Settings.cs EmitSignal 迁入） ----------------

    /// <summary>触屏虚拟控件开关变化；GameState 订阅后转发为 TouchControlsChanged 信号
    /// （ApplyRunSave 的直发路径在 GameState 侧直发同名信号，不经本事件——无双发）。</summary>
    public event Action<bool>? TouchControlsChanged;

    /// <summary>视角档位变化（参数为生效 zoom 倍率）；GameState 订阅后转发为 ViewZoomChanged 信号。</summary>
    public event Action<double>? ViewZoomChanged;

    /// <summary>窗口尺寸档位变化；GameState 订阅后转发为 WindowSizeChanged 信号。</summary>
    public event Action<StringName>? WindowSizeChanged;

    /// <summary>瞄准辅助强度档位变化；GameState 订阅后转发为 AimAssistChanged 信号。</summary>
    public event Action<StringName>? AimAssistChanged;

    /// <summary>减少闪光开关变化；GameState 订阅后转发为 ReduceFlashChanged 信号。</summary>
    public event Action<bool>? ReduceFlashChanged;

    /// <summary>鼠标锁定开关变化；GameState 订阅后转发为 MouseLockChanged 信号。</summary>
    public event Action<bool>? MouseLockChanged;

    /// <summary>P0-1 手柄设置：右摇杆瞄准灵敏度 + 摇杆死区变更；GameState 订阅后转发为 JoySettingsChanged 信号。</summary>
    public event Action<double, double>? JoySettingsChanged;

    /// <summary>语言变化；GameState 订阅后转发为 LocaleChanged 信号。</summary>
    public event Action? LocaleChanged;

    // ---------------- 设置项（Ctrl/Shift 模式，2026-08-12 自 GameState.Settings.cs 迁入） ----------------

    /// <summary>Ctrl 微调模式：false=按住生效，true=按一下切换；持久化到 profile</summary>
    public void SetCtrlToggleMode(bool enabled)
    {
        CtrlToggleMode = enabled;
        GameState.Instance.SaveProfile();
    }

    /// <summary>Shift 加速模式：false=按住生效，true=按一下切换；持久化到 profile</summary>
    public void SetShiftToggleMode(bool enabled)
    {
        ShiftToggleMode = enabled;
        GameState.Instance.SaveProfile();
    }

    /// <summary>触屏虚拟控件开关（mobile touch）：持久化 + 广播（Main 联动 VirtualControls.set_enabled）</summary>
    public void SetTouchControls(bool enabled)
    {
        TouchControls = enabled;
        GameState.Instance.SaveProfile();
        TouchControlsChanged?.Invoke(enabled);
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
        GameState.Instance.SaveProfile();
        ViewZoomChanged?.Invoke(_viewZoomFactor);
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
        GameState.Instance.SaveProfile();
        WindowSizeChanged?.Invoke(WindowSize);
    }

    /// <summary>应用当前档位到窗口：仅窗口模式生效；headless 为 dummy 渲染直接跳过。
    /// 档位尺寸按逻辑点定义：高分屏（Retina 等 content scale&gt;1）乘屏幕缩放换算物理像素，
    /// 否则 1920×1080 档位在 2x 屏上只显示为 960×540 点的小窗；超出当前屏可用区域时等比收缩并居中。</summary>
    public void ApplyWindowSize()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        var win = GameState.Instance.GetWindow();
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
        GameState.Instance.SaveProfile();
        AimAssistChanged?.Invoke(level);
    }

    /// <summary>无障碍·减少闪光：开关持久化到 profile 并广播（Meta HUD 据此折算色差/禁脉冲）</summary>
    public void SetReduceFlash(bool enabled)
    {
        if (enabled == ReduceFlash)
        {
            return;
        }

        ReduceFlash = enabled;
        GameState.Instance.SaveProfile();
        ReduceFlashChanged?.Invoke(enabled);
    }

    /// <summary>鼠标锁定窗口内：开关持久化到 profile 并广播（MouseTrap 据此决定是否拉回出框鼠标）</summary>
    public void SetMouseLock(bool enabled)
    {
        if (enabled == MouseLock)
        {
            return;
        }

        MouseLock = enabled;
        GameState.Instance.SaveProfile();
        MouseLockChanged?.Invoke(enabled);
    }

    /// <summary>P0-1 手柄设置 setter：右摇杆瞄准灵敏度（200..4000 px/s）。
    /// K06：只更新内存 + 广播（灵敏度不影响 InputMap 死区）；持久化由设置页 drag_ended 统一
    /// 提交——原实现每步全量原子写盘，滑杆拖动（数十次 value_changed）放大为磁盘写风暴</summary>
    public void SetJoyAimSpeed(double value)
    {
        JoyAimSpeed = Mathf.Clamp(value, 200.0, 4000.0);
        JoySettingsChanged?.Invoke(JoyAimSpeed, JoyDeadzone);
    }

    /// <summary>P0-1 手柄设置 setter：摇杆死区（0.05..0.90，应用至全部手柄动作的 InputMap deadzone）。
    /// K06：立即应用死区（InputMap 全局生效，base_system_test 契约）+ 广播；不自动写盘</summary>
    public void SetJoyDeadzone(double value)
    {
        JoyDeadzone = Mathf.Clamp(value, 0.05, 0.9);
        foreach (var a in GameState.Instance.JOYPAD_ACTIONS)
        {
            if (InputMap.HasAction(a))
            {
                InputMap.ActionSetDeadzone(a, (float)JoyDeadzone);
            }
        }

        JoySettingsChanged?.Invoke(JoyAimSpeed, JoyDeadzone);
    }

    /// <summary>手柄设置持久化：设置页滑杆 drag_ended 调用一次（setter 不再自动写盘，防拖动写风暴）</summary>
    public void PersistJoySettings() => GameState.Instance.SaveProfile();

    // ---------------- 视图簇（热路径：ViewWorldRect 帧缓存，2026-08-12 自 GameState.Settings.cs 逐字搬迁） ----------------

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
            var viewport = GameState.Instance.GetViewport();
            if (viewport != null)
            {
                size = viewport.GetVisibleRect().Size;
            }

            size /= (float)_viewZoomFactor;
            _viewRectCached = new Rect2(center - size * 0.5f, size);
        }

        return _viewRectCached;
    }

    // ---------------- 语言（中英双语） ----------------

    /// <summary>切换语言（白名单 zh/en，非法值忽略）：应用 TranslationServer + 持久化到 profile 并广播</summary>
    public void SetLocale(string pLocale)
    {
        if (pLocale != "zh" && pLocale != "en")
        {
            return;
        }

        Locale = pLocale;
        TranslationServer.SetLocale(pLocale);
        GameState.Instance.SaveProfile();
        LocaleChanged?.Invoke();
    }

    // ---------------- 设置域持久化桥（2026-08-12 自 GameState.Save.cs 迁入；SaveProfile 本体留在 GameState 侧） ----------------

    /// <summary>设置字段应用（profile.json 与 user_db settings 共用；含键位/窗口/视图缓存副作用，对齐原 load_profile）。
    /// 跨域键经 GameState.Instance 访问：TutorialDone/KeyBindings/Difficulty 本体留在 GameState（Input/Constants
    /// 域），本桥只读经门面；Difficulty 恢复后的 RefreshRegenCache 亦经 Instance 调用（RunProgressionService
    /// 回血缓存——GameState.Difficulty.cs 门面包装，第六轮起 public）。</summary>
    public void ApplySettingsDict(Godot.Collections.Dictionary data)
    {
        GameState.Instance.TutorialDone = GameState.Instance.SaveBool(data.GetValueOrDefault("tutorial_done", GameState.Instance.TutorialDone), GameState.Instance.TutorialDone);
        // E10：locale 加载经 zh/en 白名单守卫（对齐 set_locale）——手改非法值保持当前语言，
        // 避免 locale 变量与 TranslationServer 状态不一致
        var savedLocale = data.GetValueOrDefault("locale", Locale).AsString();
        if (savedLocale == "zh" || savedLocale == "en")
        {
            Locale = savedLocale;
        }

        // C02 修复：key_bindings 手改档案的类型守卫——非 Dictionary / 子值非 Array 时跳过该字段，
        // 不崩溃、不提前返回（其余字段照常加载）；typed 赋值在运行期校验失败会抛错并丢后续字段。
        GameState.Instance.KeyBindings.Clear();
        var savedKeys = data.GetValueOrDefault("key_bindings", new Variant());
        if (savedKeys.VariantType == Variant.Type.Dictionary)
        {
            foreach (var a in savedKeys.AsGodotDictionary().Keys)
            {
                var raw = savedKeys.AsGodotDictionary()[a];
                if (raw.VariantType != Variant.Type.Array)
                {
                    continue;
                }

                var keys = new Godot.Collections.Array<int>();
                foreach (var k in raw.AsGodotArray())
                {
                    // E11：元素级判型（C02 外层守卫的补全）——手改字符串 keycode 直接跳过，
                    // 不再 int() 转换错误刷屏（不崩溃但不干净）
                    if (k.VariantType is not Variant.Type.Int and not Variant.Type.Float)
                    {
                        continue;
                    }

                    keys.Add((int)k.AsInt64());
                }

                GameState.Instance.KeyBindings[a.AsStringName()] = keys;
            }
        }

        var savedDifficulty = data.GetValueOrDefault("difficulty", "").AsStringName();
        if (GameState.Instance.DIFFICULTY_DEFS.ContainsKey(savedDifficulty))
        {
            GameState.Instance.Difficulty = savedDifficulty;
            // Q04（2026-08-05）：存档/账户设置恢复难度后刷新被动回血缓存——
            // 原实现仅 _apply_balance 与 set_difficulty 刷新，重启后 hard 玩家按 medium 回血
            GameState.Instance.RefreshRegenCache();
        }

        CtrlToggleMode = GameState.Instance.SaveBool(data.GetValueOrDefault("ctrl_toggle_mode", CtrlToggleMode), CtrlToggleMode);
        ShiftToggleMode = GameState.Instance.SaveBool(data.GetValueOrDefault("shift_toggle_mode", ShiftToggleMode), ShiftToggleMode);
        var savedZoom = data.GetValueOrDefault("view_zoom", "").AsStringName();
        if (VIEW_ZOOM_LEVELS.ContainsKey(savedZoom))
        {
            ViewZoom = savedZoom;
            _viewZoomFactor = (double)VIEW_ZOOM_LEVELS[savedZoom].AsDouble();
            InvalidateViewRectCache();
        }

        var savedWindow = data.GetValueOrDefault("window_size", "").AsStringName();
        if (WINDOW_SIZE_LEVELS.ContainsKey(savedWindow))
        {
            WindowSize = savedWindow;
            ApplyWindowSize();
        }

        var savedAim = data.GetValueOrDefault("aim_assist", "").AsStringName();
        if (AIM_ASSIST_ORDER.Contains(savedAim))
        {
            AimAssistLevel = savedAim;
        }

        ReduceFlash = GameState.Instance.SaveBool(data.GetValueOrDefault("reduce_flash", ReduceFlash), ReduceFlash);
        MouseLock = GameState.Instance.SaveBool(data.GetValueOrDefault("mouse_lock", MouseLock), MouseLock);
        // P0-1 手柄设置：灵敏度默认取 balance player.aim_assist.joy_speed，死区默认 0.5
        var joySpeed = data.GetValueOrDefault("joy_aim_speed", GameState.Instance.Cfg("player.aim_assist.joy_speed", JoyAimSpeed));
        if (joySpeed.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            JoyAimSpeed = Mathf.Clamp(joySpeed.AsDouble(), 200.0, 4000.0);
        }

        var joyDz = data.GetValueOrDefault("joy_deadzone", JoyDeadzone);
        if (joyDz.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            JoyDeadzone = Mathf.Clamp(joyDz.AsDouble(), 0.05, 0.9);
        }
    }

    /// <summary>当前设置字段收集（profile.json 与 user_db settings 共用；统计类字段不在此列）</summary>
    public Godot.Collections.Dictionary CollectSettingsDict() => new()
    {
        ["tutorial_done"] = GameState.Instance.TutorialDone,
        ["key_bindings"] = GameState.Instance.KeyBindings,
        ["locale"] = Locale,
        ["difficulty"] = GameState.Instance.Difficulty.ToString(),
        ["ctrl_toggle_mode"] = CtrlToggleMode,
        ["shift_toggle_mode"] = ShiftToggleMode,
        ["view_zoom"] = ViewZoom.ToString(),
        ["window_size"] = WindowSize.ToString(),
        ["aim_assist"] = AimAssistLevel.ToString(),
        ["reduce_flash"] = ReduceFlash,
        ["mouse_lock"] = MouseLock,
        ["joy_aim_speed"] = JoyAimSpeed,
        ["joy_deadzone"] = JoyDeadzone,
    };
}
