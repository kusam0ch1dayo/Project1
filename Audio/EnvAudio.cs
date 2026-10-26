using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 環境音と足音。
/// ・シーンごとの環境音（ホーム: 風 / ダンジョン: テーマごとの環境音）をループで流す。
/// ・プレイヤーの足音を、歩き / 走りと、足元の地面（草・土・砂利・濡れた石・水・氷など）で切り替える。
/// 音のファイルは Resources/Audio/SFX（step_*, loop_*）。Dungeon > Audio Assignments で差し替えられる。
/// </summary>
public class EnvAudio : MonoBehaviour
{
    static EnvAudio inst;

    /// <summary>いまのシーンの基本の地面（grass / dirt / wet / gravel / hall / asphalt / gym）</summary>
    public static string BaseSurface = "grass";

    AudioSource bed, extra;
    float bedBase, extraBase, extraLevel;
    bool extraWater;
    FootstepAudio steps;
    float nextFind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        inst = null;
        BaseSurface = "grass";
    }

    /// <summary>同時に鳴らせる音の数を増やす（既定の32だと、ループ音が増えたときに他の音が途切れる）。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RaiseVoiceLimit()
    {
        var cfg = AudioSettings.GetConfiguration();
        if (cfg.numRealVoices >= 64) return;
        cfg.numRealVoices = 64;
        cfg.numVirtualVoices = Mathf.Max(cfg.numVirtualVoices, 256);
        AudioSettings.Reset(cfg);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst == null)
        {
            var go = new GameObject("EnvAudio");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<EnvAudio>();
        }
        inst.Apply(SceneManager.GetActiveScene().name);
    }

    void Awake()
    {
        bed = gameObject.AddComponent<AudioSource>();
        bed.playOnAwake = false;
        bed.loop = true;
        bed.spatialBlend = 0f;
        bed.priority = 160;
        extra = gameObject.AddComponent<AudioSource>();
        extra.playOnAwake = false;
        extra.loop = true;
        extra.spatialBlend = 0f;
        extra.priority = 165;
    }

    void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
    void OnLoaded(Scene s, LoadSceneMode m) => Apply(s.name);

    void Apply(string scene)
    {
        steps = null;
        if (scene == SceneNames.Home)
        {
            BaseSurface = "grass";
            SetBed("loop_village_wind", 0.35f);
            SetExtra(TimeOfDay.IsNight ? null : "loop_town", 0.2f, false); // 昼の村: 住宅街のざわめき
        }
        else if (scene == SceneNames.Dungeon)
        {
            SetExtra("loop_river", 0.5f, true); // 水のあるダンジョン: 水の上・近くで聞こえる、川のせせらぎ
            var theme = DungeonStyle.Resolve(DungeonSession.CurrentOrDefault);
            switch (theme)
            {
                case DungeonTheme.Cave: BaseSurface = "dirt"; SetBed("loop_cave_drip", 0.5f); break;
                case DungeonTheme.WetTunnel: BaseSurface = "wet"; SetBed("loop_sewer", 0.45f); break;
                case DungeonTheme.Mine: BaseSurface = "gravel"; SetBed("loop_factory", 0.22f); break;
                case DungeonTheme.DarkCorridor: BaseSurface = "hall"; SetBed("loop_cave_drip", 0.3f); break;
                case DungeonTheme.Crystal: BaseSurface = "asphalt"; SetBed("loop_magic_room", 0.4f); break;
                case DungeonTheme.Ice: BaseSurface = "hall"; SetBed("loop_village_wind", 0.4f); break;
                case DungeonTheme.Forest: BaseSurface = "grass"; SetBed("loop_cave_drip", 0.3f); break;
                case DungeonTheme.Volcano: BaseSurface = "gravel"; SetBed("loop_factory", 0.3f); break;
                case DungeonTheme.Desert: BaseSurface = "dirt"; SetBed("loop_village_wind", 0.45f); break;
                case DungeonTheme.Library: BaseSurface = "hall"; SetBed(null, 0f); break;
                case DungeonTheme.Starlit: BaseSurface = "asphalt"; SetBed("loop_magic_room", 0.35f); break;
                case DungeonTheme.Steampunk: BaseSurface = "gravel"; SetBed("loop_factory", 0.3f); break;
                case DungeonTheme.Neon: BaseSurface = "asphalt"; SetBed("loop_magic_room", 0.35f); break;
                default: BaseSurface = "gym"; SetBed(null, 0f); break; // 闘技場
            }
        }
        else
        {
            BaseSurface = "grass";
            SetBed(null, 0f);
        }
    }

    /// <summary>2つめの環境音。water = true なら、水のあるダンジョンで、水の上・近くにいるときだけ聞こえる。</summary>
    void SetExtra(string key, float volume, bool water)
    {
        if (extra == null) return;
        extra.Stop();
        extra.clip = null;
        extraLevel = 0f;
        extraWater = water;
        if (string.IsNullOrEmpty(key)) return;

        var clip = AudioManager.FindClip(key, out float ev, out float ep);
        if (clip == null) return;
        extra.clip = clip;
        extra.pitch = ep;
        extraBase = volume * ev;
        extra.time = Random.value * clip.length * 0.9f;
        if (!water)
        {
            extraLevel = 1f;
            extra.volume = extraBase * AudioManager.SfxLevel;
            extra.Play();
        }
        else extra.volume = 0f;
    }

    void SetBed(string key, float volume)
    {
        if (bed == null) return;
        bed.Stop();
        bed.clip = null;
        if (string.IsNullOrEmpty(key)) return;

        var clip = AudioManager.FindClip(key, out float ev, out float ep);
        if (clip == null) return;
        bed.clip = clip;
        bed.pitch = ep;
        bedBase = volume * ev;
        bed.volume = bedBase * AudioManager.SfxLevel;
        bed.time = Random.value * clip.length * 0.9f;
        bed.Play();
    }

    void Update()
    {
        if (bed != null && bed.clip != null) bed.volume = bedBase * AudioManager.SfxLevel;
        if (extra != null && extra.clip != null)
        {
            if (!extraWater) extra.volume = extraBase * AudioManager.SfxLevel;
            else
            {
                // 水のあるダンジョン: 水の上・水の中にいるほど大きく、遠いと小さい。頭が水に沈んでいる間は、水中の音に任せる
                float target = 0f;
                if (WaterZones.Active && Hz.Player != null)
                {
                    target = WaterZones.Over(Hz.PlayerPos) ? 1f : 0.2f;
                    if (Hz.Player.HeadUnderwater) target = 0f;
                }
                extraLevel = Mathf.MoveTowards(extraLevel, target, Time.unscaledDeltaTime * 1.2f);
                extra.volume = extraBase * extraLevel * AudioManager.SfxLevel;
                if (extraLevel > 0.01f && !extra.isPlaying) extra.Play();
                else if (extraLevel <= 0.01f && extra.isPlaying) extra.Stop();
            }
        }

        // プレイヤーに足音を付ける
        if (steps == null && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 0.5f;
            var fpc = FindFirstObjectByType<FirstPersonController>();
            if (fpc != null)
            {
                steps = fpc.GetComponent<FootstepAudio>();
                if (steps == null) steps = fpc.gameObject.AddComponent<FootstepAudio>();
            }
        }
    }
}

