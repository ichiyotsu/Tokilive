function isCardDark(cfg, systemDark) {
  return cfg.themeMode === 'dark' || (cfg.themeMode === 'system' && !!systemDark);
}

function isAppDark(cfg, systemDark) {
  const mode = cfg.appTheme || 'system';
  return mode === 'dark' || (mode === 'system' && !!systemDark);
}

function contrastOn(hex) {
  const m = /^#?([0-9a-f]{6})$/i.exec(String(hex || '').trim());
  if (!m) return '#ffffff';
  const n = parseInt(m[1], 16);
  const lin = c => { c /= 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
  const L = lin(n >> 16) * 0.2126 + lin((n >> 8) & 255) * 0.7152 + lin(n & 255) * 0.0722;
  return L > 0.4 ? '#1b1b1b' : '#ffffff';
}

/** 卡片主题与应用外观分开计算。改 app* 不会改 seed / themeMode / 悬浮窗材质。 */
function splitTheme(cfg, { systemDark = false } = {}) {
  return {
    cardDark: isCardDark(cfg, systemDark),
    appDark: isAppDark(cfg, systemDark),
    cardSeed: cfg.seedColor || '#6750A4',
    cardMode: cfg.themeMode || 'system',
    appTheme: cfg.appTheme || 'system',
    appAccent: cfg.appAccent || '#6750A4',
    appAccentOn: contrastOn(cfg.appAccent || '#6750A4'),
    appBackground: cfg.appBackground || '',
    appBgOpacity: cfg.appBgOpacity == null ? 0.45 : Number(cfg.appBgOpacity),
    overlayMaterial: cfg.material || 'none',
    settingsMaterial: cfg.appMaterial || 'mica',
  };
}

module.exports = { isCardDark, isAppDark, contrastOn, splitTheme };
