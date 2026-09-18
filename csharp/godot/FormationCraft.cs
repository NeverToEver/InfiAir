using Godot;
using InfiAir.Core.Visual;

namespace InfiAir;

/// <summary>
/// 轰炸编队事件·编队战机：
/// 楔形编队成员，注册 enemy 组与 GameState.Enemies（玩家子弹/激光可命中）；实现 IDamageable。
/// 自身无 AI：位置/朝向由 FormationStrikeEvent._Process 按编队锚点驱动，侧倾（bank）由事件按
/// 转向/离场进度写入（俯视视角下「压坡转弯」比平推更有质量感）。
/// 身份识别：琥珀色调 + 翼尖航行灯（红/绿）+ 机腹投弹舱照明——与普通敌机同贴图但一眼可区分。
/// 被击坠：爆炸 + 注销注册表；击杀数与击杀分同处入账（与普通敌机/精英炮塔同口）。
/// 实现 IAimTarget：与普通敌机同吃辅助瞄准（遭遇期间它是屏上唯一可打目标）。
/// </summary>
public partial class FormationCraft : Area2D, IDamageable, IAimTarget
{
    [Signal]
    public delegate void DiedEventHandler(FormationCraft craft);

    /// <summary>机体贴图。</summary>
    // 静态 Godot 资源改实例字段（退出 segfault 实测教训，UITheme.cs:53）
    private readonly Texture2D _texture = GD.Load<Texture2D>("res://assets/sprites/enemy_ship_2.png");

    public int MaxHp { get; set; } = 60;
    public int Hp { get; set; } = 60;

    /// <summary>击落入账的击杀分（balance.json formation_strike_event.craft_score，事件编排生成时注入）：
    /// 走 AddKillScore，吃连击/score_amp/难度档倍率——与普通敌机、精英炮塔同口。</summary>
    public int ScoreValue { get; set; } = 200;

    /// <summary>编队身份色（琥珀偏橙：与普通敌机的冷灰、精英炮塔的品红区分）。</summary>
    private static readonly Color FormationTint = new(1.0f, 0.82f, 0.62f);

    private Sprite2D? _sprite;
    private GlowDot? _bayLight; // 机腹投弹舱照明（投弹前预警闪烁、投弹瞬间满亮）
    private float _bayFlash;
    /// <summary>投弹预警剩余（投弹前的一小段闪烁窗口：玩家据此判断「这架要投了」）。</summary>
    private float _bayWarn;
    /// <summary>受击闪白手动衰减计时（_PhysicsProcess 逐帧 lerp，替代每命中新建 Tween）。</summary>
    private float _flashTimer;
    private Vector2 _flashBaseScale = Vector2.One; // 受击缩放回弹基准（非闪白期捕获，FlashFx 回位用）
    private const float FlashTime = 0.1f;
    private const float BayFlashTime = 0.18f;
    /// <summary>击杀震动强度缓存（_Ready 一次性读入，热路径禁 cfg）。</summary>
    private float _shakeDie = 5.0f;
    /// <summary>碰撞半径缓存（_Ready 按机体尺寸族写入，26 × world_scale）——辅助框半宽的基数。</summary>
    private float _bodyRadius;
    /// <summary>辅助瞄准标记登记态（编队机自入场即可打，登记随 _Ready/_ExitTree 成对）。</summary>
    private bool _aimMarked;

    // ---- 入场落位与相位波（纯视觉：只动 Sprite2D 的缩放与位移；节点位置/朝向/侧倾/碰撞半径零改动） ----
    private static bool _motionCfgLoaded;
    /// <summary>入场时长（effects.motion.enemy_entry_time）：与普通敌机同一时长口径。</summary>
    private static float _entryTime = 0.3f;
    /// <summary>名次相位间隔（effects.motion.formation_phase_delay）：每个名次晚这么多秒落位。</summary>
    private static float _phaseDelay = 0.15f;

    /// <summary>入场起点缩放与上方位移（局部 +Y：本机根节点按航向旋转 π 时即屏上「从上方滑入」）。
    /// 比普通敌机更明显一档——编队是整组自屏外压下来，落位要读得出来才不算白做。</summary>
    private const float EntryStartScale = 0.72f;

