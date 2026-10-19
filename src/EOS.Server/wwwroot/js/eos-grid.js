(function () {
  'use strict';
  const M = window.EosGridModel;
  const DENSITY = { compact: 28, cozy: 34, roomy: 42 };
  const GUTTER = 46;
  const BAND_H = 24;

  const esc = (s) => String(s === null || s === undefined ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  const I = (d, extra) => `<svg class="eg-i" viewBox="0 0 16 16" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"${extra || ''}>${d}</svg>`;
  const ICON = {
    down: I('<path d="M4 6l4 4 4-4"/>'),
    right: I('<path d="M6 4l4 4-4 4"/>'),
    asc: I('<path d="M8 13V3M4 7l4-4 4 4"/>'),
    desc: I('<path d="M8 3v10M4 9l4 4 4-4"/>'),
    filter: I('<path d="M2.5 3.5h11L9.5 8.5v4l-3 1v-5z"/>'),
    more: I('<circle cx="3.5" cy="8" r=".6"/><circle cx="8" cy="8" r=".6"/><circle cx="12.5" cy="8" r=".6"/>'),
    search: I('<circle cx="7" cy="7" r="4.2"/><path d="M10.2 10.2L13.5 13.5"/>'),
    undo: I('<path d="M5.5 3.5L2.5 6.5l3 3"/><path d="M2.5 6.5H9a4 4 0 010 8H6"/>'),
    redo: I('<path d="M10.5 3.5l3 3-3 3"/><path d="M13.5 6.5H7a4 4 0 000 8h3"/>'),
    cols: I('<rect x="2" y="3" width="12" height="10" rx="1.5"/><path d="M6 3v10M10 3v10"/>'),
    download: I('<path d="M8 2.5v7.5M4.8 7.2L8 10.4l3.2-3.2M3 13h10"/>'),
    check: I('<path d="M3.5 8.5l3 3 6-7"/>'),
    x: I('<path d="M4 4l8 8M12 4l-8 8"/>'),
    pin: I('<path d="M9.5 2.5l4 4-2 .8-2.2 2.2.4 3-1 1L5.7 9.3 3 12l-.5-.5L5.2 8.8 2.7 6.3l1-1 3 .4L8.9 3.5z"/>'),
    eyeOff: I('<path d="M2 8s2.2-4 6-4 6 4 6 4-2.2 4-6 4-6-4-6-4z"/><path d="M3 3l10 10"/>'),
    group: I('<rect x="2.5" y="2.5" width="11" height="4" rx="1"/><rect x="2.5" y="9.5" width="11" height="4" rx="1"/>'),
    reset: I('<path d="M3 8a5 5 0 105-5H5.5"/><path d="M7.5 .8L5.3 3 7.5 5.2"/>'),
    rows: I('<path d="M2.5 4h11M2.5 8h11M2.5 12h11"/>'),
    cal: I('<rect x="2.5" y="3.5" width="11" height="10" rx="1.5"/><path d="M2.5 6.5h11M5.5 2v3M10.5 2v3"/>'),
    trash: I('<path d="M3 4.5h10M6.5 4.5V3h3v1.5M4.5 4.5l.6 8.5h5.8l.6-8.5"/>'),
    plus: I('<path d="M8 3v10M3 8h10"/>'),
    expand: I('<path d="M3 6l5 5 5-5"/>'),
  };

  class EosGrid {
    constructor(host, config) {
      this.host = host;
      this.id = config.id || 'grid';
      this.opts = Object.assign({
        density: 'cozy', rowNumbers: true, quickRating: true, readOnly: false, tree: false, groupable: true,
        minColWidth: 56, overscan: 8, emptyText: 'Nothing to show.', hint: 'Click a cell, type to edit, Enter saves, paste a range from Excel, Ctrl+Z undoes.',
      }, config.options || {});
      this.onCommit = config.onCommit || null;
      this.onAction = config.onAction || null;
      this.undo = new M.UndoStack(200);
      this.pending = 0;
      this.errors = new Map();
      this.flash = new Map();
      this.saving = new Set();
      this.state = { sort: [], filters: {}, search: '', groupBy: null, collapsed: new Set(), density: this.opts.density, tree: !!this.opts.tree };
      this.prefs = { cols: {}, order: [] };
      this.active = null;
      this.range = null;
      this.anchor = null;
      this.editing = null;
      this.menu = null;
      this.winKey = '';
      this.loadPrefs();
      this.state.density = DENSITY[this.state.density] ? this.state.density : 'cozy';
      this.defs = [];
      this.rows = [];
      this.display = [];
      this.vcols = [];
      this.build();
      this.setData(config.columns || [], config.rows || [], true);
    }

    get rowH() { return DENSITY[this.state.density]; }

    loadPrefs() {
      try {
        const raw = localStorage.getItem('eosgrid:' + this.id);
        if (!raw) return;
        const p = JSON.parse(raw);
        if (p.cols) this.prefs.cols = p.cols;
        if (p.order) this.prefs.order = p.order;
        if (p.sort) this.state.sort = p.sort;
        if (p.groupBy) this.state.groupBy = p.groupBy;
        if (p.density) this.state.density = p.density;
      } catch { }
    }

    savePrefs() {
      try {
        localStorage.setItem('eosgrid:' + this.id, JSON.stringify({
          cols: this.prefs.cols, order: this.prefs.order, sort: this.state.sort, groupBy: this.state.groupBy, density: this.state.density,
        }));
      } catch { }
    }

    resetView() {
      this.prefs = { cols: {}, order: [] };
      this.state.sort = [];
      this.state.filters = {};
      this.state.search = '';
      this.state.groupBy = null;
      this.state.collapsed = new Set();
      this.state.density = this.opts.density;
      try { localStorage.removeItem('eosgrid:' + this.id); } catch { }
      this.syncToolbar();
      this.relayout();
    }

    build() {
      this.host.classList.add('eg');
      this.host.innerHTML = `
        <div class="eg-toolbar" role="toolbar" aria-label="Grid tools"></div>
        <div class="eg-chips" hidden></div>
        <div class="eg-scroller" tabindex="0" role="grid" aria-multiselectable="true">
          <div class="eg-head" role="rowgroup"></div>
          <div class="eg-body" role="rowgroup"></div>
        </div>
        <div class="eg-status"><span class="eg-st-left"></span><span class="eg-st-mid"></span><span class="eg-st-right"></span></div>
        <div class="eg-toast" role="status" hidden></div>
        <div class="eg-sr" aria-live="polite"></div>`;
      this.el = {
        toolbar: this.host.querySelector('.eg-toolbar'),
        chips: this.host.querySelector('.eg-chips'),
        scroller: this.host.querySelector('.eg-scroller'),
        head: this.host.querySelector('.eg-head'),
        body: this.host.querySelector('.eg-body'),
        stLeft: this.host.querySelector('.eg-st-left'),
        stMid: this.host.querySelector('.eg-st-mid'),
        stRight: this.host.querySelector('.eg-st-right'),
        toast: this.host.querySelector('.eg-toast'),
        sr: this.host.querySelector('.eg-sr'),
      };
      this.buildToolbar();
      this.bindEvents();
    }

    setData(columns, rows, initial) {
      const keepRow = this.active && this.display[this.active.r] && this.display[this.active.r].row ? this.display[this.active.r].row.id : null;
      const keepCol = this.active && this.vcols[this.active.c] ? this.vcols[this.active.c].key : null;
      this.defs = (columns || []).map((c) => Object.assign({ type: 'text', editable: true }, c));
      this.rows = (rows || []).map((r) => ({
        id: r.id, parent: r.parent === undefined ? null : r.parent, cells: r.cells || {}, tones: r.tones || {}, readonly: !!r.readonly,
        locked: r.locked || [], title: r.title || null,
      }));
      this.rowIndex = new Map(this.rows.map((r) => [r.id, r]));
      if (this.state.groupBy && !this.defs.some((c) => c.key === this.state.groupBy)) this.state.groupBy = null;
      this.relayout(!initial ? { keepRow, keepCol } : null);
      this.syncToolbar();
    }

    computeColumns() {
      const byKey = new Map(this.defs.map((c) => [c.key, c]));
      const order = this.prefs.order.filter((k) => byKey.has(k));
      for (const c of this.defs) if (!order.includes(c.key)) order.push(c.key);
      let cols = order.map((k) => {
        const d = byKey.get(k);
        const p = this.prefs.cols[k] || {};
        return Object.assign({}, d, {
          width: Math.max(this.opts.minColWidth, p.w || Math.max(d.width || 110, this.wordMin(d))),
          sized: !!p.w,
          hidden: p.hidden !== undefined ? p.hidden : !!d.hidden,
          pin: p.pin !== undefined ? p.pin : d.pin || null,
        });
      });
      const width = this.el.scroller.clientWidth;
      this.narrow = width > 0 && width < 640;
      this.rn = !!this.opts.rowNumbers && !this.narrow;
      if (this.narrow) {
        let seen = false;
        cols = cols.map((c) => {
          if (c.pin !== 'left' || c.hidden) return c;
          if (seen) return Object.assign({}, c, { pin: null });
          seen = true;
          return Object.assign({}, c, { width: Math.min(c.width, 150) });
        });
      }
      const pinned = cols.filter((c) => c.pin === 'left' && !c.hidden);
      const rest = cols.filter((c) => c.pin !== 'left' && !c.hidden);
      this.allCols = cols;
      this.vcols = pinned.concat(rest);
      const avail = this.el.scroller.clientWidth;
      const base = (this.rn ? GUTTER : 0) + this.vcols.reduce((a, c) => a + c.width, 0);
      const flex = this.vcols.filter((c) => c.pin !== 'left' && !c.sized && c.type !== 'link');
      if (this.opts.fill !== false && avail > base + 24 && flex.length) {
        const each = Math.floor((avail - base - 2) / flex.length);
        for (const c of flex) c.width = Math.min(c.width + each, Math.max(c.width, 220));
        const used = (this.rn ? GUTTER : 0) + this.vcols.reduce((a, c) => a + c.width, 0);
        const last = flex[flex.length - 1];
        if (avail - used > 0 && last.width < 220) last.width = Math.min(220, last.width + (avail - used));
      }
      let x = this.rn ? GUTTER : 0;
      this.pinnedW = this.rn ? GUTTER : 0;
      for (const c of this.vcols) {
        c.left = x;
        x += c.width;
        if (c.pin === 'left') this.pinnedW = x;
      }
      if (pinned.length) pinned[pinned.length - 1].pinLast = true;
      this.totalW = x;
      this.hasBands = this.vcols.some((c) => c.band);
    }

    wordMin(col) {
      const ctx = this.textContext();
      const words = String(col.title || col.key).split(/\s+/);
      if (!ctx) return Math.ceil(Math.max(...words.map((x) => x.length)) * 7.4 + 26);
      ctx.font = '600 12px "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif';
      const w = Math.max(...words.map((x) => ctx.measureText(x).width));
      return Math.ceil(w + 26);
    }

    textContext() {
      if (this._ctx === undefined) {
        try { this._ctx = document.createElement('canvas').getContext('2d') || null; } catch { this._ctx = null; }
      }
      return this._ctx;
    }

    relayout(keep) {
      this.computeColumns();
      this.display = M.buildDisplay(this.rows, this.vcols, {
        sort: this.state.sort, filters: this.state.filters, search: this.state.search, groupBy: this.state.groupBy,
        collapsed: this.state.collapsed, tree: this.state.tree,
      });
      if (keep && keep.keepRow !== null && keep.keepRow !== undefined) {
        const r = this.display.findIndex((d) => d.kind === 'row' && d.row.id === keep.keepRow);
        const c = this.vcols.findIndex((x) => x.key === keep.keepCol);
        if (r >= 0 && c >= 0) { this.active = { r, c }; this.range = { r1: r, r2: r, c1: c, c2: c }; this.anchor = { r, c }; }
        else { this.active = null; this.range = null; this.anchor = null; }
      } else if (this.active) {
        this.active.r = Math.min(this.active.r, Math.max(0, this.display.length - 1));
        this.active.c = Math.min(this.active.c, Math.max(0, this.vcols.length - 1));
        if (!this.display.length || !this.vcols.length) { this.active = null; this.range = null; this.anchor = null; }
      }
      this.renderHead();
      this.el.body.style.height = Math.max(this.display.length * this.rowH, 140) + 'px';
      this.el.body.style.width = this.totalW + 'px';
      this.winKey = '';
      this.renderWindow();
      this.renderStatus();
      this.renderChips();
      this.host.classList.toggle('is-empty', this.display.length === 0);
    }

    renderHead() {
      const cols = this.vcols;
      let html = '';
      if (this.hasBands) {
        html += `<div class="eg-hrow eg-band" style="height:${BAND_H}px;width:${this.totalW}px">`;
        if (this.rn) html += `<div class="eg-hcell eg-gutter" style="width:${GUTTER}px;left:0"></div>`;
        let i = 0;
        while (i < cols.length) {
          let j = i;
          while (j + 1 < cols.length && (cols[j + 1].band || '') === (cols[i].band || '') && cols[j + 1].pin === cols[i].pin) j++;
          const w = cols.slice(i, j + 1).reduce((a, c) => a + c.width, 0);
          const pin = cols[i].pin === 'left';
          html += `<div class="eg-hcell eg-bandcell${pin ? ' pin' : ''}${cols[i].band ? '' : ' empty'}" style="width:${w}px${pin ? ';left:' + cols[i].left + 'px' : ''}">${esc(cols[i].band || '')}</div>`;
          i = j + 1;
        }
        html += '</div>';
      }
      html += `<div class="eg-hrow eg-titles" style="width:${this.totalW}px">`;
      if (this.rn) html += `<div class="eg-hcell eg-gutter" data-all="1" title="Select all" style="width:${GUTTER}px;left:0" role="columnheader"></div>`;
      cols.forEach((c, j) => {
        const s = this.state.sort.findIndex((x) => x.key === c.key);
        const dir = s >= 0 ? this.state.sort[s].dir : null;
        const filtered = M.isActiveFilter(this.state.filters[c.key]);
        const cls = ['eg-hcell', `t-${c.type}`, c.pin === 'left' ? 'pin' : '', c.pinLast ? 'pin-last' : '', dir ? 'sorted' : '', filtered ? 'filtered' : '', c.editable === false || c.type === 'readonly' ? 'ro' : ''].filter(Boolean).join(' ');
        html += `<div class="${cls}" role="columnheader" aria-sort="${dir === 'asc' ? 'ascending' : dir === 'desc' ? 'descending' : 'none'}" data-h="${j}" draggable="true" title="${esc(c.title || c.key)}${c.hint ? ' — ' + esc(c.hint) : ''}" style="width:${c.width}px${c.pin === 'left' ? ';left:' + c.left + 'px' : ''}">
          <span class="eg-htext">${esc(c.title || c.key)}</span>
          <span class="eg-hmark">${dir ? `<span class="eg-sortmark">${dir === 'asc' ? ICON.asc : ICON.desc}${this.state.sort.length > 1 ? `<sup>${s + 1}</sup>` : ''}</span>` : ''}${filtered ? `<span class="eg-fmark">${ICON.filter}</span>` : ''}</span>
          <button type="button" class="eg-hmenu" data-hm="${j}" aria-label="Column menu for ${esc(c.title || c.key)}">${ICON.down}</button>
          <span class="eg-resize" data-rs="${j}"></span></div>`;
      });
      html += '</div>';
      this.el.head.innerHTML = html;
      this.el.head.style.width = this.totalW + 'px';
      this.measureHead();
    }

    measureHead() {
      const titles = this.el.head.querySelector('.eg-titles');
      if (!titles) return;
      let need = 0;
      titles.querySelectorAll('.eg-htext').forEach((t) => { need = Math.max(need, t.scrollHeight); });
      const h = Math.min(Math.max(40, need + 18), 132);
      titles.style.height = h + 'px';
      this.headH = h + (this.hasBands ? BAND_H : 0);
      this.el.scroller.style.setProperty('--eg-head-h', this.headH + 'px');
    }

    renderWindow(force) {
      const sc = this.el.scroller;
      const total = this.display.length;
      const rowH = this.rowH;
      const viewH = Math.max(sc.clientHeight - (this.headH || 40), rowH);
      const first = Math.max(0, Math.floor(sc.scrollTop / rowH) - this.opts.overscan);
      const last = Math.min(total - 1, Math.ceil((sc.scrollTop + viewH) / rowH) + this.opts.overscan);
      const key = first + ':' + last + ':' + this.totalW;
      if (!force && key === this.winKey) return;
      this.winKey = key;
      let html = '';
      for (let i = first; i <= last; i++) html += this.rowHtml(i);
      if (!total) {
        const filtered = Object.keys(this.state.filters).length > 0 || !!this.state.search;
        html = `<div class="eg-empty"><span>${esc(filtered ? 'No rows match the current search or filters.' : this.opts.emptyText)}</span>${filtered ? '<button type="button" data-clear-all>Clear search and filters</button>' : ''}</div>`;
      }
      this.el.body.innerHTML = html;
      this.positionEditor();
    }

    rowHtml(i) {
      const d = this.display[i];
      const y = i * this.rowH;
      const act = this.active && this.active.r === i;
      if (d.kind === 'group') {
        let h = `<div class="eg-row eg-grp${act ? ' act' : ''}" role="row" aria-rowindex="${i + 2}" data-r="${i}" data-gid="${esc(d.id)}" style="top:${y}px;height:${this.rowH}px;width:${this.totalW}px">`;
        h += `<div class="eg-gl" style="width:${this.pinnedW}px"><button type="button" class="eg-twist${d.collapsed ? '' : ' open'}" tabindex="-1" aria-label="${d.collapsed ? 'Expand' : 'Collapse'} ${esc(d.label)}" data-tw="${esc(d.id)}">${ICON.right}</button><strong>${esc(d.label)}</strong><span class="eg-count">${d.count}</span></div>`;
        this.vcols.forEach((c) => {
          if (c.pin === 'left') return;
          const v = d.aggs[c.key];
          h += `<div class="eg-cell eg-gagg t-${c.type}" style="width:${c.width}px">${v === null || v === undefined ? '' : esc(M.formatValue(Object.assign({}, c, { type: c.type === 'rating' ? 'number' : c.type, decimals: c.agg === 'avg' ? 1 : c.decimals }), v))}</div>`;
        });
        return h + '</div>';
      }
      const row = d.row;
      let h = `<div class="eg-row${act ? ' act' : ''}${row.readonly ? ' ro' : ''}${i % 2 ? ' alt' : ''}" role="row" aria-rowindex="${i + 2}" data-r="${i}" style="top:${y}px;height:${this.rowH}px;width:${this.totalW}px">`;
      if (this.rn) h += `<div class="eg-cell eg-gutter${this.rangeHasRow(i) ? ' sel' : ''}" data-g="${i}" style="width:${GUTTER}px;left:0">${i + 1}</div>`;
      this.vcols.forEach((c, j) => { h += this.cellHtml(i, j, c, d); });
      return h + '</div>';
    }

    isEditable(row, col) {
      return !this.opts.readOnly && !row.readonly && col.editable !== false && col.type !== 'readonly' && col.type !== 'link' && !row.locked.includes(col.key);
    }

    cellClasses(i, j, col, row) {
      const cls = ['eg-cell', `t-${col.type}`];
      if (col.pin === 'left') cls.push('pin');
      if (col.pinLast) cls.push('pin-last');
      if (!this.isEditable(row, col)) cls.push('ro');
      else if (col.type === 'number' || col.type === 'int') cls.push('input');
      const k = row.id + '|' + col.key;
      if (this.errors.has(k)) cls.push('err');
      if (this.saving.has(k)) cls.push('ing');
      if (this.flash.has(k)) cls.push('sav');
      const tone = row.tones[col.key];
      if (tone) cls.push('tone-' + tone);
      if (this.active && this.active.r === i && this.active.c === j) cls.push('actc');
      const rg = this.range;
      if (rg && M.rangeContains(rg, i, j)) {
        cls.push('inr');
        if (i === rg.r1) cls.push('rt');
        if (i === rg.r2) cls.push('rb');
        if (j === rg.c1) cls.push('rl');
        if (j === rg.c2) cls.push('rr');
      }
      return cls.join(' ');
    }

    cellHtml(i, j, col, d) {
      const row = d.row;
      const v = row.cells[col.key];
      const err = this.errors.get(row.id + '|' + col.key);
      let inner;
      if (col.type === 'rating') inner = M.isBlank(v) ? '<span class="eg-dot">·</span>' : `<span class="eg-pill r${Math.min(Math.max(v, 0), 9)}">${esc(v)}</span>`;
      else if (col.type === 'select') {
        const opt = (col.options || []).find((o) => o.value === v);
        inner = M.isBlank(v) ? '<span class="eg-dot">·</span>' : `<span class="eg-chip${opt && opt.tone ? ' tone-' + esc(opt.tone) : ''}">${esc(M.formatValue(col, v))}</span>`;
      } else if (col.type === 'bool') inner = `<span class="eg-check${v ? ' on' : ''}" role="img" aria-label="${v ? 'Yes' : 'No'}">${v ? ICON.check : ''}</span>`;
      else if (col.type === 'link' && !M.isBlank(v)) {
        const href = (col.href || '').replace('{id}', encodeURIComponent(row.id));
        inner = href ? `<a href="${esc(href)}" class="eg-a">${esc(M.formatValue(col, v))}</a>` : esc(M.formatValue(col, v));
      } else inner = `<span class="eg-txt">${esc(M.formatValue(col, v))}</span>`;
      let indent = '';
      if (this.state.tree && j === this.treeColIndex()) {
        const pad = d.level * 16;
        indent = `<span class="eg-indent" style="width:${pad}px"></span>` + (d.hasChildren
          ? `<button type="button" class="eg-twist${d.collapsed ? '' : ' open'}" tabindex="-1" data-tw="${esc(row.id)}" aria-label="${d.collapsed ? 'Expand' : 'Collapse'}">${ICON.right}</button>`
          : '<span class="eg-twist-sp"></span>');
      }
      const sel = this.range && M.rangeContains(this.range, i, j);
      return `<div class="${this.cellClasses(i, j, col, row)}" role="gridcell" aria-selected="${sel ? 'true' : 'false'}" data-r="${i}" data-c="${j}"${err ? ` title="${esc(err)}" aria-invalid="true"` : ''} style="width:${col.width}px${col.pin === 'left' ? ';left:' + col.left + 'px' : ''}">${indent}${inner}</div>`;
    }

    treeColIndex() {
      const i = this.vcols.findIndex((c) => c.treeColumn);
      return i >= 0 ? i : 0;
    }

    rangeHasRow(i) { return !!this.range && i >= this.range.r1 && i <= this.range.r2 && this.range.c1 === 0 && this.range.c2 === this.vcols.length - 1; }

    cellEl(r, c) { return this.el.body.querySelector(`.eg-cell[data-r="${r}"][data-c="${c}"]`); }

    refreshCell(r, c) {
      const el = this.cellEl(r, c);
      const d = this.display[r];
      if (!el || !d || d.kind !== 'row') return;
      const tmp = document.createElement('div');
      tmp.innerHTML = this.cellHtml(r, c, this.vcols[c], d);
      el.replaceWith(tmp.firstElementChild);
    }

    locate(rowId, key) {
      const r = this.display.findIndex((d) => d.kind === 'row' && d.row.id === rowId);
      const c = this.vcols.findIndex((x) => x.key === key);
      return r >= 0 && c >= 0 ? { r, c } : null;
    }

    paint() {
      const cells = this.el.body.querySelectorAll('.eg-cell[data-c]');
      cells.forEach((el) => {
        const r = +el.dataset.r, c = +el.dataset.c;
        const d = this.display[r];
        if (!d || d.kind !== 'row') return;
        el.className = this.cellClasses(r, c, this.vcols[c], d.row);
        el.setAttribute('aria-selected', this.range && M.rangeContains(this.range, r, c) ? 'true' : 'false');
      });
      this.el.body.querySelectorAll('.eg-row').forEach((el) => {
        const r = +el.dataset.r;
        el.classList.toggle('act', !!this.active && this.active.r === r);
      });
      this.el.body.querySelectorAll('.eg-gutter[data-g]').forEach((el) => el.classList.toggle('sel', this.rangeHasRow(+el.dataset.g)));
      this.el.head.querySelectorAll('.eg-hcell[data-h]').forEach((el) => {
        const j = +el.dataset.h;
        el.classList.toggle('csel', !!this.range && j >= this.range.c1 && j <= this.range.c2 && !(this.range.r1 === this.range.r2 && this.range.c1 === this.range.c2));
        el.classList.toggle('cact', !!this.active && this.active.c === j);
      });
      if (this.active) this.el.scroller.setAttribute('aria-activedescendant', '');
      this.renderStatus();
    }

    select(r, c, extend) {
      if (!this.display.length || !this.vcols.length) return;
      r = Math.min(Math.max(r, 0), this.display.length - 1);
      c = Math.min(Math.max(c, 0), this.vcols.length - 1);
      if (extend && this.anchor) this.range = M.normalizeRange(this.anchor, { r, c });
      else { this.anchor = { r, c }; this.range = { r1: r, r2: r, c1: c, c2: c }; }
      this.active = { r, c };
      this.ensureVisible(r, c);
      this.paint();
    }

    ensureVisible(r, c) {
      const sc = this.el.scroller;
      const rowH = this.rowH;
      const head = this.headH || 40;
      const top = r * rowH, bottom = top + rowH;
      if (top < sc.scrollTop) sc.scrollTop = top;
      else if (bottom > sc.scrollTop + sc.clientHeight - head) sc.scrollTop = bottom - (sc.clientHeight - head);
      const col = this.vcols[c];
      if (col && col.pin !== 'left') {
        const L = col.left, R = col.left + col.width;
        if (L < sc.scrollLeft + this.pinnedW) sc.scrollLeft = Math.max(0, L - this.pinnedW);
        else if (R > sc.scrollLeft + sc.clientWidth) sc.scrollLeft = R - sc.clientWidth + 2;
      }
      this.renderWindow();
    }

    visibleRowKind(r) { return this.display[r] ? this.display[r].kind : null; }

    cellValue(r, c) {
      const d = this.display[r];
      return d && d.kind === 'row' ? d.row.cells[this.vcols[c].key] : undefined;
    }

    announce(msg) { this.el.sr.textContent = ''; setTimeout(() => { this.el.sr.textContent = msg; }, 20); }

    renderStatus() {
      const total = this.rows.length, shown = this.display.filter((d) => d.kind === 'row').length;
      const filtered = shown !== total;
      this.el.stLeft.textContent = `${shown}${filtered ? ' of ' + total : ''} rows · ${this.vcols.length} columns`;
      let mid = '';
      if (this.range && (this.range.r1 !== this.range.r2 || this.range.c1 !== this.range.c2)) {
        const vals = [];
        const cells = (this.range.r2 - this.range.r1 + 1) * (this.range.c2 - this.range.c1 + 1);
        if (cells <= 20000) {
          for (let r = this.range.r1; r <= this.range.r2; r++) {
            if (this.visibleRowKind(r) !== 'row') continue;
            for (let c = this.range.c1; c <= this.range.c2; c++) vals.push(this.cellValue(r, c));
          }
        }
        const s = M.rangeStats(vals);
        mid = `${s.cells} cells`;
        if (s.count) mid += ` · Σ ${fmtNum(s.sum)} · avg ${fmtNum(s.avg)} · min ${fmtNum(s.min)} · max ${fmtNum(s.max)}`;
      } else if (this.active && this.display[this.active.r] && this.display[this.active.r].kind === 'row') {
        const col = this.vcols[this.active.c];
        mid = col ? (col.title || col.key) : '';
      }
      this.el.stMid.textContent = mid;
      let right;
      if (this.pending > 0) right = `<span class="eg-st saving"><i></i>Saving ${this.pending}…</span>`;
      else if (this.errors.size) right = `<span class="eg-st bad">${this.errors.size} not saved</span>`;
      else right = `<span class="eg-st ok">${ICON.check}All changes saved</span>`;
      this.el.stRight.innerHTML = right;
    }
  }

  const fmtNum = (n) => (n === null || n === undefined ? '–' : new Intl.NumberFormat('en-US', { maximumFractionDigits: 2 }).format(n));

  window.EosGrid = EosGrid;
  window.EosGridKit = { esc, ICON, DENSITY, GUTTER, fmtNum };
})();