/// <summary>プレイヤーの足音。歩いているあいだ、地面に合った足音をループで流す（走ると速い足音）。</summary>
public class FootstepAudio : MonoBehaviour
{
    FirstPersonController fpc;
    CharacterController cc;
    AudioSource src;
    string curKey;
    float level;
    float entryVolume = 1f;
    bool paused;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        cc = GetComponent<CharacterController>();
        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f;
        src.priority = 90;
    }

    float lastGrounded = -99f, silentSince;

    void Update()
    {
        if (fpc == null || src == null) return;

        // 接地の判定は、1フレームだけ浮いた扱いになることがあるので、少しのあいだ猶予を持たせる
        if (cc != null && cc.isGrounded) lastGrounded = Time.time;
        bool grounded = Time.time - lastGrounded < 0.25f;
        bool moving = fpc.MoveInput.sqrMagnitude > 0.01f && grounded && !fpc.IsSwimming && !fpc.actionsLocked && fpc.inputEnabled && Time.timeScale > 0.01f;
        bool run = fpc.sprintingNow;

        string surface = Hz.SurfaceNow ?? EnvAudio.BaseSurface;
        // 走る音が無い地面（水・フローリング）は、歩く音を速くして使う
        bool noRun = surface == "water" || surface == "wood";
        string key = "step_" + surface + (run && !noRun ? "_run" : "_walk");
        float pitch = run && noRun ? 1.35f : 1f;
        string stateKey = key + pitch;

        if (stateKey != curKey)
        {
            curKey = stateKey;
            var clip = AudioManager.FindClip(key, out float ev, out float ep);
            entryVolume = ev;
            src.pitch = pitch * ep;
            if (clip != src.clip)
            {
                src.clip = clip;
                if (clip != null && moving) src.Play();
            }
        }
        float target = moving && src.clip != null ? (run ? 1.0f : 0.85f) : 0f;
        level = Mathf.MoveTowards(level, target, Time.unscaledDeltaTime * (target > level ? 8f : 4f));
        src.volume = level * entryVolume * AudioManager.SfxLevel;

        // 止まっても、しばらくは無音のまま流し続ける（止めたり再開したりすると、音が途切れる）
        if (level > 0.01f)
        {
            silentSince = Time.unscaledTime;
            if (!src.isPlaying && src.clip != null) src.Play();
        }
        else if (src.isPlaying && Time.unscaledTime - silentSince > 1.5f)
        {
            src.Stop();
        }
    }
}

