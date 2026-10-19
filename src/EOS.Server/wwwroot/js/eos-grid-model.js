(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.EosGridModel = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  const MONTHS = { jan: 0, feb: 1, mar: 2, apr: 3, may: 4, jun: 5, jul: 6, aug: 7, sep: 8, oct: 9, nov: 10, dec: 11 };
  const isBlank = (v) => v === null || v === undefined || v === '';

  function pad(n) { return String(n).padStart(2, '0'); }

  function isoDate(y, m, d) {
    const dt = new Date(Date.UTC(y, m, d));
    if (dt.getUTCFullYear() !== y || dt.getUTCMonth() !== m || dt.getUTCDate() !== d) return null;
    return `${y}-${pad(m + 1)}-${pad(d)}`;
  }

  function parseDate(text) {
    const s = String(text).trim();
    let m;
    if ((m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(s))) return isoDate(+m[1], +m[2] - 1, +m[3]);
    if ((m = /^(\d{1,2})[-/.](\d{1,2})[-/.](\d{4})$/.exec(s))) return isoDate(+m[3], +m[2] - 1, +m[1]);
    if ((m = /^(\d{1,2})\s+([A-Za-z]{3})[a-z]*\s+(\d{4})$/.exec(s))) {
      const mo = MONTHS[m[2].toLowerCase()];
      return mo === undefined ? null : isoDate(+m[3], mo, +m[1]);
    }
    return null;
  }

  function formatDate(iso) {
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(iso));
    if (!m) return String(iso);
    const names = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    return `${+m[3]} ${names[+m[2] - 1]} ${m[1]}`;
  }

  function parseNumber(text, col) {
    if (typeof text === 'number') return Number.isFinite(text) ? { ok: true, value: text } : { ok: false, error: 'Not a number.' };
    let s = String(text).trim().replace(/[\s ]/g, '');
    if (s === '') return { ok: true, value: null };
    let pct = false;
    if (s.endsWith('%')) { pct = true; s = s.slice(0, -1); }
    if (/^\(.*\)$/.test(s)) s = '-' + s.slice(1, -1);
    s = s.replace(/,/g, '');
    if (!/^[-+]?(\d+\.?\d*|\.\d+)(e[-+]?\d+)?$/i.test(s)) return { ok: false, error: `"${text}" is not a number.` };
    let v = Number(s);
    if (pct && col && col.percent) v = v / 100;
    return { ok: true, value: v };
  }

  function coerce(col, input) {
    const type = col.type || 'text';
    const nullable = col.nullable !== false;
    if (type === 'text' || type === 'readonly' || type === 'link') {
      const v = input === null || input === undefined ? '' : String(input);
      if (v === '' && !nullable) return { ok: false, error: `${col.title || col.key} cannot be empty.` };
      if (col.maxLength && v.length > col.maxLength) return { ok: false, error: `Maximum ${col.maxLength} characters.` };
      return { ok: true, value: v === '' ? null : v };
    }
    if (type === 'number' || type === 'int' || type === 'rating') {
      const r = parseNumber(input, col);
      if (!r.ok) return r;
      if (r.value === null) return nullable ? r : { ok: false, error: `${col.title || col.key} cannot be empty.` };
      if (type !== 'number' && !Number.isInteger(r.value)) return { ok: false, error: `${r.value} is not a whole number.` };
      const min = col.min, max = col.max;
      if (min !== undefined && min !== null && r.value < min) return { ok: false, error: `Must be at least ${min}.` };
      if (max !== undefined && max !== null && r.value > max) return { ok: false, error: `Must be at most ${max}.` };
      return r;
    }
    if (type === 'select') {
      if (isBlank(input)) return nullable ? { ok: true, value: null } : { ok: false, error: `Choose a value for ${col.title || col.key}.` };
      const s = String(input).trim().toLowerCase();
      const opt = (col.options || []).find((o) => String(o.value).toLowerCase() === s || String(o.label).toLowerCase() === s);
      return opt ? { ok: true, value: opt.value } : { ok: false, error: `"${input}" is not one of the allowed values.` };
    }
    if (type === 'date') {
      if (isBlank(input)) return nullable ? { ok: true, value: null } : { ok: false, error: 'A date is required.' };
      const iso = parseDate(input);
      return iso ? { ok: true, value: iso } : { ok: false, error: `"${input}" is not a valid date.` };
    }
    if (type === 'bool') {
      if (typeof input === 'boolean') return { ok: true, value: input };
      const s = String(input).trim().toLowerCase();
      if (['true', 'yes', 'y', '1', 'x', 'on'].includes(s)) return { ok: true, value: true };
      if (['false', 'no', 'n', '0', '', 'off'].includes(s)) return { ok: true, value: false };
      return { ok: false, error: `"${input}" is not yes or no.` };
    }
    return { ok: true, value: input };
  }

  function formatValue(col, v) {
    if (isBlank(v)) return '';
    const type = col.type || 'text';
    if (type === 'number' || type === 'int') {
      const dec = col.decimals !== undefined ? col.decimals : type === 'int' ? 0 : undefined;
      const val = col.percent ? v * 100 : v;
      const body = dec === undefined
        ? new Intl.NumberFormat('en-US', { maximumFractionDigits: 6 }).format(val)
        : new Intl.NumberFormat('en-US', { minimumFractionDigits: dec, maximumFractionDigits: dec }).format(val);
      return (col.prefix || '') + body + (col.percent ? '%' : '') + (col.suffix || '');
    }
    if (type === 'date') return formatDate(v);
    if (type === 'bool') return v ? 'Yes' : 'No';
    if (type === 'select') {
      const opt = (col.options || []).find((o) => o.value === v);
      return opt ? opt.label : String(v);
    }
    return String(v);
  }

  function rawText(col, v) {
    if (isBlank(v)) return '';
    if (col.type === 'select') return formatValue(col, v);
    if (col.type === 'bool') return v ? 'Yes' : 'No';
    if (col.type === 'date') return String(v);
    if (col.type === 'number' && col.percent) return String(Math.round(v * 1e8) / 1e6) + '%';
    return String(v);
  }

  function compareValues(col, a, b) {
    const an = isBlank(a), bn = isBlank(b);
    if (an || bn) return an && bn ? 0 : an ? 1 : -1;
    const type = col.type || 'text';
    if (type === 'number' || type === 'int' || type === 'rating') return a - b;
    if (type === 'bool') return (a ? 1 : 0) - (b ? 1 : 0);
    if (type === 'select') return formatValue(col, a).localeCompare(formatValue(col, b), undefined, { sensitivity: 'base', numeric: true });
    return String(a).localeCompare(String(b), undefined, { sensitivity: 'base', numeric: true });
  }

  function matchesFilter(col, v, f) {
    if (!f) return true;
    if (f.blank === true && !isBlank(v)) return false;
    if (f.blank === false && isBlank(v)) return false;
    if (f.text) {
      const hay = formatValue(col, v).toLowerCase() + ' ' + String(isBlank(v) ? '' : v).toLowerCase();
      if (!hay.includes(f.text.toLowerCase())) return false;
    }
    if (f.values && f.values.length) {
      const key = isBlank(v) ? '' : String(v);
      if (!f.values.includes(key)) return false;
    }
    if (f.min !== undefined && f.min !== null && f.min !== '') {
      if (isBlank(v) || !(Number(v) >= Number(f.min))) return false;
    }
    if (f.max !== undefined && f.max !== null && f.max !== '') {
      if (isBlank(v) || !(Number(v) <= Number(f.max))) return false;
    }
    return true;
  }

  function isActiveFilter(f) {
    if (!f) return false;
    return !!(f.text || (f.values && f.values.length) || f.blank === true || f.blank === false ||
      (f.min !== undefined && f.min !== null && f.min !== '') || (f.max !== undefined && f.max !== null && f.max !== ''));
  }

  function aggregate(col, values) {
    const nums = values.filter((v) => typeof v === 'number' && Number.isFinite(v));
    switch (col.agg) {
      case 'sum': return nums.length ? nums.reduce((a, b) => a + b, 0) : null;
      case 'avg': return nums.length ? nums.reduce((a, b) => a + b, 0) / nums.length : null;
      case 'min': return nums.length ? Math.min(...nums) : null;
      case 'max': return nums.length ? Math.max(...nums) : null;
      case 'count': return values.filter((v) => !isBlank(v)).length;
      default: return null;
    }
  }

  function rowSearchText(cols, row) {
    return cols.map((c) => formatValue(c, row.cells[c.key])).join(' ').toLowerCase();
  }

  function buildDisplay(rows, cols, state) {
    const colByKey = new Map(cols.map((c) => [c.key, c]));
    const filters = Object.entries(state.filters || {}).filter(([k, f]) => colByKey.has(k) && isActiveFilter(f));
    const q = (state.search || '').trim().toLowerCase();
    const searchCols = cols.filter((c) => !c.hidden || state.searchHidden);
    const sorts = (state.sort || []).filter((s) => colByKey.has(s.key));
    const passes = (row) => {
      for (const [k, f] of filters) if (!matchesFilter(colByKey.get(k), row.cells[k], f)) return false;
      if (q && !rowSearchText(searchCols, row).includes(q)) return false;
      return true;
    };
    const cmp = (a, b) => {
      for (const s of sorts) {
        const col = colByKey.get(s.key);
        const av = a.cells[s.key], bv = b.cells[s.key];
        const ab = isBlank(av), bb = isBlank(bv);
        if (ab !== bb) return ab ? 1 : -1;
        const c = compareValues(col, av, bv);
        if (c !== 0) return s.dir === 'desc' ? -c : c;
      }
      return a.__order - b.__order;
    };
    const ordered = rows.map((r, i) => (r.__order = i, r));
    const collapsed = state.collapsed || new Set();
    const out = [];

    if (state.tree) {
      const children = new Map();
      for (const r of ordered) {
        const p = r.parent === undefined || r.parent === null ? null : r.parent;
        if (!children.has(p)) children.set(p, []);
        children.get(p).push(r);
      }
      const keep = new Set();
      const mark = (r) => {
        const kids = children.get(r.id) || [];
        let any = passes(r);
        for (const k of kids) if (mark(k)) any = true;
        if (any) keep.add(r.id);
        return any;
      };
      for (const r of children.get(null) || []) mark(r);
      const walk = (parent, level) => {
        const kids = (children.get(parent) || []).filter((r) => keep.has(r.id));
        if (sorts.length) kids.sort(cmp);
        for (const r of kids) {
          const hasKids = (children.get(r.id) || []).some((k) => keep.has(k.id));
          out.push({ kind: 'row', row: r, level, hasChildren: hasKids, collapsed: collapsed.has(r.id) });
          if (hasKids && !collapsed.has(r.id)) walk(r.id, level + 1);
        }
      };
      walk(null, 0);
      return out;
    }

    const visible = ordered.filter(passes);
    if (sorts.length) visible.sort(cmp);
    const groupCol = state.groupBy ? colByKey.get(state.groupBy) : null;
    if (!groupCol) {
      for (const r of visible) out.push({ kind: 'row', row: r, level: 0 });
      return out;
    }
    const groups = new Map();
    for (const r of visible) {
      const key = isBlank(r.cells[groupCol.key]) ? '' : String(r.cells[groupCol.key]);
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(r);
    }
    const keys = [...groups.keys()].sort((a, b) => {
      if (a === '' || b === '') return a === '' ? 1 : -1;
      return compareValues(groupCol, a, b);
    });
    for (const key of keys) {
      const members = groups.get(key);
      const gid = 'g:' + key;
      const aggs = {};
      for (const c of cols) if (c.agg) aggs[c.key] = aggregate(c, members.map((r) => r.cells[c.key]));
      const label = key === '' ? '(blank)' : formatValue(groupCol, groupCol.type === 'number' || groupCol.type === 'int' ? Number(key) : key);
      out.push({ kind: 'group', id: gid, label, count: members.length, aggs, level: 0, collapsed: collapsed.has(gid) });
      if (!collapsed.has(gid)) for (const r of members) out.push({ kind: 'row', row: r, level: 1 });
    }
    return out;
  }

  function parseTSV(text) {
    const rows = [];
    let row = [], cell = '', i = 0, inQ = false;
    const s = String(text).replace(/\r\n/g, '\n').replace(/\r/g, '\n');
    while (i < s.length) {
      const ch = s[i];
      if (inQ) {
        if (ch === '"') {
          if (s[i + 1] === '"') { cell += '"'; i += 2; continue; }
          inQ = false; i++; continue;
        }
        cell += ch; i++; continue;
      }
      if (ch === '"' && cell === '') { inQ = true; i++; continue; }
      if (ch === '\t') { row.push(cell); cell = ''; i++; continue; }
      if (ch === '\n') { row.push(cell); rows.push(row); row = []; cell = ''; i++; continue; }
      cell += ch; i++;
    }
    if (cell !== '' || row.length) { row.push(cell); rows.push(row); }
    return rows;
  }

  function toTSV(matrix) {
    return matrix.map((r) => r.map((v) => (/[\t\n"]/.test(v) ? '"' + v.replace(/"/g, '""') + '"' : v)).join('\t')).join('\n');
  }

  function toCSV(matrix) {
    return matrix.map((r) => r.map((v) => (/[,\n"]/.test(v) ? '"' + v.replace(/"/g, '""') + '"' : v)).join(',')).join('\r\n');
  }

  function normalizeRange(a, b) {
    return { r1: Math.min(a.r, b.r), r2: Math.max(a.r, b.r), c1: Math.min(a.c, b.c), c2: Math.max(a.c, b.c) };
  }

  function rangeContains(range, r, c) {
    return !!range && r >= range.r1 && r <= range.r2 && c >= range.c1 && c <= range.c2;
  }

  function planPaste(anchor, matrix, range, rowCount, colCount) {
    const plan = [];
    if (!matrix.length || !matrix[0].length) return plan;
    const single = matrix.length === 1 && matrix[0].length === 1;
    if (single && range && (range.r2 > range.r1 || range.c2 > range.c1)) {
      for (let r = range.r1; r <= range.r2; r++) for (let c = range.c1; c <= range.c2; c++) plan.push({ r, c, text: matrix[0][0] });
      return plan;
    }
    for (let i = 0; i < matrix.length; i++) {
      for (let j = 0; j < matrix[i].length; j++) {
        const r = anchor.r + i, c = anchor.c + j;
        if (r < rowCount && c < colCount) plan.push({ r, c, text: matrix[i][j] });
      }
    }
    return plan;
  }

  function planFill(range, direction) {
    const plan = [];
    if (direction === 'down') {
      for (let c = range.c1; c <= range.c2; c++) for (let r = range.r1 + 1; r <= range.r2; r++) plan.push({ r, c, fromR: range.r1, fromC: c });
    } else {
      for (let r = range.r1; r <= range.r2; r++) for (let c = range.c1 + 1; c <= range.c2; c++) plan.push({ r, c, fromR: r, fromC: range.c1 });
    }
    return plan;
  }

  function rangeStats(values) {
    const nums = values.filter((v) => typeof v === 'number' && Number.isFinite(v));
    const sum = nums.reduce((a, b) => a + b, 0);
    return {
      cells: values.length,
      filled: values.filter((v) => !isBlank(v)).length,
      count: nums.length,
      sum,
      avg: nums.length ? sum / nums.length : null,
      min: nums.length ? Math.min(...nums) : null,
      max: nums.length ? Math.max(...nums) : null,
    };
  }

  class UndoStack {
    constructor(limit) { this.limit = limit || 200; this.past = []; this.future = []; }
    push(batch) {
      if (!batch || !batch.length) return;
      this.past.push(batch);
      if (this.past.length > this.limit) this.past.shift();
      this.future = [];
    }
    undo() { const b = this.past.pop(); if (b) this.future.push(b); return b || null; }
    redo() { const b = this.future.pop(); if (b) this.past.push(b); return b || null; }
    get canUndo() { return this.past.length > 0; }
    get canRedo() { return this.future.length > 0; }
    clear() { this.past = []; this.future = []; }
  }

  function typeAhead(options, buffer) {
    const s = buffer.toLowerCase();
    if (!s) return -1;
    return options.findIndex((o) => String(o.label).toLowerCase().startsWith(s));
  }

  return {
    isBlank, parseNumber, parseDate, formatDate, coerce, formatValue, rawText, compareValues, matchesFilter, isActiveFilter,
    aggregate, buildDisplay, parseTSV, toTSV, toCSV, normalizeRange, rangeContains, planPaste, planFill, rangeStats, UndoStack, typeAhead,
  };
});
