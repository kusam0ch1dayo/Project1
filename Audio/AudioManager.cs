using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 効果音とBGMの再生・音量管理（static API）。Resources/Audio/SFX, Resources/Audio/BGM のクリップを名前で再生する。
/// 音量は PlayerPrefs に保存される（ポーズメニューの設定画面から変更）。シーンに応じたBGMも自動で切り替える。
/// </summary>
public class AudioManager : MonoBehaviour
{
    const string KeyMaster = "vol_master", KeyBgm = "vol_bgm", KeySfx = "vol_sfx";
    const int PoolSize = 64; // 敵の音が多いと、古い音が途中で切られてしまうので、多めに
    const float FadeTime = 1.2f;

    static AudioManager inst;
    static bool loaded;
    static float master = 1f, bgm = 0.5f, sfx = 0.8f;

    static readonly int[] NormalDungeonTracks = { 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 };
    static readonly int[] BossTracks = { 22, 23 };

    public static float MasterVolume { get { Load(); return master; } set { Load(); master = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeyMaster, master); } }
    public static float BgmVolume { get { Load(); return bgm; } set { Load(); bgm = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeyBgm, bgm); } }
    public static float SfxVolume { get { Load(); return sfx; } set { Load(); sfx = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeySfx, sfx); } }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        master = PlayerPrefs.GetFloat(KeyMaster, 1f);
        bgm = PlayerPrefs.GetFloat(KeyBgm, 0.5f);
        sfx = PlayerPrefs.GetFloat(KeySfx, 0.8f);
    }

    public static void ResetVolumes()
    {
        MasterVolume = 1f;
        BgmVolume = 0.5f;
        SfxVolume = 0.8f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        inst = null;
        loaded = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Ready()) inst.ApplySceneBgm(SceneManager.GetActiveScene().name);
    }

    static bool Ready()
    {
        if (!Application.isPlaying) return false;
        if (inst == null)
        {
            var go = new GameObject("AudioManager");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<AudioManager>();
        }
        return true;
    }

    // ---------------------------------------------------------------- 効果音

    public static void PlaySfx(string name, float volume = 1f, float pitch = 1f)
    {
        if (Ready()) inst.Play(name, volume, pitch, null);
    }

    /// <summary>効果音のクリップを名前で取得する（ループ再生用）。割り当て表の音量・高さも返す。無ければ null。</summary>
    public static AudioClip FindClip(string name, out float entryVolume, out float entryPitch)
    {
        entryVolume = 1f;
        entryPitch = 1f;
        if (!Ready()) return null;
        if (!inst.Resolve(name, false, out var clip, out float v, out float p)) return null;
        entryVolume = v;
        entryPitch = p;
        return clip;
    }

    /// <summary>効果音の全体音量（マスター × 効果音）</summary>
    public static float SfxLevel => SfxVolume * MasterVolume;

    public static void PlaySfxAt(string name, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (Ready()) inst.Play(name, volume, pitch, position);
    }

    // ---------------------------------------------------------------- ロード中の無音

    /// <summary>
    /// ロード中（ダンジョンを作っているあいだ）は、音（BGM・効果音・環境音）を、すべて消す。
    /// 終わると、BGMは頭から、ふわっと流れ始める。
    /// </summary>
    public static void SetLoading(bool on)
    {
        if (on) { AudioListener.volume = 0f; if (inst != null) inst.loadingMute = true; return; }
        if (inst == null) { AudioListener.volume = 1f; return; }
        inst.loadingMute = false;
        inst.StartCoroutine(inst.EndLoading());
    }

    bool loadingMute;

    System.Collections.IEnumerator EndLoading()
    {
        // 流れていた曲を、頭に戻して、フェードインさせる
        var src = bgmSources[bgmActive];
        if (src != null && src.isPlaying && bgmTarget[bgmActive] > 0f) { src.time = 0f; bgmFade[bgmActive] = 0f; }
        float t = 0f;
        while (t < 0.6f && !loadingMute)
        {
            t += Time.unscaledDeltaTime;
            AudioListener.volume = Mathf.Clamp01(t / 0.6f);
            yield return null;
        }
        if (!loadingMute) AudioListener.volume = 1f;
    }

    // ---------------------------------------------------------------- BGM

    public static void PlayBgm(string name)
    {
        if (Ready()) inst.SetBgm(name);
    }

    /// <summary>BGMを、指定の秒数をかけて徐々に小さくして、聞こえなくする。</summary>
    public static void FadeOutBgm(float seconds)
    {
        if (!Ready()) return;
        inst.fadeSeconds = Mathf.Max(0.1f, seconds);
        inst.SetBgm(null);
    }

    public static void StopBgm()
    {
        if (Ready()) inst.SetBgm(null);
    }

    /// <summary>前奏（1回だけ流れる）から、ループする曲へ、切れ目なくつなぐ。ループの曲がもう流れていれば、何もしない。</summary>
    public static void PlayBgmWithIntro(string intro, string loop)
    {
        if (Ready()) inst.SetBgmIntro(intro, loop);
    }

    public static void PlayBossBgm()
    {
        if (Ready()) inst.SetBgm("boss");
    }

    /// <summary>今のシーンに合ったBGMへ戻す（ダンジョンで既に通常曲が流れていればそのまま）。</summary>
    public static void RestoreSceneBgm()
    {
        if (Ready()) inst.ApplySceneBgm(SceneManager.GetActiveScene().name);
    }

    // ---------------------------------------------------------------- 実装

    readonly System.Collections.Generic.Dictionary<string, AudioClip> clips = new System.Collections.Generic.Dictionary<string, AudioClip>();
    AudioSource[] pool;
    int poolNext;

    AudioSource[] bgmSources;
    float[] bgmFade = new float[2];
    float[] bgmTarget = new float[2];
    float[] bgmEntryVolume = { 1f, 1f };
    int bgmActive;
    string currentBgm;
    float fadeSeconds = FadeTime;

    void Awake()
    {
        if (inst != null && inst != this) { Destroy(gameObject); return; }
        inst = this;
        DontDestroyOnLoad(gameObject);

        // 同時に鳴らせる音の数（既定は 32。敵・トラップの音が多いと、足りなくて途切れる）
        var cfg = AudioSettings.GetConfiguration();
        if (cfg.numRealVoices < 64 || cfg.numVirtualVoices < 256)
        {
            cfg.numRealVoices = Mathf.Max(cfg.numRealVoices, 64);
            cfg.numVirtualVoices = Mathf.Max(cfg.numVirtualVoices, 256);
            AudioSettings.Reset(cfg);
        }

        // 発音口ごとに、別のオブジェクトにする（同じオブジェクトだと、1つの音を鳴らすたびに、鳴っている全部の音が、同じ位置へ動いてしまい、途切れる）
        pool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var child = new GameObject("Sfx" + i);
            child.transform.SetParent(transform, false);
            pool[i] = child.AddComponent<AudioSource>();
            pool[i].playOnAwake = false;
        }

        bgmSources = new AudioSource[2];
        for (int i = 0; i < 2; i++)
        {
            bgmSources[i] = gameObject.AddComponent<AudioSource>();
            bgmSources[i].playOnAwake = false;
            bgmSources[i].loop = true;
            bgmSources[i].spatialBlend = 0f;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (inst == this) inst = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplySceneBgm(scene.name);

    AudioClip GetClip(string path)
    {
        if (clips.TryGetValue(path, out var c)) return c;
        c = Resources.Load<AudioClip>("Audio/" + path);
        if (c == null) Debug.LogWarning("音声が見つかりません: Resources/Audio/" + path);
        clips[path] = c;
        return c;
    }

    /// <summary>音の割り当て表（AudioBank）から選ぶ。表が無い・項目が無いときは、Resources/Audio の既定のファイル。</summary>
    bool Resolve(string key, bool music, out AudioClip clip, out float volume, out float pitch)
    {
        volume = 1f;
        pitch = 1f;

        var bank = AudioBank.Instance;
        var entry = bank != null ? bank.Find(key) : null;
        if (entry != null)
        {
            clip = entry.Pick(); // 未割り当てなら null（無音）
            volume = entry.volume;
            pitch = entry.pitch;
            return clip != null;
        }

        string path;
        if (music)
        {
            if (key == "dungeon_normal") path = "BGM/dungeon_" + NormalDungeonTracks[Random.Range(0, NormalDungeonTracks.Length)];
            else if (key == "boss") path = "BGM/dungeon_" + BossTracks[Random.Range(0, BossTracks.Length)];
            else path = "BGM/" + key;
        }
        else
        {
            path = key == "slash" ? "SFX/slash" + Random.Range(1, 4) : "SFX/" + key;
        }
        clip = GetClip(path);
        return clip != null;
    }

    void Play(string name, float volume, float pitch, Vector3? position)
    {
        if (!Resolve(name, false, out var clip, out float entryVolume, out float entryPitch)) return;
        volume *= entryVolume;
        pitch *= entryPitch;

        AudioSource src = null;
        for (int i = 0; i < PoolSize; i++)
        {
            var s = pool[(poolNext + i) % PoolSize];
            if (!s.isPlaying) { src = s; poolNext = (poolNext + i + 1) % PoolSize; break; }
        }
        if (src == null)
        {
            src = pool[poolNext];
            poolNext = (poolNext + 1) % PoolSize;
        }

        if (position.HasValue)
        {
            src.transform.position = position.Value;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 3f;
            src.maxDistance = 45f;
        }
        else
        {
            src.spatialBlend = 0f;
        }

        src.clip = clip;
        src.pitch = pitch * Random.Range(0.97f, 1.03f);
        src.volume = Mathf.Clamp01(volume * SfxVolume * MasterVolume);
        src.Play();
    }

    void SetBgm(string name)
    {
        if (name == currentBgm) return;
        currentBgm = name;

        int old = bgmActive;
        bgmTarget[old] = 0f;

        if (string.IsNullOrEmpty(name)) return;
        fadeSeconds = FadeTime; // 次の曲は、通常の速さでフェードイン
        if (!Resolve(name, true, out var clip, out float entryVolume, out float entryPitch)) return;

        bgmActive = 1 - old;
        if (bgmActive == introFor) StopIntro();
        var src = bgmSources[bgmActive];
        src.loop = true;
        bgmEntryVolume[bgmActive] = entryVolume;
        src.pitch = entryPitch;
        src.clip = clip;
        src.time = 0f;
        bgmFade[bgmActive] = 0f;
        bgmTarget[bgmActive] = 1f;
        src.volume = 0f;
        src.Play();
    }

    // 前奏つきの曲（前奏は専用の音源で 1 回だけ鳴らし、終わる瞬間に、ループの曲が始まるよう予約する）
    AudioSource introSrc;
    int introFor = -1;
    float introEntryVolume = 1f;

    void SetBgmIntro(string intro, string loop)
    {
        if (loop == currentBgm) return;
        if (!Resolve(intro, true, out var ic, out float iv, out float ip)) { SetBgm(loop); return; }
        if (!Resolve(loop, true, out var lc, out float lv, out float lp)) return;
        currentBgm = loop;

        int old = bgmActive;
        bgmTarget[old] = 0f;
        fadeSeconds = FadeTime;
        bgmActive = 1 - old;
        StopIntro();
        var src = bgmSources[bgmActive];
        src.Stop();
        src.loop = true;
        src.clip = lc;
        src.pitch = lp;
        src.time = 0f;
        bgmEntryVolume[bgmActive] = lv;
        bgmFade[bgmActive] = 1f;   // 前奏は、いきなり鳴らす
        bgmTarget[bgmActive] = 1f;

        if (introSrc == null)
        {
            introSrc = gameObject.AddComponent<AudioSource>();
            introSrc.playOnAwake = false;
            introSrc.loop = false;
            introSrc.spatialBlend = 0f;
        }
        introSrc.clip = ic;
        introSrc.pitch = ip;
        introEntryVolume = iv;
        introFor = bgmActive;
        float vol = Mathf.Clamp01(BgmVolume * MasterVolume);
        introSrc.volume = Mathf.Clamp01(iv * vol);
        src.volume = Mathf.Clamp01(lv * vol);
        double start = AudioSettings.dspTime + 0.05;
        introSrc.PlayScheduled(start);
        src.PlayScheduled(start + ic.samples / (double)ic.frequency / Mathf.Max(0.01f, ip));
    }

    void StopIntro()
    {
        if (introSrc != null && introSrc.isPlaying) introSrc.Stop();
        introFor = -1;
    }

    void ApplySceneBgm(string scene)
    {
        if (scene == SceneNames.Home)
        {
            GameSession.EnsureInitialized(); // セーブされた昼夜を反映してから曲を選ぶ
            SetBgm(TimeOfDay.IsNight ? "village_night" : "village");
        }
        else if (scene == SceneNames.Dungeon)
        {
            SetBgm("dungeon_normal"); // 既に通常のダンジョン曲が流れていれば、そのまま
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < 2; i++)
        {
            bgmFade[i] = Mathf.MoveTowards(bgmFade[i], bgmTarget[i], dt / fadeSeconds);
            var src = bgmSources[i];
            src.volume = Mathf.Clamp01(bgmFade[i] * bgmEntryVolume[i] * BgmVolume * MasterVolume);
            if (bgmFade[i] <= 0f && bgmTarget[i] <= 0f && src.isPlaying) src.Stop();
        }
        if (introFor >= 0 && introSrc != null)
        {
            // 前奏の音量は、つながる先の曲と同じ（フェードアウトも一緒）
            introSrc.volume = Mathf.Clamp01(bgmFade[introFor] * introEntryVolume * BgmVolume * MasterVolume);
            if (bgmFade[introFor] <= 0f && bgmTarget[introFor] <= 0f) StopIntro();
        }
    }
}
