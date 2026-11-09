function taskbarDecision({ hideRequested, trayAvailable }) {
  if (!hideRequested) return { skipTaskbar: false, hidden: false, message: '' };
  if (!trayAvailable) {
    return { skipTaskbar: false, hidden: false, message: '托盘没有创建成功，为避免窗口消失，仍保留任务栏按钮。' };
  }
  return { skipTaskbar: true, hidden: true, message: '已从任务栏隐藏。窗口仍在运行，可从托盘图标打开。' };
}

module.exports = { taskbarDecision };
