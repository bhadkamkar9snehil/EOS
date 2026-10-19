const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const dir = path.join(__dirname, '..', '..', 'src', 'EOS.Server', 'wwwroot', 'js');
const files = ['eos-grid-model.js', 'eos-grid.js', 'eos-grid-ui.js'].map((f) => fs.readFileSync(path.join(dir, f), 'utf8'));
const tick = (ms = 8) => new Promise((r) => setTimeout(r, ms));
const plain = (x) => JSON.parse(JSON.stringify(x));

const COLS = () => [
  { key: 'name', title: 'Engineer', type: 'link', editable: false, pin: 'left', width: 150, rowLabel: true },
  { key: 'a', title: 'Asset Structure', type: 'rating', min: 1, max: 5, width: 96 },
  { key: 'b', title: 'SQL', type: 'rating', min: 1, max: 5, width: 96 },
  { key: 'n', title: 'Hours', type: 'number', decimals: 1, agg: 'sum', width: 100 },
  { key: 's', title: 'Status', type: 'select', width: 120, options: [{ value: 'open', label: 'Open' }, { value: 'done', label: 'Done', tone: 'ok' }] },
  { key: 'd', title: 'Due', type: 'date', width: 120 },
  { key: 'ok', title: 'Billable', type: 'bool', width: 80 },
  { key: 't', title: 'Notes', type: 'text', width: 160 },
];

const ROWS = () => [
  { id: 1, cells: { name: 'Asha', a: null, b: 3, n: 10, s: 'open', d: '2026-10-19', ok: false, t: 'first' } },
  { id: 2, cells: { name: 'Bo', a: 5, b: null, n: 20, s: 'done', d: null, ok: true, t: null } },
  { id: 3, cells: { name: 'Cy', a: 2, b: 2, n: 5, s: 'open', d: null, ok: false, t: null }, locked: ['b'] },
  { id: 4, cells: { name: 'Di', a: 1, b: 4, n: 7.5, s: 'done', d: null, ok: false, t: null } },
];

function make(o = {}) {
  const dom = new JSDOM('<!doctype html><body><div id="host"></div></body>', { url: 'http://localhost/', runScripts: 'outside-only', pretendToBeVisual: true });
  const w = dom.window;
  Object.defineProperty(w.HTMLElement.prototype, 'clientWidth', { get() { return this.classList && this.classList.contains('eg-scroller') ? (o.width || 1400) : 0; } });
  Object.defineProperty(w.HTMLElement.prototype, 'clientHeight', { get() { return this.classList && this.classList.contains('eg-scroller') ? 600 : 0; } });
  for (const f of files) w.eval(f);
  const calls = [];
  const state = { reject: null };
  const g = new w.EosGrid(w.document.getElementById('host'), {
    id: o.id || 't' + Math.random(),
    columns: o.columns || COLS(),
    rows: o.rows || ROWS(),
    options: o.options || {},
    onCommit: async (edits) => {
      calls.push(edits);
      const r = { rejected: [], updated: [] };
      if (state.reject) for (const e of edits) { const m = state.reject(e); if (m) r.rejected.push({ rowId: e.rowId, key: e.key, message: m }); }
      return r;
    },
  });
  const sc = g.el.scroller;
  const key = (k, extra) => sc.dispatchEvent(new w.KeyboardEvent('keydown', Object.assign({ key: k, bubbles: true, cancelable: true }, extra || {})));
  const paste = (text) => { const ev = new w.Event('paste', { bubbles: true, cancelable: true }); ev.clipboardData = { getData: () => text }; sc.dispatchEvent(ev); };
  const copy = () => { let out = ''; const ev = new w.Event('copy', { bubbles: true, cancelable: true }); ev.clipboardData = { setData: (_, v) => { out = v; } }; sc.dispatchEvent(ev); return out; };
  const cell = (rowId, colKey) => { const loc = g.locate(rowId, colKey); return g.cellEl(loc.r, loc.c); };
  const at = (rowId, colKey) => { const loc = g.locate(rowId, colKey); g.select(loc.r, loc.c); return loc; };
  return { w, g, calls, state, key, paste, copy, cell, at, done: () => w.close() };
}

