const { app, BrowserWindow, Tray, Menu, ipcMain, globalShortcut, nativeImage, screen, nativeTheme, dialog, protocol, desktopCapturer } = require('electron');
app.setName('tokilive'); if (process.platform === 'win32') app.setAppUserModelId('com.ichiyotsu.tokilive');
if (process.argv.includes('--shots')) { app.disableHardwareAcceleration(); app.commandLine.appendSwitch('disable-gpu'); }
try {
  protocol.registerSchemesAsPrivileged([{ scheme: 'tokilive-bg', privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true, stream: true } }]);
} catch {}
const path = require('path'), fs = require('fs'), http = require('http'), { spawn, execFile } = require('child_process');
const { BiliLive } = require('./bili');
const { buildPalette } = require('./palette');
const { THEME_KEYS, themeExport, themeImport } = require('./theme-keys');
const { splitTheme, isAppDark } = require('./theme-split');
const { titleFor } = require('./titles');
const { applyCaptureFix } = require('./capture-win');
const { lookupOwnRoom } = require('./room');
const { visibilityActions } = require('./visibility');
const { routeOf, BROWSER_SOURCE_NOTE } = require('./obs-routes');
const { FIRST_UPGRADE_NOTE, pickUpdate, distributionKind, installPlan, portableScript, setupArgs, redact } = require('./update-core');
const { forgetSavedToken, fetchReleaseList, downloadVerified } = require('./update-run');
const { canSend, validateText, allowSend, formBody, interpretResponse } = require('./danmaku-send');
const { luminanceOutside, mapRect } = require('./adaptive');
const { classifyHotkey } = require('./hotkey-policy');
const { colorFromColorRef } = require('./accent-color');
const { taskbarDecision } = require('./taskbar-policy');
const { parseLrc, lineAt, DEMO_LRC } = require('./lyrics/lrc');
const { searchLyrics } = require('./lyrics/lrclib');
const { parseSession, sourceLabel, controlCommand } = require('./lyrics/smtc');

const DEFAULTS = {
  roomId: '', cookie: '', opacity: 0.82, blur: 16, radius: 0, fontSize: 18, fontFamily: 'Google Sans', cjkFont: 'Source Han Sans SC', jpFont: 'Source Han Sans SC', fontWeight: 700,
  medalColors: { bg: '#FF5FC8', fg: '#2A0A1E', lvBg: '#4A0A30', lvFg: '#FFFFFF' },
  medalStyle: 'unified', styleVersion: 6, themeMode: 'system', overrides: { danmaku: '', gift: '', guard: '', superchat: '', enter: '', mini: '' }, scBiliColor: false, textShadow: 0.6,
  seedColor: '#6750A4', show: { danmaku: true, gift: true, superchat: true, guard: true, enter: false },
  route: { enter: 'mini', follow: 'mini', share: 'mini', like: 'mini' }, miniVisible: true, miniLocked: false, miniBounds: null, miniLifetime: 6, miniMax: 5, miniStats: true, miniLayout: 'h', miniBg: '', miniAlpha: 0.92, miniText: '', miniLabel: '', miniLine: '', miniLineW: 3, miniPills: false,
  lifetime: 30, maxMessages: 30, locked: false, alwaysOnTop: true, material: 'none', hotkey: 'CommandOrControl+Shift+L',
  showAvatar: false, showMedal: true, minGiftPrice: 0, tts: false, ttsVoice: '', ttsRate: 1, ttsPitch: 1, ttsVolume: 1, obsServer: false, obsPort: 7788, bounds: null,
  customCss: '', design: { preset: 'default', order: ['name', 'medal', 'badge'], hide: {}, newline: false, pad: 1, gap: 1, spacing: 1 },
  appTheme: 'system', appAccent: '#6750A4', useSystemAccent: true, appBackground: '', appBgOpacity: 0.45, appMaterial: 'mica', sidebar: 'auto', overlayVisible: true,
  showWindowBorder: false, showLiveBorder: false, adaptiveText: false, adaptiveManual: 'light',
  danmakuInput: false, danmakuHotkey: '', experimentalGameTopmost: false, hideFromTaskbar: false,
  lyricsVisible: false, lyricsOffset: 0, lyricsDemo: false,
};
let cfgPath, cfg, overlay, mini, settingsWin, stats = {}, tray, live, sseClients = new Set(), lastStatus = '未连接';
let pendingCheck = null, pendingUpdate = null, lastDanmakuSent = 0, lyricCache = { key: '', hit: null }, systemAccent = '';
const SHOT_ARG = (() => { const i = process.argv.indexOf('--shots'); return i >= 0 ? (process.argv[i + 1] || '') : null; })();
function shotsDir() { if (SHOT_ARG == null) return null; return SHOT_ARG || path.join(app.getPath('temp'), 'tokilive-shots'); }

