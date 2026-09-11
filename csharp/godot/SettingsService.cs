using Godot;

namespace InfiAir;

/// <summary>
/// 设置+视图域服务：设置 setter 簇（SetCtrlToggleMode/SetShiftToggleMode/SetFireToggleMode/SetTouchControls/SetViewZoom/SetWindowMode/
/// SetResolution/SetAimAssistLevel/SetReduceFlash/SetMouseLock/SetJoyAimSpeed/SetJoyDeadzone/SetLocale/
/// PersistJoySettings）与视图簇（CameraRef/ViewWorldRect/CachedViewRect 物理帧缓存/
/// InvalidateViewRectCache/VIEW_ZOOM_LEVELS/RESOLUTION_LEVELS/AIM_ASSIST_ORDER）及状态字段
/// （CtrlToggleMode/ShiftToggleMode/FireToggleMode/TouchControls/ViewZoom/WindowMode/Resolution/CustomWindowWidth/
/// CustomWindowHeight/AimAssistLevel/ReduceFlash/MouseLock/Locale/JoyAimSpeed/JoyDeadzone/MetaFxLod）
/// 均在本服务；持久化桥 ApplySettingsDict/CollectSettingsDict 亦在此（设置域
/// 持久化，SaveSettings 留在 GameState 侧）。窗口管理：窗口模式（窗口化/无边框
/// 全屏）+ 渲染分辨率档（分辨率档即实际渲染分辨率，canvas_items 拉伸下窗口物理像素即渲染缓冲尺寸）
/// + 窗口化自由拖拽（拖拽捕获为自定义档）；ApplyWindow 单点应用，OnWindowResized 捕获拖拽结果
/// （GameState 侧对根窗口 SizeChanged 去抖后落盘并广播）。
/// Godot 绑定层：跨域访问统一经 GameState.Instance——键位/难度域（KeyBindings/TutorialDone/
/// Difficulty/DIFFICULTY_DEFS）与 SaveSettings/RefreshRegenCache/Cfg/SaveBool/JOYPAD_ACTIONS 及
/// GetViewport/GetWindow 均经 Instance 门面访问（跨域键不迁入，保持单一事实源）；_registry
/// （EntityManager）经构造注入（CameraRef 转发，与 RunProgressionService 注入 BalanceService 同构）。
/// 门面转发先例：与 MissionsService/ScoreService/RunProgressionService/CombatStateService
/// 同构——GameState 组合持有本服务，GameState.Settings.cs/State.cs 为门面对齐转发（签名/语义不变），
/// 保持唯一 autoload：GameState 约定。信号：本服务以 C# 事件 TouchControlsChanged/ViewZoomChanged/
/// WindowModeChanged/ResolutionChanged/AimAssistChanged/ReduceFlashChanged/MouseLockChanged/
/// JoySettingsChanged/LocaleChanged 通知；GameState 订阅后转发为同名信号（发射点/次数/顺序
/// 保持不变——LoadSettings 直写字段路径不发服务事件，无双发）。
/// </summary>
public sealed partial class SettingsService : RefCounted
{

    /// <summary>组合注入：GameState 持有的实体注册表（CameraRef 转发；GameState.cs 构造器传入）。</summary>
    private readonly EntityManager _registry;

    public SettingsService(EntityManager registry)
    {
        _registry = registry;
    }

    // ---------------- 状态字段 ----------------

    /// <summary>设置项：Ctrl 微调 / Shift 加速的模式（false=按住，true=切换；Player 移动/加速读取）</summary>
    public bool CtrlToggleMode { get; set; } = false;

    public bool ShiftToggleMode { get; set; } = false;

    /// <summary>开火方式（false=按住鼠标左键连发，true=按一下闩定、再按一下停火；Player 开火路径读取）</summary>
    public bool FireToggleMode { get; set; } = false;

    /// <summary>触屏虚拟控件开关（settings.json 持久化，默认关；Main 挂载 VirtualControls 联动）</summary>
    public bool TouchControls { get; set; } = false;

    /// <summary>视角档位（settings.json 持久化，默认 small=原始视角；相机 zoom = VIEW_ZOOM_LEVELS[view_zoom]）</summary>
    public StringName ViewZoom { get; set; } = new StringName("small");

    /// <summary>窗口模式（settings.json 持久化，默认 windowed=窗口化；borderless=无边框全屏）。
    /// 常量见 WindowModeWindowed/WindowModeBorderless。</summary>
    public StringName WindowMode { get; set; } = new StringName("windowed");

