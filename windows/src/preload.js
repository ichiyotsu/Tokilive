const { contextBridge, ipcRenderer } = require('electron');
const { resolveSidebar, navKey, typeahead } = require('./sidebar');
const { speechPlan } = require('./tts');
contextBridge.exposeInMainWorld('bili', {
  getSettings: () => ipcRenderer.invoke('get-settings'), getView: () => ipcRenderer.invoke('get-view'), getStatus: () => ipcRenderer.invoke('get-status'),
  getVersion: () => ipcRenderer.invoke('get-version'), getGuidance: () => ipcRenderer.invoke('get-guidance'),
  setSettings: p => ipcRenderer.invoke('set-settings', p), reconnect: () => ipcRenderer.invoke('reconnect'),
  test: t => ipcRenderer.invoke('test-message', t), resetBounds: () => ipcRenderer.invoke('reset-bounds'),
  qrGenerate: () => ipcRenderer.invoke('qr-generate'), qrPoll: k => ipcRenderer.invoke('qr-poll', k), whoami: () => ipcRenderer.invoke('whoami'), logout: () => ipcRenderer.invoke('logout'),
  readOwnRoom: cookie => ipcRenderer.invoke('read-own-room', cookie),
  pickBackground: () => ipcRenderer.invoke('pick-background'), clearBackground: () => ipcRenderer.invoke('clear-background'),
  sendDanmaku: text => ipcRenderer.invoke('send-danmaku', text),
  sampleBackground: () => ipcRenderer.invoke('sample-background'),
  lyricsNow: () => ipcRenderer.invoke('lyrics-now'),
  lyricsControl: action => ipcRenderer.invoke('lyrics-control', action),
  importLyrics: () => ipcRenderer.invoke('import-lyrics'),
  taskbarState: () => ipcRenderer.invoke('taskbar-state'),
  minWidth: w => ipcRenderer.send('min-width', w),
  edgeStart: () => ipcRenderer.send('edge-start'), edgeMove: (d, dx, dy, end) => ipcRenderer.send('edge-move', d, dx, dy, !!end),
  exportTheme: () => ipcRenderer.invoke('export-theme'), importTheme: () => ipcRenderer.invoke('import-theme'),
  resize: (w, h) => ipcRenderer.send('resize', w, h),
  updateCheck: () => ipcRenderer.invoke('update-check'), updateDownload: () => ipcRenderer.invoke('update-download'), updateInstall: () => ipcRenderer.invoke('update-install'),
  obsProbe: () => ipcRenderer.invoke('obs-probe'),
  speechPlan,
  sidebar: resolveSidebar, navKey, typeahead,
  on: (ch, fn) => ipcRenderer.on(ch, (_e, d) => fn(d)),
  onProgress: fn => ipcRenderer.on('update-progress', (_e, d) => fn(d)),
});