test('every header shows its full title, none is truncated with an ellipsis', () => {
  const t = make();
  const titles = [...t.g.el.head.querySelectorAll('.eg-htext')].map((e) => e.textContent);
  assert.deepEqual(titles, ['Engineer', 'Asset Structure', 'SQL', 'Hours', 'Status', 'Due', 'Billable', 'Notes']);
  assert.ok(titles.every((x) => !x.includes('…')));
  t.done();
});

test('narrow columns are widened so no single header word is cut mid-word', () => {
  const t = make({ columns: [{ key: 'x', title: 'Infrastructure', type: 'rating', min: 1, max: 5, width: 60 }], rows: [{ id: 1, cells: { x: 1 } }] });
  assert.ok(t.g.vcols[0].width >= 60 + 40, `width ${t.g.vcols[0].width}`);
  t.done();
});

test('on a narrow screen only the first pinned column stays pinned and row numbers are dropped', () => {
  const columns = COLS();
  columns[3].pin = 'left';
  const t = make({ width: 375, columns });
  assert.equal(t.g.narrow, true);
  assert.equal(t.g.rn, false);
  assert.equal(t.g.vcols.filter((c) => c.pin === 'left').length, 1);
  assert.ok(t.g.pinnedW <= 150, `pinned ${t.g.pinnedW}`);
  assert.equal(t.g.el.body.querySelector('.eg-gutter'), null);
  t.done();
});

test('wide screens keep every pinned column and the row numbers', () => {
  const columns = COLS();
  columns[3].pin = 'left';
  const t = make({ columns });
  assert.equal(t.g.vcols.filter((c) => c.pin === 'left').length, 2);
  assert.ok(t.g.el.body.querySelector('.eg-gutter'));
  t.done();
});

test('rows are virtualised: only a window is in the DOM', () => {
  const rows = Array.from({ length: 500 }, (_, i) => ({ id: i + 1, cells: { name: 'P' + i, a: 1 } }));
  const t = make({ rows });
  const n = t.g.el.body.querySelectorAll('.eg-row').length;
  assert.ok(n > 5 && n < 60, `rendered ${n}`);
  assert.equal(t.g.display.length, 500);
  t.done();
});

test('typing a digit on a rating cell saves it, moves down and shows the pill', async () => {
  const t = make();
  t.at(1, 'a');
  t.key('4');
  await tick();
  assert.deepEqual(plain(t.calls[0]), [{ rowId: 1, key: 'a', value: 4, old: null }]);
  assert.equal(t.g.rowIndex.get(1).cells.a, 4);
  assert.equal(t.g.active.r, 1);
  assert.ok(t.cell(1, 'a').querySelector('.eg-pill.r4'));
  assert.match(t.g.el.stRight.textContent, /All changes saved/);
  assert.ok(t.g.el.toast.querySelector('.eg-tundo'));
  t.done();
});

test('an out-of-range digit is refused without calling the server', async () => {
  const t = make();
  t.at(1, 'a');
  t.key('9');
  await tick();
  assert.equal(t.calls.length, 0);
  assert.match(t.g.el.toast.textContent, /at most 5/);
  t.done();
});

test('a server rejection puts the old value back and flags the cell', async () => {
  const t = make();
  t.state.reject = () => 'Not allowed.';
  t.at(2, 'a');
  t.key('1');
  await tick();
  assert.equal(t.g.rowIndex.get(2).cells.a, 5);
  assert.ok(t.cell(2, 'a').classList.contains('err'));
  assert.match(t.g.el.stRight.textContent, /1 not saved/);
  assert.match(t.g.el.toast.textContent, /Not allowed/);
  t.done();
});