function load() { cfgPath = path.join(app.getPath('userData'), 'settings.json');
  // 从旧版（BiliDanmakuMD3）迁移设置
  try { const old = path.join(app.getPath('appData'), 'BiliDanmakuMD3', 'settings.json'); if (!fs.existsSync(cfgPath) && fs.existsSync(old)) { fs.mkdirSync(path.dirname(cfgPath), { recursive: true }); fs.copyFileSync(old, cfgPath); } } catch {}
  try { cfg = { ...DEFAULTS, ...JSON.parse(fs.readFileSync(cfgPath, 'utf8')) }; cfg.show = { ...DEFAULTS.show, ...cfg.show }; cfg.design = { ...DEFAULTS.design, ...cfg.design, hide: { ...(cfg.design?.hide || {}) } }; if (!cfg.jpFont) cfg.jpFont = DEFAULTS.jpFont; cfg.medalColors = { ...DEFAULTS.medalColors, ...cfg.medalColors }; cfg.route = { ...DEFAULTS.route, ...cfg.route }; cfg.overrides = { ...DEFAULTS.overrides, ...cfg.overrides };
    delete cfg.perfEnabled; delete cfg.perfStrength; delete cfg.perfWhitelist; delete cfg.githubClientId;
    const v0 = cfg.styleVersion || 1;
    if (v0 < 6) { cfg.radius = 0; cfg.styleVersion = 6; }
    if (v0 < 5) { cfg.themeMode = 'system'; delete cfg.cardStyle; delete cfg.dark; }
    if (v0 < 4) Object.assign(cfg, { fontFamily: DEFAULTS.fontFamily, cjkFont: DEFAULTS.cjkFont, jpFont: DEFAULTS.jpFont, fontWeight: 700 });
    if (v0 < 3) Object.assign(cfg, { showAvatar: false, radius: 22, fontSize: 18, opacity: 0.82, fontFamily: DEFAULTS.fontFamily, medalStyle: 'unified' });
    if (v0 < 7) cfg.styleVersion = 7;
  } catch { cfg = { ...DEFAULTS }; }
  try { forgetSavedToken(fs, path.join(app.getPath('userData'), 'github-auth.bin')); } catch {}
}
function save() { fs.mkdirSync(path.dirname(cfgPath), { recursive: true }); const copy = { ...cfg }; fs.writeFileSync(cfgPath, JSON.stringify(copy, null, 2)); }
function cardDark() { return splitTheme(cfg, { systemDark: nativeTheme.shouldUseDarkColors }).cardDark; }
async function view() {
  const split = splitTheme(cfg, { systemDark: nativeTheme.shouldUseDarkColors });
  const stored = { ...cfg, dark: split.cardDark, appDark: split.appDark, winAccent: systemAccent || cfg.appAccent || '#6750A4' };
  delete stored.cookie;
  return { cfg: stored, stored, vars: await buildPalette(cfg.seedColor, split.cardDark, cfg.overrides), app: { ...split, winAccent: stored.winAccent } };
}
// target: 'all' | 'main' | 'mini'
function broadcast(ch, data, target = 'all') {
  const s = `event: ${ch}\ndata: ${JSON.stringify(data)}\n\n`;
  if (target !== 'mini') overlay?.webContents.send(ch, data);
  if (target !== 'main') mini?.webContents.send(ch, data);
  if (target === 'all') settingsWin?.webContents.send(ch, data);
  for (const r of sseClients) if (target === 'all' || r.win === target) r.write(s);
}
const LIGHT = ['enter', 'follow', 'share', 'like'];
let statT; function bumpStats() { if (statT) return; statT = setTimeout(() => { statT = null; broadcast('stats', stats, 'mini'); }, 400); }
function routeMessage(m) {
  if (m.type === 'danmaku') { stats.danmaku = (stats.danmaku || 0) + 1; bumpStats(); }
  if (LIGHT.includes(m.type)) { const r = cfg.route[m.type] || 'mini'; if (r === 'off') return; if (r === 'main') return broadcast('message', { ...m, routed: true }, 'main'); return broadcast('message', m, 'mini'); }
  broadcast('message', m, 'main');
}
async function pushConfig() { broadcast('config', await view()); }
function status(s) { lastStatus = s; broadcast('status', s); updateTray(); }

function connect() {
  live?.stop(); live = null;
  if (!cfg.roomId) return status('未设置房间号');
  live = new BiliLive({ roomId: cfg.roomId, cookie: cfg.cookie });
  live.on('status', status);
  live.on('error', e => status('错误：' + e.message));
  live.on('popularity', p => broadcast('popularity', p));
  live.on('watched', t => broadcast('watched', t));
  stats = {};
  live.on('message', m => { if (m.face && m.face.includes('noface')) m.face = ''; routeMessage(m); });
  live.on('stats', st => { Object.assign(stats, st); broadcast('stats', stats, 'mini'); });
  live.start().catch(e => e.fatal ? status('连接失败：' + e.message) : live.scheduleRetry('连接失败：' + e.message));
}

function lockWin(w, locked) {
  if (!w) return;
  w.setIgnoreMouseEvents(locked, { forward: true }); w.setResizable(!locked); w.setMovable(!locked);
  const level = cfg.experimentalGameTopmost ? 'screen-saver' : (cfg.alwaysOnTop ? 'floating' : 'normal');
  w.setAlwaysOnTop(!!(cfg.alwaysOnTop || cfg.experimentalGameTopmost), level);
  if (process.platform === 'win32' && w.setBackgroundMaterial) { try { w.setBackgroundMaterial(cfg.material); } catch {} }
}
function applyVisibility() {
  const act = visibilityActions(cfg);
  if (!mainShown) return act;
  if (overlay && !overlay.isDestroyed()) {
    if (act.overlay === 'hide') { if (overlay.isVisible()) overlay.hide(); }
    else if (!overlay.isVisible()) overlay.showInactive();
  }
  if (mini && !mini.isDestroyed()) {
    if (act.mini === 'hide') { if (mini.isVisible()) mini.hide(); }
    else if (!mini.isVisible()) mini.showInactive();
  }
  return act;
}
function applyTaskbar() {
  const decision = taskbarDecision({ hideRequested: !!cfg.hideFromTaskbar, trayAvailable: !!tray });
  for (const w of [overlay, mini]) if (w && !w.isDestroyed()) w.setSkipTaskbar(decision.skipTaskbar);
  return decision;
}
let raiseT;
function applyRaise() {
  clearInterval(raiseT); raiseT = null;
  if (!cfg.experimentalGameTopmost) return;
  raiseT = setInterval(() => {
    for (const w of [overlay, mini]) if (w && !w.isDestroyed() && w.isVisible()) { try { w.moveTop(); } catch {} }
  }, 2000);
}
function applyLock() {
  lockWin(overlay, cfg.locked); lockWin(mini, cfg.miniLocked);
  applyTaskbar();
  applyRaise();
  applyVisibility();
  updateTray(); pushConfig();
}
// 全局快捷键：同时锁定/解锁两个窗口
function toggleLock() { const v = !(cfg.locked && cfg.miniLocked); cfg.locked = v; cfg.miniLocked = v; save(); applyLock(); }

