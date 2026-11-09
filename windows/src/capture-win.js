const { spawn } = require('child_process');
const { captureFixPlan } = require('./capture-style');

function hwndString(buf) {
  if (!buf) return '';
  const n = buf.length >= 8 ? buf.readBigUInt64LE(0) : BigInt(buf.readUInt32LE(0));
  const s = n.toString(10);
  return /^[0-9]+$/.test(s) && s !== '0' ? s : '';
}

function buildScript(hwnd, title) {
  if (!/^\d+$/.test(hwnd)) throw new Error('bad hwnd');
  const b64 = Buffer.from(String(title), 'utf8').toString('base64');
  if (!/^[A-Za-z0-9+/=]+$/.test(b64)) throw new Error('bad title');
  return `
$h = [IntPtr]::new([int64]${hwnd})
$title = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('${b64}'))
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class TokiWin {
  [DllImport("user32.dll", EntryPoint="GetWindowLongPtr", SetLastError=true)] public static extern IntPtr GetWindowLongPtr64(IntPtr h, int n);
  [DllImport("user32.dll", EntryPoint="SetWindowLongPtr", SetLastError=true)] public static extern IntPtr SetWindowLongPtr64(IntPtr h, int n, IntPtr v);
  [DllImport("user32.dll", EntryPoint="GetWindowLong", SetLastError=true)] public static extern int GetWindowLong32(IntPtr h, int n);
  [DllImport("user32.dll", EntryPoint="SetWindowLong", SetLastError=true)] public static extern int SetWindowLong32(IntPtr h, int n, int v);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool SetWindowText(IntPtr h, string t);
  [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);
}
"@
$GWL_EXSTYLE = -20
if ([IntPtr]::Size -eq 8) { $ex = [int64][TokiWin]::GetWindowLongPtr64($h, $GWL_EXSTYLE) } else { $ex = [TokiWin]::GetWindowLong32($h, $GWL_EXSTYLE) }
$ex = ($ex -band (-bnot 0x80)) -bor 0x40000
if ([IntPtr]::Size -eq 8) { [void][TokiWin]::SetWindowLongPtr64($h, $GWL_EXSTYLE, [IntPtr]::new($ex)) } else { [void][TokiWin]::SetWindowLong32($h, $GWL_EXSTYLE, [int]$ex) }
[void][TokiWin]::SetWindowDisplayAffinity($h, [uint32]0)
[void][TokiWin]::SetWindowText($h, $title)
Write-Output 'ok'
`.trim();
}

function applyCaptureFix(win, title) {
  if (process.platform !== 'win32' || !win || win.isDestroyed?.()) return Promise.resolve({ ok: false, skipped: true });
  let hwnd = '';
  try { hwnd = hwndString(win.getNativeWindowHandle()); } catch { return Promise.resolve({ ok: false, skipped: true }); }
  if (!hwnd) return Promise.resolve({ ok: false, skipped: true });
  const plan = captureFixPlan({ exStyle: 0x80 | 0x80000, title });
  const script = buildScript(hwnd, plan.title);
  return new Promise(resolve => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', script], { windowsHide: true });
    let out = '';
    child.stdout.on('data', d => { out += d; });
    child.on('error', () => resolve({ ok: false, plan }));
    child.on('close', code => resolve({ ok: code === 0 && /ok/.test(out), plan }));
  });
}

module.exports = { hwndString, buildScript, applyCaptureFix };