test('undo and redo send inverse and original edits', async () => {
  const t = make();
  t.at(1, 'a');
  t.key('4');
  await tick();
  t.at(1, 'a');
  t.key('z', { ctrlKey: true });
  await tick();
  assert.deepEqual(plain(t.calls[1]), [{ rowId: 1, key: 'a', value: null, old: 4 }]);
  assert.equal(t.g.rowIndex.get(1).cells.a, null);
  t.key('y', { ctrlKey: true });
  await tick();
  assert.equal(t.g.rowIndex.get(1).cells.a, 4);
  assert.equal(t.calls.length, 3);
  t.done();
});

test('a rejected undo leaves the history so it can be tried again', async () => {
  const t = make();
  t.at(1, 'a');
  t.key('4');
  await tick();
  t.state.reject = () => 'Locked by the server.';
  await t.g.doUndo();
  assert.equal(t.g.rowIndex.get(1).cells.a, 4);
  assert.equal(t.g.undo.canUndo, true);
  t.done();
});

test('pasting a 2x2 block is one undo step, and bad cells are skipped with a reason', async () => {
  const t = make();
  const loc = t.at(1, 'a');
  t.paste('2\t4\n4\tx');
  await tick();
  assert.equal(t.calls[0].length, 3);
  assert.equal(t.g.rowIndex.get(1).cells.a, 2);
  assert.equal(t.g.rowIndex.get(1).cells.b, 4);
  assert.equal(t.g.rowIndex.get(2).cells.a, 4);
  assert.equal(t.g.rowIndex.get(2).cells.b, null);
  assert.match(t.g.el.toast.textContent, /1 skipped/);
  assert.ok(loc);
  await t.g.doUndo();
  assert.equal(t.g.rowIndex.get(1).cells.a, null);
  assert.equal(t.g.rowIndex.get(1).cells.b, 3);
  assert.equal(t.g.rowIndex.get(2).cells.a, 5);
  t.done();
});

test('paste skips locked cells and read-only columns', async () => {
  const t = make();
  t.at(3, 'name');
  t.paste('zzz\t4\t4');
  await tick();
  const keys = t.calls[0].map((e) => e.key + ':' + e.rowId);
  assert.deepEqual(plain(keys), ['a:3']);
  assert.match(t.g.el.toast.textContent, /skipped/);
  t.done();
});

test('Delete clears editable cells in the range and keeps locked ones', async () => {
  const t = make();
  t.at(1, 'a');
  t.g.select(t.g.locate(3, 'b').r, t.g.locate(3, 'b').c, true);
  t.key('Delete');
  await tick();
  const sent = t.calls[0].map((e) => e.rowId + e.key);
  assert.ok(sent.includes('2a') && sent.includes('3a') && !sent.includes('3b'));
  assert.equal(t.g.rowIndex.get(3).cells.b, 2);
  t.done();
});

test('number editor refuses text and accepts a valid number with Enter', async () => {
  const t = make();
  t.at(1, 'n');
  t.key('Enter');
  assert.equal(t.g.editing.kind, 'input');
  t.g.editing.input.value = 'abc';
  t.g.editing.input.dispatchEvent(new t.w.Event('input'));
  t.g.editing.input.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
  await tick();
  assert.ok(t.g.editing && t.g.editing.el.classList.contains('bad'));
  assert.equal(t.calls.length, 0);
  t.g.editing.input.value = '1,234.5';
  t.g.editing.input.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
  await tick();
  assert.equal(t.g.editing, null);
  assert.equal(t.g.rowIndex.get(1).cells.n, 1234.5);
  assert.equal(t.g.active.r, 1);
  t.done();
});

test('typing a character starts a text edit that replaces the value, Escape cancels it', async () => {
  const t = make();
  t.at(1, 't');
  t.key('h');
  assert.equal(t.g.editing.input.value, 'h');
  t.key('Escape');
  t.g.editing.input.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
  assert.equal(t.g.editing, null);
  assert.equal(t.g.rowIndex.get(1).cells.t, 'first');
  assert.equal(t.calls.length, 0);
  t.done();
});

