using Godot;

namespace InfiAir;

/// <summary>
/// 玩家视觉职责聚合：尾焰、
/// 冲刺残影池、机身色调（弹反金/擦弹金/无敌闪烁）、受击点脉动、弹反盾视觉、擦弹闪光状态。
/// 组合委托模式（同 PlayerDamage/PlayerDash/PlayerParry）：不持有节点所有权，经 Player
/// 传入的节点引用操作；公开接口供 Player 帧驱动与外部（PlayerDash 残影入口）调用。
/// 拆分动机：Player 视觉与战斗逻辑解耦（DESIGN_BASELINE §7.1）。
/// 无信号/导出 → 纯 C# 类（不继承 GodotObject）；仅 Player 调用。Enemy.SinFast 为同命名空间
/// 静态方法，直接引用。
/// </summary>
public class PlayerVisuals
{
    /// <summary>冲刺残影小池（预建复用，替代逐次 new Sprite2D + Tween + queue_free）。</summary>
    private const int AfterimagePoolSize = 4;
    private const float AfterimageFadeTime = 0.3f;
    private static readonly Color AfterimageColor = new(1.0f, 0.72f, 0.34f, 0.5f);

    private Sprite2D _sprite = null!;
    private GpuParticles2D _thruster = null!;
    private Polygon2D _hitboxDot = null!;
    private Polygon2D _parryArc = null!;
    private Node2D _parryRim = null!; // 分段盾缘容器（能量格子节点，Modulate/Scale 级联）
    private Polygon2D _parryShine = null!;
    private Line2D _parryPulse = null!; // 激活金光一闪（白金圆环扩张淡出，一次性）

    /// <summary>弹反命中闪光剩余时长（白金色提亮 + 边缘外扩脉冲；SetParryFlash 置位）。</summary>
    private float _parryFlash;
    private const float ParryFlashTime = 0.18f;

    /// <summary>激活闪光环剩余时长（SetParryActivatePulse 置位）。</summary>
    private float _parryPulseTimer;
    private const float ParryPulseTime = 0.32f;

    /// <summary>弹反高光带顶点缓冲预分配（UpdateParryVisuals 每物理帧原地写，防 new Vector2[6]）。</summary>
    private readonly Vector2[] _parryShinePoly = new Vector2[6];

    /// <summary>BODY_TINT_BASE 迁入（可视性增强提亮青白）。</summary>
    private static readonly Color BodyTintBase = new(1.35f, 1.4f, 1.55f);
    /// <summary>擦弹机身短闪光剩余时长（金色微闪，独立短计时；SetGrazeFlash 置位、UpdateFrame 递减）。</summary>
    private float _grazeFlash;
    private readonly System.Collections.Generic.List<Sprite2D> _afterimagePool = new();
    private int _afterimageIdx;
    private readonly System.Collections.Generic.List<Sprite2D> _activeAfterimages = new();

    // ---- 核心喷口三层软点（白芯/琥珀/红外，additive；叠加在 GpuParticles 尾焰之上） ----
    private Sprite2D? _flareCore;
    private Sprite2D? _flareMid;
    private Sprite2D? _flareOuter;
    // 数值 effects.thruster_core.*（Init 一次性读入，尺寸已乘全局缩放）
    private float _flareCoreSize = 12.0f;
    private float _flareMidSize = 24.0f;
    private float _flareOuterSize = 38.0f;
    private float _flareMidY = 6.0f;
    private float _flareOuterY = 14.0f;
    private float _flareSpeedStretch = 0.4f;
    private float _flareJitterPx = 1.5f;
    private float _flareAlphaCore = 0.85f;
    private float _flareAlphaMid = 0.5f;
    private float _flareAlphaOuter = 0.28f;
    private static readonly Color FlareCoreColor = new(1.0f, 0.96f, 0.88f); // 白芯
    private static readonly Color FlareMidColor = new(1.0f, 0.62f, 0.16f);   // 琥珀
    private static readonly Color FlareOuterColor = new(0.85f, 0.22f, 0.05f); // 红外

    /// <summary>
    /// 初始化：接收节点引用 + 预建残影池。world_root = Main（残影固定世界坐标，不随玩家移动；
    /// Main 场景构建期 add_child 会报 "busy setting up children"，延迟到帧末执行）。
    /// </summary>
    public void Init(
        Sprite2D sprite, GpuParticles2D thruster, Polygon2D hitboxDot, Polygon2D parryArc, Node2D parryRim,
        Polygon2D parryShine, Line2D parryPulse, Node worldRoot)
    {
        Init(sprite, thruster, hitboxDot, parryArc, parryRim, parryShine, parryPulse, worldRoot, AfterimagePoolSize);
    }

