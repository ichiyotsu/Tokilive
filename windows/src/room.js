const NAV = 'https://api.bilibili.com/x/web-interface/nav';
const LIVE_INFO = 'https://api.live.bilibili.com/xlive/web-ucenter/user/live_info';

function fail(reason, message) {
  return { ok: false, reason, message, roomId: null };
}

function roomFrom(json) {
  const d = json && json.data;
  if (!d) return 0;
  const id = d.room_id || d.roomid || d.room_id_str || 0;
  const n = Number(id);
  return Number.isFinite(n) && n > 0 ? String(n) : 0;
}

/**
 * 用已登录 Cookie 读自己的直播间。
 * 失败时 roomId 为 null，调用方不得改输入框。
 * fetchFn(url, cookie) 返回已解析的 JSON，抛错表示网络/接口不可达。
 */
async function lookupOwnRoom(cookie, fetchFn) {
  const ck = String(cookie || '').trim();
  if (!ck || !/(^|;\s*)SESSDATA=/.test(ck)) {
    return fail('no-login', '未登录，无法读取自己的直播间。请先扫码登录。房间号没有改动。');
  }
  let nav;
  try { nav = await fetchFn(NAV, ck); }
  catch (e) { return fail('no-api', '无法访问 B 站账号接口（' + (e.message || '网络错误') + '）。房间号没有改动。'); }
  if (!nav || nav.code === -101 || nav.data?.isLogin === false || !nav.data?.isLogin) {
    return fail('no-login', '登录已失效，或这个 Cookie 没有账号接口权限。房间号没有改动。');
  }
  const mid = nav.data.mid;
  let info;
  try { info = await fetchFn(LIVE_INFO, ck); }
  catch (e) {
    if (!mid) return fail('no-api', '直播间接口无法访问。房间号没有改动。');
    try { info = await fetchFn('https://api.live.bilibili.com/room/v1/Room/getRoomInfoOld?mid=' + encodeURIComponent(mid), ck); }
    catch (e2) { return fail('no-api', '直播间接口无法访问（' + (e2.message || '网络错误') + '）。房间号没有改动。'); }
  }
  if (info && info.code === -101) return fail('no-login', '登录已失效。房间号没有改动。');
  if (info && info.code != null && info.code !== 0 && !roomFrom(info)) {
    if (!mid) return fail('no-api', '直播间接口返回错误（' + info.code + '）。房间号没有改动。');
    try {
      const old = await fetchFn('https://api.live.bilibili.com/room/v1/Room/getRoomInfoOld?mid=' + encodeURIComponent(mid), ck);
      const id = roomFrom(old);
      if (!id) return fail('no-room', '这个账号还没有直播间。房间号没有改动。');
      return { ok: true, reason: 'ok', message: '已读取房间号 ' + id + '，确认后点保存并连接。', roomId: id };
    } catch (e) {
      return fail('no-api', '直播间接口无法访问。房间号没有改动。');
    }
  }
  const id = roomFrom(info);
  if (!id) return fail('no-room', '这个账号还没有直播间。房间号没有改动。');
  return { ok: true, reason: 'ok', message: '已读取房间号 ' + id + '，确认后点保存并连接。', roomId: id };
}

module.exports = { lookupOwnRoom, NAV, LIVE_INFO };
