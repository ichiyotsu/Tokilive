const { contextBridge, ipcRenderer } = require('electron');
const { resolveSidebar, navKey, typeahead } = require('./sidebar');
contextBridge.exposeInMainWorld('bili', {
  getSettings: () => ipcRenderer.invoke('get-settings'), getView: () => ipcRenderer.invoke('get-view'), getStatus: () => ipcRenderer.invoke('get-status'),
  getVersion: () => ipcRenderer.invoke('get-version'), getGuidance: () => ipcRenderer.invoke('get-guidance'),
  setSettings: p => ipcRenderer.invoke('set-settings', p), reconnect: () => ipcRenderer.invoke('reconnect'),
  test: t => ipcRenderer.invoke('test-message', t), resetBounds: () => ipcRenderer.invoke('reset-bounds'),
  qrGenerate: () => ipcRenderer.invoke('qr-generate'), qrPoll: k => ipcRenderer.invoke('qr-poll', k), whoami: () => ipcRenderer.invoke('whoami'), logout: () => ipcRenderer.invoke('logout'),
  readOwnRoom: cookie => ipcRenderer.invoke('read-own-room', cookie),
  pickBackground: () => ipcRenderer.invoke('pick-background'), clearBackground: () => ipcRenderer.invoke('clear-background'),
  minWidth: w => ipcRenderer.send('min-width', w),
  edgeStart: () => ipcRenderer.send('edge-start'), edgeMove: (d, dx, dy, end) => ipcRenderer.send('edge-move', d, dx, dy, !!end),
  exportTheme: () => ipcRenderer.invoke('export-theme'), importTheme: () => ipcRenderer.invoke('import-theme'),
  resize: (w, h) => ipcRenderer.send('resize', w, h),
  githubStatus: () => ipcRenderer.invoke('github-status'), githubSaveToken: t => ipcRenderer.invoke('github-save-token', t), githubClear: () => ipcRenderer.invoke('github-clear'),
  githubDeviceStart: () => ipcRenderer.invoke('github-device-start'), githubDevicePoll: () => ipcRenderer.invoke('github-device-poll'),
  updateCheck: () => ipcRenderer.invoke('update-check'), updateDownload: () => ipcRenderer.invoke('update-download'), updateInstall: () => ipcRenderer.invoke('update-install'),
  perfSample: () => ipcRenderer.invoke('perf-sample'), perfScan: () => ipcRenderer.invoke('perf-scan'),
  perfGraceful: pids => ipcRenderer.invoke('perf-graceful', pids), perfForce: pids => ipcRenderer.invoke('perf-force', pids),
  obsProbe: () => ipcRenderer.invoke('obs-probe'),
  sidebar: resolveSidebar, navKey, typeahead,
  on: (ch, fn) => ipcRenderer.on(ch, (_e, d) => fn(d)),
  onProgress: fn => ipcRenderer.on('update-progress', (_e, d) => fn(d)),
});
