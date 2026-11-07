// B 站网页扫码登录
const QR = require('qrcode');
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';
const H = { 'User-Agent': UA, Referer: 'https://www.bilibili.com/' };
async function generate() {
  const j = await (await fetch('https://passport.bilibili.com/x/passport-login/web/qrcode/generate', { headers: H })).json();
  if (j.code !== 0) throw new Error(j.message);
  return { key: j.data.qrcode_key, dataUrl: await QR.toDataURL(j.data.url, { margin: 1, width: 240 }) };
}
async function poll(key) {
  const r = await fetch('https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key=' + encodeURIComponent(key), { headers: H });
  const j = await r.json(); const c = j.data.code;
  const map = { 86101: '等待扫码', 86090: '已扫码，请在手机上确认', 86038: '二维码已过期，点击刷新', 0: '登录成功' };
  const res = { code: c, text: map[c] || j.data.message };
  if (c === 0) {
    const jar = {};
    for (const s of r.headers.getSetCookie?.() || []) { const [kv] = s.split(';'); const i = kv.indexOf('='); jar[kv.slice(0, i).trim()] = kv.slice(i + 1).trim(); }
    try { new URL(j.data.url).searchParams.forEach((v, k) => { if (['SESSDATA', 'bili_jct', 'DedeUserID', 'DedeUserID__ckMd5'].includes(k) && !jar[k]) jar[k] = encodeURIComponent(v); }); } catch {}
    try { const s = await (await fetch('https://api.bilibili.com/x/frontend/finger/spi', { headers: H })).json(); jar.buvid3 = jar.buvid3 || s.data.b_3; jar.buvid4 = jar.buvid4 || s.data.b_4; } catch {}
    res.cookie = Object.entries(jar).map(([k, v]) => `${k}=${v}`).join('; ');
  }
  return res;
}
async function whoami(cookie) {
  if (!cookie) return null;
  try { const j = await (await fetch('https://api.bilibili.com/x/web-interface/nav', { headers: { ...H, Cookie: cookie } })).json();
    return j.data?.isLogin ? { uname: j.data.uname, face: j.data.face, mid: j.data.mid } : null; } catch { return null; }
}
module.exports = { generate, poll, whoami };
