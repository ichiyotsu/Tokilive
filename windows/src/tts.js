function speechPlan(cfg) {
  const on = !!cfg.tts;
  return {
    speak: on,
    cancel: !on,
    voice: cfg.ttsVoice || '',
    rate: clamp(cfg.ttsRate, 0.5, 2, 1),
    pitch: clamp(cfg.ttsPitch, 0.5, 2, 1),
    volume: clamp(cfg.ttsVolume, 0, 1, 1),
  };
}

function clamp(value, min, max, fallback) {
  const n = Number(value);
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, n));
}

module.exports = { speechPlan };
