#!/usr/bin/env python3
"""InfiAir 数值管理器（balance editor）

本机可视化编辑 data/balance.json：分区树形展示全部可调数值，按 scripts/tools/balance_meta.json
显示中文说明、单位与取值范围，标黄未保存改动，保存前服务端递归校验结构与类型和现文件一致
（键集双向相等——编辑器加不了也删不了键），临时文件 + os.replace 原子落盘，
并自动备份（balance.json.bak，可一键回滚）。另一页签是被动分析面板（难度曲线 / 战斗节奏 /
经济与进度 / 增幅收益 / 取值体检），公式出处标在每一块上，见 scripts/tools/balance_analysis.py。

用法：
    python3 scripts/tools/balance_editor.py [--port 8931] [--no-browser] [--balance PATH] [--readonly]
                                          [--idle-timeout 30]

生命周期（防「开了不关」，见 Lifecycle）：页面关掉即自动退出；闲置超过 --idle-timeout 分钟
也自动退出；端口上已有本工具实例时不再起第二个，直接打开那个（多标签页用页面 id 区分，
关掉其中一个不会误判为全部关闭）。

仅依赖 Python 标准库。改完数值后跑门禁（python3 scripts/ci/gates.py）。
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
UI_DIR = HERE / "balance_editor_ui"
DEFAULT_BALANCE = ROOT / "data" / "balance.json"

# 单实例探测用的身份串：端口上跑着的若不是本工具，就不能当成「已有实例」直接复用
APP_ID = "infiair-balance-editor"
APP_PROTOCOL = 1

# 页面关闭后的宽限（秒）：刷新、换页、开新标签都会先来一次 bye，等这么久没有新心跳才真退
PAGE_CLOSE_GRACE = 5.0
# 页面心跳超时（秒）：超过这么久没心跳就当作该页面已消失（崩溃或被强杀时收不到 bye）
PAGE_TTL = 40.0
# 看门狗轮询间隔（秒）：决定「关掉页面」到「进程消失」的延迟上限（宽限 + 间隔 ≈ 7 秒），
# 取值同时是空闲判定的精度；2 秒一次的时间戳比较对 CPU 完全无感
WATCHDOG_INTERVAL = 2.0

sys.path.insert(0, str(HERE))
import balance_analysis as analysis  # noqa: E402  （同目录模块，入口脚本里按需 import）


# ------------------------------------------------------------------ 生命周期


class Lifecycle:
    """服务器该不该自己退出：空闲超时 + 页面心跳 + 页面关闭通知。

    为什么要它：这是本机临时起的服务，用户（尤其是只想改两个数的人）关掉标签页之后
    常常忘了后台还挂着一个进程——端口占着、内存占着，下次再开还报端口被占。
    判定做成纯函数（should_exit 只看时间戳），可单测、不依赖真实时钟推进。
    """

    def __init__(self, idle_timeout: float, now: float = 0.0):
        self.idle_timeout = idle_timeout          # ≤0 表示不按空闲退出
        self.started_at = now
        self.last_seen = now                      # 最后一次任何请求
        self.page_seen = False                    # 是否曾有页面连上（没页面时不必等心跳）
        self.pages: dict[str, float] = {}         # 页面 id → 最后心跳时刻
        self.closed_at: float | None = None       # 页面全部关闭的时刻

    def touch(self, now: float) -> None:
        self.last_seen = now

    def ping(self, page_id: str, now: float) -> None:
        self.page_seen = True
        self.pages[page_id] = now
        self.closed_at = None
        self.touch(now)

    def bye(self, page_id: str, now: float) -> None:
        self.pages.pop(page_id, None)
        if not self.pages:
            self.closed_at = now

    def should_exit(self, now: float) -> str | None:
        """返回退出原因（None = 继续跑）。判定只看时间戳，便于测试直接喂时间。"""
        for page_id, seen in list(self.pages.items()):
            if now - seen > PAGE_TTL:
                del self.pages[page_id]                # 崩掉的页面：靠心跳超时清理
        if not self.pages and self.closed_at is not None and now - self.closed_at > PAGE_CLOSE_GRACE:
            return "页面已关闭"
        if self.idle_timeout > 0 and now - self.last_seen > self.idle_timeout:
            return f"空闲超过 {self.idle_timeout / 60:.0f} 分钟"
        return None


# ------------------------------------------------------------------ 纯逻辑


def render_save(payload: object, path: Path) -> str:
    """序列化待落盘内容，行尾沿用现文件（与运行平台无关）。

    Path.write_text 走 newline=None：写入时 `\\n` 按 os.linesep 展开（Windows CRLF / Linux LF），
    于是同一份全 CRLF 的 balance.json 在 Linux 上保存一次就整文件翻成 LF，diff 全红淹没真实改值。
    改为「先取现文件行尾、自行把 json.dumps 的 LF 全量展开、再以 newline="" 写入」——文件既是
    单源，行尾也随它走。只换末尾那一个换行不够：正文的 LF 仍会被原样写出，文件变成混合行尾。
    """
    newline = "\r\n" if b"\r\n" in path.read_bytes() else "\n"
    body = json.dumps(payload, indent="\t", ensure_ascii=False)
    return body.replace("\n", newline) + newline if newline != "\n" else body + "\n"


def check_shape(new: object, old: object, path: str = "") -> list[str]:
    """递归校验结构与标量类型和现文件一致（键集双向相等；数组只要求元素类型一致，长度可变）。

    键集必须**双向**相等：编辑器的行集完全由现文件的键生成，加不了键也删不了键，
    故合法保存不可能引入新键——只判「旧键都在」时，手工构造的 POST 能往 balance.json
    塞界面上看不见的键（界面看不到，改起来只能全文件搜，且没人知道它该不该有）。
    数组长度仍可变：UI 把数字数组编成一行逗号表，本就支持增删元素，只按首元素判元素类型。
    """
    errs: list[str] = []
    where = path or "<root>"
    if isinstance(old, dict):
        if not isinstance(new, dict):
            return [f"{where}: 应为对象"]
        for k in old:
            if k not in new:
                errs.append(f"{where}.{k}: 缺失")
        for k in new:
            if k not in old:
                errs.append(f"{where}.{k}: 现文件无此键（编辑器加不了新键，拒绝写入未知键）")
        for k in old:
            if k in new:
                errs.extend(check_shape(new[k], old[k], f"{where}.{k}"))
    elif isinstance(old, list):
        if not isinstance(new, list):
            return [f"{where}: 应为数组"]
        if not old:
            return errs  # 现文件该数组为空：无模板可判
        for i, item in enumerate(new):
            # 元素按位比对（超出长度的新元素拿首元素当模板）：对象数组在 UI 里按位置改值，
            # 元素形状本就允许逐位不同（boss 某阶段的 waves / duration 交替），
            # 一律拿 old[0] 当模板会对着原样文件报缺键——保存被整体拒掉，编辑器等于不可用。
            errs.extend(check_shape(item, old[i] if i < len(old) else old[0], f"{where}[{i}]"))
    elif isinstance(old, bool):  # bool 是 int 子类，必须先判
        if not isinstance(new, bool):
            errs.append(f"{where}: 应为布尔")
    elif isinstance(old, (int, float)):
        if not isinstance(new, (int, float)) or isinstance(new, bool):
            errs.append(f"{where}: 应为数字")
    elif isinstance(old, str):
        if not isinstance(new, str):
            errs.append(f"{where}: 应为字符串")
    return errs


def diff_values(new: object, old: object, path: str = "") -> list[dict]:
    """列出全部差异叶子（路径 / 旧值 / 新值）。

    数字数组整体当一个叶子（界面就是一行逗号表，逐元素报差异只会把一行拆成十几条噪音），
    对象数组逐元素下钻（boss 的阶段表是真按位置编辑的）。
    比较只看值不看 int/float 之分：JSON 往返后 `10.0` 与 `10` 在 Python 里相等，
    把它们判成差异会让「没动过任何键」也报出一串改动。
    """
    out: list[dict] = []
    if isinstance(old, dict) and isinstance(new, dict):
        for key in old:
            if key in new:
                out.extend(diff_values(new[key], old[key], f"{path}.{key}".lstrip(".")))
        return out
    if isinstance(old, list) and isinstance(new, list) and any(isinstance(v, dict) for v in old):
        for i in range(max(len(new), len(old))):
            if i < len(new) and i < len(old):
                out.extend(diff_values(new[i], old[i], f"{path}[{i}]"))
            else:
                out.append({"path": f"{path}[{i}]",
                            "old": old[i] if i < len(old) else None,
                            "new": new[i] if i < len(new) else None})
        return out
    if new != old:
        out.append({"path": path, "old": old, "new": new})
    return out


def restore_numeric_kinds(new: object, old: object) -> object:
    """按现文件的数值形态回写 new：现文件是浮点就保持浮点，是整数就保持整数（值不变）。

    JSON 数字没有类型：`0.1333` 过一趟浏览器回来仍是浮点，但 `1.0` 回来会变成 `1`，
    于是「只改了别处」的一次保存会在 git diff 里带出成片的 `1.0 → 1`。
    两边值相等时按旧文件的形态归一（值不等时不动——那是用户真改了值，形态随新值走）。
    """
    if isinstance(old, dict) and isinstance(new, dict):
        return {key: restore_numeric_kinds(new[key], old[key]) if key in old else new[key] for key in new}
    if isinstance(old, list) and isinstance(new, list):
        return [restore_numeric_kinds(item, old[i] if i < len(old) else None) for i, item in enumerate(new)]
    if (analysis.is_num(old) and analysis.is_num(new) and old == new
            and isinstance(old, float) != isinstance(new, float)):
        return float(old) if isinstance(old, float) else int(old)
    return new


# ------------------------------------------------------------------ 服务端


class Editor:
    """编辑器状态：文件路径、只读开关、备份路径。请求处理器只做协议转换，判定都收在这里。"""

    def __init__(self, balance_path: Path, readonly: bool = False):
        self.balance = balance_path
        self.backup = balance_path.with_suffix(balance_path.suffix + ".bak")
        self.readonly = readonly
        self._state_cache: tuple[tuple, bytes] | None = None

    def read_balance(self) -> dict:
        return json.loads(self.balance.read_text(encoding="utf-8"))

    def _signature(self) -> tuple:
        stat = self.balance.stat()
        try:
            backup = self.backup.stat()
            backup_key: tuple | None = (backup.st_mtime_ns, backup.st_size)
        except OSError:
            backup_key = None
        return (stat.st_mtime_ns, stat.st_size, backup_key, self.readonly)

    def state_json(self) -> bytes:
        """带缓存的 /api/state 响应体。

        这份响应里有键说明展开出来的 580 余条记录（每条约百字节），而浏览器每次刷新、
        每次保存后都会要一次——逐次重新解析与序列化纯属浪费。缓存键取「文件与备份的
        mtime+size + 只读开关」，任何一个变了就重算；文件被编辑器之外的工具改动同样失效。
        """
        signature = self._signature()
        if self._state_cache is not None and self._state_cache[0] == signature:
            return self._state_cache[1]
        payload = json.dumps(self.state(), ensure_ascii=False).encode("utf-8")
        self._state_cache = (signature, payload)
        return payload

    def presets(self) -> list[dict]:
        """预设清单（只发界面要用的字段；ops 留在文件里，应用时由服务端展开成变更清单）。"""
        out = []
        for preset in analysis.load_presets():
            if not isinstance(preset, dict) or "id" not in preset:
                continue
            out.append({"id": preset["id"], "name": preset.get("name", preset["id"]),
                        "desc": preset.get("desc", ""), "tags": preset.get("tags", [])})
        return out

    def preset_plan(self, preset_id: str, balance: object) -> tuple[int, dict]:
        """把预设按**当前编辑值**展开成变更清单（真正落值在前端，可一步撤销）。"""
        if not isinstance(balance, dict):
            return 400, {"ok": False, "message": "请求体里没有数值表"}
        preset = next((p for p in analysis.load_presets() if isinstance(p, dict) and p.get("id") == preset_id), None)
        if preset is None:
            return 404, {"ok": False, "message": f"没有这个预设：{preset_id}"}
        plan = analysis.plan_preset(preset, balance)
        return 200, {"ok": True, "changes": plan["changes"], "skipped": plan["skipped"],
                     "name": preset.get("name", preset_id)}

    def origin_balance(self) -> tuple[int, dict]:
        """已提交版本（git HEAD 里的那份）：给「调乱了想回官方」一个不依赖备份链的落点。

        备份只有上一版，预设又是相对变换（点两次会叠加），所以「回到已知良好状态」
        需要一份稳定基准——仓库里已提交的那份正是这个语义。
        """
        try:
            rel = self.balance.resolve().relative_to(ROOT).as_posix()
        except ValueError:
            return 404, {"ok": False, "message": "该文件不在仓库内，取不到已提交版本"}
        try:
            proc = subprocess.run(["git", "show", f"HEAD:{rel}"], cwd=str(ROOT),
                                  capture_output=True, timeout=15)
        except (OSError, subprocess.SubprocessError) as e:
            return 404, {"ok": False, "message": f"调用 git 失败：{e}"}
        if proc.returncode != 0:
            return 404, {"ok": False, "message": "取不到已提交版本（不在 git 仓库，或该文件尚未提交）"}
        try:
            return 200, {"ok": True, "balance": json.loads(proc.stdout.decode("utf-8"))}
        except ValueError as e:
            return 404, {"ok": False, "message": f"已提交版本不是合法 JSON：{e}"}

    def state(self) -> dict:
        meta = analysis.load_meta()
        balance = self.read_balance()
        stat = self.balance.stat()
        backup_stat = self.backup.stat() if self.backup.exists() else None
        try:
            relative = str(self.balance.relative_to(ROOT))
        except ValueError:
            relative = str(self.balance)   # 指向仓库外的副本（--balance）时按绝对路径显示
        return {
            "balance": balance,
            "meta": analysis.expand_meta(meta, balance),
            "sections": meta.get("sections", {}),
            "file": {
                "path": str(self.balance),
                "relative": relative,
                "mtime": stat.st_mtime,
                "size": stat.st_size,
            },
            "backup": None if backup_stat is None else {
                "path": str(self.backup), "mtime": backup_stat.st_mtime, "size": backup_stat.st_size,
            },
            "readonly": self.readonly,
        }

    def save(self, payload: object) -> tuple[int, dict]:
        """校验 → 备份 → 原子落盘。返回 (HTTP 码, 响应体)。"""
        if self.readonly:
            return 400, {"ok": False, "message": "只读模式：本次启动未开启保存"}
        try:
            current = self.read_balance()
        except (ValueError, OSError) as e:
            # 读侧裸异常必须兜成可读诊断：balance.json 损坏时若裸 traceback，编辑器无法说明原因
            return 400, {"ok": False, "message": f"保存失败：读取 balance.json 失败（文件损坏？）{e}"}
        errors = check_shape(payload, current)
        if errors:
            return 400, {"ok": False, "message": "结构/类型与现文件不一致",
                         "errors": errors[:20]}
        changes = diff_values(payload, current)
        if not changes:
            return 200, {"ok": True, "message": "内容与磁盘一致，未写入", "changes": []}
        # 数值形态按现文件归一（见 restore_numeric_kinds：防「只改一处」带出成片的 1.0 → 1）
        payload = restore_numeric_kinds(payload, current)
        # 备份 + 原子落盘（临时文件同目录 os.replace，写一半不会损坏原文件）
        try:
            shutil.copy2(self.balance, self.backup)
            tmp = self.balance.with_suffix(self.balance.suffix + ".tmp")
            # newline="" 关闭换行翻译，行尾由 render_save 显式给出（见其说明）
            with open(tmp, "w", encoding="utf-8", newline="") as handle:
                handle.write(render_save(payload, self.balance))
            os.replace(tmp, self.balance)
        except OSError as e:
            # 写盘侧 OSError 必须兜底为可读响应——磁盘满/只读/权限不足时若裸抛 traceback，客户端收不到响应
            return 400, {"ok": False, "message": f"保存失败：写入/备份失败（磁盘满或权限不足？）{e}"}
        stat = self.balance.stat()
        return 200, {"ok": True, "changes": changes, "backup": str(self.backup),
                     "file": {"mtime": stat.st_mtime, "size": stat.st_size},
                     "message": f"已保存 {len(changes)} 处改动（原文件备份为 {self.backup.name}）"}

    def revert(self) -> tuple[int, dict]:
        """从备份回滚：把 .bak 覆盖回 balance.json（回滚本身也留一份回滚前的现场）。"""
        if self.readonly:
            return 400, {"ok": False, "message": "只读模式：本次启动未开启回滚"}
        if not self.backup.exists():
            return 400, {"ok": False, "message": "没有备份可回滚（尚未保存过一次）"}
        try:
            changes = diff_values(json.loads(self.balance.read_text(encoding="utf-8")),
                                  json.loads(self.backup.read_text(encoding="utf-8")))
            shutil.copy2(self.balance, self.balance.with_suffix(self.balance.suffix + ".pre-revert"))
            shutil.copy2(self.backup, self.balance)
        except (OSError, ValueError) as e:
            return 400, {"ok": False, "message": f"回滚失败：{e}"}
        return 200, {"ok": True, "changes": changes, "message": f"已回滚 {len(changes)} 处（回滚前现场存为 .pre-revert）"}


def make_handler(editor: Editor, life: Lifecycle) -> type[BaseHTTPRequestHandler]:
    class Handler(BaseHTTPRequestHandler):
        # 连接复用：页面一次加载要连发 4~5 个请求，再叠加 10 秒一次的心跳
        protocol_version = "HTTP/1.1"
        server_version = "InfiAirBalanceEditor"
        # 请求体上限：数值表只有几十 KB，给足余量即可拦住畸形/恶意的大包把内存吃掉
        MAX_BODY = 32 * 1024 * 1024

        def log_message(self, fmt, *args):  # 静音请求日志（本机单用户工具，日志只会刷屏）
            pass

        def _send(self, code: int, body: bytes, ctype: str) -> None:
            self.send_response(code)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def _send_text(self, code: int, body: str, ctype: str = "text/plain; charset=utf-8") -> None:
            self._send(code, body.encode("utf-8"), ctype)

        def _send_json(self, code: int, payload: object) -> None:
            self._send(code, json.dumps(payload, ensure_ascii=False).encode("utf-8"),
                       "application/json; charset=utf-8")

        def _read_body(self) -> object:
            length = int(self.headers.get("Content-Length") or 0)
            if length > self.MAX_BODY:
                raise ValueError(f"请求体过大（{length} 字节）")
            return json.loads(self.rfile.read(length) or b"null")

        @staticmethod
        def _page_id(query: str) -> str:
            """页面标识：心跳与关闭通知都带上它，多标签页才不会被「关掉一个」误判为全部关闭。"""
            params = urllib.parse.parse_qs(query)
            return (params.get("page") or ["default"])[0][:64]

        # ---- 静态资源：限定在 UI_DIR 内，杜绝 ../ 穿越读到仓库其它文件
        def _serve_asset(self, name: str) -> None:
            target = (UI_DIR / name).resolve()
            if not target.is_file() or UI_DIR.resolve() not in target.parents:
                self._send_text(404, "not found")
                return
            ctype = {".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8",
                     ".css": "text/css; charset=utf-8"}.get(target.suffix, "application/octet-stream")
            self._send(200, target.read_bytes(), ctype)

        def do_GET(self) -> None:
            path, _, query = self.path.partition("?")
            life.touch(time.monotonic())
            if path in ("/", "/index.html"):
                self._serve_asset("index.html")
            elif path.startswith("/ui/"):
                self._serve_asset(path[len("/ui/"):])
            elif path == "/api/ping":
                # 页面心跳：既是「还活着」的信号，也是单实例探测的身份应答
                life.ping(self._page_id(query), time.monotonic())
                self._send_json(200, {"app": APP_ID, "protocol": APP_PROTOCOL,
                                      "balance": str(editor.balance), "readonly": editor.readonly,
                                      "idle_timeout": life.idle_timeout})
            elif path == "/api/state":
                try:
                    self._send(200, editor.state_json(), "application/json; charset=utf-8")
                except (ValueError, OSError) as e:
                    self._send_json(500, {"ok": False, "message": f"读取 balance.json 失败：{e}"})
            elif path == "/api/presets":
                self._send_json(200, {"presets": editor.presets()})
            elif path == "/api/origin":
                code, body = editor.origin_balance()
                self._send_json(code, body)
            else:
                self._send_text(404, "not found")

        def do_POST(self) -> None:
            path, _, query = self.path.partition("?")
            life.touch(time.monotonic())
            if path == "/api/bye":
                # 页面关闭通知（navigator.sendBeacon 发来，可能没有请求体）
                life.bye(self._page_id(query), time.monotonic())
                self._send_json(200, {"ok": True})
                return
            try:
                payload = self._read_body()
            except (ValueError, KeyError) as e:
                self._send_json(400, {"ok": False, "message": f"请求体不是合法 JSON：{e}"})
                return
            if path == "/api/save":
                code, body = editor.save(payload)
                self._send_json(code, body)
            elif path == "/api/analyze":
                try:
                    self._send_json(200, analysis.build_report(payload))
                except (TypeError, AttributeError, ValueError) as e:
                    # 分析是只读派生量：算不出来就说清原因，不能连累编辑本身
                    self._send_json(400, {"ok": False, "message": f"分析失败（数据形状不符？）{e}"})
            elif path == "/api/revert":
                code, body = editor.revert()
                self._send_json(code, body)
            elif path == "/api/preset":
                data = payload if isinstance(payload, dict) else {}
                code, body = editor.preset_plan(str(data.get("id", "")), data.get("balance"))
                self._send_json(code, body)
            elif path == "/api/validate":
                # 精细编辑（原始 JSON）在应用前先过一遍与保存同一套结构校验：否则错误要等到
                # 「保存」才暴露，中间那段时间界面上显示的是坏数据，而且已经覆盖了编辑区的现场
                try:
                    current = editor.read_balance()
                except (ValueError, OSError) as e:
                    self._send_json(400, {"ok": False, "message": f"读取现文件失败：{e}"})
                    return
                errors = check_shape(payload, current)
                self._send_json(200, {"ok": not errors, "errors": errors[:20]})
            else:
                self._send_text(404, "not found")

    return Handler


def probe_existing(port: int) -> str | None:
    """端口上是否已有本工具实例：有则返回它正在编辑的文件路径，否则 None。

    判定靠 /api/ping 的身份串而不是「端口能不能绑上」——端口被别的程序占着时，
    我们既不能当成本工具复用，也不该直接甩一句 Address already in use 了事。
    """
    try:
        with urllib.request.urlopen(f"http://127.0.0.1:{port}/api/ping", timeout=1.5) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
    except (urllib.error.URLError, OSError, ValueError, TimeoutError):
        return None
    if not isinstance(payload, dict) or payload.get("app") != APP_ID:
        return None
    return str(payload.get("balance") or "")


def watchdog(server: ThreadingHTTPServer, life: Lifecycle) -> None:
    """后台看门狗：每 WATCHDOG_INTERVAL 秒问一次「该退了吗」。

    shutdown 必须从 serve_forever 之外的线程调用（它在等主循环退出），这也是要单开线程的原因。
    """
    while True:
        time.sleep(WATCHDOG_INTERVAL)
        reason = life.should_exit(time.monotonic())
        if reason:
            print(f"\n[balance-editor] {reason}，自动退出。"
                  f"需要时重开：python3 scripts/tools/balance_editor.py")
            server.shutdown()
            return


def main() -> None:
    ap = argparse.ArgumentParser(description="InfiAir 数值管理器")
    ap.add_argument("--port", type=int, default=8931)
    ap.add_argument("--no-browser", action="store_true", help="不自动打开浏览器")
    ap.add_argument("--balance", default=str(DEFAULT_BALANCE),
                    help="要编辑的 balance.json 路径（默认 data/balance.json；测试/试用可指向副本）")
    ap.add_argument("--readonly", action="store_true", help="只读：可查看与看分析，禁止保存/回滚")
    ap.add_argument("--idle-timeout", type=float, default=30.0, metavar="分钟",
                    help="闲置超过这么多分钟就自动退出（0 = 不按空闲退出；页面关闭时仍会退出）")
    args = ap.parse_args()

    balance_path = Path(args.balance).resolve()
    if not balance_path.is_file():
        print(f"[balance-editor] 找不到数值文件：{balance_path}", file=sys.stderr)
        sys.exit(2)
    if not (UI_DIR / "index.html").is_file():
        print(f"[balance-editor] 界面资源缺失：{UI_DIR}", file=sys.stderr)
        sys.exit(2)

    url = f"http://127.0.0.1:{args.port}/"
    idle_seconds = max(args.idle_timeout, 0.0) * 60.0

    # 先问一句端口上是不是已经有本工具在跑：同一端口开两个只会互相打架，
    # 而「开完忘了关」正是要防的事——直接复用那个实例并退出，比报错让人自己收拾更省事
    existing = probe_existing(args.port)
    if existing is not None:
        print(f"[balance-editor] 已有实例在跑（正在编辑 {existing or '未知文件'}），直接打开它：{url}")
        if not args.no_browser:
            webbrowser.open(url)
        return

    editor = Editor(balance_path, readonly=args.readonly)
    life = Lifecycle(idle_seconds, time.monotonic())
    try:
        server = ThreadingHTTPServer(("127.0.0.1", args.port), make_handler(editor, life))
    except OSError as e:
        # 走到这里说明端口被别的程序占着（本工具的实例已被上面的探测拦下）：裸 traceback 只说明
        # 「地址被占用」，却不说下一步——而这里正有一个现成答案（换个端口）
        print(f"[balance-editor] 端口 {args.port} 起不来（被其它程序占用？）：{e}\n"
              f"  换个端口：--port {args.port + 1}", file=sys.stderr)
        sys.exit(2)
    threading.Thread(target=watchdog, args=(server, life), daemon=True).start()

    print(f"[balance-editor] {url}  ->  {balance_path}{'  [只读]' if args.readonly else ''}")
    idle_hint = f"闲置超过 {args.idle_timeout:g} 分钟自动退出；" if idle_seconds > 0 else ""
    print(f"[balance-editor] 关掉页面即自动退出；{idle_hint}Ctrl+C 立即退出")
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
