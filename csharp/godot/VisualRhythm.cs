using System;
using Godot;
using InfiAir.Core;
using InfiAir.Core.Audio;
using InfiAir.Core.Visual;

namespace InfiAir;

/// <summary>
/// 战斗节奏服务：把「音乐播放位置」这**一个**真实时间面（`DESIGN_BASELINE` §2.10 已登记）
/// 折算成屏上可用的节奏读数——拍点包络 <see cref="Beat01"/>、慢呼吸 <see cref="Breath01"/>、
/// 全屏呼吸系数 <see cref="FullScreenRhythm"/>。
///
/// 为什么要有这一层（§2.12 判据 4「单一同步源」）：全屏尺度的同步脉动只由 WorldPostFx 一处实现，
/// 其余元素（弹体尾部 / 舰体流光 / 仪表 / 亮星）只做**相位从属**。相位若各自去取一次播放位置，
/// 就有多份相位、各自处理抖动回退与降级，同一拍在不同元素上会错开；本类是播放位置的唯一消费点，
/// 取样规则、降级路径与强度缩放都只有这一份。
///
/// **仅视觉层消费、绝不进判定**（§2.10 划线标准：音乐播放位置等待的是「引擎之外的现实世界」，
/// 属真实时间；判定与模拟一律模拟时间，AGENTS §4）。本类不写任何玩法字段。
/// 算式全在 core <see cref="Rhythm"/>（纯逻辑、有单测），本类只做取值与适配。
/// </summary>
public partial class VisualRhythm : Node
{
    /// <summary>「回卷」判据（秒）：播放位置一次性回退超过这个量即视为曲目循环/切曲/重播，按新位置重取相位。
    /// 取值落在两个量级之间——官方口径里的取样抖动是毫秒级（混音缓冲 + 一帧），本作曲目是一圈 40s；
    /// 贴着抖动取会把正常抖动误判成回卷（相位倒跳），贴着曲长取则回卷后相位停摆（整拍从此不亮）。</summary>
    private const double MusicRewindSeconds = 0.25;

    /// <summary>静态缓存（形状对齐 GameState.TryGetInstance）：缓存 + 失效重查，取不到返回 null 而非抛——
    /// 「本场景没有节奏服务」（标题屏 / 拆树期）与「autoload 先释放的非常规退出序」都是常态输入。
    /// 消费方一律 `VisualRhythm.Instance?.X ?? 0f`：取不到即中性（零振幅），不抛异常。</summary>
    private static VisualRhythm? _instance;

    /// <summary>当前节奏服务实例；无实例时为 null。</summary>
    public static VisualRhythm? Instance
    {
        get
        {
            if (_instance != null && GodotObject.IsInstanceValid(_instance))
            {
                return _instance;
            }

            // 整棵树一次性拆除时 _ExitTree 未必跑过：失效即视为无实例，不把已释放对象递出去
            _instance = null;
            return null;
        }
    }

    /// <summary>拍点包络（0..1，整拍一脉冲；包络窗口之外恒 0）。拍点**一律下放局部元素**（判据 1：
    /// 全屏层只允许慢呼吸），局部消费方按它取振幅；本批尚无生产消费方（B2/B3 的弹体尾部与舰体流光接它）。</summary>
    public float Beat01 { get; private set; }

    /// <summary>慢呼吸振荡（0..1，谷 0 / 峰 1）：绕中位摆动的波形，消费方自行乘振幅。</summary>
    public float Breath01 { get; private set; }

    /// <summary>动效强度（0..1，设置项 `fx_intensity` 的每帧直读值）：缩放本轮新增动效的振幅、不改频率，
    /// 0 = 回到本批次之前的画面。不缓存、不接信号——设置页滑杆拖到 0 的当帧画面就要回落。</summary>
    public float Intensity { get; private set; }

    /// <summary>全屏层**唯一**要上传给后处理的带符号系数（WorldPostFx 的 `u_rhythm`）：慢呼吸绕中位的
    /// ±半振幅，已过 <see cref="FlashBudget.Amplitude"/>（减闪归零）与 <see cref="Rhythm.FullScreenHalfAmplitude"/>
    /// （峰谷亮度差硬线）双重约束，并乘过动效强度。三项中任一项失效（关闭 / 减闪 / 强度 0）都落**精确 0**
    /// ——shader 侧 `c *= 1.0 + u_rhythm` 在 0 时逐位等于改造前的输出。</summary>
    public float FullScreenRhythm { get; private set; }

