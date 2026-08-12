using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：设置项（Ctrl/Shift/视角/窗口/瞄准/语言）与视图。
/// 第六轮拆域收官（2026-08-12）：设置+视图域全部职责迁至 SettingsService（csharp/godot/SettingsService.cs，
/// 组合持有；职责 A 设置 setter 簇 + 职责 B 视图簇 + 状态字段 + 设置域持久化桥 ApplySettingsDict/
/// CollectSettingsDict 一并迁入），本文件为门面对齐转发——公开 API 签名/语义不变（测试白盒经
/// 此处零适配直读直写 ViewZoom/WindowSize/AimAssistLevel/Locale 等属性、直调 ViewWorldRect 全保留）；
/// ApplyWindowSize 为私有一行包装（GameState._Ready 启动补一次默认档位调用）。
/// 信号：TouchControlsChanged/ViewZoomChanged/WindowSizeChanged/AimAssistChanged/ReduceFlashChanged/
/// MouseLockChanged/JoySettingsChanged/LocaleChanged 由 SettingsService 的 C# 事件经 GameState 订阅重发
/// （ApplyRunSave 的 TouchControlsChanged 直发路径在 GameState 侧直发同名信号，不经服务事件，不重复）。
/// 健康/Buff 域 C 簇（第五轮拆域）保留转发 → CombatStateService，见文件末尾。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 设置项（Ctrl/Shift 模式；门面转发 → SettingsService） ----------------

    /// <summary>Ctrl 微调模式：false=按住生效，true=按一下切换；持久化到 profile</summary>
    public void SetCtrlToggleMode(bool enabled) => _settings.SetCtrlToggleMode(enabled);

    /// <summary>Shift 加速模式：false=按住生效，true=按一下切换；持久化到 profile</summary>
    public void SetShiftToggleMode(bool enabled) => _settings.SetShiftToggleMode(enabled);

    /// <summary>触屏虚拟控件开关（mobile touch）：持久化 + 广播（Main 联动 VirtualControls.set_enabled）</summary>
    public void SetTouchControls(bool enabled) => _settings.SetTouchControls(enabled);

    // ---------------- 视角缩放（门面转发 → SettingsService） ----------------

    /// <summary>视角档位表（设置页三选，profile 持久化；值为相机 zoom 倍率）。
    /// zoom&gt;1 时可见世界区域 = 视口 ÷ zoom（以相机位置为中心收窄），
    /// 所有"屏幕边缘/出屏"逻辑统一走 view_world_rect() 适配。</summary>
    public Godot.Collections.Dictionary VIEW_ZOOM_LEVELS => _settings.VIEW_ZOOM_LEVELS;

    public Godot.Collections.Array<StringName> VIEW_ZOOM_ORDER => _settings.VIEW_ZOOM_ORDER;

    /// <summary>main 场景相机注册表（main.gd 在 _ready/_exit_tree 维护），供可见区域计算</summary>
    public Camera2D? CameraRef
    {
        get => _settings.CameraRef;
        set => _settings.CameraRef = value;
    }

    /// <summary>切换视角档位（非法/同档忽略），持久化到 profile 并广播</summary>
    public void SetViewZoom(StringName level) => _settings.SetViewZoom(level);

    public double ViewZoomFactor() => _settings.ViewZoomFactor();

    public void SetViewZoomFactor(double factor) => _settings.SetViewZoomFactor(factor);

    /// <summary>当前可见世界区域（相机未注册时以 (960,540) 为心），margin 向外扩张。
    /// 屏幕边缘钳制 / 出屏销毁 / 刷怪位置统一以此为准；zoom=1 时即全屏 1920×1080。
    /// 物理帧内缓存（P0-1）：同一物理帧内多次调用（每弹/每敌/玩家/Boss）共享一次视口查询——
    /// SettingsService 转发（帧缓存逻辑在服务侧逐字保持）。</summary>
    public Rect2 ViewWorldRect(double margin = 0.0) => _settings.ViewWorldRect(margin);

    // ---------------- 窗口大小（门面转发 → SettingsService） ----------------

    /// <summary>窗口尺寸档位表（设置页三选，profile 持久化；stretch 等比缩放，仅改窗口物理尺寸）。</summary>
    public Godot.Collections.Dictionary WINDOW_SIZE_LEVELS
    {
        get => _settings.WINDOW_SIZE_LEVELS;
        set => _settings.WINDOW_SIZE_LEVELS = value;
    }

    public Godot.Collections.Array<StringName> WINDOW_SIZE_ORDER => _settings.WINDOW_SIZE_ORDER;

    /// <summary>切换窗口尺寸档位（非法/同档忽略）：立即应用窗口，持久化到 profile 并广播</summary>
    public void SetWindowSize(StringName level) => _settings.SetWindowSize(level);

    /// <summary>应用当前档位到窗口：仅窗口模式生效；headless 为 dummy 渲染直接跳过——
    /// 一行包装（本体在 SettingsService；GameState._Ready 启动补一次默认档位调用；
    /// 第七轮拆域：UserSessionService.LoginUser 跨域经 Instance 调用——可见性提升，
    /// 与 RefreshRegenCache private→public 先例同款）。</summary>
    public void ApplyWindowSize() => _settings.ApplyWindowSize();

    /// <summary>视角缓存失效（UserSessionService.LoginUser 登录即时生效路径调用；本体在
    /// SettingsService）——一行包装（第七轮拆域：跨域经 Instance 调用——可见性提升）。</summary>
    public void InvalidateViewRectCache() => _settings.InvalidateViewRectCache();

    // ---------------- 瞄准辅助强度（门面转发 → SettingsService） ----------------

    /// <summary>强度档位表（设置页三选，profile 持久化；辅助瞄准常驻、刻意不提供关闭档）。
    /// 各档数值（辅助框内边距 frame_pad/追踪转向速率 homing_turn_rate）在 balance.json player.aim_assist.levels。</summary>
    public Godot.Collections.Array<StringName> AIM_ASSIST_ORDER => _settings.AIM_ASSIST_ORDER;

    /// <summary>切换瞄准辅助强度档位（非法/同档忽略），持久化到 profile 并广播</summary>
    public void SetAimAssistLevel(StringName level) => _settings.SetAimAssistLevel(level);

    /// <summary>无障碍·减少闪光：开关持久化到 profile 并广播（Meta HUD 据此折算色差/禁脉冲）</summary>
    public void SetReduceFlash(bool enabled) => _settings.SetReduceFlash(enabled);

    /// <summary>鼠标锁定窗口内：开关持久化到 profile 并广播（MouseTrap 据此决定是否拉回出框鼠标）</summary>
    public void SetMouseLock(bool enabled) => _settings.SetMouseLock(enabled);

    /// <summary>P0-1 手柄设置 setter：右摇杆瞄准灵敏度（200..4000 px/s）。</summary>
    public void SetJoyAimSpeed(double value) => _settings.SetJoyAimSpeed(value);

    /// <summary>P0-1 手柄设置 setter：摇杆死区（0.05..0.90，应用至全部手柄动作的 InputMap deadzone）。</summary>
    public void SetJoyDeadzone(double value) => _settings.SetJoyDeadzone(value);

    /// <summary>手柄设置持久化：设置页滑杆 drag_ended 调用一次（setter 不再自动写盘，防拖动写风暴）</summary>
    public void PersistJoySettings() => _settings.PersistJoySettings();

    // ---------------- 健康/Buff 域（门面转发 → CombatStateService） ----------------

    /// <summary>生命上限：基础 100 + extra_life 每层 +50（对齐原作 EXTRA_LIFE_BONUS_HP）
    /// P0-2：基础值 _apply_balance 缓存，热路径免 cfg 路径解析（extra_life 层数查询 O(1)）——CombatStateService 转发。</summary>
    public double MaxHealth() => _combat.MaxHealth();

    public void LoseHealth(double amount = 1.0) => _combat.LoseHealth(amount);

    /// <summary>治疗（单点封顶 max_health，调用侧不再各自判断）</summary>
    public void Heal(double amount) => _combat.Heal(amount);

    /// <summary>吸血 buff：击杀回复 int(上限 × 10%)（对齐原作 LIFESTEAL_FRACTION），每帧至多结算一次</summary>
    public void TryLifesteal() => _combat.TryLifesteal();

    public int BuffCount(StringName id) => _combat.BuffCount(id);

    public void AddBuff(StringName id) => _combat.AddBuff(id);

    /// <summary>消耗一层 buff（护盾等一次性层；无剩余层返回 false；层数变动广播 buffs_changed）</summary>
    public bool ConsumeBuff(StringName id) => _combat.ConsumeBuff(id);

    // ---------------- 语言（中英双语；门面转发 → SettingsService） ----------------

    /// <summary>当前语言（"zh"/"en"，profile 持久化）——SettingsService 转发（测试白盒直读直写保留）。</summary>
    public string Locale
    {
        get => _settings.Locale;
        set => _settings.Locale = value;
    }

    public void SetLocale(string pLocale) => _settings.SetLocale(pLocale);

    // ---------------- 设置域持久化桥（第七轮拆域：UserSessionService.LoadSessionSettings 跨域
    // 经 Instance 调用——公开转发；CollectSettingsDict 仍在 GameState.Save.cs 内部直调服务，无需门面） ----------------

    /// <summary>设置字段应用（profile.json 与 user_db settings 共用；含键位/窗口/视图缓存副作用，
    /// 对齐原 load_profile）——本体在 SettingsService，UserSessionService 登录会话应用经此跨域。</summary>
    public void ApplySettingsDict(Godot.Collections.Dictionary data) => _settings.ApplySettingsDict(data);
}
