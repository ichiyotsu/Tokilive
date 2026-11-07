const crypto = require('crypto');
const { API, REPO, assetApiUrl, classifyHttp, parseDigest, redirectPlan, redact, verifySha256 } = require('./update-core');

async function githubJson(url, token, fetchImpl = fetch) {
  const headers = {
    'User-Agent': 'tokilive',
    Accept: 'application/vnd.github+json',
    'X-GitHub-Api-Version': '2022-11-28',
  };
  if (token) headers.Authorization = 'Bearer ' + token;
  const res = await fetchImpl(url, { headers });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  return { status: res.status, json, message: classifyHttp(res.status).message };
}

async function resolveToken({ fs, file, safeStorage, env, execFile, log = () => {} }) {
  const note = m => log(redact(m));
  if (file && fs?.existsSync(file) && safeStorage?.isEncryptionAvailable?.()) {
    try {
      const token = safeStorage.decryptString(fs.readFileSync(file));
      if (token && String(token).trim()) return { token: String(token).trim(), source: 'stored' };
    } catch (e) { note('无法解密已保存的令牌：' + (e && e.message)); }
  }
  if (env && String(env).trim()) return { token: String(env).trim(), source: 'env' };
  if (typeof execFile === 'function') {
    try {
      const token = await new Promise((resolve, reject) => {
        execFile('gh', ['auth', 'token', '--hostname', 'github.com'], { timeout: 1500, windowsHide: true, env: { ...process.env, GH_PROMPT_DISABLED: '1', GIT_TERMINAL_PROMPT: '0' } }, (err, stdout) => {
          if (err) reject(err);
          else resolve(String(stdout || '').trim());
        });
      });
      if (token) return { token, source: 'gh' };
    } catch (e) { note('GitHub CLI 不可用'); }
  }
  return { token: '', source: 'none' };
}

async function pump(res, file, fs, onProgress) {
  const total = Number(res.headers.get('content-length') || 0);
  const hash = crypto.createHash('sha256');
  const ws = fs.createWriteStream(file);
  let received = 0;
  const body = res.body;
  if (body && typeof body.getReader === 'function') {
    const reader = body.getReader();
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      const buf = Buffer.from(value);
      hash.update(buf);
      ws.write(buf);
      received += buf.length;
      if (onProgress) onProgress({ received, total });
    }
  } else {
    const buf = Buffer.from(await res.arrayBuffer());
    hash.update(buf);
    ws.write(buf);
    received = buf.length;
    if (onProgress) onProgress({ received, total: total || received });
  }
  await new Promise((ok, err) => { ws.on('error', err); ws.end(ok); });
  return { sha256: hash.digest('hex'), bytes: received };
}

async function downloadToFile({ fetchImpl = fetch, fs, file, url, token, redirects = 0, onProgress }) {
  if (redirects > 5) throw new Error('下载重定向过多，已停止。');
  const headers = { 'User-Agent': 'tokilive', Accept: 'application/octet-stream' };
  if (token) headers.Authorization = 'Bearer ' + token;
  const res = await fetchImpl(url, { headers, redirect: 'manual' });
  const plan = redirectPlan(res.status, res.headers.get('location'));
  if (plan) {
    if (plan.auth) throw new Error('重定向仍携带授权，已停止。');
    return downloadToFile({ fetchImpl, fs, file, url: plan.url, token: null, redirects: redirects + 1, onProgress });
  }
  if (res.status !== 200) {
    const err = new Error(classifyHttp(res.status).message || ('下载失败：HTTP ' + res.status));
    err.status = res.status;
    throw err;
  }
  return pump(res, file, fs, onProgress);
}

async function fetchReleaseList(token, fetchImpl = fetch) {
  const url = `${API}/repos/${REPO}/releases?per_page=20`;
  const res = await githubJson(url, token, fetchImpl);
  if (res.status !== 200 || !Array.isArray(res.json)) {
    const err = new Error(res.message || '无法读取发行版');
    err.status = res.status;
    throw err;
  }
  return res.json;
}

async function downloadVerified({ fetchImpl, fs, file, release, asset, token, onProgress }) {
  const url = assetApiUrl(asset.id);
  if (!url) throw new Error('缺少私人仓库资源地址');
  let expected = parseDigest(asset, '');
  if (!expected) {
    const sums = (release.assets || []).find(a => /^SHA256SUMS(\.txt)?$/i.test(a.name || ''));
    if (sums?.id) {
      const tmp = file + '.sums';
      try {
        await downloadToFile({ fetchImpl, fs, file: tmp, url: assetApiUrl(sums.id), token, onProgress: () => {} });
        expected = parseDigest(asset, fs.readFileSync(tmp, 'utf8'));
      } catch { /* 没有校验文件就继续走 digest */ }
      finally { try { fs.unlinkSync(tmp); } catch {} }
    }
  }
  if (!expected) {
    try { fs.unlinkSync(file); } catch {}
    const err = new Error('这个发行版没有 sha256。已拒绝下载安装，避免装上未校验的文件。');
    err.code = 'NO_CHECKSUM';
    throw err;
  }
  const got = await downloadToFile({ fetchImpl, fs, file, url, token, onProgress });
  if (!verifySha256(got.sha256, expected)) {
    try { fs.unlinkSync(file); } catch {}
    const err = new Error('校验和不匹配，安装包已删除，没有进入安装。');
    err.code = 'CHECKSUM';
    throw err;
  }
  return { sha256: got.sha256, bytes: got.bytes, expected };
}

async function startDevice({ clientId, fetchImpl = fetch }) {
  if (!clientId || !/^[\w.-]{8,128}$/.test(clientId)) {
    return { ok: false, message: '需要 GitHub OAuth App 的 client id（公开标识，不是密钥）。没有的话请用 GitHub CLI，或保存一个只读令牌。' };
  }
  const res = await fetchImpl('https://github.com/login/device/code', {
    method: 'POST',
    headers: { Accept: 'application/json', 'User-Agent': 'tokilive', 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ client_id: clientId, scope: 'repo' }).toString(),
  });
  const json = await res.json().catch(() => ({}));
  if (!res.ok || !json.device_code) return { ok: false, message: '设备码申请失败（HTTP ' + res.status + '）。' };
  return {
    ok: true,
    deviceCode: json.device_code,
    userCode: json.user_code,
    uri: json.verification_uri,
    interval: json.interval || 5,
  };
}

async function pollDevice({ clientId, deviceCode, fetchImpl = fetch }) {
  const res = await fetchImpl('https://github.com/login/oauth/access_token', {
    method: 'POST',
    headers: { Accept: 'application/json', 'User-Agent': 'tokilive', 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ client_id: clientId, device_code: deviceCode, grant_type: 'urn:ietf:params:oauth:grant-type:device_code' }).toString(),
  });
  const json = await res.json().catch(() => ({}));
  if (json.access_token) return { ok: true, token: json.access_token };
  if (json.error === 'authorization_pending') return { ok: false, pending: true, message: '还在等待浏览器里的授权。' };
  if (json.error === 'slow_down') return { ok: false, pending: true, slow: true, message: '请求太密，稍等再试。' };
  return { ok: false, pending: false, message: '设备码登录没有完成：' + redact(json.error || ('HTTP ' + res.status)) };
}

module.exports = {
  githubJson, resolveToken, downloadToFile, fetchReleaseList, downloadVerified, startDevice, pollDevice,
};
