using Godot;

namespace InfiAir;

/// <summary>
/// 迁移期 GameState（GDScript autoload）动态访问桥（M3 建立，2026-08-08）。
/// GameState 本体推迟到 M7 迁移，期间 C# 实体经本桥访问其方法/属性/信号
/// （跨语言动态派发，snake_case 名称——C# 调 GDScript 的约定）。
/// 热路径纪律：每帧访问须缓存（如 Bullet 的静态每帧 view rect 缓存），禁止逐帧走本桥。
/// M7 后删除，改 typed 访问（GetNode&lt;GameState&gt;("/root/GameState")）。
/// </summary>
public static class GameStateBridge
{
    /// <summary>GameState autoload 节点（root 下名为 GameState）。</summary>
    public static GodotObject? Instance
    {
        get
        {
            var tree = (SceneTree?)Engine.GetMainLoop();
            return tree?.Root?.GetNodeOrNull("GameState");
        }
    }

    public static Variant Call(StringName method, params Variant[] args)
    {
        return Instance!.Call(method, args);
    }

    public static Variant Get(StringName property)
    {
        return Instance!.Get(property);
    }

    public static void Set(StringName property, Variant value)
    {
        Instance!.Set(property, value);
    }
}
