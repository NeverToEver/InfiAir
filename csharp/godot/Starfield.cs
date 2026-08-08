using Godot;

namespace InfiAir;

/// <summary>
/// 程序化双层视差滚动星空背景（M1 全量迁移样板，2026-08-08 自 scripts/starfield.gd 迁移）。
/// P1-4 合批保持：每层单条 draw_multiline（星点 1px 短线段 + 线宽，视觉等价圆点）；
/// _Process 原地写 PackedVector2Array，零每帧分配（热路径红线）。
/// C07/M5 保持：星点范围随可见世界区域 view_world_rect（尺寸 + 锚点，zoom>1 时锚点
/// 随可见区平移，回绕同基线）；R07 判型 + 非负钳制保持。
/// </summary>
public partial class Starfield : Node2D
{
    private int _farCount = 140;
    private int _nearCount = 90;
    private float _farSpeed = 60.0f;
    private float _nearSpeed = 140.0f;

    private Vector2[] _far = System.Array.Empty<Vector2>();
    private Vector2[] _near = System.Array.Empty<Vector2>();
    private Vector2[] _farLines = System.Array.Empty<Vector2>(); // Godot C#：PackedVector2Array → Vector2[]
    private Vector2[] _nearLines = System.Array.Empty<Vector2>();
    private const float LineLen = 1.0f;

    /// <summary>返航过场的星光拉伸倍率，随时间衰减回 1（过场导演 warp() 设置）。</summary>
    public float WarpFactor { get; private set; } = 1.0f;

    /// <summary>C07 修复：可见世界区域尺寸缓存（view_world_rect），替代硬编码 1920×1080。</summary>
    private Vector2 _areaSize = new(1920.0f, 1080.0f);

    /// <summary>M5 审计：星点区域锚点（_ready 时可见区左上角），随可见区平移，回绕同基线。</summary>
    private Vector2 _origin = Vector2.Zero;

    /// <summary>A7：测试/诊断白盒断言经公开接口（M5 断言星空覆盖区域）。</summary>
    public Vector2 Origin() => _origin;

    public Vector2 AreaSize() => _areaSize;

    public void Warp(float factor) => WarpFactor = factor;

    public override void _Ready()
    {
        ZIndex = -10;
        // R07：判型 + 非负钳制（L 系列判型族登记遗留）——字符串/负数手改配置不崩、不做负尺寸 resize
        var gameState = GetNode("/root/GameState");
        var fc = gameState.Call("cfg", "effects.starfield.far_count", _farCount);
        if (fc.VariantType == Variant.Type.Int && fc.AsInt64() >= 0)
        {
            _farCount = (int)fc.AsInt64();
        }

        var nc = gameState.Call("cfg", "effects.starfield.near_count", _nearCount);
        if (nc.VariantType == Variant.Type.Int && nc.AsInt64() >= 0)
        {
            _nearCount = (int)nc.AsInt64();
        }

        var fs = gameState.Call("cfg", "effects.starfield.far_speed", _farSpeed);
        if (fs.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            _farSpeed = (float)fs.AsDouble();
        }

        var ns = gameState.Call("cfg", "effects.starfield.near_speed", _nearSpeed);
        if (ns.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            _nearSpeed = (float)ns.AsDouble();
        }

        // C07：星点范围随可见世界区域而非写死 1920×1080；M5：区域锚点 = 可见区左上角
        var rng = new RandomNumberGenerator();
        rng.Seed = 12345;
        var view = gameState.Call("view_world_rect").AsRect2();
        _areaSize = view.Size;
        _origin = view.Position;
        _far = new Vector2[_farCount];
        _near = new Vector2[_nearCount];
        for (int i = 0; i < _farCount; i++)
        {
            _far[i] = new Vector2(_origin.X + rng.Randf() * _areaSize.X, _origin.Y + rng.Randf() * _areaSize.Y);
        }

        for (int i = 0; i < _nearCount; i++)
        {
            _near[i] = new Vector2(_origin.X + rng.Randf() * _areaSize.X, _origin.Y + rng.Randf() * _areaSize.Y);
        }

        // P1-4：线段数组一次性分配（每星 2 点：起点 + 1px 尾端）
        _farLines = new Vector2[_farCount * 2];
        _nearLines = new Vector2[_nearCount * 2];
    }

    public override void _Process(double delta)
    {
        WarpFactor = Mathf.Lerp(WarpFactor, 1.0f, 1.5f * (float)delta);
        var wrapY = _origin.Y + _areaSize.Y; // M5：回绕基线随区域锚点（zoom>1 时非 0）
        for (int i = 0; i < _far.Length; i++)
        {
            var p = _far[i] + new Vector2(0.0f, _farSpeed * WarpFactor * (float)delta);
            if (p.Y > wrapY)
            {
                p.Y -= _areaSize.Y;
            }

            _far[i] = p;
            _farLines[i * 2] = p;
            _farLines[i * 2 + 1] = p + new Vector2(LineLen, 0.0f);
        }

        for (int i = 0; i < _near.Length; i++)
        {
            var p = _near[i] + new Vector2(0.0f, _nearSpeed * WarpFactor * (float)delta);
            if (p.Y > wrapY)
            {
                p.Y -= _areaSize.Y;
            }

            _near[i] = p;
            _nearLines[i * 2] = p;
            _nearLines[i * 2 + 1] = p + new Vector2(LineLen, 0.0f);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        // P1-4：每层单条 draw_multiline 合批（230 条绘制指令 → 2 条）；线宽对应原圆直径
        DrawMultiline(_farLines, new Color(0.7f, 0.75f, 0.9f, 0.6f), 3.0f);
        DrawMultiline(_nearLines, new Color(1.0f, 1.0f, 1.0f, 0.9f), 5.0f);
    }
}
