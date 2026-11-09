const REPO = 'ichiyotsu/tokilive';
const API = 'https://api.github.com';

const FIRST_UPGRADE_NOTE = '仓库已经公开。1.2.0 起检查更新不需要令牌、设备码或 GitHub CLI。正在使用 1.0.0，或 1.1.0 里那套私人仓库授权的人，需要手动安装 1.2.0 一次。之后的正式版按平台选择安装包，并校验 SHA-256。';

const TOKEN_RE = /(?:ghp_|gho_|ghu_|ghs_|ghr_|github_pat_)[A-Za-z0-9_]+/g;

function redact(value) {
  return String(value == null ? '' : value).replace(TOKEN_RE, '[token]').replace(/Bearer\s+\S+/gi, 'Bearer [token]');
}

function parseVersion(input) {
  const raw = String(input || '').trim().replace(/^v/i, '');
  const m = /^(\d+(?:\.\d+)*)(?:-([0-9A-Za-z.-]+))?$/.exec(raw);
  if (!m) return null;
  return { core: m[1].split('.').map(n => Number(n)), pre: m[2] ? m[2].split('.') : [], raw };
}

function compareVersions(a, b) {
  const pa = parseVersion(a), pb = parseVersion(b);
  if (!pa || !pb) return String(a).localeCompare(String(b));
  const n = Math.max(pa.core.length, pb.core.length);
  for (let i = 0; i < n; i++) {
    const d = (pa.core[i] || 0) - (pb.core[i] || 0);
    if (d) return d;
  }
  if (!pa.pre.length && pb.pre.length) return 1;
  if (pa.pre.length && !pb.pre.length) return -1;
  const m = Math.max(pa.pre.length, pb.pre.length);
  for (let i = 0; i < m; i++) {
    if (pa.pre[i] == null) return -1;
    if (pb.pre[i] == null) return 1;
    const na = Number(pa.pre[i]), nb = Number(pb.pre[i]);
    if (Number.isInteger(na) && Number.isInteger(nb) && String(na) === pa.pre[i] && String(nb) === pb.pre[i]) {
      if (na !== nb) return na - nb;
    } else if (pa.pre[i] !== pb.pre[i]) return pa.pre[i] < pb.pre[i] ? -1 : 1;
  }
  return 0;
}

function isNewer(remote, current) { return compareVersions(remote, current) > 0; }

function isStable(version) {
  const p = parseVersion(version);
  return !!p && p.pre.length === 0;
}

function versionOfRelease(rel) {
  return String(rel.tag_name || rel.version || rel.name || '').replace(/^v/i, '');
}

function distributionKind({ portableFile, portableDir, execPath, packaged }) {
  if (portableFile || portableDir) return 'portable';
  if (/portable/i.test(execPath || '')) return 'portable';
  if (packaged === false) return 'dev';
  return 'setup';
}

function pickAsset(assets, kind) {
  const list = assets || [];
  if (kind === 'android') {
    return list.find(a => /android/i.test(a.name || '') && /\.apk$/i.test(a.name || ''))
      || list.find(a => /\.apk$/i.test(a.name || ''));
  }
  const want = kind === 'portable' ? /-portable\.exe$/i : /-setup\.exe$/i;
  return list.find(a => want.test(a.name || '') && !/blockmap|yml$/i.test(a.name || ''));
}

function pickUpdate(releases, current, kind) {
  const stable = (releases || []).filter(r => r && !r.draft && !r.prerelease && isStable(versionOfRelease(r)));
  stable.sort((a, b) => compareVersions(versionOfRelease(a), versionOfRelease(b)));
  if (!stable.length) return { state: 'none', current, message: '没有找到正式版。' };
  const newest = stable[stable.length - 1];
  const latest = versionOfRelease(newest);
  if (!isNewer(latest, current)) return { state: 'uptodate', current, latest, message: '已经是最新正式版 ' + current + '。' };
  const asset = pickAsset(newest.assets, kind === 'dev' ? 'setup' : kind);
  if (!asset) return { state: 'no-asset', current, latest, kind, message: '发现 ' + latest + '，但没有对应的安装包。' };
  if (!asset.id) return { state: 'no-api-asset', current, latest, message: '安装包缺少 API 资源编号，私人仓库无法校验下载。' };
  return {
    state: 'available', current, latest, kind,
    notes: String(newest.body || '').slice(0, 2000),
    htmlUrl: newest.html_url || `https://github.com/${REPO}/releases/tag/${newest.tag_name || ('v' + latest)}`,
    asset: { id: asset.id, name: asset.name, size: asset.size || 0, digest: asset.digest || '' },
    sums: (() => {
      const s = (newest.assets || []).find(a => /^SHA256SUMS(\.txt)?$/i.test(a.name || ''));
      return s && s.id ? { id: s.id, name: s.name } : null;
    })(),
    message: '可以更新到 ' + latest + '。',
  };
}

function assetApiUrl(id) {
  if (!Number.isInteger(Number(id)) || Number(id) <= 0) return null;
  return `${API}/repos/${REPO}/releases/assets/${Number(id)}`;
}

