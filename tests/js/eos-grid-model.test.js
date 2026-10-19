const test = require('node:test');
const assert = require('node:assert/strict');
const M = require('../../src/EOS.Server/wwwroot/js/eos-grid-model.js');

const rating = { key: 'r', title: 'SQL', type: 'rating', min: 1, max: 5 };
const num = { key: 'n', title: 'Hours', type: 'number', decimals: 1, agg: 'sum' };
const sel = { key: 's', title: 'Status', type: 'select', options: [{ value: 'open', label: 'Open' }, { value: 'done', label: 'Done' }] };
const date = { key: 'd', title: 'Due', type: 'date' };

test('rating accepts 1..5 and blank, rejects out of range and decimals', () => {
  assert.deepEqual(M.coerce(rating, '3'), { ok: true, value: 3 });
  assert.deepEqual(M.coerce(rating, ''), { ok: true, value: null });
  assert.equal(M.coerce(rating, '6').ok, false);
  assert.equal(M.coerce(rating, '0').ok, false);
  assert.equal(M.coerce(rating, '2.5').ok, false);
  assert.equal(M.coerce(rating, 'abc').ok, false);
});

test('number parsing handles thousands, brackets, percent and rejects text', () => {
  assert.equal(M.coerce(num, '1,234.5').value, 1234.5);
  assert.equal(M.coerce(num, '(12)').value, -12);
  assert.equal(M.coerce({ key: 'p', type: 'number', percent: true }, '25%').value, 0.25);
  assert.equal(M.coerce(num, '12 abc').ok, false);
  assert.equal(M.coerce({ ...num, nullable: false }, '').ok, false);
});

test('select matches by value or label, case-insensitive', () => {
  assert.equal(M.coerce(sel, 'DONE').value, 'done');
  assert.equal(M.coerce(sel, 'Open').value, 'open');
  assert.equal(M.coerce(sel, 'nope').ok, false);
});

test('dates parse the formats people paste and normalise to ISO', () => {
  assert.equal(M.coerce(date, '19-10-2026').value, '2026-10-19');
  assert.equal(M.coerce(date, '19/10/2026').value, '2026-10-19');
  assert.equal(M.coerce(date, '2026-10-19').value, '2026-10-19');
  assert.equal(M.coerce(date, '19 Oct 2026').value, '2026-10-19');
  assert.equal(M.coerce(date, '31-02-2026').ok, false);
  assert.equal(M.formatDate('2026-10-19'), '19 Oct 2026');
});

test('bool accepts yes/no variants', () => {
  const b = { key: 'b', type: 'bool' };
  assert.equal(M.coerce(b, 'Yes').value, true);
  assert.equal(M.coerce(b, 'no').value, false);
  assert.equal(M.coerce(b, 'maybe').ok, false);
});

const cols = [rating, num, sel, { key: 'name', title: 'Name', type: 'text' }];
const mk = (id, name, r, n, s, parent) => ({ id, parent, cells: { name, r, n, s } });
const rows = () => [
  mk(1, 'Asha', 3, 10, 'open'), mk(2, 'Bo', 5, 20, 'done'), mk(3, 'Cy', null, 5, 'open'), mk(4, 'Di', 1, 7.5, 'done'),
];

test('sort puts blanks last in both directions and is stable', () => {
  const asc = M.buildDisplay(rows(), cols, { sort: [{ key: 'r', dir: 'asc' }] }).map((d) => d.row.id);
  const desc = M.buildDisplay(rows(), cols, { sort: [{ key: 'r', dir: 'desc' }] }).map((d) => d.row.id);
  assert.deepEqual(asc, [4, 1, 2, 3]);
  assert.deepEqual(desc, [2, 1, 4, 3]);
});

test('descending sort reverses values', () => {
  const desc = M.buildDisplay(rows(), cols, { sort: [{ key: 'n', dir: 'desc' }] }).map((d) => d.row.id);
  assert.deepEqual(desc, [2, 1, 4, 3]);
});

test('multi-key sort breaks ties with the second key', () => {
  const d = M.buildDisplay(rows(), cols, { sort: [{ key: 's', dir: 'asc' }, { key: 'n', dir: 'desc' }] }).map((x) => x.row.id);
  assert.deepEqual(d, [2, 4, 1, 3]);
});

test('filters combine: text, values, numeric range, blank', () => {
  const ids = (st) => M.buildDisplay(rows(), cols, st).map((d) => d.row.id);
  assert.deepEqual(ids({ filters: { name: { text: 'a' } } }), [1]);
  assert.deepEqual(ids({ filters: { s: { values: ['open'] } } }), [1, 3]);
  assert.deepEqual(ids({ filters: { n: { min: 7, max: 15 } } }), [1, 4]);
  assert.deepEqual(ids({ filters: { r: { blank: true } } }), [3]);
  assert.deepEqual(ids({ filters: { s: { values: ['open'] }, n: { min: 6 } } }), [1]);
});

