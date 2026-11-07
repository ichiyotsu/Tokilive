/** WinUI NavigationView：宽屏展开、中等紧凑、窄窗覆盖。 */
function resolveSidebar({ width, preference = 'auto', userOpen = false } = {}) {
  const w = Number(width) || 0;
  let display;
  if (w > 0 && w < 720) display = 'minimal';
  else if (preference === 'expanded') display = 'expanded';
  else if (preference === 'compact') display = 'compact';
  else display = w > 0 && w < 1000 ? 'compact' : 'expanded';
  const open = display === 'minimal' ? !!userOpen : true;
  return {
    display,
    open,
    showLabels: display === 'expanded' || (display === 'minimal' && open),
    overlay: display === 'minimal',
  };
}

function navKey(key, index, count) {
  if (!count) return null;
  if (key === 'ArrowDown') return (index + 1) % count;
  if (key === 'ArrowUp') return (index - 1 + count) % count;
  if (key === 'Home') return 0;
  if (key === 'End') return count - 1;
  return null;
}

function typeahead(key, labels, start) {
  if (!key || key.length !== 1 || /\s/.test(key)) return null;
  const q = key.toLowerCase();
  const n = labels.length;
  for (let step = 1; step <= n; step++) {
    const i = (start + step) % n;
    if (String(labels[i] || '').trim().toLowerCase().startsWith(q)) return i;
  }
  return null;
}

module.exports = { resolveSidebar, navKey, typeahead };
