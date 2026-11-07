// B 站直播弹幕协议客户端（自写实现）
const WebSocket = require('ws');
const zlib = require('zlib');
const crypto = require('crypto');
const { EventEmitter } = require('events');

const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';
const MIXIN = [46,47,18,2,53,8,23,32,15,50,10,31,58,3,45,35,27,43,5,49,33,9,42,19,29,28,14,39,12,38,41,13,37,48,7,16,24,55,40,61,26,17,0,1,60,51,30,4,22,25,54,21,56,59,6,63,57,62,11,36,20,34,44,52];

// 极简 protobuf 解码（INTERACT_WORD_V2）：1 uid, 2 uname, 5 msg_type, 9 fans_medal{3 medal_name? ...}
function pbFields(buf) {
  const f = {}; let i = 0;
  const varint = () => { let r = 0n, s = 0n, b; do { b = buf[i++]; r |= BigInt(b & 0x7f) << s; s += 7n; } while (b & 0x80); return r; };
  while (i < buf.length) { const k = Number(varint()), no = k >> 3, wt = k & 7; let v;
    if (wt === 0) v = varint(); else if (wt === 2) { const l = Number(varint()); v = buf.subarray(i, i + l); i += l; } else if (wt === 1) { i += 8; continue; } else if (wt === 5) { i += 4; continue; } else break;
    (f[no] = f[no] || []).push(v); }
  return f;
}
function decodeInteract(buf) {
  const f = pbFields(buf);
  return { uid: Number(f[1]?.[0] || 0), uname: f[2]?.[0]?.toString('utf8') || '', msg_type: Number(f[5]?.[0] || 0) };
}
function parseCookie(c) { const o = {}; (c || '').split(';').forEach(p => { const i = p.indexOf('='); if (i > 0) o[p.slice(0, i).trim()] = p.slice(i + 1).trim(); }); return o; }

