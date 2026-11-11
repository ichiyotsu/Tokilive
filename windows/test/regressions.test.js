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
const { classifyHotkey } = require('../src/hotkey-policy');
const { luminanceOutside } = require('../src/adaptive');
const { canSend, validateText, allowSend, interpretResponse } = require('../src/danmaku-send');
const { speechPlan } = require('../src/tts');
const { parseLrc, lineAt, DEMO_LRC } = require('../src/lyrics/lrc');
const { parseTtml } = require('../src/lyrics/ttml');
const { pickRecord, searchUrl } = require('../src/lyrics/lrclib');
const { parseSession, controlCommand, sourceLabel } = require('../src/lyrics/smtc');
const { colorFromColorRef } = require('../src/accent-color');
const { taskbarDecision } = require('../src/taskbar-policy');
const { forgetSavedToken } = require('../src/update-run');
const { routeOf } = require('../src/obs-routes');
const {
  compareVersions, isNewer, pickUpdate, assetApiUrl, redirectPlan, verifySha256, parseDigest,
  redact, installPlan, portableScript, setupArgs, distributionKind, FIRST_UPGRADE_NOTE,
} = require('../src/update-core');
const { downloadToFile } = require('../src/update-run');
const { buildScript } = require('../src/capture-win');

const root = path.join(__dirname, '..', '..');

