const n = ['夜宵', '咕咕鸽', '星川', '阿梨', '小林'];
const r = a => a[Math.floor(Math.random() * a.length)];
function sample(type) {
  const base = { uid: 0, uname: r(n), face: '', medal: { level: 12 + Math.floor(Math.random() * 20), name: '粉丝团', color: 2951253 } };
  if (type === 'gift') return { ...base, type: 'gift', gift: '小花花', action: '投喂', num: 10, icon: '', price: 1000, coin: 'gold' };
  if (type === 'superchat') return { ...base, type: 'superchat', text: '主播今天好帅！这是测试醒目留言～', price: 30, color: '#2A60B2' };
  if (type === 'guard') return { ...base, type: 'guard', gift: '舰长', level: 3, num: 1, price: 198 };
  if (type === 'like') return { ...base, type: 'like', action: '点赞了' };
  if (type === 'share') return { ...base, type: 'share', action: '分享了直播间' };
  if (type === 'follow') return { ...base, type: 'follow', action: '按下了关注' };
  if (type === 'enter') return { ...base, type: 'enter', action: '进入直播间' };
  return { ...base, type: 'danmaku', text: r(['哈哈哈哈哈', '主播好！', '这波操作 666', '前排围观～', '晚上好呀']), ulevel: 20, guard: r([0, 0, 3]) };
}
module.exports = { sample };