    /// <summary>渲染分辨率档（settings.json 持久化，默认 1920x1080；值为窗口逻辑尺寸，
    /// 表见 RESOLUTION_LEVELS；"custom"=用户拖拽出的自由尺寸，数值见 CustomWindowWidth/Height）。
    /// 窗口化模式下即实际窗口物理像素（×屏幕缩放），"分辨率档即渲染分辨率"。</summary>
    public StringName Resolution { get; set; } = new StringName("1920x1080");

    /// <summary>自定义窗口逻辑宽（Resolution=="custom" 时生效；拖拽窗口后记录）</summary>
    public int CustomWindowWidth { get; set; } = 1920;

    /// <summary>自定义窗口逻辑高（Resolution=="custom" 时生效；拖拽窗口后记录）</summary>
    public int CustomWindowHeight { get; set; } = 1080;

    /// <summary>瞄准辅助强度档位（settings.json 持久化，默认 medium；常驻不可关，无 off 档；数值见 AIM_ASSIST_ORDER 注释）</summary>
    public StringName AimAssistLevel { get; set; } = new StringName("medium");

    /// <summary>Meta HUD 当前 LOD（由 MetaHealthFX._ready 从 effects.meta_health.lod 写入；0=MetaFX 接管
    /// 低血晕影，hud 旧晕影恒 0；非 0=回退路径，hud 保留低血脉动。MetaFX 离场时置 1）</summary>
    public int MetaFxLod { get; set; } = 1;

    /// <summary>无障碍：减少闪光（settings.json 持久化；开启后色差 ×0.4、禁呼吸/抖动/心跳视觉脉冲，音效保留）</summary>
    public bool ReduceFlash { get; set; } = false;

    /// <summary>世界层画面增强（辉光/色彩分级/晕影，settings.json 持久化，默认开）。
    /// 关闭 = 逐元素发光回退路径，WorldPostFx 全屏层隐藏（低配机/风格偏好）。</summary>
    public bool WorldPostFx { get; set; } = true;

    /// <summary>帧率上限档位（settings.json 持久化，默认 "60"；档位表见 FPS_CAP_LEVELS）。
    /// 生效值写入 Engine.MaxFps；与垂直同步共同决定实际帧率（VSync 开时受显示器刷新率再钳制）。</summary>
    public StringName FpsCap { get; set; } = new StringName("60");

    /// <summary>垂直同步（settings.json 持久化，默认开；关闭可降输入延迟、配合帧率上限使用）。
    /// 生效值写入 DisplayServer WindowSetVsyncMode。</summary>
    public bool VSync { get; set; } = true;

    /// <summary>鼠标锁定窗口内（持久化，默认开启；开启后窗口聚焦期间鼠标移出内容区即被拉回，
    /// 防止准星跟随鼠标出框后位置冻结/跳变；窗口失焦自动放行，不阻碍切换应用）</summary>
    public bool MouseLock { get; set; } = true;

    /// <summary>默认跳过开场过场（持久化，默认关=播过场；开启后开机直达标题屏）</summary>
    public bool SkipIntroCinematic { get; set; } = false;

    /// <summary>手柄设置：右摇杆瞄准灵敏度 px/s（默认取 balance player.aim_assist.joy_speed）与摇杆死区。</summary>
    public double JoyAimSpeed { get; set; } = 1400.0;

    public double JoyDeadzone { get; set; } = 0.5;

    /// <summary>当前语言（"zh"/"en"，settings.json 持久化）。</summary>
    public string Locale { get; set; } = "zh";

    // ---------------- 信号 C# 事件 ----------------

    /// <summary>触屏虚拟控件开关变化；GameState 订阅后转发为 TouchControlsChanged 信号
    /// （LoadSettings 直写字段路径不经本事件——无双发）。</summary>
    public event Action<bool>? TouchControlsChanged;

    /// <summary>视角档位变化（参数为生效 zoom 倍率）；GameState 订阅后转发为 ViewZoomChanged 信号。</summary>
    public event Action<double>? ViewZoomChanged;

    /// <summary>窗口模式变化（窗口化/无边框全屏）；GameState 订阅后转发为 WindowModeChanged 信号。</summary>
    public event Action<StringName>? WindowModeChanged;

    /// <summary>渲染分辨率档变化（含拖拽捕获为 custom）；GameState 订阅后转发为 ResolutionChanged 信号。</summary>
    public event Action<StringName>? ResolutionChanged;

    /// <summary>瞄准辅助强度档位变化；GameState 订阅后转发为 AimAssistChanged 信号。</summary>
    public event Action<StringName>? AimAssistChanged;

    /// <summary>减少闪光开关变化；GameState 订阅后转发为 ReduceFlashChanged 信号。</summary>
    public event Action<bool>? ReduceFlashChanged;

