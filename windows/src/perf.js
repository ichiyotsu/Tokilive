const PROFILES = [
  { id: 'off', label: '关闭', enabled: false, strength: 0 },
  { id: 'light', label: '轻度', enabled: true, strength: 30 },
  { id: 'smooth', label: '流畅', enabled: true, strength: 70 },
  { id: 'low', label: '更低开销', enabled: true, strength: 100 },
];

const GUIDANCE = [
  '下面是 Intel Iris Xe（大约 2021 年，第 11 代核显）做 1080p30 推流时的常见分界，用来对照 OBS / 直播姬自己的统计。这不是在你的电脑上测出来的结论，也不能保证延迟会下降。',
  '编码：Iris Xe 没有 NVENC。x264 预设偏慢时，CPU 会把帧堆在编码器里。可以试 Quick Sync（QSV），或把 x264 放到 veryfast / ultrafast。1080p30 可以先从大约 4500–6000 kbps 试，再按画面和上行调整。',
  '渲染和采集：透明分层窗口的窗口捕获很吃合成。优先用本应用给出的浏览器源。预览分辨率不必跟输出一样大，把预览缩到一半往往比关几个后台更有效。',
  '网络：统计里如果是网络丢帧，而不是渲染延迟或编码延迟，降码率或查上行。结束后台进程解决不了上行不够。',
  '这里的强度只限制 tokilive 自己的模糊、同时保留的消息数量和动画，用来降低悬浮窗的合成开销。关掉之后立刻回到你原来的外观数值，不会改写那些设置。',
].join('\n');

const DENY = new Set([
  'system', 'registry', 'smss', 'csrss', 'wininit', 'winlogon', 'services', 'lsass', 'svchost',
  'dwm', 'explorer', 'fontdrvhost', 'sihost', 'taskhostw', 'runtimebroker', 'searchhost',
  'startmenuexperiencehost', 'shellexperiencehost', 'ctfmon', 'conhost', 'dllhost', 'spoolsv',
  'lsaiso', 'securityhealthservice', 'securityhealthsystray', 'msmpeng', 'nissrv', 'smartscreen',
  'mpdefendercoreservice', 'msense', 'sense', 'windefend', 'securityhealthhost',
  'audiodg', 'audioendpointbuilder', 'voicemeeter', 'voicemeeter8', 'voicemeeterpro',
  'obs', 'obs64', 'obs32', 'obs-browser-page', 'obs-ffmpeg-mux',
  'livehime', 'livehime64', 'bililive', 'bili-livehime',
  'ffmpeg', 'xsplit', 'xsplit.core', 'streamlabs', 'prismlivestudio',
  'nvcontainer', 'nvidiashare', 'nvidia share', 'nvdisplay.container', 'nvidiaservice',
  'igfxem', 'igfxhk', 'igfxtray', 'igfxext', 'intelgraphicssoftware', 'oneapp.igcc', 'presentmon',
  'amdow', 'radeonsoftware', 'atiesrxx', 'amdrsserv',
  'gamebar', 'gamebarftserver', 'gamebarpresencewriter',
  'wlanext', 'dashost', 'wmiprvse', 'searchindexer', 'unsecapp', 'lsm',
  'tokilive', 'tokilive-updater', 'electron',
]);

function baseName(name) {
  return String(name || '').toLowerCase().replace(/\.exe$/i, '').trim();
}

