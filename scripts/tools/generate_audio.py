#!/usr/bin/env python3
"""一次性音频程序合成脚本（仅 Python 标准库）。

生成以下产物到 assets/audio/：
- explosion.wav      敌机爆炸（噪声爆破 + 低频衰减）
- explosion_big.wav  Boss/精英爆炸（更长、更低沉、双层噪声）
- player_hit.wav     玩家受击（短促噪声 + 下滑音）
- buff_pick.wav      Buff 确认音（上行双音）
- dash.wav           相位冲刺（上行扫频 + 噪声）
- resupply.wav       母舰补给（上行三音琶音）
- heartbeat.wav      Meta HUD DYING 心跳（55Hz 双脉冲 lub-dub）
- ui_hover.wav        界面·悬停（极短高频软点）
- ui_confirm.wav      界面·确认（上行两音）
- ui_cancel.wav       界面·取消（下行两音，与确认互为反向手势）
- ui_toggle.wav       界面·切换（单音短点，档位/开关变化）
- ui_deny.wav         界面·受阻（低频双脉冲回绝）
- bgm_loop.wav       40s 无缝循环氛围电子 BGM（和弦垫 + 琶音 + 低音）
- bgm_boss.wav       Boss 战曲：同族音色、更快的驱动琶音与和声小调进行（32s 无缝循环）
- bgm_base.wav       基地休整曲：同族音色、慢速大七和弦垫 + 稀疏钟音琶音（40s 无缝循环）
- bullet_fire.wav / bullet_fire_b.wav / bullet_fire_c.wav  玩家开火（类消音枪械：低频砰 + 瞬态 + 气体嘶，三变体）

用法：python3 scripts/tools/generate_audio.py
"""

import math
import os
import random
import struct
import wave

SR = 44100
OUT_DIR = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "assets", "audio"))

random.seed(20260720)