function redirectPlan(status, location) {
  if (![301, 302, 303, 307, 308].includes(Number(status)) || !location) return null;
  if (!/^https:\/\//i.test(location)) return null;
  return { url: location, auth: false };
}

function parseDigest(asset, checksumText) {
  if (asset && asset.digest) {
    const m = /sha256:([a-f0-9]{64})/i.exec(asset.digest);
    if (m) return m[1].toLowerCase();
  }
  const name = asset && asset.name;
  if (name && checksumText) {
    for (const line of String(checksumText).split(/\r?\n/)) {
      const m = /^([a-f0-9]{64})\s+\*?(\S+)/i.exec(line.trim());
      if (m && m[2] === name) return m[1].toLowerCase();
    }
  }
  return null;
}

function verifySha256(actualHex, expectedHex) {
  const a = String(actualHex || '').toLowerCase();
  const b = String(expectedHex || '').toLowerCase();
  if (!/^[a-f0-9]{64}$/.test(a) || !/^[a-f0-9]{64}$/.test(b)) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

function classifyHttp(status) {
  const code = Number(status);
  if (code === 200 || code === 302) return { ok: true, message: '' };
  if (code === 401) return { ok: false, message: '私人仓库拒绝了这个身份（401）。请重新授权。令牌内容不会显示。' };
  if (code === 403) return { ok: false, message: '请求被拒绝（403）。可能是权限不够，或 GitHub 的访问频率到了上限。' };
  if (code === 404) return { ok: false, message: '找不到发行版（404）。私人仓库在没有权限时也会显示成 404。请确认令牌可以读取 ichiyotsu/tokilive 的内容。' };
  return { ok: false, message: 'GitHub 返回 HTTP ' + code + '。' };
}

function publicDeviceStart(raw) {
  if (!raw || !raw.ok) return { ok: false, message: redact(raw && raw.message) || '设备码登录没有开始。' };
  return { ok: true, userCode: raw.userCode, uri: raw.uri, message: '在浏览器里输入这组设备码，完成后回到这里继续。' };
}

function authStatus({ hasStored, envOk, ghOk }) {
  if (hasStored) return { source: 'stored', label: '已使用保存在系统加密存储中的令牌' };
  if (envOk) return { source: 'env', label: '已使用本机环境变量 TOKILIVE_GITHUB_TOKEN（不会显示内容）' };
  if (ghOk) return { source: 'gh', label: '已使用 GitHub CLI 的登录状态' };
  return { source: 'none', label: '尚未授权，无法读取私人仓库的 Release' };
}

function sealToken(safeStorage, token) {
  const value = String(token || '').trim();
  if (value.length < 8) return { ok: false, message: '令牌太短，没有保存。' };
  if (!safeStorage || typeof safeStorage.isEncryptionAvailable !== 'function' || !safeStorage.isEncryptionAvailable()) {
    return { ok: false, message: '系统加密不可用，没有保存令牌。可以改用已经登录的 GitHub CLI。' };
  }
  return { ok: true, blob: safeStorage.encryptString(value) };
}

function installPlan({ packaged, kind, verified, version }) {
  if (!verified) return { ok: false, message: '安装包还没有通过校验，不能安装。' };
  if (!packaged || kind === 'dev') {
    return { ok: false, dev: true, message: '当前是未打包的开发运行，不能在原地替换程序。从 1.0.0 升级也需要手动安装一次 1.1.0。' };
  }
  if (kind === 'portable') {
    return { ok: true, kind, version, silent: false, args: [], message: '将退出 tokilive，用已校验的新文件替换便携版并重新打开。不会静默覆盖你没有确认的路径。' };
  }
  return { ok: true, kind: 'setup', version, silent: false, args: [], message: '将退出 tokilive 并打开安装程序。安装程序可以选择目录（包括 D:），不会静默安装。' };
}

function portableScript({ pid, source, dest }) {
  const id = Number(pid);
  if (!Number.isInteger(id) || id <= 0) throw new Error('bad pid');
  for (const p of [source, dest]) {
    if (!p || /[\r\n]/.test(p)) throw new Error('bad path');
  }
  const q = s => "'" + String(s).replace(/'/g, "''") + "'";
  const script = [
    "$ErrorActionPreference = 'Stop'",
    `Wait-Process -Id ${id} -ErrorAction SilentlyContinue`,
    'Start-Sleep -Milliseconds 800',
    `Copy-Item -LiteralPath ${q(source)} -Destination ${q(dest)} -Force`,
    `Start-Process -FilePath ${q(dest)}`,
  ].join('\n');
  if (/\*/.test(script) || /taskkill/i.test(script)) throw new Error('bad script');
  return script;
}

function setupArgs() { return []; }

module.exports = {
  REPO, API, FIRST_UPGRADE_NOTE, redact, parseVersion, compareVersions, isNewer, isStable,
  distributionKind, pickAsset, pickUpdate, assetApiUrl, redirectPlan, parseDigest, verifySha256,
  classifyHttp, publicDeviceStart, authStatus, sealToken, installPlan, portableScript, setupArgs,
  versionOfRelease,
};
