const crypto = require('crypto');
const { API, REPO, assetApiUrl, classifyHttp, parseDigest, redirectPlan, verifySha256 } = require('./update-core');

async function githubJson(url, fetchImpl = fetch) {
  const headers = {
    'User-Agent': 'tokilive',
    Accept: 'application/vnd.github+json',
    'X-GitHub-Api-Version': '2022-11-28',
  };
  const res = await fetchImpl(url, { headers });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  return { status: res.status, json, message: classifyHttp(res.status).message };
}

function forgetSavedToken(fs, file) {
  if (file && fs?.existsSync?.(file)) {
    fs.unlinkSync(file);
    return true;
  }
  return false;
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

async function downloadToFile({ fetchImpl = fetch, fs, file, url, redirects = 0, onProgress }) {
  if (redirects > 5) throw new Error('下载重定向过多，已停止。');
  const headers = { 'User-Agent': 'tokilive', Accept: 'application/octet-stream' };
  const res = await fetchImpl(url, { headers, redirect: 'manual' });
  const plan = redirectPlan(res.status, res.headers.get('location'));
  if (plan) {
    if (plan.auth) throw new Error('重定向仍携带授权，已停止。');
    return downloadToFile({ fetchImpl, fs, file, url: plan.url, redirects: redirects + 1, onProgress });
  }
  if (res.status !== 200) {
    const err = new Error(classifyHttp(res.status).message || ('下载失败：HTTP ' + res.status));
    err.status = res.status;
    throw err;
  }
  return pump(res, file, fs, onProgress);
}

async function fetchReleaseList(fetchImpl = fetch) {
  const url = `${API}/repos/${REPO}/releases?per_page=20`;
  const res = await githubJson(url, fetchImpl);
  if (res.status !== 200 || !Array.isArray(res.json)) {
    const err = new Error(res.message || '无法读取发行版');
    err.status = res.status;
    throw err;
  }
  return res.json;
}

async function downloadVerified({ fetchImpl, fs, file, release, asset, onProgress }) {
  const url = assetApiUrl(asset.id);
  if (!url) throw new Error('缺少私人仓库资源地址');
  let expected = parseDigest(asset, '');
  if (!expected) {
    const sums = (release.assets || []).find(a => /^SHA256SUMS(\.txt)?$/i.test(a.name || ''));
    if (sums?.id) {
      const tmp = file + '.sums';
      try {
        await downloadToFile({ fetchImpl, fs, file: tmp, url: assetApiUrl(sums.id), onProgress: () => {} });
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
  const got = await downloadToFile({ fetchImpl, fs, file, url, onProgress });
  if (!verifySha256(got.sha256, expected)) {
    try { fs.unlinkSync(file); } catch {}
    const err = new Error('校验和不匹配，安装包已删除，没有进入安装。');
    err.code = 'CHECKSUM';
    throw err;
  }
  return { sha256: got.sha256, bytes: got.bytes, expected };
}

module.exports = { githubJson, forgetSavedToken, downloadToFile, fetchReleaseList, downloadVerified };
