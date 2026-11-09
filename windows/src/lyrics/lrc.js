const STAMP = /\[(\d+):(\d+(?:\.\d+)?)\]/g;

function parseLrc(text) {
  const lines = [];
  for (const raw of String(text || '').split(/\r?\n/)) {
    const stamps = [...raw.matchAll(STAMP)];
    if (!stamps.length) continue;
    let body = raw.replace(STAMP, '').trim();
    const words = [];
    const wordRe = /<(\d+):(\d+(?:\.\d+)?)>([^<]*)/g;
    let wordMatch;
    let plain = '';
    if (body.includes('<')) {
      while ((wordMatch = wordRe.exec(body))) {
        const t = Number(wordMatch[1]) * 60 + Number(wordMatch[2]);
        const w = wordMatch[3];
        words.push({ time: t, text: w });
        plain += w;
      }
      body = plain || body.replace(/<[^>]*>/g, '');
    }
    const parts = body.split('|').map(s => s.trim());
    const textLine = parts[0] || '';
    const translation = parts[1] || '';
    for (const stamp of stamps) {
      lines.push({
        time: Number(stamp[1]) * 60 + Number(stamp[2]),
        text: textLine,
        translation,
        words,
      });
    }
  }
  lines.sort((a, b) => a.time - b.time);
  return lines;
}

function lineAt(lines, seconds) {
  let index = -1;
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].time <= seconds) index = i;
    else break;
  }
  return index;
}

const DEMO_LRC = `[00:00.00]这是 tokilive 的演示歌词 | demo lyric
[00:03.00]<00:03.00>不<00:03.40>是<00:03.80>在<00:04.20>售<00:04.60>歌<00:05.00>曲 | not a commercial song
[00:07.00]用来检查逐行、翻译和逐字高亮
[00:10.50]关闭演示后，这里只显示系统正在播放的曲目
`;

module.exports = { parseLrc, lineAt, DEMO_LRC };
