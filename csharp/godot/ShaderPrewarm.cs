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

    private bool _drawn;

    public override void _Ready()
    {
        Layer = CoverLayer;
        var seenShaders = new HashSet<ulong>();
        var mats = new List<Material>();
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
