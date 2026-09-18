using System;
using System.Collections.Generic;
using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 启动期着色器预热：把开机那一轮已建好、却等不到绘制的着色器材质各画一帧，
/// 让它们的编译成本落在开机黑场里，而不是玩家按下开始那一帧上。
///
/// 为什么需要：Compatibility 后端没有 ubershader，着色器**首次被绘制时**才编译。开机链路是
/// `main.tscn` 装载（`Main._Ready` 建好 world_grade / meta_health / ship_energy / nebula 等材质）
/// → 同一帧末切标题屏；这些材质在切场景前没有任何绘制机会，于是编译落到「标题屏 → 开局」那一帧，
/// 正是 <see cref="FrameCache.MaxStepDelta"/> 要兜住的那种巨帧。同机实测（M2 / Metal）：真实渲染器下
/// 首帧绘制约 110ms、无头 dummy 约 15ms，差值即编译成本；OpenGL 后端的编译通常更贵。
///
/// 口径与边界：
///   - **材质取自场景树并按 Shader 去重**，不另立着色器路径清单——清单是第二处事实源，新增着色器
///     时会漂。一张都扫不到时 PushError：静默空转等于预热没做而启动照常。
///   - **扫不到的材质走登记面**（<see cref="RegisterExtraSeed"/>）：懒创建池化的材质（弹体）在开机
///     那一刻不在树上，由创建方登记后与场景树材质合流、同样按 Shader 去重预热。
///   - 只画一帧：1×1 代理面片贴在左上角，整屏黑盖（本节点自成一图层）压在上面——玩家看到的是
///     开机黑场而不是一帧战场，且与标题屏的黑场淡入同色，衔接无跳变。
///   - **有意留白两处**：MetaHealthFX 的 SubViewport 烘焙材质（开局首帧才创建，按既有设计本就是
///     一次显式单帧成本）与引擎内建的粒子着色器（首批爆炸时编译）——两者都不在场景树的
///     ShaderMaterial 面上。
///   - 本局尚未开始，预热不触碰任何状态机；无头（dummy 渲染）下照走，收益只能在真实渲染器上过目。
/// </summary>
public partial class ShaderPrewarm : CanvasLayer
{
    /// <summary>黑盖所在图层：高于世界与 HUD，只为掩住预热帧。</summary>
    private const int CoverLayer = 128;

    /// <summary>预热帧绘制完成（已至少画过一帧）后触发；调用方在此接手后续流程（开机交接标题屏）。</summary>
    public event Action? Completed;

    /// <summary>额外预热材质（不在开机场景树里的那些）。</summary>
    private static readonly List<WeakReference<ShaderMaterial>> ExtraSeeds = new();

    /// <summary>登记去重用的材质 id（GetInstanceId）：池化材质按阵营/外观各建一张时避免重复登记。</summary>
    private static readonly HashSet<ulong> ExtraSeedIds = new();

    /// <summary>预热已跑过一轮（开机黑场那一帧）：此后 <see cref="RegisterExtraSeed"/> 一律空操作。
    /// 为什么必须停收（不只是清表）：预热只在**首局**开机跑一次，而重开局/回标题屏每局都会重建
    /// 节奏服务、每次都登记一张新材质——表只进不出，几局下来两份静态容器无界增长
    /// （WeakReference 本身不延长材质寿命，泄漏的是容器条目与 id 集合）。
    /// 预热帧已经画过全部着色器，之后登记的材质本就没有可补的编译成本——空操作不是丢功能。</summary>
    private static bool _prewarmed;

    private bool _drawn;

    /// <summary>
    /// 把不在开机场景树里的材质纳入预热（只收 <see cref="ShaderMaterial"/>：预热面就是着色器编译，
    /// 主场景树的收集面同此口径）。弹体材质是懒创建池化的——第一次开火前它不在树上，预热扫不到，
    /// 于是着色器编译落在「第一发开火」那一帧上，正是 <see cref="FrameCache.MaxStepDelta"/> 要兜住的
    /// 那种巨帧，不注册就会当场顿一下。静态只持**弱引用**：材质本体由持有方（对象池）保管，本机制不
    /// 延长其生命周期——静态强持 Godot 对象会在引擎退出 finalize 期 segfault（本仓库踩过），
    /// 且未走预热路径的场景（标题屏 / 练习 / 从标题屏直入）根本不会清空这张表。
    /// 登记须早于开机那一轮的预热：预热已跑完后登记是空操作（不排队、不补画，且静态登记表不再增长）。
    /// </summary>
    public static void RegisterExtraSeed(ShaderMaterial material)
    {
        if (_prewarmed)
        {
            return; // 预热只发生一次；之后的重开局登记没有可补的编译成本，只会有界地撑大静态容器
        }

        if (material == null || !GodotObject.IsInstanceValid(material))
        {
            return;
        }

        if (ExtraSeedIds.Add(material.GetInstanceId()))
        {
            ExtraSeeds.Add(new WeakReference<ShaderMaterial>(material));
        }
    }

    public override void _Ready()
    {
        Layer = CoverLayer;
        var seenShaders = new HashSet<ulong>();
        var mats = new List<Material>();
        // 注册面先合并（scene 树收集会按 shader 去重，两边合流后各 shader 只画一次）
        foreach (var weak in ExtraSeeds)
        {
            if (weak.TryGetTarget(out var extra) && GodotObject.IsInstanceValid(extra)
                && extra.Shader != null && seenShaders.Add(extra.Shader.GetInstanceId()))
            {
                mats.Add(extra);
            }
        }

        ExtraSeeds.Clear();
        ExtraSeedIds.Clear(); // 静态不留 Godot 引用（也不会跨场景残留）
        _prewarmed = true;    // 本轮之后一律停收（见 RegisterExtraSeed）
        Collect(GetTree().Root, seenShaders, mats);
        foreach (var mat in mats)
        {
            AddChild(new ColorRect
            {
                Color = Colors.Black,
                Size = new Vector2(1.0f, 1.0f),
                Material = mat,
            });
        }

        // 黑盖最后加：同图层内绘制顺序即树序，盖在面片之上
        var cover = new ColorRect { Color = Colors.Black };
        cover.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(cover);

        if (mats.Count == 0)
        {
            GD.PushError("[boot] 着色器预热没扫到任何材质——预热实际未发生（启动照常、编译成本仍落在开局首帧）");
        }

        GD.Print(GdFormat.Format("[boot] 着色器预热绘制 %d 张材质", mats.Count));
    }

    public override void _Process(double delta)
    {
        if (!_drawn)
        {
            // 本帧留给绘制：面片在这一帧的绘制阶段被画到，着色器在此编译
            _drawn = true;
            return;
        }

        Completed?.Invoke();
        QueueFree();
    }

    private static void Collect(Node node, HashSet<ulong> seenShaders, List<Material> outMats)
    {
        if (node is CanvasItem item && item.Material is ShaderMaterial mat && mat.Shader != null
            && seenShaders.Add(mat.Shader.GetInstanceId()))
        {
            outMats.Add(mat);
        }

        foreach (var child in node.GetChildren())
        {
            Collect(child, seenShaders, outMats);
        }
    }
}