function isProtected(proc) {
  const name = baseName(proc && proc.name);
  if (!name) return true;
  if (DENY.has(name)) return true;
  if (name.includes('tokilive')) return true;
  if (name.includes('livehime') || name.includes('obs')) return true;
  const path = String((proc && proc.path) || '').toLowerCase().replace(/\//g, '\\');
  if (path.startsWith('c:\\windows\\') || path.includes('\\windows\\system32\\') || path.includes('\\windows\\syswow64\\')) return true;
  if (path.includes('\\windows defender\\') || path.includes('\\microsoft defender\\')) return true;
  if (path.includes('\\windowsapps\\microsoft.')) return true;
  return false;
}

function applyPerf(cfg) {
  const enabled = !!cfg.perfEnabled;
  const strength = Math.max(0, Math.min(100, Number(cfg.perfStrength) || 0));
  const base = { ...cfg, reduceMotion: false, perfActive: false, perfStrength: strength };
  if (!enabled || strength <= 0) return base;
  const t = strength / 100;
  const blurCap = Math.round(16 * (1 - t));
  const maxCap = Math.max(8, Math.round(40 - 28 * t));
  const miniCap = Math.max(3, Math.round(8 - 4 * t));
  return {
    ...base,
    perfActive: true,
    blur: Math.min(Number(cfg.blur) || 0, blurCap),
    maxMessages: Math.min(Number(cfg.maxMessages) || 30, maxCap),
    miniMax: Math.min(Number(cfg.miniMax) || 5, miniCap),
    reduceMotion: t >= 0.66,
    perfBlurCap: blurCap,
    perfMaxCap: maxCap,
  };
}

function profileById(id) {
  return PROFILES.find(p => p.id === id) || PROFILES[0];
}

function normalizeWhitelist(list) {
  const out = [];
  for (const item of list || []) {
    const name = baseName(item);
    if (!name || isProtected({ name })) continue;
    if (!out.includes(name)) out.push(name);
  }
  return out.slice(0, 100);
}

function filterCandidates(list, whitelist) {
  const allow = new Set(normalizeWhitelist(whitelist));
  const selectable = [];
  let protectedCount = 0;
  let whitelistSkipped = 0;
  for (const raw of list || []) {
    const proc = {
      pid: Number(raw.pid ?? raw.ProcessId ?? raw.Id),
      name: raw.name || raw.ProcessName || raw.Name || '',
      path: raw.path || raw.ExecutablePath || raw.Path || '',
      title: raw.title || raw.MainWindowTitle || '',
      mem: Number(raw.mem ?? raw.WorkingSet64 ?? raw.WorkingSetSize ?? 0),
    };
    if (!Number.isInteger(proc.pid) || proc.pid <= 4) { protectedCount++; continue; }
    if (isProtected(proc)) { protectedCount++; continue; }
    if (allow.has(baseName(proc.name))) { whitelistSkipped++; continue; }
    selectable.push(proc);
  }
  selectable.sort((a, b) => b.mem - a.mem);
  return { selectable: selectable.slice(0, 80), protectedCount, whitelistSkipped };
}

function parseProcessJson(text) {
  if (!text || !String(text).trim()) return [];
  const data = JSON.parse(text);
  return Array.isArray(data) ? data : [data];
}

function planClose(processes, pids, { whitelist = [], force = false, forceConfirmed = false } = {}) {
  if (force && forceConfirmed !== true) {
    const err = new Error('强制结束需要单独确认。未保存的内容可能会丢失。');
    err.code = 'NEED_FORCE_CONFIRM';
    throw err;
  }
  const byId = new Map();
  for (const p of processes || []) byId.set(Number(p.pid), p);
  const allowed = [];
  const rejected = [];
  for (const id of pids || []) {
    const pid = Number(id);
    if (!Number.isInteger(pid) || pid <= 4) { rejected.push({ pid, reason: 'invalid' }); continue; }
    const proc = byId.get(pid);
    if (!proc) { rejected.push({ pid, reason: 'missing' }); continue; }
    if (isProtected(proc)) { rejected.push({ pid, name: proc.name, reason: 'protected' }); continue; }
    if (normalizeWhitelist(whitelist).includes(baseName(proc.name))) { rejected.push({ pid, name: proc.name, reason: 'whitelist' }); continue; }
    allowed.push({ pid, name: String(proc.name), title: String(proc.title || '') });
  }
  return { allowed, rejected, force: !!force };
}

function assertNoWildcard(command) {
  const s = String(command);
  if (/\*/.test(s) || /taskkill/i.test(s) || /Stop-Process\s+-Name/i.test(s) || /\/IM\b/i.test(s)) {
    throw new Error('拒绝包含通配符或按进程名结束的命令');
  }
  if (/RunAs|Start-Process\b|-Verb\b|reg(\.exe)?\s+add|sc(\.exe)?\s+stop|Set-MpPreference|DisableRealtimeMonitoring/i.test(s)) {
    throw new Error('拒绝提权、注册表或关闭安全功能的命令');
  }
}

function psIdList(pids) {
  const ids = (pids || []).map(n => Number(n)).filter(n => Number.isInteger(n) && n > 4);
  if (!ids.length) throw new Error('没有可关闭的进程');
  return ids.join(',');
}

function gracefulScript(pids) {
  const list = psIdList(pids);
  const script = `$ids = @(${list})\nforeach ($id in $ids) {\n  $p = Get-Process -Id $id -ErrorAction SilentlyContinue\n  if ($p) { [void]$p.CloseMainWindow() }\n}\n`;
  assertNoWildcard(script);
  if (/ -Force\b/.test(script)) throw new Error('优雅关闭不能带 -Force');
  return script;
}

function forceScript(pids) {
  const list = psIdList(pids);
  const script = `Stop-Process -Id ${list} -Force\n`;
  assertNoWildcard(script);
  return script;
}

function scanScript() {
  const script = [
    "$rows = foreach ($p in Get-Process) {",
    "  $path = ''",
    "  try { $path = $p.Path } catch {}",
    "  [PSCustomObject]@{ pid = $p.Id; name = $p.ProcessName; mem = [int64]$p.WorkingSet64; title = [string]$p.MainWindowTitle; path = [string]$path }",
    "}",
    "$rows | ConvertTo-Json -Compress -Depth 3",
  ].join('\n');
  if (/Stop-Process|taskkill|CloseMainWindow|\*/i.test(script)) throw new Error('扫描脚本必须是只读的');
  return script;
}

module.exports = {
  PROFILES, GUIDANCE, DENY, isProtected, applyPerf, profileById, normalizeWhitelist,
  filterCandidates, parseProcessJson, planClose, assertNoWildcard, gracefulScript, forceScript, scanScript,
};
