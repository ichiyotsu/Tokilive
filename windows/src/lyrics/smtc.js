function parseSession(json) {
  if (!json || json.ok === false) {
    return { ok: false, message: (json && json.message) || '没有正在播放的媒体。' };
  }
  const source = String(json.source || json.sourceApp || '');
  return {
    ok: true,
    source,
    sourceKind: classifySource(source),
    title: String(json.title || ''),
    artist: String(json.artist || ''),
    position: Number(json.position) || 0,
    duration: Number(json.duration) || 0,
    status: String(json.status || ''),
  };
}

function classifySource(source) {
  const s = source.toLowerCase();
  if (/qq|tencent\.qqmusic|qq音乐/.test(s)) return 'qq';
  if (/netease|cloudmusic|网易云/.test(s)) return 'netease';
  if (!source) return 'unknown';
  return 'other';
}

function sourceLabel(kind) {
  if (kind === 'qq') return 'QQ音乐';
  if (kind === 'netease') return '网易云音乐';
  if (kind === 'other') return '其他播放器';
  return '未识别来源';
}

/** 只允许这四个由用户点击触发的命令，避免脚本接收任意字符串。 */
function controlCommand(action) {
  const allowed = { play: 'TryPlayAsync', pause: 'TryPauseAsync', toggle: 'TryTogglePlayPauseAsync', next: 'TrySkipNextAsync', previous: 'TrySkipPreviousAsync' };
  const method = allowed[action];
  if (!method) throw new Error('不支持的播放控制');
  return method;
}

module.exports = { parseSession, classifySource, sourceLabel, controlCommand };
