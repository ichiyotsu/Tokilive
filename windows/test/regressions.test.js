const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { splitTheme } = require('../src/theme-split');
const { THEME_KEYS, APP_APPEARANCE_KEYS, themeExport, themeImport } = require('../src/theme-keys');
const { resolveSidebar, navKey, typeahead } = require('../src/sidebar');
const { nextVisibility, visibilityActions } = require('../src/visibility');
const { WINDOW_TITLES, titleFor } = require('../src/titles');
const { captureExStyle, captureFixPlan, WS_EX_LAYERED, WS_EX_TOOLWINDOW, WDA_EXCLUDEFROMCAPTURE } = require('../src/capture-style');
const { lookupOwnRoom } = require('../src/room');
const { applyPerf, isProtected, planClose, gracefulScript, forceScript, scanScript, filterCandidates, assertNoWildcard } = require('../src/perf');
const { routeOf } = require('../src/obs-routes');
const {
  compareVersions, isNewer, pickUpdate, assetApiUrl, redirectPlan, verifySha256, parseDigest,
  redact, publicDeviceStart, sealToken, installPlan, portableScript, setupArgs, distributionKind,
  FIRST_UPGRADE_NOTE, authStatus,
} = require('../src/update-core');
const { resolveToken, downloadToFile } = require('../src/update-run');
const { buildScript } = require('../src/capture-win');

const root = path.join(__dirname, '..', '..');