    private const float EntryRise = 34.0f;

    /// <summary>投弹名次（<c>FormationPlan.DropRank</c> 的既有排序取值；0 = 长机档）。</summary>
    private int _entryRank;
    private float _entryElapsed;
    /// <summary>名次相位等待剩余（秒；到达屏上后才开始扣）。</summary>
    private float _entryDelay;
    /// <summary>已到屏上开始计时（编队自屏顶外压入，屏外起算的入场动效玩家看不到）。</summary>
    private bool _entryArmed;
    private bool _entryDone = true;
    private Vector2 _entryBaseScale = Vector2.One;

    // ---- IAimTarget 契约（辅助瞄准扫描只读量） ----

    /// <summary>可打 = 未被击坠（Hp 守卫与 TakeDamage 同口径）。</summary>
    public bool AimTargetable => Hp > 0;

    /// <summary>标记态与计数同源：登记即标记（<see cref="SetAimMarked"/>）。</summary>
    public bool AimMarked => _aimMarked;

    /// <summary>世界坐标（编队机位置由事件按编队锚点驱动）。</summary>
    public Vector2 AimWorldPosition => GlobalPosition;

    /// <summary>碰撞半径（已含 world_scale）；辅助框半宽 = 本值 + 档位 frame_pad。</summary>
    public float AimCollisionRadius => _bodyRadius;

    /// <summary>标记登记（幂等）：改标记即改计数——AimFrameLayer 的零标记早退依赖计数准确。</summary>
    private void SetAimMarked(bool marked)
    {
        if (_aimMarked == marked)
        {
            return;
        }

        _aimMarked = marked;
        AimTargetCount.SetEncounterMarked(marked);
    }

    /// <summary>setup() 在入树/_Ready() 之前调用。</summary>
    public void Setup(int pHp)
    {
        MaxHp = Mathf.Max(1, pHp);
        Hp = MaxHp;
    }

    public override void _Ready()
    {
        CollisionLayer = 4; // 第 3 层：enemy（玩家子弹以 enemy 组结算）
        CollisionMask = 0;
        _sprite = new Sprite2D
        {
            Texture = _texture,
            Scale = Vector2.One * 0.9f * (float)GameState.Instance.WorldScale, // 设计值 0.9 × 全局缩放
            Modulate = FormationTint,
        };
        AddChild(_sprite);
        // 翼尖航行灯（编队身份）+ 机腹投弹舱照明（默认灭，投弹时亮起）
        var ws = (float)GameState.Instance.WorldScale;
        var portLight = CinematicFx.Glow(4.5f * ws, new Color(1.0f, 0.35f, 0.3f, 0.85f));
        var starboardLight = CinematicFx.Glow(4.5f * ws, new Color(0.4f, 1.0f, 0.55f, 0.85f));
        portLight.Position = new Vector2(-26.0f, 4.0f) * ws;
        starboardLight.Position = new Vector2(26.0f, 4.0f) * ws;
        _sprite.AddChild(portLight);
        _sprite.AddChild(starboardLight);
        _bayLight = CinematicFx.Glow(7.0f * ws, new Color(1.0f, 0.78f, 0.35f, 1.0f));
        _bayLight.Position = new Vector2(0.0f, 12.0f) * ws;
        _bayLight.Visible = false;
        _sprite.AddChild(_bayLight);
        var shape = new CollisionShape2D();
        _bodyRadius = 26.0f * (float)GameState.Instance.WorldScale;
        var circle = new CircleShape2D { Radius = _bodyRadius };
        shape.Shape = circle;
        AddChild(shape);
        GameState.Instance.BindEnemy(this); // 统一绑定
        SetAimMarked(true); // 编队机自入场即可打：纳入辅助瞄准标记
        // 击杀震动强度缓存
        _shakeDie = (float)GameState.Instance.Cfg("effects.shake.enemy_die", _shakeDie).AsDouble();
        LoadMotionCfg();
        BeginEntry(_entryRank); // 默认长机档；事件在入树后按投弹名次重起一次（相位波）
    }