function fitBounds(b, def) {
  if (b && !screen.getAllDisplays().some(d => { const a = d.workArea; return b.x < a.x + a.width - 60 && b.x + b.width > a.x + 60 && b.y >= a.y - 20 && b.y < a.y + a.height - 60; })) b = null;
  return b || def;
}
// 互动小窗尺寸约束（随布局变化）
const MINI_SIZE = { h: { min: [220, 40], max: [900, 120], def: [460, 60] }, v: { min: [100, 90], max: [280, 360], def: [150, 120] } };
const miniLay = () => cfg.miniLayout === 'v' ? 'v' : 'h';
function miniLimits(resetSize) { if (!mini) return; const L = MINI_SIZE[miniLay()], big = cfg.miniPills; const max = big ? [Math.max(L.max[0], 600), 800] : L.max;
  mini.setMinimumSize(...L.min); mini.setMaximumSize(...max);
  const b = mini.getBounds(); let [w, h] = resetSize ? L.def : [b.width, b.height];
  if (resetSize && big) h += 220;
  w = Math.min(Math.max(w, L.min[0]), max[0]); h = Math.min(Math.max(h, L.min[1]), max[1]);
  mini.setBounds(clampToScreen({ ...b, width: w, height: h })); cfg.miniBounds = mini.getBounds(); }
// 保证窗口完整位于某块屏幕的工作区内
function clampToScreen(b) { const a = screen.getDisplayMatching(b).workArea;
  const w = Math.min(b.width, a.width), h = Math.min(b.height, a.height);
  return { width: w, height: h, x: Math.min(Math.max(b.x, a.x), a.x + a.width - w), y: Math.min(Math.max(b.y, a.y), a.y + a.height - h) }; }
function bindTitle(win, title) {
  win.setTitle(title);
  win.on('page-title-updated', e => { e.preventDefault(); if (!win.isDestroyed()) win.setTitle(title); });
}
function makeWin(b, key, query, minW, minH, title) {
  const w = new BrowserWindow({ ...b, title, frame: false, transparent: true, backgroundColor: '#00000000', hasShadow: false, skipTaskbar: false, alwaysOnTop: cfg.alwaysOnTop, show: false,
    minWidth: minW, minHeight: minH, webPreferences: { preload: path.join(__dirname, 'preload.js'), sandbox: false, backgroundThrottling: false } });
  bindTitle(w, title);
  w.loadFile(path.join(__dirname, 'overlay.html'), { query });
  const fix = () => { w.setTitle(title); applyCaptureFix(w, title).catch(() => {}); };
  w.once('ready-to-show', fix);
  w.webContents.on('did-finish-load', fix);
  const rb = () => { let nb = w.getBounds(); if (key === 'miniBounds') { const c = clampToScreen(nb); if (c.x !== nb.x || c.y !== nb.y || c.width !== nb.width || c.height !== nb.height) { w.setBounds(c); nb = c; } } cfg[key] = nb; save(); };
  w.on('moved', rb); w.on('resized', rb);
  return w;
}
// 启动窗口：约 2 秒，期间后台加载各组件
let splash, splashT = 0, overlayReady = false, mainShown = false;
function createSplash() { const wa = screen.getPrimaryDisplay().workArea, w = 384, h = 256;
  splash = new BrowserWindow({ width: w, height: h, x: Math.round(wa.x + (wa.width - w) / 2), y: Math.round(wa.y + (wa.height - h) / 2), frame: false, transparent: true, resizable: false, movable: true,
    skipTaskbar: false, alwaysOnTop: true, show: false, hasShadow: false, title: titleFor('splash'), icon: path.join(__dirname, 'icon.png') });
  bindTitle(splash, titleFor('splash'));
  splash.loadFile(path.join(__dirname, 'splash.html')); splash.once('ready-to-show', () => { splash.show(); splashT = Date.now(); }); splashT = Date.now(); }
function maybeShowMain() { if (mainShown || !overlayReady) return; const wait = Math.max(0, (shotsDir() ? 0 : 2000) - (Date.now() - splashT));
  setTimeout(() => { if (mainShown) return; mainShown = true; applyVisibility(); if (shotsDir() || !cfg.roomId) openSettings();
    if (splash && !splash.isDestroyed()) { splash.webContents.executeJavaScript("document.body.classList.add('out')").catch(() => {}); setTimeout(() => splash && !splash.isDestroyed() && splash.close(), 320); } }, wait); }
