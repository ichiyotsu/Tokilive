const MIN_GAP_MS = 1500;
const MAX_LEN = 40;

function csrfFromCookie(cookie) {
  const m = /(?:^|;\s*)bili_jct=([^;]+)/.exec(String(cookie || ''));
  return m ? decodeURIComponent(m[1]) : '';
}

function canSend(cookie) {
  const ck = String(cookie || '');
  if (!/SESSDATA=/.test(ck)) return { ok: false, reason: 'no-login', message: '未登录，不能代发弹幕。不会匿名发送。' };
  if (!csrfFromCookie(ck)) return { ok: false, reason: 'no-csrf', message: '登录态缺少 bili_jct，请重新扫码。没有发送。' };
  return { ok: true };
}

function validateText(text) {
  const msg = String(text || '').replace(/\r?\n/g, ' ').trim();
  if (!msg) return { ok: false, message: '没有内容，没有发送。' };
  if ([...msg].length > MAX_LEN) return { ok: false, message: `弹幕超过 ${MAX_LEN} 字，没有发送。` };
  return { ok: true, text: msg };
}

function allowSend(now, lastSentAt) {
  if (lastSentAt && now - lastSentAt < MIN_GAP_MS) {
    return { ok: false, message: '发送太密，请稍等一秒半。没有发送。' };
  }
  return { ok: true };
}

function formBody({ roomId, text, csrf }) {
  const params = new URLSearchParams({
    bubble: '0',
    msg: text,
    color: '16777215',
    mode: '1',
    fontsize: '25',
    rnd: String(Math.floor(Date.now() / 1000)),
    roomid: String(roomId),
    csrf,
    csrf_token: csrf,
    dm_type: '0',
  });
  return params.toString();
}

function interpretResponse(json) {
  const code = json && json.code;
  if (code === 0) return { ok: true, message: '已发送。' };
  if (code === -101 || code === -111) return { ok: false, reason: 'auth', message: '登录已失效，没有发送成功。' };
  if (code === 10031 || code === 1003) return { ok: false, reason: 'limit', message: json.message || '被 B 站限流或内容过长，没有发出去。' };
  return { ok: false, reason: 'api', message: (json && json.message) || ('发送失败（' + code + '）。') };
}

module.exports = {
  MIN_GAP_MS, MAX_LEN, csrfFromCookie, canSend, validateText, allowSend, formBody, interpretResponse,
};
