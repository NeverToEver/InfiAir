#!/usr/bin/env python3
"""SFX 逐文件体检 + 微调工具（纯标准库，assets/audio 均为 mono/44100/16bit PCM）。

用法：
  python3 scripts/tools/tune_sfx.py            # 体检报告（只读）
  python3 scripts/tools/tune_sfx.py --apply    # 按 TUNING 表处理并写回，输出前后对比

处理链（每文件可配）：直流/次声滤除 → 频段整形（shelf/peak biquad）→ 截尾淡出 →
起振淡入 → 峰值归一到目标 dBFS。目标电平与 SfxPlayer.BaseDb 叠加后构成游戏内实际响度。
"""
import array
import math
import sys
import wave

SRC_DIR = "assets/audio"
FS = 44100
FULL = 32768.0


# ---------------- biquad（RBJ cookbook，direct form 1） ----------------

def _shelve(f0, gain_db, high):
    A = 10 ** (gain_db / 40.0)
    w0 = 2 * math.pi * f0 / FS
    c, s = math.cos(w0), math.sin(w0)
    alpha = s / 2 * math.sqrt((A + 1 / A) * (1 / 0.707 - 1) + 2)  # S=0.707 斜率
    sq = 2 * math.sqrt(A) * alpha
    if high:
        b0 = A * ((A + 1) + (A - 1) * c + sq)
        b1 = -2 * A * ((A - 1) + (A + 1) * c)
        b2 = A * ((A + 1) + (A - 1) * c - sq)
        a0 = (A + 1) - (A - 1) * c + sq
        a1 = 2 * ((A - 1) - (A + 1) * c)
        a2 = (A + 1) - (A - 1) * c - sq
    else:
        b0 = A * ((A + 1) - (A - 1) * c + sq)
        b1 = 2 * A * ((A - 1) - (A + 1) * c)
        b2 = A * ((A + 1) - (A - 1) * c - sq)
        a0 = (A + 1) + (A - 1) * c + sq
        a1 = -2 * ((A - 1) + (A + 1) * c)
        a2 = (A + 1) + (A - 1) * c - sq
    return [v / a0 for v in (b0, b1, b2, a1, a2)]


def _peak(f0, gain_db, q=1.2):
    A = 10 ** (gain_db / 40.0)
    w0 = 2 * math.pi * f0 / FS
    c, s = math.cos(w0), math.sin(w0)
    alpha = s / (2 * q)
    a0 = 1 + alpha / A
    return [
        (1 + alpha * A) / a0,
        (-2 * c) / a0,
        (1 - alpha * A) / a0,
        (-2 * c) / a0,
        (1 - alpha / A) / a0,
    ]


def _lowpass(f0, q=0.707):
    w0 = 2 * math.pi * f0 / FS
    c, s = math.cos(w0), math.sin(w0)
    alpha = s / (2 * q)
    a0 = 1 + alpha
    return [
        ((1 - c) / 2) / a0,
        (1 - c) / a0,
        ((1 - c) / 2) / a0,
        (-2 * c) / a0,
        (1 - alpha) / a0,
    ]


def _highpass(f0, q=0.707):
    w0 = 2 * math.pi * f0 / FS
    c, s = math.cos(w0), math.sin(w0)
    alpha = s / (2 * q)
    a0 = 1 + alpha
    return [
        ((1 + c) / 2) / a0,
        (-(1 + c)) / a0,
        ((1 + c) / 2) / a0,
        (-2 * c) / a0,
        (1 - alpha) / a0,
    ]


def biquad(x, coef):
    b0, b1, b2, a1, a2 = coef
    x1 = x2 = y1 = y2 = 0.0
    out = [0.0] * len(x)
    for i, xi in enumerate(x):
        y = b0 * xi + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        out[i] = y
        x2, x1 = x1, xi
        y2, y1 = y1, y
    return out


def pwr(x):
    return sum(v * v for v in x) / max(len(x), 1)


# ---------------- 分析 ----------------

def read_wav(path):
    w = wave.open(path)
    assert (w.getnchannels(), w.getframerate(), w.getsampwidth()) == (1, FS, 2), path
    data = array.array("h", w.readframes(w.getnframes()))
    w.close()
    return data


def analyze(data):
    x = [v / FULL for v in data]
    peak = max(abs(v) for v in x) or 1e-9
    rms = math.sqrt(pwr(x))
    n_clip = sum(1 for v in data if abs(v) >= 32767)
    # 频段能量占比（累计低通作差，省 5 遍全带滤波）
    lp = [biquad(x, _lowpass(f)) for f in (100, 300, 2000, 6000)]
    total = pwr(x)
    p = [pwr(v) for v in lp]
    bands = [
        100 * p[0] / total,
        100 * (p[1] - p[0]) / total,
        100 * (p[2] - p[1]) / total,
        100 * (p[3] - p[2]) / total,
        100 * (total - p[3]) / total,
    ]
    end_hot = 0
    floor = 0.005
    while end_hot < len(x) and abs(x[-1 - end_hot]) > floor:
        end_hot += 1
    return {
        "peak_db": 20 * math.log10(peak),
        "rms_db": 20 * math.log10(rms) if rms > 0 else -192.0,
        "clip": n_clip,
        "bands": bands,
        "start_amp": abs(x[0]),
        "end_hot_ms": 1000 * end_hot / FS,
    }


