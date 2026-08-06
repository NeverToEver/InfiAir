#!/usr/bin/env python3
"""InfiAir 数值管理器（balance editor）

本机可视化编辑 data/balance.json：分区树形展示全部可调数值，标黄未保存改动，
保存前服务端递归校验结构/类型与现文件一致，临时文件 + os.replace 原子落盘，
并自动备份（balance.json.bak）。

用法：
    python3 scripts/tools/balance_editor.py [--port 8931] [--no-browser]

仅依赖 Python 标准库。改完数值后按 AGENTS.md 约定跑最小验证集
（--headless --import / --quit-after 300 / smoke_test.tscn）。
"""

import argparse
import json
import os
import shutil
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BALANCE = ROOT / "data" / "balance.json"

PAGE = """<!DOCTYPE html>
<html lang="zh">
<head>
<meta charset="utf-8">
<title>InfiAir 数值管理器</title>
<style>
  :root { --accent:#00d4ff; --bg:#050a12; --panel:#0a1120; --border:#1c3a4a;
          --text:#e0e8f0; --dim:#8a9bb0; --changed:#d8a868; --error:#ff3366; }
  * { box-sizing: border-box; }
  body { margin:0; background:var(--bg); color:var(--text);
         font:14px/1.5 "SF Mono", Menlo, monospace; }
  header { position:sticky; top:0; background:var(--panel); border-bottom:1px solid var(--accent);
           padding:10px 20px; display:flex; gap:16px; align-items:center; z-index:9; }
  header h1 { font-size:18px; color:var(--accent); margin:0; }
  header .spacer { flex:1; }
  button { background:transparent; color:var(--text); border:1px solid var(--accent);
           padding:6px 18px; cursor:pointer; font:inherit; }
  button.primary { background:rgba(0,212,255,.18); font-weight:bold; }
  button:hover { background:rgba(0,212,255,.12); }
  #status { color:var(--dim); }
  #status.error { color:var(--error); }
  nav { position:sticky; top:47px; background:var(--panel); padding:6px 20px;
        border-bottom:1px solid var(--border); display:flex; flex-wrap:wrap; gap:6px; z-index:8; }
  nav a { color:var(--dim); text-decoration:none; padding:2px 10px; border:1px solid var(--border); }
  nav a:hover { color:var(--accent); border-color:var(--accent); }
  main { padding:12px 20px 80px; max-width:1100px; }
  details { margin:10px 0; border:1px solid var(--border); background:var(--panel); }
  details > summary { cursor:pointer; padding:8px 14px; color:var(--accent);
                      font-weight:bold; user-select:none; }
  .group { padding:4px 14px 12px; }
  .row { display:flex; align-items:center; gap:10px; padding:3px 0; }
  .row label { flex:0 0 380px; color:var(--dim); overflow:hidden; text-overflow:ellipsis;
               white-space:nowrap; }
  .row input[type=number], .row input[type=text] { flex:0 0 260px; background:var(--bg);
        color:var(--text); border:1px solid var(--border); padding:4px 8px; font:inherit; }
  .row input:focus { border-color:var(--accent); outline:none; }
  .row.changed label { color:var(--changed); }
  .row.changed input { border-color:var(--changed); }
  .row.error label, .row.error input { color:var(--error); border-color:var(--error); }
</style>
</head>
<body>
<header>
  <h1>InfiAir 数值管理器</h1><span id="status">data/balance.json</span>
  <div class="spacer"></div>
  <button id="reload">还原未保存改动</button>
  <button id="save" class="primary">保存到 balance.json</button>
</header>
<nav id="nav"></nav>
<main id="main"></main>
<script>
let original = null;   // 服务器文件快照
let current = null;    // 编辑中的副本
const rows = [];       // {path, parse, input, row}
let errorCount = 0;    // 处于解析错误状态的行数（>0 禁止保存）

const statusEl = document.getElementById('status');
function setStatus(msg, isError = false) {
  statusEl.textContent = msg;
  statusEl.className = isError ? 'error' : '';
}

function getAt(obj, path) { return path.reduce((o, k) => o[k], obj); }
function setAt(obj, path, v) {
  const parent = path.slice(0, -1).reduce((o, k) => o[k], obj);
  parent[path[path.length - 1]] = v;
}

function isNumArray(v) { return Array.isArray(v) && v.every(x => typeof x === 'number'); }

// 按原始值类型生成解析器；解析失败一律 throw（行标红，current 不更新）
function makeParser(orig) {
  if (typeof orig === 'boolean') return input => input.checked;
  if (typeof orig === 'number') return input => {
    if (input.value.trim() === '') throw new Error('empty');
    const n = Number(input.value);
    if (!Number.isFinite(n)) throw new Error('not a number');
    return n;
  };
  if (typeof orig === 'string') return input => input.value;
  if (isNumArray(orig)) return input => {
    const tokens = input.value.split(',').map(s => s.trim()).filter(s => s !== '');
    const nums = tokens.map(Number);
    if (tokens.length === 0 || nums.some(n => !Number.isFinite(n)))
      throw new Error('bad number array');
    return nums;
  };
  // 其余结构（对象数组里的非标量等）：按 JSON 解析
  return input => JSON.parse(input.value);
}

function addRow(container, value, path) {
  const name = path.join('.');
  const row = document.createElement('div'); row.className = 'row';
  const label = document.createElement('label');
  label.textContent = name; label.title = name;
  row.appendChild(label);
  const input = document.createElement('input');
  if (typeof value === 'boolean') {
    input.type = 'checkbox'; input.checked = value;
  } else if (typeof value === 'number') {
    input.type = 'number'; input.step = 'any'; input.value = value;
  } else {
    input.type = 'text';
    input.value = isNumArray(value) ? value.join(', ')
      : (typeof value === 'string' ? value : JSON.stringify(value));
  }
  row.appendChild(input); container.appendChild(row);
  const rec = { path, input, row, parse: makeParser(value) };
  input.addEventListener('input', () => {
    let v;
    try {
      v = rec.parse(input);
    } catch (e) {
      if (!row.classList.contains('error')) { row.classList.add('error'); errorCount++; }
      return;  // 解析失败：current 保持旧值，等输入合法再更新
    }
    if (row.classList.contains('error')) { row.classList.remove('error'); errorCount--; }
    setAt(current, path, v);
    row.classList.toggle('changed',
      JSON.stringify(v) !== JSON.stringify(getAt(original, path)));
  });
  rows.push(rec);
}

function buildGroup(container, node, path, depth) {
  for (const key of Object.keys(node)) {
    const v = node[key], p = path.concat(key);
    const isLeaf = v === null || typeof v !== 'object' || isNumArray(v);
    if (isLeaf) {
      addRow(container, v, p);
    } else {
      const det = document.createElement('details');
      det.open = depth === 0;  // 顶层分区默认展开，深层折叠
      const sum = document.createElement('summary');
      sum.textContent = Array.isArray(node) ? '[' + key + ']' : String(key);
      det.appendChild(sum);
      const g = document.createElement('div'); g.className = 'group';
      det.appendChild(g); container.appendChild(det);
      buildGroup(g, v, p, depth + 1);
    }
  }
}

async function load() {
  original = await (await fetch('/api/balance')).json();
  current = JSON.parse(JSON.stringify(original));
  rows.length = 0; errorCount = 0;
  const main = document.getElementById('main'); main.innerHTML = '';
  const nav = document.getElementById('nav'); nav.innerHTML = '';
  for (const key of Object.keys(original)) {
    const a = document.createElement('a');
    a.textContent = key; a.href = '#sec-' + key;
    nav.appendChild(a);
  }
  buildGroup(main, original, [], 0);
  document.querySelectorAll('#main > details').forEach(d => {
    d.id = 'sec-' + d.querySelector('summary').textContent;
  });
  setStatus('已加载（编辑即时生效于页面，保存才落盘）');
}

function dirty() {
  return rows.some(r => r.row.classList.contains('changed')) || errorCount > 0;
}

document.getElementById('reload').onclick = load;
document.getElementById('save').onclick = async () => {
  if (errorCount > 0) {
    setStatus(`有 ${errorCount} 行输入无法解析，请先修正（红色行）`, true);
    return;
  }
  const res = await fetch('/api/balance', {
    method: 'POST', headers: {'Content-Type': 'application/json'},
    body: JSON.stringify(current),
  });
  const msg = await res.text();
  setStatus(msg, !res.ok);
  if (res.ok) {
    original = JSON.parse(JSON.stringify(current));
    document.querySelectorAll('.row.changed').forEach(r => r.classList.remove('changed'));
  }
};
window.addEventListener('beforeunload', e => { if (dirty()) e.preventDefault(); });
load();
</script>
</body>
</html>
"""