test('search matches displayed text across visible columns', () => {
  const ids = M.buildDisplay(rows(), cols, { search: 'done' }).map((d) => d.row.id);
  assert.deepEqual(ids, [2, 4]);
});

test('grouping emits group rows with aggregates and honours collapse', () => {
  const d = M.buildDisplay(rows(), cols, { groupBy: 's' });
  assert.equal(d[0].kind, 'group');
  assert.equal(d[0].label, 'Done');
  assert.equal(d[0].count, 2);
  assert.equal(d[0].aggs.n, 27.5);
  assert.equal(d.filter((x) => x.kind === 'row').length, 4);
  const c = M.buildDisplay(rows(), cols, { groupBy: 's', collapsed: new Set(['g:done']) });
  assert.equal(c.filter((x) => x.kind === 'row').length, 2);
});

test('tree mode nests by parent, keeps ancestors of matches, collapses', () => {
  const t = [mk(1, 'Phase', null, 0, 'open'), mk(2, 'Task A', null, 0, 'open', 1), mk(3, 'Task B', null, 0, 'open', 1), mk(4, 'Other', null, 0, 'open')];
  const all = M.buildDisplay(t, cols, { tree: true });
  assert.deepEqual(all.map((d) => [d.row.id, d.level]), [[1, 0], [2, 1], [3, 1], [4, 0]]);
  assert.equal(all[0].hasChildren, true);
  const hit = M.buildDisplay(t, cols, { tree: true, search: 'task b' });
  assert.deepEqual(hit.map((d) => d.row.id), [1, 3]);
  const col = M.buildDisplay(t, cols, { tree: true, collapsed: new Set([1]) });
  assert.deepEqual(col.map((d) => d.row.id), [1, 4]);
});

test('TSV round trip including quotes, tabs and newlines in cells', () => {
  const m = [['a', 'b\tc', 'd"e'], ['1', 'x\ny', '']];
  assert.deepEqual(M.parseTSV(M.toTSV(m)), m);
  assert.deepEqual(M.parseTSV('1\t2\r\n3\t4\r\n'), [['1', '2'], ['3', '4']]);
});

test('paste plan clips at the grid edge and fills a selected range from one value', () => {
  const p = M.planPaste({ r: 2, c: 1 }, [['a', 'b'], ['c', 'd']], null, 3, 3);
  assert.deepEqual(p.map((x) => [x.r, x.c, x.text]), [[2, 1, 'a'], [2, 2, 'b']]);
  const f = M.planPaste({ r: 0, c: 0 }, [['7']], { r1: 0, r2: 1, c1: 0, c2: 1 }, 5, 5);
  assert.equal(f.length, 4);
  assert.ok(f.every((x) => x.text === '7'));
});

test('fill down and right copy from the first row or column of the range', () => {
  const d = M.planFill({ r1: 1, r2: 3, c1: 0, c2: 1 }, 'down');
  assert.equal(d.length, 4);
  assert.ok(d.every((x) => x.fromR === 1));
  const r = M.planFill({ r1: 0, r2: 1, c1: 2, c2: 4 }, 'right');
  assert.equal(r.length, 4);
  assert.ok(r.every((x) => x.fromC === 2));
});

test('range stats ignore blanks and text', () => {
  const s = M.rangeStats([1, 2, null, 'x', 6]);
  assert.deepEqual([s.count, s.sum, s.avg, s.min, s.max, s.cells], [3, 9, 3, 1, 6, 5]);
});

test('undo stack: push clears redo, undo and redo walk history, limit trims', () => {
  const u = new M.UndoStack(2);
  u.push([1]); u.push([2]); u.push([3]);
  assert.equal(u.past.length, 2);
  assert.deepEqual(u.undo(), [3]);
  assert.equal(u.canRedo, true);
  assert.deepEqual(u.redo(), [3]);
  u.undo();
  u.push([9]);
  assert.equal(u.canRedo, false);
  assert.equal(new M.UndoStack().undo(), null);
});

test('type-ahead finds the first label that starts with the buffer', () => {
  const o = [{ label: 'Open' }, { label: 'Done' }, { label: 'Doing' }];
  assert.equal(M.typeAhead(o, 'do'), 1);
  assert.equal(M.typeAhead(o, 'doi'), 2);
  assert.equal(M.typeAhead(o, 'x'), -1);
});

test('formatting: numbers, percent, dates, select labels', () => {
  assert.equal(M.formatValue(num, 1234.5), '1,234.5');
  assert.equal(M.formatValue({ type: 'number', percent: true, decimals: 1 }, 0.256), '25.6%');
  assert.equal(M.formatValue(sel, 'done'), 'Done');
  assert.equal(M.formatValue(rating, null), '');
});