def fmt_report(name, r):
    b = r["bands"]
    return (f"{name:22s} peak={r['peak_db']:6.1f} rms={r['rms_db']:6.1f} "
            f"crest={r['peak_db'] - r['rms_db']:4.1f} clip={r['clip']:5d} "
            f"带%: <100:{b[0]:4.1f} 100-300:{b[1]:4.1f} 0.3-2k:{b[2]:4.1f} "
            f"2-6k:{b[3]:4.1f} >6k:{b[4]:4.1f} 起振:{r['start_amp'] * 100:4.1f}% "
            f"截尾:{r['end_hot_ms']:5.1f}ms")


# ---------------- 处理配置 ----------------
# 每文件：filters=[(工厂, 参数...)]；target_db=峰值目标；fade_out_ms 截尾淡出（0=按检测）；
# fade_in_ms 起振淡入（0=按检测）。BGM 不在音效库范围，不处理。
TUNING = {
    # 枪声 91-96% 能量压在 <300Hz：去浑浊（100Hz 高切 + 250Hz 搁浅衰减），不补高频（样本里没有的内容 boost 出来是噪声）
    "bullet_fire.wav": {"filters": [("highpass", 100, 0), ("lowshelf", 250, -2.0)], "target_db": -6.0},
    "bullet_fire_b.wav": {"filters": [("highpass", 100, 0), ("lowshelf", 250, -2.0)], "target_db": -6.0},
    "bullet_fire_c.wav": {"filters": [("highpass", 100, 0), ("lowshelf", 250, -2.0)], "target_db": -6.0},
    # 爆炸低频已足（59-78% <100Hz），只驯 >4k 脆响；起振 25% 硬启动/截尾由自动淡入淡出兜住
    "explosion.wav": {"filters": [("highshelf", 4000, -3.0)], "target_db": -6.0},
    "explosion_big.wav": {"filters": [("highshelf", 4000, -3.0)], "target_db": -6.0},
    # 受击尾被硬切（截尾 344ms）+ >6k 11.4% 嘶脆
    "player_hit.wav": {"filters": [("highshelf", 4000, -2.5)], "target_db": -7.0},
    # dash 是唯一真正的高频刺耳源（>6k 占 16.5% 嘶声）
    "dash.wav": {"filters": [("highshelf", 5000, -4.0)], "target_db": -8.0},
    # 频谱健康，纯峰值归一
    "buff_pick.wav": {"filters": [], "target_db": -7.0},
    "resupply.wav": {"filters": [], "target_db": -7.0},
    "heartbeat.wav": {"filters": [], "target_db": -9.0},  # 90.8% <100Hz 纯低频，只压电平+截尾淡出
}
HPF_HZ = 30  # 全部先过 30Hz 高通：去直流 + 次声吃掉却听不到的动态余量


def process(name):
    path = f"{SRC_DIR}/{name}"
    cfg = TUNING[name]
    before = analyze(read_wav(path))
    x = biquad([v / FULL for v in read_wav(path)], _highpass(HPF_HZ))
    desc = [f"hp{HPF_HZ}"]
    for spec in cfg["filters"]:
        kind, f0, gain = spec
        if kind == "highpass":
            x = biquad(x, _highpass(f0))
        elif kind == "peak":
            x = biquad(x, _peak(f0, gain))
        else:
            x = biquad(x, _shelve(f0, gain, kind == "highshelf"))
        desc.append(f"{kind}@{f0}:{gain:+.1f}")
    # 截尾淡出：文件在仍可闻时被硬切（末样超地板）则淡出 25ms 防咔哒
    end_hot = 0
    while end_hot < len(x) and abs(x[-1 - end_hot]) > 0.005:
        end_hot += 1
    fo = cfg.get("fade_out_ms") or (25 if end_hot > 0 else 0)
    if fo:
        n = min(len(x), int(FS * fo / 1000))
        for i in range(n):
            x[-1 - i] *= i / n
        desc.append(f"fadeout{fo}ms")
    # 起振淡入：首样幅度显著才做（2ms），钝化不了瞬态
    fi = cfg.get("fade_in_ms", 0) or (2 if abs(x[0]) > 0.02 else 0)
    if fi:
        n = min(len(x), int(FS * fi / 1000))
        for i in range(n):
            x[i] *= i / n
        desc.append(f"fadein{fi}ms")
    # 峰值归一
    peak = max(abs(v) for v in x) or 1e-9
    g = 10 ** (cfg["target_db"] / 20.0) / peak
    x = [v * g for v in x]
    out = array.array("h", (max(-32768, min(32767, round(v * FULL))) for v in x))
    w = wave.open(path, "wb")
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(FS)
    w.writeframes(out.tobytes())
    w.close()
    after = analyze(read_wav(path))
    print(f"{name}\n  处理: {' '.join(desc)} norm->{cfg['target_db']:.0f}dB\n"
          f"  前: {fmt_report(name, before)}\n  后: {fmt_report(name, after)}")


if __name__ == "__main__":
    names = sorted(TUNING)
    if "--apply" in sys.argv:
        for n in names:
            process(n)
    else:
        for n in names:
            print(fmt_report(n, analyze(read_wav(f"{SRC_DIR}/{n}"))))
