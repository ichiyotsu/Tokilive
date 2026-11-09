/** 只解析带 begin 的 <p>，没有时间就不编造逐字时间。 */
function parseTtml(xml) {
  const lines = [];
  const re = /<p\b([^>]*)>([\s\S]*?)<\/p>/gi;
  let match;
  while ((match = re.exec(String(xml || '')))) {
    const attrs = match[1];
    const begin = /begin="([^"]+)"/i.exec(attrs);
    if (!begin) continue;
    const text = match[2].replace(/<[^>]+>/g, '').replace(/\s+/g, ' ').trim();
    if (!text) continue;
    lines.push({ time: clockToSeconds(begin[1]), text, translation: '', words: [] });
  }
  lines.sort((a, b) => a.time - b.time);
  return lines;
}

function clockToSeconds(value) {
  const parts = String(value).trim().split(':').map(Number);
  if (parts.some(n => !Number.isFinite(n))) return 0;
  if (parts.length === 3) return parts[0] * 3600 + parts[1] * 60 + parts[2];
  if (parts.length === 2) return parts[0] * 60 + parts[1];
  return parts[0] || 0;
}

module.exports = { parseTtml, clockToSeconds };
