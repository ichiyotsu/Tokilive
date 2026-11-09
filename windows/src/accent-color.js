/** Windows COLORREF：0x00BBGGRR。读不到时返回 null，不假装有系统强调色。 */
function colorFromColorRef(value) {
  const n = Number(value);
  if (!Number.isFinite(n)) return null;
  const u = n >>> 0;
  const r = u & 255;
  const g = (u >> 8) & 255;
  const b = (u >> 16) & 255;
  const hex = '#' + [r, g, b].map(c => c.toString(16).padStart(2, '0')).join('');
  if (hex === '#000000' && u === 0) return null;
  return hex;
}

module.exports = { colorFromColorRef };
