/** 显示/隐藏只改可见性，不拆窗口、不断开直播连接。 */
function nextVisibility(state, patch) {
  const next = {
    overlayVisible: state.overlayVisible !== false,
    miniVisible: state.miniVisible !== false,
    connected: !!state.connected,
  };
  if (patch && Object.prototype.hasOwnProperty.call(patch, 'overlayVisible')) next.overlayVisible = !!patch.overlayVisible;
  if (patch && Object.prototype.hasOwnProperty.call(patch, 'miniVisible')) next.miniVisible = !!patch.miniVisible;
  return { ...next, destroyWindows: false, stopConnection: false };
}

function visibilityActions(flags) {
  return {
    overlay: flags.overlayVisible === false ? 'hide' : 'show',
    mini: flags.miniVisible === false ? 'hide' : 'show',
    disconnect: false,
  };
}

module.exports = { nextVisibility, visibilityActions };
