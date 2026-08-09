using Godot;

namespace InfiAir;

/// <summary>
/// 玩家视觉职责聚合（M3c 全量迁移，2026-08-08 自 scripts/player_visuals.gd 迁移）：尾焰、
/// 冲刺残影池、机身色调（弹反金/擦弹金/无敌闪烁）、受击点脉动、弹反盾视觉、擦弹闪光状态。
/// 组合委托模式（同 PlayerDamage/PlayerDash/PlayerParry）：不持有节点所有权，经 player
/// 传入的节点引用操作；公开接口供 player 帧驱动与外部（player_dash 残影入口）调用。
/// 拆分动机：player.gd 视觉与战斗逻辑解耦（AUDIT_VAULT A8；DESIGN_BASELINE §7.1）。
/// 源文件 extends RefCounted 且无信号/导出 → 纯 C# 类（不继承 GodotObject）；仅 player.gd
/// （随 M3c 批次迁 C#）调用。Enemy.SinFast（M3b 已迁）为同命名空间静态方法，直接引用。
/// </summary>
public class PlayerVisuals
{
    /// <summary>P1-5：冲刺残影小池（预建复用，替代逐次 new Sprite2D + Tween + queue_free）。</summary>
    private const int AfterimagePoolSize = 4;
    private const float AfterimageFadeTime = 0.3f;
    private static readonly Color AfterimageColor = new(0.5f, 0.9f, 1.0f, 0.5f);

    private Sprite2D _sprite = null!;
    private GpuParticles2D _thruster = null!;
    private Polygon2D _hitboxDot = null!;
    private Polygon2D _parryArc = null!;
    private Polygon2D _parryShine = null!;

    /// <summary>2026-08-09 审计：弹反高光带顶点缓冲预分配（UpdateParryVisuals 每物理帧原地写，防 new Vector2[6]）。</summary>
    private readonly Vector2[] _parryShinePoly = new Vector2[6];

    /// <summary>BODY_TINT_BASE 迁入（可视性增强提亮青白）。</summary>
    private static readonly Color BodyTintBase = new(1.35f, 1.4f, 1.55f);
    /// <summary>擦弹机身短闪光剩余时长（金色微闪，独立短计时；SetGrazeFlash 置位、UpdateFrame 递减）。</summary>
    private float _grazeFlash;
    private readonly System.Collections.Generic.List<Sprite2D> _afterimagePool = new();
    private int _afterimageIdx;
    private readonly System.Collections.Generic.List<Sprite2D> _activeAfterimages = new();

    /// <summary>
    /// 初始化：接收节点引用 + 预建残影池。world_root = Main（残影固定世界坐标，不随玩家移动；
    /// Main 场景构建期 add_child 会报 "busy setting up children"，延迟到帧末执行——原 _ready 逻辑迁移）。
    /// </summary>
    public void Init(
        Sprite2D sprite, GpuParticles2D thruster, Polygon2D hitboxDot, Polygon2D parryArc, Polygon2D parryShine,
        Node worldRoot)
    {
        Init(sprite, thruster, hitboxDot, parryArc, parryShine, worldRoot, AfterimagePoolSize);
    }

    public void Init(
        Sprite2D sprite, GpuParticles2D thruster, Polygon2D hitboxDot, Polygon2D parryArc, Polygon2D parryShine,
        Node worldRoot, int poolSize)
    {
        _sprite = sprite;
        _thruster = thruster;
        _hitboxDot = hitboxDot;
        _parryArc = parryArc;
        _parryShine = parryShine;
        for (var i = 0; i < poolSize; i++)
        {
            var ghost = new Sprite2D { Visible = false, Modulate = AfterimageColor };
            worldRoot.CallDeferred(Node.MethodName.AddChild, ghost);
            _afterimagePool.Add(ghost);
        }
    }

    /// <summary>尾焰档位应用（冲刺/加速/巡航/静止五处共用；engine_tint 由 player 传入——buff 外观
    /// 写入 player.engine_tint，公开字段被 PlayerBuffVisuals/测试访问，留在 player 侧）。</summary>
    public void SetThruster(float speedScale, float amountRatio, float alpha, Color engineTint)
    {
        _thruster.SpeedScale = speedScale;
        _thruster.AmountRatio = amountRatio;
        _thruster.SelfModulate = new Color(1.0f, 1.0f, 1.0f, alpha) * engineTint;
    }

    /// <summary>残影生成（player_dash 冲刺时经 player.spawn_afterimage 转发）：复用池节点；
    /// 同一节点淡出中被再次冲刺命中时 alpha 重置重新淡出。</summary>
    public void SpawnAfterimage(Texture2D spriteTexture, Vector2 spriteScale, Vector2 gpos, float rot)
    {
        SpawnAfterimage(spriteTexture, spriteScale, gpos, rot, AfterimageColor);
    }