def write_wav(name: str, samples: list, peak_target: float = 0.89) -> None:
    peak = max(1e-9, max(abs(s) for s in samples))
    scale = peak_target / peak
    frames = b"".join(
        struct.pack("<h", int(max(-1.0, min(1.0, s * scale)) * 32767)) for s in samples
    )
    path = os.path.join(OUT_DIR, name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(frames)
    print(f"wrote {path} ({len(samples) / SR:.2f}s)")


def make_bullet_fire(pitch: float, punch: float) -> list:
    """类消音枪械开火：低频砰 + 中频瞬态 + 低通气体嘶（替代激光/晶体音）。

    低频主体 pitch→0.55x 快速下滑 + ~50ms 指数衰减（闷"噗"）；
    中频非谐波瞬态 6ms 内消失（机械"啪"）；一阶低通噪声模拟气体释放（"嘶"）。
    三个变体音高错开、力度（punch）不同，轮转播放不单调。
    """
    dur = 0.09
    n = int(SR * dur)
    out = []
    phase = 0.0
    hiss = 0.0
    for i in range(n):
        t = i / SR
        # 低频砰：pitch 快速下滑到 0.55x
        freq = pitch * (0.55 + 0.45 * math.exp(-t / 0.018))
        phase += 2.0 * math.pi * freq / SR
        thump = (math.sin(phase) + 0.25 * math.sin(2.0 * phase)) * math.exp(-t / 0.020)
        # 中频瞬态"啪"：非整数倍，6ms 内消失
        snap = math.sin(2.0 * math.pi * pitch * 3.1 * t) * math.exp(-t / 0.006)
        # 气体嘶：一阶低通噪声（~700Hz 截止），16ms 衰减
        hiss += 0.10 * (random.uniform(-1.0, 1.0) - hiss)
        out.append(0.75 * thump + 0.30 * punch * snap + 0.35 * hiss * math.exp(-t / 0.016))
    return out


def make_explosion(dur: float, base_freq: float, noise_decay: float) -> list:
    """噪声爆破 + 下滑低频 thump。"""
    n = int(SR * dur)
    out = []
    phase = 0.0
    for i in range(n):
        t = i / SR
        env = math.exp(-t * noise_decay)
        noise = random.uniform(-1.0, 1.0) * env
        freq = base_freq * (1.0 - 0.6 * min(t / dur, 1.0))
        phase += 2.0 * math.pi * freq / SR
        thump = math.sin(phase) * math.exp(-t * 4.0)
        out.append(0.55 * noise + 0.85 * thump)
    return out


def make_explosion_big() -> list:
    """双层：主体同 explosion，叠加更慢衰减的次低频轰响。"""
    base = make_explosion(1.6, 55.0, 3.2)
    n = len(base)
    phase = 0.0
    for i in range(n):
        t = i / SR
        phase += 2.0 * math.pi * 38.0 / SR
        base[i] += 0.6 * math.sin(phase) * math.exp(-t * 2.2)
        base[i] += 0.35 * random.uniform(-1.0, 1.0) * math.exp(-t * 6.0)
    return base


def make_player_hit() -> list:
    dur = 0.35
    n = int(SR * dur)
    out = []
    phase = 0.0
    for i in range(n):
        t = i / SR
        noise = random.uniform(-1.0, 1.0) * math.exp(-t * 25.0)
        freq = 420.0 - 270.0 * (t / dur)
        phase += 2.0 * math.pi * freq / SR
        tone = (1.0 if math.sin(phase) >= 0.0 else -1.0) * math.exp(-t * 9.0)
        out.append(0.5 * noise + 0.45 * tone)
    return out


def make_buff_pick() -> list:
    dur = 0.32
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        s = 0.0
        for t0, freq in ((0.0, 660.0), (0.12, 990.0)):
            if t >= t0:
                lt = t - t0
                env = min(lt / 0.01, 1.0) * math.exp(-lt * 7.0)
                s += env * (math.sin(2.0 * math.pi * freq * lt) + 0.4 * math.sin(4.0 * math.pi * freq * lt))
        out.append(0.6 * s)
    return out


def make_dash() -> list:
    """冲刺呼啸：上行音高扫频 + 噪声。"""
    dur = 0.28
    n = int(SR * dur)
    out = []
    phase = 0.0
    for i in range(n):
        t = i / SR
        env = min(t / 0.03, 1.0) * math.exp(-t * 8.0)
        freq = 220.0 + 700.0 * (t / dur)
        phase += 2.0 * math.pi * freq / SR
        out.append(env * (0.6 * math.sin(phase) + 0.4 * random.uniform(-1.0, 1.0)))
    return out


def make_resupply() -> list:
    """补给确认：上行三音琶音（C5-E5-G5）+ 尾音。"""
    dur = 0.5
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        s = 0.0
        for t0, freq in ((0.0, 523.25), (0.1, 659.25), (0.2, 784.0)):
            if t >= t0:
                lt = t - t0
                env = min(lt / 0.01, 1.0) * math.exp(-lt * 5.0)
                s += env * (math.sin(2.0 * math.pi * freq * lt) + 0.3 * math.sin(4.0 * math.pi * freq * lt))
        out.append(0.5 * s)
    return out


def make_heartbeat() -> list:
    """DYING 心跳（D7）：55Hz 正弦双脉冲（lub 强 / dub 弱），0.28s，指数包络。"""
    dur = 0.28
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        s = 0.0
        for t0, amp in ((0.0, 1.0), (0.13, 0.65)):
            if t >= t0:
                lt = t - t0
                env = min(lt / 0.008, 1.0) * math.exp(-lt * 18.0)
                s += amp * env * math.sin(2.0 * math.pi * 55.0 * lt)
        out.append(0.8 * s)
    return out


# ---------------- 界面反馈族（UI 音效） ----------------
# 全部是纯音（正弦 + 一次谐波），**不消费随机流**：短、无噪声尾巴、无起振硬切，
# 逐字节可复现与既有产物无关（见 main() 末尾的种子说明）。
# 音量另有分工：采样峰值在 tune_sfx.py 里归一得更低，SfxPlayer.BaseDb 再低一档——
# 界面反馈是操作确认，任何时候都不该盖过开火/爆炸/受击。

def make_ui_hover() -> list:
    """界面·悬停（0.05s）：极短高频软点，只回答「指到了」。

    时长与余量都压到「听得见即止」：鼠标扫过一排按钮会连续穿过多个控件，
    长一点、响一点就成连发噪声（同因，SfxPlayer 的 UiHover 最小间隔取 80ms）。
    """
    dur = 0.05
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        env = min(t / 0.004, 1.0) * math.exp(-t * 72.0)
        out.append(0.5 * env * (math.sin(2.0 * math.pi * 1560.0 * t)
                                + 0.25 * math.sin(4.0 * math.pi * 1560.0 * t)))
    return out


def make_ui_confirm() -> list:
    """界面·确认（0.16s）：上行两音（G5→D6），与 buff_pick 同族但更短、更轻。"""
    dur = 0.16
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        s = 0.0
        for t0, freq, decay in ((0.0, 784.0, 40.0), (0.055, 1174.66, 30.0)):
            if t >= t0:
                lt = t - t0
                env = min(lt / 0.003, 1.0) * math.exp(-lt * decay)
                s += env * (math.sin(2.0 * math.pi * freq * lt)
                            + 0.22 * math.sin(4.0 * math.pi * freq * lt))
        out.append(0.55 * s)
    return out


def make_ui_cancel() -> list:
    """界面·取消（0.20s）：下行两音（D5→G4）——与确认的上行互为反向手势，不看画面也听得出方向。"""
    dur = 0.20
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        s = 0.0
        for t0, freq, decay in ((0.0, 587.33, 34.0), (0.06, 392.0, 26.0)):
            if t >= t0:
                lt = t - t0
                env = min(lt / 0.003, 1.0) * math.exp(-lt * decay)
                s += env * (math.sin(2.0 * math.pi * freq * lt)
                            + 0.22 * math.sin(4.0 * math.pi * freq * lt))
        out.append(0.55 * s)
    return out


def make_ui_toggle() -> list:
    """界面·切换（0.10s）：单音短点（C6）——档位/开关变化的定位音，不带方向语义。"""
    dur = 0.10
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        env = min(t / 0.003, 1.0) * math.exp(-t * 34.0)
        out.append(0.5 * env * (math.sin(2.0 * math.pi * 1046.5 * t)
                                + 0.30 * math.sin(4.0 * math.pi * 1046.5 * t)))
    return out


def make_ui_deny() -> list:
    """界面·受阻（0.24s）：低频双脉冲、脉冲内下滑（175→149Hz）——「按不动 / 被拒」的回绝音。

    用下滑与低频（而不是高频蜂鸣）表达否定：本作 UI 是暖琥珀军事终端语汇，
    蜂鸣会读成警报（Danger 在本项目留给「须立即处置」，见 DESIGN_BASELINE §1.9.1）。
    """
    dur = 0.24
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        s = 0.0
        for t0 in (0.0, 0.10):
            if t >= t0:
                lt = t - t0
                env = min(lt / 0.004, 1.0) * math.exp(-lt * 26.0)
                # 线性下滑的解析相位（∫f dt = f0·t − k·t²/2），免去逐样本状态
                phase = 2.0 * math.pi * (175.0 * lt - 0.5 * 260.0 * lt * lt)
                s += env * (math.sin(phase) + 0.30 * math.sin(2.0 * phase))
        out.append(0.7 * s)
    return out


# ---------------- BGM ----------------

# 战斗曲速度（战斗 / Boss / 基地三档的唯一事实另一侧：视觉节拍层按
# data/balance.json effects.motion.bpm_battle 换算拍相位，两处必须一致）。
BPM = 120.0
BEAT = 60.0 / BPM
LOOP_DUR = 40.0
CHORD_DUR = 5.0  # 8 个和弦槽 × 5s = 40s

# BGM 生成种子：三首曲目各自从这个起点取相位（见 main() 的逐首重置）
BGM_SEED = 20260720

# 频率表（等程近似）
FREQ = {
    "C2": 65.41, "D2": 73.42, "E2": 82.41, "F2": 87.31, "G2": 98.00, "A2": 110.00, "B2": 123.47,
    "C3": 130.81, "C#3": 138.59, "D3": 146.83, "E3": 164.81, "F3": 174.61, "G3": 196.00,
    "A3": 220.00, "Bb3": 233.08, "B3": 246.94,
    "C4": 261.63, "C#4": 277.18, "D4": 293.66, "E4": 329.63, "F4": 349.23, "G4": 392.00,
    "A4": 440.00, "Bb4": 466.16, "B4": 493.88,
    "C5": 523.25, "D5": 587.33, "E5": 659.25,
    "Bb2": 116.54,
}

CHORDS = {
    "Am": (["A2", "C3", "E3", "A3"], "A2", ["A3", "C4", "E4", "A4"]),
    "F": (["F2", "A2", "C3", "F3"], "F2", ["F3", "A3", "C4", "F4"]),
    "C": (["C3", "E3", "G3", "C4"], "C2", ["C4", "E4", "G4", "C5"]),
    "G": (["G2", "B2", "D3", "G3"], "G2", ["G3", "B3", "D4", "G4"]),
}

PROGRESSION = ["Am", "F", "C", "G", "Am", "F", "C", "G"]
XFade = 0.5  # 和弦间交叉淡化时长

# 新增两首曲目的和声表：与上方共用同一套音色（pad + 低音 + 拨弦），只换进行/速度/力度。
# Boss 用和声小调（Gm→A 的导音制造压迫感），基地用大七和弦（无导音、无解决压力）。
BOSS_CHORDS = {
    "Dm": (["D3", "F3", "A3", "D4"], "D3", ["D4", "F4", "A4", "D5"]),
    "Bb": (["Bb2", "D3", "F3", "Bb3"], "Bb3", ["Bb3", "D4", "F4", "Bb4"]),
    "Gm": (["G2", "Bb2", "D3", "G3"], "G2", ["G3", "Bb3", "D4", "G4"]),
    "A": (["A2", "C#3", "E3", "A3"], "A2", ["A3", "C#4", "E4", "A4"]),
}
BOSS_PROGRESSION = ["Dm", "Bb", "Gm", "A", "Dm", "Bb", "Gm", "A"]

BASE_CHORDS = {
    "Cmaj7": (["C3", "E3", "G3", "B3"], "C3", ["C4", "E4", "G4", "B4"]),
    "Am7": (["A2", "C3", "E3", "G3"], "A2", ["A3", "C4", "E4", "G4"]),
    "Fmaj7": (["F2", "A2", "C3", "E3"], "F2", ["F3", "A3", "C4", "E4"]),
    "G": (["G2", "B2", "D3", "G3"], "G2", ["G3", "B3", "D4", "G4"]),
}
BASE_PROGRESSION = ["Cmaj7", "Am7", "Fmaj7", "G", "Cmaj7", "Am7", "Fmaj7", "G"]

# 新增曲目的生成参数（缺省值即既有曲目的音色与力度，见 make_bgm 签名）：
# 逐首从同一随机流起点生成，谱面微调只影响本首——同 bullet_fire 的种子重置口径。
EXTRA_BGM = (
    (
        "bgm_boss.wav",
        dict(
            progression=BOSS_PROGRESSION,
            chords=BOSS_CHORDS,
            bpm=150.0,  # 与 data/balance.json effects.motion.bpm_boss 一致（视觉节拍相位按它换算）
            loop_dur=32.0,
            chord_dur=4.0,
            arp_pattern=[0, 1, 2, 3, 2, 1, 2, 3],
            arp_step=60.0 / 150.0 / 4.0,  # 十六分音符驱动
            pad_edge=0.22,   # 垫音加二次谐波：同族正弦但更硬
            bass_gain=0.13,
            bass_edge=0.18,
            arp_gain=0.042,  # 音更密，单音力度压低（总能量与既有曲目相当）
            arp_len=0.16,
            arp_decay=20.0,
            xfade=0.4,
        ),
    ),
    (
        "bgm_base.wav",
        dict(
            progression=BASE_PROGRESSION,
            chords=BASE_CHORDS,
            bpm=84.0,  # 与 data/balance.json effects.motion.bpm_base 一致（视觉节拍相位按它换算）
            loop_dur=40.0,
            chord_dur=5.0,
            arp_pattern=[0, 2, 1, 3],  # 四分音符、窄跨度，钟音式点缀
            arp_step=60.0 / 84.0,
            pad_gain=0.062,        # 休整：垫音为主
            pad_lfo_cycles=1.0,    # 呼吸更慢（每圈一次）
            pad_trem=0.09,
            bass_gain=0.095,
            arp_gain=0.058,
            arp_len=0.55,          # 长尾钟音
            arp_decay=5.0,
            arp_edge=0.25,
            xfade=0.7,
        ),
    ),
)


def chord_weight(t: float, slot: int, chord_dur: float, loop_dur: float, xfade: float) -> float:
    """第 slot 个和弦槽在时刻 t 的权重，对 ±loop_dur 平移周期化以保证无缝循环。
    有效区间必须为 [0, chord_dur + xfade)：若在 chord_dur 处严格截断，相邻和弦
    交界点权重和为 0，每槽一次 pad/bass 零谷塌陷；区间扩展后槽尾衰减与
    下一槽头上升重叠，交界处权重和恒为 1（线性交叉淡化）。
    """
    w = 0.0
    for shift in (-loop_dur, 0.0, loop_dur):
        start = slot * chord_dur + shift
        local = t - start
        if 0.0 <= local < chord_dur + xfade:
            w += min(local / xfade, 1.0) * min((chord_dur + xfade - local) / xfade, 1.0)
    return w


def make_bgm(
    progression: list,
    chords: dict,
    *,
    bpm: float,
    loop_dur: float,
    chord_dur: float,
    arp_pattern: list,
    arp_step: float,
    pad_gain: float = 0.055,
    pad_trem: float = 0.15,
    pad_lfo_cycles: float = 2.0,
    pad_edge: float = 0.0,
    bass_gain: float = 0.11,
    bass_edge: float = 0.0,
    arp_gain: float = 0.05,
    arp_len: float = 0.22,
    arp_decay: float = 14.0,
    arp_edge: float = 0.0,
    xfade: float = XFade,
    fade: float = 0.05,
) -> list:
    """三首曲目共用的合成器：和弦垫 + 低音 + 琶音，首尾互补淡化后整段可无缝循环。
    缺省值即既有曲目（bgm_loop）的音色与力度，新增曲目只覆盖差异项。
    """
    n = int(SR * loop_dur)
    out = [0.0] * n

    for slot, chord_name in enumerate(progression):
        pad_notes, bass_note, arp_notes = chords[chord_name]
        # 和弦垫：慢起落的正弦叠加 + 周期化颤音
        for note in pad_notes:
            freq = FREQ[note]
            inc = 2.0 * math.pi * freq / SR
            phase = random.uniform(0.0, 2.0 * math.pi)
            lfo_phase = random.uniform(0.0, 2.0 * math.pi)
            for i in range(n):
                t = i / SR
                w = chord_weight(t, slot, chord_dur, loop_dur, xfade)
                if w <= 0.0:
                    phase += inc
                    continue
                trem = (1.0 - pad_trem) + pad_trem * math.sin(
                    2.0 * math.pi * pad_lfo_cycles * t / loop_dur + lfo_phase)
                wave = math.sin(phase) + pad_edge * math.sin(2.0 * phase)
                out[i] += pad_gain * w * trem * wave
                phase += inc
        # 低音：根音低八度
        bass_inc = 2.0 * math.pi * FREQ[bass_note] / 2.0 / SR
        phase = 0.0
        for i in range(n):
            t = i / SR
            w = chord_weight(t, slot, chord_dur, loop_dur, xfade)
            wave = math.sin(phase) + bass_edge * math.sin(2.0 * phase)
            out[i] += bass_gain * w * wave
            phase += bass_inc
        # 琶音：定长拨弦（arp_step 为音间隔）
        onset = slot * chord_dur
        while onset < (slot + 1) * chord_dur:
            idx = int((onset / arp_step)) % len(arp_pattern)
            freq = FREQ[arp_notes[arp_pattern[idx]]]
            start_i = int(onset * SR)
            pluck_len = int(arp_len * SR)
            for j in range(pluck_len):
                i = start_i + j
                if i >= n:
                    break
                lt = j / SR
                env = math.exp(-lt * arp_decay)
                out[i] += arp_gain * env * (
                    math.sin(2.0 * math.pi * freq * lt) + arp_edge * math.sin(4.0 * math.pi * freq * lt))
            onset += arp_step

    # 首尾交叉淡化——必须首尾互补：单边 50ms 淡入会使圈首 ≈5dB/50ms 凹陷、
    # 回绕点（圈末→0）波形跳变，与「无缝循环」相悖；首尾互补淡出后回绕点
    # 两侧均趋于 0、连续，播放起点仍防爆音
    fade_len = int(fade * SR)
    for i in range(fade_len):
        k = i / fade_len
        out[i] *= k
        out[n - 1 - i] *= k
    return out


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    write_wav("explosion.wav", make_explosion(0.7, 90.0, 6.0))
    write_wav("explosion_big.wav", make_explosion_big())
    write_wav("player_hit.wav", make_player_hit())
    write_wav("buff_pick.wav", make_buff_pick())
    write_wav("dash.wav", make_dash())
    write_wav("resupply.wav", make_resupply())
    write_wav("heartbeat.wav", make_heartbeat())
    write_wav(
        "bgm_loop.wav",
        make_bgm(PROGRESSION, CHORDS, bpm=BPM, loop_dur=LOOP_DUR, chord_dur=CHORD_DUR,
                 arp_pattern=[0, 1, 2, 3, 2, 1, 2, 3], arp_step=BEAT / 2.0))
    # 新增两首曲目：逐首把随机流复位到同一起点再接既有序列——Boss 与基地曲目的相互独立
    # （改一首的谱面不会改到另一首的垫音相位），且既有曲目与音效的随机流顺序不受影响
    for name, params in EXTRA_BGM:
        random.seed(BGM_SEED)
        write_wav(name, make_bgm(**params))
    # bullet_fire 三变体必须在调用前重置种子对齐资产——它们是在「random 流起点
    # 独立生成」的，在全序列流中生成会得到不同音色（实测 max 差 ~3000/16bit）；
    # 重置保证全量重跑输出与提交资产逐字节一致（bf 位于 main() 末段，不影响其他音效）
    random.seed(BGM_SEED)
    write_wav("bullet_fire.wav", make_bullet_fire(135.0, 0.8), peak_target=0.42)
    write_wav("bullet_fire_b.wav", make_bullet_fire(115.0, 0.65), peak_target=0.42)
    write_wav("bullet_fire_c.wav", make_bullet_fire(160.0, 1.0), peak_target=0.42)
    # 界面反馈族：合成函数是纯音（零 random 调用），本段既不消费也不移动随机流位置，
    # 上方全部产物逐字节不变；调用前的 seed 是护栏——日后若给这几个函数加噪声源，
    # 它们仍从已知起点取相位，不会把既有音效/BGM 的流位置往后推。
    random.seed(BGM_SEED)
    write_wav("ui_hover.wav", make_ui_hover())
    write_wav("ui_confirm.wav", make_ui_confirm())
    write_wav("ui_cancel.wav", make_ui_cancel())
    write_wav("ui_toggle.wav", make_ui_toggle())
    write_wav("ui_deny.wav", make_ui_deny())


if __name__ == "__main__":
    main()
