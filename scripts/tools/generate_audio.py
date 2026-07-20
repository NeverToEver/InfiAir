#!/usr/bin/env python3
"""一次性音频程序合成脚本（仅 Python 标准库）。

生成以下产物到 assets/audio/：
- explosion.wav      敌机爆炸（噪声爆破 + 低频衰减）
- explosion_big.wav  Boss/精英爆炸（更长、更低沉、双层噪声）
- player_hit.wav     玩家受击（短促噪声 + 下滑音）
- buff_pick.wav      Buff 确认音（上行双音）
- bgm_loop.wav       40s 无缝循环氛围电子 BGM（和弦垫 + 琶音 + 低音）

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


def write_wav(name: str, samples: list) -> None:
    peak = max(1e-9, max(abs(s) for s in samples))
    scale = 0.89 / peak
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


# ---------------- BGM ----------------

BPM = 120.0
BEAT = 60.0 / BPM
LOOP_DUR = 40.0
CHORD_DUR = 5.0  # 8 个和弦槽 × 5s = 40s

# 频率表（等程近似）
FREQ = {
    "C2": 65.41, "D2": 73.42, "E2": 82.41, "F2": 87.31, "G2": 98.00, "A2": 110.00, "B2": 123.47,
    "C3": 130.81, "D3": 146.83, "E3": 164.81, "F3": 174.61, "G3": 196.00, "A3": 220.00, "B3": 246.94,
    "C4": 261.63, "D4": 293.66, "E4": 329.63, "F4": 349.23, "G4": 392.00, "A4": 440.00, "B4": 493.88,
    "C5": 523.25, "D5": 587.33, "E5": 659.25,
}

CHORDS = {
    "Am": (["A2", "C3", "E3", "A3"], "A2", ["A3", "C4", "E4", "A4"]),
    "F": (["F2", "A2", "C3", "F3"], "F2", ["F3", "A3", "C4", "F4"]),
    "C": (["C3", "E3", "G3", "C4"], "C2", ["C4", "E4", "G4", "C5"]),
    "G": (["G2", "B2", "D3", "G3"], "G2", ["G3", "B3", "D4", "G4"]),
}

PROGRESSION = ["Am", "F", "C", "G", "Am", "F", "C", "G"]
XFade = 0.5  # 和弦间交叉淡化时长


def chord_weight(t: float, slot: int) -> float:
    """第 slot 个和弦槽在时刻 t 的权重，对 ±LOOP_DUR 平移周期化以保证无缝循环。"""
    w = 0.0
    for shift in (-LOOP_DUR, 0.0, LOOP_DUR):
        start = slot * CHORD_DUR + shift
        local = t - start
        if 0.0 <= local < CHORD_DUR:
            w += min(local / XFade, 1.0) * min((CHORD_DUR - local) / XFade, 1.0)
    return w


def make_bgm() -> list:
    n = int(SR * LOOP_DUR)
    out = [0.0] * n
    arp_pattern = [0, 1, 2, 3, 2, 1, 2, 3]
    step = BEAT / 2.0  # 八分音符

    for slot, chord_name in enumerate(PROGRESSION):
        pad_notes, bass_note, arp_notes = CHORDS[chord_name]
        # 和弦垫：慢起落的正弦叠加 + 周期化颤音
        for note in pad_notes:
            freq = FREQ[note]
            inc = 2.0 * math.pi * freq / SR
            phase = random.uniform(0.0, 2.0 * math.pi)
            lfo_phase = random.uniform(0.0, 2.0 * math.pi)
            for i in range(n):
                t = i / SR
                w = chord_weight(t, slot)
                if w <= 0.0:
                    phase += inc
                    continue
                trem = 0.85 + 0.15 * math.sin(2.0 * math.pi * 2.0 * t / LOOP_DUR + lfo_phase)
                out[i] += 0.055 * w * trem * math.sin(phase)
                phase += inc
        # 低音：根音低八度
        bass_inc = 2.0 * math.pi * FREQ[bass_note] / 2.0 / SR
        phase = 0.0
        for i in range(n):
            t = i / SR
            w = chord_weight(t, slot)
            out[i] += 0.11 * w * math.sin(phase)
            phase += bass_inc
        # 琶音：八分音符拨弦，跨越接缝处用周期化起始点
        onset = slot * CHORD_DUR
        while onset < (slot + 1) * CHORD_DUR:
            for o in (onset, onset + LOOP_DUR if onset < XFade else None):
                if o is None or o >= LOOP_DUR:
                    continue
                idx = int((o / step)) % len(arp_pattern)
                freq = FREQ[arp_notes[arp_pattern[idx]]]
                start_i = int(o * SR)
                pluck_len = int(0.22 * SR)
                for j in range(pluck_len):
                    i = start_i + j
                    if i >= n:
                        break
                    lt = j / SR
                    env = math.exp(-lt * 14.0)
                    out[i] += 0.05 * env * math.sin(2.0 * math.pi * freq * lt)
            onset += step

    # 全局软起音，避免开头爆音
    fade = int(0.05 * SR)
    for i in range(fade):
        out[i] *= i / fade
    return out


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    write_wav("explosion.wav", make_explosion(0.7, 90.0, 6.0))
    write_wav("explosion_big.wav", make_explosion_big())
    write_wav("player_hit.wav", make_player_hit())
    write_wav("buff_pick.wav", make_buff_pick())
    write_wav("bgm_loop.wav", make_bgm())


if __name__ == "__main__":
    main()