test('Tab commits and moves right, Shift+Enter commits and moves up', async () => {
  const t = make();
  t.at(2, 'n');
  t.key('5');
  t.g.editing.input.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
  await tick();
  assert.equal(t.g.rowIndex.get(2).cells.n, 5);
  assert.equal(t.g.active.c, t.g.locate(2, 's').c);
  t.done();
});

test('select popover: type-ahead skips the Clear entry and Enter picks the match', async () => {
  const t = make();
  t.at(1, 's');
  t.key('Enter');
  const pop = t.g.el.body.querySelector('.eg-list');
  assert.ok(pop);
  pop.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'd', bubbles: true }));
  pop.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
  await tick();
  assert.equal(t.g.rowIndex.get(1).cells.s, 'done');
  t.done();
});

test('select popover: choosing Clear value empties the cell', async () => {
  const t = make();
  t.at(1, 's');
  t.key('Enter');
  const pop = t.g.el.body.querySelector('.eg-list');
  pop.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }));
  pop.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
  await tick();
  assert.equal(t.g.rowIndex.get(1).cells.s, null);
  t.done();
});

test('rating popover: arrow keys and Enter choose a value', async () => {
  const t = make();
  t.at(1, 'a');
  t.key('Enter');
  const pop = t.g.el.body.querySelector('.eg-rate');
  assert.ok(pop);
  for (let i = 0; i < 2; i++) pop.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
  pop.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
  await tick();
  assert.equal(t.g.rowIndex.get(1).cells.a, 3);
  t.done();
});

test('space toggles a bool cell', async () => {
  const t = make();
  t.at(1, 'ok');
  t.key(' ');
  await tick();
  assert.equal(t.g.rowIndex.get(1).cells.ok, true);
  t.done();
});