    // balance 缓存（`effects.motion.*`，_Ready 读一次；热路径零字典查）。
    // 默认值与 data/balance.json 同值：json 缺失/损坏时回退到同值而不是旧常数（§2.11）。
    private double _bpmBattle = 120.0;
    private double _bpmBoss = 150.0;
    private double _bpmBase = 84.0;
    private double _breathHzMin = 0.5;
    private double _breathHzMax = 1.2;
    private double _breathAmp = 0.025;
    private double _beatAttack = 0.08;
    private double _beatRelease = 0.2;

    /// <summary>音乐编排（Main 注入；未注入 / 尚未装载 / 无播放器时为 null → 降级路径）。</summary>
    private MusicDirector? _music;

    /// <summary>节拍时基（秒）：音乐时钟可用时＝播放位置，否则＝模拟时钟累计。两条路径共用同一相位算式。</summary>
    private double _beatClock;

    /// <summary>最近一次被采纳的播放位置（秒）：官方口径要求丢弃回退取样，比较基准就是它。</summary>
    private double _musicPosition;

    /// <summary>音乐时钟是否已被证明在推进——冻结的播放位置（headless dummy 驱动）视同没有可用时基。</summary>
    private bool _musicLive;

    /// <summary>呼吸相位（周期数）：逐帧按当前频率累加，不写成「绝对时间 × 频率」（理由见 <see cref="_Process"/>）。</summary>
    private double _breathCycles;

