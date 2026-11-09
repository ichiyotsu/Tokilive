const USER_AGENT = 'tokilive/1.2.0 (https://github.com/ichiyotsu/tokilive)';

function searchUrl(title, artist) {
  const q = new URL('https://lrclib.net/api/search');
  q.searchParams.set('track_name', title || '');
  if (artist) q.searchParams.set('artist_name', artist);
  return q.toString();
}

function pickRecord(records, title) {
  const list = Array.isArray(records) ? records : [];
  const wanted = String(title || '').trim().toLowerCase();
  const ranked = list.filter(r => r && (r.syncedLyrics || r.plainLyrics));
  ranked.sort((a, b) => score(b, wanted) - score(a, wanted));
  return ranked[0] || null;
}

function score(record, title) {
  let s = record.syncedLyrics ? 2 : 1;
  if (title && String(record.trackName || '').toLowerCase() === title) s += 3;
  return s;
}

async function searchLyrics(title, artist, fetchImpl = fetch) {
  if (!title) return { ok: false, message: '没有曲名，没有向歌词库查询。' };
  const res = await fetchImpl(searchUrl(title, artist), { headers: { 'User-Agent': USER_AGENT, Accept: 'application/json' } });
  if (res.status === 429) return { ok: false, message: '歌词库限流，稍后再试。没有反复重试。' };
  if (res.status === 404) return { ok: false, message: '歌词库没有这首的记录。可以导入本地 LRC。' };
  if (res.status !== 200) return { ok: false, message: '歌词库返回 HTTP ' + res.status + '。' };
  const json = await res.json();
  const hit = pickRecord(json, title);
  if (!hit) return { ok: false, message: '歌词库没有可用歌词。可以导入本地 LRC。' };
  return {
    ok: true,
    source: 'LRCLIB',
    instrumental: !!hit.instrumental,
    synced: hit.syncedLyrics || '',
    plain: hit.plainLyrics || '',
    trackName: hit.trackName || title,
    artistName: hit.artistName || artist || '',
  };
}

module.exports = { USER_AGENT, searchUrl, pickRecord, searchLyrics };
