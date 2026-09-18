using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：设置项（Ctrl/Shift/开火/视角/窗口/瞄准/语言）与视图，职责在 SettingsService
/// （csharp/godot/SettingsService.cs，组合持有；设置 setter 簇 + 视图簇 + 状态字段 +
/// 设置域持久化桥 ApplySettingsDict/CollectSettingsDict 均在其中），本文件为门面对齐转发——公开 API 签名/语义不变。
/// ApplyWindow/OnWindowResized 为一行包装（GameState._Ready 启动补一次默认档；
/// 拖拽捕获经根窗口 SizeChanged 去抖后调 OnWindowResized）。
/// 信号：ViewZoomChanged/WindowModeChanged/ResolutionChanged/AimAssistChanged/
/// ReduceFlashChanged/MouseLockChanged/JoySettingsChanged/LocaleChanged 由 SettingsService 的 C# 事件
/// 经 GameState 订阅重发。
/// 健康/增幅 域保留转发 → CombatStateService，见文件末尾。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 设置项（Ctrl/Shift/开火 模式；门面转发 → SettingsService） ----------------

    /// <summary>Ctrl 微调模式：false=按住生效，true=按一下切换；持久化到 settings.json</summary>
    public void SetCtrlToggleMode(bool enabled) => _settings.SetCtrlToggleMode(enabled);

    /// <summary>Shift 加速模式：false=按住生效，true=按一下切换；持久化到 settings.json</summary>
    public void SetShiftToggleMode(bool enabled) => _settings.SetShiftToggleMode(enabled);

    /// <summary>开火方式：false=按住鼠标左键连发，true=按一下切换；持久化到 settings.json</summary>
    public void SetFireToggleMode(bool enabled) => _settings.SetFireToggleMode(enabled);

    // ---------------- 视角缩放（门面转发 → SettingsService） ----------------

    /// <summary>视角档位表（设置页三选，settings.json 持久化；值为相机 zoom 倍率）。
    /// zoom&gt;1 时可见世界区域 = 视口 ÷ zoom（以相机位置为中心收窄），
    /// 所有"屏幕边缘/出屏"逻辑统一走 view_world_rect() 适配。</summary>
    public Godot.Collections.Dictionary VIEW_ZOOM_LEVELS => _settings.VIEW_ZOOM_LEVELS;

    public Godot.Collections.Array<StringName> VIEW_ZOOM_ORDER => _settings.VIEW_ZOOM_ORDER;

    /// <summary>main 场景相机注册表（Main 在 _Ready/_ExitTree 维护），供可见区域计算</summary>
    public Camera2D? CameraRef
    {
        get => _settings.CameraRef;
        set => _settings.CameraRef = value;
    }

    /// <summary>切换视角档位（非法/同档忽略），持久化到 settings.json 并广播</summary>
    public void SetViewZoom(StringName level) => _settings.SetViewZoom(level);

    public double ViewZoomFactor() => _settings.ViewZoomFactor();

    /// <summary>当前可见世界区域（相机未注册时以 (960,540) 为心），margin 向外扩张。
    /// 屏幕边缘钳制 / 出屏销毁 / 刷怪位置统一以此为准；zoom=1 时即全屏 1920×1080。
    /// 物理帧内缓存：同一物理帧内多次调用（每弹/每敌/玩家/Boss）共享一次视口查询——
    /// SettingsService 转发（帧缓存逻辑在服务侧逐字保持）。</summary>
    public Rect2 ViewWorldRect(double margin = 0.0) => _settings.ViewWorldRect(margin);

    // ---------------- 窗口管理（门面转发 → SettingsService） ----------------

    /// <summary>渲染分辨率档表（设置页选项，settings.json 持久化；值为窗口逻辑尺寸，
    /// canvas_items 拉伸下逻辑坐标系恒 1920×1080，档位只改窗口物理像素=实际渲染分辨率）。</summary>
    public Godot.Collections.Dictionary RESOLUTION_LEVELS => _settings.RESOLUTION_LEVELS;

    public Godot.Collections.Array<StringName> RESOLUTION_ORDER => _settings.RESOLUTION_ORDER;

    /// <summary>切换窗口模式（窗口化/无边框全屏；非法/同值忽略）：立即应用 + 持久化 + 广播</summary>
    public void SetWindowMode(StringName mode) => _settings.SetWindowMode(mode);

    /// <summary>切换渲染分辨率档（非法/同值忽略）：立即应用 + 持久化 + 广播</summary>
    public void SetResolution(StringName preset) => _settings.SetResolution(preset);

    /// <summary>当前档位的逻辑尺寸（预设查表，custom 用 CustomWindowWidth/Height）——转发。</summary>
    public Vector2I ResolutionPointSize() => _settings.ResolutionPointSize();

    /// <summary>应用当前窗口模式 + 分辨率到实际窗口（启动补默认档调用；
    /// headless 跳过窗口 API）——一行包装（本体在 SettingsService）。</summary>
    public void ApplyWindow() => _settings.ApplyWindow();

    /// <summary>拖拽捕获（根窗口 SizeChanged 去抖后调用，返回档位是否变化）——转发。</summary>
    public bool OnWindowResized(Vector2I physicalSize) => _settings.OnWindowResized(physicalSize);

    /// <summary>视角缓存失效（切换视角/窗口档位后下一物理帧强制重算 ViewWorldRect；
    /// 本体在 SettingsService，服务内部变更档位时自行失效）——一行包装。</summary>
    public void InvalidateViewRectCache() => _settings.InvalidateViewRectCache();

    // ---------------- 瞄准辅助强度（门面转发 → SettingsService） ----------------

    /// <summary>强度档位表（设置页三选，settings.json 持久化；辅助瞄准常驻、刻意不提供关闭档）。
    /// 各档数值（辅助框内边距 frame_pad/追踪转向速率 homing_turn_rate）在 balance.json player.aim_assist.levels。</summary>
    public Godot.Collections.Array<StringName> AIM_ASSIST_ORDER => _settings.AIM_ASSIST_ORDER;

    /// <summary>切换瞄准辅助强度档位（非法/同档忽略），持久化到 settings.json 并广播</summary>
    public void SetAimAssistLevel(StringName level) => _settings.SetAimAssistLevel(level);

    /// <summary>无障碍·减少闪光：开关持久化到 settings.json 并广播（Meta HUD 据此折算色差/禁脉冲）</summary>
    public void SetReduceFlash(bool enabled) => _settings.SetReduceFlash(enabled);

    /// <summary>无障碍·高对比弹体：开关持久化到 settings.json 并广播（在飞敌弹据此换描边贴图）</summary>
    public void SetHighContrast(bool enabled) => _settings.SetHighContrast(enabled);

    /// <summary>无障碍·屏幕震动强度（0..1，0 = 完全关闭）：持久化到 settings.json，Shake 出口按此折算</summary>
    public void SetShakeScale(double value) => _settings.SetShakeScale(value);

    /// <summary>无障碍·命中顿帧强度（0..1，0 = 完全关闭）：持久化到 settings.json，顿帧请求按此折算时长</summary>
    public void SetHitStopScale(double value) => _settings.SetHitStopScale(value);

    /// <summary>无障碍·动效强度（0..1，0 = 关闭本轮战斗动效）：持久化到 settings.json，
    /// 呼吸/拍点/流光的振幅按此折算（只缩放振幅、不改频率；与减闪/震动/顿帧互不连坐）</summary>
    public void SetFxIntensity(double value) => _settings.SetFxIntensity(value);

    /// <summary>主音量（0..1）：立即应用到 Master 总线 + 持久化</summary>
    public void SetMasterVolume(double value) => _settings.SetMasterVolume(value);

    /// <summary>音乐音量（0..1）：立即应用到 BGM 总线 + 持久化</summary>
    public void SetMusicVolume(double value) => _settings.SetMusicVolume(value);

    /// <summary>音效音量（0..1）：立即应用到 SFX 总线 + 持久化</summary>
    public void SetSfxVolume(double value) => _settings.SetSfxVolume(value);

    /// <summary>世界层画面增强：开关持久化到 settings.json 并广播（WorldPostFx 据此显隐全屏增强层）</summary>
    public void SetWorldPostFx(bool enabled) => _settings.SetWorldPostFx(enabled);

    /// <summary>帧率上限档位：持久化到 settings.json + 立即应用到引擎并广播</summary>
    public void SetFpsCap(StringName level) => _settings.SetFpsCap(level);

    /// <summary>垂直同步：持久化到 settings.json + 立即应用到引擎并广播</summary>
    public void SetVSync(bool enabled) => _settings.SetVSync(enabled);

    /// <summary>把持久化的帧率上限/垂直同步应用到引擎（启动加载后调用一次）</summary>
    public void ApplyDisplaySettings() => _settings.ApplyDisplay();

    /// <summary>鼠标锁定窗口内：开关持久化到 settings.json 并广播（MouseTrap 据此决定是否拉回出框鼠标）</summary>
    public void SetMouseLock(bool enabled) => _settings.SetMouseLock(enabled);

    /// <summary>手柄设置 setter：右摇杆瞄准灵敏度（200..4000 px/s）。</summary>
    public void SetJoyAimSpeed(double value) => _settings.SetJoyAimSpeed(value);

    /// <summary>手柄设置 setter：摇杆死区（0.05..0.90，径向语义，读取侧 StickShaper 生效，不写 InputMap）。</summary>
    public void SetJoyDeadzone(double value) => _settings.SetJoyDeadzone(value);

    /// <summary>手柄震动开关：持久化（RumbleService 每次脉冲直读设置域）。</summary>
    public void SetJoyVibration(bool enabled) => _settings.SetJoyVibration(enabled);

    /// <summary>手柄设置持久化：设置页滑杆 drag_ended 调用一次（setter 不再自动写盘，防拖动写风暴）</summary>
    public void PersistJoySettings() => _settings.PersistJoySettings();

    // ---------------- 准星档案（门面转发 → SettingsService；样式单源 core CrosshairProfile） ----------------

    /// <summary>档案簿条目（列表展示；名字存文案键，显示口 Tr 过——无翻译命中原样返回）。</summary>
    public IReadOnlyList<Core.Hud.CrosshairEntry> CrosshairProfiles => _settings.CrosshairProfiles;

    public int CrosshairActiveIndex => _settings.CrosshairActiveIndex;

    /// <summary>当前生效样式（AimCrosshair 每帧读一份引用，设置页改动即时生效）。</summary>
    public Core.Hud.CrosshairProfile ActiveCrosshair => _settings.ActiveCrosshair;

    public void SetCrosshairActive(int index) => _settings.SetCrosshairActive(index);

    /// <summary>编辑器写回（不落盘；滑杆拖动结束统一走 PersistCrosshairSettings 防写风暴）。</summary>
    public void UpdateCrosshairProfile(int index, Core.Hud.CrosshairProfile profile) =>
        _settings.UpdateCrosshairProfile(index, profile);

    public void PersistCrosshairSettings() => _settings.PersistCrosshairSettings();

    public void AddCrosshairProfile(string name, Core.Hud.CrosshairProfile profile) =>
        _settings.AddCrosshairProfile(name, profile);

    public void RemoveCrosshairProfile(int index) => _settings.RemoveCrosshairProfile(index);

    public void RenameCrosshairProfile(int index, string name) => _settings.RenameCrosshairProfile(index, name);

    /// <summary>准星码导入（校验失败返回错误档不落档，成功＝追加新档并激活并落盘）。</summary>
    public Core.Hud.CrosshairCodeResult ImportCrosshairCode(string code) => _settings.ImportCrosshairCode(code);

    // ---------------- 健康/增幅 域（门面转发 → CombatStateService） ----------------

    /// <summary>生命上限：基础 100 + extra_life 每层 +50（对齐原作 EXTRA_LIFE_BONUS_HP）
    /// 基础值 _apply_balance 缓存，热路径免 cfg 路径解析（extra_life 层数查询 O(1)）——CombatStateService 转发。</summary>
    public double MaxHealth() => _combat.MaxHealth();

    public void LoseHealth(double amount = 1.0) => _combat.LoseHealth(amount);

    /// <summary>治疗（单点封顶 max_health，调用侧不再各自判断）</summary>
    public void Heal(double amount) => _combat.Heal(amount);

    /// <summary>吸血增幅：击杀回复 基础上限 × 5% 取整（定额，core LifestealHeal），每帧至多结算一次</summary>
    public void TryLifesteal() => _combat.TryLifesteal();

    public int AugmentLevel(StringName id) => _combat.AugmentLevel(id);

    /// <summary>消耗一层增幅（护盾等一次性层；无剩余层返回 false；层数变动广播 augments_changed）
    /// 注：层级写入唯一入口 = TalentService。</summary>
    public bool ConsumeAugment(StringName id) => _combat.ConsumeAugment(id);

    // ---------------- 语言（中英双语；门面转发 → SettingsService） ----------------

    /// <summary>当前语言（"zh"/"en"，settings.json 持久化）——SettingsService 转发。</summary>
    public string Locale
    {
        get => _settings.Locale;
        set => _settings.Locale = value;
    }

    public void SetLocale(string pLocale) => _settings.SetLocale(pLocale);

    // ---------------- 恢复默认（设置页「全部恢复默认」） ----------------

    /// <summary>全部设置回到出厂默认并落盘：键位/难度/画质/音频/无障碍/手柄全量复位。
    /// 刻意保留 `tutorial_done` 与 `tutorial_stage`（教程进度不是偏好设置，复位它等于让玩家重看教程）。
    /// 只做内存复位 + 重放必要的运行期副作用（键位/开火动作/手柄装配/显示/窗口/音量/换语言/
    /// 视角与辅瞄与减闪与画面增强的事件回放——服务侧 ResetToDefaultsAndBroadcast 只对变化过的项补发）。</summary>
    public void ResetAllSettings()
    {
        var tutorialDone = TutorialDone;
        var tutorialStage = TutorialStage;
        // 键位与难度各有独立事实源：先复位它们，再重置设置域字段并重放副作用
        ResetKeyBindings();
        SetDifficulty(new StringName("medium")); // 走玩家改档正口：校验/落盘/广播，已在 medium 时幂等早退
        // 语言要先记下复位前的值：ResetToDefaults 已把 Locale 直写成 "zh"，复位后再拿它做条件
        // 恒为假——SetLocale 永不执行、LocaleChanged 永不发射，英文档点「全部恢复默认」后
        // 设置页标题/导航/分组与其余订阅方（HUD/暂停/基地/天赋/确认弹窗）全部停在旧语言，
        // 且不会自愈（LocaleChanged 只在玩家手动切语言时才发）。
        var prevLocale = _settings.Locale;
        // 复位走带广播的口（视角/辅瞄/减闪/画面增强的消费方都是「_Ready 读一次 + 信号刷新」的
        // 缓存型；无变化时不发信号，不会造成多余重建）
        _settings.ResetToDefaultsAndBroadcast();
        TutorialDone = tutorialDone;
        TutorialStage = tutorialStage;
        // 语言经 SetLocale 重放（走 LocaleChanged 广播，设置页据此整页重建文案）；
        // TranslationServer 已由其内部写，避免此处再写一次
        if (prevLocale != _settings.Locale)
        {
            SetLocale(_settings.Locale);
        }
        else
        {
            TranslationServer.SetLocale(_settings.Locale);
        }

        _input.ApplyKeyBindings();
        _input.EnsureFireBinding();
        _input.BindJoypadDefaults();
        _settings.ApplyDisplay();
        _settings.ApplyWindow();
        _settings.ApplyVolumes();
        // 手感域同步：ResetToDefaults 直写字段不发 setter 事件，漏这一步会「界面显示已恢复、
        // 实际仍按旧的关闭档运行」（顿帧/震动强度为 0 时手感域直接忽略请求）
        SyncHitStopScale(_settings.HitStopScale);
        // 手柄设置同理：ResetToDefaults 直写 JoyAimSpeed/JoyDeadzone 不发服务事件，而 Player 的
        // 灵敏度只在事件里赋值——不补发则「全部恢复默认」后存档/设置页回到默认，游戏内仍按旧档跑。
        _settings.EmitJoySettingsChanged();
        SaveSettings();
    }

    // ---------------- 设置域持久化桥（ApplySettingsDict 公开转发；
    // CollectSettingsDict 仍在 GameState.Save.cs 内部直调服务，无需门面） ----------------

    /// <summary>设置字段应用（user://settings.json 读入的设置字典；含键位/窗口/视图缓存副作用）
    /// ——本体在 SettingsService。</summary>
    public void ApplySettingsDict(Godot.Collections.Dictionary data) => _settings.ApplySettingsDict(data);
}
