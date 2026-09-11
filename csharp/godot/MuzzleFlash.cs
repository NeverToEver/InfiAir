using Godot;

namespace InfiAir;

/// <summary>
/// 开火口闪光：单发 additive 软点，点亮后约 0.06s 淡出（effects.muzzle_flash.time）。
/// 每个发射源常驻一盏复用（BossFire 一阀一盏，高频开火只重复点亮，零分配零泄漏）；
/// 玩家侧由 Player._muzzleGlow 同构实现（挂在机体上随机头朝向）。
/// 挂 boss 节点下用局部坐标，随机体移动不脱节。
/// </summary>
public partial class MuzzleFlash : Sprite2D
{
    private float _total = 0.06f;
    private float _life;
    private float _baseScale = 0.5f;
    private Color _color = new(1.0f, 0.4f, 0.55f);

    /// <summary>常驻挂载（懒建一次）；返回实例供每次开火 Flash()。</summary>
    public static MuzzleFlash Attach(Node parent)
    {
        var f = new MuzzleFlash();
        parent.AddChild(f);
        return f;
    }

    public override void _Ready()
    {
        Texture = CinematicFx.SoftTexture();
        Material = CinematicFx.AdditiveMaterial();
        ZIndex = 1;
        _total = Mathf.Max((float)GameState.Instance.Cfg("effects.muzzle_flash.time", _total).AsDouble(), 0.02f);
        var size = (float)GameState.Instance.Cfg("effects.muzzle_flash.size", 30.0).AsDouble()
            * (float)GameState.Instance.WorldScale;
        _baseScale = size / CinematicFx.SoftTexSize;
        Visible = false;
    }

    /// <summary>点亮一次（局部坐标 + 阵营色）；高频开火反复调用只重置寿命，不增节点。</summary>
    public void Flash(Vector2 localPos, Color color)
    {
        Position = localPos;
        _color = color;
        Modulate = color;
        _life = _total;
        Scale = Vector2.One * (_baseScale * 1.2f);
        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _life -= (float)delta;
        if (_life <= 0.0f)
        {
            Visible = false;
            return;
        }

        // 急速收缩 + 线性淡出（爆点收束感）
        var p = _life / _total;
        Scale = Vector2.One * (_baseScale * (0.7f + 0.5f * p));
        Modulate = new Color(_color.R, _color.G, _color.B, _color.A * p);
    }
}
