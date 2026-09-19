/* InfiAir 数值管理器前端：数值编辑 + 搜索过滤 + 改动清单 + 平衡分析。
   无框架、无构建：所有状态都在本文件里，页面结构见 index.html。 */
(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);

  // ------------------------------------------------------------ 状态

  let disk = null;              // 磁盘快照（保存/重载后刷新）
  let current = null;           // 编辑中的副本
  let metaMap = {};             // 具体路径 -> {title, unit, range, note, source}
  let sectionMeta = {};         // 顶层键 -> {title, note, source}
  let readonly = false;
  let fileMeta = null;          // {path, mtime, size}
  let fileLabel = '';           // 相对仓库根的路径，仅用于显示
  const rows = [];              // 行记录
  const rowByPath = new Map();
  const undoStack = [];
  const redoStack = [];
  let focusBeforeDrawer = null;

  // ------------------------------------------------------------ 路径工具

  const toParts = (path) => path.replace(/\[(\d+)\]/g, '.$1').split('.').filter((s) => s !== '');

  function getAt(obj, parts) {
    let node = obj;
    for (const part of parts) {
      if (node === null || typeof node !== 'object' || !(part in node)) return undefined;
      node = node[part];
    }
    return node;
  }

  function setAt(obj, parts, value) {
    const parent = getAt(obj, parts.slice(0, -1));
    if (parent === null || typeof parent !== 'object') return;
    parent[parts[parts.length - 1]] = value;
  }

  const deepCopy = (value) => JSON.parse(JSON.stringify(value));
  const isNumArray = (v) => Array.isArray(v) && v.length > 0 && v.every((x) => typeof x === 'number');
  const isLeaf = (v) => v === null || typeof v !== 'object' || isNumArray(v);
  const sameValue = (a, b) => JSON.stringify(a) === JSON.stringify(b);

  function fmtValue(v) {
    if (Array.isArray(v)) return v.join(', ');
    if (typeof v === 'string') return v;
    return String(v);
  }

  function makeParser(value) {
    if (typeof value === 'boolean') return (input) => input.checked;
    if (typeof value === 'number') return (input) => {
      if (input.value.trim() === '') throw new Error('空值');
      const n = Number(input.value);
      if (!Number.isFinite(n)) throw new Error('不是数字');
      return n;
    };
    if (typeof value === 'string') return (input) => input.value;
    if (isNumArray(value)) return (input) => {
      const tokens = input.value.split(',').map((s) => s.trim()).filter((s) => s !== '');
      const nums = tokens.map(Number);
      if (tokens.length === 0 || nums.some((n) => !Number.isFinite(n))) throw new Error('不是数字数组');
      return nums;
    };
    return (input) => JSON.parse(input.value);
  }

  // ------------------------------------------------------------ 状态栏

  function setStatus(message, kind = '') {
    const el = $('status');
    el.textContent = message;
    el.className = kind;
  }

  function setFileMeta() {
    if (!fileMeta) return;
    $('file-meta').textContent = `${fileLabel} · ${(fileMeta.size / 1024).toFixed(1)} KB` +
      ` · 修改于 ${new Date(fileMeta.mtime * 1000).toLocaleString()}`;
    $('file-meta').title = fileMeta.path;
  }

  // ------------------------------------------------------------ 载入

  async function load() {
    setStatus('载入中…');
    const res = await fetch('/api/state');
    if (!res.ok) {
      setStatus('载入失败：服务端读不到 balance.json', 'error');
      return;
    }
    const state = await res.json();
    disk = state.balance;
    current = deepCopy(state.balance);
    metaMap = state.meta || {};
    sectionMeta = state.sections || {};
    readonly = !!state.readonly;
    fileMeta = state.file;
    fileLabel = state.file.relative || state.file.path;
    rows.length = 0;
    rowByPath.clear();
    undoStack.length = 0;
    redoStack.length = 0;
    build();
    buildNav();
    refreshAll();
    setFileMeta();
    $('file-path').textContent = fileLabel;
    setStatus(state.backup
      ? `已载入 · 备份可用（${new Date(state.backup.mtime * 1000).toLocaleTimeString()}）`
      : '已载入 · 尚无备份（首次保存时生成）');
    if (readonly) {
      $('btn-save').disabled = true;
      $('btn-revert').disabled = true;
      setStatus('只读模式：可以查看与看分析，保存与回滚已禁用');
    }
  }

  // ------------------------------------------------------------ 构建视图

  function metaFor(path) {
    return metaMap[path] || null;
  }

  function build() {
    const groups = $('groups');
    groups.innerHTML = '';
    for (const key of Object.keys(current)) {
      const value = current[key];
      const card = document.createElement('details');
      card.className = 'group';
      card.id = 'sec-' + key;
      card.open = true;
      const summary = document.createElement('summary');
      const sec = sectionMeta[key] || {};
      const name = document.createElement('span');
      name.textContent = sec.title ? `${sec.title}（${key}）` : key;
      summary.appendChild(name);
      if (sec.note) {
        const note = document.createElement('span');
        note.className = 'sec-note';
        note.textContent = sec.note;
        note.title = sec.source ? `${sec.note}\n出处：${sec.source}` : sec.note;
        summary.appendChild(note);
      }
      const count = document.createElement('span');
      count.className = 'sec-count';
      summary.appendChild(count);
      card.appendChild(summary);
      const body = document.createElement('div');
      body.className = 'group-body';
      card.appendChild(body);
      groups.appendChild(card);
      if (isLeaf(value)) {
        addRow(body, value, [key], key);
      } else {
        buildChildren(body, value, [key], key);
      }
    }
  }

  function buildChildren(container, node, parts, sectionKey) {
    for (const key of Object.keys(node)) {
      const value = node[key];
      const childParts = parts.concat(key);
      if (isLeaf(value)) {
        addRow(container, value, childParts, sectionKey);
      } else {
        const det = document.createElement('details');
        det.className = 'subgroup';
        det.open = false;
        const summary = document.createElement('summary');
        const label = document.createElement('span');
        label.className = 'sub-path';
        label.textContent = childParts.slice(1).join('.');
        summary.appendChild(label);
        det.appendChild(summary);
        const body = document.createElement('div');
        body.className = 'group-body';
        det.appendChild(body);
        container.appendChild(det);
        buildChildren(body, value, childParts, sectionKey);
      }
    }
  }

  function addRow(container, value, parts, sectionKey) {
    const path = parts.join('.').replace(/\.(\d+)/g, '[$1]');
    const meta = metaFor(path);
    const rowEl = document.createElement('div');
    rowEl.className = 'row';

    const pathEl = document.createElement('div');
    pathEl.className = 'path';
    pathEl.textContent = path;
    pathEl.title = '点击复制路径';
    pathEl.addEventListener('click', () => {
      navigator.clipboard?.writeText(path);
      setStatus(`已复制路径 ${path}`);
    });
    rowEl.appendChild(pathEl);

    const label = document.createElement('div');
    label.className = 'label';
    const titleEl = document.createElement('span');
    titleEl.className = 'title';
    titleEl.textContent = meta?.title || parts[parts.length - 1];
    label.appendChild(titleEl);
    if (meta?.unit) {
      const unit = document.createElement('span');
      unit.className = 'unit';
      unit.textContent = `(${meta.unit})`;
      label.appendChild(unit);
    }
    const hintText = meta ? [meta.note, meta.range ? `取值范围 [${meta.range[0] ?? '−∞'}, ${meta.range[1] ?? '+∞'}]` : '', meta.source ? `出处 ${meta.source}` : ''].filter(Boolean).join(' · ') : '';
    const hint = document.createElement('span');
    hint.className = 'hint';
    hint.textContent = meta?.note || (meta?.source ? `出处 ${meta.source}` : '');
    hint.dataset.base = hint.textContent;
    if (hintText) hint.title = hintText;
    label.appendChild(hint);
    rowEl.appendChild(label);

    const input = document.createElement('input');
    if (typeof value === 'boolean') {
      input.type = 'checkbox';
      input.checked = value;
    } else if (typeof value === 'number') {
      input.type = 'number';
      input.step = 'any';
      input.value = value;
    } else if (isNumArray(value) || typeof value === 'string' || value === null) {
      input.type = 'text';
      input.value = fmtValue(value);
    } else {
      input.type = 'text';
      input.value = JSON.stringify(value);
    }
    rowEl.appendChild(input);

    const actions = document.createElement('div');
    actions.className = 'row-actions';
    const resetBtn = document.createElement('button');
    resetBtn.textContent = '恢复';
    resetBtn.title = '恢复为磁盘上的值';
    resetBtn.disabled = true;
    actions.appendChild(resetBtn);
    rowEl.appendChild(actions);

    const rec = { path, parts, input, rowEl, sectionKey, meta, parse: makeParser(value), resetBtn };
    rows.push(rec);
    rowByPath.set(path, rec);

    input.addEventListener('input', () => {
      let parsed;
      try {
        parsed = rec.parse(input);
      } catch (err) {
        rowEl.classList.add('invalid');
        input.title = `输入无法解析：${err.message}`;
        return;
      }
      rowEl.classList.remove('invalid');
      input.title = '';
      const before = getAt(current, parts);
      if (sameValue(before, parsed)) return;
      setAt(current, parts, parsed);
      pushUndo(path, before, parsed);
      updateRow(rec);
      onEdited();
    });
    resetBtn.addEventListener('click', () => {
      const before = getAt(current, parts);
      const after = getAt(disk, parts);
      if (sameValue(before, after)) return;
      setAt(current, parts, deepCopy(after));
      pushUndo(path, before, deepCopy(after));
      syncInput(rec);
      updateRow(rec);
      onEdited();
    });

    container.appendChild(rowEl);
  }

  function syncInput(rec) {
    const value = getAt(current, rec.parts);
    if (rec.input.type === 'checkbox') rec.input.checked = !!value;
    else rec.input.value = isNumArray(value) ? fmtValue(value) : (typeof value === 'string' || value === null ? fmtValue(value) : String(value));
    rec.rowEl.classList.remove('invalid');
  }

  function rangeBad(rec, value) {
    const range = rec.meta?.range;
    if (!range || typeof value !== 'number') return null;
    if (range[0] !== null && value < range[0]) return `低于下限 ${range[0]}：会被代码钳制或回退默认`;
    if (range[1] !== null && value > range[1]) return `高于上限 ${range[1]}：会被代码钳制或回退默认`;
    return null;
  }

  function updateRow(rec) {
    const value = getAt(current, rec.parts);
    const changed = !sameValue(value, getAt(disk, rec.parts));
    rec.rowEl.classList.toggle('changed', changed);
    rec.resetBtn.disabled = !changed;
    const bad = rangeBad(rec, value);
    rec.rowEl.classList.toggle('out-of-range', !!bad);
    const hint = rec.rowEl.querySelector('.hint');
    if (bad) {
      hint.textContent = bad;
      hint.classList.add('bad');
    } else {
      hint.textContent = hint.dataset.base || '';
      hint.classList.remove('bad');
    }
  }

  function refreshAll() {
    for (const rec of rows) updateRow(rec);
    updateSummary();
    applyFilter();
  }

  // ------------------------------------------------------------ 改动汇总

  function changes() {
    const out = [];
    for (const rec of rows) {
      const now = getAt(current, rec.parts);
      const before = getAt(disk, rec.parts);
      if (!sameValue(now, before)) out.push({ path: rec.path, old: before, new: now, rec });
    }
    return out;
  }

  function outOfRangeCount() {
    let n = 0;
    for (const rec of rows) {
      const value = getAt(current, rec.parts);
      if (rangeBad(rec, value)) n++;
    }
    return n;
  }

  function updateSummary() {
    const list = changes();
    $('change-badge').textContent = String(list.length);
    $('change-badge').classList.toggle('zero', list.length === 0);
    $('btn-undo').disabled = undoStack.length === 0;
    $('btn-redo').disabled = redoStack.length === 0;

    for (const link of $('nav').children) {
      const key = link.dataset.key;
      const count = list.filter((item) => item.rec.sectionKey === key).length;
      const badge = link.querySelector('.count');
      badge.textContent = count ? String(count) : '';
    }
    const bad = outOfRangeCount();
    if (list.length || bad) {
      setStatus(`${list.length} 处未保存改动${bad ? ` · ${bad} 处超出登记范围` : ''}`, bad ? 'error' : '');
    } else {
      setStatus('无未保存改动');
    }
    renderDrawer(list);
  }

  function renderDrawer(list) {
    const body = $('change-list');
    $('drawer-count').textContent = `${list.length} 处`;
    body.innerHTML = '';
    if (!list.length) {
      body.innerHTML = '<div class="dim" style="padding:12px 0">没有未保存改动。</div>';
      return;
    }
    for (const item of list) {
      const el = document.createElement('div');
      el.className = 'change';
      const p = document.createElement('div');
      p.className = 'p';
      p.textContent = item.path;
      const v = document.createElement('div');
      v.className = 'v';
      v.innerHTML = `${escapeHtml(fmtValue(item.old))} → <span class="new">${escapeHtml(fmtValue(item.new))}</span>`;
      const jump = document.createElement('span');
      jump.className = 'jump';
      jump.textContent = '定位';
      jump.addEventListener('click', () => {
        closeDrawer();
        const rec = item.rec;
        for (let el2 = rec.rowEl; el2; el2 = el2.parentElement) {
          if (el2.tagName === 'DETAILS') el2.open = true;
        }
        rec.rowEl.scrollIntoView({ block: 'center', behavior: 'smooth' });
        rec.input.focus();
      });
      p.appendChild(document.createTextNode(' '));
      p.appendChild(jump);
      el.appendChild(p);
      el.appendChild(v);
      body.appendChild(el);
    }
  }

  function escapeHtml(text) {
    return String(text).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  }

  function changesAsMarkdown() {
    const list = changes();
    if (!list.length) return '（无改动）';
    const lines = ['| 键 | 原值 | 新值 |', '| --- | --- | --- |'];
    for (const item of list) lines.push(`| \`${item.path}\` | ${fmtValue(item.old)} | ${fmtValue(item.new)} |`);
    return lines.join('\n');
  }

  // ------------------------------------------------------------ 撤销/重做

  function pushUndo(path, before, after) {
    const now = Date.now();
    const top = undoStack[undoStack.length - 1];
    // 同一个键的连续输入合并成一条（打字时不该攒出几十步）
    if (top && top.path === path && now - top.time < 900) {
      top.after = after;
      top.time = now;
    } else {
      undoStack.push({ path, before, after, time: now });
    }
    if (undoStack.length > 200) undoStack.shift();
    redoStack.length = 0;
  }

  function applyHistory(entry, value) {
    const rec = rowByPath.get(entry.path);
    if (!rec) return;
    setAt(current, rec.parts, deepCopy(value));
    syncInput(rec);
    updateRow(rec);
    onEdited();
    rec.rowEl.scrollIntoView({ block: 'center' });
  }

  function undo() {
    const entry = undoStack.pop();
    if (!entry) return;
    redoStack.push(entry);
    applyHistory(entry, entry.before);
  }

  function redo() {
    const entry = redoStack.pop();
    if (!entry) return;
    undoStack.push(entry);
    applyHistory(entry, entry.after);
  }

  // ------------------------------------------------------------ 搜索与过滤

  let openSnapshot = null;

  function applyFilter() {
    const query = $('search').value.trim().toLowerCase();
    const onlyChanged = $('only-changed').checked;
    let visible = 0;
    for (const rec of rows) {
      const hay = `${rec.path} ${rec.meta?.title || ''} ${rec.meta?.note || ''} ${rec.meta?.source || ''}`.toLowerCase();
      const match = (!query || hay.includes(query)) && (!onlyChanged || rec.rowEl.classList.contains('changed'));
      rec.rowEl.classList.toggle('hidden', !match);
      if (match) visible++;
    }
    for (const card of document.querySelectorAll('details')) {
      const anyVisible = card.querySelector('.row:not(.hidden)') !== null;
      card.classList.toggle('hidden', !anyVisible);
      if (query && anyVisible) {
        if (openSnapshot === null) openSnapshot = new Map();
        if (!openSnapshot.has(card)) openSnapshot.set(card, card.open);
        card.open = true;
      }
    }
    if (!query && openSnapshot) {
      for (const [card, wasOpen] of openSnapshot) card.open = wasOpen;
      openSnapshot = null;
    }
    $('search-count').textContent = query ? `${visible} / ${rows.length}` : '';
  }

  // ------------------------------------------------------------ 服务端往返

  async function save() {
    if (readonly) return;
    const res = await fetch('/api/save', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(current),
    });
    const body = await res.json().catch(() => ({ ok: false, message: '服务端返回无法解析' }));
    if (!res.ok) {
      const detail = body.errors ? `\n${body.errors.slice(0, 6).join('\n')}` : '';
      setStatus(`${body.message}${detail}`, 'error');
      return;
    }
    if (body.changes && body.changes.length) {
      disk = deepCopy(current);
      undoStack.length = 0;
      redoStack.length = 0;
      refreshAll();          // 会重算改动数（归零）
    }
    if (body.file) {
      fileMeta = { ...fileMeta, ...body.file };   // 页脚的大小/修改时间跟着落盘结果走
      setFileMeta();
    }
    setStatus(body.message, 'ok');   // 放在 refreshAll 之后：改动归零的提示不覆盖保存结果
  }

  async function revert() {
    if (readonly) return;
    const res = await fetch('/api/revert', { method: 'POST' });
    const body = await res.json().catch(() => ({ ok: false, message: '服务端返回无法解析' }));
    setStatus(body.message, res.ok ? 'ok' : 'error');
    if (res.ok) await load();
  }

  async function analyze() {
    const el = $('analysis');
    el.innerHTML = '<div class="dim" style="padding:20px 0">计算中…</div>';
    const res = await fetch('/api/analyze', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(current),
    });
    if (!res.ok) {
      el.innerHTML = '<div class="warn-box has-warn">分析失败：数据形状不符（结构错误会导致派生量无意义）</div>';
      return;
    }
    renderReport(await res.json());
  }

  // ------------------------------------------------------------ 分析渲染

  function renderReport(report) {
    const el = $('analysis');
    el.innerHTML = '';
    const warnings = report.warnings || [];
    const box = document.createElement('div');
    box.className = 'warn-box' + (warnings.length ? ' has-warn' : '');
    box.innerHTML = `<h3>取值体检（${warnings.length} 条）</h3>`;
    if (!warnings.length) {
      box.innerHTML += '<div class="warn-item dim">全部登记范围与结构约束都通过。</div>';
    }
    for (const item of warnings) {
      const line = document.createElement('div');
      line.className = 'warn-item';
      line.innerHTML = `<span class="p">${escapeHtml(item.path)}</span> — ${escapeHtml(item.why)}` +
        (item.source ? ` <span class="src">（${escapeHtml(item.source)}）</span>` : '');
      box.appendChild(line);
    }
    el.appendChild(box);

    for (const section of report.sections || []) {
      const card = document.createElement('div');
      card.className = 'card';
      const h = document.createElement('h3');
      h.textContent = section.title;
      card.appendChild(h);
      if (section.note) {
        const note = document.createElement('div');
        note.className = 'card-note';
        note.textContent = section.note;
        card.appendChild(note);
      }
      if (section.metrics?.length) {
        const metrics = document.createElement('div');
        metrics.className = 'metrics';
        for (const metric of section.metrics) {
          const m = document.createElement('div');
          m.className = 'metric';
          m.innerHTML = `<div class="k">${escapeHtml(metric.label)}</div><div class="v">${escapeHtml(metric.value)}</div>`;
          metrics.appendChild(m);
        }
        card.appendChild(metrics);
      }
      if (section.series) card.appendChild(renderChart(section.series));
      for (const key of ['table', 'table2', 'table3']) {
        if (section[key]) card.appendChild(renderTable(section[key]));
      }
      const src = section.source2 ? `${section.source} ；${section.source2}` : section.source;
      if (src) {
        const s = document.createElement('div');
        s.className = 'card-src';
        s.textContent = `公式出处：${src}`;
        card.appendChild(s);
      }
      el.appendChild(card);
    }
  }

  function renderTable(table) {
    const wrap = document.createElement('div');
    const el = document.createElement('table');
    el.className = 'data';
    const caption = document.createElement('caption');
    caption.textContent = table.title;
    el.appendChild(caption);
    const thead = document.createElement('thead');
    const headRow = document.createElement('tr');
    for (const head of table.head) {
      const th = document.createElement('th');
      th.textContent = head;
      headRow.appendChild(th);
    }
    thead.appendChild(headRow);
    el.appendChild(thead);
    const tbody = document.createElement('tbody');
    for (const row of table.rows) {
      const tr = document.createElement('tr');
      for (const cell of row) {
        const td = document.createElement('td');
        td.textContent = cell;
        tr.appendChild(td);
      }
      tbody.appendChild(tr);
    }
    el.appendChild(tbody);
    wrap.appendChild(el);
    return wrap;
  }

  function renderChart(series) {
    const W = 900, H = 200, PAD = { l: 46, r: 14, t: 12, b: 24 };
    const xs = series.x, ys = series.y;
    const xMin = Math.min(...xs), xMax = Math.max(...xs);
    const yMin = Math.min(...ys, 1), yMax = Math.max(...ys, 1.05);
    const sx = (x) => PAD.l + (x - xMin) / Math.max(xMax - xMin, 1e-9) * (W - PAD.l - PAD.r);
    const sy = (y) => H - PAD.b - (y - yMin) / Math.max(yMax - yMin, 1e-9) * (H - PAD.t - PAD.b);
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'chart');
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
    svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');
    const ns = 'http://www.w3.org/2000/svg';
    const add = (tag, attrs, text) => {
      const el = document.createElementNS(ns, tag);
      for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
      if (text !== undefined) el.textContent = text;
      svg.appendChild(el);
      return el;
    };
    const ticks = 4;
    for (let i = 0; i <= ticks; i++) {
      const value = yMin + (yMax - yMin) * (i / ticks);
      const y = sy(value);
      add('line', { class: 'grid-line', x1: PAD.l, x2: W - PAD.r, y1: y, y2: y });
      add('text', { x: PAD.l - 6, y: y + 3, 'text-anchor': 'end' }, value.toFixed(1));
    }
    for (let i = 0; i <= 3; i++) {
      const value = xMin + (xMax - xMin) * (i / 3);
      const x = sx(value);
      add('text', { x, y: H - 6, 'text-anchor': 'middle' }, `${Math.round(value)}${series.x_label || ''}`);
    }
    add('line', { class: 'axis', x1: PAD.l, x2: PAD.l, y1: PAD.t, y2: H - PAD.b });
    add('line', { class: 'axis', x1: PAD.l, x2: W - PAD.r, y1: H - PAD.b, y2: H - PAD.b });
    const points = xs.map((x, i) => `${sx(x).toFixed(2)},${sy(ys[i]).toFixed(2)}`).join(' ');
    add('polyline', { class: 'line', points });
    const wrap = document.createElement('div');
    wrap.appendChild(svg);
    if (series.title) {
      const cap = document.createElement('div');
      cap.className = 'dim';
      cap.style.fontSize = '11px';
      cap.textContent = series.title;
      wrap.appendChild(cap);
    }
    return wrap;
  }

  // ------------------------------------------------------------ 抽屉

  function openDrawer() {
    focusBeforeDrawer = document.activeElement;
    $('drawer').classList.add('open');
    $('drawer-scrim').classList.add('open');
    $('btn-close-drawer').focus();
  }

  function closeDrawer() {
    $('drawer').classList.remove('open');
    $('drawer-scrim').classList.remove('open');
    if (focusBeforeDrawer?.focus) focusBeforeDrawer.focus();
  }

  // ------------------------------------------------------------ 事件绑定

  function onEdited() {
    updateSummary();
    applyFilter();
  }

  function buildNav() {
    const nav = $('nav');
    nav.innerHTML = '';
    for (const key of Object.keys(current)) {
      const link = document.createElement('a');
      link.href = '#sec-' + key;
      link.dataset.key = key;
      const sec = sectionMeta[key] || {};
      const name = document.createElement('span');
      name.className = 'name';
      name.textContent = sec.title || key;
      name.title = sec.source ? `${key}\n出处：${sec.source}` : key;
      const count = document.createElement('span');
      count.className = 'count';
      link.appendChild(name);
      link.appendChild(count);
      link.addEventListener('click', (event) => {
        event.preventDefault();
        const card = document.getElementById('sec-' + key);
        card.open = true;
        card.scrollIntoView({ block: 'start', behavior: 'smooth' });
      });
      nav.appendChild(link);
    }
  }

  function bind() {
    $('search').addEventListener('input', applyFilter);
    $('only-changed').addEventListener('change', applyFilter);
    $('btn-save').addEventListener('click', save);
    $('btn-reload').addEventListener('click', () => {
      if (changes().length && !confirm('丢弃未保存改动并重新载入？')) return;
      load();
    });
    $('btn-revert').addEventListener('click', () => {
      if (!confirm('用 balance.json.bak 覆盖当前文件？当前内容会另存为 .pre-revert。')) return;
      revert();
    });
    $('btn-undo').addEventListener('click', undo);
    $('btn-redo').addEventListener('click', redo);
    $('btn-changes').addEventListener('click', openDrawer);
    $('btn-close-drawer').addEventListener('click', closeDrawer);
    $('drawer-scrim').addEventListener('click', closeDrawer);
    $('btn-copy-changes').addEventListener('click', async () => {
      const text = changesAsMarkdown();
      try {
        await navigator.clipboard.writeText(text);
        setStatus(`已复制 ${changes().length} 处改动（Markdown 表格）`);
      } catch (err) {
        setStatus('复制失败：浏览器未授权剪贴板', 'error');
      }
    });

    for (const tab of document.querySelectorAll('.tab')) {
      tab.addEventListener('click', () => {
        for (const other of document.querySelectorAll('.tab')) other.classList.toggle('active', other === tab);
        for (const panel of document.querySelectorAll('.tab-panel')) {
          panel.classList.toggle('active', panel.id === 'tab-' + tab.dataset.tab);
        }
        if (tab.dataset.tab === 'analysis') analyze();
      });
    }
    $('btn-analyze').addEventListener('click', analyze);

    document.addEventListener('keydown', (event) => {
      const tag = (event.target.tagName || '').toLowerCase();
      const typing = tag === 'input' || tag === 'textarea';
      if (event.key === '/' && !typing) {
        event.preventDefault();
        $('search').focus();
      } else if ((event.key === 'k' || event.key === 'K') && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        $('search').focus();
      } else if ((event.key === 's' || event.key === 'S') && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        save();
      } else if ((event.key === 'z' || event.key === 'Z') && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        if (event.shiftKey) redo(); else undo();
      } else if ((event.key === 'd' || event.key === 'D') && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        openDrawer();
      } else if (event.key === 'Escape') {
        if ($('drawer').classList.contains('open')) closeDrawer();
        else if ($('search').value) { $('search').value = ''; applyFilter(); }
      }
    });

    window.addEventListener('beforeunload', (event) => {
      if (!changes().length) return;
      event.preventDefault();
      event.returnValue = '';   // 部分浏览器仍要求设置该字段才会弹确认
    });

    window.addEventListener('scroll', () => {
      let active = null;
      for (const card of document.querySelectorAll('.group')) {
        if (card.getBoundingClientRect().top <= 120) active = card.id;
      }
      for (const link of $('nav').children) {
        link.classList.toggle('active', link.href.endsWith(active));
      }
    }, { passive: true });
  }

  // ------------------------------------------------------------ 启动

  bind();
  load();
})();
