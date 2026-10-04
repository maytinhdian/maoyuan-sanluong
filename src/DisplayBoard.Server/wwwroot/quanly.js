// Trang quản lý (bản 4.x): sửa số liệu mọi ngày, kế hoạch, mục tiêu tháng, danh mục, lịch làm việc, xuất/nhập Excel.
// Đăng nhập dùng chung mã phiên với trang /nhap; máy chủ chỉ cho người được đánh dấu "Quản lý".
(function () {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const TOKEN = 'nhap.token', NAME = 'nhap.name';
  const store = {
    get(k) { try { return localStorage.getItem(k); } catch { return null; } },
    set(k, v) { try { v == null ? localStorage.removeItem(k) : localStorage.setItem(k, v); } catch { } }
  };
  const state = { token: store.get(TOKEN), catalog: null, day: null, tab: 'day' };

  const fmt = (n) => n == null ? '' : Number(n).toLocaleString('vi-VN');
  const esc = (s) => String(s == null ? '' : s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const iso = (d) => d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
  const vn = (s) => s ? s.split('-').reverse().join('/') : '';
  const addDays = (s, n) => { const d = new Date(s + 'T00:00:00'); d.setDate(d.getDate() + n); return iso(d); };
  const num = (v) => { v = String(v == null ? '' : v).trim().replace(',', '.'); return v === '' ? null : Number(v); };
  const text = (v) => { v = String(v == null ? '' : v).trim(); return v === '' ? null : v; };

  // ---------- Gọi máy chủ ----------
  async function api(path, options = {}) {
    const headers = {};
    if (state.token) headers['X-Entry-Token'] = state.token;
    let body = options.body;
    if (options.json !== undefined) {
      headers['Content-Type'] = 'application/json';
      body = JSON.stringify(options.json);
    }
    let res;
    try {
      res = await fetch(path, { method: options.method || 'GET', headers, body, cache: 'no-store' });
    } catch {
      throw new Error('Không kết nối được máy chủ.');
    }
    if (options.raw && res.ok) return res;
    let data = null;
    try { data = await res.json(); } catch { }
    if (res.status === 401 || res.status === 403) {
      logout(data && data.error);
      throw new Error((data && data.error) || 'Cần đăng nhập lại.');
    }
    if (!res.ok) throw new Error((data && data.error) || ('Lỗi máy chủ (' + res.status + ').'));
    return data;
  }

  let toastTimer;
  function toast(msg, bad) {
    const el = $('toast');
    el.textContent = msg;
    el.classList.toggle('bad', !!bad);
    el.classList.remove('hidden');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => el.classList.add('hidden'), bad ? 7000 : 3000);
  }

  async function busy(button, action) {
    if (button) button.disabled = true;
    try { return await action(); } catch (e) { toast(e.message, true); } finally { if (button) button.disabled = false; }
  }

  // ---------- Đăng nhập ----------
  async function showLogin(message) {
    $('main').classList.add('hidden');
    $('login').classList.remove('hidden');
    $('loginError').textContent = message || '';
    $('loginError').classList.toggle('hidden', !message);
    try {
      const info = await api('/api/nhap/info');
      $('loginApp').textContent = info.appName || 'Quản lý';
      document.title = 'Quản lý · ' + (info.appName || '');
    } catch { }
    $('pin').focus();
  }

  async function login() {
    const pin = $('pin').value.trim();
    if (!pin) return;
    await busy($('loginBtn'), async () => {
      try {
        const r = await api('/api/nhap/login', { method: 'POST', json: { pin } });
        if (!r.manager) throw new Error(r.name + ' không phải quản lý. Trang này chỉ dành cho quản lý.');
        state.token = r.token;
        store.set(TOKEN, r.token);
        store.set(NAME, r.name);
        $('pin').value = '';
        await start();
      } catch (e) {
        $('loginError').textContent = e.message;
        $('loginError').classList.remove('hidden');
      }
    });
  }

  function logout(message) {
    state.token = null;
    store.set(TOKEN, null);
    showLogin(message);
  }

  async function start() {
    state.catalog = await api('/api/quanly/catalog');
    $('login').classList.add('hidden');
    $('main').classList.remove('hidden');
    $('who').textContent = (store.get(NAME) || '') + ' · Đăng xuất';
    const info = await api('/api/nhap/info').catch(() => null);
    $('appName').textContent = 'Quản lý · ' + ((info && info.appName) || '');
    $('dbInfo').textContent = state.catalog.filePath || '';
    const today = state.catalog.today;
    $('dayDate').value = state.catalog.displayDate || today;
    $('exportDate').value = state.catalog.displayDate || today;
    $('targetMonth').value = (state.catalog.displayDate || today).slice(0, 7);
    $('exportMonth').value = today.slice(0, 7);
    showTab(state.tab);
  }

  // ---------- Tab ----------
  function showTab(tab) {
    state.tab = tab;
    document.querySelectorAll('#tabs button').forEach((b) => b.classList.toggle('on', b.dataset.tab === tab));
    document.querySelectorAll('.tab').forEach((t) => t.classList.toggle('hidden', t.id !== 'tab-' + tab));
    if (tab === 'day') loadDay();
    if (tab === 'targets') loadTargets();
    if (tab === 'catalog') renderCatalog();
    if (tab === 'calendar') renderCalendar();
    if (tab === 'audit') loadAudit();
  }

  // ---------- Bảng sửa được dùng chung ----------
  // columns: { key, label, type: 'text'|'num'|'check'|'select'|'periods', options?, cls? }
  function editGrid(table, columns, rows, extra) {
    table.innerHTML = '<thead><tr>' + columns.map((c) => '<th>' + esc(c.label) + '</th>').join('') + '<th></th></tr></thead><tbody></tbody>';
    const body = table.tBodies[0];
    const add = (row) => {
      const tr = document.createElement('tr');
      tr.innerHTML = columns.map((c) => '<td>' + cell(c, row[c.key]) + '</td>').join('') +
        '<td><button class="btn danger" title="Xoá dòng">✕</button></td>';
      tr.querySelector('.danger').onclick = () => tr.remove();
      tr.addEventListener('input', () => tr.classList.add('dirty'));
      if (extra) extra(tr, row);
      body.appendChild(tr);
      return tr;
    };
    rows.forEach(add);
    return {
      add: (row) => { const tr = add(row || {}); tr.classList.add('dirty'); const f = tr.querySelector('input,select'); if (f) f.focus(); },
      values: () => Array.from(body.rows).map((tr) => {
        const out = {};
        columns.forEach((c, i) => { out[c.key] = read(c, tr.cells[i]); });
        return out;
      })
    };
  }

  function cell(c, value) {
    if (c.type === 'check') return '<input type="checkbox"' + (value ? ' checked' : '') + '>';
    if (c.type === 'select') {
      const opts = (c.options || []);
      const has = value == null || value === '' || opts.some((o) => o.value === value);
      return '<select><option value=""></option>' + (has ? '' : '<option selected>' + esc(value) + '</option>') +
        opts.map((o) => '<option value="' + esc(o.value) + '"' + (o.value === value ? ' selected' : '') + '>' + esc(o.label) + '</option>').join('') + '</select>';
    }
    if (c.type === 'periods') {
      const p = value || [];
      return [0, 1, 2].map((i) => '<input class="m"' + (i === 0 && !p[0] ? ' placeholder="07:30-11:30"' : '') + ' value="' + esc(p[i] || '') + '">').join(' ');
    }
    if (c.type === 'readonly') return '<span class="num">' + esc(value == null ? '' : value) + '</span>';
    return '<input class="' + (c.cls || (c.type === 'num' ? 'n' : '')) + '" ' + (c.type === 'num' ? 'inputmode="decimal" ' : '') +
      'value="' + esc(value == null ? '' : value) + '">';
  }

  function read(c, td) {
    if (c.type === 'check') return td.querySelector('input').checked;
    if (c.type === 'select') return text(td.querySelector('select').value);
    if (c.type === 'periods') return Array.from(td.querySelectorAll('input')).map((i) => text(i.value)).filter(Boolean);
    if (c.type === 'readonly') return undefined;
    const v = td.querySelector('input').value;
    return c.type === 'num' ? num(v) : text(v);
  }

  // ---------- Theo ngày ----------
  async function loadDay(date) {
    if (date) $('dayDate').value = date;
    const d = $('dayDate').value || state.catalog.today;
    try {
      state.day = await api('/api/quanly/day?date=' + d);
      renderDay();
    } catch (e) { toast(e.message, true); }
  }

  function renderDay() {
    const c = state.catalog, day = state.day;
    const products = c.products.filter((p) => p.active).map((p) => ({ value: p.code, label: p.code + (p.name ? ' · ' + p.name : '') }));
    const shifts = c.shifts.filter((s) => s.active).map((s) => ({ value: s.code, label: s.code + ' (' + fmt(s.hours) + 'h)' }));
    const reasons = c.reasons.map((r) => ({ value: r.name, label: r.name }));
    const sel = (opts, v) => cell({ type: 'select', options: opts }, v);
    const head = ['Chuyền', 'Mã sản phẩm', 'Ca', 'MT/giờ'].concat(Array.from({ length: 12 }, (_, i) => 'G' + (i + 1)))
      .concat(['Tổng', 'MT ngày', '%', 'Lỗi', 'Công nhân', 'Lý do', 'Dừng máy (phút)', 'Trạng thái', 'Ghi chú', '']);
    const t = $('dayTable');
    t.innerHTML = '<thead><tr>' + head.map((h) => '<th>' + h + '</th>').join('') + '</tr></thead><tbody></tbody>';
    day.lines.forEach((l) => {
      const e = l.entry || { hours: [] };
      const tr = document.createElement('tr');
      if (!l.active) tr.classList.add('off');
      // % đạt như cột % HOÀN THÀNH của HIEN_THI (đã nhân 100).
      const rate = l.dailyRate == null ? '' : Math.round(l.dailyRate) + '%';
      const rateCls = l.dailyRate == null ? '' : l.dailyRate >= 100 ? 'ok' : l.dailyRate >= 90 ? 'low' : 'bad';
      tr.innerHTML = '<td><b>' + esc(l.name) + '</b>' + (l.active ? '' : ' <span class="t2 small">(ngừng)</span>') + '</td>' +
        '<td>' + sel(products, e.productCode) + '</td><td>' + sel(shifts, e.shiftCode) + '</td>' +
        '<td><input class="n" inputmode="decimal" value="' + esc(e.hourlyTarget == null ? '' : e.hourlyTarget) + '"></td>' +
        Array.from({ length: 12 }, (_, i) => '<td><input class="h" inputmode="decimal" title="' + esc((e.slots || [])[i] || '') + '" value="' +
          esc(e.hours[i] == null ? '' : e.hours[i]) + '"></td>').join('') +
        '<td class="num"><b>' + (l.entry ? fmt(e.actual) : '') + '</b></td><td class="num">' + fmt(e.dailyTarget) + '</td>' +
        '<td class="num ' + rateCls + '">' + rate + '</td><td class="num">' + fmt(l.defects) + '</td>' +
        '<td><input class="n" inputmode="decimal" value="' + esc(e.workers == null ? '' : e.workers) + '"></td>' +
        '<td>' + sel(reasons, e.reason) + '</td>' +
        '<td><input class="n" inputmode="decimal" value="' + esc(e.downtimeMinutes == null ? '' : e.downtimeMinutes) + '"></td>' +
        '<td><input class="s" value="' + esc(e.status || '') + '"></td>' +
        '<td><input class="w" value="' + esc(e.note || '') + '"></td>' +
        '<td><button class="btn primary save">Lưu</button> ' + (l.entry ? '<button class="btn danger del" title="Xoá dòng của chuyền ngày này">Xoá</button>' : '') + '</td>';
      tr.addEventListener('input', () => tr.classList.add('dirty'));
      tr.querySelector('.save').onclick = (ev) => busy(ev.target, () => saveEntry(l, tr));
      const del = tr.querySelector('.del');
      if (del) del.onclick = (ev) => {
        if (!confirm('Xoá cả dòng của ' + l.name + ' ngày ' + vn(day.date) + ' (kế hoạch và sản lượng các giờ)?')) return;
        busy(ev.target, async () => {
          state.day = await api('/api/quanly/entry?date=' + day.date + '&line=' + encodeURIComponent(l.code), { method: 'DELETE' });
          renderDay();
          toast('Đã xoá dòng ' + l.name);
        });
      };
      t.tBodies[0].appendChild(tr);
    });
    renderDefects();
  }

  async function saveEntry(line, tr) {
    const inputs = tr.querySelectorAll('input, select');
    const v = Array.from(inputs).map((i) => i.value);
    // Thứ tự ô: mã SP, ca, MT/giờ, 12 giờ, công nhân, lý do, dừng máy, trạng thái, ghi chú.
    const body = {
      line: line.code, date: state.day.date, productCode: text(v[0]), shiftCode: text(v[1]), hourlyTarget: num(v[2]),
      hours: v.slice(3, 15).map(num), workers: num(v[15]), reason: text(v[16]), downtimeMinutes: num(v[17]), status: text(v[18]), note: text(v[19])
    };
    if ([body.hourlyTarget, body.workers, body.downtimeMinutes].concat(body.hours).some((n) => n != null && isNaN(n)))
      throw new Error(line.name + ': có ô không phải số.');
    state.day = await api('/api/quanly/entry', { method: 'POST', json: body });
    renderDay();
    toast('Đã lưu ' + line.name + ' ngày ' + vn(body.date));
  }

  function renderDefects() {
    const c = state.catalog, day = state.day;
    const lines = c.lines.map((l) => ({ value: l.name, label: l.name }));
    const types = c.defectTypes.map((t) => ({ value: t.name, label: t.name }));
    const t = $('defectTable');
    t.innerHTML = '<thead><tr><th>Giờ</th><th>Chuyền</th><th>Loại lỗi</th><th>Số lượng</th><th>Ghi chú</th><th>Ảnh</th><th></th></tr></thead><tbody></tbody>';
    const add = (d) => {
      const tr = document.createElement('tr');
      tr.innerHTML = '<td><input class="s" placeholder="09:30" value="' + esc(d.time || '') + '"></td>' +
        '<td>' + cell({ type: 'select', options: lines }, d.line) + '</td>' +
        '<td>' + cell({ type: 'select', options: types }, d.defectType) + '</td>' +
        '<td><input class="n" inputmode="decimal" value="' + esc(d.quantity == null ? '' : d.quantity) + '"></td>' +
        '<td><input class="w" value="' + esc(d.note || '') + '"></td>' +
        '<td class="small t2">' + esc(d.imageFiles || '') + '</td>' +
        '<td><button class="btn primary save">Lưu</button> ' + (d.id ? '<button class="btn danger del">Xoá</button>' : '') + '</td>';
      tr.addEventListener('input', () => tr.classList.add('dirty'));
      if (!d.id) tr.classList.add('dirty');
      tr.querySelector('.save').onclick = (ev) => busy(ev.target, async () => {
        const v = Array.from(tr.querySelectorAll('input, select')).map((i) => i.value);
        state.day = await api('/api/quanly/defect', {
          method: 'POST',
          json: { id: d.id || 0, date: day.date, time: text(v[0]), line: text(v[1]), defectType: text(v[2]), quantity: num(v[3]), note: text(v[4]) }
        });
        renderDay();
        toast('Đã lưu phiếu hàng lỗi');
      });
      const del = tr.querySelector('.del');
      if (del) del.onclick = (ev) => {
        if (!confirm('Xoá phiếu hàng lỗi này?')) return;
        busy(ev.target, async () => {
          state.day = await api('/api/quanly/defect/' + d.id, { method: 'DELETE' });
          renderDay();
          toast('Đã xoá phiếu hàng lỗi');
        });
      };
      t.tBodies[0].appendChild(tr);
      return tr;
    };
    day.defects.forEach(add);
    $('addDefect').onclick = () => add({}).querySelector('input').focus();
  }

  // ---------- Mục tiêu tháng ----------
  let targetGrid;
  async function loadTargets() {
    const month = ($('targetMonth').value || state.catalog.today.slice(0, 7)) + '-01';
    await busy(null, async () => renderTargets(await api('/api/quanly/targets?month=' + month)));
  }

  function renderTargets(r) {
    const products = state.catalog.products.map((p) => ({ value: p.code, label: p.code + (p.name ? ' · ' + p.name : '') }));
    $('workDays').textContent = 'Ngày làm việc trong tháng: ' + r.workingDays;
    targetGrid = editGrid($('targetTable'), [
      { key: 'productCode', label: 'Mã sản phẩm', type: 'select', options: products },
      { key: 'target', label: 'Mục tiêu tháng', type: 'num', cls: 'm' },
      { key: 'done', label: 'Đã làm', type: 'readonly' },
      { key: 'note', label: 'Ghi chú', type: 'text', cls: 'w' }
    ], r.targets.map((t) => Object.assign({}, t, { done: fmt(t.done) })));
  }

  async function saveTargets(button) {
    await busy(button, async () => {
      const month = $('targetMonth').value + '-01';
      const targets = targetGrid.values().filter((t) => t.productCode).map((t) => ({ productCode: t.productCode, target: t.target || 0, note: t.note }));
      renderTargets(await api('/api/quanly/targets', { method: 'POST', json: { month, targets } }));
      toast('Đã lưu mục tiêu tháng ' + $('targetMonth').value.split('-').reverse().join('/'));
    });
  }

  // ---------- Danh mục ----------
  const lists = {
    lines: {
      path: 'lines', what: 'chuyền',
      columns: [{ key: 'code', label: 'Mã', cls: 's' }, { key: 'name', label: 'Tên hiện trên TV', cls: 'm' }, { key: 'leader', label: 'Trưởng chuyền', cls: 'm' }, { key: 'active', label: 'Đang dùng', type: 'check' }],
      rows: (c) => c.lines, blank: () => ({ active: true })
    },
    products: {
      path: 'products', what: 'sản phẩm',
      columns: [{ key: 'code', label: 'Mã sản phẩm', cls: 'm' }, { key: 'name', label: 'Tên sản phẩm', cls: 'w' }, { key: 'note', label: 'Ghi chú', cls: 'w' }, { key: 'active', label: 'Đang dùng', type: 'check' }],
      rows: (c) => c.products, blank: () => ({ active: true })
    },
    shifts: {
      path: 'shifts', what: 'ca',
      columns: [{ key: 'code', label: 'Mã ca', cls: 's' }, { key: 'name', label: 'Tên ca', cls: 'm' }, { key: 'periods', label: 'Đợt 1 · Đợt 2 · Đợt 3', type: 'periods' }, { key: 'hours', label: 'Giờ ca', type: 'readonly' }, { key: 'active', label: 'Đang dùng', type: 'check' }],
      rows: (c) => c.shifts, blank: () => ({ active: true })
    },
    reasons: { path: 'reasons', what: 'lý do', columns: [{ key: 'name', label: 'Lý do', cls: 'w' }, { key: 'note', label: 'Ghi chú', cls: 'w' }], rows: (c) => c.reasons, blank: () => ({}) },
    defectTypes: { path: 'defect-types', what: 'loại lỗi', columns: [{ key: 'name', label: 'Loại lỗi', cls: 'w' }, { key: 'note', label: 'Ghi chú', cls: 'w' }], rows: (c) => c.defectTypes, blank: () => ({}) },
    calendar: {
      path: 'calendar', what: 'lịch',
      columns: [{ key: 'date', label: 'Ngày (năm-tháng-ngày)', cls: 'm' }, { key: 'kind', label: 'Loại', type: 'select', options: [{ value: 'Nghỉ', label: 'Nghỉ' }, { value: 'Làm bù', label: 'Làm bù' }] }, { key: 'note', label: 'Ghi chú', cls: 'w' }],
      rows: (c) => c.calendar, blank: () => ({ kind: 'Nghỉ' })
    }
  };

  function renderList(panel) {
    const def = lists[panel.dataset.list];
    const grid = editGrid(panel.querySelector('table'), def.columns, def.rows(state.catalog));
    panel.querySelector('.add').onclick = () => grid.add(def.blank());
    panel.querySelector('.save').onclick = (ev) => busy(ev.target, async () => {
      let rows = grid.values().map((r) => { delete r.hours; return r; });
      if (def.path === 'calendar') {
        rows = rows.filter((r) => r.date);
        if (rows.some((r) => !/^\d{4}-\d{2}-\d{2}$/.test(r.date))) throw new Error('Ngày phải dạng 2026-10-05.');
      }
      state.catalog = await api('/api/quanly/' + def.path, { method: 'POST', json: rows });
      renderList(panel);
      toast('Đã lưu danh sách ' + def.what);
    });
  }

  function renderCatalog() {
    document.querySelectorAll('#tab-catalog .panel').forEach(renderList);
  }

  function renderCalendar() {
    renderList(document.querySelector('#tab-calendar .panel[data-list=calendar]'));
    const c = state.catalog;
    $('displayInfo').textContent = c.displayDateOverride
      ? 'TV đang hiện ngày ' + vn(c.displayDateOverride) + ' (chọn tay). Bấm "Hiện ngày mới nhất" để TV tự theo ngày có số liệu mới nhất.'
      : 'TV đang hiện ngày mới nhất có số liệu' + (c.displayDate ? ': ' + vn(c.displayDate) : '') + '.';
    $('displayDate').value = c.displayDateOverride || c.displayDate || c.today;
  }

  async function setDisplay(button, date) {
    await busy(button, async () => {
      state.catalog = await api('/api/quanly/display-date', { method: 'POST', json: { date } });
      renderCalendar();
      toast(date ? 'TV sẽ hiện ngày ' + vn(date) : 'TV hiện ngày mới nhất');
    });
  }

  // ---------- Xuất / nhập Excel ----------
  async function download(button, query) {
    await busy(button, async () => {
      const res = await api('/api/quanly/export?' + query, { raw: true });
      const name = (/filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(res.headers.get('Content-Disposition') || '') || [])[1] || 'SanLuong.xlsx';
      const url = URL.createObjectURL(await res.blob());
      const a = document.createElement('a');
      a.href = url;
      a.download = decodeURIComponent(name);
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 10000);
    });
  }

  async function importRead(button) {
    const file = $('importFile').files[0];
    if (!file) return toast('Chọn file Excel trước.', true);
    await busy(button, async () => {
      const form = new FormData();
      form.append('file', file);
      const r = await api('/api/quanly/import', { method: 'POST', body: form });
      const box = $('importResult');
      const ok = r.mismatches === 0;
      box.innerHTML =
        '<div class="msg ' + (ok ? 'ok' : 'warn') + '"><b>' + esc(r.file) + '</b>: ' + r.lines + ' chuyền, ' + r.products + ' mã sản phẩm, ' +
        r.entries + ' dòng nhập liệu' + (r.from ? ' (từ ' + vn(r.from) + ' đến ' + vn(r.to) + ')' : '') + ', ' + r.defects + ' phiếu hàng lỗi.<br>' +
        (!r.hasExcelValues ? 'File chưa được lưu bằng Excel nên không có số để so. Mở file bằng Excel, bấm Lưu rồi chọn lại nếu muốn so số.'
          : ok ? '✓ Đã so ' + r.checked + ' dòng: mục tiêu ngày và sản lượng app tính khớp với file Excel.'
          : '⚠ Có ' + r.mismatches + ' chỗ khác nhau giữa số trong file và số app tính (xem bảng dưới).') + '</div>' +
        (r.warnings.length ? '<p class="small t2">Lưu ý: ' + r.warnings.map(esc).join('<br>') + '</p>' : '') +
        (r.rows.length ? '<div class="scroll"><table class="grid"><thead><tr><th>Ngày</th><th>Chuyền</th><th>MT ngày Excel</th><th>MT ngày app</th><th>SL Excel</th><th>SL app</th></tr></thead><tbody>' +
          r.rows.map((x) => '<tr><td>' + vn(x.date) + '</td><td>' + esc(x.line) + '</td><td class="num">' + fmt(x.excelTarget) + '</td><td class="num">' + fmt(x.appTarget) +
            '</td><td class="num">' + fmt(x.excelActual) + '</td><td class="num">' + fmt(x.appActual) + '</td></tr>').join('') + '</tbody></table></div>' : '') +
        (r.displayDifferences.length ? '<p class="small t2">' + r.displayDifferences.map(esc).join('<br>') + '</p>' : '') +
        '<div class="bar"><button class="btn primary" id="importApply">Xác nhận: thay toàn bộ dữ liệu bằng file này</button></div>';
      box.classList.remove('hidden');
      $('importApply').onclick = (ev) => {
        if (!confirm('Thay toàn bộ dữ liệu trên máy chủ bằng dữ liệu trong file ' + r.file + '? Dữ liệu hiện có được sao lưu trước.')) return;
        busy(ev.target, async () => {
          state.catalog = await api('/api/quanly/import/' + r.id, { method: 'POST' });
          box.innerHTML = '<div class="msg ok">✓ Đã nhập dữ liệu từ ' + esc(r.file) + '. TV đã cập nhật.</div>';
          $('dayDate').value = state.catalog.displayDate || state.catalog.today;
        });
      };
    });
  }

  // ---------- Nhật ký ----------
  async function loadAudit() {
    await busy(null, async () => {
      const rows = await api('/api/quanly/audit');
      $('auditTable').innerHTML = '<thead><tr><th>Lúc</th><th>Người</th><th>Việc</th><th>Trước</th><th>Sau</th></tr></thead><tbody>' +
        rows.map((a) => '<tr><td>' + new Date(a.at).toLocaleString('vi-VN') + '</td><td>' + esc(a.user) + '</td><td>' + esc(a.action) +
          '</td><td class="wrap t2">' + esc(a.before) + '</td><td class="wrap">' + esc(a.after) + '</td></tr>').join('') + '</tbody>';
    });
  }

  // ---------- Gắn sự kiện ----------
  $('loginBtn').onclick = login;
  $('pin').addEventListener('keydown', (e) => { if (e.key === 'Enter') login(); });
  $('who').onclick = () => { if (confirm('Đăng xuất?')) logout(); };
  document.querySelectorAll('#tabs button').forEach((b) => { b.onclick = () => showTab(b.dataset.tab); });
  $('dayDate').onchange = () => loadDay();
  $('dayPrev').onclick = () => loadDay(addDays($('dayDate').value, -1));
  $('dayNext').onclick = () => loadDay(addDays($('dayDate').value, 1));
  $('dayToday').onclick = () => loadDay(state.catalog.today);
  $('copyPlan').onclick = (ev) => busy(ev.target, async () => {
    state.day = await api('/api/quanly/copy-plan?date=' + $('dayDate').value, { method: 'POST' });
    renderDay();
    toast('Đã chép kế hoạch cho các chuyền chưa có dòng');
  });
  $('targetMonth').onchange = loadTargets;
  $('addTarget').onclick = () => targetGrid && targetGrid.add({});
  $('saveTargets').onclick = (ev) => saveTargets(ev.target);
  $('setDisplay').onclick = (ev) => setDisplay(ev.target, $('displayDate').value || null);
  $('clearDisplay').onclick = (ev) => setDisplay(ev.target, null);
  $('exportDay').onclick = (ev) => download(ev.target, 'date=' + $('exportDate').value);
  $('exportMonthBtn').onclick = (ev) => download(ev.target, 'month=' + $('exportMonth').value + '-01');
  $('importRead').onclick = (ev) => importRead(ev.target);

  if (state.token) start().catch(() => showLogin()); else showLogin();
})();
