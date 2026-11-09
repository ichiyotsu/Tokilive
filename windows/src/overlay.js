(() => {
  const MINI = /[?&]mini=1/.test(location.search) || location.pathname === '/mini';
  if (MINI) document.body.classList.add('mini');
  document.title = MINI ? 'tokilive · 实时窗' : (/[?&]preview=1/.test(location.search) ? 'tokilive · 预览' : 'tokilive · 弹幕');
  const list = document.getElementById('list');
  let cfg = { show: {}, maxMessages: 30, lifetime: 30, showAvatar: true, showMedal: true, minGiftPrice: 0 };
  const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const GUARD = ['', '总督', '提督', '舰长'];
  let applyConfig = function ({ cfg: c, vars }) {
    cfg = c; const r = document.documentElement.style;
    for (const [k, v] of Object.entries(vars || {})) r.setProperty(k, v);
    // 圆角刻度：自动 = 1（胶囊）；自定义 px 时按单行卡片半高换算，所有圆角元素同步缩放
    r.setProperty('--rk', c.radius > 0 ? (c.radius / (c.fontSize * (0.62 * (c.design?.pad ?? 1) + 0.65))).toFixed(3) : 1);
    applyDesign(c); r.setProperty('--blur', c.blur + 'px'); r.setProperty('--alpha', c.opacity);
    r.setProperty('--fs', c.fontSize + 'px'); applyFonts(c); r.setProperty('--fw', c.fontWeight || 700);
    const mc = c.medalColors || {}; r.setProperty('--mc-bg', mc.bg || '#FF5FC8'); r.setProperty('--mc-fg', mc.fg || '#2A0A1E'); r.setProperty('--mc-lv-bg', mc.lvBg || '#4A0A30'); r.setProperty('--mc-lv-fg', mc.lvFg || '#FFFFFF');
    document.body.classList.toggle('transparent', c.themeMode === 'transparent'); r.setProperty('--ts', c.textShadow ?? .6); document.body.classList.toggle('medal-custom', c.medalStyle === 'custom');
    document.body.classList.toggle('reduce-motion', !!c.reduceMotion);
    document.body.classList.toggle('no-avatar', c.showAvatar !== true);
    document.body.classList.toggle('win-border', !!(MINI ? c.showLiveBorder : c.showWindowBorder));
    document.documentElement.style.setProperty('--win-accent', c.winAccent || '#6750A4');
    document.body.classList.toggle('input-on', !!c.danmakuInput && !MINI);
    const plan = window.bili?.speechPlan ? bili.speechPlan(c) : { speak: !!c.tts, cancel: !c.tts };
    if (plan.cancel && window.speechSynthesis) { speechSynthesis.cancel(); window.__ttsQueue = []; }
    document.body.classList.toggle('unlocked', !(MINI ? c.miniLocked : c.locked) && !!window.bili);
    trim(); miniStyle(vars); requestAnimationFrame(() => document.fonts.ready.then(() => measureMin(true)));
  }
  // 自定义设计：元素顺序 / 显隐 / 间距 / 自定义 CSS
  function applyDesign(c) { const d = c.design || {}, r = document.documentElement.style, b = document.body.classList;
    const o = d.order || ['name', 'medal', 'badge']; o.forEach((k, i) => r.setProperty('--o-' + k, i + 1));
    for (const k of ['name', 'medal', 'badge', 'verb', 'giftimg']) b.toggle('h-' + k, !!d.hide?.[k]);
    b.toggle('d-newline', !!d.newline);
    r.setProperty('--pad', (0.62 * (d.pad ?? 1)) + 'em'); r.setProperty('--gap', (0.55 * (d.gap ?? 1)) + 'em');
    const sp = d.spacing ?? 1; [['--s1', .18], ['--s2', .32], ['--s3', .48], ['--s4', .66]].forEach(([k, v]) => r.setProperty(k, (v * sp).toFixed(3) + 'em'));
    const el = document.getElementById('custom-css'); if (el && el.textContent !== (c.customCss || '')) el.textContent = c.customCss || ''; }
  // 按文字脚本分配字体：拉丁 → 英文字体；假名 → 日文字体（unicode-range 别名）；汉字 → 中文字体
  const KANA = 'U+3040-30FF,U+31F0-31FF,U+FF66-FF9F,U+3190-319F';
  const CJK_FB = ['Noto Sans SC', 'Noto Sans CJK SC', 'Microsoft YaHei', 'Microsoft YaHei UI', 'PingFang SC'];
  const JP_FB = ['Noto Sans JP', 'Noto Sans CJK JP', 'Yu Gothic', 'Yu Gothic UI', 'Meiryo', 'Hiragino Sans'];
  const qf = f => /^-apple-system|^system-ui$|^sans-serif$/.test(f) ? f : `"${String(f).replace(/["\\]/g, '')}"`;
  const locals = f => [f, f + ' Regular', f.replace(/\s+/g, '') + '-Regular', f.replace(/\s+/g, '')].map(n => `local("${n.replace(/["\\]/g, '')}")`).join(',');
  function applyFonts(c) {
    const latin = (c.fontFamily || 'Inter').trim(), zh = (c.cjkFont || '').trim(), jp = (c.jpFont || '').trim();
    const jpList = [jp, ...JP_FB].filter(Boolean);
    const B = window.BUNDLED_FONTS || {};
    let css = B[jp] ? B[jp].map(([w, f]) => `@font-face{font-family:"DM-JP";src:url(fonts/${f}) format("woff2");unicode-range:${KANA};font-weight:${w}}`).join('')
      : `@font-face{font-family:"DM-JP";src:${jpList.map(locals).join(',')};unicode-range:${KANA};font-weight:100 900}`;
    let el = document.getElementById('dm-fonts'); if (!el) { el = document.createElement('style'); el.id = 'dm-fonts'; document.head.appendChild(el); }
    el.textContent = css;
    const stack = [latin, 'Inter', 'DM-JP', zh, ...CJK_FB, jp, ...JP_FB, 'sans-serif'].filter(Boolean);
    document.documentElement.style.setProperty('--font', [...new Set(stack)].map(qf).join(','));
  }
  function avatar(m) {
    if (!cfg.showAvatar) return '';
    if (cfg.showAvatar !== true) return '';
    const ch = esc((m.uname || '?').replace(/\*/g, '').slice(0, 1) || '?');
    return m.face ? `<img class="av" src="${esc(/\.(jpe?g|png)$/i.test(m.face) ? m.face + '@96w_96h.webp' : m.face)}" onerror="this.outerHTML='<div class=av>${ch}</div>'">` : `<div class="av">${ch}</div>`;
  }
  const hex = n => '#' + (Number(n) >>> 0 & 0xffffff).toString(16).padStart(6, '0');
  function medal(m) {
    if (!cfg.showMedal || !m.medal || !m.medal.name || !m.medal.level) return '';
    let st = '';
    if (cfg.medalStyle === 'bili' && m.medal.color) { const c = hex(m.medal.color); st = ` style="--m-bg:${c};--m-fg:#fff;--m-lv-bg:color-mix(in srgb,${c} 45%,#000);--m-lv-fg:#fff"`; }
    return `<span class="medal"${st} title="${esc(m.medal.anchor || '')}">${esc(m.medal.name)}<span class="lv">lv.${esc(m.medal.level)}</span></span>`;
  }
  function chips(m) {
    let h = '';
    if (m.admin) h += `<span class="badge" title="房管">管</span>`;
    if (m.guard) h += `<span class="chip">${GUARD[m.guard]}</span>`;
    return medal(m) + h;
  }
  const pool = [];
  function render(m) {
    const d = pool.pop() || document.createElement('div'); d.removeAttribute('style');
    const name = `<span class="name">${esc(m.uname)}</span>`;
    // 统一结构：.head（用户名/粉丝牌/徽章）+ .content（内容）；宽度不足时由 flex-wrap 自动折行
    let cls = 'msg', head = name + chips(m), content = '';
    if (m.type === 'danmaku') {
      cls = m.emote ? 'msg em' : 'msg dm';
      content = m.emote ? `<img class="emote" src="${esc(m.emote.replace(/^http:/, 'https:'))}" alt="${esc(m.text)}" onerror="this.outerHTML='<span class=text>'+this.alt.replace(/</g,'&lt;')+'</span>'">` : `<span class="text">${esc(m.text)}</span>`;
    } else if (m.type === 'gift') {
      cls = 'msg gift'; head = name + medal(m);
      content = `<span class="verb">${esc(m.action || '投喂')}了</span> <span class="gname">${esc(m.gift)} ×${esc(m.num)}</span>${m.icon ? `<img class="gi" src="${esc(m.icon)}">` : ''}`;
    } else if (m.type === 'guard') {
      cls = 'msg guard'; head = name + medal(m);
      content = `<span class="verb">开通了</span> <span class="gname">${esc(m.gift)} ${esc(m.num || 1)} 个月</span>`;
    } else if (m.type === 'superchat') {
      cls = 'msg sc'; if (m.color && cfg.scBiliColor) d.style.setProperty('--sc', m.color);
      head = name + medal(m) + `<span class="price">+${esc(m.price)}</span>`; content = `<span class="text">${esc(m.text)}</span>`;
    } else if (['enter', 'follow', 'share', 'like'].includes(m.type)) {
      cls = 'msg enter ' + m.type; head = name + medal(m);
      const ic = { like: '👍', share: '↗' }[m.type]; if (ic) head = `<span class="ic">${ic}</span>` + head;
      content = `<span class="verb">${esc(m.action)}</span>`;
    } else return;
    d.className = cls;
    d.innerHTML = `${avatar(m)}<div class="head">${head}</div><div class="content">${content}</div>`;
    if (!MINI && d.querySelector('.medal')) newMedals.push(d);
    tightQ.push(d); if (!tq) tq = requestAnimationFrame(tighten);
    list.appendChild(d); if (newMedals.length && !mq) mq = requestAnimationFrame(() => { mq = 0; measureMin(false); });
    const lt = MINI ? (cfg.miniLifetime ?? 6) : cfg.lifetime;
    const life = (m.type === 'superchat' ? Math.max(lt, m.duration || 60) : lt) * 1000;
    if (lt > 0) setTimeout(() => fade(d), life);
    trim();
    const plan = window.bili?.speechPlan ? bili.speechPlan(cfg) : { speak: !!cfg.tts };
    if (plan.speak && !MINI && window.speechSynthesis && ['danmaku', 'gift', 'superchat', 'guard'].includes(m.type)) {
      const t = m.type === 'danmaku' ? `${m.uname}说：${m.text}` : m.type === 'gift' ? `感谢${m.uname}${m.action}的${m.num}个${m.gift}` : m.type === 'superchat' ? `${m.uname}的醒目留言：${m.text}` : `感谢${m.uname}开通${m.gift}`;
      const u = new SpeechSynthesisUtterance(t); u.lang = 'zh-CN'; u.rate = plan.rate || 1; u.pitch = plan.pitch || 1; u.volume = plan.volume ?? 1;
      const voice = plan.voice && speechSynthesis.getVoices().find(v => v.name === plan.voice); if (voice) u.voice = voice;
      speechSynthesis.speak(u);
    }
  }
  // 最小宽度 = 最宽粉丝牌 + 左右内边距（随字号动态更新）
  let newMedals = [], medalW = 0, minSent = 0, mq = 0;
  function measureMin(reset) {
    if (reset) { medalW = 0; const pr = document.createElement('div'); pr.className = 'msg'; pr.style.cssText = 'position:absolute;visibility:hidden;animation:none';
      pr.innerHTML = `<div class="head"><span class="medal">粉丝团<span class="lv">lv.40</span></span></div>`; list.appendChild(pr); medalW = pr.querySelector('.medal').offsetWidth; pr.remove(); }
    for (const d of newMedals.splice(0)) { const e = d.querySelector('.medal'); if (e && e.isConnected) medalW = Math.max(medalW, e.offsetWidth); }
    const cs = getComputedStyle(document.documentElement), fs = parseFloat(cs.getPropertyValue('--fs')) || 18;
    const w = Math.ceil(medalW + 2 * 0.62 * fs + 2 * 12 + 4);
    document.documentElement.style.setProperty('--min-card', (medalW + 2 * .62 * fs) + 'px');
    if (w !== minSent && window.bili?.minWidth && !MINI && !PREVIEW_) { minSent = w; bili.minWidth(w); }
  }
  const PREVIEW_ = /[?&]preview=1/.test(location.search);
  // 折行后收紧卡片宽度，使右侧留白与左侧一致（批量读写，避免逐条强制回流）
  let tightQ = [], tq = 0;
  function tighten() { tq = 0; const q = tightQ.splice(0).filter(d => d.isConnected);
    const ms = q.map(d => { const h = d.querySelector('.head'), c = d.querySelector('.content'); if (!h || !c) return null;
      const r = d.getBoundingClientRect(), hr = h.getBoundingClientRect(), cr = c.getBoundingClientRect(), pad = hr.left - r.left;
      if (cr.top <= hr.top + 1) return null;
      const span = el => { let w = 0; for (const k of el.children) { const b = k.getBoundingClientRect(); w = Math.max(w, b.right - r.left - pad); }
        const rg = document.createRange(); rg.selectNodeContents(el); for (const b of rg.getClientRects()) w = Math.max(w, b.right - r.left - pad); return w; };
      const hw = d.classList.contains('sc') ? [...h.children].reduce((a, k) => a + k.getBoundingClientRect().width, 0) + 12 : span(h);
      return Math.ceil(Math.max(hw, span(c)) + 2 * pad + 1); });
    q.forEach((d, i) => { if (ms[i]) d.style.width = ms[i] + 'px'; }); }
  let rzT; addEventListener('resize', () => { clearTimeout(rzT); rzT = setTimeout(() => { const all = [...list.children]; all.forEach(d => d.style.width = ''); tightQ.push(...all); if (!tq) tq = requestAnimationFrame(tighten); }, 60); });
  function fade(d) { if (!d.isConnected || d.classList.contains('out')) return; d.classList.add('out'); setTimeout(() => { d.remove(); d.classList.remove('out'); if (pool.length < 40) pool.push(d); }, 800); }
  function trim() { const items = [...list.children].filter(e => !e.classList.contains('out')); items.slice(0, Math.max(0, items.length - (MINI ? (cfg.miniMax || 5) : cfg.maxMessages))).forEach(fade); }
  let onMessage = function (m) {
    const light = ['enter', 'follow', 'share', 'like'].includes(m.type);
    if (MINI ? (!light || !cfg.miniPills) : (!light && !cfg.show[m.type])) return;
    if (m.type === 'gift' && m.coin !== 'gold' && cfg.minGiftPrice > 0) return;
    if (m.type === 'gift' && m.coin === 'gold' && m.price / 1000 < cfg.minGiftPrice) return;
    q.push(m); if (q.length > 60) q.splice(0, q.length - 60); if (!qr) qr = requestAnimationFrame(flush);
  }
  // 批量渲染：同一帧内到达的消息合并一次布局
  const q = []; let qr = 0; function flush() { qr = 0; const n = Math.min(q.length, cfg.maxMessages || 30); q.splice(0, q.length - n).length; for (const m of q.splice(0)) render(m); }
  // 无形的边缘缩放区（仅解锁时可用，无任何可见元素）
  document.querySelectorAll('#edges i').forEach(el => el.addEventListener('pointerdown', e => {
    if (!window.bili?.edgeStart) return; e.preventDefault(); el.setPointerCapture(e.pointerId); const sx = e.screenX, sy = e.screenY, d = el.dataset.d; bili.edgeStart();
    let raf = 0, lx = sx, ly = sy; const mv = ev => { lx = ev.screenX; ly = ev.screenY; if (!raf) raf = requestAnimationFrame(() => { raf = 0; bili.edgeMove(d, lx - sx, ly - sy); }); };
    el.addEventListener('pointermove', mv); el.addEventListener('pointerup', () => { el.removeEventListener('pointermove', mv); bili.edgeMove(d, lx - sx, ly - sy, true); }, { once: true }); }));
  const statsEl = document.createElement('div'); statsEl.id = 'stats';
  if (MINI) document.getElementById('frame').insertBefore(statsEl, document.getElementById('list'));
  const fmt = n => n == null ? '—' : typeof n === 'number' ? (n >= 100000 ? (n / 10000).toFixed(1).replace(/\.0$/, '') + '万' : String(n)) : String(n).replace(/人看过$/, '');
  let lastSt = {};
  // 相对亮度（WCAG）
  const lum = h => { const m = /^#?([0-9a-f]{6})$/i.exec(String(h).trim()); if (!m) return 1; const n = parseInt(m[1], 16);
    return [n >> 16, n >> 8 & 255, n & 255].map(c => { c /= 255; return c <= .03928 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4; }).reduce((a, c, i) => a + c * [.2126, .7152, .0722][i], 0); };
  function miniStyle(vars) {
    if (!MINI) return; const r = document.documentElement.style;
    const bg = cfg.miniBg || (vars || {})['--miniw-bg'] || getComputedStyle(document.documentElement).getPropertyValue('--miniw-bg').trim() || '#ffffff';
    const light = lum(bg) > .4;
    r.setProperty('--sw-bg', bg); r.setProperty('--sw-alpha', cfg.miniAlpha ?? .92);
    r.setProperty('--sw-line', cfg.miniLine || 'var(--miniw-line)'); r.setProperty('--sw-lw', (cfg.miniLineW ?? 3) + 'px');
    // 自动对比：浅底 → 黑色数字 + 白色带阴影标签；深底 → 白色数字 + 浅灰标签
    r.setProperty('--sw-num', cfg.miniText || (light ? '#111111' : '#FFFFFF'));
    r.setProperty('--sw-label', cfg.miniLabel || (light ? '#FFFFFF' : 'rgba(255,255,255,.72)'));
    r.setProperty('--sw-shadow', light ? '0 0 .18em rgba(0,0,0,.55),0 0 .5em rgba(0,0,0,.25)' : '0 0 .3em rgba(0,0,0,.6)');
    document.body.classList.toggle('pills', !!cfg.miniPills); layout();
  }
  function layout() { if (!MINI) return; const L = cfg.miniLayout === 'auto' ? (innerWidth / Math.max(1, innerHeight) > 1.8 ? 'h' : 'v') : (cfg.miniLayout || 'h');
    statsEl.className = 'sw-' + L; onStats(lastSt);
    const W = innerWidth, H = cfg.miniPills ? 60 : innerHeight; statsEl.style.fontSize = Math.max(11, Math.floor(L === 'h' ? Math.min(H * .42, W / 19.5) : Math.min(H / 4.2, W / 6.4))) + 'px'; }
  addEventListener('resize', layout);
  function onStats(st) {
    if (!MINI) return; lastSt = st || {};
    statsEl.style.display = cfg.miniStats === false ? 'none' : '';
    const v = statsEl.className === 'sw-v';
    const row = (l, n) => `<span class="st"><i>${l}</i><b>${fmt(n)}</b></span>`;
    statsEl.innerHTML = row(v ? '点赞' : '本场点赞', st.likes) + row('弹幕', st.danmaku) + row(v ? '在线' : '当前在线', st.online ?? st.watched);
  }
  onStats({});
  window.__stats = onStats;
  // 预览模式：静态展示全部 7 种卡片（设置页 / 截图用）
  const PREVIEW = /[?&]preview=1/.test(location.search);
  const DEMO = () => { const md = l => ({ name: '粉丝团', level: l, color: 2951253 }), u = '用户 ID';
    return [{ type: 'guard', uname: u, medal: md(22), gift: '舰长', num: 1 }, { type: 'superchat', uname: u, medal: md(24), price: 30, text: '输入的文本输入的文本输入的文本', color: '#2A60B2' },
      { type: 'danmaku', uname: u, medal: md(24), admin: 1, text: '输入的文本输入的文本输入的文本' }, { type: 'danmaku', uname: u, medal: md(22), text: '输入的文本' },
      { type: 'gift', uname: u, medal: md(22), action: '投喂', gift: '小礼物', num: 1 }, { type: 'danmaku', uname: u, medal: md(22), text: '[dog]', emote: 'https://i0.hdslb.com/bfs/live/4428c84e694fbf4e0ef6c06e958d9352c3582740.png' },
      { type: 'enter', uname: u, medal: md(22), action: '进入了直播间 ciallo~' }, ...(new URLSearchParams(location.search).get('jp') ? [{ type: 'danmaku', uname: 'さくら Sakura', medal: md(18), text: 'こんにちは！今日の配信も楽しみです ✨ Nice play 123' }] : [])]; };
  function renderDemo() { list.innerHTML = ''; const c = cfg; cfg = { ...c, lifetime: 0, maxMessages: 99, show: { danmaku: 1, gift: 1, superchat: 1, guard: 1 } }; DEMO().forEach(render); cfg = c; tightQ.length = 0; requestAnimationFrame(() => document.fonts.ready.then(() => { tightQ.push(...list.children); tighten(); })); [...list.children].forEach(e => e.style.animation = 'none'); }
  if (PREVIEW) { document.body.classList.add('preview'); const ac = applyConfig; applyConfig = x => { ac(x); renderDemo(); }; onMessage = () => {};
    addEventListener('message', e => { if (e.data?.vars) applyConfig(e.data); }); }
  window.__push = m => onMessage(m); window.__config = x => applyConfig(x);
  if (window.bili) {
    bili.on('config', x => applyConfig(x)); bili.on('message', m => onMessage(m)); 
    bili.on('stats', onStats); bili.getView().then(x => applyConfig(x));
  } else if (PREVIEW && parent !== window && !/^http/.test(location.protocol)) { document.body.classList.add('obs'); } else {
    document.body.classList.add('obs');
    const bg = new URLSearchParams(location.search).get('bg'); if (bg) document.body.style.background = `url(${bg}) center/cover`;
    const es = new EventSource('/events' + (MINI ? '?w=mini' : ''));
    es.addEventListener('stats', e => onStats(JSON.parse(e.data)));
    es.addEventListener('config', e => applyConfig(JSON.parse(e.data)));
    es.addEventListener('message', e => onMessage(JSON.parse(e.data)));
  }
  const say = document.getElementById('say');
  const sayMsg = document.getElementById('sayMsg');
  document.getElementById('composer')?.addEventListener('submit', async ev => {
    ev.preventDefault();
    if (!window.bili?.sendDanmaku) { sayMsg.textContent = '预览里不会发送。'; return; }
    sayMsg.textContent = '发送中…';
    const r = await bili.sendDanmaku(say.value);
    sayMsg.textContent = r.message || '';
    if (r.ok) say.value = '';
  });
  say?.addEventListener('keydown', ev => { if (ev.isComposing || ev.keyCode === 229) ev.stopPropagation(); });
  addEventListener('keydown', ev => {
    if (MINI || ev.isComposing) return;
    if (ev.key === '/' && document.activeElement !== say && cfg.danmakuHotkey === '/') {
      ev.preventDefault(); document.body.classList.add('input-temp'); say?.focus();
    }
  });
  if (window.bili?.on) bili.on('composer', () => { document.body.classList.add('input-temp'); say?.focus(); });
  async function adapt() {
    if (!cfg.adaptiveText || !window.bili?.sampleBackground) {
      document.body.classList.remove('adaptive-dark', 'adaptive-light');
      return;
    }
    const r = await bili.sampleBackground();
    const mode = r.ok ? r.text : (cfg.adaptiveManual === 'dark' ? 'dark' : 'light');
    document.body.classList.toggle('adaptive-dark', mode === 'dark');
    document.body.classList.toggle('adaptive-light', mode === 'light');
    if (!r.ok && sayMsg && !MINI) sayMsg.textContent = r.message || '';
  }
  setInterval(adapt, 2500);
})();
