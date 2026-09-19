/* InfiAir 数值管理器前端：数值编辑 + 搜索 + 预设 + 改动清单 + 平衡分析 + 设置/精细编辑。
   无框架、无构建：所有状态都在本文件里，页面结构见 index.html。 */
(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);

  // ------------------------------------------------------------ 状态

  let disk = null;              // 磁盘快照（保存/重载后刷新）
  let current = null;           // 编辑中的副本
  let metaMap = {};             // 具体路径 -> {title, unit, range, note, source}
  let sectionMeta = {};         // 顶层键 -> {title, note, source}
  let presets = [];             // 预设清单（不含 ops，应用由服务端展开）
  let readonly = false;
  let fileMeta = null;          // {path, relative, mtime, size}
  let fileLabel = '';
  let idleTimeout = 0;          // 服务端空闲超时（秒），0 = 不按空闲退出
  const rows = [];
  const rowByPath = new Map();
  const undoStack = [];
  const redoStack = [];
  let focusBeforeOverlay = null;

  // 页面标识：心跳与「页面关闭」通知都带它。多标签页各用各的 id，
  // 服务端因此不会把「关掉其中一个标签」误判成「全部关闭」。
  const pageId = Math.random().toString(36).slice(2, 10);

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

  // 数值显示：缩放的预设会产生 0.5625000000000001 这类尾巴，展示与写回都收一下
  function fmtNum(v, digits = 4) {
    if (typeof v !== 'number' || Number.isInteger(v)) return String(v);
    const rounded = Number(v.toFixed(digits));
    return String(rounded);
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
    undoStack.length = 0;
    redoStack.length = 0;
    rebuild();
    setFileMeta();
    $('file-path').textContent = fileLabel;
    setStatus(state.backup
      ? `已载入 · 备份可用（${new Date(state.backup.mtime * 1000).toLocaleTimeString()}）`
      : '已载入 · 尚无备份（首次保存时生成）');
    if (readonly) {
      $('btn-save').disabled = true;
      $('btn-revert').disabled = true;
      $('btn-revert-2').disabled = true;
      setStatus('只读模式：可以查看与看分析，保存与回滚已禁用');
    }
    renderSettingsFile(state);
  }

  function renderSettingsFile(state) {
    const kv = $('settings-file');
    const backup = state.backup;
    kv.innerHTML = '';
    const add = (key, value, mono = true) => {
      const dt = document.createElement('dt');
      dt.textContent = key;
      const dd = document.createElement('dd');
      dd.textContent = value;
      if (!mono) dd.classList.remove('mono');
      kv.appendChild(dt);
      kv.appendChild(dd);
    };
    add('数值文件', state.file.path);
    add('当前状态', state.readonly ? '只读（本次启动未开启保存）' : `大小 ${(state.file.size / 1024).toFixed(1)} KB`);
    add('备份', backup
      ? `${backup.path.split('/').pop()} · ${new Date(backup.mtime * 1000).toLocaleString()}`
      : '尚无备份（首次保存时生成）');
    $('settings-endpoint').textContent = location.origin;
  }

  // ------------------------------------------------------------ 构建视图

  const metaFor = (path) => metaMap[path] || null;

  function rebuild() {
    rows.length = 0;
    rowByPath.clear();
    build();
    buildNav();
    refreshAll();
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
      if (isLeaf(value)) addRow(body, value, [key], key);
      else buildChildren(body, value, [key], key);
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
    const hintText = meta
      ? [meta.note, meta.range ? `取值范围 [${meta.range[0] ?? '−∞'}, ${meta.range[1] ?? '+∞'}]` : '',
         meta.source ? `出处 ${meta.source}` : ''].filter(Boolean).join(' · ')
      : '';
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
      input.setAttribute('aria-label', path);
    } else if (typeof value === 'number') {
      input.type = 'number';
      input.step = 'any';
      input.value = value;
      input.setAttribute('aria-label', `${path}${meta?.title ? `（${meta.title}）` : ''}`);
    } else {
      input.type = 'text';
      input.value = fmtValue(value);
      input.setAttribute('aria-label', `${path}${meta?.title ? `（${meta.title}）` : ''}`);
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
      pushUndo([{ path, before, after: parsed }]);
      updateRow(rec);
      onEdited();
    });
    resetBtn.addEventListener('click', () => {
      const before = getAt(current, parts);
      const after = getAt(disk, parts);
      if (sameValue(before, after)) return;
      setAt(current, parts, deepCopy(after));
      pushUndo([{ path, before, after: deepCopy(after) }]);
      syncInput(rec);
      updateRow(rec);
      onEdited();
    });

    container.appendChild(rowEl);
  }

  function syncInput(rec) {
    const value = getAt(current, rec.parts);
    if (rec.input.type === 'checkbox') rec.input.checked = !!value;
    else if (typeof value === 'number') rec.input.value = value;
    else rec.input.value = fmtValue(value);
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
    for (const rec of rows) if (rangeBad(rec, getAt(current, rec.parts))) n++;
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
      link.querySelector('.count').textContent = count ? String(count) : '';
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
      jump.tabIndex = 0;
      jump.addEventListener('click', () => {
        closeDrawer();
        for (let node = item.rec.rowEl; node; node = node.parentElement) {
          if (node.tagName === 'DETAILS') node.open = true;
        }
        item.rec.rowEl.scrollIntoView({ block: 'center', behavior: 'smooth' });
        item.rec.input.focus();
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

  // 条目两种形态：{kind:'paths', changes:[{path,before,after}]} 与
  // {kind:'snapshot', before, after}（整棵树替换，如精细编辑 / 导入 / 恢复已提交版本）
  function pushUndo(items) {
    if (!items.length) return;
    const now = Date.now();
    const top = undoStack[undoStack.length - 1];
    if (top && top.kind === 'paths' && items.length === 1 && top.changes.length === 1 &&
        top.changes[0].path === items[0].path && now - top.time < 900) {
      top.changes[0].after = items[0].after;   // 连续输入同一个键：合并成一步
      top.time = now;
    } else {
      undoStack.push({ kind: 'paths', changes: items, time: now });
    }
    if (undoStack.length > 200) undoStack.shift();
    redoStack.length = 0;
  }

  function pushSnapshotUndo(before, after) {
    undoStack.push({ kind: 'snapshot', before: deepCopy(before), after: deepCopy(after), time: Date.now() });
    if (undoStack.length > 200) undoStack.shift();
    redoStack.length = 0;
  }

  function applyPathChanges(entry, useBefore) {
    for (const change of entry.changes) {
      const rec = rowByPath.get(change.path);
      if (!rec) continue;
      setAt(current, rec.parts, deepCopy(useBefore ? change.before : change.after));
      syncInput(rec);
      updateRow(rec);
    }
    onEdited();
    const first = rowByPath.get(entry.changes[0]?.path);
    if (first) first.rowEl.scrollIntoView({ block: 'center' });
  }

  function undo() {
    const entry = undoStack.pop();
    if (!entry) return;
    redoStack.push(entry);
    if (entry.kind === 'snapshot') {
      current = deepCopy(entry.before);
      rebuild();
      setStatus('已撤销整次替换');
    } else {
      applyPathChanges(entry, true);
    }
  }

  function redo() {
    const entry = redoStack.pop();
    if (!entry) return;
    undoStack.push(entry);
    if (entry.kind === 'snapshot') {
      current = deepCopy(entry.after);
      rebuild();
      setStatus('已重做整次替换');
    } else {
      applyPathChanges(entry, false);
    }
  }

  // ------------------------------------------------------------ 预设

  async function loadPresets() {
    const res = await fetch('/api/presets');
    if (!res.ok) return;
    presets = (await res.json()).presets || [];
    renderPresets();
    renderPresetNotes();
  }

  function renderPresets() {
    const list = $('preset-list');
    list.innerHTML = '';
    if (!presets.length) {
      list.innerHTML = '<span class="dim">没有可用预设（scripts/tools/balance_presets.json 缺失或为空）</span>';
      return;
    }
    for (const preset of presets) {
      const card = document.createElement('div');
      card.className = 'preset-card';
      const head = document.createElement('div');
      head.className = 'p-head';
      const name = document.createElement('span');
      name.className = 'p-name';
      name.textContent = preset.name;
      head.appendChild(name);
      for (const tag of preset.tags || []) {
        const tagEl = document.createElement('span');
        tagEl.className = 'p-tag';
        tagEl.textContent = tag;
        head.appendChild(tagEl);
      }
      card.appendChild(head);
      const desc = document.createElement('div');
      desc.className = 'p-desc';
      desc.textContent = preset.desc;
      card.appendChild(desc);
      const actions = document.createElement('div');
      actions.className = 'p-actions';
      const previewBtn = document.createElement('button');
      previewBtn.textContent = '看改动';
      previewBtn.setAttribute('aria-expanded', 'false');
      const applyBtn = document.createElement('button');
      applyBtn.className = 'primary';
      applyBtn.textContent = '套用';
      actions.appendChild(previewBtn);
      actions.appendChild(applyBtn);
      card.appendChild(actions);
      const diff = document.createElement('div');
      diff.className = 'p-diff';
      diff.hidden = true;
      card.appendChild(diff);

      previewBtn.addEventListener('click', async () => {
        if (!diff.hidden) {
          diff.hidden = true;
          previewBtn.setAttribute('aria-expanded', 'false');
          return;
        }
        const plan = await requestPreset(preset.id);
        if (!plan) return;
        diff.innerHTML = plan.changes.length
          ? plan.changes.map((c) => `${escapeHtml(c.path)} <span class="to">${escapeHtml(fmtNum(c.from))} → ${escapeHtml(fmtNum(c.to))}</span>`).join('<br>')
          : '<span class="dim">当前值下没有可改的地方</span>';
        if (plan.skipped?.length) {
          diff.innerHTML += `<br><span class="dim">跳过 ${plan.skipped.length} 处（路径或类型不适用）</span>`;
        }
        diff.hidden = false;
        previewBtn.setAttribute('aria-expanded', 'true');
      });
      applyBtn.addEventListener('click', () => applyPreset(preset));
      list.appendChild(card);
    }
  }

  function renderPresetNotes() {
    const box = $('settings-presets');
    box.innerHTML = '';
    for (const preset of presets) {
      const row = document.createElement('div');
      row.className = 'row-note';
      row.innerHTML = `<b>${escapeHtml(preset.name)}</b>　${escapeHtml(preset.desc)}`;
      box.appendChild(row);
    }
  }

  async function requestPreset(id) {
    const res = await fetch('/api/preset', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ id, balance: current }),
    });
    const body = await res.json().catch(() => ({ ok: false, message: '服务端返回无法解析' }));
    if (!res.ok) {
      setStatus(body.message || '预设不可用', 'error');
      return null;
    }
    return body;
  }

  async function applyPreset(preset) {
    const plan = await requestPreset(preset.id);
    if (!plan) return;
    const applied = [];
    for (const item of plan.changes) {
      const rec = rowByPath.get(item.path);
      if (!rec) continue;
      const before = getAt(current, rec.parts);
      setAt(current, rec.parts, item.to);
      syncInput(rec);
      updateRow(rec);
      applied.push({ path: item.path, before, after: item.to });
    }
    if (!applied.length) {
      setStatus(`预设「${preset.name}」在当前值下没有可改的地方`);
      return;
    }
    pushUndo(applied);           // 整条预设合成一步：一次 Ctrl+Z 全退掉
    onEdited();
    setStatus(`已套用预设「${preset.name}」：${applied.length} 处改动（Ctrl+Z 撤销）`, 'ok');
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
    $('empty-state').hidden = visible > 0;
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
      refreshAll();
    }
    if (body.file) {
      fileMeta = { ...fileMeta, ...body.file };
      setFileMeta();
    }
    setStatus(body.message, 'ok');
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

  // ------------------------------------------------------------ 精细编辑 / 导入导出

  function applyTree(tree, message) {
    const before = current;
    current = tree;
    pushSnapshotUndo(before, tree);
    rebuild();
    setStatus(message, 'ok');
  }

  function openRawModal() {
    focusBeforeOverlay = document.activeElement;
    $('raw-editor').value = JSON.stringify(current, null, '\t');
    $('raw-status').textContent = '';
    $('raw-status').className = 'modal-status';
    $('raw-modal').hidden = false;
    $('raw-editor').focus();
  }

  function closeRawModal() {
    $('raw-modal').hidden = true;
    if (focusBeforeOverlay?.focus) focusBeforeOverlay.focus();
  }

  function setRawStatus(text, kind = '') {
    const el = $('raw-status');
    el.textContent = text;
    el.className = `modal-status ${kind}`.trim();
  }

  async function applyRaw() {
    let parsed;
    try {
      parsed = JSON.parse($('raw-editor').value);
    } catch (err) {
      setRawStatus(`JSON 语法错误：${err.message}`, 'error');
      return;
    }
    setRawStatus('校验中…');
    const res = await fetch('/api/validate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(parsed),
    });
    const body = await res.json().catch(() => ({ ok: false, errors: ['服务端返回无法解析'] }));
    if (!body.ok) {
      const errors = body.errors || [body.message || '结构校验未通过'];
      setRawStatus(`结构校验未通过（${errors.length} 处）：${errors.slice(0, 3).join('；')}`, 'error');
      return;
    }
    applyTree(parsed, '精细编辑已应用到编辑区（Ctrl+Z 撤销整次替换；需点保存才写盘）');
    closeRawModal();
  }

  function exportJson() {
    const blob = new Blob([`${JSON.stringify(current, null, '\t')}\n`], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileLabel.split('/').pop() || 'balance.json';
    link.click();
    URL.revokeObjectURL(url);
    setStatus('已导出当前编辑内容（未保存改动也在内）');
  }

  async function importJson(file) {
    const text = await file.text();
    let parsed;
    try {
      parsed = JSON.parse(text);
    } catch (err) {
      setStatus(`导入失败：不是合法 JSON（${err.message}）`, 'error');
      return;
    }
    const res = await fetch('/api/validate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(parsed),
    });
    const body = await res.json().catch(() => ({ ok: false, errors: ['服务端返回无法解析'] }));
    if (!body.ok) {
      const errors = body.errors || [body.message || '结构校验未通过'];
      setStatus(`导入失败：结构不符（${errors.slice(0, 2).join('；')}）`, 'error');
      return;
    }
    applyTree(parsed, `已导入 ${file.name} 到编辑区（Ctrl+Z 撤销；需点保存才写盘）`);
  }

  async function loadOrigin() {
    const res = await fetch('/api/origin');
    const body = await res.json().catch(() => ({ ok: false, message: '服务端返回无法解析' }));
    if (!res.ok) {
      setStatus(body.message || '取不到已提交版本', 'error');
      return;
    }
    applyTree(body.balance, '已把「已提交版本」载入编辑区（Ctrl+Z 撤销；需点保存才写盘）');
  }

  // ------------------------------------------------------------ 分析渲染

  function renderReport(report) {
    const el = $('analysis');
    el.innerHTML = '';
    const warnings = report.warnings || [];
    const box = document.createElement('div');
    box.className = 'warn-box' + (warnings.length ? ' has-warn' : '');
    box.innerHTML = `<h3>取值体检（${warnings.length} 条）</h3>`;
    if (!warnings.length) box.innerHTML += '<div class="warn-item dim">全部登记范围与结构约束都通过。</div>';
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
      th.scope = 'col';
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
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('class', 'chart');
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
    svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');
    const add = (tag, attrs, text) => {
      const el = document.createElementNS(ns, tag);
      for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
      if (text !== undefined) el.textContent = text;
      svg.appendChild(el);
    };
    for (let i = 0; i <= 4; i++) {
      const value = yMin + (yMax - yMin) * (i / 4);
      const y = sy(value);
      add('line', { class: 'grid-line', x1: PAD.l, x2: W - PAD.r, y1: y, y2: y });
      add('text', { x: PAD.l - 6, y: y + 3, 'text-anchor': 'end' }, value.toFixed(1));
    }
    for (let i = 0; i <= 3; i++) {
      const value = xMin + (xMax - xMin) * (i / 3);
      add('text', { x: sx(value), y: H - 6, 'text-anchor': 'middle' }, `${Math.round(value)}${series.x_label || ''}`);
    }
    add('line', { class: 'axis', x1: PAD.l, x2: PAD.l, y1: PAD.t, y2: H - PAD.b });
    add('line', { class: 'axis', x1: PAD.l, x2: W - PAD.r, y1: H - PAD.b, y2: H - PAD.b });
    add('polyline', { class: 'line', points: xs.map((x, i) => `${sx(x).toFixed(2)},${sy(ys[i]).toFixed(2)}`).join(' ') });
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
    focusBeforeOverlay = document.activeElement;
    $('drawer').classList.add('open');
    $('drawer').setAttribute('aria-hidden', 'false');
    $('drawer-scrim').classList.add('open');
    $('btn-close-drawer').focus();
  }

  function closeDrawer() {
    $('drawer').classList.remove('open');
    $('drawer').setAttribute('aria-hidden', 'true');
    $('drawer-scrim').classList.remove('open');
    if (focusBeforeOverlay?.focus) focusBeforeOverlay.focus();
  }

  const drawerOpen = () => $('drawer').classList.contains('open');
  const modalOpen = () => !$('raw-modal').hidden;

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

  function selectTab(name) {
    for (const tab of document.querySelectorAll('.tab')) {
      const active = tab.dataset.tab === name;
      tab.classList.toggle('active', active);
      tab.setAttribute('aria-selected', String(active));
    }
    for (const panel of document.querySelectorAll('.tab-panel')) {
      panel.classList.toggle('active', panel.id === 'tab-' + name);
    }
    if (name === 'analysis') analyze();
  }

  function bind() {
    $('search').addEventListener('input', applyFilter);
    $('only-changed').addEventListener('change', applyFilter);
    $('btn-save').addEventListener('click', save);
    $('btn-reload').addEventListener('click', () => {
      if (changes().length && !confirm('丢弃未保存改动并重新载入？')) return;
      load();
    });
    const doRevert = () => {
      if (!confirm('用 balance.json.bak 覆盖当前文件？当前内容会另存为 .pre-revert。')) return;
      revert();
    };
    $('btn-revert').addEventListener('click', doRevert);
    $('btn-revert-2').addEventListener('click', doRevert);
    $('btn-undo').addEventListener('click', undo);
    $('btn-redo').addEventListener('click', redo);
    $('btn-changes').addEventListener('click', openDrawer);
    $('btn-close-drawer').addEventListener('click', closeDrawer);
    $('drawer-scrim').addEventListener('click', closeDrawer);
    $('btn-copy-changes').addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(changesAsMarkdown());
        setStatus(`已复制 ${changes().length} 处改动（Markdown 表格）`);
      } catch (err) {
        setStatus('复制失败：浏览器未授权剪贴板', 'error');
      }
    });

    for (const tab of document.querySelectorAll('.tab')) {
      tab.addEventListener('click', () => selectTab(tab.dataset.tab));
    }
    $('btn-analyze').addEventListener('click', analyze);

    // 设置页
    $('btn-raw').addEventListener('click', openRawModal);
    $('btn-origin').addEventListener('click', loadOrigin);
    $('btn-export').addEventListener('click', exportJson);
    $('btn-import').addEventListener('click', () => $('import-file').click());
    $('import-file').addEventListener('change', (event) => {
      const file = event.target.files?.[0];
      if (file) importJson(file);
      event.target.value = '';
    });

    // 精细编辑模态
    $('raw-close').addEventListener('click', closeRawModal);
    $('raw-apply').addEventListener('click', applyRaw);
    $('raw-reload').addEventListener('click', () => {
      $('raw-editor').value = JSON.stringify(current, null, '\t');
      setRawStatus('已载入当前编辑内容');
    });
    $('raw-format').addEventListener('click', () => {
      try {
        $('raw-editor').value = JSON.stringify(JSON.parse($('raw-editor').value), null, '\t');
        setRawStatus('已格式化', 'ok');
      } catch (err) {
        setRawStatus(`JSON 语法错误：${err.message}`, 'error');
      }
    });
    $('raw-editor').addEventListener('input', () => {
      $('raw-editor').classList.remove('invalid');
      setRawStatus('');
    });
    $('raw-modal').addEventListener('mousedown', (event) => {
      if (event.target === $('raw-modal')) closeRawModal();
    });

    document.addEventListener('keydown', (event) => {
      const tag = (event.target.tagName || '').toLowerCase();
      const typing = tag === 'input' || tag === 'textarea';
      if (event.key === 'Escape') {
        if (modalOpen()) { closeRawModal(); return; }
        if (drawerOpen()) { closeDrawer(); return; }
        if ($('search').value) { $('search').value = ''; applyFilter(); }
        return;
      }
      // 模态打开时不接管其它快捷键（Tab 由浏览器在模态内走，配合 mousedown 遮罩关闭）
      if (modalOpen()) return;
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
      }
    });

    window.addEventListener('beforeunload', (event) => {
      if (!changes().length) return;
      event.preventDefault();
      event.returnValue = '';
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

  // ------------------------------------------------------------ 心跳与生命周期

  function startHeartbeat() {
    const ping = async () => {
      try {
        const res = await fetch(`/api/ping?page=${pageId}`, { cache: 'no-store' });
        if (res.ok) {
          const body = await res.json();
          idleTimeout = body.idle_timeout || 0;
        }
      } catch (err) {
        /* 服务已退出（多半是空闲超时）：静默即可，用户下次操作会看到失败提示 */
      }
    };
    ping();
    setInterval(ping, 10000);
    // pagehide 比 beforeunload 可靠：bfcache、移动端切后台都能触发，且 sendBeacon 不阻塞卸载
    window.addEventListener('pagehide', () => {
      navigator.sendBeacon?.(`/api/bye?page=${pageId}`);
    });
  }

  // ------------------------------------------------------------ 启动

  bind();
  load().then(loadPresets);
  startHeartbeat();
})();