    public void Init(
        Sprite2D sprite, GpuParticles2D thruster, Polygon2D hitboxDot, Polygon2D parryArc, Node2D parryRim,
        Polygon2D parryShine, Line2D parryPulse, Node worldRoot, int poolSize)
    {
        _sprite = sprite;
        _thruster = thruster;
        _hitboxDot = hitboxDot;
        _parryArc = parryArc;
        _parryRim = parryRim;
        _parryShine = parryShine;
        _parryPulse = parryPulse;
        for (var i = 0; i < poolSize; i++)
        {
            var ghost = new Sprite2D { Visible = false, Modulate = AfterimageColor };
            worldRoot.CallDeferred(Node.MethodName.AddChild, ghost);
            _afterimagePool.Add(ghost);
        }

        BuildThrusterFlare();
    }

    /// <summary>核心喷口三层软点（白芯/琥珀/红外，additive）：作为喷口根部的持续亮核，
    /// 叠加在 GpuParticles 尾焰之上；挂在 Thruster 节点下随其位置/缩放。数值 effects.thruster_core.*。</summary>
    private void BuildThrusterFlare()
    {
        var ws = (float)GameState.Instance.WorldScale;
        _flareCoreSize = CfgFx.Float("effects.thruster_core.core_size", _flareCoreSize, 1.0f) * ws;
        _flareMidSize = CfgFx.Float("effects.thruster_core.mid_size", _flareMidSize, 1.0f) * ws;
        _flareOuterSize = CfgFx.Float("effects.thruster_core.outer_size", _flareOuterSize, 1.0f) * ws;
        _flareMidY = 6.0f * ws;
        _flareOuterY = 14.0f * ws;
        _flareSpeedStretch = CfgFx.Float("effects.thruster_core.speed_stretch", _flareSpeedStretch, 0.0f);
        _flareJitterPx = CfgFx.Float("effects.thruster_core.jitter_px", _flareJitterPx, 0.0f) * ws;
        _flareAlphaCore = CfgFx.Float("effects.thruster_core.alpha_core", _flareAlphaCore, 0.0f, 1.0f);
        _flareAlphaMid = CfgFx.Float("effects.thruster_core.alpha_mid", _flareAlphaMid, 0.0f, 1.0f);
        _flareAlphaOuter = CfgFx.Float("effects.thruster_core.alpha_outer", _flareAlphaOuter, 0.0f, 1.0f);
        _flareCore = MakeFlareLayer(0.0f);
        _flareMid = MakeFlareLayer(_flareMidY);
        _flareOuter = MakeFlareLayer(_flareOuterY);
    }