class BiliLive extends EventEmitter {
  constructor(opts) { super(); this.opts = opts; this.cookie = opts.cookie || ''; this.closed = false; this.retry = 0; this.faceCache = new Map(); this.giftMap = new Map(); }
  async req(url) {
    const headers = { 'User-Agent': UA, Referer: 'https://live.bilibili.com/', Origin: 'https://live.bilibili.com' };
    if (this.cookie) headers.Cookie = this.cookie;
    const r = await fetch(url, { headers }); return r.json();
  }
  async wbiSign(params) {
    if (!this.mixKey) {
      const nav = await this.req('https://api.bilibili.com/x/web-interface/nav');
      const k = u => u.split('/').pop().split('.')[0];
      const raw = k(nav.data.wbi_img.img_url) + k(nav.data.wbi_img.sub_url);
      this.mixKey = MIXIN.map(i => raw[i]).join('').slice(0, 32);
      this.selfUid = nav.data.mid || 0;
    }
    params.wts = Math.floor(Date.now() / 1000);
    const q = Object.keys(params).sort().map(k => `${encodeURIComponent(k)}=${encodeURIComponent(String(params[k]).replace(/[!'()*]/g, ''))}`).join('&');
    return q + '&w_rid=' + crypto.createHash('md5').update(q + this.mixKey).digest('hex');
  }
  async resolveRoom(id) {
    if (!/^\d+$/.test(String(id))) throw Object.assign(new Error('房间号格式不正确'), { fatal: true });
    try { const j = await this.req(`https://api.live.bilibili.com/room/v1/Room/room_init?id=${id}`); if (j.code === 0) { this.liveStatus = j.data.live_status; return j.data.room_id; } if (j.code === 60004) throw new Error('直播间不存在'); } catch (e) { if (e.message === '直播间不存在') throw Object.assign(e, { fatal: true }); }
    const j = await this.req(`https://api.live.bilibili.com/xlive/web-room/v1/index/getInfoByRoom?room_id=${id}`);
    if (j.code !== 0 || !j.data?.room_info) throw Object.assign(new Error('直播间不存在'), { fatal: true });
    this.liveStatus = j.data.room_info.live_status; return j.data.room_info.room_id;
  }
  async ensureBuvid() {
    const ck = parseCookie(this.cookie);
    if (ck.buvid3) return ck.buvid3;
    try { const j = await this.req('https://api.bilibili.com/x/frontend/finger/spi'); const b = j.data.b_3; this.cookie = (this.cookie ? this.cookie + '; ' : '') + 'buvid3=' + b; return b; } catch { return ''; }
  }
  async loadGifts() {
    try { const j = await this.req(`https://api.live.bilibili.com/xlive/web-room/v1/giftPanel/roomGiftConfig?platform=pc&room_id=${this.roomId}`);
      for (const g of (j.data.global_gift?.list || j.data.list || [])) this.giftMap.set(g.id, g.img_basic || g.webp); } catch {}
  }
  async face(uid) {
    if (!uid) return '';
    if (this.faceCache.has(uid)) return this.faceCache.get(uid);
    const p = this.req(`https://api.bilibili.com/x/web-interface/card?mid=${uid}`).then(j => j?.data?.card?.face || '').catch(() => '');
    this.faceCache.set(uid, p); const f = await p; this.faceCache.set(uid, f); return f;
  }
  async start() {
    this.emit('status', '正在解析房间号…');
    this.roomId = await this.resolveRoom(this.opts.roomId);
    const buvid = await this.ensureBuvid();
    const q = await this.wbiSign({ id: this.roomId, type: 0, web_location: '444.8' });
    const info = await this.req('https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?' + q);
    if (info.code !== 0) throw new Error('getDanmuInfo 失败: ' + info.message);
    this.loadGifts();
    const h = info.data.host_list[0];
    const uid = Number(parseCookie(this.cookie).DedeUserID) || this.selfUid || 0;
    this.connect(`wss://${h.host}:${h.wss_port}/sub`, { uid, roomid: this.roomId, protover: 3, buvid, platform: 'web', type: 2, key: info.data.token });
  }
  connect(url, auth) {
    this.emit('status', '正在连接弹幕服务器…');
    const ws = this.ws = new WebSocket(url, { headers: { 'User-Agent': UA, Origin: 'https://live.bilibili.com' } });
    ws.on('open', () => { ws.send(BiliLive.pack(7, JSON.stringify(auth))); });
    ws.on('message', d => { this.lastRx = Date.now(); this.onData(Buffer.from(d)); });
    this.lastRx = Date.now(); clearInterval(this.wd); this.wd = setInterval(() => { if (Date.now() - this.lastRx > 75000) { clearInterval(this.wd); try { ws.terminate(); } catch {} } }, 10000);
    ws.on('close', () => { clearInterval(this.hb); clearInterval(this.wd); this.scheduleRetry('连接断开'); });
    ws.on('error', e => this.emit('error', e));
  }
  scheduleRetry(why) {
    if (this.closed || this.retryTimer) return;
    const s = Math.min(60, 2 ** Math.min(this.retry++, 6));
    this.emit('status', `${why}，${s} 秒后重连…`);
    this.retryTimer = setTimeout(() => { this.retryTimer = null; if (!this.closed) this.start().catch(e => this.scheduleRetry('重连失败：' + e.message)); }, s * 1000);
  }
  stop() { clearInterval(this.wd); clearTimeout(this.retryTimer); this.retryTimer = null; this.closed = true; clearInterval(this.hb); try { this.ws?.close(); } catch {} }
  static pack(op, body = '') { const b = Buffer.from(body); const h = Buffer.alloc(16); h.writeUInt32BE(16 + b.length, 0); h.writeUInt16BE(16, 4); h.writeUInt16BE(1, 6); h.writeUInt32BE(op, 8); h.writeUInt32BE(1, 12); return Buffer.concat([h, b]); }
  onData(buf) {
    let off = 0;
    while (off + 16 <= buf.length) {
      const len = buf.readUInt32BE(off), hl = buf.readUInt16BE(off + 4), ver = buf.readUInt16BE(off + 6), op = buf.readUInt32BE(off + 8);
      const body = buf.subarray(off + hl, off + len); off += len;
      if (op === 8) { this.retry = 0; clearInterval(this.hb); this.emit('status', '已连接 · 房间 ' + this.roomId + (this.liveStatus === 1 ? '' : '（未开播，开播后自动显示弹幕）')); this.emit('connected', this.roomId); const send = () => this.ws.readyState === 1 && this.ws.send(BiliLive.pack(2, '[object Object]')); send(); this.hb = setInterval(send, 30000); }
      else if (op === 3) this.emit('popularity', body.readUInt32BE(0));
      else if (op === 5) {
        try { if (ver === 2) { this.onData(zlib.inflateSync(body)); continue; } if (ver === 3) { this.onData(zlib.brotliDecompressSync(body)); continue; } } catch { continue; }
        {
        try { this.handle(JSON.parse(body.toString('utf8'))).catch(() => {}); } catch {} }
      }
    }
  }
  async handle(m) {
    const cmd = (m.cmd || '').split(':')[0];
    const out = x => this.emit('message', x);
    if (cmd === 'DANMU_MSG') {
      const i = m.info, ext = i[0][15] || {}, u = ext.user || {};
      const med = i[3] && i[3].length && i[3][1] ? { level: i[3][0], name: i[3][1], anchor: i[3][2], roomid: i[3][3], color: i[3][4] } : null;
      const emo = i[0][13] && i[0][13].url ? i[0][13].url : null;
      const msg = { type: 'danmaku', uid: i[2][0], uname: u.base?.name || i[2][1], face: u.base?.face || '', text: i[1], emote: emo,
        admin: !!i[2][2], vip: !!(i[2][3] || i[2][4]), guard: i[7] || 0, medal: med, ulevel: i[4]?.[0] || 0 };
      if (!msg.face) msg.face = await this.face(msg.uid);
      out(msg);
    } else if (cmd === 'SEND_GIFT') {
      const d = m.data;
      out({ type: 'gift', uid: d.uid, uname: d.uname, face: d.face || await this.face(d.uid), gift: d.giftName, action: d.action || '投喂', num: d.num,
        icon: d.gift_info?.webp || d.gift_info?.img_basic || this.giftMap.get(d.giftId) || '', coin: d.coin_type, price: d.total_coin || d.price * d.num,
        medal: d.medal_info?.medal_name && d.medal_info.medal_level ? { level: d.medal_info.medal_level, name: d.medal_info.medal_name, anchor: d.medal_info.anchor_uname, roomid: d.medal_info.anchor_roomid, color: d.medal_info.medal_color } : null });
    } else if (cmd === 'SUPER_CHAT_MESSAGE') {
      const d = m.data;
      out({ type: 'superchat', uid: d.uid, uname: d.user_info.uname, face: d.user_info.face, text: d.message, price: d.price, color: d.background_bottom_color || d.background_color, duration: d.time,
        medal: d.medal_info?.medal_name && d.medal_info.medal_level ? { level: d.medal_info.medal_level, name: d.medal_info.medal_name, anchor: d.medal_info.anchor_uname, roomid: d.medal_info.anchor_roomid, color: d.medal_info.medal_color } : null });
    } else if (cmd === 'GUARD_BUY') {
      const d = m.data;
      out({ type: 'guard', uid: d.uid, uname: d.username, face: await this.face(d.uid), level: d.guard_level, gift: d.gift_name || ['', '总督', '提督', '舰长'][d.guard_level], num: d.num, price: d.price / 1000 });
    } else if (cmd === 'INTERACT_WORD' || cmd === 'INTERACT_WORD_V2') {
      let d = m.data;
      if (cmd === 'INTERACT_WORD_V2') { try { d = decodeInteract(Buffer.from(m.data.pb, 'base64')); } catch { return; } }
      const t = { 1: ['enter', '进入直播间'], 2: ['follow', '按下了关注'], 3: ['share', '分享了直播间'], 4: ['follow', '按下了关注'], 5: ['follow', '与主播互粉了'] }[d.msg_type];
      if (!t) return;
      out({ type: t[0], uid: d.uid, uname: d.uname, face: '', action: t[1], medal: d.fans_medal?.medal_name && d.fans_medal.medal_level ? { level: d.fans_medal.medal_level, name: d.fans_medal.medal_name, color: d.fans_medal.medal_color } : null });
    } else if (cmd === 'LIKE_INFO_V3_CLICK') {
      const d = m.data;
      out({ type: 'like', uid: d.uid, uname: d.uname, face: '', action: d.like_text || '点赞了', medal: d.fans_medal?.medal_name && d.fans_medal.medal_level ? { level: d.fans_medal.medal_level, name: d.fans_medal.medal_name, color: d.fans_medal.medal_color } : null });
    } else if (cmd === 'LIKE_INFO_V3_UPDATE') this.emit('stats', { likes: m.data.click_count });
    else if (cmd === 'ONLINE_RANK_COUNT') this.emit('stats', { online: m.data.online_count ?? m.data.count, highEnergy: m.data.count });
    else if (cmd === 'WATCHED_CHANGE') { this.emit('watched', m.data.text_large); this.emit('stats', { watched: m.data.text_large || m.data.num }); }
  }
}
module.exports = { BiliLive };