function createOverlay() {
  const wa = screen.getPrimaryDisplay().workArea;
  overlay = makeWin(fitBounds(cfg.bounds, { width: 420, height: 640, x: wa.x + 40, y: wa.y + wa.height - 700 }), 'bounds', {}, 220, 160, titleFor('danmaku'));
  overlay.once('ready-to-show', () => { overlayReady = true; maybeShowMain(); });
  overlay.webContents.on('did-finish-load', () => { applyLock(); status(lastStatus); });
  mini = makeWin(fitBounds(cfg.miniBounds, { width: 460, height: 60, x: wa.x + wa.width - 500, y: wa.y + wa.height - 120 }), 'miniBounds', { mini: '1' }, 100, 40, titleFor('mini')); miniLimits(false); save();
  mini.webContents.on('did-finish-load', () => { applyLock(); mini.webContents.send('stats', stats); });
}
function applySettingsMaterial(win) {
  const dk = isAppDark(cfg, nativeTheme.shouldUseDarkColors);
  const m = cfg.appMaterial || 'mica';
  if (process.platform === 'win32' && m !== 'none' && win.setBackgroundMaterial) {
    try { win.setBackgroundMaterial(m); return; } catch {}
  }
  try { win.setBackgroundMaterial?.('none'); } catch {}
  try { win.setBackgroundColor(dk ? '#202020' : '#f3f3f3'); } catch {}
}
function applySettingsChrome() {
  if (!settingsWin || settingsWin.isDestroyed()) return;
  const dk = isAppDark(cfg, nativeTheme.shouldUseDarkColors);
  try { settingsWin.setTitleBarOverlay({ color: '#00000000', symbolColor: dk ? '#ffffff' : '#1b1b1b', height: 40 }); } catch {}
  applySettingsMaterial(settingsWin);
}
function openSettings() {
  if (settingsWin) return settingsWin.focus();
  const dk = isAppDark(cfg, nativeTheme.shouldUseDarkColors);
  settingsWin = new BrowserWindow({ width: 1180, height: 780, minWidth: 640, minHeight: 480, title: titleFor('settings'), autoHideMenuBar: true, show: false,
    backgroundColor: dk ? '#202020' : '#f3f3f3', titleBarStyle: 'hidden', titleBarOverlay: { color: '#00000000', symbolColor: dk ? '#ffffff' : '#1b1b1b', height: 40 }, icon: path.join(__dirname, 'icon.png'),
    webPreferences: { preload: path.join(__dirname, 'preload.js'), sandbox: false } });
  bindTitle(settingsWin, titleFor('settings'));
  settingsWin.loadFile(path.join(__dirname, 'settings.html'));
  settingsWin.webContents.on('console-message', (_e, _l, message) => { if (shotsDir()) console.log('PAGE', message); });
  settingsWin.once('ready-to-show', () => { applySettingsChrome(); settingsWin.show(); if (shotsDir()) runShots().catch(e => { console.error(redact(e.message)); app.exit(1); }); });
  settingsWin.on('closed', () => settingsWin = null);
}
async function runShots() {
  const SHOTS = shotsDir();
  fs.mkdirSync(SHOTS, { recursive: true });
  await new Promise(r => setTimeout(r, 800));
  const pages = ['conn', 'danmaku', 'live', 'lyrics', 'source', 'settings'];
  for (const p of pages) {
    await settingsWin.webContents.executeJavaScript(`document.querySelector('[data-p="${p}"]')?.click()`);
    await new Promise(r => setTimeout(r, 350));
    const img = await settingsWin.webContents.capturePage();
    fs.writeFileSync(path.join(SHOTS, `win-${p}.png`), img.toPNG());
  }
  await settingsWin.setBounds({ width: 860, height: 640 });
  await new Promise(r => setTimeout(r, 250));
  const probe = await settingsWin.webContents.executeJavaScript(`(() => { try { if (!window.__tokiSidebar) return 'missing '+typeof window.__tokiSidebar; window.__tokiSidebar.apply(); return 'ok '+document.body.className+' '+innerWidth; } catch (e) { return 'ERR '+e.stack; } })()`);
  console.log('SIDEBAR', probe);
  await new Promise(r => setTimeout(r, 200));
  fs.writeFileSync(path.join(SHOTS, 'win-compact.png'), (await settingsWin.webContents.capturePage()).toPNG());
  await settingsWin.setBounds({ width: 640, height: 700 });
  await new Promise(r => setTimeout(r, 250));
  await settingsWin.webContents.executeJavaScript('window.__tokiSidebar.open()');
  await new Promise(r => setTimeout(r, 250));
  fs.writeFileSync(path.join(SHOTS, 'win-narrow.png'), (await settingsWin.webContents.capturePage()).toPNG());
  app.quit();
}
function updateTray() {
  if (!tray) return;
  tray.setToolTip('tokilive - ' + lastStatus);
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: lastStatus, enabled: false }, { type: 'separator' },
    { label: '设置…', click: openSettings },
    { label: (cfg.locked && cfg.miniLocked) ? '解锁全部窗口（可移动）' : '锁定全部窗口（鼠标穿透）', click: toggleLock },
    { label: '锁定弹幕窗', type: 'checkbox', checked: cfg.locked, click: () => { cfg.locked = !cfg.locked; save(); applyLock(); } },
    { label: '锁定实时窗', type: 'checkbox', checked: cfg.miniLocked, click: () => { cfg.miniLocked = !cfg.miniLocked; save(); applyLock(); } },
    { label: '显示弹幕窗', type: 'checkbox', checked: cfg.overlayVisible !== false, click: () => { cfg.overlayVisible = cfg.overlayVisible === false; save(); applyLock(); } },
    { label: '显示实时窗', type: 'checkbox', checked: cfg.miniVisible !== false, click: () => { cfg.miniVisible = cfg.miniVisible === false; save(); applyLock(); } },
    { label: '置顶', type: 'checkbox', checked: cfg.alwaysOnTop, click: () => { cfg.alwaysOnTop = !cfg.alwaysOnTop; save(); applyLock(); } },
    { label: '重新连接', click: connect }, { type: 'separator' },
    { label: '退出', click: () => app.quit() }]));
}
function registerHotkey() {
  globalShortcut.unregisterAll();
  let ok = false; try { ok = globalShortcut.register(cfg.hotkey, toggleLock); } catch {}
  if (!ok) status('快捷键 ' + cfg.hotkey + ' 注册失败（可能被占用），请在设置中更换');
  const policy = classifyHotkey(cfg.danmakuHotkey);
  if (!policy.registerGlobal) return;
  let inputOk = false;
  try { inputOk = globalShortcut.register(policy.accelerator, () => broadcast('composer', { show: true })); } catch {}
  if (!inputOk) status('弹幕输入快捷键没有注册成功，系统按键不会被这个失败的注册占用。');
}

