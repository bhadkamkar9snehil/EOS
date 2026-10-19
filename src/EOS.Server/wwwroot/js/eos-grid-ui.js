(function () {
  'use strict';
  const M = window.EosGridModel;
  const G = window.EosGrid;
  const { esc, ICON, DENSITY, GUTTER, fmtNum } = window.EosGridKit;
  const same = (a, b) => (M.isBlank(a) && M.isBlank(b)) || a === b;
  const GROUPABLE = ['select', 'text', 'bool', 'rating', 'int', 'date'];

  Object.assign(G.prototype, {
    buildToolbar() {
      const t = this.el.toolbar;
      t.innerHTML = `
        <label class="eg-search">${ICON.search}<input type="search" placeholder="Search rows" aria-label="Search rows" spellcheck="false"><kbd>Ctrl F</kbd></label>
        <label class="eg-groupsel" hidden>${ICON.group}<select aria-label="Group rows by"></select></label>
        <span class="eg-spacer"></span>
        <button type="button" class="eg-btn" data-act="undo" title="Undo (Ctrl+Z)" aria-label="Undo">${ICON.undo}</button>
        <button type="button" class="eg-btn" data-act="redo" title="Redo (Ctrl+Y)" aria-label="Redo">${ICON.redo}</button>
        <span class="eg-seg" role="group" aria-label="Row density">
          <button type="button" data-den="compact" title="Compact rows">${ICON.rows}<span>S</span></button>
          <button type="button" data-den="cozy" title="Standard rows"><span>M</span></button>
          <button type="button" data-den="roomy" title="Roomy rows"><span>L</span></button>
        </span>
        <button type="button" class="eg-btn wide" data-act="cols">${ICON.cols}<span>Columns</span></button>
        <button type="button" class="eg-btn" data-act="export" title="Download CSV" aria-label="Download CSV">${ICON.download}</button>
        <button type="button" class="eg-btn wide" data-act="reset" title="Reset sort, filters, columns and density">${ICON.reset}<span>Reset</span></button>`;
      const search = t.querySelector('.eg-search input');
      let timer;
      search.addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(() => { this.state.search = search.value; this.relayout(); }, 120);
      });
      search.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') { search.value = ''; this.state.search = ''; this.relayout(); this.el.scroller.focus(); }
        if (e.key === 'Enter' || e.key === 'ArrowDown') { e.preventDefault(); this.el.scroller.focus(); if (!this.active) this.select(0, 0); }
      });
      t.querySelector('.eg-groupsel select').addEventListener('change', (e) => {
        this.state.groupBy = e.target.value || null;
        this.state.collapsed = new Set();
        this.savePrefs();
        this.relayout();
      });
      t.addEventListener('click', (e) => {
        const den = e.target.closest('[data-den]');
        if (den) { this.setDensity(den.dataset.den); return; }
        const b = e.target.closest('[data-act]');
        if (!b) return;
        const a = b.dataset.act;
        if (a === 'undo') this.doUndo();
        else if (a === 'redo') this.doRedo();
        else if (a === 'reset') this.resetView();
        else if (a === 'export') this.exportCsv();
        else if (a === 'cols') this.openColumnsMenu(b);
      });
    },

    syncToolbar() {
      const t = this.el.toolbar;
      const sel = t.querySelector('.eg-groupsel select');
      const opts = this.defs.filter((c) => c.groupable !== false && GROUPABLE.includes(c.type));
      const wrap = t.querySelector('.eg-groupsel');
      wrap.hidden = !this.opts.groupable || this.state.tree || opts.length === 0;
      sel.innerHTML = '<option value="">No grouping</option>' + opts.map((c) => `<option value="${esc(c.key)}">Group by ${esc(c.title || c.key)}</option>`).join('');
      sel.value = this.state.groupBy || '';
      t.querySelectorAll('[data-den]').forEach((b) => b.classList.toggle('on', b.dataset.den === this.state.density));
      t.querySelector('[data-act="undo"]').disabled = !this.undo.canUndo;
      t.querySelector('[data-act="redo"]').disabled = !this.undo.canRedo;
      const search = t.querySelector('.eg-search input');
      if (search.value !== this.state.search) search.value = this.state.search;
    },

    setDensity(d) {
      if (!DENSITY[d]) return;
      this.state.density = d;
      this.savePrefs();
      this.syncToolbar();
      this.relayout();
    },

    renderChips() {
      const chips = [];
      for (const c of this.allCols || []) {
        const f = this.state.filters[c.key];
        if (!M.isActiveFilter(f)) continue;
        let txt = '';
        if (f.text) txt = `contains “${f.text}”`;
        else if (f.values && f.values.length) txt = f.values.map((v) => (v === '' ? '(blank)' : M.formatValue(c, c.type === 'rating' || c.type === 'int' ? Number(v) : v))).join(', ');
        else if (f.blank === true) txt = 'is blank';
        else if (f.blank === false) txt = 'has a value';
        else txt = `${f.min !== undefined && f.min !== '' && f.min !== null ? '≥ ' + f.min : ''} ${f.max !== undefined && f.max !== '' && f.max !== null ? '≤ ' + f.max : ''}`.trim();
        chips.push(`<span class="eg-fchip"><button type="button" data-fc="${esc(c.key)}" class="eg-fc-main"><b>${esc(c.title || c.key)}</b> ${esc(txt)}</button><button type="button" data-fx="${esc(c.key)}" aria-label="Remove filter on ${esc(c.title || c.key)}">${ICON.x}</button></span>`);
      }
      const box = this.el.chips;
      box.hidden = chips.length === 0;
      box.innerHTML = chips.join('') + (chips.length > 1 ? `<button type="button" class="eg-clearall" data-fx="*">Clear all</button>` : '');
      this.syncToolbar();
    },

    toast(msg, o) {
      o = o || {};
      const t = this.el.toast;
      t.hidden = false;
      t.className = 'eg-toast' + (o.error ? ' bad' : '');
      t.innerHTML = `<span class="eg-tmsg">${esc(msg)}</span>${o.undo ? '<button type="button" class="eg-tundo">Undo</button>' : ''}<button type="button" class="eg-tx" aria-label="Dismiss">${ICON.x}</button>`;
      clearTimeout(this.toastTimer);
      this.toastTimer = setTimeout(() => { t.hidden = true; }, o.error ? 9000 : 6000);
      this.announce(msg);
    },

    bindEvents() {
      const sc = this.el.scroller;
      let raf = 0;
      sc.addEventListener('scroll', () => {
        if (raf) return;
        raf = requestAnimationFrame(() => { raf = 0; this.renderWindow(); this.positionEditor(); });
      });
      if (window.ResizeObserver) {
        let lastW = 0;
        this.ro = new ResizeObserver(() => {
          const w = sc.clientWidth;
          if (this.defs.length && Math.abs(w - lastW) > 2 && !this.editing) { lastW = w; this.relayoutKeep(); }
          else { this.renderWindow(true); this.positionEditor(); }
        });
        this.ro.observe(sc);
      }
      this.el.toast.addEventListener('click', (e) => {
        if (e.target.closest('.eg-tundo')) { this.el.toast.hidden = true; this.doUndo(); }
        else if (e.target.closest('.eg-tx')) this.el.toast.hidden = true;
      });
      this.el.chips.addEventListener('click', (e) => {
        const x = e.target.closest('[data-fx]');
        if (x) {
          if (x.dataset.fx === '*') this.state.filters = {};
          else delete this.state.filters[x.dataset.fx];
          this.relayout();
          return;
        }
        const m = e.target.closest('[data-fc]');
        if (m) {
          const j = this.vcols.findIndex((c) => c.key === m.dataset.fc);
          if (j >= 0) this.openColumnMenu(j, m);
        }
      });

      this.el.head.addEventListener('click', (e) => {
        if (e.target.closest('.eg-resize')) return;
        const menu = e.target.closest('[data-hm]');
        if (menu) { this.openColumnMenu(+menu.dataset.hm, menu); return; }
        if (e.target.closest('[data-all]')) { this.selectAll(); return; }
        const h = e.target.closest('.eg-hcell[data-h]');
        if (h) this.toggleSort(+h.dataset.h, e.shiftKey);
      });
      this.el.head.addEventListener('contextmenu', (e) => {
        const h = e.target.closest('.eg-hcell[data-h]');
        if (h) { e.preventDefault(); this.openColumnMenu(+h.dataset.h, h); }
      });
      this.el.head.addEventListener('mousedown', (e) => {
        const rs = e.target.closest('[data-rs]');
        if (!rs) return;
        e.preventDefault();
        e.stopPropagation();
        const j = +rs.dataset.rs;
        this.drag = { kind: 'resize', j, x0: e.clientX, w0: this.vcols[j].width };
        document.body.classList.add('eg-resizing');
      });
      this.el.head.addEventListener('dblclick', (e) => {
        const rs = e.target.closest('[data-rs]');
        if (rs) this.autosize(+rs.dataset.rs);
      });
      this.bindReorder();

      sc.addEventListener('mousedown', (e) => {
        if (e.button === 2) { this.onContextDown(e); return; }
        if (e.button !== 0) return;
        const tw = e.target.closest('[data-tw]');
        if (tw) {
          e.preventDefault();
          const rowEl = tw.closest('.eg-row');
          if (rowEl) { sc.focus({ preventScroll: true }); this.select(+rowEl.dataset.r, this.active ? this.active.c : 0); }
          this.toggleCollapse(tw.dataset.tw);
          return;
        }
        if (e.target.closest('.eg-editor, .eg-pop, a')) return;
        const g = e.target.closest('.eg-gutter[data-g]');
        if (g) {
          e.preventDefault();
          sc.focus();
          const r = +g.dataset.g;
          this.anchor = this.anchor && e.shiftKey ? this.anchor : { r, c: 0 };
          this.range = M.normalizeRange({ r: this.anchor.r, c: 0 }, { r, c: this.vcols.length - 1 });
          this.active = { r, c: this.active ? this.active.c : 0 };
          this.paint();
          this.drag = { kind: 'rows' };
          return;
        }
        const grp = e.target.closest('.eg-grp');
        if (grp) { e.preventDefault(); sc.focus(); this.select(+grp.dataset.r, 0); return; }
        const cell = e.target.closest('.eg-cell[data-c]');
        if (!cell) return;
        const r = +cell.dataset.r, c = +cell.dataset.c;
        const wasActive = this.active && this.active.r === r && this.active.c === c;
        if (this.editing) this.finishEdit(true);
        const col = this.vcols[c];
        const row = this.display[r].row;
        if (col.type === 'bool' && e.target.closest('.eg-check') && this.isEditable(row, col)) {
          e.preventDefault();
          sc.focus();
          this.select(r, c);
          this.toggleBool(r, c);
          return;
        }
        e.preventDefault();
        sc.focus({ preventScroll: true });
        this.select(r, c, e.shiftKey);
        this.drag = { kind: 'cells' };
        this.dragStartActive = wasActive && !e.shiftKey && ['select', 'rating', 'date'].includes(col.type);
      });
      sc.addEventListener('click', (e) => {
        if (!e.target.closest('[data-clear-all]')) return;
        this.state.filters = {};
        this.state.search = '';
        this.relayout();
      });
      sc.addEventListener('dblclick', (e) => {
        const cell = e.target.closest('.eg-cell[data-c]');
        if (!cell || e.target.closest('a, .eg-twist')) return;
        this.startEdit(+cell.dataset.r, +cell.dataset.c, {});
      });
      sc.addEventListener('contextmenu', (e) => {
        const cell = e.target.closest('.eg-cell[data-c]');
        if (!cell) return;
        e.preventDefault();
        this.openContextMenu(e, +cell.dataset.r, +cell.dataset.c);
      });
      document.addEventListener('mousemove', this._mm = (e) => this.onDragMove(e));
      document.addEventListener('mouseup', this._mu = () => this.onDragEnd());
      document.addEventListener('mousedown', this._md = (e) => {
        if (this.menu && !e.target.closest('.eg-menu')) this.closeMenu();
      }, true);
      sc.addEventListener('keydown', (e) => this.onKey(e));
      sc.addEventListener('copy', (e) => this.onCopy(e, false));
      sc.addEventListener('cut', (e) => this.onCopy(e, true));
      sc.addEventListener('paste', (e) => this.onPaste(e));
    },

    onContextDown(e) {
      const cell = e.target.closest('.eg-cell[data-c]');
      if (!cell) return;
      const r = +cell.dataset.r, c = +cell.dataset.c;
      if (!(this.range && M.rangeContains(this.range, r, c))) this.select(r, c);
    },

    onDragMove(e) {
      const d = this.drag;
      if (!d) return;
      if (d.kind === 'resize') {
        const w = Math.max(this.opts.minColWidth, d.w0 + e.clientX - d.x0);
        const col = this.vcols[d.j];
        this.prefs.cols[col.key] = Object.assign(this.prefs.cols[col.key] || {}, { w });
        this.relayoutKeep();
        return;
      }
      const sc = this.el.scroller;
      const rect = sc.getBoundingClientRect();
      if (e.clientY > rect.bottom - 24) sc.scrollTop += 24;
      else if (e.clientY < rect.top + (this.headH || 40) + 16) sc.scrollTop -= 24;
      if (e.clientX > rect.right - 24) sc.scrollLeft += 24;
      else if (e.clientX < rect.left + this.pinnedW + 16) sc.scrollLeft -= 24;
      const el = document.elementFromPoint(e.clientX, e.clientY);
      if (!el) return;
      if (d.kind === 'cells') {
        const cell = el.closest && el.closest('.eg-cell[data-c]');
        if (cell && this.active && (+cell.dataset.r !== this.active.r || +cell.dataset.c !== this.active.c || this.range.r1 !== this.range.r2 || this.range.c1 !== this.range.c2)) {
          this.dragMoved = true;
          const r = +cell.dataset.r, c = +cell.dataset.c;
          this.range = M.normalizeRange(this.anchor, { r, c });
          this.paint();
        }
      } else if (d.kind === 'rows') {
        const row = el.closest && el.closest('.eg-row');
        if (row) {
          const r = +row.dataset.r;
          this.range = M.normalizeRange({ r: this.anchor.r, c: 0 }, { r, c: this.vcols.length - 1 });
          this.paint();
        }
      }
    },

    onDragEnd() {
      const d = this.drag;
      this.drag = null;
      document.body.classList.remove('eg-resizing');
      if (!d) return;
      if (d.kind === 'resize') { this.savePrefs(); return; }
      if (d.kind === 'cells' && !this.dragMoved && this.dragStartActive && this.active) {
        this.startEdit(this.active.r, this.active.c, {});
      }
      this.dragMoved = false;
      this.dragStartActive = false;
    },

    relayoutKeep() {
      const keep = this.active && this.display[this.active.r] && this.display[this.active.r].row
        ? { keepRow: this.display[this.active.r].row.id, keepCol: this.vcols[this.active.c].key } : null;
      const range = this.range, anchor = this.anchor;
      this.relayout(keep);
      if (keep && range) { this.range = range; this.anchor = anchor; this.paint(); }
    },

    bindReorder() {
      const head = this.el.head;
      let dragKey = null;
      head.addEventListener('dragstart', (e) => {
        const h = e.target.closest && e.target.closest('.eg-hcell[data-h]');
        if (!h || this.drag) { e.preventDefault(); return; }
        dragKey = this.vcols[+h.dataset.h].key;
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', dragKey);
        h.classList.add('dragging');
      });
      head.addEventListener('dragend', () => {
        dragKey = null;
        head.querySelectorAll('.dragging, .drop-l, .drop-r').forEach((el) => el.classList.remove('dragging', 'drop-l', 'drop-r'));
      });
      head.addEventListener('dragover', (e) => {
        const h = e.target.closest && e.target.closest('.eg-hcell[data-h]');
        if (!h || !dragKey) return;
        e.preventDefault();
        const r = h.getBoundingClientRect();
        head.querySelectorAll('.drop-l, .drop-r').forEach((el) => el.classList.remove('drop-l', 'drop-r'));
        h.classList.add(e.clientX < r.left + r.width / 2 ? 'drop-l' : 'drop-r');
      });
      head.addEventListener('drop', (e) => {
        const h = e.target.closest && e.target.closest('.eg-hcell[data-h]');
        if (!h || !dragKey) return;
        e.preventDefault();
        const targetKey = this.vcols[+h.dataset.h].key;
        const r = h.getBoundingClientRect();
        const after = e.clientX >= r.left + r.width / 2;
        if (targetKey === dragKey) return;
        const order = this.allCols.map((c) => c.key).filter((k) => k !== dragKey);
        const idx = order.indexOf(targetKey) + (after ? 1 : 0);
        order.splice(idx, 0, dragKey);
        this.prefs.order = order;
        this.savePrefs();
        this.relayoutKeep();
      });
    },

    toggleSort(j, multi) {
      const key = this.vcols[j].key;
      const i = this.state.sort.findIndex((s) => s.key === key);
      const cur = i >= 0 ? this.state.sort[i].dir : null;
      const next = cur === null ? 'asc' : cur === 'asc' ? 'desc' : null;
      this.setSort(key, next, multi);
    },

    setSort(key, dir, multi) {
      let sort = multi ? this.state.sort.filter((s) => s.key !== key) : [];
      if (dir) {
        const prev = this.state.sort.findIndex((s) => s.key === key);
        if (multi && prev >= 0) sort.splice(Math.min(prev, sort.length), 0, { key, dir });
        else sort.push({ key, dir });
      }
      this.state.sort = sort;
      this.savePrefs();
      this.relayoutKeep();
    },

    toggleCollapse(id) {
      const cs = this.state.collapsed;
      const row = this.rows.find((r) => String(r.id) === String(id));
      const key = row ? row.id : id;
      if (cs.has(key)) cs.delete(key); else cs.add(key);
      this.relayoutKeep();
    },

    selectAll() {
      if (!this.display.length || !this.vcols.length) return;
      this.anchor = { r: 0, c: 0 };
      this.range = { r1: 0, r2: this.display.length - 1, c1: 0, c2: this.vcols.length - 1 };
      this.active = this.active || { r: 0, c: 0 };
      this.el.scroller.focus();
      this.paint();
    },

    onKey(e) {
      if (this.editing) return;
      const ctrl = e.ctrlKey || e.metaKey;
      const k = e.key;
      const last = this.display.length - 1, lastC = this.vcols.length - 1;
      if (!this.display.length) return;
      if (ctrl && !e.altKey) {
        const lk = k.toLowerCase();
        if (lk === 'z') { e.preventDefault(); e.shiftKey ? this.doRedo() : this.doUndo(); return; }
        if (lk === 'y') { e.preventDefault(); this.doRedo(); return; }
        if (lk === 'a') { e.preventDefault(); this.selectAll(); return; }
        if (lk === 'd') { e.preventDefault(); this.fill('down'); return; }
        if (lk === 'r') { e.preventDefault(); this.fill('right'); return; }
        if (lk === 'f') { e.preventDefault(); this.el.toolbar.querySelector('.eg-search input').focus(); return; }
        if (lk === 'c' || lk === 'x' || lk === 'v') return;
        if (k === ' ') { e.preventDefault(); if (this.active) { this.anchor = { r: 0, c: this.active.c }; this.range = { r1: 0, r2: last, c1: this.active.c, c2: this.active.c }; this.paint(); } return; }
      }
      if (!this.active) {
        if (['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight', 'Tab', 'Home', 'Enter'].includes(k)) { e.preventDefault(); this.select(0, 0); }
        return;
      }
      const a = this.active;
      const grp = this.display[a.r].kind === 'group';
      switch (k) {
        case 'ArrowDown': e.preventDefault(); this.select(ctrl ? last : a.r + 1, a.c, e.shiftKey); return;
        case 'ArrowUp': e.preventDefault(); this.select(ctrl ? 0 : a.r - 1, a.c, e.shiftKey); return;
        case 'ArrowRight':
          e.preventDefault();
          if (e.altKey || grp) { this.expandCollapse(a.r, false); return; }
          this.select(a.r, ctrl ? lastC : a.c + 1, e.shiftKey); return;
        case 'ArrowLeft':
          e.preventDefault();
          if (e.altKey || grp) { this.expandCollapse(a.r, true); return; }
          this.select(a.r, ctrl ? 0 : a.c - 1, e.shiftKey); return;
        case 'Tab': e.preventDefault(); e.shiftKey ? (a.c === 0 ? this.select(a.r - 1, lastC) : this.select(a.r, a.c - 1)) : (a.c === lastC ? this.select(a.r + 1, 0) : this.select(a.r, a.c + 1)); return;
        case 'Home': e.preventDefault(); this.select(ctrl ? 0 : a.r, 0, e.shiftKey); return;
        case 'End': e.preventDefault(); this.select(ctrl ? last : a.r, lastC, e.shiftKey); return;
        case 'PageDown': e.preventDefault(); this.select(a.r + this.pageRows(), a.c, e.shiftKey); return;
        case 'PageUp': e.preventDefault(); this.select(a.r - this.pageRows(), a.c, e.shiftKey); return;
        case 'Escape': if (this.range && (this.range.r1 !== this.range.r2 || this.range.c1 !== this.range.c2)) { this.select(a.r, a.c); } return;
        case 'Enter': case 'F2':
          e.preventDefault();
          if (grp) { this.expandCollapse(a.r, null); return; }
          this.startEdit(a.r, a.c, {});
          return;
        case 'Delete': case 'Backspace': e.preventDefault(); this.clearSelection(); return;
        case ' ':
          e.preventDefault();
          if (e.shiftKey) { this.anchor = { r: a.r, c: 0 }; this.range = { r1: a.r, r2: a.r, c1: 0, c2: lastC }; this.paint(); return; }
          if (!grp && this.vcols[a.c].type === 'bool') this.toggleBool(a.r, a.c);
          else if (!grp) this.startEdit(a.r, a.c, {});
          return;
        default:
      }
      if (!grp && k.length === 1 && !ctrl && !e.altKey) {
        const col = this.vcols[a.c];
        const row = this.display[a.r].row;
        if (!this.isEditable(row, col)) return;
        e.preventDefault();
        if (col.type === 'rating' && this.opts.quickRating && col.quick !== false && /^\d$/.test(k)) { this.quickRate(a.r, a.c, +k); return; }
        this.startEdit(a.r, a.c, { typed: k });
      }
    },

    pageRows() { return Math.max(1, Math.floor((this.el.scroller.clientHeight - (this.headH || 40)) / this.rowH) - 1); },

    expandCollapse(r, collapse) {
      const d = this.display[r];
      if (!d) return;
      const id = d.kind === 'group' ? d.id : d.row.id;
      const isOpen = d.kind === 'group' ? !d.collapsed : d.hasChildren && !d.collapsed;
      if (d.kind === 'row' && !d.hasChildren) return;
      if (collapse === null || (collapse === true && isOpen) || (collapse === false && !isOpen)) this.toggleCollapse(id);
    },

    quickRate(r, c, n) {
      const col = this.vcols[c];
      const row = this.display[r].row;
      const res = M.coerce(col, n === 0 && col.min !== 0 ? '' : String(n));
      if (!res.ok) { this.toast(`${col.title || col.key}: ${res.error}`, { error: true }); return; }
      this.commitEdits([{ rowId: row.id, key: col.key, value: res.value, old: row.cells[col.key] }]);
      this.select(Math.min(r + 1, this.display.length - 1), c);
    },

    toggleBool(r, c) {
      const row = this.display[r].row;
      const col = this.vcols[c];
      if (!this.isEditable(row, col)) return;
      this.commitEdits([{ rowId: row.id, key: col.key, value: !row.cells[col.key], old: !!row.cells[col.key] }]);
    },

    startEdit(r, c, o) {
      const d = this.display[r];
      if (!d || d.kind !== 'row') return;
      const col = this.vcols[c];
      const row = d.row;
      this.select(r, c);
      if (!this.isEditable(row, col)) {
        if (col.type === 'link') return;
        const why = this.opts.readOnly ? 'This grid is read-only.'
          : row.readonly ? 'This row is read-only.'
          : row.locked.includes(col.key) ? `${col.title || col.key} cannot be edited on this row.`
          : `${col.title || col.key} is calculated and cannot be edited.`;
        this.toast(why, { error: true });
        return;
      }
      if (col.type === 'bool') { this.toggleBool(r, c); return; }
      if (this.editing) this.finishEdit(true);
      if (col.type === 'select') this.openSelect(r, c);
      else if (col.type === 'rating') this.openRating(r, c, o.typed);
      else this.openInput(r, c, o);
    },

    editorBox(r, c) {
      const col = this.vcols[c];
      const sc = this.el.scroller;
      return { x: col.pin === 'left' ? sc.scrollLeft + col.left : col.left, y: r * this.rowH, w: col.width, h: this.rowH };
    },

    positionEditor() {
      const e = this.editing;
      if (!e) return;
      const b = this.editorBox(e.r, e.c);
      const el = e.el;
      el.style.left = b.x + 'px';
      el.style.top = b.y + 'px';
      if (e.kind === 'input') { el.style.width = Math.max(b.w, 120) + 'px'; el.style.height = b.h + 'px'; }
      else { el.style.minWidth = b.w + 'px'; el.style.top = b.y + b.h + 2 + 'px'; }
    },

    openInput(r, c, o) {
      const col = this.vcols[c];
      const row = this.display[r].row;
      const wrap = document.createElement('div');
      wrap.className = 'eg-editor t-' + col.type;
      const input = document.createElement('input');
      input.type = 'text';
      input.autocomplete = 'off';
      input.spellcheck = false;
      input.setAttribute('aria-label', col.title || col.key);
      if (col.type === 'number' || col.type === 'int') input.inputMode = 'decimal';
      if (col.type === 'date') input.placeholder = 'dd-mm-yyyy';
      wrap.appendChild(input);
      let picker = null;
      if (col.type === 'date') {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.tabIndex = -1;
        btn.className = 'eg-calbtn';
        btn.setAttribute('aria-label', 'Pick a date');
        btn.innerHTML = ICON.cal;
        picker = document.createElement('input');
        picker.type = 'date';
        picker.tabIndex = -1;
        picker.className = 'eg-datehidden';
        picker.addEventListener('change', () => { input.value = picker.value; input.focus(); });
        btn.addEventListener('mousedown', (ev) => { ev.preventDefault(); picker.value = /^\d{4}-/.test(input.value) ? input.value : ''; if (picker.showPicker) picker.showPicker(); });
        wrap.appendChild(btn);
        wrap.appendChild(picker);
      }
      this.el.body.appendChild(wrap);
      const typed = o.typed !== undefined;
      input.value = typed ? o.typed : M.rawText(col, row.cells[col.key]);
      this.editing = { kind: 'input', r, c, el: wrap, input, typedMode: typed, rowId: row.id, key: col.key, old: row.cells[col.key] };
      this.positionEditor();
      input.focus();
      if (typed) input.setSelectionRange(input.value.length, input.value.length); else input.select();
      const live = () => {
        const res = M.coerce(col, input.value);
        wrap.classList.toggle('bad', !res.ok);
        wrap.title = res.ok ? '' : res.error;
      };
      input.addEventListener('input', live);
      input.addEventListener('keydown', (ev) => {
        if (ev.key === 'Enter') { ev.preventDefault(); this.finishEdit(true, ev.shiftKey ? 'up' : 'down'); }
        else if (ev.key === 'Tab') { ev.preventDefault(); this.finishEdit(true, ev.shiftKey ? 'left' : 'right'); }
        else if (ev.key === 'Escape') { ev.preventDefault(); this.finishEdit(false); }
        else if (ev.key === 'F2') { ev.preventDefault(); this.editing.typedMode = false; }
        else if (this.editing.typedMode && (ev.key === 'ArrowDown' || ev.key === 'ArrowUp' || ev.key === 'ArrowLeft' || ev.key === 'ArrowRight')) {
          ev.preventDefault();
          this.finishEdit(true, { ArrowDown: 'down', ArrowUp: 'up', ArrowLeft: 'left', ArrowRight: 'right' }[ev.key]);
        }
        ev.stopPropagation();
      });
      input.addEventListener('blur', () => { if (this.editing && this.editing.input === input && !this.editing.closing) setTimeout(() => { if (this.editing && this.editing.input === input && document.activeElement !== picker) this.finishEdit(true); }, 0); });
      live();
    },

    openSelect(r, c) {
      const col = this.vcols[c];
      const row = this.display[r].row;
      const cur = row.cells[col.key];
      const options = (col.options || []).slice();
      const items = col.nullable === false ? options : [{ value: null, label: 'Clear value', clear: true }].concat(options);
      const pop = document.createElement('div');
      pop.className = 'eg-pop eg-list';
      pop.tabIndex = -1;
      pop.setAttribute('role', 'listbox');
      pop.innerHTML = items.map((o, i) => `<div class="eg-opt${o.clear ? ' clear' : ''}" role="option" data-i="${i}">${o.clear ? esc(o.label) : `<span class="eg-chip${o.tone ? ' tone-' + esc(o.tone) : ''}">${esc(o.label)}</span>`}${o.value === cur && !o.clear ? ICON.check : ''}</div>`).join('');
      this.el.body.appendChild(pop);
      let hi = Math.max(0, items.findIndex((o) => o.value === cur && !o.clear));
      this.editing = { kind: 'pop', r, c, el: pop, rowId: row.id, key: col.key, old: cur, items, hi, buffer: '', bufT: 0 };
      this.positionEditor();
      const paintHi = () => pop.querySelectorAll('.eg-opt').forEach((el, i) => { el.classList.toggle('hi', i === this.editing.hi); if (i === this.editing.hi && el.scrollIntoView) el.scrollIntoView({ block: 'nearest' }); });
      this.editing.paintHi = paintHi;
      paintHi();
      const choose = (i) => { const it = items[i]; if (!it) return; this.finishPop(it.value); };
      pop.addEventListener('mousedown', (ev) => { ev.preventDefault(); const o = ev.target.closest('.eg-opt'); if (o) choose(+o.dataset.i); });
      pop.addEventListener('mousemove', (ev) => { const o = ev.target.closest('.eg-opt'); if (o && this.editing) { this.editing.hi = +o.dataset.i; paintHi(); } });
      pop.addEventListener('keydown', (ev) => {
        const e = this.editing;
        if (!e) return;
        if (ev.key === 'ArrowDown') { ev.preventDefault(); e.hi = Math.min(items.length - 1, e.hi + 1); paintHi(); }
        else if (ev.key === 'ArrowUp') { ev.preventDefault(); e.hi = Math.max(0, e.hi - 1); paintHi(); }
        else if (ev.key === 'Enter') { ev.preventDefault(); choose(e.hi); }
        else if (ev.key === 'Tab') { ev.preventDefault(); this.finishPop(items[e.hi].value, ev.shiftKey ? 'left' : 'right'); }
        else if (ev.key === 'Escape') { ev.preventDefault(); this.cancelPop(); }
        else if (ev.key.length === 1 && !ev.ctrlKey && !ev.metaKey) {
          const now = Date.now();
          e.buffer = now - e.bufT > 700 ? ev.key : e.buffer + ev.key;
          e.bufT = now;
          const i = M.typeAhead(items.map((o) => (o.clear ? { label: '' } : o)), e.buffer);
          if (i >= 0) { e.hi = i; paintHi(); }
        }
        ev.stopPropagation();
      });
      pop.addEventListener('blur', () => setTimeout(() => { if (this.editing && this.editing.el === pop) this.cancelPop(); }, 120));
      pop.focus();
    },

    openRating(r, c, typed) {
      const col = this.vcols[c];
      const row = this.display[r].row;
      const cur = row.cells[col.key];
      const min = col.min === undefined ? 1 : col.min, max = col.max === undefined ? 5 : col.max;
      const pop = document.createElement('div');
      pop.className = 'eg-pop eg-rate';
      pop.tabIndex = -1;
      pop.setAttribute('role', 'listbox');
      let html = '';
      for (let v = min; v <= max; v++) html += `<button type="button" class="eg-pill r${v}${v === cur ? ' cur' : ''}" role="option" data-v="${v}" tabindex="-1">${v}</button>`;
      if (col.nullable !== false) html += `<button type="button" class="eg-rclear" data-v="" tabindex="-1">Clear</button>`;
      pop.innerHTML = html;
      this.el.body.appendChild(pop);
      this.editing = { kind: 'pop', r, c, el: pop, rowId: row.id, key: col.key, old: cur, rating: true, min, max, hi: cur === null || cur === undefined ? min : cur };
      this.positionEditor();
      const paintHi = () => pop.querySelectorAll('[data-v]').forEach((b) => b.classList.toggle('hi', b.dataset.v !== '' && +b.dataset.v === this.editing.hi));
      paintHi();
      pop.addEventListener('mousedown', (ev) => { ev.preventDefault(); const b = ev.target.closest('[data-v]'); if (b) this.finishPop(b.dataset.v === '' ? null : +b.dataset.v); });
      pop.addEventListener('keydown', (ev) => {
        const e = this.editing;
        if (!e) return;
        if (ev.key === 'ArrowRight' || ev.key === 'ArrowUp') { ev.preventDefault(); e.hi = Math.min(max, e.hi + 1); paintHi(); }
        else if (ev.key === 'ArrowLeft' || ev.key === 'ArrowDown') { ev.preventDefault(); e.hi = Math.max(min, e.hi - 1); paintHi(); }
        else if (ev.key === 'Enter') { ev.preventDefault(); this.finishPop(e.hi); }
        else if (ev.key === 'Tab') { ev.preventDefault(); this.finishPop(e.hi, ev.shiftKey ? 'left' : 'right'); }
        else if (ev.key === 'Escape') { ev.preventDefault(); this.cancelPop(); }
        else if (ev.key === 'Delete' || ev.key === 'Backspace') { ev.preventDefault(); this.finishPop(null); }
        else if (/^\d$/.test(ev.key)) { ev.preventDefault(); const n = +ev.key; if (n >= min && n <= max) this.finishPop(n); }
        ev.stopPropagation();
      });
      pop.addEventListener('blur', () => setTimeout(() => { if (this.editing && this.editing.el === pop) this.cancelPop(); }, 120));
      pop.focus();
    },

    finishPop(value, move) {
      const e = this.editing;
      if (!e) return;
      const col = this.vcols[e.c];
      e.closing = true;
      this.closeEditor();
      this.el.scroller.focus({ preventScroll: true });
      const res = M.coerce(col, value);
      if (!res.ok) { this.toast(res.error, { error: true }); return; }
      this.commitEdits([{ rowId: e.rowId, key: e.key, value: res.value, old: e.old }]);
      if (move) this.moveAfter(e.r, e.c, move);
    },

    cancelPop() {
      if (!this.editing) return;
      this.editing.closing = true;
      this.closeEditor();
      this.el.scroller.focus({ preventScroll: true });
    },

    closeEditor() {
      if (this.editing && this.editing.el) this.editing.el.remove();
      this.editing = null;
    },

    finishEdit(commit, move) {
      const e = this.editing;
      if (!e) return;
      if (e.kind === 'pop') { this.cancelPop(); return; }
      if (!commit) { e.closing = true; this.closeEditor(); this.el.scroller.focus({ preventScroll: true }); return; }
      const col = this.vcols[e.c];
      const res = M.coerce(col, e.input.value);
      if (!res.ok) {
        e.el.classList.add('bad', 'shake');
        setTimeout(() => e.el && e.el.classList.remove('shake'), 400);
        this.toast(`${col.title || col.key}: ${res.error}`, { error: true });
        e.input.focus();
        return;
      }
      e.closing = true;
      this.closeEditor();
      this.el.scroller.focus({ preventScroll: true });
      this.commitEdits([{ rowId: e.rowId, key: e.key, value: res.value, old: e.old }]);
      if (move) this.moveAfter(e.r, e.c, move);
    },

    moveAfter(r, c, dir) {
      if (dir === 'down') this.select(Math.min(r + 1, this.display.length - 1), c);
      else if (dir === 'up') this.select(Math.max(r - 1, 0), c);
      else if (dir === 'right') this.select(r, Math.min(c + 1, this.vcols.length - 1));
      else if (dir === 'left') this.select(r, Math.max(c - 1, 0));
    },

    labelFor(edit) {
      const row = this.rowIndex.get(edit.rowId);
      const col = this.defs.find((c) => c.key === edit.key);
      const lc = this.vcols.find((c) => c.rowLabel) || this.vcols[0];
      const who = row && lc ? M.formatValue(lc, row.cells[lc.key]) : '';
      return { who, col };
    },

    async commitEdits(edits, o) {
      o = o || {};
      edits = edits.filter((e) => !same(e.old, e.value));
      if (!edits.length) return { accepted: [], rejected: [] };
      for (const e of edits) {
        const row = this.rowIndex.get(e.rowId);
        if (!row) continue;
        const k = e.rowId + '|' + e.key;
        row.cells[e.key] = e.value;
        this.saving.add(k);
        this.errors.delete(k);
      }
      this.pending += edits.length;
      this.reflow();
      let result;
      try {
        result = this.onCommit ? await this.onCommit(edits.map((e) => ({ rowId: e.rowId, key: e.key, value: e.value, old: e.old }))) : {};
      } catch (ex) {
        result = { rejected: edits.map((e) => ({ rowId: e.rowId, key: e.key, message: (ex && ex.message) || 'Could not save.' })) };
      }
      result = result || {};
      const rej = new Map((result.rejected || []).map((r) => [r.rowId + '|' + r.key, r]));
      const accepted = [];
      const rejected = [];
      for (const e of edits) {
        const k = e.rowId + '|' + e.key;
        const row = this.rowIndex.get(e.rowId);
        this.saving.delete(k);
        this.pending = Math.max(0, this.pending - 1);
        if (rej.has(k)) {
          const r = rej.get(k);
          if (row) row.cells[e.key] = r.value !== undefined && r.value !== null ? r.value : e.old;
          this.errors.set(k, r.message || 'Not saved.');
          rejected.push(Object.assign({}, e, { message: r.message }));
          setTimeout(() => { this.errors.delete(k); this.reflow(); }, 9000);
        } else {
          accepted.push(e);
          this.flash.set(k, true);
          setTimeout(() => { this.flash.delete(k); const loc = this.locate(e.rowId, e.key); if (loc) this.refreshCell(loc.r, loc.c); }, 1100);
        }
      }
      for (const u of result.updated || []) {
        const row = this.rowIndex.get(u.rowId);
        if (row) row.cells[u.key] = u.value;
      }
      if (o.record !== false && accepted.length) this.undo.push(accepted);
      this.reflow();
      if (rejected.length) {
        const first = rejected[0];
        const lab = this.labelFor(first);
        this.toast(rejected.length === 1 ? `${lab.who} · ${lab.col ? lab.col.title : first.key}: ${first.message}` : `${rejected.length} cells not saved. ${first.message}`, { error: true });
      } else if (accepted.length) {
        if (o.label) this.toast(o.label, { undo: o.record !== false });
        else if (accepted.length === 1) {
          const e = accepted[0];
          const lab = this.labelFor(e);
          const f = (v) => (M.isBlank(v) ? '–' : M.formatValue(lab.col, v));
          this.toast(`${lab.who} · ${lab.col ? lab.col.title : e.key}: ${f(e.old)} → ${f(e.value)}`, { undo: o.record !== false });
        } else this.toast(`${accepted.length} cells updated`, { undo: o.record !== false });
      }
      this.syncToolbar();
      return { accepted, rejected };
    },

    reflow() {
      this.winKey = '';
      this.renderWindow(true);
      this.renderStatus();
      this.syncToolbar();
    },

    async doUndo() {
      const batch = this.undo.undo();
      if (!batch) return;
      const inv = batch.map((e) => ({ rowId: e.rowId, key: e.key, value: e.old, old: e.value }));
      const r = await this.commitEdits(inv, { record: false, label: `Undid ${batch.length === 1 ? '1 change' : batch.length + ' changes'}` });
      if (!r.accepted.length) this.undo.redo();
      else if (r.rejected.length) { /* partial: keep as undone */ }
      this.syncToolbar();
    },

    async doRedo() {
      const batch = this.undo.redo();
      if (!batch) return;
      const again = batch.map((e) => ({ rowId: e.rowId, key: e.key, value: e.value, old: e.old }));
      const r = await this.commitEdits(again, { record: false, label: `Redid ${batch.length === 1 ? '1 change' : batch.length + ' changes'}` });
      if (!r.accepted.length) this.undo.undo();
      this.syncToolbar();
    },

    rangeCells() {
      if (!this.range) return [];
      const out = [];
      for (let r = this.range.r1; r <= this.range.r2; r++) {
        const d = this.display[r];
        if (!d || d.kind !== 'row') continue;
        for (let c = this.range.c1; c <= this.range.c2; c++) out.push({ r, c, row: d.row, col: this.vcols[c] });
      }
      return out;
    },

    clearSelection() {
      const edits = [];
      let skipped = 0;
      for (const x of this.rangeCells()) {
        if (!this.isEditable(x.row, x.col)) { skipped++; continue; }
        const res = M.coerce(x.col, '');
        if (!res.ok) { skipped++; continue; }
        edits.push({ rowId: x.row.id, key: x.col.key, value: res.value, old: x.row.cells[x.col.key] });
      }
      if (!edits.length) { if (skipped) this.toast('Those cells cannot be cleared.', { error: true }); return; }
      this.commitEdits(edits, { label: `Cleared ${edits.length} cell${edits.length === 1 ? '' : 's'}${skipped ? ` (${skipped} kept)` : ''}` });
    },

    fill(dir) {
      if (!this.range) return;
      const plan = M.planFill(this.range, dir);
      const edits = [];
      for (const p of plan) {
        const d = this.display[p.r], s = this.display[p.fromR];
        if (!d || d.kind !== 'row' || !s || s.kind !== 'row') continue;
        const col = this.vcols[p.c], scol = this.vcols[p.fromC];
        if (!this.isEditable(d.row, col) || col.type !== scol.type) continue;
        edits.push({ rowId: d.row.id, key: col.key, value: s.row.cells[scol.key], old: d.row.cells[col.key] });
      }
      if (!edits.length) return;
      this.commitEdits(edits, { label: `Filled ${edits.length} cell${edits.length === 1 ? '' : 's'} ${dir}` });
    },

    onCopy(e, cut) {
      if (this.editing || !this.range) return;
      const m = [];
      for (let r = this.range.r1; r <= this.range.r2; r++) {
        const d = this.display[r];
        if (!d || d.kind !== 'row') continue;
        const line = [];
        for (let c = this.range.c1; c <= this.range.c2; c++) line.push(M.rawText(this.vcols[c], d.row.cells[this.vcols[c].key]));
        m.push(line);
      }
      e.clipboardData.setData('text/plain', M.toTSV(m));
      e.preventDefault();
      const n = m.length * (this.range.c2 - this.range.c1 + 1);
      this.announce(`Copied ${n} cells`);
      if (cut) this.clearSelection();
      else this.toast(`Copied ${n} cell${n === 1 ? '' : 's'}`);
    },

    onPaste(e) {
      if (this.editing || !this.active) return;
      const text = e.clipboardData.getData('text/plain');
      if (!text) return;
      e.preventDefault();
      const matrix = M.parseTSV(text);
      const rowsIdx = [];
      this.display.forEach((d, i) => { if (d.kind === 'row') rowsIdx.push(i); });
      const startPos = rowsIdx.indexOf(this.active.r);
      const range = this.range ? { r1: rowsIdx.indexOf(this.range.r1 >= 0 ? this.firstRowAtOrAfter(this.range.r1) : -1), r2: rowsIdx.indexOf(this.lastRowAtOrBefore(this.range.r2)), c1: this.range.c1, c2: this.range.c2 } : null;
      const plan = M.planPaste({ r: Math.max(startPos, 0), c: this.active.c }, matrix, range && range.r1 >= 0 && range.r2 >= 0 ? range : null, rowsIdx.length, this.vcols.length);
      const edits = [];
      let skipped = 0, firstErr = '';
      for (const p of plan) {
        const row = this.display[rowsIdx[p.r]].row;
        const col = this.vcols[p.c];
        if (!this.isEditable(row, col)) { skipped++; if (!firstErr) firstErr = `${col.title || col.key} is not editable.`; continue; }
        const res = M.coerce(col, p.text);
        if (!res.ok) { skipped++; if (!firstErr) firstErr = `${col.title || col.key}: ${res.error}`; continue; }
        edits.push({ rowId: row.id, key: col.key, value: res.value, old: row.cells[col.key] });
      }
      if (!edits.length) { this.toast(skipped ? `Nothing pasted. ${firstErr}` : 'Nothing to paste.', { error: true }); return; }
      this.commitEdits(edits, { label: `Pasted ${edits.length} cell${edits.length === 1 ? '' : 's'}${skipped ? `, ${skipped} skipped. ${firstErr}` : ''}` });
    },

    firstRowAtOrAfter(r) { while (r < this.display.length && this.display[r].kind !== 'row') r++; return r; },
    lastRowAtOrBefore(r) { while (r >= 0 && this.display[r].kind !== 'row') r--; return r; },

    exportCsv() {
      const cols = this.vcols;
      const m = [cols.map((c) => c.title || c.key)];
      for (const d of this.display) if (d.kind === 'row') m.push(cols.map((c) => M.rawText(c, d.row.cells[c.key])));
      const blob = new Blob(['﻿' + M.toCSV(m)], { type: 'text/csv;charset=utf-8' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = (this.id || 'grid') + '.csv';
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 2000);
      this.toast(`Downloaded ${m.length - 1} rows`);
    },

    autosize(j) {
      const col = this.vcols[j];
      const ctx = this.textContext();
      const measure = (s) => (ctx ? ctx.measureText(s).width : String(s).length * 7.4);
      if (ctx) { const cs = getComputedStyle(this.el.scroller); ctx.font = `${cs.fontWeight} 13px ${cs.fontFamily}`; }
      let w = measure(col.title || col.key) / 2 + 40;
      for (const d of this.display) {
        if (d.kind !== 'row') continue;
        w = Math.max(w, measure(M.formatValue(col, d.row.cells[col.key])) + 30);
      }
      const width = Math.round(Math.min(Math.max(w, this.opts.minColWidth), 420));
      this.prefs.cols[col.key] = Object.assign(this.prefs.cols[col.key] || {}, { w: width });
      this.savePrefs();
      this.relayoutKeep();
    },

    closeMenu() {
      if (this.menu) { this.menu.el.remove(); this.menu = null; }
    },

    showMenu(anchor, el, opts) {
      this.closeMenu();
      el.classList.add('eg-menu');
      document.body.appendChild(el);
      const r = anchor.getBoundingClientRect ? anchor.getBoundingClientRect() : { left: anchor.x, right: anchor.x, top: anchor.y, bottom: anchor.y, width: 0 };
      const mw = el.offsetWidth, mh = el.offsetHeight;
      let x = (opts && opts.alignRight ? r.right - mw : r.left);
      let y = r.bottom + 4;
      x = Math.max(8, Math.min(x, window.innerWidth - mw - 8));
      if (y + mh > window.innerHeight - 8) y = Math.max(8, r.top - mh - 4);
      el.style.left = x + 'px';
      el.style.top = y + 'px';
      this.menu = { el };
      el.addEventListener('keydown', (ev) => { if (ev.key === 'Escape') { this.closeMenu(); this.el.scroller.focus({ preventScroll: true }); } });
      const first = el.querySelector('input, button, [tabindex]');
      if (first && opts && opts.focus) first.focus();
    },

    openColumnsMenu(btn) {
      const el = document.createElement('div');
      el.className = 'eg-cmenu';
      const draw = () => {
        el.innerHTML = `<div class="eg-mh">Columns <button type="button" class="eg-link" data-all>Show all</button></div>
          <input type="search" class="eg-mfind" placeholder="Find column" aria-label="Find column" spellcheck="false">
          <div class="eg-mlist">${this.allCols.map((c) => `<label class="eg-mrow" data-k="${esc(c.key)}"><input type="checkbox" ${c.hidden ? '' : 'checked'} data-k="${esc(c.key)}"><span>${esc(c.title || c.key)}</span>${c.pin === 'left' ? '<em>pinned</em>' : ''}</label>`).join('')}</div>`;
      };
      draw();
      el.addEventListener('change', (ev) => {
        const k = ev.target.dataset && ev.target.dataset.k;
        if (!k) return;
        this.prefs.cols[k] = Object.assign(this.prefs.cols[k] || {}, { hidden: !ev.target.checked });
        this.savePrefs();
        this.relayoutKeep();
      });
      el.addEventListener('click', (ev) => {
        if (ev.target.closest('[data-all]')) {
          for (const c of this.allCols) this.prefs.cols[c.key] = Object.assign(this.prefs.cols[c.key] || {}, { hidden: false });
          this.savePrefs();
          this.relayoutKeep();
          draw();
        }
      });
      el.addEventListener('input', (ev) => {
        if (!ev.target.classList.contains('eg-mfind')) return;
        const q = ev.target.value.toLowerCase();
        el.querySelectorAll('.eg-mrow').forEach((row) => { row.hidden = q && !row.textContent.toLowerCase().includes(q); });
      });
      this.showMenu(btn, el, { alignRight: true, focus: true });
    },

    openColumnMenu(j, anchor) {
      const col = this.vcols[j];
      if (!col) return;
      const el = document.createElement('div');
      el.className = 'eg-cmenu';
      const f = this.state.filters[col.key] || {};
      const sortDir = (this.state.sort.find((s) => s.key === col.key) || {}).dir;
      let filterHtml = '';
      const t = col.type;
      if (t === 'number' || t === 'int' || t === 'date') {
        const inType = t === 'date' ? 'date' : 'number';
        filterHtml = `<div class="eg-mh sub">Filter</div><div class="eg-frange"><label>From<input type="${inType}" data-f="min" value="${esc(f.min ?? '')}"></label><label>To<input type="${inType}" data-f="max" value="${esc(f.max ?? '')}"></label></div>`;
      } else if (t === 'select' || t === 'rating' || t === 'bool') {
        const vals = new Map();
        for (const r of this.rows) {
          const v = r.cells[col.key];
          const k = M.isBlank(v) ? '' : String(v);
          vals.set(k, (vals.get(k) || 0) + 1);
        }
        const keys = [...vals.keys()].sort((a, b) => (a === '' ? 1 : b === '' ? -1 : M.compareValues(col, t === 'rating' ? +a : a, t === 'rating' ? +b : b)));
        const sel = new Set(f.values || []);
        filterHtml = `<div class="eg-mh sub">Filter</div><div class="eg-mlist short">${keys.map((k) => `<label class="eg-mrow"><input type="checkbox" data-v="${esc(k)}" ${sel.has(k) ? 'checked' : ''}><span>${k === '' ? '(blank)' : esc(M.formatValue(col, t === 'rating' ? +k : k === 'true' ? true : k === 'false' ? false : k))}</span><em>${vals.get(k)}</em></label>`).join('')}</div>`;
      } else {
        filterHtml = `<div class="eg-mh sub">Filter</div><input type="search" class="eg-mfind" data-f="text" placeholder="Contains…" value="${esc(f.text || '')}" spellcheck="false">`;
      }
      const groupable = this.opts.groupable && !this.state.tree && col.groupable !== false && GROUPABLE.includes(t);
      el.innerHTML = `<div class="eg-mh">${esc(col.title || col.key)}</div>
        <button type="button" class="eg-mi${sortDir === 'asc' ? ' on' : ''}" data-m="asc">${ICON.asc}<span>Sort ascending</span></button>
        <button type="button" class="eg-mi${sortDir === 'desc' ? ' on' : ''}" data-m="desc">${ICON.desc}<span>Sort descending</span></button>
        ${sortDir ? `<button type="button" class="eg-mi" data-m="nosort">${ICON.x}<span>Clear sort</span></button>` : ''}
        <div class="eg-msep"></div>${filterHtml}
        <div class="eg-mfoot"><button type="button" class="eg-link" data-m="clearf">Clear filter</button></div>
        <div class="eg-msep"></div>
        ${groupable ? `<button type="button" class="eg-mi${this.state.groupBy === col.key ? ' on' : ''}" data-m="group">${ICON.group}<span>${this.state.groupBy === col.key ? 'Ungroup' : 'Group by this column'}</span></button>` : ''}
        <button type="button" class="eg-mi${col.pin === 'left' ? ' on' : ''}" data-m="pin">${ICON.pin}<span>${col.pin === 'left' ? 'Unpin' : 'Pin to left'}</span></button>
        <button type="button" class="eg-mi" data-m="fit">${ICON.expand}<span>Fit width to content</span></button>
        <button type="button" class="eg-mi" data-m="hide">${ICON.eyeOff}<span>Hide column</span></button>`;
      const apply = () => {
        const nf = {};
        el.querySelectorAll('[data-f]').forEach((i) => { if (i.value !== '') nf[i.dataset.f] = i.value; });
        const vs = [...el.querySelectorAll('input[data-v]:checked')].map((i) => i.dataset.v);
        if (vs.length) nf.values = vs;
        if (M.isActiveFilter(nf)) this.state.filters[col.key] = nf; else delete this.state.filters[col.key];
        this.relayoutKeep();
      };
      el.addEventListener('input', (ev) => { if (ev.target.dataset.f) apply(); });
      el.addEventListener('change', (ev) => { if (ev.target.dataset.v !== undefined || ev.target.dataset.f) apply(); });
      el.addEventListener('click', (ev) => {
        const b = ev.target.closest('[data-m]');
        if (!b) return;
        const m = b.dataset.m;
        if (m === 'asc' || m === 'desc') this.setSort(col.key, m, false);
        else if (m === 'nosort') this.setSort(col.key, null, false);
        else if (m === 'clearf') { delete this.state.filters[col.key]; this.relayoutKeep(); }
        else if (m === 'group') { this.state.groupBy = this.state.groupBy === col.key ? null : col.key; this.state.collapsed = new Set(); this.savePrefs(); this.relayoutKeep(); }
        else if (m === 'pin') { this.prefs.cols[col.key] = Object.assign(this.prefs.cols[col.key] || {}, { pin: col.pin === 'left' ? null : 'left' }); this.savePrefs(); this.relayoutKeep(); }
        else if (m === 'fit') this.autosize(j);
        else if (m === 'hide') { this.prefs.cols[col.key] = Object.assign(this.prefs.cols[col.key] || {}, { hidden: true }); this.savePrefs(); this.relayoutKeep(); }
        if (m !== 'clearf') this.closeMenu();
        else this.closeMenu();
      });
      this.showMenu(anchor, el, { focus: false });
      const input = el.querySelector('input[data-f="text"]');
      if (input) input.focus();
    },

    openContextMenu(ev, r, c) {
      const d = this.display[r];
      if (!d || d.kind !== 'row') return;
      const col = this.vcols[c];
      const row = d.row;
      const editable = this.isEditable(row, col);
      const v = row.cells[col.key];
      const el = document.createElement('div');
      el.className = 'eg-cmenu';
      el.innerHTML = `
        <button type="button" class="eg-mi" data-m="copy"><span>Copy</span><kbd>Ctrl C</kbd></button>
        <button type="button" class="eg-mi" data-m="cut" ${editable ? '' : 'disabled'}><span>Cut</span><kbd>Ctrl X</kbd></button>
        <button type="button" class="eg-mi" data-m="clear" ${editable ? '' : 'disabled'}><span>Clear</span><kbd>Del</kbd></button>
        <div class="eg-msep"></div>
        <button type="button" class="eg-mi" data-m="down" ${editable ? '' : 'disabled'}><span>Fill down</span><kbd>Ctrl D</kbd></button>
        <button type="button" class="eg-mi" data-m="right" ${editable ? '' : 'disabled'}><span>Fill right</span><kbd>Ctrl R</kbd></button>
        <div class="eg-msep"></div>
        <button type="button" class="eg-mi" data-m="only"><span>Show only “${esc(M.isBlank(v) ? 'blank' : M.formatValue(col, v))}”</span></button>
        <button type="button" class="eg-mi" data-m="asc"><span>Sort ${esc(col.title || col.key)} ascending</span></button>
        <button type="button" class="eg-mi" data-m="desc"><span>Sort ${esc(col.title || col.key)} descending</span></button>
        <div class="eg-msep"></div>
        <button type="button" class="eg-mi" data-m="undo" ${this.undo.canUndo ? '' : 'disabled'}><span>Undo</span><kbd>Ctrl Z</kbd></button>`;
      el.addEventListener('click', (e) => {
        const b = e.target.closest('[data-m]');
        if (!b || b.disabled) return;
        const m = b.dataset.m;
        this.closeMenu();
        this.el.scroller.focus({ preventScroll: true });
        if (m === 'copy' || m === 'cut') document.execCommand(m);
        else if (m === 'clear') this.clearSelection();
        else if (m === 'down' || m === 'right') this.fill(m);
        else if (m === 'only') { this.state.filters[col.key] = M.isBlank(v) ? { blank: true } : { values: [String(v)] }; this.relayoutKeep(); }
        else if (m === 'asc' || m === 'desc') this.setSort(col.key, m, false);
        else if (m === 'undo') this.doUndo();
      });
      this.showMenu({ x: ev.clientX, y: ev.clientY }, el, {});
    },

    destroy() {
      document.removeEventListener('mousemove', this._mm);
      document.removeEventListener('mouseup', this._mu);
      document.removeEventListener('mousedown', this._md, true);
      if (this.ro) this.ro.disconnect();
      this.closeMenu();
      this.host.innerHTML = '';
      this.host.classList.remove('eg');
    },
  });

  const instances = new Map();
  window.EosGridHost = {
    create(id, el, ref, config) {
      if (instances.has(id)) { instances.get(id).destroy(); instances.delete(id); }
      config.onCommit = (edits) => ref.invokeMethodAsync('OnCommit', edits);
      const g = new G(el, config);
      instances.set(id, g);
      return true;
    },
    update(id, columns, rows) {
      const g = instances.get(id);
      if (g) g.setData(columns, rows, false);
    },
    dispose(id) {
      const g = instances.get(id);
      if (g) { g.destroy(); instances.delete(id); }
    },
    get(id) { return instances.get(id); },
  };
  window.eosGridCreate = (id, el, ref, json) => window.EosGridHost.create(id, el, ref, JSON.parse(json));
  window.eosGridUpdate = (id, cols, rows) => window.EosGridHost.update(id, JSON.parse(cols), JSON.parse(rows));
  window.eosGridDispose = (id) => window.EosGridHost.dispose(id);
})();