/// <summary>場所に付けるループ音（水流・焚き火・魔力の部屋など）。離れると小さくなり、別の階では聞こえない。</summary>
public class AudioLoop : MonoBehaviour
{
    AudioSource src;
    float baseVolume;

    public static AudioLoop Attach(GameObject go, string key, float volume, float minDist = 2f, float maxDist = 18f)
    {
        var clip = AudioManager.FindClip(key, out float ev, out float ep);
        if (clip == null) return null;

        var l = go.AddComponent<AudioLoop>();
        l.src = go.AddComponent<AudioSource>();
        l.src.playOnAwake = false;
        l.src.loop = true;
        l.src.spatialBlend = 1f;
        l.src.rolloffMode = AudioRolloffMode.Linear;
        l.src.minDistance = minDist;
        l.src.maxDistance = maxDist;
        l.src.clip = clip;
        l.src.pitch = ep;
        l.baseVolume = volume * ev;
        l.src.volume = 0f;
        l.src.priority = 200;
        l.src.time = Random.value * clip.length * 0.9f;
        l.maxDist = maxDist;
        return l;
    }

    float maxDist, next;

    void Update()
    {
        if (src == null || Time.time < next) return;
        next = Time.time + 0.1f;

        // 聞こえる範囲に入ったときだけ鳴らす（遠くのループ音で、同時に鳴らせる数を使い切らないように）
        bool audible = true;
        if (Hz.Player != null)
        {
            Vector3 p = Hz.PlayerPos;
            audible = !FloorUtil.Different(p, transform.position) && (p - transform.position).sqrMagnitude < (maxDist + 2f) * (maxDist + 2f);
        }
        if (audible)
        {
            src.volume = baseVolume * AudioManager.SfxLevel;
            if (!src.isPlaying) src.Play();
        }
        else if (src.isPlaying) src.Stop();
    }
}

/// <summary>この範囲の中では、足音が指定の地面になる（宝部屋のじゅうたんなど）。</summary>
public class SurfaceZone : MonoBehaviour
{
    Bounds bounds;
    string key;
    float next;

    public static SurfaceZone Make(Transform parent, Bounds b, string key)
    {
        var go = new GameObject("SurfaceZone");
        go.transform.SetParent(parent, true);
        var z = go.AddComponent<SurfaceZone>();
        z.bounds = b;
        z.key = key;
        return z;
    }

    void Update()
    {
        if (Time.time < next || !Hz.PlayerAlive) return;
        next = Time.time + 0.15f;
        Vector3 p = Hz.PlayerPos;
        if (Mathf.Abs(p.x - bounds.center.x) < bounds.extents.x && Mathf.Abs(p.z - bounds.center.z) < bounds.extents.z && Mathf.Abs(p.y - bounds.center.y) < 3f)
            Hz.SetSurface(key);
    }
}
