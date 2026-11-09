const ROUTES = {
  '/': 'overlay.html',
  '/overlay': 'overlay.html',
  '/mini': 'overlay.html',
  '/overlay.js': 'overlay.js',
  '/overlay.css': 'overlay.css',
  '/fonts.css': 'fonts.css',
  '/fonts.js': 'fonts.js',
};

function routeOf(url) {
  const path = String(url || '/').split('?')[0];
  if (path === '/events') return { kind: 'events', win: /[?&]w=mini/.test(url) ? 'mini' : 'main' };
  const font = path.match(/^\/fonts\/([\w.-]+\.woff2)$/);
  if (font) return { kind: 'font', file: font[1] };
  const file = ROUTES[path];
  if (!file) return null;
  return { kind: 'file', file, mini: path === '/mini' };
}

const BROWSER_SOURCE_NOTE = 'OBS 浏览器源可以保持 CSS 透明。弹幕用 /overlay，互动小窗用 /mini。窗口捕获对分层透明窗口经常是黑的或列不出来；浏览器源不依赖那条路径。直播姬如果提供浏览器源，用同一对地址。本机只验证了这个本地服务能返回页面，没有在真实的 OBS 或直播姬里核对画面。';

module.exports = { ROUTES, routeOf, BROWSER_SOURCE_NOTE };