    /// <summary>世界层画面增强开关变化；GameState 订阅后转发为 WorldPostFxChanged 信号。</summary>
    public event Action<bool>? WorldPostFxChanged;

    /// <summary>性能/显示设置（帧率上限 / 垂直同步）变化；GameState 订阅后转发为 DisplaySettingsChanged 信号。</summary>
    public event Action? DisplaySettingsChanged;

    /// <summary>鼠标锁定开关变化；GameState 订阅后转发为 MouseLockChanged 信号。</summary>
    public event Action<bool>? MouseLockChanged;

    /// <summary>手柄设置：右摇杆瞄准灵敏度 + 摇杆死区变更；GameState 订阅后转发为 JoySettingsChanged 信号。</summary>
    public event Action<double, double>? JoySettingsChanged;

    /// <summary>语言变化；GameState 订阅后转发为 LocaleChanged 信号。</summary>
    public event Action? LocaleChanged;

    // ---------------- 设置项（Ctrl/Shift 模式） ----------------

    /// <summary>Ctrl 微调模式：false=按住生效，true=按一下切换；持久化到 settings.json</summary>
    public void SetCtrlToggleMode(bool enabled)
    {
        CtrlToggleMode = enabled;
        GameState.Instance.SaveSettings();
    }

    /// <summary>Shift 加速模式：false=按住生效，true=按一下切换；持久化到 settings.json</summary>
    public void SetShiftToggleMode(bool enabled)
    {
        ShiftToggleMode = enabled;
        GameState.Instance.SaveSettings();
    }

    /// <summary>开火方式：false=按住鼠标左键连发，true=按一下切换；持久化到 settings.json</summary>
    public void SetFireToggleMode(bool enabled)
    {
        FireToggleMode = enabled;
        GameState.Instance.SaveSettings();
    }

    /// <summary>触屏虚拟控件开关（mobile touch）：持久化 + 广播（Main 联动 VirtualControls.set_enabled）</summary>
    public void SetTouchControls(bool enabled)
    {
        TouchControls = enabled;
        GameState.Instance.SaveSettings();
        TouchControlsChanged?.Invoke(enabled);
    }

    // ---------------- 视角缩放 ----------------

    /// <summary>视角档位表（设置页三选，settings.json 持久化；值为相机 zoom 倍率）。
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

    /// <summary>main 场景相机注册表（Main 在 _Ready/_ExitTree 维护），供可见区域计算</summary>
    public Camera2D? CameraRef
    {
        get => _registry.CameraRef;
        set
        {
            _registry.CameraRef = value;
            InvalidateViewRectCache();
        }
    }

    /// <summary>生效 zoom 倍率缓存（SetViewZoom/ApplySettingsDict 同步；热路径免查表，须与 small 档一致）</summary>
    private double _viewZoomFactor = 1.0;

    /// <summary>view_world_rect 物理帧缓存：同帧多次调用免重复视口查询（子弹/敌机/玩家每帧共用一帧结果）。
    /// zoom 因子或相机注册变更时置 -1 强制重算；相机位置固定 (960,540)，帧内语义不变。</summary>
    private long _viewRectFrame = -1;

    private Rect2 _viewRectCached = new();

    /// <summary>切换视角档位（非法/同档忽略），持久化到 settings.json 并广播</summary>
    public void SetViewZoom(StringName level)
    {
        if (!VIEW_ZOOM_LEVELS.ContainsKey(level) || level == ViewZoom)
        {
            return;
        }

        ViewZoom = level;
        _viewZoomFactor = (double)VIEW_ZOOM_LEVELS[level].AsDouble();
        InvalidateViewRectCache();
        GameState.Instance.SaveSettings();
        ViewZoomChanged?.Invoke(_viewZoomFactor);
    }

    public double ViewZoomFactor() => _viewZoomFactor;

    public void SetViewZoomFactor(double factor)
    {
        _viewZoomFactor = factor;
        InvalidateViewRectCache();
    }

    // ---------------- 窗口管理（窗口模式 + 渲染分辨率档 + 自由拖拽） ----------------

    /// <summary>窗口模式常量：窗口化（可拖拽/可缩放）与无边框全屏（铺满显示器、无标题栏、不改显示模式）。</summary>
    public static readonly StringName WindowModeWindowed = new("windowed");
    public static readonly StringName WindowModeBorderless = new("borderless");

