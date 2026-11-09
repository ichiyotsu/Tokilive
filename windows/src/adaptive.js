/** 只统计窗口矩形之外的像素，避免把悬浮窗自己采进去。 */
function luminanceOutside(bgra, width, height, rect) {
  if (!bgra || width < 2 || height < 2) return { ok: false, reason: 'no-bitmap' };
  const x0 = clamp(rect.x, 0, width);
  const y0 = clamp(rect.y, 0, height);
  const x1 = clamp(rect.x + rect.width, 0, width);
  const y1 = clamp(rect.y + rect.height, 0, height);
  let sum = 0;
  let n = 0;
  const step = Math.max(1, Math.floor(Math.max(width, height) / 80));
  for (let y = 0; y < height; y += step) {
    for (let x = 0; x < width; x += step) {
      if (x >= x0 && x < x1 && y >= y0 && y < y1) continue;
      const i = (y * width + x) * 4;
      const b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
      sum += (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
      n++;
    }
  }
  if (n < 8) return { ok: false, reason: 'too-few', message: '窗口几乎铺满采样图，无法判断外侧背景。' };
  const luminance = sum / n;
  return { ok: true, luminance, samples: n, text: luminance > 0.6 ? 'dark' : 'light' };
}

function clamp(n, a, b) { return Math.min(b, Math.max(a, n)); }

function mapRect(windowRect, displayRect, thumbW, thumbH) {
  const sx = thumbW / Math.max(1, displayRect.width);
  const sy = thumbH / Math.max(1, displayRect.height);
  return {
    x: Math.round((windowRect.x - displayRect.x) * sx),
    y: Math.round((windowRect.y - displayRect.y) * sy),
    width: Math.round(windowRect.width * sx),
    height: Math.round(windowRect.height * sy),
  };
}

module.exports = { luminanceOutside, mapRect };