test('versions are 1.3.0 on Windows and Android', () => {
  const pkg = JSON.parse(fs.readFileSync(path.join(root, 'windows', 'package.json'), 'utf8'));
  assert.equal(pkg.version, '1.3.0');
  const csproj = fs.readFileSync(path.join(root, 'windows', 'winui', 'Tokilive', 'Tokilive.csproj'), 'utf8');
  assert.match(csproj, /<Version>1\.3\.1<\/Version>/);
  assert.equal(pkg.build.appId, 'com.ichiyotsu.tokilive');
  const gradle = fs.readFileSync(path.join(root, 'android', 'app', 'build.gradle.kts'), 'utf8');
  assert.match(gradle, /versionName = "1\.3\.0"/);
  assert.match(gradle, /versionCode = 4/);
  assert.match(gradle, /minSdk = 31/);
  const plist = fs.readFileSync(path.join(root, 'ios', 'Tokilive', 'Info.plist'), 'utf8');
  assert.match(plist, /<key>CFBundleShortVersionString<\/key>\s*<string>1\.3\.0<\/string>/);
  assert.match(plist, /<key>CFBundleVersion<\/key>\s*<string>4<\/string>/);
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
  assert.equal(titleFor('mini'), 'tokilive · 实时窗');
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

test('performance tooling is gone', () => {
  assert.equal(fs.existsSync(path.join(root, 'windows', 'src', 'perf.js')), false);
  const main = fs.readFileSync(path.join(root, 'windows', 'src', 'main.js'), 'utf8');
  assert.doesNotMatch(main, /perf-scan|perf-force|taskkill|Stop-Process/);
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
  assert.equal(assetApiUrl(picked.asset.id), 'https://api.github.com/repos/ichiyotsu/Tokilive/releases/assets/9');
  assert.equal(pickUpdate(releases, '1.0.0', 'portable').asset.id, 10);
  assert.equal(pickUpdate(releases, '1.1.0', 'setup').state, 'uptodate');
  const redir = redirectPlan(302, 'https://objects.githubusercontent.com/x');
  assert.equal(redir.auth, false);
  assert.equal(redirectPlan(302, 'http://evil.example/x'), null);
  assert.equal(verifySha256('ab'.repeat(32), 'ab'.repeat(32)), true);
  assert.equal(verifySha256('ab'.repeat(32), 'cd'.repeat(32)), false);
  assert.match(FIRST_UPGRADE_NOTE, /1\.0\.0/);
  assert.match(redact('token ghp_' + 'a'.repeat(30) + ' Bearer ghp_zzzz'), /\[token\]/);
  assert.equal(isNewer('1.2.0', '1.1.0'), true);
  assert.equal(isNewer('1.1.0', '1.2.0'), false);
  assert.equal(isNewer('1.2.0', '1.2.0'), false);
  assert.ok(compareVersions('0.1.1-b1', '0.1.1') < 0);
  assert.ok(compareVersions('0.1.1', '0.1.1-b1') > 0);
  assert.ok(compareVersions('0.1.0', '0.1.1-b1') < 0);
  assert.ok(compareVersions('0.1.1-b1', '0.1.1-b2') < 0);
  assert.ok(compareVersions('0.1.1-b2', '0.1.1-b10') < 0);
  assert.equal(compareVersions('v0.1.1-b1', '0.1.1-b1'), 0);
  const channel = [
    { tag_name: 'v0.1.1-b2', draft: false, prerelease: true, assets: [{ id: 3, name: 'tokilive-0.1.1-b2-setup.exe', digest: 'sha256:' + 'aa'.repeat(32) }] },
    { tag_name: 'v0.1.1-b1', draft: false, prerelease: false, assets: [{ id: 5, name: 'tokilive-0.1.1-b1-setup.exe', digest: 'sha256:' + 'ee'.repeat(32) }] },
    { tag_name: 'v0.1.1', draft: false, prerelease: false, assets: [{ id: 4, name: 'tokilive-0.1.1-setup.exe', digest: 'sha256:' + 'bb'.repeat(32) }] },
    { tag_name: 'v0.1.0', draft: false, prerelease: false, assets: [{ id: 2, name: 'tokilive-0.1.0-setup.exe', digest: 'sha256:' + 'cc'.repeat(32) }] },
  ];
  assert.equal(pickUpdate(channel, '0.1.0', 'setup').latest, '0.1.1');
  assert.equal(pickUpdate(channel, '0.1.1-b2', 'setup').latest, '0.1.1');
  assert.equal(pickUpdate(channel, '0.1.1', 'setup').state, 'uptodate');
  assert.equal(pickUpdate([channel[0], channel[1], channel[3]], '0.1.0', 'setup').state, 'uptodate');
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

test('public downloads never send an authorization header', async () => {
  const calls = [];
  const fetchImpl = async (url, opts) => {
    calls.push({ url, auth: opts.headers.Authorization || '', keys: Object.keys(opts.headers) });
    if (url.includes('api.github.com')) {
      return { status: 302, headers: { get: k => k === 'location' ? 'https://objects.githubusercontent.com/pkg' : null } };
    }
    return { status: 200, headers: { get: () => '0' }, body: { getReader: () => ({ read: async () => ({ done: true }) }) } };
  };
  const fsMock = { createWriteStream: () => ({ write() {}, on() {}, end(cb) { cb(); } }) };
  await downloadToFile({ fetchImpl, fs: fsMock, file: 'x.bin', url: 'https://api.github.com/repos/ichiyotsu/Tokilive/releases/assets/9' });
  assert.equal(calls.every(c => c.auth === '' && !c.keys.includes('Authorization')), true);
  const removed = [];
  forgetSavedToken({ existsSync: () => true, unlinkSync: p => removed.push(p) }, 'user/github-auth.bin');
  assert.deepEqual(removed, ['user/github-auth.bin']);
});

test('danmaku send, lyrics, adaptive text and taskbar policy', () => {
  assert.equal(canSend('').ok, false);
  assert.equal(canSend('SESSDATA=a').reason, 'no-csrf');
  assert.equal(validateText('  你好 ').text, '你好');
  assert.equal(allowSend(2000, 1000).ok, false);
  assert.equal(interpretResponse({ code: 0 }).ok, true);
  assert.equal(classifyHotkey('/').registerGlobal, false);
  assert.equal(classifyHotkey('CommandOrControl+Alt+D').registerGlobal, true);
  const px = Buffer.alloc(8 * 8 * 4, 255);
  const sample = luminanceOutside(px, 8, 8, { x: 2, y: 2, width: 2, height: 2 });
  assert.equal(sample.text, 'dark');
  assert.equal(speechPlan({ tts: false }).cancel, true);
  const lines = parseLrc(DEMO_LRC);
  assert.ok(lines.length >= 3);
  assert.equal(lineAt(lines, 3.2) >= 1, true);
  assert.ok(lines.some(l => l.words.length));
  const ttml = parseTtml('<tt><body><p begin="00:00:02.5">第二行</p><p>无时间</p></body></tt>');
  assert.equal(ttml.length, 1);
  assert.equal(ttml[0].text, '第二行');
  assert.match(searchUrl('歌', '人'), /lrclib\.net/);
  assert.equal(pickRecord([{ trackName: '歌', syncedLyrics: '[00:01.00]词' }], '歌').syncedLyrics.includes('词'), true);
  assert.equal(sourceLabel(parseSession({ ok: true, source: 'QQ音乐', title: 'a' }).sourceKind), 'QQ音乐');
  assert.equal(controlCommand('next'), 'TrySkipNextAsync');
  assert.throws(() => controlCommand('format C:'));
  assert.equal(colorFromColorRef(0x00ff8000), '#0080ff');
  assert.equal(taskbarDecision({ hideRequested: true, trayAvailable: false }).skipTaskbar, false);
  assert.equal(taskbarDecision({ hideRequested: true, trayAvailable: true }).skipTaskbar, true);
});

test('obs routes keep overlay and mini distinct', () => {
  assert.equal(routeOf('/overlay').file, 'overlay.html');
  assert.equal(routeOf('/mini').mini, true);
  assert.equal(routeOf('/events?w=mini').win, 'mini');
  assert.equal(routeOf('/nope'), null);
});