    /// <summary>渲染分辨率档表（设置页选项 + settings.json 持久化；值为窗口逻辑尺寸（逻辑点），
    /// 应用时 × DisplayServer.ScreenGetScale 换算物理像素）。canvas_items 拉伸下逻辑坐标系恒
    /// 1920×1080，档位只改窗口物理像素（即实际渲染分辨率）。仅列 16:9 档位；非 16:9 由用户拖拽
    /// 自由尺寸（stretch/aspect=keep 会留黑边，世界坐标不受影响）。</summary>
    public Godot.Collections.Dictionary RESOLUTION_LEVELS { get; } = new()
    {
        [new StringName("1280x720")] = new Vector2I(1280, 720),
        [new StringName("1600x900")] = new Vector2I(1600, 900),
        [new StringName("1920x1080")] = new Vector2I(1920, 1080),
        [new StringName("2560x1440")] = new Vector2I(2560, 1440),
        [new StringName("3840x2160")] = new Vector2I(3840, 2160),
    };

    public Godot.Collections.Array<StringName> RESOLUTION_ORDER { get; } = new()
    {
        new StringName("1280x720"),
        new StringName("1600x900"),
        new StringName("1920x1080"),
        new StringName("2560x1440"),
        new StringName("3840x2160"),
    };

    /// <summary>自定义档键（拖拽出非预设尺寸时 Resolution 切到此值，数值存 CustomWindowWidth/Height）</summary>
    public static readonly StringName ResolutionCustom = new("custom");

    /// <summary>上次由 ApplyWindow 写入的物理窗口尺寸（拖拽捕获据此区分"程序改"与"用户拖"）</summary>
    private Vector2I _lastAppliedWindowSize = Vector2I.Zero;

    /// <summary>ApplyWindow 写入窗口期间置位，抑制 SizeChanged 回写（避免自触发→改档→再应用循环）</summary>
    private bool _applyingWindow;

    /// <summary>切换窗口模式（非法/同值忽略）：立即应用 + 持久化 + 广播</summary>
    public void SetWindowMode(StringName mode)
    {
        if ((mode != WindowModeWindowed && mode != WindowModeBorderless) || mode == WindowMode)
        {
            return;
        }

        WindowMode = mode;
        ApplyWindow();
        GameState.Instance.SaveSettings();
        WindowModeChanged?.Invoke(mode);
    }

    /// <summary>切换渲染分辨率档（非法/同值忽略）：立即应用窗口，持久化并广播</summary>
    public void SetResolution(StringName preset)
    {
        if (!RESOLUTION_LEVELS.ContainsKey(preset) || preset == Resolution)
        {
            return;
        }

        Resolution = preset;
        ApplyWindow();
        GameState.Instance.SaveSettings();
        ResolutionChanged?.Invoke(preset);
    }

    /// <summary>应用当前窗口模式 + 分辨率到实际窗口（启动加载后与设置变更时调用）。
    /// headless 为 dummy 渲染直接跳过窗口 API（沿用 ApplyDisplay 惯例）。
    /// 窗口化：写 Windowed 模式 + 可缩放 + 逻辑尺寸 × 屏幕缩放 → 越界等比收缩 → 居中；
    /// 无边框全屏：写 Fullscreen 模式（Godot 自动铺满显示器、用桌面分辨率、不改显示模式），
    /// 无需显式设尺寸。写入前后以 _applyingWindow/_lastAppliedWindowSize 标记，供拖拽捕获区分。</summary>
    public void ApplyWindow()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        var win = GameState.Instance.GetWindow();
        if (win == null)
        {
            return;
        }

