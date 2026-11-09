// MD3 配色：种子色 → 色调调色板(TonalPalette) → 角色(role) → CSS 变量
// 每种消息卡片映射到 MD3 角色；可单独指定“覆盖色”，此时该卡片使用由覆盖色生成的调色板。
let mcu;
const GOLD = '#C9B46E', PINK = '#FF5FC8';
async function buildPalette(seed, dark, overrides = {}) {
  mcu = mcu || await import('@material/material-color-utilities');
  const { argbFromHex, hexFromArgb, themeFromSourceColor, TonalPalette, Blend } = mcu;
  const A = h => { try { return argbFromHex(h); } catch { return null; } };
  const src = A(seed || '#6750A4') ?? argbFromHex('#6750A4');
  const t = themeFromSourceColor(src), s = (dark ? t.schemes.dark : t.schemes.light).toJSON(), p = t.palettes;
  const v = {}; for (const [k, a] of Object.entries(s)) v['--md-' + k.replace(/[A-Z]/g, c => '-' + c.toLowerCase())] = hexFromArgb(a);
  const N = p.neutral, NV = p.neutralVariant;
  const tone = (pal, l, d) => hexFromArgb(pal.tone(dark ? d : l));
  Object.assign(v, { '--md-surface-container-lowest': tone(N, 100, 4), '--md-surface-container-low': tone(N, 96, 10), '--md-surface-container': tone(N, 94, 12), '--md-surface-container-high': tone(N, 92, 17), '--md-surface-container-highest': tone(N, 90, 22) });
  // 自定义色（MD3 custom color：与种子色协调 harmonize）
  const custom = h => TonalPalette.fromInt(Blend.harmonize(argbFromHex(h), src));
  const ov = k => { const a = overrides[k] && A(overrides[k]); return a == null ? null : TonalPalette.fromInt(a); };
  const set = (k, pal, r) => { for (const [n, [l, d]] of Object.entries(r)) v[`--${k}-${n}`] = tone(pal, l, d); };
  // 弹幕：surface-container-highest 系（中性色），描边 outline-variant
  const dm = ov('danmaku'); set('dm', dm || NV, { bg: [90, 30], line: [78, 60], name: [50, 70], text: [10, 95] });
  // 上舰：primary-container 填充 + on-primary-container 描边
  set('guard', ov('guard') || p.primary, { bg: [85, 30], line: [25, 80], name: [20, 90], verb: [55, 70], text: [10, 95] });
  // 醒目留言：primary 实心 + on-primary 文字 + primary-container 描边
  set('sc', ov('superchat') || p.primary, { bg: [30, 80], line: [80, 30], name: [75, 40], text: [100, 10], price: [90, 20] });
  // 礼物：金色自定义色（协调后）的 container 角色
  set('gift', ov('gift') || custom(GOLD), { bg: [85, 30], line: [20, 80], name: [20, 90], verb: [55, 70], text: [10, 95] });
  // 进场：surface / inverse 高对比描边
  set('enter', ov('enter') || N, { bg: [99, 4], line: [10, 95], name: [50, 65], text: [10, 95] });
  // 互动小窗：沿用弹幕角色
  set('mini', ov('mini') || NV, { bg: [90, 30], line: [78, 60], name: [50, 70], text: [10, 95], accent: [40, 80] });
  // 互动小窗统计条：浅色=白底浅灰细描边；深色=灰底黑色粗描边（参考设计稿两种变体）
  set('miniw', ov('mini') || N, { bg: [99, 85], line: [88, 0] });
  // 透明样式：无填充，仅描边 + 带阴影文字（取浅色色调）
  const T = (pal, r) => { for (const [n, tt] of Object.entries(r)) v[n] = hexFromArgb(pal.tone(tt)); };
  T(dm || NV, { '--tr-dm-line': 75, '--tr-dm-name': 45, '--tr-dm-text': 8, '--tr-em-bg': 92 });
  T(ov('guard') || p.primary, { '--tr-guard-line': 80, '--tr-guard-name': 35, '--tr-guard-text': 30 });
  T(ov('superchat') || p.primary, { '--tr-sc-line': 25, '--tr-sc-name': 85, '--tr-sc-text': 30 });
  T(ov('gift') || custom(GOLD), { '--tr-gift-line': 85 }); T(p.primary, { '--tr-gift-text': 30, '--tr-verb': 40 });
  T(ov('enter') || N, { '--tr-enter-line': 87, '--tr-enter-name': 40, '--tr-enter-text': 8 });
  // 房管徽章：error 角色
  set('badge', p.error, { bg: [60, 40], fg: [100, 95] });
  // 粉丝牌（统一样式）：粉色自定义色，两种模式保持一致；礼物/上舰内用金色深色版
  const pk = custom(PINK), gd = custom(GOLD), fix = (pal, r) => { for (const [n, tt] of Object.entries(r)) v[n] = hexFromArgb(pal.tone(tt)); };
  fix(pk, { '--medal-bg': 72, '--medal-fg': 10, '--medal-lv-bg': 20, '--medal-lv-fg': 95 });
  fix(gd, { '--gmedal-bg': 45, '--gmedal-fg': 10, '--gmedal-lv-bg': 22, '--gmedal-lv-fg': 92 });
  return v;
}
module.exports = { buildPalette };
