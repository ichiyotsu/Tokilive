const WINDOW_TITLES = {
  danmaku: 'tokilive · 弹幕',
  mini: 'tokilive · 实时窗',
  settings: 'tokilive · 设置',
  splash: 'tokilive',
};

function titleFor(kind) {
  const t = WINDOW_TITLES[kind];
  if (!t) throw new Error('unknown window ' + kind);
  return t;
}

module.exports = { WINDOW_TITLES, titleFor };
