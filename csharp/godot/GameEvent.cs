using Godot;

namespace InfiAir;

/// <summary>
/// 通用游戏事件基类（纯生命周期接口，零系统耦合，2026-08-05 接口方向重构；M 批次全量迁移）。
/// 这是「事件」的唯一基底：编排器（GameEventManager）负责注册/选择/统一计时/结束清理；
/// 本类只定义事件自身的生命周期契约与上下文注入：
///   - context：执行上下文 Dictionary（编排器注入；本类存浅拷贝隔离，键约定由各系统
///     的中间层基类（如 FogEvent）定义，本类不解析任何键）；
///   - duration：编排器给定的持续时间（秒，≤0 钳制为 0）；
///   - is_active：生命周期守卫（start → tick… → end；end 幂等、tick 仅活跃期派发）。
/// 子类实现点（模板方法）：event_id() 必实现；_on_start()/_on_tick()/_on_end() 可选。
/// 契约（健壮性，2026-08-05 审计）：
///   - start()/tick()/end() 为模板方法，子类不得覆盖（实现点用 _on_* 钩子）；
///   - start() 可安全重复调用：先内部清理旧效果（_on_end）再注入新上下文重启，
///     不会叠加半状态（子类 _on_start 无需为重复调用特判）；
///   - end() 幂等；tick() 仅活跃期派发；context 浅拷贝隔离编排器后续修改；
///   - duration ≤ 0 钳制为 0（编排器侧另有下限，双保险）。
/// 迁移期：GDScript 测试经 load("res://csharp/godot/GameEvent.cs").new() 实例化 → RefCounted 基链；
/// 公开 API 保持 snake_case 别名桥（M7 删除）。
/// </summary>
public partial class GameEvent : RefCounted
{
    /// <summary>执行上下文（编排器注入；start 时浅拷贝，事件持有的引用不受编排器后续修改影响）。
    /// 通用键约定（GameEvent 自身定义，各系统中间层另定义系统键）：
    /// "request_end": Callable —— 编排器的提前结束回调（事件可主动 request_end()）。</summary>
    public Godot.Collections.Dictionary Context { get; set; } = new();

    /// <summary>编排器给定的持续时间（秒）。</summary>
    public float Duration { get; set; }

    /// <summary>生命周期守卫（end 幂等、tick 仅活跃期派发）。</summary>
    public bool IsActive { get; set; }

    /// <summary>事件唯一 id（注册表键，如 &amp;"fake_enemies"）；子类必须实现。</summary>
    public virtual StringName EventId()
    {
        GD.PushError($"GameEvent.event_id() 未实现：{GetScript()}");
        return new StringName();
    }

    /// <summary>通用 context 读取辅助（宽容性：简单事件无需了解系统访问器，一行读自定义数据）。</summary>
    public Variant GetCtx(StringName key) => Context.GetValueOrDefault(key, new Variant());

    /// <summary>通用 context 读取辅助（键缺失/类型不符返回 default，不抛错）。</summary>
    public Variant GetCtx(StringName key, Variant def) => Context.GetValueOrDefault(key, def);

    /// <summary>请求提前结束（宽容性：复杂事件在内部目标达成后可主动请求编排器结束）。
    /// 经 context 的 "request_end" 回调（编排器注入）实现；回调缺失/无效时降级警告，
    /// 事件继续按 duration 自然结束。注：从 _on_tick 内调用时，当前 tick 帧的剩余代码
    /// 仍会执行完（end 幂等，编排器结束信号随后发出）。</summary>
    public void RequestEnd()
    {
        var cb = Context.GetValueOrDefault("request_end", new Variant());
        if (cb.VariantType == Variant.Type.Callable && cb.AsCallable().Target is not null)
        {
            cb.AsCallable().Call();
        }
        else
        {
            GD.PushWarning("GameEvent.request_end：context 缺 request_end 回调，事件将按 duration 自然结束");
        }
    }

    /// <summary>由编排器调用：注入上下文并启动。重复调用安全（先清理旧效果再重启）。
    /// 子类在 _on_start 应用效果，勿覆盖本方法。</summary>
    public void Start(Godot.Collections.Dictionary pContext, float pDuration)
    {
        if (IsActive)
        {
            OnEnd(); // 自愈：重复 start 先清理旧状态，防 _on_start 叠加
        }

        Context = pContext.Duplicate(); // 浅拷贝：编排器复用/修改字典不污染本事件
        Duration = Mathf.Max(pDuration, 0.0f);
        IsActive = true;
        OnStart();
    }

    /// <summary>事件进行中每帧驱动（编排器调用；子类实现 _on_tick，勿覆盖本方法）。</summary>
    public void Tick(float delta)
    {
        if (IsActive)
        {
            OnTick(delta);
        }
    }

    /// <summary>结束并清理（幂等：编排器可能在 duration 前因返航/死亡提前调用；子类实现 _on_end）。</summary>
    public void End()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        OnEnd();
    }

    // ---------------- 子类实现点（模板方法） ----------------

    protected virtual void OnStart()
    {
    }

    protected virtual void OnTick(float delta)
    {
    }

    protected virtual void OnEnd()
    {
    }

    // ---------------- GDScript 鸭子调用兼容桥（M 批次过渡，M7 删除） ----------------
    // 原 GDScript 公开 var/方法（snake_case）别名转发；GDScript 测试 load(.cs) 后经动态
    // 派发访问。C# 内部调用一律 PascalCase。

    public Godot.Collections.Dictionary context { get => Context; set => Context = value; }

    public float duration { get => Duration; set => Duration = value; }

    public bool is_active { get => IsActive; set => IsActive = value; }

    public StringName event_id() => EventId();

    public Variant get_ctx(StringName key) => GetCtx(key);

    public Variant get_ctx(StringName key, Variant def) => GetCtx(key, def);

    public void request_end() => RequestEnd();

    public void start(Godot.Collections.Dictionary pContext, float pDuration) => Start(pContext, pDuration);

    public void tick(float delta) => Tick(delta);

    public void end() => End();
}
