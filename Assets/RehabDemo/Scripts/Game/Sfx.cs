using UnityEngine;

namespace RehabDemo
{
  /// <summary>音效。声音是用正弦波现场合成的，不需要音频文件。</summary>
  public class Sfx : MonoBehaviour
  {
    [Range(0f, 1f)] public float volume = 0.5f;

    private const int _SampleRate = 44100;

    private AudioSource _source;
    private AudioClip _ding;
    private AudioClip _click;
    private AudioClip _fanfare;

    private void Awake()
    {
      _source = gameObject.AddComponent<AudioSource>();
      _source.playOnAwake = false;
      _source.spatialBlend = 0f;

      // 频率（Hz）；相邻音符的间隔、衰减时间、总时长（秒）
      _ding = CreateTones("Ding", new[] { 1318.5f, 1975.5f }, 0.07f, 0.16f, 0.6f);
      _click = CreateTones("Click", new[] { 660f }, 0f, 0.03f, 0.1f);
      _fanfare = CreateTones("Fanfare", new[] { 523.3f, 659.3f, 784.0f, 1046.5f }, 0.11f, 0.28f, 1.3f);
    }

    /// <summary>计一次时的“叮”。</summary>
    public void PlayDing() => Play(_ding);

    /// <summary>按钮触发时的轻响。</summary>
    public void PlayClick() => Play(_click);

    /// <summary>完成训练时的上行音阶。</summary>
    public void PlayFanfare() => Play(_fanfare);

    private void Play(AudioClip clip)
    {
      if (_source != null && clip != null)
      {
        _source.PlayOneShot(clip, volume);
      }
    }

    private static AudioClip CreateTones(string name, float[] frequencies, float interval, float decay, float length)
    {
      var samples = new float[Mathf.CeilToInt(length * _SampleRate)];
      for (var n = 0; n < frequencies.Length; n++)
      {
        var start = Mathf.RoundToInt(n * interval * _SampleRate);
        for (var i = start; i < samples.Length; i++)
        {
          var t = (float)(i - start) / _SampleRate;
          // 开头 5 毫秒淡入，避免“啪”的一声
          var envelope = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t / decay);
          samples[i] += Mathf.Sin(2f * Mathf.PI * frequencies[n] * t) * envelope * 0.4f;
        }
      }
      var clip = AudioClip.Create(name, samples.Length, 1, _SampleRate, false);
      clip.SetData(samples, 0);
      return clip;
    }

    private void OnDestroy()
    {
      Destroy(_ding);
      Destroy(_click);
      Destroy(_fanfare);
    }
  }
}