    /// <summary>入场名次注入（编队事件按 <c>FormationPlan.DropRank</c> 传入；入树后调用——
    /// 贴图精灵在 _Ready 才构建）。名次越靠后入场越晚：相位间隔单源在 balance
    /// （`effects.motion.formation_phase_delay`），本类不另存一份。</summary>
    public void SetEntryRank(int rank)
    {
        _entryRank = Mathf.Max(rank, 0);
        LoadMotionCfg();
        BeginEntry(_entryRank);
    }

    /// <summary>入场落位配置（effects.motion.*；懒加载一次，静态标量共享）。</summary>
    private static void LoadMotionCfg()
    {
        if (_motionCfgLoaded)
        {
            return;
        }

        _entryTime = CfgFx.Float("effects.motion.enemy_entry_time", _entryTime, 0.0f);
        _phaseDelay = CfgFx.Float("effects.motion.formation_phase_delay", _phaseDelay, 0.0f);
        _motionCfgLoaded = true;
    }

    /// <summary>写入入场起点姿态（名次相位在等待期里保持这一姿态：机体在视野里「还没落位」，
    /// 落位过程因此读得出先后）。只写 Sprite2D 子节点，节点自身的缩放（侧倾压坡）与碰撞不受影响。</summary>
    private void BeginEntry(int rank)
    {
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite == null)
        {
            _entryDone = true;
            return;
        }

        _entryBaseScale = _sprite.Scale;
        _entryRank = Mathf.Max(rank, 0);
        _entryDelay = _entryRank * _phaseDelay;
        _entryArmed = false;
        _entryElapsed = 0.0f;
        _entryDone = !(_entryTime > 0.0f); // 时长坏配置：直接落位，不留「永久偏小」的姿态
        if (_entryDone)
        {
            _sprite.Scale = _entryBaseScale;
            _sprite.Position = Vector2.Zero;
            return;
        }