    public override void _EnterTree()
    {
        // 置位早于 _Ready：_Ready 里创建的子面片（B2 起）与同帧的消费方都要能取到实例
        _instance = this;
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    public override void _Ready()
    {
        // balance 一次性读入（非热路径；CfgFx 自带类型守卫与钳制）。钳制只防「手改 JSON 写出零频率 /
        // 天文频率」这类会把相位钉死或烧穿守卫的值，取值口径仍是 balance 一处（§2.11）。
        _bpmBattle = CfgFx.Float("effects.motion.bpm_battle", (float)_bpmBattle, 1.0f, 1200.0f);
        _bpmBoss = CfgFx.Float("effects.motion.bpm_boss", (float)_bpmBoss, 1.0f, 1200.0f);
        _bpmBase = CfgFx.Float("effects.motion.bpm_base", (float)_bpmBase, 1.0f, 1200.0f);
        _breathHzMin = CfgFx.Float("effects.motion.breath_hz_min", (float)_breathHzMin, 0.0f, 20.0f);
        _breathHzMax = CfgFx.Float("effects.motion.breath_hz_max", (float)_breathHzMax, 0.0f, 20.0f);
        // 呼吸振幅只钳非负：上界是 core 的峰谷硬线（FullScreenHalfAmplitude），此处再写一遍等于第二处事实源
        _breathAmp = CfgFx.Float("effects.motion.breath_amp", (float)_breathAmp, 0.0f);
        _beatAttack = CfgFx.Float("effects.motion.beat_attack", (float)_beatAttack, 0.0f, 1.0f);
        _beatRelease = CfgFx.Float("effects.motion.beat_release", (float)_beatRelease, 0.0f, 1.0f);
    }

    /// <summary>注入音乐编排（Main 在建立 MusicDirector 之后调用）。未注入或注入 null 都走降级路径：
    /// 无头冒烟 / 曲目加载失败 / 非战斗场景下节奏照旧可算，不崩、不抛、不空引用。</summary>
    public void SetMusic(MusicDirector? music)
    {
        _music = music;
    }

    public override void _Process(double delta)
    {
        // 自愈：一次异常 delta（或异常取样）会把时基污染成 NaN，此后相位恒 0（拍点整段不亮、呼吸停摆），
        // 而引擎侧零报错——与「这一帧不动」在画面上无从分辨，只能在源头复位
        if (!double.IsFinite(_beatClock) || !double.IsFinite(_musicPosition) || !double.IsFinite(_breathCycles))
        {
            _beatClock = 0.0;
            _musicPosition = 0.0;
            _breathCycles = 0.0;
            _musicLive = false;
        }

        var fx = GameState.Instance.FxIntensity;
        Intensity = double.IsFinite(fx) ? (float)Math.Clamp(fx, 0.0, 1.0) : 0.0f;

        if (!SampleMusic())
        {
            // 降级：无播放器 / 未在放音 / 播放位置不推进——用 _Process 的 delta 累积模拟时钟。
            // 曲速仍取当前档位（BpmForCurrentCue）：Boss 战即使取不到音频时钟，节拍也落在该曲的拍上，
            // 不会与正在播的曲子打架。--fixed-fps 下 delta 恒定，故无头下逐帧可重复（§2.12 判据 5）。
            _beatClock += delta;
        }

        var bpm = BpmForCurrentCue();
        Beat01 = (float)Rhythm.BeatPulse(Rhythm.BeatPhase(_beatClock, bpm), _beatAttack, _beatRelease);

        // 呼吸相位按当前频率逐帧累加，不写成 BreathValue(绝对时间, 频率)：那个形式里 hz 一变，
        // 相位会跳 hz 变化量 × 已播时长（本局打几分钟后换档就是几百个周期），表现为呼吸「闪」一下
        _breathCycles += Rhythm.BreathHz(Danger01(), _breathHzMin, _breathHzMax) * delta;
        Breath01 = (float)Rhythm.BreathValue(_breathCycles, 1.0);

        // 全屏系数：波形取带符号（谷 -1 / 峰 +1），振幅经减闪归零与硬线双重约束后再乘动效强度。
        // 乘的顺序无关合规性（都在 0..1 区间内单调），但振幅必须先归零再钳——反过来的话
        // 「减闪」只能把已经合法的值再除一次，读数与意图对不上
        var wave = Breath01 * 2.0 - 1.0;
        var amp = FlashBudget.Amplitude((float)(_breathAmp * Intensity), PulseId.WorldBreath, GameState.Instance.ReduceFlash);
        FullScreenRhythm = (float)(wave * Rhythm.FullScreenHalfAmplitude(amp));
    }

    /// <summary>
    /// 音乐时钟取样（官方节拍同步口径，出处见 REFERENCES §4.7）：返回 true 表示时基已由播放位置接管
    /// （<see cref="_beatClock"/> 已按它前移或停在上一帧采纳值）。三条丢弃规则：
    ///   1. 取样**小幅**小于上一帧采纳值——多线程混音会让播放位置偶发抖动回退，官方明确
    ///      「discard it if so」：保留上一帧的值（相位慢一帧，而不是倒跳一拍）；
    ///   2. 取样非有限——NaN 会顺着乘法污染整屏着色器输出（整块黑/白，引擎零报错）；
    ///   3. 位置始终不推进（headless dummy 驱动 / 流未真正推进）——「不推进的播放位置」等于没有可用
    ///      时基，按降级处理而不是把相位钉死在拍首（钉死＝整屏不动，无头下看不出，真机上却是节奏消失）。
    /// 另有一条例外必须与 1 分开：**大幅回退**（曲目循环一圈 / 切曲 / 重播，位置回到 0）要采纳，
    /// 否则相位会永久钉在回卷前那一刻——40s 循环曲播完一圈后整拍就再也不亮，引擎侧零报错。
    /// 两档靠幅度区分：抖动在毫秒级，回卷是整曲长度（见 <see cref="MusicRewindSeconds"/>）。
    /// 播放器不存在 / 已停止：直接降级（不再取样）。
    /// </summary>
    private bool SampleMusic()
    {
        var player = _music?.Primary;
        if (player == null || !GodotObject.IsInstanceValid(player) || !player.IsPlaying())
        {
            _musicLive = false;
            return false;
        }

        var position = (double)player.GetPlaybackPosition()
            + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency();
        if (!double.IsFinite(position))
        {
            // 单帧异常取样：保持上一帧的结论与时基（这里若改判降级，整个音乐时钟会因一帧抖动被丢掉）
            return _musicLive;
        }

        if (_musicLive && position < _musicPosition - MusicRewindSeconds)
        {
            _musicPosition = position; // 回卷：按新位置重取相位（拍首对齐新的一圈）
        }
        else if (position > _musicPosition)
        {
            _musicPosition = position;
            _musicLive = true;
        }

        if (!_musicLive)
        {
            return false;
        }

        // 未推进或小幅回退都不改 _musicPosition：相位停在上一帧采纳值（官方口径的「保留上一帧」）
        _beatClock = _musicPosition;
        return true;
    }

    /// <summary>当前曲目档 → BPM；无音乐 / 尚未装载 / 基地以外未匹配到的档位一律按战斗曲
    /// （降级路径的基准，§2.12 判据 5）。</summary>
    private double BpmForCurrentCue() => _music?.Playing switch
    {
        MusicCue.Boss => _bpmBoss,
        MusicCue.Base => _bpmBase,
        _ => _bpmBattle,
    };

    /// <summary>危险度（0..1）：本批只由「是否在 Boss 战」给出——Boss 在场是本作最紧张的一段，
    /// 呼吸随之贴近 breath_hz_max，其余时段停在 min（静止/满血时几乎不动）。
    /// 判据取当前曲目档而不是另查 Boss 节点：档位本身就来自 Boss 在场与否的判定，零额外查询、
    /// 不与 Boss 节点的入场/退场帧序打架；B4「背景战况响应」要细化时只改这一处。</summary>
    private double Danger01() => _music != null && _music.Playing == MusicCue.Boss ? 1.0 : 0.0;
}