    public void SpawnAfterimage(Texture2D spriteTexture, Vector2 spriteScale, Vector2 gpos, float rot, Color color)
    {
        var ghost = _afterimagePool[_afterimageIdx];
        _afterimageIdx = (_afterimageIdx + 1) % _afterimagePool.Count;
        ghost.Texture = spriteTexture;
        ghost.Scale = spriteScale;
        ghost.GlobalPosition = gpos;
        ghost.GlobalRotation = rot;
        ghost.Modulate = color;
        ghost.Visible = true;
        if (!_activeAfterimages.Contains(ghost))
        {
            _activeAfterimages.Add(ghost);
        }
    }

    /// <summary>残影淡出推进（player._process 每帧调用；池内每节点 alpha 线性衰减，归零隐藏）。</summary>
    public void UpdateAfterimages(float delta)
    {
        if (_activeAfterimages.Count == 0)
        {
            return;
        }

        var i = 0;
        while (i < _activeAfterimages.Count)
        {
            var g = _activeAfterimages[i];
            var m = g.Modulate;
            m.A -= delta / AfterimageFadeTime;
            g.Modulate = m;
            if (m.A <= 0.0f)
            {
                g.Visible = false;
                _activeAfterimages.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>机身色调四源（优先级从高到低）：弹反金 tint &gt; 擦弹金色微闪 &gt; 无敌帧闪烁 &gt; 常态基底。
    /// 擦弹闪光在此递减（原 _physics_process 视觉分支）；无敌倒计时递减留在 player（战斗状态）。
    /// 受击点光点脉动同帧驱动（常亮低频闪烁，提示实际受击判定位置）。</summary>
    public void UpdateFrame(float delta, float parryTint, float invincible, long nowMs)
    {
        if (parryTint > 0.0f)
        {
            _sprite.Modulate = BodyTintBase.Lerp(new Color(1.7f, 1.25f, 0.5f), parryTint);
        }
        else if (_grazeFlash > 0.0f)
        {
            _grazeFlash -= delta;
            _sprite.Modulate = BodyTintBase.Lerp(new Color(1.7f, 1.35f, 0.5f), 1.0f);
        }
        else if (invincible > 0.0f)
        {
            var m = BodyTintBase;
            m.A = 0.35f + 0.65f * Mathf.Abs(Enemy.SinFast((float)(nowMs * 0.02)));
            _sprite.Modulate = m;
        }
        else
        {
            _sprite.Modulate = BodyTintBase;
        }

        var hd = _hitboxDot.Modulate;
        hd.A = 0.45f + 0.55f * Mathf.Abs(Enemy.SinFast((float)(nowMs * 0.006)));
        _hitboxDot.Modulate = hd;
    }

    /// <summary>擦弹机身金色短闪置位（_on_graze_entered 反馈三件套之一；时长 balance player.graze.flash_time）。</summary>
    public void SetGrazeFlash(float time) => _grazeFlash = time;

    /// <summary>盾视觉逐物理帧驱动：WINDUP 小弧展开到全弧（缩放）、ACTIVE 珍珠流光自弧线左端扫至右端、
    /// RECOVER 保持全弧、IDLE 隐藏；高光带角度按 active 进度线性插值（零 shader 依赖）。
    /// 参数化（expand/shine 来自 PlayerParry，radius/arc 来自 player 常量）——视觉不感知 parry 组件。</summary>
    public void UpdateParryVisuals(float expand, float shine, float radius, float arcDeg)
    {
        _parryArc.Visible = expand > 0.0f;
        if (!_parryArc.Visible)
        {
            _parryShine.Visible = false;
            return;
        }

        var scale = 0.3f + 0.7f * expand;
        _parryArc.Scale = Vector2.One * scale;
        _parryShine.Visible = shine > 0.0f;
        if (!_parryShine.Visible)
        {
            return;
        }

        var arc = Mathf.DegToRad(arcDeg) * 0.5f;
        var centerA = -Mathf.Pi / 2.0f - arc + 2.0f * arc * shine;
        var w = Mathf.DegToRad(22.0f); // 高光带角宽
        var sp = _parryShinePoly; // 2026-08-09：预分配复用，防每物理帧 new Vector2[6]
        sp[0] = Vector2.Zero;
        for (var i = 0; i < 5; i++)
        {
            var a = centerA - w + 2.0f * w * i / 4.0f;
            sp[i + 1] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * scale;
        }

        _parryShine.Polygon = sp;
    }

}
