/* Màn hình TV chạy trong trình duyệt. Nhận dữ liệu từ máy chủ Display Board qua WebSocket,
   xoay các màn hình theo playlist của TV và vẽ lại giống bản WPF (thiết kế 1920×1080).
   Viết theo cú pháp cũ (không dùng ?. và ??) để chạy được trên trình duyệt của Smart TV. */
(function () {
  'use strict';

  // ---------- Icon (lưới 24×24, giống Controls/IconGeometries.cs) ----------
  var ICONS = {
    factory: 'M2,22 L2,10 L8,14 L8,10 L14,14 L14,3 L18,3 L18,14 L22,14 L22,22 Z M5,17 H8 V19 H5 Z M11,17 H14 V19 H11 Z M17,17 H20 V19 H17 Z',
    paint: 'M3,3 H18 V9 H3 Z M18,5 H21 V12 H12 V15 H10 V10 H19 V7 H18 Z M9.5,15 H12.5 V22 H9.5 Z',
    box: 'M12,2 L21,6.5 L21,17.5 L12,22 L3,17.5 L3,6.5 Z M12,4.2 L6,7.2 L12,10.2 L18,7.2 Z M11,12 L5,9 V16.3 L11,19.3 Z M13,12 V19.3 L19,16.3 V9 Z',
    search: 'M10,2 A8,8 0 1 1 9.99,2 Z M10,5 A5,5 0 1 0 10.01,5 Z M15.2,13.8 L22,20.6 L20.6,22 L13.8,15.2 Z',
    trophy: 'M6,2 H18 V4 H22 V8 A4,4 0 0 1 18,12 A6,6 0 0 1 13,15 V18 H17 V22 H7 V18 H11 V15 A6,6 0 0 1 6,12 A4,4 0 0 1 2,8 V4 H6 Z M4,6 V8 A2,2 0 0 0 6,10 V6 Z M18,6 V10 A2,2 0 0 0 20,8 V6 Z',
    people: 'M8,3 A3.5,3.5 0 1 1 7.99,3 Z M1,20 A7,7 0 0 1 15,20 V21 H1 Z M17,5 A3,3 0 1 1 16.99,5 Z M16.5,13 A6,6 0 0 1 23,19 V21 H17 V20 A8.5,8.5 0 0 0 14.8,13.3 A6,6 0 0 1 16.5,13 Z',
    chart: 'M2,20 H22 V22 H2 Z M3.5,12 H7.5 V19 H3.5 Z M10,7 H14 V19 H10 Z M16.5,3 H20.5 V19 H16.5 Z',
    trend: 'M2,20 H22 V22 H2 Z M2,17 L8,10 L12,14 L20,5 L21.5,6.4 L12,17 L8,13 L3.5,18.3 Z',
    star: 'M12,1.5 L15.1,8 L22.2,8.9 L17,13.8 L18.3,20.9 L12,17.5 L5.7,20.9 L7,13.8 L1.8,8.9 L8.9,8 Z',
    crown: 'M2,7 L7,11.5 L12,3.5 L17,11.5 L22,7 L20,19 H4 Z M4,20.5 H20 V22.5 H4 Z',
    warning: 'M12,1.5 L23.5,21.5 H0.5 Z M10.8,8.5 V15 H13.2 V8.5 Z M10.8,16.8 V19.2 H13.2 V16.8 Z',
    list: 'M2,4 H5 V7 H2 Z M7,4 H22 V7 H7 Z M2,10.5 H5 V13.5 H2 Z M7,10.5 H22 V13.5 H7 Z M2,17 H5 V20 H2 Z M7,17 H22 V20 H7 Z',
    megaphone: 'M3,9 H8 L18,3 V21 L8,15 H7 L8.5,21 H5.5 L4,15 H3 Z M20,9 H23 V15 H20 Z',
    clock: 'M12,1 A11,11 0 1 1 11.99,1 Z M12,4 A8,8 0 1 0 12.01,4 Z M11,6 H13 V11.4 L17,13.8 L16,15.5 L11,12.6 Z',
    check: 'M12,1 A11,11 0 1 1 11.99,1 Z M10.2,15.6 L6.6,12 L5.2,13.4 L10.2,18.4 L18.8,9.8 L17.4,8.4 Z',
    tool: 'M21,6.5 A5.5,5.5 0 0 1 13.4,11.6 L5,20 A2.1,2.1 0 0 1 2,17 L10.4,8.6 A5.5,5.5 0 0 1 17.5,1 L14,4.5 L15,9 L19.5,10 Z',
    truck: 'M1,5 H15 V16 H1 Z M15,9 H19.5 L23,12.5 V16 H15 Z M5,15 A2.5,2.5 0 1 1 4.99,15 Z M18,15 A2.5,2.5 0 1 1 17.99,15 Z'
  };
  ICONS.gear = (function () {
    var d = '', teeth = 8;
    for (var i = 0; i < teeth * 4; i++) {
      var a = Math.PI * 2 * i / (teeth * 4), r = (i % 4) < 2 ? 11 : 8.5;
      d += (i === 0 ? 'M' : 'L') + (12 + r * Math.cos(a)).toFixed(2) + ',' + (12 + r * Math.sin(a)).toFixed(2) + ' ';
    }
    return d + 'Z M15.5,12 A3.5,3.5 0 1 0 15.5,12.01 Z';
  })();

  function icon(name, cls, style) {
    var d = ICONS[String(name || '').trim().toLowerCase()] || ICONS.star;
    return '<svg class="' + (cls || '') + '"' + (style ? ' style="' + style + '"' : '') +
      ' viewBox="0 0 24 24"><path fill-rule="evenodd" d="' + d + '"/></svg>';
  }

  // ---------- Định dạng (giống ViewModels/Format.cs, kiểu vi-VN) ----------
  function isNum(v) { return typeof v === 'number' && !isNaN(v); }
  function roundAway(v) { return v < 0 ? -Math.round(-v) : Math.round(v); }
  function num(v) {
    if (!isNum(v)) return '—';
    var n = roundAway(v), s = String(Math.abs(n)).replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    return (n < 0 ? '-' : '') + s;
  }
  function pct(v) { return isNum(v) ? roundAway(v) + '%' : '—'; }
  // Tỷ lệ lỗi nhỏ nên giữ 2 số lẻ: 0,42%.
  function pct2(v) { return isNum(v) ? String(Math.round(v * 100) / 100).replace('.', ',') + '%' : '—'; }
  function signed(v) { return !isNum(v) ? '—' : (v > 0 ? '+' + num(v) : num(v)); }
  function esc(s) {
    return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }
  function st(s) {
    switch (s) { case 'Met': return 'met'; case 'Near': return 'near'; case 'Behind': return 'behind'; default: return 'none'; }
  }
  function statusOf(rate) { return !isNum(rate) ? 'None' : rate >= 100 ? 'Met' : rate >= 90 ? 'Near' : 'Behind'; }
  function statusText(s) { return s === 'Met' ? 'Đạt' : s === 'Near' ? 'Gần đạt' : s === 'Behind' ? 'Chậm' : '—'; }
  var STATUS_HEX = { met: '#22C55E', near: '#F5B301', behind: '#EF4444', none: '#5B6B85' };
  function frac(a, t) { return t > 0 ? Math.max(0, Math.min(1, (a || 0) / t)) : 0; }
  function color(hex) { return /^#[0-9a-fA-F]{3,8}$/.test(hex || '') ? hex : '#4682B4'; }
  function dmy(iso) { var p = String(iso || '').split('-'); return p.length === 3 ? p[2].substr(0, 2) + '/' + p[1] + '/' + p[0] : ''; }
  function dm(iso) { return dmy(iso).substr(0, 5); }
  function title(p) { return p.line ? p.line + ' · ' + p.displayName : p.displayName; }
  function bar(fraction, status, h, extra) {
    return '<div class="bar" style="height:' + h + 'px;' + (extra || '') + '"><i class="bg-' + st(status) + '" style="width:' +
      (fraction > 0 ? (fraction * 100).toFixed(2) + '%;min-width:' + h + 'px' : '0') + '"></i></div>';
  }
  var DAYS = ['Chủ Nhật', 'Thứ Hai', 'Thứ Ba', 'Thứ Tư', 'Thứ Năm', 'Thứ Sáu', 'Thứ Bảy'];

  // ---------- Các màn hình (giống ViewModels/Displays/*) ----------
  var VIEWS = {};

  VIEWS.overview = {
    title: function () { return 'SẢN LƯỢNG HÔM NAY'; }, icon: 'clock',
    hasContent: function () { return true; },
    render: function (s) {
      var m = s.summary, unit = esc(s.unit), rate = isNum(m.dailyRate) ? m.dailyRate : 0;
      var C = 2 * Math.PI * 44, f = Math.max(0, Math.min(1, rate / 100));
      var ring = '<svg viewBox="0 0 100 100"><circle cx="50" cy="50" r="44" fill="none" stroke="rgba(255,255,255,.24)" stroke-width="10"/>' +
        (f > 0 ? '<circle cx="50" cy="50" r="44" fill="none" stroke="' + STATUS_HEX[st(m.dailyStatus)] + '" stroke-width="10" stroke-linecap="round"' +
          ' stroke-dasharray="' + (f * C).toFixed(2) + ' ' + C.toFixed(2) + '" transform="rotate(-90 50 50)"/>' : '') + '</svg>';
      var h = '<div class="ov-kpis">' +
        '<div class="card" style="flex:1.4;margin-right:20px;background:#154A8A"><div class="lbl">THỰC TẾ HÔM NAY</div>' +
        '<div class="big" style="font-size:150px">' + num(m.dailyActual) + '</div><div class="card-title">' + unit + '</div></div>' +
        '<div class="card" style="flex:1;margin:0 10px"><div class="lbl">MỤC TIÊU HÔM NAY</div>' +
        '<div class="big">' + num(m.dailyTarget) + '</div><div class="card-title">' + unit + '</div></div>' +
        '<div class="card" style="flex:1;margin-left:20px;background:#0E3A2A;border-color:#1C6B45;justify-content:flex-start">' +
        '<div class="lbl">HOÀN THÀNH</div><div class="ov-ring">' + ring + '<div class="pct">' + pct(m.dailyRate) + '</div></div></div></div>';
      if (m.lineMonthCumulative > 0)
        h += '<div class="banner" style="background:var(--header)"><span class="dot bg-none"></span><span class="ell">Lũy kế tháng các chuyền: ' +
          num(m.lineMonthCumulative) + ' ' + unit + (isNum(m.hourlyProgress) ? ' · Tiến độ theo giờ: ' + pct(m.hourlyProgress) : '') + '</span></div>';
      if (m.carriedShortfall > 0)
        h += '<div class="banner warn">' + icon('warning') + '<span class="ell">Ngày ' + dm(m.carriedFromDate) + ' còn thiếu ' +
          num(m.carriedShortfall) + ' ' + unit + ' (' + m.carriedProductCount + ' chuyền), cần bù</span></div>';
      var max = 0;
      s.products.forEach(function (p) { max = Math.max(max, p.dailyActual || 0, p.dailyTarget || 0); });
      // Cột cao nhất chiếm 80% khung, chừa chỗ cho số phía trên.
      var bars = s.products.map(function (p) {
        var hgt = max > 0 ? (p.dailyActual || 0) / max * 80 : 0;
        var tgt = max > 0 ? (p.dailyTarget || 0) / max * 80 : 0;
        var c = p.dailyStatus === 'None' ? color(p.color) : STATUS_HEX[st(p.dailyStatus)];
        return '<div class="col"><div class="plot">' +
          '<div class="fill" style="height:' + hgt.toFixed(2) + '%;background:' + c + '"></div>' +
          '<div class="val" style="bottom:' + hgt.toFixed(2) + '%">' + num(p.dailyActual) + '</div>' +
          (p.dailyTarget > 0 ? '<div class="target" style="bottom:' + tgt.toFixed(2) + '%"></div>' : '') +
          '</div><div class="name ell">' + esc(p.line) + '</div></div>';
      }).join('');
      h += '<div class="card ov-chart"><div class="head"><div class="card-title" style="color:#fff">SẢN LƯỢNG THEO CHUYỀN' +
        '<span style="font-size:24px;color:var(--text2)">&nbsp;&nbsp; ▬ mục tiêu</span></div>' +
        '<div class="card-title" style="font-weight:400">' + m.productCount + ' chuyền · ' + m.metCount + ' đạt · ' + m.notMetCount + ' chưa đạt</div></div>' +
        '<div class="ov-bars">' + bars + '</div></div>';
      return h;
    }
  };

  VIEWS.ranking = {
    title: function () { return 'BẢNG XẾP HẠNG CHUYỀN'; }, icon: 'chart',
    hasContent: function (s) { return s.products.length > 0; },
    render: function (s) {
      var w = [110, 0, 250, 250, 190, 190, 250];
      var rows = sortBy(s.products, 'rank').slice(0, 10).map(function (p, i) {
        return '<div class="tr' + (i % 2 === 1 ? ' alt' : '') + '" style="height:76px">' +
          '<div class="col" style="width:110px;padding:0;display:flex;justify-content:center"><div class="b" style="width:56px;height:56px;border-radius:28px;' +
          'display:flex;align-items:center;justify-content:center;' + (p.rank <= 3 ? 'background:var(--near)' : '') + '">' + p.rank + '</div></div>' +
          '<div class="grow sb ell">' + esc(title(p)) + '</div>' +
          '<div class="col r" style="width:250px">' + num(p.dailyTarget) + '</div>' +
          '<div class="col r b" style="width:250px">' + num(p.dailyActual) + '</div>' +
          '<div class="col r b c-' + st(p.dailyStatus) + '" style="width:190px">' + pct(p.dailyRate) + '</div>' +
          '<div class="col r c-' + st(p.monthStatus) + '" style="width:190px">' + pct(p.monthRate) + '</div>' +
          '<div class="col" style="width:250px;padding:10px 20px"><div class="pill bg-' + st(p.dailyStatus) + '">' + statusText(p.dailyStatus) + '</div></div></div>';
      }).join('');
      return '<div class="tbl"><div class="th" style="height:70px"><div class="col c" style="width:110px;padding:0">#</div><div class="grow">Sản phẩm</div>' +
        '<div class="col r" style="width:' + w[2] + 'px">Mục tiêu</div><div class="col r" style="width:' + w[3] + 'px">Thực tế</div>' +
        '<div class="col r" style="width:' + w[4] + 'px">% ngày</div><div class="col r" style="width:' + w[5] + 'px">% tháng</div>' +
        '<div class="col c" style="width:' + w[6] + 'px;padding:0">Trạng thái</div></div>' + rows + '</div>';
    }
  };

  function cardGrid(count, cols, rows) {
    return 'grid-template-columns:repeat(' + cols + ',1fr);grid-template-rows:repeat(' + rows + ',1fr)';
  }

  VIEWS['product-progress'] = {
    title: function () { return 'TIẾN ĐỘ TỪNG CHUYỀN'; }, icon: 'factory',
    hasContent: function (s) { return s.products.length > 0; },
    paged: true, pageMs: 10000,
    pageCount: function (s) { return Math.max(1, Math.ceil(s.products.length / 9)); },
    render: function (s, page) {
      var items = s.products.slice(page * 9, page * 9 + 9), n = items.length;
      var g = n <= 1 ? [1, 1] : n === 2 ? [2, 1] : n <= 4 ? [2, 2] : n <= 6 ? [3, 2] : [3, 3];
      var cards = items.map(function (p) {
        var month = p.monthTarget > 0 ? '<div style="display:flex;align-items:flex-end;margin-top:14px"><div style="flex:1;min-width:0">' +
          '<div class="t2" style="font-size:26px">Tháng: ' + num(p.monthCumulative) + ' / ' + num(p.monthTarget) + '</div>' +
          bar(frac(p.monthCumulative, p.monthTarget), p.monthStatus, 14, 'margin-top:6px') + '</div>' +
          '<div class="r c-' + st(p.monthStatus) + '" style="width:150px;font-size:32px">' + pct(p.monthRate) + '</div></div>' : '';
        return '<div class="pcard fit" style="border-bottom:8px solid ' + STATUS_HEX[st(p.dailyStatus)] + '"><div class="inner" style="width:520px">' +
          '<div style="display:flex;align-items:center"><span style="width:16px;height:44px;border-radius:4px;margin-right:16px;flex:none;background:' + color(p.color) + '"></span>' +
          '<span class="sb ell" style="font-size:42px">' + esc(title(p)) + '</span></div>' +
          '<div style="margin-top:4px;white-space:nowrap"><span class="b" style="font-size:86px">' + num(p.dailyActual) + '</span>' +
          '<span class="t2" style="font-size:42px;margin-left:14px">/' + num(p.dailyTarget) + '</span></div>' +
          '<div style="display:flex;align-items:center;margin-top:8px"><div style="flex:1">' + bar(frac(p.dailyActual, p.dailyTarget), p.dailyStatus, 34) + '</div>' +
          '<div class="b r c-' + st(p.dailyStatus) + '" style="width:150px;font-size:50px">' + pct(p.dailyRate) + '</div></div>' +
          (p.carriedShortfall > 0 ? '<div style="font-size:28px;margin-top:8px;color:var(--near)">Hôm trước thiếu: <b>' + num(p.carriedShortfall) + '</b></div>' : '') +
          month + '</div></div>';
      }).join('');
      var pc = this.pageCount(s);
      return '<div class="cards" style="margin-bottom:0;' + cardGrid(n, g[0], g[1]) + '">' + cards + '</div>' +
        '<div style="height:60px;flex:none"></div>' + (pc > 1 ? '<div class="page">Trang ' + (page + 1) + '/' + pc + '</div>' : '');
    }
  };

  VIEWS['top-products'] = {
    title: function (s) { return s.products.some(function (p) { return p.dailyStatus === 'Met'; }) ? 'CHUYỀN VƯỢT MỤC TIÊU' : 'CHUYỀN DẪN ĐẦU'; },
    icon: 'star', bg: '#0B2A1C', headerBg: '#0F3A26', iconColor: 'var(--met)',
    hasContent: function (s) { return s.products.some(function (p) { return isNum(p.dailyRate); }); },
    render: function (s, page, key) {
      var top = sortBy(s.products.filter(function (p) { return isNum(p.dailyRate); }), 'rank').slice(0, 5);
      var cards = top.map(function (p) {
        var neg = p.dailyTarget > 0 && p.dailyVariance < 0;
        return '<div class="tc"><div class="crown">' + (p.rank <= 3 ? icon('crown') : '<span class="b" style="font-size:64px">' + p.rank + '</span>') + '</div>' +
          '<div class="tile" style="background:' + color(p.color) + '"><span class="fitw">' + esc(p.productCode) + '</span>' +
          (p.imagePath ? '<img src="' + esc(withKey(p.imagePath, key)) + '" alt="" onerror="this.style.display=\'none\'">' : '') + '</div>' +
          '<div class="sb ell" style="font-size:38px">' + esc(p.displayName) + '</div>' +
          (p.line ? '<div class="t2 ell" style="font-size:28px">' + esc(p.line) + '</div>' : '') +
          '<div class="b" style="font-size:92px;margin-top:20px">' + num(p.dailyActual) + '</div>' +
          '<div class="b c-' + st(p.dailyStatus) + '" style="font-size:56px">' + pct(p.dailyRate) + '</div>' +
          '<div class="' + (neg ? 'c-behind' : 'c-met') + '" style="font-size:36px">' + signed(p.dailyVariance) + '</div></div>';
      });
      while (cards.length < 5) cards.push('<div style="flex:1;margin:14px"></div>');
      return '<div class="top">' + cards.join('') + '</div>';
    }
  };

  VIEWS['not-met'] = {
    title: function () { return 'CHUYỀN CHƯA ĐẠT'; }, icon: 'warning', bg: '#3B0F16', headerBg: '#5A1320', iconColor: 'var(--notmet)',
    hasContent: function (s) { return s.products.some(function (p) { return isNum(p.dailyRate); }); },
    render: function (s) {
      var notMet = sortBy(s.products.filter(function (p) { return p.dailyStatus === 'Near' || p.dailyStatus === 'Behind'; }), 'dailyRate');
      if (notMet.length === 0)
        return '<div class="allmet">' + icon('check') + '<div class="b" style="font-size:80px;margin-top:40px">Tất cả chuyền đã đạt mục tiêu</div>' +
          '<div class="t2" style="font-size:44px">Cảm ơn sự cố gắng của mọi người!</div></div>';
      var shown = notMet.length > 7 ? 6 : notMet.length;
      var rows = notMet.slice(0, shown).map(function (p, i) {
        return '<div class="tr" style="height:80px;border-bottom:2px solid #5A1320">' +
          '<div class="col c" style="width:100px;padding:0">' + (i + 1) + '</div><div class="grow sb ell">' + esc(title(p)) + '</div>' +
          '<div class="col r" style="width:230px">' + num(p.dailyTarget) + '</div>' +
          '<div class="col r b" style="width:230px">' + num(p.dailyActual) + '</div>' +
          '<div class="col r b" style="width:170px;color:' + STATUS_HEX[st(p.dailyStatus)] + '">' + pct(p.dailyRate) + '</div>' +
          '<div class="col r b" style="width:230px;color:#FF8A8A">' + signed(p.dailyVariance) + '</div>' +
          '<div class="col r b" style="width:240px;color:var(--near)">' + (p.carriedShortfall > 0 ? num(p.carriedShortfall) : '') + '</div>' +
          '<div class="col r c-' + st(p.monthStatus) + '" style="width:180px;padding-right:20px">' + pct(p.monthRate) + '</div></div>';
      }).join('');
      var behindMonth = s.products.filter(function (p) { return p.monthRemaining > 0; }).length;
      return '<div class="tbl" style="flex:1"><div class="th" style="height:70px;background:#5A1320"><div class="col c" style="width:100px;padding:0">#</div>' +
        '<div class="grow">Sản phẩm</div><div class="col r" style="width:230px">Mục tiêu</div><div class="col r" style="width:230px">Thực tế</div>' +
        '<div class="col r" style="width:170px">%</div><div class="col r" style="width:230px">Chênh lệch</div>' +
        '<div class="col r" style="width:240px">Thiếu hôm trước</div><div class="col r" style="width:180px;padding-right:20px">% tháng</div></div>' + rows +
        (notMet.length > shown ? '<div class="t2" style="font-size:34px;margin:16px 0 0 40px">+' + (notMet.length - shown) + ' chuyền khác</div>' : '') + '</div>' +
        '<div class="alert">' + icon('warning') + '<div style="margin-left:30px;min-width:0">' +
        '<div class="b" style="font-size:46px;color:#FFB4B4">Còn ' + notMet.length + ' chuyền chưa đạt mục tiêu hôm nay</div>' +
        '<div style="font-size:32px">Hãy cùng cố gắng để hoàn thành kế hoạch hôm nay!</div>' +
        (behindMonth > 0 ? '<div class="t2" style="font-size:28px;margin-top:6px">' + behindMonth + ' chuyền đang thiếu so với mục tiêu tháng</div>' : '') +
        '</div></div>';
    }
  };

  VIEWS.notice = {
    title: function (s, page) { return s.notices.length ? String(s.notices[page % s.notices.length].title || '').toUpperCase() : 'THÔNG BÁO'; },
    icon: 'megaphone', headerBg: 'rgba(15,36,71,.6)', bg: 'transparent',
    hasContent: function (s) { return s.notices.length > 0; },
    pageMs: 8000,
    pageCount: function (s) { return Math.max(1, s.notices.length); },
    background: function (s, page, key) {
      var n = s.notices.length ? s.notices[page % s.notices.length] : null;
      return '<div class="notice-bg"' + (n && n.backgroundImagePath ? ' style="background-image:url(\'' + withKey(n.backgroundImagePath, key).replace(/'/g, '%27') + '\')"' : '') +
        '></div><div class="notice-dim"></div>';
    },
    render: function (s, page) {
      var n = s.notices.length ? s.notices[page % s.notices.length] : null;
      var content = n ? n.content : (s.companyName || '');
      var slogans = (s.slogans || []).map(function (x) {
        return '<div>' + icon(x.icon) + '<p style="margin-top:16px">' + esc(x.line1) + '</p><p>' + esc(x.line2 || '') + '</p></div>';
      }).join('');
      return '<div class="notice-body fit"><div class="inner" style="max-width:1680px">' + esc(content) + '</div></div>' +
        (slogans ? '<div class="slogans">' + slogans + '</div>' : '');
    }
  };

  VIEWS.detail = {
    title: function () { return 'CHI TIẾT SẢN LƯỢNG'; }, icon: 'list',
    hasContent: function (s) { return s.products.length > 0; },
    paged: true, pageMs: 8000,
    pageCount: function (s) { return Math.max(1, Math.ceil(s.products.length / 10)); },
    render: function (s, page) {
      var W = [0, 110, 130, 160, 160, 110, 170, 180, 180, 170, 130];
      function cells(values, cls) {
        return values.map(function (v, i) {
          if (i === 0) return v;
          return '<div class="col r ' + (cls && cls[i] || '') + '" style="width:' + W[i] + 'px;padding-right:18px">' + v + '</div>';
        }).join('');
      }
      var head = cells(['<div class="grow">Chuyền · Mã hàng</div>', 'Giờ ca', 'MT / giờ', 'MT ngày', 'Thực tế', '%', 'Thiếu hôm trước', 'MT tháng', 'Lũy kế', 'Còn thiếu tháng', '% tháng']);
      var rows = s.products.slice(page * 10, page * 10 + 10).map(function (p, i) {
        return '<div class="tr' + ((page * 10 + i + 1) % 2 === 0 ? ' alt' : '') + '" style="height:66px;font-size:32px">' + cells([
          '<div class="grow" style="display:flex;align-items:center;min-width:0"><span style="width:10px;height:36px;border-radius:3px;margin-right:14px;flex:none;background:' +
            color(p.color) + '"></span><span class="sb ell">' + esc(title(p)) + '</span></div>',
          num(p.shiftHours), num(p.hourlyTarget), num(p.dailyTarget), num(p.dailyActual), pct(p.dailyRate),
          p.carriedShortfall > 0 ? num(p.carriedShortfall) : '', num(p.monthTarget), num(p.monthCumulative), num(p.monthRemaining), pct(p.monthRate)
        ], ['', '', '', '', 'b', 'b c-' + st(p.dailyStatus), 'c-near', '', '', p.monthRemaining > 0 ? 'c-behind' : 'c-met', 'c-' + st(p.monthStatus)]) + '</div>';
      }).join('');
      var m = s.summary;
      var total = cells(['<div class="grow b">TỔNG</div>', '', '', num(m.dailyTarget), num(m.dailyActual), pct(m.dailyRate),
        m.carriedShortfall > 0 ? num(m.carriedShortfall) : '', '—', num(m.lineMonthCumulative), '—', '—'],
        ['', 'b', 'b', 'b', 'b', 'b', 'b c-near', 'b', 'b', 'b', 'b']);
      var pc = this.pageCount(s);
      return '<div class="tbl" style="margin-top:24px"><div class="th" style="height:80px;font-size:26px;line-height:1.15">' + head + '</div>' + rows +
        '<div class="tr" style="height:72px;font-size:32px;background:var(--header);border-radius:0 0 10px 10px">' + total + '</div></div>' +
        (pc > 1 ? '<div class="page">Trang ' + (page + 1) + '/' + pc + '</div>' : '');
    }
  };

  VIEWS['month-progress'] = {
    title: function () { return 'TIẾN ĐỘ THÁNG'; }, icon: 'trend',
    hasContent: function (s) { return s.products.some(function (p) { return p.monthTarget > 0; }); },
    render: function (s) {
      var withMonth = sortBy(s.products.filter(function (p) { return p.monthTarget > 0; }), 'monthRate');
      var shown = withMonth.length > 10 ? 9 : withMonth.length;
      // V19 có thêm "cần làm mỗi ngày" và "thiếu tháng trước"; file V18 không có thì ẩn cột.
      var hasNeed = withMonth.some(function (p) { return isNum(p.neededPerDay); });
      var head = '<div class="t2" style="display:flex;align-items:flex-end;height:40px;font-size:24px">' +
        '<div style="flex:1"></div><div class="r" style="width:280px">Lũy kế / Mục tiêu</div><div class="r" style="width:140px">% tháng</div>' +
        (hasNeed ? '<div class="r" style="width:220px">Cần mỗi ngày</div>' : '') + '</div>';
      var rows = withMonth.slice(0, shown).map(function (p) {
        var prev = p.previousMonthShortfall > 0
          ? '<div class="ell" style="font-size:22px;color:var(--near);line-height:1.1">Tháng trước thiếu ' + num(p.previousMonthShortfall) + '</div>' : '';
        return '<div style="display:flex;align-items:center;height:80px;font-size:32px">' +
          '<div style="width:380px;min-width:0"><div class="sb ell">' + esc(title(p)) + '</div>' + prev + '</div>' +
          '<div style="flex:1;margin:0 20px 0 10px">' + bar(frac(p.monthCumulative, p.monthTarget), p.monthStatus, 26) + '</div>' +
          '<div class="r t2" style="width:280px;font-size:28px"><span style="color:#fff">' + num(p.monthCumulative) + '</span> / ' + num(p.monthTarget) + '</div>' +
          '<div class="r b c-' + st(p.monthStatus) + '" style="width:140px;font-size:34px">' + pct(p.monthRate) + '</div>' +
          (hasNeed ? '<div class="r b" style="width:220px;font-size:34px">' + (isNum(p.neededPerDay) ? num(p.neededPerDay) : '') + '</div>' : '') + '</div>';
      }).join('');
      var days = s.summary.workingDaysLeft;
      return '<div style="flex:1;display:flex;margin:30px 40px 36px;min-height:0">' +
        '<div class="card" style="width:600px;flex:none;margin-right:30px;display:flex;flex-direction:column;justify-content:center;text-align:center">' +
        '<div class="sb" style="font-size:40px">LŨY KẾ THÁNG</div><div class="b" style="font-size:120px;margin-top:40px">' + num(s.summary.lineMonthCumulative) + '</div>' +
        '<div class="t2" style="font-size:40px">' + esc(s.unit) + '</div><div class="t2" style="font-size:32px;margin-top:30px">Tổng 6 chuyền trong tháng</div>' +
        (isNum(days)
          ? '<div style="font-size:36px;margin-top:40px">Còn <b style="font-size:56px">' + num(days) + '</b> ngày làm việc</div>' +
            '<div class="t2" style="font-size:26px;margin-top:4px">tính cả hôm nay</div>'
          : '<div class="t2" style="font-size:28px;margin-top:8px">% tháng xem theo từng chuyền bên phải</div>') + '</div>' +
        '<div class="card" style="flex:1;min-width:0;padding-top:20px"><div style="display:flex;align-items:flex-end"><div class="card-title" style="color:#fff;flex:1">THEO TỪNG CHUYỀN</div></div>' + head + rows +
        (withMonth.length > shown ? '<div class="t2" style="font-size:28px;margin-top:10px">+' + (withMonth.length - shown) + ' chuyền khác</div>' : '') + '</div></div>';
    }
  };

  VIEWS.lines = {
    title: function () { return 'SO SÁNH CÁC CHUYỀN'; }, icon: 'factory',
    hasContent: function (s) { return distinct(s.products.map(function (p) { return p.line; })).length > 1; },
    render: function (s) {
      var n = s.products.length, cols = Math.max(1, Math.min(3, n)), rows = Math.max(1, Math.ceil(n / cols));
      var cards = s.products.map(function (p) {
        return '<div class="pcard fit" style="padding:24px 36px;border-bottom:10px solid ' + STATUS_HEX[st(p.dailyStatus)] + '"><div class="inner" style="width:520px">' +
          '<div style="display:flex;align-items:center"><span class="b ell" style="flex:1;font-size:48px">' + esc(p.line) + '</span>' +
          '<span class="b c-' + st(p.dailyStatus) + '" style="font-size:64px">' + pct(p.dailyRate) + '</span></div>' +
          '<div class="t2 ell" style="font-size:32px">' + esc(p.displayName) + '</div>' +
          '<div style="margin-top:6px;white-space:nowrap"><span class="b" style="font-size:80px">' + num(p.dailyActual) + '</span>' +
          '<span class="t2" style="font-size:40px;margin-left:14px">/' + num(p.dailyTarget) + '</span></div>' +
          bar(frac(p.dailyActual, p.dailyTarget), p.dailyStatus, 30, 'margin-top:8px') +
          '<div style="display:flex;margin-top:14px;font-size:30px"><span class="t2" style="flex:1">' + (isNum(p.hourlyProgress) ? 'Theo giờ: ' + pct(p.hourlyProgress) : '') + '</span>' +
          (p.monthTarget > 0 ? '<span><span class="t2">Tháng </span><b class="c-' + st(p.monthStatus) + '">' + pct(p.monthRate) + '</b></span>' : '') + '</div>' +
          (p.carriedShortfall > 0 ? '<div style="font-size:30px;margin-top:10px;color:var(--near)">Hôm trước thiếu: <b>' + num(p.carriedShortfall) + '</b></div>' : '') +
          '</div></div>';
      }).join('');
      return '<div class="cards" style="' + cardGrid(n, cols, rows) + '">' + cards + '</div>';
    }
  };

  VIEWS.hourly = {
    title: function () { return 'SẢN LƯỢNG THEO GIỜ'; }, icon: 'clock',
    hasContent: function (s) { return s.products.some(function (p) { return (p.hourly || []).some(isNum); }); },
    render: function (s) {
      var count = 0;
      s.products.forEach(function (p) {
        var last = 0, h = p.hourly || [];
        for (var i = h.length - 1; i >= 0; i--) if (isNum(h[i])) { last = i + 1; break; }
        count = Math.max(count, Math.ceil(p.shiftHours || 0), last);
      });
      count = Math.max(1, Math.min(12, count === 0 ? 8 : count));
      var heads = '', i;
      for (i = 1; i <= count; i++) heads += '<div class="c" style="font-size:26px">Giờ ' + i + '</div>';
      var rows = s.products.map(function (p, r) {
        var cells = '';
        for (var h = 0; h < count; h++) {
          var v = p.hourly && h < p.hourly.length ? p.hourly[h] : null;
          var stt = isNum(v) && p.hourlyTarget > 0 ? st(statusOf(v / p.hourlyTarget * 100)) : 'none';
          cells += '<div><div class="hcell" style="border-bottom-color:' + STATUS_HEX[stt] + ';color:' + (stt === 'none' ? '#fff' : STATUS_HEX[stt]) + '">' +
            (isNum(v) ? num(v) : '·') + '</div></div>';
        }
        return '<div class="tr' + (r % 2 === 1 ? ' alt' : '') + '" style="height:118px">' +
          '<div style="width:360px;flex:none;min-width:0"><div class="b ell" style="font-size:36px">' + esc(p.line) + '</div>' +
          '<div class="t2 ell" style="font-size:24px">' + esc(p.displayName) + '&nbsp;&nbsp;' + (p.hourlyTarget > 0 ? 'MT ' + num(p.hourlyTarget) + '/giờ' : '') + '</div></div>' +
          '<div class="hours">' + cells + '</div>' +
          '<div class="r b c-' + st(p.hourlyStatus) + '" style="width:200px;font-size:44px">' + pct(p.hourlyProgress) + '</div></div>';
      }).join('');
      return '<div class="tbl" style="flex:1"><div class="th" style="height:70px"><div style="width:360px;flex:none">Chuyền</div>' +
        '<div class="hours">' + heads + '</div><div class="r" style="width:200px">Tiến độ</div></div>' + rows + '</div>' +
        '<div class="t2" style="font-size:26px;margin:0 40px 28px">Màu ô: xanh ≥ mục tiêu giờ · vàng 90–99% · đỏ &lt; 90% · dấu chấm = chưa nhập. ' +
        'Tiến độ = % so với mục tiêu tới giờ đã nhập (Excel tính).</div>';
    }
  };

  // Chưa có ảnh (file V19, hoặc chưa ghi ảnh): bảng số lỗi từng chuyền.
  function defectTable(s) {
    // Chỉ các chuyền có kế hoạch; nhiều lỗi xếp trên, chưa nhập số lỗi xếp cuối.
    var list = s.products.filter(function (p) { return p.productCode; })
      .map(function (p, i) { return [p, i]; })
      .sort(function (a, b) {
        var x = a[0].defects, y = b[0].defects;
        if (!isNum(x) || !isNum(y)) return isNum(x) === isNum(y) ? a[1] - b[1] : (isNum(x) ? -1 : 1);
        return y - x || (b[0].defectRate || 0) - (a[0].defectRate || 0) || a[1] - b[1];
      }).map(function (x) { return x[0]; });
    var shown = list.length > 8 ? 7 : list.length, maxRate = 0;
    list.forEach(function (p) { if (isNum(p.defectRate)) maxRate = Math.max(maxRate, p.defectRate); });
    var rows = list.slice(0, shown).map(function (p, i) {
      var has = isNum(p.defects), c = 'c-' + st(p.defectStatus);
      return '<div class="tr' + (i % 2 === 1 ? ' alt' : '') + '" style="height:88px">' +
        '<div class="grow" style="display:flex;align-items:center;min-width:0"><span style="width:10px;height:40px;border-radius:3px;margin-right:16px;flex:none;background:' +
          color(p.color) + '"></span><span class="sb ell">' + esc(title(p)) + '</span></div>' +
        '<div class="col r" style="width:170px">' + num(p.dailyActual) + '</div>' +
        (has ? '<div class="col r b ' + c + '" style="width:170px">' + num(p.defects) + '</div>'
          : '<div class="col r t2" style="width:170px;font-size:26px;white-space:nowrap">chưa nhập</div>') +
        '<div class="col" style="width:200px">' + (isNum(p.defectRate) ? bar(maxRate > 0 ? p.defectRate / maxRate : 0, p.defectStatus, 20) : '') + '</div>' +
        '<div class="col r b ' + c + '" style="width:170px;padding-right:10px">' + pct2(p.defectRate) + '</div></div>';
    }).join('');
    var m = s.summary, withDefects = list.filter(function (p) { return p.defects > 0; }).length;
    var left = '<div class="card" style="width:500px;flex:none;margin-right:30px;display:flex;flex-direction:column;justify-content:center;text-align:center">' +
      '<div class="sb" style="font-size:40px">TỔNG SỐ LỖI</div>' +
      '<div class="b ' + (m.defects > 0 ? 'c-' + st(m.defectStatus) : '') + '" style="font-size:150px;line-height:1.15;margin-top:20px">' + num(m.defects) + '</div>' +
      '<div class="t2" style="font-size:40px">' + esc(s.unit) + '</div>' +
      '<div style="font-size:36px;margin-top:40px">Tỷ lệ lỗi chung <b class="c-' + st(m.defectStatus) + '" style="font-size:56px">' + pct2(m.defectRate) + '</b></div>' +
      '<div class="t2" style="font-size:28px;margin-top:6px">trên ' + num(m.dailyActual) + ' ' + esc(s.unit) + ' thực tế</div>' +
      '<div class="t2" style="font-size:28px;margin-top:30px">' + withDefects + ' chuyền có hàng lỗi</div></div>';
    return '<div style="flex:1;display:flex;margin:30px 40px 0;min-height:0">' + left +
      '<div style="flex:1;min-width:0"><div class="tbl" style="margin:0"><div class="th" style="height:70px">' +
      '<div class="grow">Chuyền · Mã hàng</div><div class="col r" style="width:170px">Thực tế</div><div class="col r" style="width:170px">Số lỗi</div>' +
      '<div class="col" style="width:200px"></div><div class="col r" style="width:170px;padding-right:10px">Tỷ lệ lỗi</div></div>' + rows +
      (list.length > shown ? '<div class="t2" style="font-size:30px;margin:14px 0 0 20px">+' + (list.length - shown) + ' chuyền khác</div>' : '') + '</div></div></div>' +
      '<div class="t2" style="font-size:26px;margin:16px 40px 28px">Màu tỷ lệ lỗi: xanh ≤ 1% · vàng 1–3% · đỏ &gt; 3%. Số lỗi và tỷ lệ lấy từ file Excel (sheet HIEN_THI).</div>';
  }

  // Thứ tự chuyền theo số lỗi (Excel đã cộng từ HANG_LOI), chưa nhập xếp cuối.
  function defectLines(s) {
    return s.products.filter(function (p) { return p.productCode; })
      .map(function (p, i) { return [p, i]; })
      .sort(function (a, b) {
        var x = a[0].defects, y = b[0].defects;
        if (!isNum(x) || !isNum(y)) return isNum(x) === isNum(y) ? a[1] - b[1] : (isNum(x) ? -1 : 1);
        return y - x || a[1] - b[1];
      }).map(function (x) { return x[0]; });
  }

  function hm(t) { return t ? String(t).substr(0, 5) : ''; }

  // Hàng lỗi có ảnh: 6 ảnh/trang kèm cột tổng bên trái; 4 hoặc 2 ảnh/trang thì ảnh chiếm cả màn hình, tổng ghi ở chân trang.
  function defectsView(per) {
    return {
      title: function () { return 'HÀNG LỖI HÔM NAY'; }, icon: 'search',
      hasContent: function (s) { return (s.defectPhotos || []).length > 0 || s.products.some(function (p) { return isNum(p.defects); }); },
      paged: true, pageMs: 10000,
      pageCount: function (s) { return Math.max(1, Math.ceil((s.defectPhotos || []).length / per)); },
      render: function (s, page, key) {
        var photos = s.defectPhotos || [];
        if (photos.length === 0) return defectTable(s);
        var m = s.summary, pc = this.pageCount(s), big = per < 6;
        var side = '';
        if (!big) {
          var lines = defectLines(s).slice(0, 7).map(function (p) {
            return '<div style="display:flex"><span class="ell" style="flex:1">' + esc(title(p)) + '</span>' +
              (isNum(p.defects) ? '<b class="c-' + st(p.defectStatus) + '" style="margin-left:16px">' + num(p.defects) + '</b>' : '<span class="t2" style="margin-left:16px">—</span>') + '</div>';
          }).join('');
          side = '<div style="width:420px;flex:none;margin-right:24px;display:flex;flex-direction:column">' +
            '<div class="card" style="text-align:center;padding:22px"><div class="sb" style="font-size:34px">TỔNG SỐ LỖI</div>' +
            '<div class="b ' + (m.defects > 0 ? 'c-' + st(m.defectStatus) : '') + '" style="font-size:120px;line-height:1.1">' + num(m.defects) + '</div>' +
            '<div class="t2" style="font-size:30px">' + esc(s.unit) + ' · tỷ lệ lỗi <b class="c-' + st(m.defectStatus) + '">' + pct2(m.defectRate) + '</b></div></div>' +
            '<div class="card" style="margin-top:20px;flex:1;padding:22px 26px;min-height:0;overflow:hidden"><div class="card-title" style="color:#fff;margin-bottom:10px">THEO CHUYỀN</div>' +
            '<div style="font-size:30px;line-height:2.05">' + lines + '</div></div></div>';
        }
        var cards = photos.slice(page * per, page * per + per).map(function (d) {
          var img = d.imagePath
            ? '<img src="' + esc(withKey(d.imagePath, key)) + '" alt="" style="width:100%;height:100%;object-fit:contain;background:#000">'
            : '<div class="t2" style="font-size:26px;text-align:center;padding:20px">' + icon('warning', '', 'width:64px;height:64px;fill:var(--near)') +
              '<div style="margin-top:10px">Không tìm thấy ảnh</div><div class="ell" style="font-size:22px">' + esc(d.imageFile) + '</div></div>';
          return '<div class="ph"><div class="img">' + img +
            (d.defectType ? '<span class="tag ell">' + esc(d.defectType) + '</span>' : '') +
            (isNum(d.quantity) ? '<span class="qty">×' + num(d.quantity) + '</span>' : '') + '</div>' +
            '<div class="cap"><span style="width:8px;height:30px;border-radius:3px;margin-right:12px;flex:none;align-self:center;background:' + color(d.color) + '"></span>' +
            '<span class="sb ell" style="flex:1">' + esc(d.line + (d.productCode ? ' · ' + d.productCode : '')) + '</span>' +
            '<span class="t2 tm">' + hm(d.time) + '</span></div></div>';
        }).join('');
        var grid = per === 2 ? 'grid-template-columns:repeat(2,1fr);grid-template-rows:1fr'
          : per === 4 ? 'grid-template-columns:repeat(2,1fr);grid-template-rows:repeat(2,1fr)' : '';
        var foot = big
          ? 'Tổng số lỗi <b class="c-' + st(m.defectStatus) + '">' + num(m.defects) + ' ' + esc(s.unit) + '</b> · tỷ lệ lỗi <b class="c-' + st(m.defectStatus) + '">' +
            pct2(m.defectRate) + '</b> · ' + photos.length + ' ảnh hàng lỗi hôm nay'
          : photos.length + ' ảnh hàng lỗi hôm nay · khung đỏ là loại lỗi, góc phải là số lượng';
        return '<div style="flex:1;display:flex;margin:26px 40px 0;min-height:0">' + side +
          '<div class="dgrid' + (big ? ' big' : '') + '" style="' + grid + '">' + cards + '</div></div>' +
          '<div class="t2" style="font-size:' + (big ? 30 : 24) + 'px;margin:14px 40px 22px;display:flex"><span style="flex:1">' + foot + '</span>' +
          (pc > 1 ? '<span>Trang ' + (page + 1) + '/' + pc + '</span>' : '') + '</div>';
      }
    };
  }
  VIEWS.defects = defectsView(6);
  VIEWS['defects-4'] = defectsView(4);
  VIEWS['defects-2'] = defectsView(2);

  function sortBy(list, key) {
    // Sắp ổn định, giá trị trống xếp đầu (giống OrderBy của C# với null).
    return list.map(function (x, i) { return [x, i]; }).sort(function (a, b) {
      var x = a[0][key], y = b[0][key];
      var nx = !isNum(x), ny = !isNum(y);
      if (nx || ny) return nx === ny ? a[1] - b[1] : (nx ? -1 : 1);
      return x - y || a[1] - b[1];
    }).map(function (x) { return x[0]; });
  }
  function distinct(arr) { var o = []; arr.forEach(function (x) { if (o.indexOf(x) < 0) o.push(x); }); return o; }

  // ---------- Kết nối máy chủ ----------
  var match = /\/tv\/(\d+)/.exec(location.pathname);
  var tvNumber = match ? parseInt(match[1], 10) : 1;
  var STORE = 'displayboard.';
  function load(k) { try { return localStorage.getItem(STORE + k); } catch (e) { return null; } }
  function save(k, v) { try { if (v == null) localStorage.removeItem(STORE + k); else localStorage.setItem(STORE + k, v); } catch (e) { /* bộ nhớ trình duyệt bị chặn */ } }

  var key = (function () {
    var m = /[?&]key=([^&]*)/.exec(location.search);
    if (m) { var k = decodeURIComponent(m[1]); save('key', k); return k; }
    return load('key') || '';
  })();
  function withKey(url, k) { return k ? url + (url.indexOf('?') < 0 ? '?' : '&') + 'key=' + encodeURIComponent(k) : url; }

  var stage = document.getElementById('stage');
  var overlay = document.getElementById('overlay');
  var badge = document.getElementById('badge');
  var state = null;           // trạng thái gần nhất từ máy chủ
  var version = null;
  var skewMs = 0, tzMinutes = 0; // đồng hồ theo giờ máy chủ, không theo giờ TV
  var socket = null, retryMs = 2000, connected = false;

  function showOverlay(html) { overlay.innerHTML = html; overlay.className = html ? 'show' : ''; }
  function showBadge(text) { badge.textContent = text || ''; badge.className = text ? 'show' : ''; }

  function applyState(next, fromCache) {
    if (!next || next.type !== 'state') return;
    if (!fromCache) {
      if (version && next.version !== version) { location.reload(); return; }
      version = next.version;
      var t = Date.parse(next.serverTime);
      if (!isNaN(t)) skewMs = t - Date.now();
      var tz = /([+-])(\d\d):(\d\d)$/.exec(next.serverTime || '');
      tzMinutes = tz ? (tz[1] === '-' ? -1 : 1) * (parseInt(tz[2], 10) * 60 + parseInt(tz[3], 10)) : 0;
      save('state.' + tvNumber, JSON.stringify(next));
    }
    var playlistChanged = !state || JSON.stringify(state.screen) !== JSON.stringify(next.screen);
    state = next;
    if (!next.screen) {
      stopRotation();
      stage.innerHTML = '';
      document.title = next.appName;
      showOverlay('Máy chủ chưa có TV số ' + tvNumber + '<small>Thêm TV ở ứng dụng Display Board trên máy chủ, hoặc chọn TV khác tại <a href="/" style="color:#7CC4FF">' +
        esc(location.host) + '</a></small>');
      return;
    }
    document.title = next.appName + ' · ' + next.screen.name;
    if (!next.data) {
      stopRotation();
      stage.innerHTML = '';
      showOverlay(next.status === 'NoFileSelected' ? 'Máy chủ chưa chọn file Excel<small>Mở Display Board trên máy chủ và chọn file dữ liệu.</small>'
        : 'Đang chờ dữ liệu từ máy chủ…' + (next.error ? '<small>' + esc(next.error) + '</small>' : ''));
      return;
    }
    showOverlay('');
    if (playlistChanged || !currentId) startRotation(); else render(false);
  }

  function fetchState(done) {
    var xhr = new XMLHttpRequest();
    xhr.open('GET', withKey('/api/state?tv=' + tvNumber, key));
    xhr.timeout = 8000;
    xhr.onload = function () {
      if (xhr.status === 401) { askKey(key ? 'Mã truy cập không đúng' : null); return; }
      if (xhr.status !== 200) { done(false); return; }
      try { applyState(JSON.parse(xhr.responseText)); } catch (e) { done(false); return; }
      done(true);
    };
    xhr.onerror = xhr.ontimeout = function () { done(false); };
    xhr.send();
  }

  function connect() {
    fetchState(function (ok) {
      if (!ok) { offline(); return; }
      try {
        socket = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + withKey('/ws?tv=' + tvNumber, key));
      } catch (e) { offline(); return; }
      socket.onopen = function () { connected = true; retryMs = 2000; showBadge(''); };
      socket.onmessage = function (e) { try { applyState(JSON.parse(e.data)); } catch (err) { /* bỏ qua tin hỏng */ } };
      socket.onclose = function () { socket = null; offline(); };
    });
  }

  function offline() {
    connected = false;
    if (!state) {
      var cached = load('state.' + tvNumber);
      if (cached) { try { applyState(JSON.parse(cached), true); } catch (e) { /* bỏ qua */ } }
    }
    if (state && state.data) showBadge('Mất kết nối máy chủ, đang thử lại… (hiển thị số liệu gần nhất)');
    else showOverlay('Không kết nối được máy chủ<small>Đang thử lại… Kiểm tra máy chủ Display Board đang chạy và cùng mạng LAN.</small>');
    setTimeout(connect, retryMs);
    retryMs = Math.min(retryMs * 2, 15000);
  }

  function askKey(message) {
    showOverlay('Nhập mã truy cập của máy chủ' + (message ? '<small style="color:#FF8A8A">' + esc(message) + '</small>' : '') +
      '<input id="keyInput" type="password" autocomplete="off"><button id="keyButton">Kết nối</button>');
    var input = document.getElementById('keyInput');
    function submit() { key = input.value.trim(); save('key', key); showOverlay(''); connect(); }
    document.getElementById('keyButton').onclick = submit;
    input.onkeydown = function (e) { if (e.keyCode === 13) submit(); };
    input.focus();
  }

  // ---------- Xoay màn hình (giống Core/Display/PlaylistRotator.cs) ----------
  var playlist = [], index = -1, currentId = null, page = 0, rotTimer = null, pageTimer = null;

  function stopRotation() { clearTimeout(rotTimer); clearInterval(pageTimer); currentId = null; }

  function startRotation() {
    clearTimeout(rotTimer);
    playlist = (state.screen.playlist || []).filter(function (p) { return VIEWS[p.viewId]; });
    if (playlist.length === 0) playlist = [{ viewId: 'overview' }];
    index = -1;
    advance();
  }

  function advance() {
    var snap = state && state.data, next = -1, id, ms;
    for (var step = 1; step <= playlist.length; step++) {
      var c = (index + step) % playlist.length;
      var v = VIEWS[playlist[c].viewId];
      if (snap ? v.hasContent(snap) : playlist[c].viewId === 'overview') { next = c; break; }
    }
    var defMs = state.screen.defaultSeconds * 1000;
    if (next < 0) { index = -1; id = 'overview'; ms = defMs; }
    else {
      index = next;
      var item = playlist[next], view = VIEWS[item.viewId];
      id = item.viewId;
      var configured = item.seconds > 0 ? item.seconds * 1000 : defMs;
      // Bảng nhiều trang được thêm thời gian để lật hết trang (Notice thì không, giống bản WPF).
      var required = view.paged ? view.pageMs * view.pageCount(snap) : 0;
      var maxMs = state.screen.maxPagedSeconds * 1000;
      ms = !required || required <= configured ? configured : (required < maxMs ? required : Math.max(maxMs, configured));
    }
    rotTimer = setTimeout(advance, ms);
    if (id !== currentId) show(id);
  }

  function show(id) {
    currentId = id;
    page = 0;
    clearInterval(pageTimer);
    var view = VIEWS[id];
    if (view.pageCount) {
      pageTimer = setInterval(function () {
        var count = state && state.data ? view.pageCount(state.data) : 1;
        if (count > 1) { page = (page + 1) % count; render(false); }
      }, view.pageMs);
    }
    render(true);
  }

  function render(animate) {
    if (!state || !state.data || !currentId) return;
    var s = state.data, view = VIEWS[currentId];
    if (view.pageCount && page >= view.pageCount(s)) page = 0;
    var m = s.summary;
    var note = m.isToday || m.productCount === 0 ? '' : 'Dữ liệu ngày ' + dmy(m.date);
    var html = (view.background ? view.background(s, page, key) : '') +
      '<div class="hdr" style="position:relative;' + (view.headerBg ? 'background:' + view.headerBg : '') + '">' +
      icon(view.icon, 'icon', view.iconColor ? 'fill:' + view.iconColor : '') +
      '<div class="ttl"><h1 class="ell">' + esc(view.title(s, page)) + '</h1>' + (note ? '<div class="note">' + note + '</div>' : '') + '</div>' +
      '<div class="clock"><div class="d js-date"></div><div class="t js-time"></div></div></div>' +
      view.render(s, page, key);
    var el = document.createElement('div');
    el.className = 'view' + (animate ? ' fade-in' : '');
    if (view.bg) el.style.background = view.bg;
    el.innerHTML = html;
    stage.innerHTML = '';
    stage.appendChild(el);
    fitAll(el);
    tick();
  }

  // Thu nhỏ nội dung thẻ cho vừa ô (giống Viewbox StretchDirection=DownOnly).
  function fitAll(root) {
    var boxes = root.querySelectorAll('.fit');
    for (var i = 0; i < boxes.length; i++) {
      var box = boxes[i], inner = box.querySelector('.inner');
      if (!inner) continue;
      var cs = window.getComputedStyle(box);
      var aw = box.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
      var ah = box.clientHeight - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom);
      var iw = inner.offsetWidth, ih = inner.offsetHeight;
      var k = Math.min(1, aw / iw, ah / ih);
      if (k > 0 && k < 1) {
        inner.style.transform = 'scale(' + k + ')';
        inner.style.margin = (-(ih * (1 - k)) / 2) + 'px ' + (-(iw * (1 - k)) / 2) + 'px';
      }
    }
    var tiles = root.querySelectorAll('.fitw');
    for (var j = 0; j < tiles.length; j++) {
      var t = tiles[j], size = 64;
      while (size > 16 && (t.scrollWidth > 240 || t.scrollHeight > 240)) { size -= 4; t.style.fontSize = size + 'px'; }
    }
  }

  function tick() {
    var d = new Date(Date.now() + skewMs + tzMinutes * 60000);
    var time = ('0' + d.getUTCHours()).slice(-2) + ':' + ('0' + d.getUTCMinutes()).slice(-2);
    var date = DAYS[d.getUTCDay()] + ', ' + ('0' + d.getUTCDate()).slice(-2) + '/' + ('0' + (d.getUTCMonth() + 1)).slice(-2) + '/' + d.getUTCFullYear();
    var t = stage.querySelectorAll('.js-time'), ds = stage.querySelectorAll('.js-date'), i;
    for (i = 0; i < t.length; i++) t[i].textContent = time;
    for (i = 0; i < ds.length; i++) ds[i].textContent = date;
  }

  // ---------- Co giãn 1920×1080 theo màn hình ----------
  function layout() {
    var k = Math.min(window.innerWidth / 1920, window.innerHeight / 1080);
    stage.style.transform = 'scale(' + k + ')';
    stage.style.left = ((window.innerWidth - 1920 * k) / 2) + 'px';
    stage.style.top = ((window.innerHeight - 1080 * k) / 2) + 'px';
  }
  window.addEventListener('resize', layout);
  layout();
  setInterval(tick, 1000);

  // Bấm vào màn hình để chiếu toàn màn hình; giữ màn hình luôn sáng nếu trình duyệt hỗ trợ.
  var hint = document.getElementById('hint');
  setTimeout(function () { hint.className = 'hide'; }, 8000);
  document.addEventListener('click', function (e) {
    if (e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'BUTTON')) return;
    var el = document.documentElement, req = el.requestFullscreen || el.webkitRequestFullscreen;
    if (req && !(document.fullscreenElement || document.webkitFullscreenElement)) { try { req.call(el); } catch (err) { /* không hỗ trợ */ } }
    hint.className = 'hide';
  });
  var cursorTimer = null;
  document.addEventListener('mousemove', function () {
    document.body.className = 'pointer';
    clearTimeout(cursorTimer);
    cursorTimer = setTimeout(function () { document.body.className = ''; }, 3000);
  });
  var wakeLock = null;
  function keepAwake() {
    if (!navigator.wakeLock || document.visibilityState !== 'visible') return;
    navigator.wakeLock.request('screen').then(function (l) { wakeLock = l; }).catch(function () { /* không được phép */ });
  }
  document.addEventListener('visibilitychange', keepAwake);
  keepAwake();

  // Dùng cho kiểm thử tự động.
  window.DisplayBoardTv = { views: Object.keys(VIEWS), show: function (id) { if (VIEWS[id]) { clearTimeout(rotTimer); show(id); } }, state: function () { return state; } };

  connect();
})();
