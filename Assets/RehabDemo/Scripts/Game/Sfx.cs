using UnityEngine;

namespace RehabDemo
{
  /// <summary>音效。声音是用正弦波现场合成的，不需要音频文件。</summary>
  public class Sfx : MonoBehaviour
  {
    [Range(0f, 1f)] public float volume = 0.5f;

    private const int _SampleRate = 44100;

    private AudioSource _source;
    private AudioSource _humSource;
    private AudioClip _ding;
    private AudioClip _click;
    private AudioClip _fanfare;
    private AudioClip _doorOpen;
    private AudioClip _doorClose;
    private AudioClip _cupClink;
    private AudioClip _microwaveDing;
    private AudioClip _microwaveHum;

    private void Awake()
    {
      _source = gameObject.AddComponent<AudioSource>();
      _source.playOnAwake = false;
      _source.spatialBlend = 0f;

      _humSource = gameObject.AddComponent<AudioSource>();
      _humSource.playOnAwake = false;
      _humSource.loop = true;
      _humSource.spatialBlend = 0f;
      _humSource.volume = 0f;

      // 频率（Hz）；相邻音符的间隔、衰减时间、总时长（秒）
      _ding = CreateTones("Ding", new[] { 1318.5f, 1975.5f }, 0.07f, 0.16f, 0.6f);
      _click = CreateTones("Click", new[] { 660f }, 0f, 0.03f, 0.1f);
      _fanfare = CreateTones("Fanfare", new[] { 523.3f, 659.3f, 784.0f, 1046.5f }, 0.11f, 0.28f, 1.3f);
      _doorOpen = CreateTones("DoorOpen", new[] { 340f, 210f }, 0.035f, 0.055f, 0.22f);
      _doorClose = CreateTones("DoorClose", new[] { 190f, 480f }, 0.025f, 0.045f, 0.20f);
      _cupClink = CreateTones("CupClink", new[] { 784f, 1175f }, 0.02f, 0.05f, 0.18f);
      _microwaveDing = CreateMicrowaveBell("MicrowaveDing", 1568f, 1.35f);
      _microwaveHum = CreateHumLoop("MicrowaveHum", 1.0f);
      _humSource.clip = _microwaveHum;
    }

    /// <summary>计一次时的“叮”。</summary>
    public void PlayDing() => Play(_ding);

    /// <summary>按钮触发时的轻响。</summary>
    public void PlayClick() => Play(_click);

    /// <summary>完成训练时的上行音阶。</summary>
    public void PlayFanfare() => Play(_fanfare);

    /// <summary>拉开微波炉门的卡扣声。</summary>
    public void PlayDoorOpen() => Play(_doorOpen);

    /// <summary>关上微波炉门的落锁声。</summary>
    public void PlayDoorClose() => Play(_doorClose);

    /// <summary>拿起或放下杯子的轻响。</summary>
    public void PlayCupClink() => Play(_cupClink);

    /// <summary>微波炉加热结束的清脆金属铃声“叮——”。</summary>
    public void PlayMicrowaveDing() => Play(_microwaveDing, 1.15f);

    /// <summary>开启或关闭微波炉加热时的低鸣声。</summary>
    public void SetMicrowaveHum(bool active)
    {
      if (_humSource == null)
      {
        return;
      }
      if (active)
      {
        _humSource.volume = volume * 0.22f;
        if (!_humSource.isPlaying)
        {
          _humSource.Play();
        }
      }
      else if (_humSource.isPlaying)
      {
        _humSource.Stop();
      }
    }

    private void Play(AudioClip clip, float scale = 1f)
    {
      if (_source != null && clip != null)
      {
        _source.PlayOneShot(clip, Mathf.Clamp01(volume * scale));
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

    private static AudioClip CreateMicrowaveBell(string name, float fundamental, float length)
    {
      var samples = new float[Mathf.CeilToInt(length * _SampleRate)];
      for (var i = 0; i < samples.Length; i++)
      {
        var t = (float)i / _SampleRate;
        var attack = Mathf.Min(1f, t / 0.003f);
        // 基频 + 金属泛音，快速敲击感叠加长余音
        var body = Mathf.Sin(2f * Mathf.PI * fundamental * t) * Mathf.Exp(-t / 0.48f) * 0.50f;
        var overtone1 = Mathf.Sin(2f * Mathf.PI * (fundamental * 2.0f) * t) * Mathf.Exp(-t / 0.22f) * 0.25f;
        var overtone2 = Mathf.Sin(2f * Mathf.PI * (fundamental * 2.76f) * t) * Mathf.Exp(-t / 0.09f) * 0.18f;
        var strike = Mathf.Sin(2f * Mathf.PI * (fundamental * 4.1f) * t) * Mathf.Exp(-t / 0.025f) * 0.15f;
        samples[i] = (body + overtone1 + overtone2 + strike) * attack;
      }
      var clip = AudioClip.Create(name, samples.Length, 1, _SampleRate, false);
      clip.SetData(samples, 0);
      return clip;
    }

    private static AudioClip CreateHumLoop(string name, float length)
    {
      var count = Mathf.RoundToInt(length * _SampleRate);
      var samples = new float[count];
      for (var i = 0; i < count; i++)
      {
        var t = (float)i / _SampleRate;
        // 整周期正弦叠加，首尾无缝循环
        var hum100 = Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.28f;
        var hum200 = Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.16f;
        var hum300 = Mathf.Sin(2f * Mathf.PI * 300f * t) * 0.08f;
        samples[i] = hum100 + hum200 + hum300;
      }
      var clip = AudioClip.Create(name, count, 1, _SampleRate, false);
      clip.SetData(samples, 0);
      return clip;
    }

    private void OnDestroy()
    {
      Destroy(_ding);
      Destroy(_click);
      Destroy(_fanfare);
      Destroy(_doorOpen);
      Destroy(_doorClose);
      Destroy(_cupClink);
      Destroy(_microwaveDing);
      Destroy(_microwaveHum);
    }
  }
}