        _applyingWindow = true;
        try
        {
            if (WindowMode == WindowModeBorderless)
            {
                win.Mode = Window.ModeEnum.Fullscreen;
                return;
            }

            win.Mode = Window.ModeEnum.Windowed;
            win.Unresizable = false; // 窗口化允许自由拖拽（分辨率档只是预设起点）
            var screen = win.CurrentScreen;
            var scale = DisplayServer.ScreenGetScale(screen);
            var logical = ResolutionPointSize();
            var phys = (Vector2I)((Vector2)logical * scale);
            var usable = DisplayServer.ScreenGetUsableRect(screen);
            if (phys.X > usable.Size.X || phys.Y > usable.Size.Y)
            {
                var fit = Mathf.Min((float)usable.Size.X / phys.X, (float)usable.Size.Y / phys.Y);
                phys = (Vector2I)((Vector2)phys * fit);
            }

            win.Size = phys;
            win.Position = usable.Position + (usable.Size - phys) / 2;
            _lastAppliedWindowSize = phys;
        }
        finally
        {
            _applyingWindow = false;
        }
    }

    /// <summary>当前档位的逻辑尺寸：预设档查 RESOLUTION_LEVELS，custom 用 CustomWindowWidth/Height。</summary>
    public Vector2I ResolutionPointSize()
    {
        if (RESOLUTION_LEVELS.TryGetValue(Resolution, out var v))
        {
            return v.AsVector2I();
        }

        return new Vector2I(Mathf.Max(CustomWindowWidth, 320), Mathf.Max(CustomWindowHeight, 240));
    }

    /// <summary>拖拽捕获（GameState 对根窗口 SizeChanged 去抖后调用）：把物理窗口尺寸折算回逻辑档。
    /// 程序写入（_applyingWindow 或等于 _lastAppliedWindowSize）与无边框全屏一律忽略；
    /// 与当前档位逻辑尺寸一致则不变；命中某预设档切到该档，否则切 custom 并记录尺寸。
    /// 返回 true = 档位/自定义尺寸有变化（调用侧据此落盘 + 广播）。</summary>
    public bool OnWindowResized(Vector2I physicalSize)
    {
        if (_applyingWindow || WindowMode == WindowModeBorderless || physicalSize == _lastAppliedWindowSize)
        {
            return false;
        }

        var win = GameState.Instance.GetWindow();
        if (win == null || physicalSize.X <= 0 || physicalSize.Y <= 0)
        {
            return false;
        }

        // 用户拖拽已生效：更新基线，保证后续拖回原尺寸不被误判为程序写入
        _lastAppliedWindowSize = physicalSize;
        var scale = win.CurrentScreen >= 0 ? DisplayServer.ScreenGetScale(win.CurrentScreen) : 1.0f;
        if (scale <= 0.0f)
        {
            scale = 1.0f;
        }

        var logical = new Vector2I(
            Mathf.Max((int)Mathf.Round(physicalSize.X / scale), 320),
            Mathf.Max((int)Mathf.Round(physicalSize.Y / scale), 240)
        );
        if (logical == ResolutionPointSize())
        {
            return false;
        }

        // 命中预设档（容差 1 逻辑点，容忍 scale 舍入）→ 切档；否则自定义
        foreach (var key in RESOLUTION_ORDER)
        {
            var preset = RESOLUTION_LEVELS[key].AsVector2I();
            if (Mathf.Abs(preset.X - logical.X) <= 1 && Mathf.Abs(preset.Y - logical.Y) <= 1)
            {
                if (key == Resolution)
                {
                    return false;
                }

                Resolution = key;
                return true;
            }
        }

        CustomWindowWidth = logical.X;
        CustomWindowHeight = logical.Y;
        if (Resolution == ResolutionCustom)
        {
            return true; // 尺寸变了但仍是 custom 档：调用侧仍需落盘
        }

        Resolution = ResolutionCustom;
        return true;
    }

    // ---------------- 性能（帧率上限 / 垂直同步） ----------------

    /// <summary>帧率上限档位表（设置页六选，settings.json 持久化；值为 Engine.MaxFps）。
    /// 0 = 无限制（本作未列入档位表——默认 60；如需无限制档可加 "unlimited"→0）。</summary>
    public Godot.Collections.Dictionary FPS_CAP_LEVELS { get; } = new()
    {
        [new StringName("fps60")] = 60,
        [new StringName("fps120")] = 120,
        [new StringName("fps144")] = 144,
        [new StringName("fps165")] = 165,
        [new StringName("fps180")] = 180,
        [new StringName("fps240")] = 240,
    };

    public Godot.Collections.Array<StringName> FPS_CAP_ORDER { get; } = new()
    {
        new StringName("fps60"),
        new StringName("fps120"),
        new StringName("fps144"),
        new StringName("fps165"),
        new StringName("fps180"),
        new StringName("fps240"),
    };

    /// <summary>切换帧率上限档位（非法/同档忽略）：立即应用 + 持久化 + 广播</summary>
    public void SetFpsCap(StringName level)
    {
        if (!FPS_CAP_LEVELS.ContainsKey(level) || level == FpsCap)
        {
            return;
        }

        FpsCap = level;
        ApplyDisplay();
        GameState.Instance.SaveSettings();
        DisplaySettingsChanged?.Invoke();
    }

    /// <summary>切换垂直同步（同值忽略）：立即应用 + 持久化 + 广播</summary>
    public void SetVSync(bool enabled)
    {
        if (enabled == VSync)
        {
            return;
        }

        VSync = enabled;
        ApplyDisplay();
        GameState.Instance.SaveSettings();
        DisplaySettingsChanged?.Invoke();
    }

    /// <summary>把帧率上限 + 垂直同步应用到引擎（启动加载后与设置变更时调用；headless 跳过窗口 API）。
    /// 两者相互独立：VSync 开时实际帧率再受显示器刷新率钳制；关则只受 MaxFps 限制。</summary>
    public void ApplyDisplay()
    {
        Engine.MaxFps = (int)FPS_CAP_LEVELS.GetValueOrDefault(FpsCap, 60).AsInt64();
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        }
    }

    // ---------------- 瞄准辅助强度 ----------------

    /// <summary>强度档位表（设置页三选，settings.json 持久化；辅助瞄准常驻、刻意不提供关闭档）。
    /// 各档数值（辅助框内边距 frame_pad/追踪转向速率 homing_turn_rate）在 balance.json player.aim_assist.levels。</summary>
    public Godot.Collections.Array<StringName> AIM_ASSIST_ORDER { get; } = new()
    {
        new StringName("low"),
        new StringName("medium"),
        new StringName("high"),
    };

    /// <summary>切换瞄准辅助强度档位（非法/同档忽略），持久化到 settings.json 并广播</summary>
    public void SetAimAssistLevel(StringName level)
    {
        if (!AIM_ASSIST_ORDER.Contains(level) || level == AimAssistLevel)
        {
            return;
        }

        AimAssistLevel = level;
        GameState.Instance.SaveSettings();
        AimAssistChanged?.Invoke(level);
    }

    /// <summary>无障碍·减少闪光：开关持久化到 settings.json 并广播（Meta HUD 据此折算色差/禁脉冲）</summary>
    public void SetReduceFlash(bool enabled)
    {
        if (enabled == ReduceFlash)
        {
            return;
        }

        ReduceFlash = enabled;
        GameState.Instance.SaveSettings();
        ReduceFlashChanged?.Invoke(enabled);
    }

    /// <summary>世界层画面增强：开关持久化并广播（WorldPostFx 据此显隐全屏增强层）</summary>
    public void SetWorldPostFx(bool enabled)
    {
        if (enabled == WorldPostFx)
        {
            return;
        }

        WorldPostFx = enabled;
        GameState.Instance.SaveSettings();
        WorldPostFxChanged?.Invoke(enabled);
    }

    /// <summary>鼠标锁定窗口内：开关持久化并广播（MouseTrap 据此决定是否拉回出框鼠标）</summary>
    public void SetMouseLock(bool enabled)
    {
        if (enabled == MouseLock)
        {
            return;
        }

        MouseLock = enabled;
        GameState.Instance.SaveSettings();
        MouseLockChanged?.Invoke(enabled);
    }

    /// <summary>默认跳过开场过场：开关持久化（无运行期副作用——只在下次开机生效）</summary>
    public void SetSkipIntroCinematic(bool enabled)
    {
        if (enabled == SkipIntroCinematic)
        {
            return;
        }

        SkipIntroCinematic = enabled;
        GameState.Instance.SaveSettings();
    }

    /// <summary>手柄设置 setter：右摇杆瞄准灵敏度（200..4000 px/s）。
    /// 只更新内存 + 广播（灵敏度不影响 InputMap 死区）；持久化由设置页 drag_ended 统一
    /// 提交——不得每步全量原子写盘，否则滑杆拖动（数十次 value_changed）放大为磁盘写风暴</summary>
    public void SetJoyAimSpeed(double value)
    {
        JoyAimSpeed = Mathf.Clamp(value, 200.0, 4000.0);
        JoySettingsChanged?.Invoke(JoyAimSpeed, JoyDeadzone);
    }

    /// <summary>手柄设置 setter：摇杆死区（0.05..0.90，应用至全部手柄动作的 InputMap deadzone）。
    /// 立即应用死区（InputMap 全局生效）+ 广播；不自动写盘</summary>
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
    public void PersistJoySettings() => GameState.Instance.SaveSettings();

    // ---------------- 视图簇（热路径：ViewWorldRect 帧缓存） ----------------

    /// <summary>当前可见世界区域（相机未注册时以 (960,540) 为心），margin 向外扩张。
    /// 屏幕边缘钳制 / 出屏销毁 / 刷怪位置统一以此为准；zoom=1 时即全屏 1920×1080。
    /// 物理帧内缓存：同一物理帧内多次调用（每弹/每敌/玩家/Boss）共享一次视口查询。</summary>
    public Rect2 ViewWorldRect(double margin = 0.0)
    {
        if (margin == 0.0)
        {
            return CachedViewRect();
        }

        return CachedViewRect().Grow((float)margin);
    }

    /// <summary>视角缓存失效（set_view_zoom/camera_ref/登录即时生效路径调用；GameState 侧私有一行包装）。</summary>
    public void InvalidateViewRectCache() => _viewRectFrame = -1;

    private Rect2 CachedViewRect()
    {
        var frame = (long)Engine.GetPhysicsFrames();
        if (frame != _viewRectFrame)
        {
            _viewRectFrame = frame;
            var center = new Vector2(960.0f, 540.0f);
            var size = new Vector2(1920.0f, 1080.0f);
            var viewport = GameState.Instance.GetViewport();
            if (viewport != null)
            {
                size = viewport.GetVisibleRect().Size;
            }

            // 视角缩放只在相机已注册时参与收窄：zoom 经 Main.ApplyCameraZoom 写入相机才真正
            // 作用于渲染，"可见世界区域"必须与实际可见范围一致。相机未注册的屏幕（标题屏/
            // 教程）渲染本就是全画布，rect 若仍除持久化 zoom 会把星空/刷怪布局收窄成屏幕
            // 中央一块；过场镜头等 CanvasLayer 内 1:1 画布
            // 的装饰（相机已注册但 zoom 不作用于该画布）由 Starfield 侧按语境自行取全视口。
            if (CameraRef != null && GodotObject.IsInstanceValid(CameraRef))
            {
                center = CameraRef.GlobalPosition;
                size /= (float)_viewZoomFactor;
            }

            _viewRectCached = new Rect2(center - size * 0.5f, size);
        }

        return _viewRectCached;
    }

    // ---------------- 语言（中英双语） ----------------

    /// <summary>切换语言（白名单 zh/en，非法值忽略）：应用 TranslationServer + 持久化到 settings.json 并广播</summary>
    public void SetLocale(string pLocale)
    {
        if (pLocale != "zh" && pLocale != "en")
        {
            return;
        }

        Locale = pLocale;
        TranslationServer.SetLocale(pLocale);
        GameState.Instance.SaveSettings();
        LocaleChanged?.Invoke();
    }

    // ---------------- 设置域持久化桥（SaveSettings 本体留在 GameState 侧） ----------------

    /// <summary>设置字段应用（user://settings.json 读入的字典；含键位/窗口/视图缓存副作用）。
    /// 跨域键经 GameState.Instance 访问：TutorialDone/KeyBindings/Difficulty 本体留在 GameState（Input/Constants
    /// 域），本桥只读经门面；Difficulty 恢复后的 RefreshRegenCache 亦经 Instance 调用（RunProgressionService
    /// 回血缓存——GameState.Difficulty.cs 门面包装）。</summary>
    public void ApplySettingsDict(Godot.Collections.Dictionary data)
    {
        GameState.Instance.TutorialDone = GameState.Instance.SaveBool(data.GetValueOrDefault("tutorial_done", GameState.Instance.TutorialDone), GameState.Instance.TutorialDone);
        // locale 加载经 zh/en 白名单守卫（同 SetLocale）——手改非法值保持当前语言，
        // 避免 locale 变量与 TranslationServer 状态不一致
        var savedLocale = data.GetValueOrDefault("locale", Locale).AsString();
        if (savedLocale == "zh" || savedLocale == "en")
        {
            Locale = savedLocale;
        }

        // key_bindings 手改档案的类型守卫——非 Dictionary / 子值非 Array 时跳过该字段，
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
                    // 元素级判型（外层守卫的补全）——手改字符串 keycode 直接跳过，
                    // 不再 int() 转换错误刷屏（不崩溃但不干净）
                    if (k.VariantType is not Variant.Type.Int and not Variant.Type.Float)
                    {
                        continue;
                    }

                    keys.Add((int)k.AsInt64());
                }

                var action = a.AsStringName();
                // 档案里的旧增幅 面板键位迁往新动作名
                if (action == new StringName("buff_panel"))
                {
                    action = new StringName("augment_panel");
                }

                GameState.Instance.KeyBindings[action] = keys;
            }
        }

        var savedDifficulty = data.GetValueOrDefault("difficulty", "").AsStringName();
        if (GameState.Instance.DIFFICULTY_DEFS.ContainsKey(savedDifficulty))
        {
            GameState.Instance.Difficulty = savedDifficulty;
            // 存档/账户设置恢复难度后必须刷新被动回血缓存——
            // 否则仅 _apply_balance 与 set_difficulty 刷新，重启后 hard 玩家按 medium 回血
            GameState.Instance.RefreshRegenCache();
        }

        CtrlToggleMode = GameState.Instance.SaveBool(data.GetValueOrDefault("ctrl_toggle_mode", CtrlToggleMode), CtrlToggleMode);
        ShiftToggleMode = GameState.Instance.SaveBool(data.GetValueOrDefault("shift_toggle_mode", ShiftToggleMode), ShiftToggleMode);
        FireToggleMode = GameState.Instance.SaveBool(data.GetValueOrDefault("fire_toggle_mode", FireToggleMode), FireToggleMode);
        var savedZoom = data.GetValueOrDefault("view_zoom", "").AsStringName();
        if (VIEW_ZOOM_LEVELS.ContainsKey(savedZoom))
        {
            ViewZoom = savedZoom;
            _viewZoomFactor = (double)VIEW_ZOOM_LEVELS[savedZoom].AsDouble();
            InvalidateViewRectCache();
        }

        // 新模式键缺失时兼容迁移旧 window_size（small/medium/large）。
        var savedMode = data.GetValueOrDefault("window_mode", "").AsStringName();
        if (savedMode == WindowModeWindowed || savedMode == WindowModeBorderless)
        {
            WindowMode = savedMode;
        }

        var savedResolution = data.GetValueOrDefault("resolution", "").AsStringName();
        if (RESOLUTION_LEVELS.ContainsKey(savedResolution) || savedResolution == ResolutionCustom)
        {
            Resolution = savedResolution;
        }
        else if (!data.ContainsKey("window_mode") && data.ContainsKey("window_size"))
        {
            // 旧档案迁移：window_size 三档 → 16:9 分辨率档（旧档位仅窗口化生效，模式取默认 windowed）
            var legacy = data.GetValueOrDefault("window_size", "").AsStringName();
            Resolution = legacy switch
            {
                var s when s == new StringName("small") => new StringName("1280x720"),
                var s when s == new StringName("medium") => new StringName("1600x900"),
                _ => new StringName("1920x1080"),
            };
        }

        // custom 尺寸判型（对齐 joy 字段惯例）：手改档案非数值时跳过，不触发 Variant 转换错误
        var customW = data.GetValueOrDefault("custom_width", new Variant());
        var customH = data.GetValueOrDefault("custom_height", new Variant());
        if (customW.VariantType is Variant.Type.Int or Variant.Type.Float
            && customH.VariantType is Variant.Type.Int or Variant.Type.Float)
        {
            var w = (int)Mathf.Clamp(customW.AsInt32(), 320, 16384);
            var h = (int)Mathf.Clamp(customH.AsInt32(), 240, 16384);
            CustomWindowWidth = w;
            CustomWindowHeight = h;
        }

        ApplyWindow();

        var savedAim = data.GetValueOrDefault("aim_assist", "").AsStringName();
        if (AIM_ASSIST_ORDER.Contains(savedAim))
        {
            AimAssistLevel = savedAim;
        }

        // 性能：帧率上限档（白名单）+ 垂直同步；非法值保持默认，末尾统一应用到引擎
        var savedFps = data.GetValueOrDefault("fps_cap", "").AsStringName();
        if (FPS_CAP_LEVELS.ContainsKey(savedFps))
        {
            FpsCap = savedFps;
        }

        VSync = GameState.Instance.SaveBool(data.GetValueOrDefault("vsync", VSync), VSync);
        ApplyDisplay();
        ReduceFlash = GameState.Instance.SaveBool(data.GetValueOrDefault("reduce_flash", ReduceFlash), ReduceFlash);
        WorldPostFx = GameState.Instance.SaveBool(data.GetValueOrDefault("world_post_fx", WorldPostFx), WorldPostFx);
        MouseLock = GameState.Instance.SaveBool(data.GetValueOrDefault("mouse_lock", MouseLock), MouseLock);
        SkipIntroCinematic = GameState.Instance.SaveBool(data.GetValueOrDefault("skip_intro", SkipIntroCinematic), SkipIntroCinematic);
        // 手柄设置：灵敏度默认取 balance player.aim_assist.joy_speed，死区默认 0.5
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

    /// <summary>当前设置字段收集（settings.json；统计类字段不在此列）</summary>
    public Godot.Collections.Dictionary CollectSettingsDict() => new()
    {
        ["tutorial_done"] = GameState.Instance.TutorialDone,
        ["key_bindings"] = GameState.Instance.KeyBindings,
        ["locale"] = Locale,
        ["difficulty"] = GameState.Instance.Difficulty.ToString(),
        ["ctrl_toggle_mode"] = CtrlToggleMode,
        ["shift_toggle_mode"] = ShiftToggleMode,
        ["fire_toggle_mode"] = FireToggleMode,
        ["view_zoom"] = ViewZoom.ToString(),
        ["window_mode"] = WindowMode.ToString(),
        ["resolution"] = Resolution.ToString(),
        ["custom_width"] = CustomWindowWidth,
        ["custom_height"] = CustomWindowHeight,
        ["aim_assist"] = AimAssistLevel.ToString(),
        ["reduce_flash"] = ReduceFlash,
        ["world_post_fx"] = WorldPostFx,
        ["fps_cap"] = FpsCap.ToString(),
        ["vsync"] = VSync,
        ["mouse_lock"] = MouseLock,
        ["skip_intro"] = SkipIntroCinematic,
        ["joy_aim_speed"] = JoyAimSpeed,
        ["joy_deadzone"] = JoyDeadzone,
    };
}