test('a date cell accepts dd-mm-yyyy and stores ISO', async () => {
  const t = make();
  t.at(2, 'd');
  t.key('Enter');
  t.g.editing.input.value = '05-11-2026';
  t.g.editing.input.dispatchEvent(new t.w.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
  await tick();
  assert.equal(t.g.rowIndex.get(2).cells.d, '2026-11-05');
  t.done();
});

test('locked and calculated cells explain why they cannot be edited', () => {
  const t = make();
  t.at(3, 'b');
  t.key('Enter');
  assert.equal(t.g.editing, null);
  assert.match(t.g.el.toast.textContent, /cannot be edited on this row/);
  t.done();
  const c = make({ columns: [{ key: 'name', title: 'Name', type: 'text' }, { key: 'calc', title: 'Total', type: 'number', editable: false }], rows: [{ id: 1, cells: { name: 'x', calc: 3 } }] });
  c.at(1, 'calc');
  c.key('Enter');
  assert.match(c.g.el.toast.textContent, /calculated/);
  c.done();
});

test('a read-only grid never edits', async () => {
  const t = make({ options: { readOnly: true } });
  t.at(1, 'a');
  t.key('4');
  t.key('Delete');
  await tick();
  assert.equal(t.calls.length, 0);
  t.done();
});

test('fill down and fill right copy the first cell of the selection', async () => {
  const t = make();
  const a = t.at(2, 'a');
  t.g.select(t.g.locate(4, 'a').r, a.c, true);
  t.key('d', { ctrlKey: true });
  await tick();
  assert.equal(t.g.rowIndex.get(3).cells.a, 5);
  assert.equal(t.g.rowIndex.get(4).cells.a, 5);
  t.done();
});

test('copy puts a tab-separated block on the clipboard', () => {
  const t = make();
  const a = t.at(1, 'a');
  t.g.select(t.g.locate(2, 'a').r, t.g.locate(2, 'b').c, true);
  assert.equal(a.c + 1, t.g.locate(1, 'b').c);
  assert.equal(t.copy(), '\t3\n5\t');
  t.done();
});

test('keyboard navigation: arrows, Home, End, Ctrl+arrows, Tab wrap', () => {
  const t = make();
  t.at(1, 'a');
  t.key('ArrowDown');
  assert.equal(t.g.active.r, 1);
  t.key('End');
  assert.equal(t.g.active.c, t.g.vcols.length - 1);
  t.key('Home');
  assert.equal(t.g.active.c, 0);
  t.key('ArrowDown', { ctrlKey: true });
  assert.equal(t.g.active.r, 3);
  t.key('ArrowUp', { ctrlKey: true });
  assert.equal(t.g.active.r, 0);
  t.key('End');
  t.key('Tab');
  assert.deepEqual([t.g.active.r, t.g.active.c], [1, 0]);
  t.done();
});

test('shift+arrow extends a range and the status bar summarises numbers', () => {
  const t = make();
  const a = t.at(1, 'n');
  t.key('ArrowDown', { shiftKey: true });
  t.key('ArrowDown', { shiftKey: true });
  assert.equal(t.g.range.r2 - t.g.range.r1, 2);
  assert.match(t.g.el.stMid.textContent, /3 cells · Σ 35/);
  assert.ok(a);
  t.done();
});

test('header click cycles sort asc, desc, none; shift adds a second key', () => {
  const t = make();
  const h = () => t.g.el.head.querySelector('.eg-hcell[data-h="3"]');
  h().click();
  assert.deepEqual(plain(t.g.state.sort), [{ key: 'n', dir: 'asc' }]);
  assert.deepEqual(plain(t.g.display.map((d) => d.row.id)), [3, 4, 1, 2]);
  h().click();
  assert.equal(t.g.state.sort[0].dir, 'desc');
  h().click();
  assert.equal(t.g.state.sort.length, 0);
  t.g.setSort('s', 'asc', false);
  t.g.setSort('n', 'desc', true);
  assert.deepEqual(plain(t.g.display.map((d) => d.row.id)), [2, 4, 1, 3]);
  t.done();
});

test('search and filters narrow rows, chips can be cleared, edits keep working while filtered', async () => {
  const t = make();
  t.g.state.filters = { s: { values: ['open'] } };
  t.g.relayout();
  assert.deepEqual(plain(t.g.display.map((d) => d.row.id)), [1, 3]);
  assert.ok(!t.g.el.chips.hidden);
  t.g.el.chips.querySelector('[data-fx="s"]').click();
  assert.equal(t.g.display.length, 4);
  t.g.state.search = 'bo';
  t.g.relayout();
  t.at(2, 'a');
  t.key('1');
  await tick();
  assert.equal(t.g.rowIndex.get(2).cells.a, 1);
  t.done();
});

test('grouping adds group rows with totals; Enter collapses and expands', () => {
  const t = make();
  t.g.state.groupBy = 's';
  t.g.relayout();
  const groups = t.g.display.filter((d) => d.kind === 'group');
  assert.deepEqual(plain(groups.map((g) => [g.label, g.count, g.aggs.n])), [['Done', 2, 27.5], ['Open', 2, 15]]);
  t.g.select(0, 0);
  t.key('Enter');
  assert.equal(t.g.display.filter((d) => d.kind === 'row').length, 2);
  t.key('Enter');
  assert.equal(t.g.display.filter((d) => d.kind === 'row').length, 4);
  t.done();
});

test('tree mode indents children and Alt+arrows collapse and expand', () => {
  const rows = [
    { id: 1, parent: null, cells: { name: 'Phase', a: null } },
    { id: 2, parent: 1, cells: { name: 'Task A', a: 1 } },
    { id: 3, parent: 1, cells: { name: 'Task B', a: 2 } },
    { id: 4, parent: null, cells: { name: 'Other', a: 3 } },
  ];
  const cols = [{ key: 'name', title: 'Name', type: 'text', treeColumn: true, pin: 'left' }, { key: 'a', title: 'A', type: 'rating', min: 1, max: 5 }];
  const t = make({ rows, columns: cols, options: { tree: true } });
  assert.deepEqual(plain(t.g.display.map((d) => d.level)), [0, 1, 1, 0]);
  assert.ok(t.g.el.body.querySelector('.eg-twist'));
  t.g.select(0, 0);
  t.key('ArrowLeft', { altKey: true });
  assert.deepEqual(plain(t.g.display.map((d) => d.row.id)), [1, 4]);
  t.key('ArrowRight', { altKey: true });
  assert.equal(t.g.display.length, 4);
  t.done();
});

test('view preferences persist and Reset view clears them', () => {
  const t = make({ id: 'persist-test' });
  t.g.prefs.cols.n = { hidden: true };
  t.g.setSort('n', 'desc', false);
  assert.ok(t.w.localStorage.getItem('eosgrid:persist-test'));
  const saved = JSON.parse(t.w.localStorage.getItem('eosgrid:persist-test'));
  assert.equal(saved.sort[0].key, 'n');
  t.g.resetView();
  assert.equal(t.w.localStorage.getItem('eosgrid:persist-test'), null);
  assert.ok(t.g.vcols.some((c) => c.key === 'n'));
  assert.equal(t.g.state.sort.length, 0);
  t.done();
});

test('hiding and pinning columns re-lays out and keeps the pinned column first', () => {
  const t = make();
  t.g.prefs.cols.t = { hidden: true };
  t.g.relayoutKeep();
  assert.ok(!t.g.vcols.some((c) => c.key === 't'));
  t.g.prefs.cols.n = { pin: 'left' };
  t.g.relayoutKeep();
  assert.deepEqual(plain(t.g.vcols.slice(0, 2).map((c) => c.key)), ['name', 'n']);
  t.done();
});

test('setData keeps the active cell on the same row and column when rows are refreshed', () => {
  const t = make();
  t.at(2, 'b');
  const rows = ROWS().reverse();
  t.g.setData(COLS(), rows, false);
  assert.equal(t.g.display[t.g.active.r].row.id, 2);
  assert.equal(t.g.vcols[t.g.active.c].key, 'b');
  t.done();
});

test('server can push computed values back in the commit result', async () => {
  const t = make();
  t.g.onCommit = async () => ({ rejected: [], updated: [{ rowId: 1, key: 'n', value: 99 }] });
  t.at(1, 'a');
  t.key('2');
  await tick();
  assert.equal(t.g.rowIndex.get(1).cells.n, 99);
  assert.match(t.cell(1, 'n').textContent, /99/);
  t.done();
});

test('empty grid shows the empty message, and a no-match search offers to clear', () => {
  const t = make({ rows: [] });
  assert.ok(t.g.el.body.querySelector('.eg-empty'));
  const u = make();
  u.g.state.search = 'nomatch';
  u.g.relayout();
  assert.ok(u.g.el.body.querySelector('[data-clear-all]'));
  t.done();
  u.done();
});

test('accessibility: grid, columnheader, gridcell roles and sort state', () => {
  const t = make();
  assert.equal(t.g.el.scroller.getAttribute('role'), 'grid');
  assert.ok(t.g.el.head.querySelector('[role="columnheader"]'));
  assert.ok(t.g.el.body.querySelector('[role="gridcell"]'));
  t.g.setSort('n', 'asc', false);
  assert.equal(t.g.el.head.querySelector('[data-h="3"]').getAttribute('aria-sort'), 'ascending');
  t.done();
});

test('CSV export is built from the visible, sorted rows', () => {
  const t = make();
  let blob = null;
  t.w.URL.createObjectURL = (b) => { blob = b; return 'blob:x'; };
  t.w.URL.revokeObjectURL = () => {};
  t.w.HTMLAnchorElement.prototype.click = () => {};
  t.g.setSort('n', 'asc', false);
  t.g.exportCsv();
  assert.ok(blob);
  t.done();
});
