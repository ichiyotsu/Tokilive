/** 单独的 `/` 不能注册成全局热键，否则会吃掉游戏和输入框里的斜杠。 */
function classifyHotkey(accelerator) {
  const raw = String(accelerator || '').trim();
  if (!raw) return { scope: 'none', registerGlobal: false, message: '未设置弹幕输入快捷键。' };
  const parts = raw.split('+').map(s => s.trim()).filter(Boolean);
  const modifiers = new Set(['ctrl', 'control', 'alt', 'shift', 'meta', 'cmd', 'command', 'commandorcontrol', 'super']);
  const mods = parts.filter(p => modifiers.has(p.toLowerCase()));
  const key = parts[parts.length - 1];
  if (parts.length === 1 && key === '/') {
    return { scope: 'focused-window', registerGlobal: false, message: '单独的 / 只在 tokilive 窗口处于前台且未穿透时打开输入框，不会全局占用。' };
  }
  if (!mods.length) {
    return { scope: 'focused-window', registerGlobal: false, message: '没有修饰键的快捷键只在 tokilive 自己的窗口里生效。' };
  }
  return { scope: 'global', registerGlobal: true, accelerator: parts.join('+'), message: '带修饰键的快捷键会尝试全局注册。如果被占用，会提示失败并且不发送弹幕。' };
}

module.exports = { classifyHotkey };
