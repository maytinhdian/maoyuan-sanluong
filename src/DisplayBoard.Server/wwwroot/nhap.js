// Trang nhập liệu cho tổ trưởng: sản lượng theo giờ, kế hoạch trong ngày, hàng lỗi kèm ảnh.
// Máy chủ ghi thẳng vào cơ sở dữ liệu (bản 4.x); trang này chỉ gửi và xem trạng thái từng phiếu.
(function () {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const TOKEN = 'nhap.token', NAME = 'nhap.name', LINE = 'nhap.line';
  const store = {
    get(k) { try { return localStorage.getItem(k); } catch { return null; } },
    set(k, v) { try { v == null ? localStorage.removeItem(k) : localStorage.setItem(k, v); } catch { } }
  };

  const state = {
    token: store.get(TOKEN),
    line: store.get(LINE),
    context: null,
    hour: null,
    jobs: [],
    photos: [], // { blob, url }
    tab: 'output',
    mine: new Set() // phiếu gửi từ máy này, chờ báo kết quả
  };

  const fmt = (n) => n == null ? '–' : Number(n).toLocaleString('vi-VN');
  const hhmm = (t) => t ? t.slice(0, 5) : '';

  // ---------- Gọi máy chủ ----------
  async function api(path, options = {}) {
    const headers = Object.assign({}, options.headers || {});
    if (state.token) headers['X-Entry-Token'] = state.token;
    if (options.json !== undefined) {
      headers['Content-Type'] = 'application/json';
      options.body = JSON.stringify(options.json);
    }
    let res;
    try {
      res = await fetch(path, { method: options.method || 'GET', headers, body: options.body, cache: 'no-store' });
    } catch {
      throw new Error('Không kết nối được máy chủ. Kiểm tra Wi‑Fi.');
    }
    let data = null;
    try { data = await res.json(); } catch { }
    if (res.status === 401 && path !== '/api/nhap/login') {
      logout(data && data.error);
      throw new Error((data && data.error) || 'Cần đăng nhập lại.');
    }
    if (!res.ok) throw new Error((data && data.error) || ('Lỗi máy chủ (' + res.status + ').'));
    return data;
  }

  let toastTimer;
  function toast(text, bad) {
    const el = $('toast');
    el.textContent = text;
    el.classList.toggle('bad', !!bad);
    el.classList.remove('hidden');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => el.classList.add('hidden'), bad ? 6000 : 3000);
  }

  // ---------- Đăng nhập ----------
  async function showLogin(message) {
    $('main').classList.add('hidden');
    $('login').classList.remove('hidden');
    $('loginError').textContent = message || '';
    $('loginError').classList.toggle('hidden', !message);
    try {
      const info = await api('/api/nhap/info');
      $('loginApp').textContent = info.appName || 'Nhập liệu';
      document.title = 'Nhập liệu · ' + (info.appName || '');
      const note = !info.enabled ? 'Chưa có ai được nhập liệu. Quản lý cần thêm người và mã PIN ở app trên máy chủ, tab Nhập liệu.'
        : info.unavailable ? info.unavailable : '';
      $('loginNote').textContent = note;
      $('loginNote').classList.toggle('hidden', !note);
    } catch { }
    $('pin').focus();
  }

  async function login() {
    const pin = $('pin').value.trim();
    if (!pin) return;
    $('loginBtn').disabled = true;
    try {
      const r = await api('/api/nhap/login', { method: 'POST', json: { pin } });
      state.token = r.token;
      store.set(TOKEN, r.token);
      store.set(NAME, r.name);
      $('pin').value = '';
      await start();
    } catch (e) {
      $('loginError').textContent = e.message;
      $('loginError').classList.remove('hidden');
    } finally {
      $('loginBtn').disabled = false;
    }
  }

  function logout(message) {
    state.token = null;
    store.set(TOKEN, null);
    showLogin(message);
  }

  // ---------- Dữ liệu ----------
  async function start() {
    $('login').classList.add('hidden');
    $('main').classList.remove('hidden');
    $('who').textContent = store.get(NAME) || 'Đăng xuất';
    const info = await api('/api/nhap/info').catch(() => null);
    $('unavailable').textContent = info && info.unavailable ? info.unavailable : '';
    $('unavailable').classList.toggle('hidden', !(info && info.unavailable));
    await loadContext();
    await loadJobs();
  }

  async function loadContext() {
    try {
      const c = await api('/api/nhap/context' + (state.line ? '?line=' + encodeURIComponent(state.line) : ''));
      if (!c.line && c.lines.length) {
        state.line = c.lines[0];
        store.set(LINE, state.line);
        return loadContext();
      }
      const changedDay = state.context && state.context.date !== c.date;
      const prevLine = state.context && state.context.line && state.context.line.line;
      state.context = c;
      if (changedDay || prevLine !== (c.line && c.line.line) || state.hour == null) state.hour = c.line ? c.line.suggestedHour : null;
      render();
    } catch (e) {
      if (state.token) toast(e.message, true);
    }
  }

  async function loadJobs() {
    try {
      state.jobs = await api('/api/nhap/jobs');
      // Phiếu gửi từ máy này vừa ghi xong: báo và đọc lại số liệu để thấy số mới.
      let refresh = false;
      for (const j of state.jobs) {
        if (!state.mine.has(j.id) || (j.status !== 'Done' && j.status !== 'Failed')) continue;
        state.mine.delete(j.id);
        toast(j.status === 'Done' ? '✓ Đã lưu: ' + j.summary : '✗ ' + (j.message || 'Không ghi được'), j.status !== 'Done');
        refresh = refresh || j.status === 'Done';
      }
      renderJobs();
      if (refresh) await loadContext();
    } catch { }
  }

  // ---------- Hiển thị ----------
  function render() {
    const c = state.context;
    if (!c) return;
    $('today').textContent = new Date(c.date + 'T00:00:00').toLocaleDateString('vi-VN', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' });

    const lines = $('lines');
    lines.innerHTML = '';
    for (const name of c.lines) {
      const b = document.createElement('button');
      b.className = 'chip' + (c.line && c.line.line === name ? ' on' : '');
      b.textContent = shortLine(name);
      b.title = name;
      b.onclick = () => { state.line = name; state.hour = null; store.set(LINE, name); hidePlanForm(); loadContext(); };
      lines.appendChild(b);
    }
    if (!c.lines.length) lines.innerHTML = '<div class="t2 small">Bạn chưa được giao chuyền nào.</div>';

    renderOutput();
    renderDefect();
  }

  function shortLine(name) {
    const m = /(\d+)\s*$/.exec(name);
    return m && name.length > 4 ? m[1] : name;
  }

  function renderOutput() {
    const day = state.context.line;
    const hasPlan = !!(day && day.plan);
    // Đang sửa kế hoạch thì ẩn phần nhập giờ để không bấm nhầm "Lưu".
    const editing = !$('planForm').classList.contains('hidden');
    $('hourBlock').classList.toggle('hidden', !hasPlan || editing);
    $('sendHour').classList.toggle('hidden', !hasPlan || editing);
    $('planCard').classList.toggle('hidden', !day);
    if (!day) return;
    if (!hasPlan) { showPlanForm('Chuyền này chưa có kế hoạch. Chọn mã sản phẩm, ca và mục tiêu mỗi giờ để bắt đầu nhập.'); }

    $('pProduct').textContent = hasPlan ? day.plan.productCode : '–';
    $('pShift').textContent = hasPlan ? day.plan.shiftCode + ' · ' + fmt(day.plan.hourlyTarget) : '–';
    $('pActual').textContent = fmt(day.actual) + ' / ' + fmt(day.dailyTarget);
    const pct = day.dailyTarget ? Math.min(100, day.actual / day.dailyTarget * 100) : 0;
    $('pBar').style.width = pct + '%';
    $('pBar').classList.toggle('ok', pct >= 100);
    $('planNote').textContent = !day.hasRow && day.planCopiedFrom
      ? 'Chưa có dòng hôm nay: dùng kế hoạch ngày ' + new Date(day.planCopiedFrom + 'T00:00:00').toLocaleDateString('vi-VN') + '.'
      : '';

    const grid = $('hours');
    grid.innerHTML = '';
    for (const h of day.hours) {
      const el = document.createElement('button');
      const cls = h.number === state.hour ? 'now' : h.quantity == null ? 'empty' : (h.target && h.quantity < h.target ? 'low' : 'ok');
      el.className = 'h ' + cls;
      el.innerHTML = '<div class="n">G' + h.number + (h.start ? ' ' + hhmm(h.start) : '') + '</div><div class="v">' +
        (h.number === state.hour && h.quantity == null ? '?' : h.quantity == null ? '–' : fmt(h.quantity)) + '</div>';
      el.onclick = () => { state.hour = h.number; renderOutput(); };
      grid.appendChild(el);
    }
    const cur = day.hours.find((h) => h.number === state.hour) || day.hours[0];
    if (cur) {
      state.hour = cur.number;
      $('hourLabel').innerHTML = 'GIỜ ' + cur.number + (cur.start ? ' (' + hhmm(cur.start) + '–' + hhmm(cur.end) + ')' : '') +
        ' · SỐ SẢN PHẨM<span class="zh">第' + cur.number + '小時</span>';
      const qty = $('qty');
      if (qty.dataset.hour !== String(cur.number) || qty.dataset.line !== day.line) {
        qty.value = cur.quantity == null ? '' : cur.quantity;
        qty.dataset.hour = String(cur.number);
        qty.dataset.line = day.line;
      }
      updateHint();
    }
  }

  function updateHint() {
    const day = state.context && state.context.line;
    const cur = day && day.hours.find((h) => h.number === state.hour);
    if (!cur) return;
    const v = $('qty').value === '' ? null : Number($('qty').value);
    let text = cur.target ? 'Mục tiêu giờ này ' + fmt(cur.target) : '';
    if (cur.target && v != null) text += v >= cur.target ? ' · đạt' : ' · còn thiếu ' + fmt(cur.target - v);
    if (cur.quantity != null) text += (text ? ' · ' : '') + 'đã ghi ' + fmt(cur.quantity);
    $('hourHint').textContent = text;
  }

  function showPlanForm(why) {
    const c = state.context, day = c.line;
    $('planLine').textContent = day.line;
    $('planWhy').textContent = why || '';
    fill($('fProduct'), c.products.map((p) => [p.code, p.name ? p.code + ' · ' + p.name : p.code]), day.plan && day.plan.productCode);
    fill($('fShift'), c.shifts.map((s) => [s.code, s.name ? s.code + ' · ' + s.name : s.code]), day.plan && day.plan.shiftCode);
    $('fTarget').value = day.plan ? day.plan.hourlyTarget : '';
    $('cancelPlan').classList.toggle('hidden', !day.plan);
    if ($('planForm').classList.contains('hidden')) {
      $('planForm').classList.remove('hidden');
      renderOutput();
    }
  }

  function hidePlanForm() {
    $('planForm').classList.add('hidden');
    if (state.context) renderOutput();
  }

  function fill(select, items, selected) {
    select.innerHTML = '';
    for (const [value, label] of items) {
      const o = document.createElement('option');
      o.value = value; o.textContent = label;
      if (selected && value.toLowerCase() === String(selected).toLowerCase()) o.selected = true;
      select.appendChild(o);
    }
  }

  function renderDefect() {
    const c = state.context, day = c.line;
    $('dProduct').textContent = day && day.plan ? day.plan.productCode : '–';
    const now = new Date();
    $('dTime').textContent = String(now.getHours()).padStart(2, '0') + ':' + String(now.getMinutes()).padStart(2, '0');
    const sel = $('dType');
    if (sel.options.length !== c.defectTypes.length) fill(sel, c.defectTypes.map((t) => [t, t]), sel.value);
    renderPhotos();
  }

  function renderPhotos() {
    const box = $('photos');
    box.querySelectorAll('.ph:not(.add)').forEach((el) => el.remove());
    state.photos.forEach((p, i) => {
      const el = document.createElement('div');
      el.className = 'ph';
      el.style.backgroundImage = 'url(' + p.url + ')';
      const x = document.createElement('button');
      x.className = 'x'; x.textContent = '×'; x.title = 'Bỏ ảnh';
      x.onclick = () => { URL.revokeObjectURL(p.url); state.photos.splice(i, 1); renderPhotos(); };
      el.appendChild(x);
      box.insertBefore(el, $('addPhoto'));
    });
    $('addPhoto').classList.toggle('hidden', state.photos.length >= 4);
  }

  function renderJobs() {
    const jobs = state.jobs;
    const pending = jobs.filter((j) => j.status === 'Pending' || j.status === 'Waiting').length;
    $('pendingBadge').textContent = pending;
    $('pendingBadge').classList.toggle('hidden', pending === 0);
    $('noJobs').classList.toggle('hidden', jobs.length > 0);

    const banners = $('sentBanners');
    banners.innerHTML = '';
    const last = jobs[0];
    if (last && last.status === 'Done') banners.appendChild(banner('b-ok', '✓ Đã lưu: ' + last.summary + ' · ' + last.line + '.'));
    const waiting = jobs.find((j) => j.status === 'Waiting');
    if (waiting) banners.appendChild(banner('b-wait', '⏳ ' + (waiting.message || 'Máy chủ đang bận.') + ' Phiếu sẽ tự ghi khi máy chủ rảnh.'));

    const list = $('jobs');
    list.innerHTML = '';
    for (const j of jobs) {
      const [dot, sign, text] = j.status === 'Done' ? ['d-ok', '✓', 'đã ghi'] : j.status === 'Failed' ? ['d-bad', '!', 'không ghi được: ' + (j.message || '')]
        : j.status === 'Waiting' ? ['d-wait', '…', 'đang chờ'] : ['d-wait', '…', 'đang ghi'];
      const it = document.createElement('div');
      it.className = 'it';
      it.innerHTML = '<div class="dot ' + dot + '">' + sign + '</div><div class="m"></div><div class="tm"></div>';
      it.querySelector('.m').textContent = j.summary;
      const s = document.createElement('div');
      s.className = 's'; s.textContent = j.line + ' · ' + text;
      it.querySelector('.m').appendChild(s);
      it.querySelector('.tm').textContent = new Date(j.createdAt).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
      list.appendChild(it);
    }
  }

  function banner(cls, text) {
    const el = document.createElement('div');
    el.className = 'banner ' + cls;
    el.textContent = text;
    return el;
  }

  // ---------- Gửi ----------
  async function submit(button, action) {
    button.disabled = true;
    try {
      const job = await action();
      state.mine.add(job.id);
      toast('Đã gửi. Đang lưu…');
      await loadJobs();
      schedulePoll(1000);
      return true;
    } catch (e) {
      toast(e.message, true);
      return false;
    } finally {
      button.disabled = false;
    }
  }

  async function sendHour() {
    const day = state.context && state.context.line;
    if (!day) return;
    const raw = $('qty').value.trim();
    if (raw === '') { toast('Chưa nhập số sản phẩm.', true); return; }
    const quantity = Number(raw);
    if (!Number.isFinite(quantity) || quantity < 0) { toast('Số sản phẩm không hợp lệ.', true); return; }
    const hour = state.hour;
    const ok = await submit($('sendHour'), () => api('/api/nhap/hourly', { method: 'POST', json: { line: day.line, hour, quantity } }));
    if (ok) {
      // Hiện ngay số vừa gửi, chuyển sang giờ kế tiếp còn trống.
      const h = day.hours.find((x) => x.number === hour);
      if (h) h.quantity = quantity;
      day.actual = day.hours.reduce((s, x) => s + (x.quantity || 0), 0);
      const next = day.hours.find((x) => x.number > hour && x.quantity == null && (!x.start || x.start <= nowTime()));
      if (next) state.hour = next.number;
      $('qty').dataset.hour = '';
      renderOutput();
    }
  }

  async function savePlan() {
    const day = state.context.line;
    const hourlyTarget = Number($('fTarget').value);
    if (!(hourlyTarget > 0)) { toast('Nhập mục tiêu mỗi giờ.', true); return; }
    const ok = await submit($('savePlan'), () => api('/api/nhap/plan', {
      method: 'POST', json: { line: day.line, productCode: $('fProduct').value, shiftCode: $('fShift').value, hourlyTarget }
    }));
    if (ok) hidePlanForm();
  }

  async function sendDefect() {
    const day = state.context && state.context.line;
    if (!day) return;
    const quantity = Number($('dQty').value);
    if (!(quantity > 0)) { toast('Nhập số lượng hàng lỗi.', true); return; }
    if (!$('dType').value) { toast('Chọn loại lỗi.', true); return; }
    const form = new FormData();
    form.append('line', day.line);
    form.append('defectType', $('dType').value);
    form.append('quantity', String(quantity));
    form.append('note', $('dNote').value.trim());
    state.photos.forEach((p, i) => form.append('photos', p.blob, 'anh' + (i + 1) + '.jpg'));
    const ok = await submit($('sendDefect'), () => api('/api/nhap/defect', { method: 'POST', body: form }));
    if (ok) {
      state.photos.forEach((p) => URL.revokeObjectURL(p.url));
      state.photos = [];
      $('dQty').value = 1;
      $('dNote').value = '';
      renderPhotos();
    }
  }

  function nowTime() {
    const d = new Date();
    return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':00';
  }

  // Ảnh điện thoại thường 3–5 MB: thu nhỏ còn cạnh dài 1600px, JPEG, trước khi gửi.
  async function shrink(file) {
    const url = URL.createObjectURL(file);
    try {
      const img = await new Promise((resolve, reject) => {
        const i = new Image();
        i.onload = () => resolve(i);
        i.onerror = reject;
        i.src = url;
      });
      const scale = Math.min(1, 1600 / Math.max(img.naturalWidth, img.naturalHeight));
      const canvas = document.createElement('canvas');
      canvas.width = Math.round(img.naturalWidth * scale);
      canvas.height = Math.round(img.naturalHeight * scale);
      canvas.getContext('2d').drawImage(img, 0, 0, canvas.width, canvas.height);
      return await new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.82));
    } finally {
      URL.revokeObjectURL(url);
    }
  }

  async function addPhotos(files) {
    for (const file of files) {
      if (state.photos.length >= 4) { toast('Tối đa 4 ảnh mỗi lần gửi.', true); break; }
      try {
        const blob = await shrink(file);
        state.photos.push({ blob, url: URL.createObjectURL(blob) });
      } catch {
        toast('Không đọc được ảnh này.', true);
      }
    }
    renderPhotos();
  }

  // ---------- Theo dõi trạng thái ----------
  let pollTimer;
  function schedulePoll(ms) {
    clearTimeout(pollTimer);
    pollTimer = setTimeout(poll, ms);
  }
  async function poll() {
    if (!state.token || document.hidden) { schedulePoll(5000); return; }
    await loadJobs();
    const busy = state.jobs.some((j) => j.status === 'Pending' || j.status === 'Waiting');
    schedulePoll(busy ? 2000 : 20000);
  }
  document.addEventListener('visibilitychange', () => {
    if (!document.hidden && state.token) { loadContext(); schedulePoll(200); }
  });
  // Đổi ngày hoặc người khác vừa nhập: đọc lại mỗi phút.
  setInterval(() => { if (state.token && !document.hidden && $('planForm').classList.contains('hidden')) loadContext(); }, 60000);

  // ---------- Sự kiện ----------
  function selectTab(tab) {
    state.tab = tab;
    document.querySelectorAll('.tabs button').forEach((b) => b.classList.toggle('on', b.dataset.tab === tab));
    for (const t of ['output', 'defect', 'sent']) $('tab-' + t).classList.toggle('hidden', t !== tab);
    $('lineBlock').classList.toggle('hidden', tab === 'sent');
    if (tab === 'defect' && state.context) renderDefect();
    if (tab === 'sent') loadJobs();
  }

  document.querySelectorAll('.tabs button').forEach((b) => b.onclick = () => selectTab(b.dataset.tab));
  document.querySelectorAll('.btnr').forEach((b) => b.onclick = () => {
    const input = $(b.dataset.for);
    const min = Number(input.min || 0);
    input.value = Math.max(min, (Number(input.value) || 0) + Number(b.dataset.step));
    if (input.id === 'qty') updateHint();
  });
  $('qty').addEventListener('input', updateHint);
  $('loginBtn').onclick = login;
  $('pin').addEventListener('keydown', (e) => { if (e.key === 'Enter') login(); });
  $('who').onclick = () => { if (confirm('Đăng xuất khỏi máy này?')) logout(); };
  $('editPlan').onclick = () => showPlanForm('');
  $('cancelPlan').onclick = hidePlanForm;
  $('savePlan').onclick = savePlan;
  $('sendHour').onclick = sendHour;
  $('sendDefect').onclick = sendDefect;
  $('photoInput').addEventListener('change', (e) => { addPhotos(Array.from(e.target.files || [])); e.target.value = ''; });

  if (state.token) start().then(() => schedulePoll(20000)); else showLogin();
})();