let server;
function startServer() {
  server?.close(); server = null; if (!cfg.obsServer) return;
  server = http.createServer(async (req, res) => {
    const hit = routeOf(req.url || '/');
    if (!hit) { res.writeHead(404); return res.end(); }
    if (hit.kind === 'events') { res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', 'Access-Control-Allow-Origin': '*' });
      res.win = hit.win; sseClients.add(res);
      res.write(`event: config\ndata: ${JSON.stringify(await view())}\n\n`); if (res.win === 'mini') res.write(`event: stats\ndata: ${JSON.stringify(stats)}\n\n`); req.on('close', () => sseClients.delete(res)); return; }
    if (hit.kind === 'font') { try { const b = fs.readFileSync(path.join(__dirname, 'fonts', hit.file)); res.writeHead(200, { 'Content-Type': 'font/woff2', 'Cache-Control': 'max-age=86400' }); return res.end(b); } catch { res.writeHead(404); return res.end(); } }
    const f = hit.file;
    res.writeHead(200, { 'Content-Type': f.endsWith('.js') ? 'text/javascript; charset=utf-8' : f.endsWith('.css') ? 'text/css; charset=utf-8' : 'text/html; charset=utf-8' });
    res.end(fs.readFileSync(path.join(__dirname, f)));
  });
  server.on('error', e => status('OBS 服务启动失败：' + e.message));
  server.listen(cfg.obsPort, '127.0.0.1');
}

function fromSettings(e) { return !!(settingsWin && e.sender === settingsWin.webContents); }
function runPowershell(script) {
  return new Promise((resolve, reject) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', script], { windowsHide: true });
    let out = '', err = '';
    child.stdout.on('data', d => { out += d; });
    child.stderr.on('data', d => { err += d; });
    child.on('error', reject);
    child.on('close', code => code === 0 ? resolve(out) : reject(new Error((err || out || ('PowerShell ' + code)).slice(0, 500))));
  });
}
async function readSystemAccent() {
  if (process.platform !== 'win32') return '';
  try {
    const raw = await runPowershell("(Get-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\DWM' -Name AccentColor -ErrorAction Stop).AccentColor");
    return colorFromColorRef(String(raw).trim()) || '';
  } catch { return ''; }
}

function sanitizePatch(patch) {
  const p = { ...patch };
  delete p.cookieLog; delete p.token; delete p.githubToken; delete p.accessToken;
  if ('appBackground' in p && p.appBackground !== '' && p.appBackground !== cfg.appBackground) delete p.appBackground;
  delete p.perfEnabled; delete p.perfStrength; delete p.perfWhitelist; delete p.githubClientId; delete p.token;
  if ('appBgOpacity' in p) p.appBgOpacity = Math.max(0, Math.min(1, Number(p.appBgOpacity) || 0));
  if ('ttsRate' in p) p.ttsRate = Math.max(0.5, Math.min(2, Number(p.ttsRate) || 1));
  if ('ttsPitch' in p) p.ttsPitch = Math.max(0.5, Math.min(2, Number(p.ttsPitch) || 1));
  if ('ttsVolume' in p) p.ttsVolume = Math.max(0, Math.min(1, Number(p.ttsVolume) || 0));
  if ('lyricsOffset' in p) p.lyricsOffset = Math.max(-10, Math.min(10, Number(p.lyricsOffset) || 0));
  if ('danmakuHotkey' in p) p.danmakuHotkey = String(p.danmakuHotkey || '').slice(0, 40);
  if ('overlayVisible' in p) p.overlayVisible = !!p.overlayVisible;
  if ('miniVisible' in p) p.miniVisible = !!p.miniVisible;
  return p;
}

