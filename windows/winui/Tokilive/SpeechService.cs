using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Tokilive;

public static class SpeechService
{
    static SpeechSynthesizer? _synth;
    static MediaPlayer? _player;

    public static IReadOnlyList<string> Voices()
    {
        try { return SpeechSynthesizer.AllVoices.Select(v => v.DisplayName).Distinct().ToList(); }
        catch { return Array.Empty<string>(); }
    }

    public static void Stop()
    {
        try { _player?.Pause(); } catch { }
        try { if (_player != null) _player.Source = null; } catch { }
        _synth?.Dispose();
        _synth = null;
    }

    public static async void Speak(string text, bool force = false)
    {
        if ((!force && !AppState.Current.TtsEnabled) || string.IsNullOrWhiteSpace(text)) { Stop(); return; }
        try
        {
            Stop();
            if (!force && !AppState.Current.TtsEnabled) return;
            _synth = new SpeechSynthesizer();
            var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName == AppState.Current.TtsVoice);
            if (voice != null) _synth.Voice = voice;
            _synth.Options.SpeakingRate = Math.Clamp(AppState.Current.TtsRate, 0.5, 2);
            var stream = await _synth.SynthesizeTextToStreamAsync(text);
            if (!force && !AppState.Current.TtsEnabled) { Stop(); return; }
            _player ??= new MediaPlayer();
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Play();
        }
        catch { Stop(); }
    }
}
