#!/usr/bin/env python3
"""InfiAir 数值管理器（balance editor）

本机可视化编辑 data/balance.json：分区树形展示全部可调数值，按 scripts/tools/balance_meta.json
显示中文说明、单位与取值范围，标黄未保存改动，保存前服务端递归校验结构与类型和现文件一致
（键集双向相等——编辑器加不了也删不了键），临时文件 + os.replace 原子落盘，
并自动备份（balance.json.bak，可一键回滚）。另一页签是被动分析面板（难度曲线 / 战斗节奏 /
经济与进度 / 增幅收益 / 取值体检），公式出处标在每一块上，见 scripts/tools/balance_analysis.py。

用法：
    python3 scripts/tools/balance_editor.py [--port 8931] [--no-browser] [--balance PATH] [--readonly]

仅依赖 Python 标准库。改完数值后跑门禁（python3 scripts/ci/gates.py）。
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
UI_DIR = HERE / "balance_editor_ui"
DEFAULT_BALANCE = ROOT / "data" / "balance.json"

sys.path.insert(0, str(HERE))
import balance_analysis as analysis  # noqa: E402  （同目录模块，入口脚本里按需 import）


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

    def read_balance(self) -> dict:
        return json.loads(self.balance.read_text(encoding="utf-8"))

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


def make_handler(editor: Editor) -> type[BaseHTTPRequestHandler]:
    class Handler(BaseHTTPRequestHandler):
        server_version = "InfiAirBalanceEditor"

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
            return json.loads(self.rfile.read(length) or b"null")

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
            path = self.path.split("?", 1)[0]
            if path in ("/", "/index.html"):
                self._serve_asset("index.html")
            elif path.startswith("/ui/"):
                self._serve_asset(path[len("/ui/"):])
            elif path == "/api/state":
                try:
                    self._send_json(200, editor.state())
                except (ValueError, OSError) as e:
                    self._send_json(500, {"ok": False, "message": f"读取 balance.json 失败：{e}"})
            else:
                self._send_text(404, "not found")

        def do_POST(self) -> None:
            path = self.path.split("?", 1)[0]
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
            else:
                self._send_text(404, "not found")

    return Handler


def main() -> None:
    ap = argparse.ArgumentParser(description="InfiAir 数值管理器")
    ap.add_argument("--port", type=int, default=8931)
    ap.add_argument("--no-browser", action="store_true", help="不自动打开浏览器")
    ap.add_argument("--balance", default=str(DEFAULT_BALANCE),
                    help="要编辑的 balance.json 路径（默认 data/balance.json；测试/试用可指向副本）")
    ap.add_argument("--readonly", action="store_true", help="只读：可查看与看分析，禁止保存/回滚")
    args = ap.parse_args()

    balance_path = Path(args.balance).resolve()
    if not balance_path.is_file():
        print(f"[balance-editor] 找不到数值文件：{balance_path}", file=sys.stderr)
        sys.exit(2)
    if not (UI_DIR / "index.html").is_file():
        print(f"[balance-editor] 界面资源缺失：{UI_DIR}", file=sys.stderr)
        sys.exit(2)

    editor = Editor(balance_path, readonly=args.readonly)
    server = ThreadingHTTPServer(("127.0.0.1", args.port), make_handler(editor))
    url = f"http://127.0.0.1:{args.port}/"
    print(f"[balance-editor] {url}  ->  {balance_path}{'  [只读]' if args.readonly else ''}")
    print("[balance-editor] Ctrl+C 退出")
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