        _sprite.Scale = _entryBaseScale * EntryStartScale;
        _sprite.Position = new Vector2(0.0f, EntryRise * (float)GameState.Instance.WorldScale);
    }

    /// <summary>入场落位逐帧推进（模拟时间；缓动算式在 core <see cref="UnitEntry"/>）。
    /// **到屏上才起算**：编队自屏顶外整组压下来，从建队那一刻起算会让先头机体的落位在屏外跑完；
    /// 到屏上后先按名次等待，再走一遍缓动——读感是「一架一架压进视野落位」。
    /// 排在受击闪白之前：闪白的绝对值后写覆盖入场值（缩放回弹是高优先的打击感读数）。</summary>
    private void UpdateEntry(float delta)
    {
        if (_entryDone || _sprite == null)
        {
            return;
        }

        if (!_entryArmed)
        {
            if (GlobalPosition.Y < FrameCache.ViewRect().Position.Y)
            {
                return; // 仍在屏顶之外：先攒着，进场那一刻才起算
            }

            _entryArmed = true;
        }

        if (_entryDelay > 0.0f)
        {
            _entryDelay -= delta;
            return;
        }

        _entryElapsed += delta;
        var k = UnitEntry.Placement01(_entryElapsed, _entryTime);
        _sprite.Scale = _entryBaseScale * Mathf.Lerp(EntryStartScale, 1.0f, k);
        _sprite.Position = new Vector2(0.0f, EntryRise * (1.0f - k) * (float)GameState.Instance.WorldScale);
        if (k < 1.0f)
        {
            return;
        }

        _entryDone = true;
        _sprite.Scale = _entryBaseScale;
        _sprite.Position = Vector2.Zero;
    }

    /// <summary>侧倾写入（事件按转弯/离场进度调用）：节点横向压缩模拟俯视压坡。</summary>
    public void SetBank(float bank01)
    {
        var b = Mathf.Clamp(bank01, 0.0f, 1.0f);
        Scale = new Vector2(Mathf.Lerp(1.0f, 0.82f, b), 1.0f);
    }

    /// <summary>投弹预警（投放前由事件按时刻表提前调用）：机腹灯进入闪烁窗口，
    /// 玩家能看出「哪一架即将投弹」——投弹动作可预判，威胁才是公平的。
    /// 窗口长度由调用方给（唯一口径在事件编排的 BayWarnLead），不在此重复一份时长。</summary>
    public void WarnBay(float duration)
    {
        _bayWarn = Mathf.Max(_bayWarn, Mathf.Max(duration, 0.0f));
        if (_bayLight != null && GodotObject.IsInstanceValid(_bayLight))
        {
            _bayLight.Visible = true;
        }
    }

    /// <summary>投弹舱满亮（每次投弹由事件调用）：投弹动作的确认信号。</summary>
    public void FlashBay()
    {
        _bayWarn = 0.0f; // 投放已发生，预警窗口即刻结束
        _bayFlash = BayFlashTime;
        if (_bayLight != null && GodotObject.IsInstanceValid(_bayLight))
        {
            _bayLight.Visible = true;
        }
    }

    /// <summary>受击闪白逐帧衰减（编队机自身无移动回调，独立物理帧推进闪白；FlashFx 共享实现）。
    /// 入场落位在闪白之前推进：闪白写的缩放绝对值后落笔（高优先的打击感读数）。</summary>
    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        UpdateEntry(d);
        if (_flashTimer > 0.0f && _sprite != null)
        {
            FlashFx.Update(_sprite, ref _flashTimer, d, FlashTime, Colors.White, ref _flashBaseScale);
        }

        if (_bayWarn > 0.0f)
        {
            _bayWarn -= d;
        }

        if (_bayFlash <= 0.0f && _bayWarn <= 0.0f)
        {
            if (_bayLight != null && GodotObject.IsInstanceValid(_bayLight) && _bayLight.Visible)
            {
                _bayLight.Visible = false;
            }

            return;
        }

        if (_bayLight == null || !GodotObject.IsInstanceValid(_bayLight))
        {
            return;
        }

        // 投弹舱灯的可见期 = 预警窗口（脉冲）+ 投放窗口（满亮）；两者同源，先满亮后脉冲
        _bayLight.Visible = true;
        float k;
        if (_bayFlash > 0.0f)
        {
            _bayFlash -= d;
            k = Mathf.Clamp(_bayFlash / BayFlashTime, 0.0f, 1.0f);
        }
        else
        {
            k = 0.35f + (0.45f * Mathf.Abs(Enemy.SinFast(_bayWarn * 42.0f))); // 预警脉冲
        }

        _bayLight.Modulate = new Color(1.0f, 1.0f, 1.0f, k);
        if (_bayFlash <= 0.0f && _bayWarn <= 0.0f)
        {
            _bayLight.Visible = false;
        }
    }

    public override void _ExitTree()
    {
        // 标记计数离树兜底（幂等）：事件清场直接 QueueFree 的编队机不经 Die
        SetAimMarked(false);
        GameState.TryGetInstance()?.UnbindEnemy(this); // 统一解绑；autoload 可能先于本节点释放
    }

    public void TakeDamage(int amount, float scoreScale)
    {
        if (Hp <= 0)
        {
            return;
        }

        Hp -= amount;
        // 受击闪白 + 缩放回弹（_sprite 在 _Ready 构建；防御性判空与 _PhysicsProcess 同口径）
        if (_sprite != null)
        {
            if (!_entryDone)
            {
                // 入场中受击：闪白回位的基准必须是**落位后**的缩放（入场起点的小缩放若被当成基准
                // 捕获，闪白结束会把机体写回那个小缩放并就此固定——画面上只是一架「略小」的僚机）
                _sprite.Scale = _entryBaseScale;
            }

            FlashFx.Hit(_sprite, ref _flashTimer, FlashTime, ref _flashBaseScale);
        }
        else
        {
            _flashTimer = FlashTime;
        }

        if (Hp <= 0)
        {
            Die();
        }
    }

    public void TakeDamage(int amount) => TakeDamage(amount, 1.0f);

    public void Die()
    {
        // 击杀数与击杀分在同一处入账（ScoreValue 由事件编排注入）：两笔分处两处时，
        // 「打光编队」与「打光一波敌机」的账目形态不同，漏一侧也不报错
        SetAimMarked(false);
        GameState.Instance.AddKillScore(ScoreValue);
        GameState.Instance.AddKill();
        GameState.Instance.PlaySfx(SfxId.Explosion);
        GameState.Instance.Shake(_shakeDie);
        Explosion.SpawnAt(GetParent(), GlobalPosition, 1.0f);
        EmitSignal(SignalName.Died, this);
        QueueFree();
    }
}