    private Sprite2D MakeFlareLayer(float yOff)
    {
        var s = new Sprite2D
        {
            Texture = CinematicFx.SoftTexture(),
            Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f), // 初始灭，SetThruster 逐帧点亮
            Material = CinematicFx.AdditiveMaterial(),
            Position = new Vector2(0.0f, yOff),
        };
        _thruster.AddChild(s);
        return s;
    }

    /// <summary>尾焰档位应用（冲刺/加速/巡航/静止五处共用；engine_tint 由 Player 传入——增幅 外观
    /// 写入 Player.EngineTint，公开字段被 PlayerAugmentVisuals 访问，留在 Player 侧）。</summary>
    public void SetThruster(float speedScale, float amountRatio, float alpha, Color engineTint)
    {
        _thruster.SpeedScale = speedScale;
        _thruster.AmountRatio = amountRatio;
        _thruster.SelfModulate = new Color(1.0f, 1.0f, 1.0f, alpha) * engineTint;
        UpdateThrusterFlare(speedScale, alpha, engineTint);
    }

    /// <summary>核心喷口三层逐帧驱动（SetThruster 逐帧调用）：随速度 Y 向伸缩（外层拉伸更大）+
    /// 双正交高频小幅抖动；alpha 随尾焰档位，增幅 染色经 engineTint 只染琥珀/红外两层（白芯保白）。
    /// 只写 struct 属性，零托管分配。</summary>
    private void UpdateThrusterFlare(float speedScale, float alpha, Color engineTint)
    {
        if (_flareCore == null || _flareMid == null || _flareOuter == null)
        {
            return;
        }

        var t = Time.GetTicksMsec() / 1000.0f;
        var stretch = 1.0f + _flareSpeedStretch * Mathf.Max(speedScale - 1.0f, 0.0f);
        var jx = Mathf.Sin(t * 43.0f) * _flareJitterPx;
        var jy = Mathf.Cos(t * 37.0f) * _flareJitterPx;
        ApplyFlare(_flareCore, _flareCoreSize, _flareAlphaCore * alpha, stretch, jx, 0.0f + jy * 0.4f, FlareCoreColor);
        ApplyFlare(_flareMid, _flareMidSize, _flareAlphaMid * alpha, stretch * 1.15f, jx * 0.7f, _flareMidY + jy * 0.7f, FlareMidColor);
        ApplyFlare(_flareOuter, _flareOuterSize, _flareAlphaOuter * alpha, stretch * 1.35f, jx * 0.5f, _flareOuterY + jy, FlareOuterColor);
        // 琥珀/红外两层乘增幅 染色（白芯不染）
        _flareMid.Modulate = new Color(
            _flareMid.Modulate.R * engineTint.R, _flareMid.Modulate.G * engineTint.G,
            _flareMid.Modulate.B * engineTint.B, _flareMid.Modulate.A);
        _flareOuter.Modulate = new Color(
            _flareOuter.Modulate.R * engineTint.R, _flareOuter.Modulate.G * engineTint.G,
            _flareOuter.Modulate.B * engineTint.B, _flareOuter.Modulate.A);
    }

    private static void ApplyFlare(Sprite2D flare, float size, float a, float stretchY, float x, float y, Color color)
    {
        flare.Scale = new Vector2(size / CinematicFx.SoftTexSize, size / CinematicFx.SoftTexSize * stretchY);
        flare.Modulate = new Color(color.R, color.G, color.B, a);
        flare.Position = new Vector2(x, y);
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

    /// <summary>弹反命中闪光置位（Player 盾区反射成功时调用）：边缘白金色提亮 + 外扩脉冲。</summary>
    public void SetParryFlash() => _parryFlash = ParryFlashTime;

    /// <summary>激活金光一闪置位（Player 盾进入 ACTIVE 瞬间调用）：白金圆环 0.45×→1.5× 缓出扩张 + 淡出。</summary>
    public void SetParryActivatePulse()
    {
        _parryPulseTimer = ParryPulseTime;
        _parryPulse.Visible = true;
    }

    /// <summary>盾视觉逐物理帧驱动：WINDUP 小弧展开到全弧（缩放）、ACTIVE 盾缘能量脉动 + 珍珠流光
    /// 自弧线左端扫至右端、RECOVER 保持全弧、IDLE 隐藏；弹反命中时短闪（白金色提亮 + 边缘外扩）。
    /// 三层结构：暗金填充扇面 + 亮金分段盾缘（伪能量格）+ 流光高光带（零 shader 依赖，ADD 混合出辉光）。
    /// 参数化（expand/shine 来自 PlayerParry，radius/arc 来自 player 常量）——视觉不感知 弹反组件。
    /// 每物理帧调用：只写 Modulate/Scale（struct），流光带顶点走预分配缓冲，零托管分配。</summary>
    public void UpdateParryVisuals(float expand, float shine, float radius, float arcDeg, float delta, long nowMs)
    {
        var visible = expand > 0.0f;
        _parryArc.Visible = visible;
        _parryRim.Visible = visible;
        if (!visible)
        {
            _parryShine.Visible = false;
            _parryFlash = 0.0f;
            _parryPulseTimer = 0.0f;
            _parryPulse.Visible = false;
            return;
        }

        if (_parryFlash > 0.0f)
        {
            _parryFlash = Mathf.Max(_parryFlash - delta, 0.0f);
        }

        // 激活金光一闪：0.32s 内圆环 0.45×→1.5× 二次缓出扩张，alpha 线性淡出
        if (_parryPulseTimer > 0.0f)
        {
            _parryPulseTimer = Mathf.Max(_parryPulseTimer - delta, 0.0f);
            var t = 1.0f - _parryPulseTimer / ParryPulseTime;
            var easeOut = 1.0f - (1.0f - t) * (1.0f - t);
            _parryPulse.Scale = Vector2.One * (0.45f + 1.05f * easeOut);
            _parryPulse.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f - t);
            if (_parryPulseTimer <= 0.0f)
            {
                _parryPulse.Visible = false;
            }
        }

        var flash = _parryFlash / ParryFlashTime;
        var scale = 0.3f + 0.7f * expand;
        _parryArc.Scale = Vector2.One * scale;
        _parryArc.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f + 1.4f * flash);
        // 盾缘：ACTIVE（shine>0）能量脉动，RECOVER 恒定高亮；命中闪叠加外扩 + 白金色提亮
        var pulse = shine > 0.0f ? 0.72f + 0.28f * Mathf.Abs(Mathf.Sin((float)(nowMs * 0.012))) : 0.9f;
        _parryRim.Scale = Vector2.One * (scale * (1.0f + 0.14f * flash));
        _parryRim.Modulate = new Color(1.0f + 1.1f * flash, 1.0f + 0.6f * flash, 1.0f, Mathf.Min(pulse + 0.6f * flash, 1.0f));
        _parryShine.Visible = shine > 0.0f;
        if (!_parryShine.Visible)
        {
            return;
        }

        var arc = Mathf.DegToRad(arcDeg) * 0.5f;
        var centerA = -Mathf.Pi / 2.0f - arc + 2.0f * arc * shine;
        var w = Mathf.DegToRad(14.0f); // 高光带角宽
        var sp = _parryShinePoly; // 预分配复用，防每物理帧 new Vector2[6]
        sp[0] = Vector2.Zero;
        for (var i = 0; i < 5; i++)
        {
            var a = centerA - w + 2.0f * w * i / 4.0f;
            sp[i + 1] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * scale;
        }

        _parryShine.Polygon = sp;
    }

}