test('versions are 1.1.0 and android versionCode moved forward', () => {
  const pkg = JSON.parse(fs.readFileSync(path.join(root, 'windows', 'package.json'), 'utf8'));
  assert.equal(pkg.version, '1.1.0');
  assert.equal(pkg.build.appId, 'com.ichiyotsu.tokilive');
  const gradle = fs.readFileSync(path.join(root, 'android', 'app', 'build.gradle.kts'), 'utf8');
  assert.match(gradle, /versionName = "1\.1\.0"/);
  assert.match(gradle, /versionCode = 2/);
  assert.match(gradle, /applicationId = "com\.ichiyotsu\.tokilive"/);
  assert.doesNotMatch(gradle, /storePassword\s*=\s*"/);
  assert.doesNotMatch(gradle, /keyPassword\s*=\s*"/);
});

test('no embedded tokens or signing keys', () => {
  const skip = new Set(['node_modules', 'dist', 'build', '.gradle']);
  const hits = [];
  function walk(dir) {
    for (const name of fs.readdirSync(dir)) {
      if (skip.has(name)) continue;
      const p = path.join(dir, name);
      const st = fs.statSync(p);
      if (st.isDirectory()) walk(p);
      else if (/\.(js|kt|kts|html|json|xml|md|pro)$/i.test(name) && st.size < 2_000_000) {
        const text = fs.readFileSync(p, 'utf8');
        if (/ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|BEGIN [A-Z ]*PRIVATE KEY|storePassword\s*=\s*"/.test(text)) hits.push(p);
      }
    }
  }
  walk(root);
  assert.deepEqual(hits, []);
});

test('app theme does not change card theme', () => {
  const cfg = { themeMode: 'dark', appTheme: 'light', seedColor: '#112233', appAccent: '#aabbcc', material: 'none', appMaterial: 'mica', opacity: 0.8 };
  const a = splitTheme(cfg, { systemDark: true });
  const b = splitTheme({ ...cfg, appTheme: 'dark', appAccent: '#00ff00', appMaterial: 'acrylic', appBgOpacity: 0.2 }, { systemDark: true });
  assert.equal(a.cardDark, true);
  assert.equal(a.appDark, false);
  assert.equal(b.cardDark, a.cardDark);
  assert.equal(b.cardSeed, a.cardSeed);
  assert.equal(b.cardMode, 'dark');
  assert.equal(b.overlayMaterial, 'none');
  assert.equal(b.settingsMaterial, 'acrylic');
  assert.notEqual(b.appAccent, a.appAccent);
  const exported = themeExport(cfg);
  for (const k of APP_APPEARANCE_KEYS) assert.equal(exported[k], undefined);
  assert.ok(THEME_KEYS.includes('themeMode'));
  assert.ok(THEME_KEYS.includes('seedColor'));
  const imported = themeImport({ ...cfg, appTheme: 'light', appAccent: '#aabbcc' }, { ...exported, appTheme: 'dark', appAccent: '#ffffff', themeMode: 'light' });
  assert.equal(imported.appTheme, 'light');
  assert.equal(imported.appAccent, '#aabbcc');
  assert.equal(imported.themeMode, 'light');
});

test('hiding windows does not drop the connection', () => {
  const next = nextVisibility({ overlayVisible: true, miniVisible: true, connected: true }, { overlayVisible: false });
  assert.equal(next.overlayVisible, false);
  assert.equal(next.miniVisible, true);
  assert.equal(next.connected, true);
  assert.equal(next.destroyWindows, false);
  assert.equal(next.stopConnection, false);
  assert.deepEqual(visibilityActions(next), { overlay: 'hide', mini: 'show', disconnect: false });
  const onlyMini = nextVisibility(next, { miniVisible: false });
  assert.equal(onlyMini.overlayVisible, false);
  assert.equal(visibilityActions(onlyMini).disconnect, false);
});

test('window titles are distinct and capture keeps transparency flags', () => {
  assert.equal(titleFor('danmaku'), 'tokilive · 弹幕');
  assert.equal(titleFor('mini'), 'tokilive · 互动小窗');
  assert.equal(titleFor('settings'), 'tokilive · 设置');
  assert.equal(new Set(Object.values(WINDOW_TITLES)).size, Object.values(WINDOW_TITLES).length);
  const styled = captureExStyle(WS_EX_TOOLWINDOW | WS_EX_LAYERED);
  assert.equal(styled & WS_EX_TOOLWINDOW, 0);
  assert.equal(styled & WS_EX_LAYERED, WS_EX_LAYERED);
  const plan = captureFixPlan({ exStyle: WS_EX_TOOLWINDOW | WS_EX_LAYERED, title: WINDOW_TITLES.danmaku });
  assert.equal(plan.displayAffinity, 0);
  assert.notEqual(plan.displayAffinity, WDA_EXCLUDEFROMCAPTURE);
  assert.equal(plan.createHiddenDuplicate, false);
  assert.equal(plan.layered, true);
  const script = buildScript('1234', plan.title);
  assert.match(script, /SetWindowDisplayAffinity\(\$h, \[uint32\]0\)/);
  assert.doesNotMatch(script, /0x11/);
  assert.throws(() => buildScript('1; calc', 'tokilive'));
});

test('sidebar collapses and stays keyboard addressable', () => {
  assert.equal(resolveSidebar({ width: 1200, preference: 'auto' }).display, 'expanded');
  assert.equal(resolveSidebar({ width: 800, preference: 'auto' }).display, 'compact');
  assert.equal(resolveSidebar({ width: 500, preference: 'expanded', userOpen: false }).display, 'minimal');
  assert.equal(resolveSidebar({ width: 500, preference: 'expanded', userOpen: true }).open, true);
  assert.equal(navKey('Home', 4, 10), 0);
  assert.equal(navKey('End', 1, 10), 9);
  assert.equal(navKey('ArrowDown', 9, 10), 0);
  assert.equal(typeahead('更', ['连接', '应用外观', '更新'], 0), 2);
});

test('room lookup failures do not return a room id', async () => {
  const none = await lookupOwnRoom('', async () => { throw new Error('should not fetch'); });
  assert.equal(none.ok, false);
  assert.equal(none.reason, 'no-login');
  assert.equal(none.roomId, null);
  const expired = await lookupOwnRoom('SESSDATA=abc', async () => ({ code: -101, data: { isLogin: false } }));
  assert.equal(expired.reason, 'no-login');
  assert.equal(expired.roomId, null);
  const noRoom = await lookupOwnRoom('SESSDATA=abc', async (url) => {
    if (url.includes('nav')) return { code: 0, data: { isLogin: true, mid: 7 } };
    return { code: 0, data: { room_id: 0 } };
  });
  assert.equal(noRoom.reason, 'no-room');
  assert.equal(noRoom.roomId, null);
  const down = await lookupOwnRoom('SESSDATA=abc', async () => { throw new Error('offline'); });
  assert.equal(down.reason, 'no-api');
  assert.equal(down.roomId, null);
  const ok = await lookupOwnRoom('SESSDATA=abc', async (url) => {
    if (url.includes('nav')) return { code: 0, data: { isLogin: true, mid: 7 } };
    return { code: 0, data: { room_id: 7777 } };
  });
  assert.equal(ok.roomId, '7777');
});

test('cleanup refuses protected processes, wildcards and unconfirmed force', () => {
  for (const name of ['obs64', 'livehime', 'tokilive', 'MsMpEng', 'svchost', 'dwm', 'audiodg', 'nvcontainer']) {
    assert.equal(isProtected({ name, path: 'D:\\apps\\' + name + '.exe' }), true, name);
  }
  assert.equal(isProtected({ name: 'notepad', path: 'C:\\Windows\\System32\\notepad.exe' }), true);
  assert.equal(isProtected({ name: 'chrome', path: 'C:\\Users\\a\\AppData\\Local\\Google\\Chrome\\Application\\chrome.exe' }), false);
  const list = filterCandidates([
    { pid: 4, name: 'System' },
    { pid: 20, name: 'obs64', mem: 100 },
    { pid: 30, name: 'chrome', mem: 50, path: 'C:\\Users\\a\\Chrome\\chrome.exe' },
    { pid: 31, name: 'chrome', mem: 10, path: 'C:\\Users\\a\\Chrome\\chrome.exe' },
  ], ['chrome']);
  assert.equal(list.selectable.length, 0);
  assert.ok(list.protectedCount >= 2);
  const open = [
    { pid: 30, name: 'chrome', path: 'C:\\Users\\a\\Chrome\\chrome.exe', title: 'page' },
    { pid: 40, name: 'obs64', path: 'D:\\obs\\obs64.exe' },
  ];
  const graceful = planClose(open, [30, 40], { force: false });
  assert.deepEqual(graceful.allowed.map(p => p.pid), [30]);
  assert.equal(graceful.rejected.some(r => r.reason === 'protected'), true);
  const script = gracefulScript(graceful.allowed.map(p => p.pid));
  assert.match(script, /CloseMainWindow/);
  assert.doesNotMatch(script, /-Force|taskkill|\*/);
  assert.throws(() => planClose(open, [30], { force: true, forceConfirmed: false }));
  const forced = forceScript([30]);
  assert.match(forced, /Stop-Process -Id 30 -Force/);
  assert.throws(() => assertNoWildcard('taskkill /IM *'));
  assert.throws(() => assertNoWildcard('Stop-Process -Name chrome*'));
  assert.doesNotMatch(scanScript(), /Stop-Process|taskkill|CloseMainWindow|\*/);
  const perfOff = applyPerf({ perfEnabled: false, perfStrength: 100, blur: 16, maxMessages: 30 });
  assert.equal(perfOff.blur, 16);
  assert.equal(perfOff.perfActive, false);
  const perfOn = applyPerf({ perfEnabled: true, perfStrength: 100, blur: 16, maxMessages: 30, miniMax: 5 });
  assert.equal(perfOn.blur, 0);
  assert.ok(perfOn.maxMessages < 30);
  assert.equal(perfOn.reduceMotion, true);
});

test('private update compares versions and will not fetch anonymously', () => {
  assert.equal(isNewer('1.1.0', '1.0.0'), true);
  assert.equal(isNewer('1.0.0', '1.1.0'), false);
  assert.equal(isNewer('v1.1.0', '1.1.0'), false);
  assert.equal(compareVersions('1.2.0', '1.1.10'), 1);
  const releases = [{
    tag_name: 'v1.1.0', draft: false, prerelease: false, body: 'notes',
    assets: [
      { id: 9, name: 'tokilive-1.1.0-setup.exe', digest: 'sha256:' + 'ab'.repeat(32) },
      { id: 10, name: 'tokilive-1.1.0-portable.exe', digest: 'sha256:' + 'cd'.repeat(32) },
      { name: 'tokilive-1.1.0-setup.exe.blockmap' },
    ],
  }, { tag_name: 'v1.0.0', draft: false, prerelease: false, assets: [] }];
  const picked = pickUpdate(releases, '1.0.0', 'setup');
  assert.equal(picked.state, 'available');
  assert.equal(picked.asset.name, 'tokilive-1.1.0-setup.exe');
  assert.equal(assetApiUrl(picked.asset.id), 'https://api.github.com/repos/ichiyotsu/tokilive/releases/assets/9');
  assert.equal(pickUpdate(releases, '1.0.0', 'portable').asset.id, 10);
  assert.equal(pickUpdate(releases, '1.1.0', 'setup').state, 'uptodate');
  const redir = redirectPlan(302, 'https://objects.githubusercontent.com/x');
  assert.equal(redir.auth, false);
  assert.equal(redirectPlan(302, 'http://evil.example/x'), null);
  assert.equal(verifySha256('ab'.repeat(32), 'ab'.repeat(32)), true);
  assert.equal(verifySha256('ab'.repeat(32), 'cd'.repeat(32)), false);
  assert.match(FIRST_UPGRADE_NOTE, /1\.0\.0/);
  assert.match(redact('token ghp_' + 'a'.repeat(30) + ' Bearer ghp_zzzz'), /\[token\]/);
  assert.equal(publicDeviceStart({ ok: true, userCode: 'ABCD-1234', uri: 'https://github.com/login/device', deviceCode: 'secret-code' }).deviceCode, undefined);
  assert.deepEqual(setupArgs(), []);
  const plan = installPlan({ packaged: true, kind: 'setup', verified: true, version: '1.2.0' });
  assert.equal(plan.silent, false);
  assert.deepEqual(plan.args, []);
  assert.equal(installPlan({ packaged: false, kind: 'dev', verified: true, version: '1.2.0' }).ok, false);
  const ps = portableScript({ pid: 99, source: 'C:\\a\\new.exe', dest: 'D:\\tokilive\\tokilive-portable.exe' });
  assert.match(ps, /LiteralPath/);
  assert.match(ps, /Wait-Process -Id 99/);
  assert.doesNotMatch(ps, /\*/);
  assert.equal(distributionKind({ portableFile: 'D:\\a.exe' }), 'portable');
  const sums = 'cd'.repeat(32) + '  tokilive-1.1.0-portable.exe\n';
  assert.equal(parseDigest({ name: 'tokilive-1.1.0-portable.exe' }, sums), 'cd'.repeat(32));
});

test('token never appears in resolver logs or renderer-facing status', async () => {
  const secret = 'github_pat_' + 'abcDEF1234567890xyz';
  const logs = [];
  const auth = await resolveToken({
    fs: { existsSync: () => true, readFileSync: () => Buffer.from('blob') },
    file: 'github-auth.bin',
    safeStorage: { isEncryptionAvailable: () => true, decryptString: () => secret },
    env: '',
    execFile: () => { throw new Error('should not call gh'); },
    log: m => logs.push(m),
  });
  assert.equal(auth.source, 'stored');
  assert.equal(logs.join('').includes(secret), false);
  assert.equal(JSON.stringify(authStatus({ hasStored: true, envOk: false, ghOk: false })).includes(secret), false);
  const sealed = sealToken({ isEncryptionAvailable: () => false }, secret);
  assert.equal(sealed.ok, false);
  assert.equal(JSON.stringify(sealed).includes(secret), false);
});

test('asset download drops authorization on redirect', async () => {
  const secret = 'ghp_' + 'q'.repeat(36);
  const calls = [];
  const fetchImpl = async (url, opts) => {
    calls.push({ url, auth: opts.headers.Authorization || '' });
    if (url.includes('api.github.com')) {
      return { status: 302, headers: { get: k => k === 'location' ? 'https://objects.githubusercontent.com/pkg' : null } };
    }
    return {
      status: 200,
      headers: { get: () => '3' },
      body: { getReader: () => ({ read: async () => ({ done: true, value: undefined }) }) },
    };
  };
  const fsMock = { createWriteStream: () => ({ write() {}, on() {}, end(cb) { cb(); } }) };
  await downloadToFile({ fetchImpl, fs: fsMock, file: 'x.bin', url: 'https://api.github.com/repos/ichiyotsu/tokilive/releases/assets/9', token: secret });
  assert.match(calls[0].auth, /Bearer/);
  assert.equal(calls[1].auth, '');
  assert.equal(calls[1].url, 'https://objects.githubusercontent.com/pkg');
});

test('obs routes keep overlay and mini distinct', () => {
  assert.equal(routeOf('/overlay').file, 'overlay.html');
  assert.equal(routeOf('/mini').mini, true);
  assert.equal(routeOf('/events?w=mini').win, 'mini');
  assert.equal(routeOf('/nope'), null);
});
