// OBS window-helpers 与多数「窗口枚举」会跳过 WS_EX_TOOLWINDOW。
// Electron 的 skipTaskbar:true 会加上这个样式，于是透明弹幕窗经常根本不在列表里。
// 分层透明（WS_EX_LAYERED）仍要保留，否则就不是真透明。
// WDA_EXCLUDEFROMCAPTURE (0x11) 会让系统直接拒绝采集，必须保持 WDA_NONE。
const WS_EX_TOOLWINDOW = 0x00000080;
const WS_EX_APPWINDOW = 0x00040000;
const WS_EX_LAYERED = 0x00080000;
const WDA_NONE = 0;
const WDA_EXCLUDEFROMCAPTURE = 0x00000011;

function captureExStyle(exStyle) {
  let next = (Number(exStyle) || 0) & ~WS_EX_TOOLWINDOW;
  next |= WS_EX_APPWINDOW;
  return next >>> 0;
}

function keepsLayered(exStyle) {
  return ((Number(exStyle) || 0) & WS_EX_LAYERED) !== 0;
}

/** 采集修复只改扩展样式和显示亲和性，不新建隐藏窗口。 */
function captureFixPlan({ exStyle, title }) {
  return {
    exStyle: captureExStyle(exStyle),
    layered: keepsLayered(exStyle),
    displayAffinity: WDA_NONE,
    forbidAffinity: WDA_EXCLUDEFROMCAPTURE,
    title: String(title || ''),
    createHiddenDuplicate: false,
  };
}

module.exports = {
  WS_EX_TOOLWINDOW, WS_EX_APPWINDOW, WS_EX_LAYERED, WDA_NONE, WDA_EXCLUDEFROMCAPTURE,
  captureExStyle, keepsLayered, captureFixPlan,
};