if (!app.requestSingleInstanceLock()) app.quit();
app.on('second-instance', openSettings);
app.whenReady().then(() => {
  protocol.handle('tokilive-bg', () => {
    try {
      const file = cfg?.appBackground;
      if (!file || !fs.existsSync(file)) return new Response('missing', { status: 404 });
      const ext = path.extname(file).toLowerCase();
      const type = { '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.bmp': 'image/bmp' }[ext];
      if (!type) return new Response('type', { status: 415 });
      return new Response(fs.readFileSync(file), { headers: { 'Content-Type': type, 'Cache-Control': 'no-store' } });
    } catch { return new Response('err', { status: 500 }); }
  });
  nativeTheme.on('updated', () => { if (cfg.themeMode === 'system' || (cfg.appTheme || 'system') === 'system') { pushConfig(); applySettingsChrome(); } });
  load();
  readSystemAccent().then(color => { systemAccent = color; if (cfg.useSystemAccent && color) pushConfig(); });
  ipcMain.handle('get-settings', () => cfg);
  ipcMain.handle('get-view', view);
  ipcMain.handle('get-status', () => lastStatus);
  ipcMain.handle('get-version', () => app.getVersion());
  ipcMain.handle('get-guidance', () => ({ note: BROWSER_SOURCE_NOTE, upgrade: FIRST_UPGRADE_NOTE, topmost: '实验性置顶只会反复把 tokilive 自己的窗口抬到普通置顶层。独占全屏游戏不经过桌面合成，这个开关过不去，也不会注入游戏或改动反作弊。无边框窗口化时更有机会露在画面上。' }));
  ipcMain.handle('set-settings', async (_e, patch) => {
    const prev = { ...cfg }; const clean = sanitizePatch(patch || {}); cfg = { ...cfg, ...clean }; save();
    if (prev.roomId !== cfg.roomId || prev.cookie !== cfg.cookie) connect();
    if (prev.hotkey !== cfg.hotkey || prev.danmakuHotkey !== cfg.danmakuHotkey) registerHotkey();
    if (prev.miniLayout !== cfg.miniLayout || prev.miniPills !== cfg.miniPills) { miniLimits(true); save(); }
    if (prev.obsServer !== cfg.obsServer || prev.obsPort !== cfg.obsPort) startServer();
    if (prev.appMaterial !== cfg.appMaterial || prev.appTheme !== cfg.appTheme) applySettingsChrome();
    applyLock(); return cfg;
  });
  ipcMain.on('min-width', (e, w) => { const win = BrowserWindow.fromWebContents(e.sender); if (win !== overlay || !(w > 60 && w < 2000)) return; win.setMinimumSize(Math.round(w), 160); const b = win.getBounds(); if (b.width < w) win.setBounds({ ...b, width: Math.round(w) }); });
  ipcMain.on('resize', (e, w, h) => { const win = BrowserWindow.fromWebContents(e.sender); const isMini = win === mini; if (!win || (isMini ? cfg.miniLocked : cfg.locked)) return;
    const b = win.getBounds(); let nb = { ...b, width: Math.round(w), height: Math.round(h) }; if (!isMini) nb.width = Math.max(nb.width, win.getMinimumSize()[0]); if (isMini) { const [mn, mx] = [win.getMinimumSize(), win.getMaximumSize()]; nb.width = Math.max(mn[0], mx[0] ? Math.min(mx[0], nb.width) : nb.width); nb.height = Math.max(mn[1], mx[1] ? Math.min(mx[1], nb.height) : nb.height); nb = clampToScreen(nb); } win.setBounds(nb); cfg[isMini ? 'miniBounds' : 'bounds'] = win.getBounds(); save(); });
  let edge0 = null;
  ipcMain.on('edge-start', e => { const win = BrowserWindow.fromWebContents(e.sender); if (win) edge0 = { win, b: win.getBounds() }; });
  ipcMain.on('edge-move', (e, d, dx, dy, end) => { const win = BrowserWindow.fromWebContents(e.sender); if (!win || !edge0 || edge0.win !== win || (win === mini ? cfg.miniLocked : cfg.locked)) return;
    const b0 = edge0.b, [mnW, mnH] = win.getMinimumSize(), [mxW, mxH] = win.getMaximumSize(); let { x, y, width: w, height: h } = b0;
    const cw = v => Math.max(mnW || 60, mxW ? Math.min(mxW, v) : v), ch = v => Math.max(mnH || 30, mxH ? Math.min(mxH, v) : v);
    if (d.includes('e')) w = cw(b0.width + dx); if (d.includes('s')) h = ch(b0.height + dy);
    if (d.includes('w')) { w = cw(b0.width - dx); x = b0.x + b0.width - w; } if (d.includes('n')) { h = ch(b0.height - dy); y = b0.y + b0.height - h; }
    win.setBounds({ x: Math.round(x), y: Math.round(y), width: Math.round(w), height: Math.round(h) }); if (end) edge0 = null; });
  ipcMain.handle('export-theme', async () => { const r = await dialog.showSaveDialog(settingsWin, { title: '导出主题', defaultPath: 'tokilive-theme.json', filters: [{ name: 'tokilive 主题', extensions: ['json'] }] });
    if (r.canceled) return null; fs.writeFileSync(r.filePath, JSON.stringify(themeExport(cfg), null, 2)); return r.filePath; });
  ipcMain.handle('import-theme', async () => { const r = await dialog.showOpenDialog(settingsWin, { title: '导入主题', properties: ['openFile'], filters: [{ name: 'tokilive 主题', extensions: ['json'] }] });
    if (r.canceled || !r.filePaths[0]) return null; const t = JSON.parse(fs.readFileSync(r.filePaths[0], 'utf8')); cfg = themeImport(cfg, t); save(); applyLock(); return cfg; });
  ipcMain.handle('reconnect', connect);
  const login = require('./login');
  ipcMain.handle('qr-generate', () => login.generate());
  ipcMain.handle('qr-poll', async (_e, key) => { const r = await login.poll(key); if (r.cookie) { cfg.cookie = r.cookie; save(); connect(); } return { code: r.code, text: r.text }; });
  ipcMain.handle('whoami', () => login.whoami(cfg.cookie));
  ipcMain.handle('logout', () => { cfg.cookie = ''; save(); connect(); });
  ipcMain.handle('read-own-room', async (_e, typed) => {
    const cookie = String(typed || cfg.cookie || '').trim().slice(0, 8192);
    return lookupOwnRoom(cookie, async (url, ck) => {
      const headers = { 'User-Agent': 'Mozilla/5.0 tokilive', Referer: 'https://www.bilibili.com/' };
      if (ck) headers.Cookie = ck;
      const res = await fetch(url, { headers });
      return res.json();
    });
  });
  ipcMain.handle('pick-background', async () => {
    const r = await dialog.showOpenDialog(settingsWin, { title: '选择背景图片', properties: ['openFile'], filters: [{ name: '图片', extensions: ['png', 'jpg', 'jpeg', 'webp', 'bmp'] }] });
    if (r.canceled || !r.filePaths[0]) return { ok: false, message: '没有选择图片' };
    const ext = path.extname(r.filePaths[0]).toLowerCase();
    if (!['.png', '.jpg', '.jpeg', '.webp', '.bmp'].includes(ext)) return { ok: false, message: '只支持 png、jpg、webp、bmp。已保留原来的背景。' };
    try {
      const dest = path.join(app.getPath('userData'), 'appearance-bg' + ext);
      fs.copyFileSync(r.filePaths[0], dest);
      cfg.appBackground = dest; save(); applySettingsChrome(); pushConfig();
      return { ok: true };
    } catch (e) { return { ok: false, message: '图片无法使用，已保留原来的背景。' + e.message }; }
  });
  ipcMain.handle('clear-background', async () => { cfg.appBackground = ''; save(); pushConfig(); return { ok: true }; });
  ipcMain.handle('test-message', (_e, type) => { const m = require('./samples').sample(type); routeMessage(m); if (type === 'like') { stats.likes = (stats.likes || 1234) + 1; stats.watched = stats.watched || '1.2万人看过'; stats.online = stats.online || 321; broadcast('stats', stats, 'mini'); } });
  ipcMain.handle('reset-bounds', () => { cfg.bounds = null; cfg.miniBounds = null; save(); const wa = screen.getPrimaryDisplay().workArea; overlay.setBounds({ width: 420, height: 640, x: wa.x + 40, y: wa.y + 40 }); mini.setBounds({ width: 460, height: 60, x: wa.x + 480, y: wa.y + 40 }); miniLimits(true); save(); });
  ipcMain.handle('update-check', async e => {
    if (!fromSettings(e)) return { ok: false, message: '拒绝' };
    try {
      const releases = await fetchReleaseList();
      const kind = distributionKind({ portableFile: process.env.PORTABLE_EXECUTABLE_FILE, portableDir: process.env.PORTABLE_EXECUTABLE_DIR, execPath: process.execPath, packaged: app.isPackaged });
      pendingCheck = pickUpdate(releases, app.getVersion(), kind);
      pendingUpdate = null;
      return { ok: pendingCheck.state === 'available' || pendingCheck.state === 'uptodate', ...pendingCheck, note: FIRST_UPGRADE_NOTE };
    } catch (err) { return { ok: false, message: redact(err.message), note: FIRST_UPGRADE_NOTE }; }
  });
  ipcMain.handle('update-download', async e => {
    if (!fromSettings(e)) return { ok: false, message: '拒绝' };
    if (!pendingCheck || pendingCheck.state !== 'available') return { ok: false, message: '没有可下载的更新。请先检查。' };
    const destDir = path.join(app.getPath('temp'), 'tokilive-update');
    fs.mkdirSync(destDir, { recursive: true });
    const dest = path.join(destDir, pendingCheck.asset.name);
    try {
      const release = { assets: [pendingCheck.asset, pendingCheck.sums].filter(Boolean) };
      const verified = await downloadVerified({
        fs, file: dest, release, asset: pendingCheck.asset,
        onProgress: p => { try { settingsWin?.webContents.send('update-progress', p); } catch {} },
      });
      pendingUpdate = { file: dest, version: pendingCheck.latest, kind: pendingCheck.kind, sha256: verified.sha256 };
      return { ok: true, message: '已下载并校验 ' + pendingCheck.asset.name + '。还没有安装。', bytes: verified.bytes };
    } catch (err) { pendingUpdate = null; return { ok: false, message: redact(err.message) }; }
  });
  ipcMain.handle('update-install', async e => {
    if (!fromSettings(e)) return { ok: false, message: '拒绝' };
    if (!pendingUpdate) return { ok: false, message: '还没有已校验的安装包。' };
    const kind = pendingUpdate.kind === 'portable' ? 'portable' : (app.isPackaged ? 'setup' : 'dev');
    const plan = installPlan({ packaged: app.isPackaged, kind, verified: true, version: pendingUpdate.version });
    if (!plan.ok) return plan;
    const choice = await dialog.showMessageBox(settingsWin, {
      type: 'warning', buttons: ['取消', '退出并安装'], defaultId: 0, cancelId: 0, noLink: true,
      title: '安装更新', message: `安装 tokilive ${pendingUpdate.version} 会先退出当前程序。`,
      detail: plan.message + '\n\n1.0.0 和旧的私人仓库更新需要手动安装 1.2.0 一次。',
    });
    if (choice.response !== 1) return { ok: false, message: '已取消，程序仍在运行。' };
    if (kind === 'portable') {
      const dest = process.env.PORTABLE_EXECUTABLE_FILE;
      if (!dest) return { ok: false, message: '找不到便携版自己的文件，没有退出。' };
      const script = portableScript({ pid: process.pid, source: pendingUpdate.file, dest });
      const ps1 = path.join(app.getPath('temp'), 'tokilive-update', 'apply.ps1');
      fs.writeFileSync(ps1, '\ufeff' + script, 'utf8');
      const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', ps1], { detached: true, stdio: 'ignore', windowsHide: true });
      child.unref();
    } else {
      const child = spawn(pendingUpdate.file, setupArgs(), { detached: true, stdio: 'ignore' });
      child.unref();
    }
    setTimeout(() => app.quit(), 250);
    return { ok: true, message: '正在退出并继续安装。' };
  });
  ipcMain.handle('send-danmaku', async (_e, text) => {
    const gate = canSend(cfg.cookie);
    if (!gate.ok) return gate;
    const bodyText = validateText(text);
    if (!bodyText.ok) return bodyText;
    const pace = allowSend(Date.now(), lastDanmakuSent);
    if (!pace.ok) return pace;
    if (!cfg.roomId) return { ok: false, message: '还没有房间号，没有发送。' };
    try {
      const res = await fetch('https://api.live.bilibili.com/msg/send', {
        method: 'POST',
        headers: { 'User-Agent': 'Mozilla/5.0 tokilive', Referer: 'https://live.bilibili.com/', Origin: 'https://live.bilibili.com', Cookie: cfg.cookie, 'Content-Type': 'application/x-www-form-urlencoded' },
        body: formBody({ roomId: cfg.roomId, text: bodyText.text, csrf: (cfg.cookie.match(/(?:^|;\s*)bili_jct=([^;]+)/) || [])[1] ? decodeURIComponent((cfg.cookie.match(/(?:^|;\s*)bili_jct=([^;]+)/) || [])[1]) : '' }),
      });
      const json = await res.json();
      const out = interpretResponse(json);
      if (out.ok) lastDanmakuSent = Date.now();
      return out;
    } catch (err) { return { ok: false, message: '发送失败：' + redact(err.message) }; }
  });
  let adaptiveAt = 0;
  ipcMain.handle('sample-background', async e => {
    if (!cfg.adaptiveText) return { ok: false, reason: 'off', message: '文字颜色自适应默认关闭。' };
    const now = Date.now();
    if (now - adaptiveAt < 2000) return { ok: false, reason: 'throttled', message: '采样间隔未到。' };
    adaptiveAt = now;
    const win = BrowserWindow.fromWebContents(e.sender);
    if (!win) return { ok: false, reason: 'no-window' };
    try {
      const display = screen.getDisplayMatching(win.getBounds());
      const sources = await desktopCapturer.getSources({ types: ['screen'], thumbnailSize: { width: 480, height: 270 } });
      const source = sources.find(s => String(s.display_id) === String(display.id)) || sources[0];
      if (!source) return { ok: false, reason: 'unavailable', message: '当前环境读不到桌面画面，请改用手动深浅。这不是根据种子色猜测的。' };
      const image = source.thumbnail;
      const size = image.getSize();
      const bitmap = image.getBitmap();
      const rect = mapRect(win.getBounds(), display.bounds, size.width, size.height);
      const sample = luminanceOutside(bitmap, size.width, size.height, rect);
      if (!sample.ok) return { ...sample, message: sample.message || '无法分析窗口外侧背景。' };
      return { ok: true, text: sample.text, luminance: sample.luminance, message: sample.text === 'dark' ? '外侧背景偏亮，文字用深色。' : '外侧背景偏暗，文字用浅色。' };
    } catch (err) { return { ok: false, reason: 'unavailable', message: '无法读取桌面：' + err.message }; }
  });
  ipcMain.handle('lyrics-now', async () => {
    if (cfg.lyricsDemo) {
      const lines = parseLrc(DEMO_LRC);
      const t = (Date.now() / 1000) % 14;
      return { ok: true, demo: true, session: { ok: true, source: '演示', sourceKind: 'demo', title: 'tokilive 演示', artist: '不是在售歌曲', position: t, duration: 14, status: 'playing' }, lines, index: lineAt(lines, t), label: '演示，不是正在播放的歌' };
    }
    if (process.platform !== 'win32') return { ok: false, message: '系统媒体会话只在 Windows 10/11 上读取。这里不会去翻播放器的数据库。', lines: parseLrc(DEMO_LRC), demoAvailable: true };
    try {
      const raw = await runPowershell(`$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation\`1' })[0]
function Await($op, $type) { $m = $asTask.MakeGenericMethod($type); $task = $m.Invoke($null, @($op)); $task.Wait(); $task.Result }
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime]
$mgr = Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
$s = $mgr.GetCurrentSession()
if (-not $s) { '{"ok":false,"message":"没有正在播放的媒体"}'; exit 0 }
$props = Await ($s.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
$tl = $s.GetTimelineProperties(); $pb = $s.GetPlaybackInfo()
@{ ok=$true; source=$s.SourceAppUserModelId; title=$props.Title; artist=$props.Artist; position=$tl.Position.TotalSeconds; duration=$tl.EndTime.TotalSeconds; status=[string]$pb.PlaybackStatus } | ConvertTo-Json -Compress`);
      const session = parseSession(JSON.parse(raw));
      if (!session.ok || !session.title) return { ...session, lines: [], label: sourceLabel(session.sourceKind) };
      const key = session.sourceKind + '|' + session.title + '|' + session.artist;
      if (lyricCache.key !== key) {
        lyricCache = { key, hit: await searchLyrics(session.title, session.artist).catch(err => ({ ok: false, message: err.message })) };
      }
      const lines = lyricCache.hit && lyricCache.hit.ok ? parseLrc(lyricCache.hit.synced || lyricCache.hit.plain) : [];
      const at = Math.max(0, session.position + Number(cfg.lyricsOffset || 0));
      return { ok: true, session, lines, index: lineAt(lines, at), lyric: lyricCache.hit, label: sourceLabel(session.sourceKind) };
    } catch (err) { return { ok: false, message: '读不到系统媒体会话：' + redact(err.message), capability: 'SMTC 不可用或系统版本过旧' }; }
  });
  ipcMain.handle('lyrics-control', async (_e, action) => {
    let method;
    try { method = controlCommand(action); } catch (err) { return { ok: false, message: err.message }; }
    if (process.platform !== 'win32') return { ok: false, message: '播放控制只在 Windows 上交给系统媒体会话。' };
    try {
      await runPowershell(`$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation\`1' })[0]
function Await($op, $type) { $m = $asTask.MakeGenericMethod($type); $task = $m.Invoke($null, @($op)); $task.Wait(); $task.Result }
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime]
$mgr = Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
$s = $mgr.GetCurrentSession(); if (-not $s) { throw 'no session' }
$null = Await ($s.${method}()) ([bool])`);
      return { ok: true };
    } catch (err) { return { ok: false, message: '播放器没有接受这个操作。' }; }
  });
  ipcMain.handle('taskbar-state', () => taskbarDecision({ hideRequested: !!cfg.hideFromTaskbar, trayAvailable: !!tray }));
  ipcMain.handle('obs-probe', async () => {
    if (!cfg.obsServer) return { ok: false, message: '本地服务没开。打开开关后才能把地址交给 OBS 浏览器源。', note: BROWSER_SOURCE_NOTE };
    try {
      const overlayRes = await fetch(`http://127.0.0.1:${cfg.obsPort}/overlay`);
      const miniRes = await fetch(`http://127.0.0.1:${cfg.obsPort}/mini`);
      const ok = overlayRes.ok && miniRes.ok;
      return { ok, overlay: `http://127.0.0.1:${cfg.obsPort}/overlay`, mini: `http://127.0.0.1:${cfg.obsPort}/mini`, message: ok ? '两个本地页面都有响应。这只说明地址可用，不代表已经在 OBS 或直播姬里采到画面。' : '本地服务没有同时返回两个页面。', note: BROWSER_SOURCE_NOTE };
    } catch (e) { return { ok: false, message: '连不上本地服务：' + e.message, note: BROWSER_SOURCE_NOTE }; }
  });
  try {
    tray = new Tray(nativeImage.createFromPath(path.join(__dirname, 'icon.png')).resize({ width: 16, height: 16 }));
    tray.on('click', openSettings);
  } catch (e) { tray = null; status('托盘不可用：' + e.message); }
  try { createSplash(); } catch { splashT = Date.now(); }
  createOverlay(); updateTray(); registerHotkey(); startServer(); connect();
});
app.on('will-quit', () => globalShortcut.unregisterAll());
app.on('window-all-closed', e => e.preventDefault?.());

module.exports = { THEME_KEYS };