def _check_shape(new: object, old: object, path: str = "") -> list[str]:
    """递归校验结构与标量类型和现文件一致（数组只要求元素类型一致，长度可变）。"""
    errs: list[str] = []
    where = path or "<root>"
    if isinstance(old, dict):
        if not isinstance(new, dict):
            return [f"{where}: 应为对象"]
        for k in old:
            if k not in new:
                errs.append(f"{where}.{k}: 缺失")
            else:
                errs.extend(_check_shape(new[k], old[k], f"{where}.{k}"))
    elif isinstance(old, list):
        if not isinstance(new, list):
            return [f"{where}: 应为数组"]
        if old and new:
            for i, item in enumerate(new):
                errs.extend(_check_shape(item, old[0], f"{where}[{i}]"))
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


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):  # 静音请求日志
        pass

    def _send(self, code: int, body: str, ctype: str = "text/plain; charset=utf-8") -> None:
        data = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self) -> None:
        if self.path == "/":
            self._send(200, PAGE, "text/html; charset=utf-8")
        elif self.path == "/api/balance":
            self._send(200, BALANCE.read_text(encoding="utf-8"), "application/json; charset=utf-8")
        else:
            self._send(404, "not found")

    def do_POST(self) -> None:
        if self.path != "/api/balance":
            self._send(404, "not found")
            return
        try:
            payload = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        except (ValueError, KeyError) as e:
            self._send(400, f"保存失败：JSON 解析错误 {e}")
            return
        try:
            current = json.loads(BALANCE.read_text(encoding="utf-8"))
        except (ValueError, OSError) as e:
            # R07：读侧裸异常修复（L 系列工具链登记遗留）——balance.json 损坏时
            # 原实现裸 traceback 500，编辑器无法给出可读诊断
            self._send(400, f"保存失败：读取 balance.json 失败（文件损坏？）{e}")
            return
        errors = _check_shape(payload, current)
        if errors:
            self._send(400, "保存失败：结构/类型与现文件不一致\n" + "\n".join(errors[:10]))
            return
        # 备份 + 原子落盘（临时文件同目录 os.replace，写一半不会损坏原文件）
        try:
            shutil.copy2(BALANCE, BALANCE.with_suffix(".json.bak"))
            tmp = BALANCE.with_suffix(".json.tmp")
            tmp.write_text(json.dumps(payload, indent="\t", ensure_ascii=False) + "\n", encoding="utf-8")
            os.replace(tmp, BALANCE)
        except OSError as e:
            # 2026-08-06 审计：写盘侧 OSError 兜底（R08 只给读侧加了友好 400）——
            # 磁盘满/只读/权限不足时原实现裸 traceback 且无任何响应
            self._send(400, f"保存失败：写入/备份 balance.json 失败（磁盘满或权限不足？）{e}")
            return
        self._send(200, "已保存（原文件备份为 balance.json.bak）")


def main() -> None:
    ap = argparse.ArgumentParser(description="InfiAir 数值管理器")
    ap.add_argument("--port", type=int, default=8931)
    ap.add_argument("--no-browser", action="store_true")
    args = ap.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    url = f"http://127.0.0.1:{args.port}/"
    print(f"[balance-editor] {url}（Ctrl+C 退出）")
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
