// 只包含悬浮卡片 / 弹幕外观。应用外观（appTheme、appAccent、背景、侧栏）不在这里，
// 导入导出主题时不能把应用外壳一起换掉。
const THEME_KEYS = ['seedColor', 'themeMode', 'overrides', 'scBiliColor', 'textShadow', 'opacity', 'blur', 'radius', 'fontSize', 'fontFamily', 'cjkFont', 'jpFont', 'fontWeight', 'medalStyle', 'medalColors', 'showAvatar', 'showMedal', 'miniLayout', 'miniBg', 'miniAlpha', 'miniText', 'miniLabel', 'miniLine', 'miniLineW', 'customCss', 'design'];

const APP_APPEARANCE_KEYS = ['appTheme', 'appAccent', 'appBackground', 'appBgOpacity', 'appMaterial', 'sidebar'];

function themeExport(cfg) {
  const t = { tokilive: 1 };
  for (const k of THEME_KEYS) t[k] = cfg[k];
  return t;
}

function themeImport(cfg, file) {
  const next = { ...cfg };
  for (const k of THEME_KEYS) if (k in file) next[k] = file[k];
  return next;
}

module.exports = { THEME_KEYS, APP_APPEARANCE_KEYS, themeExport, themeImport };
