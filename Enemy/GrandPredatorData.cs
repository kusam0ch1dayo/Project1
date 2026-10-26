using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 大ボス「グランドプレデター」（炎を纏う巨大ワーム）のデータと、見た目・効果の材質。
/// 実行時に作る。Resources/Enemies/GrandPredator.asset があれば、それを使う（エディタを開くと自動で作られ、Dungeon Creator のボスの一覧に出る）。
/// </summary>
public static class GrandPredatorData
{
    public const string Id = "grand_predator";
    public const string AssetPath = "Enemies/GrandPredator";

    public static readonly Color Skin = new Color(0.34f, 0.07f, 0.05f);
    public static readonly Color Ember = new Color(1f, 0.42f, 0.08f);
    public static readonly Color Lava = new Color(1f, 0.3f, 0.04f);
    public static readonly Color Sand = new Color(0.78f, 0.64f, 0.42f);

    static EnemyData boss, magmaSlime;
    static Material gumMat, skinMat, toothMat, mouthMat, glowMat, rockMat, sandMat, lavaMat, moltenMat;
    static Texture2D crackTex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { boss = magmaSlime = null; }

    public static bool Is(EnemyData d) => d != null && d.id == Id;

    public static EnemyData Boss
    {
        get
        {
            if (boss != null) return boss;
            boss = Resources.Load<EnemyData>(AssetPath);
            if (boss != null) return boss;
            boss = Create();
            boss.hideFlags = HideFlags.HideAndDontSave;
            return boss;
        }
    }

    /// <summary>データを新しく作る（エディタでアセットにするときにも使う）。</summary>
    public static EnemyData Create()
    {
        var d = ScriptableObject.CreateInstance<EnemyData>();
        d.name = "GrandPredator";
        d.id = Id;
        d.displayName = "グランドプレデター";
        d.isBoss = true;
        d.description = "炎を纏う巨大ワーム。赤黒い皮膚は、どんなダメージも95%カットする。正面には、鋭い牙だけが並んだ丸い巨大な口。砂の海を、イルカのように弧を描いて泳ぎ回る。強スタンで横に倒れると、皮膚が一時的に柔らかくなる。";
        d.model = EnemyModel.Default;
        d.element = Element.Fire;
        d.randomElement = false;
        d.color = Skin;
        d.scale = 1f;
        d.colliderHeight = 5f;
        d.colliderRadius = 2.3f;
        d.labelHeight = 4.6f;
        d.maxHealth = 1100f;      // HP は高め
        d.damage = 16f;
        d.moveSpeed = 7f;
        d.detectRange = 34f;
        d.loseRange = 400f;
        d.behaviors = EnemyBehavior.Chase;
        d.enrageHealthRatio = 0f;
        d.attackRange = 4f;
        d.staggerTime = 0f;
        d.flyMode = FlyMode.Ground;
        d.baseExp = 340f;
        d.goldRange = new Vector2Int(480, 640);
        d.bossStoneCount = 3;
        d.crystalDropChance = 0.4f;
        return d;
    }

    /// <summary>炎柱から噴き出す、炎属性の手下（倒しても報酬なし）。</summary>
    public static EnemyData MagmaSlime
    {
        get
        {
            if (magmaSlime != null) return magmaSlime;
            var d = Object.Instantiate(EnemyFactory.DefaultEnemy);
            d.hideFlags = HideFlags.HideAndDontSave;
            d.name = "predator_magma_slime";
            d.id = "predator_magma_slime";
            d.displayName = "マグマスライム";
            d.model = EnemyModel.Slime;
            d.element = Element.Fire;
            d.randomElement = false;
            d.color = new Color(1f, 0.35f, 0.08f);
            d.maxHealth = 40f;
            d.damage = 8f;
            d.moveSpeed = 3.6f;
            d.detectRange = 40f;
            d.baseExp = 0f;
            d.goldRange = Vector2Int.zero;
            d.stoneDropChance = 0f;
            d.crystalDropChance = 0f;
            d.makoDropChance = 0f;
            magmaSlime = d;
            return d;
        }
    }

    // ---------------------------------------------------------------- 材質

    /// <summary>赤黒い皮膚の、光るひび割れ（ボロノイの境界）。</summary>
    public static Texture2D CrackTexture()
    {
        if (crackTex != null) return crackTex;
        const int N = 128;
        crackTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
        var rng = new System.Random(4711);
        var pts = new Vector2[28];
        for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((float)rng.NextDouble() * N, (float)rng.NextDouble() * N);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d1 = 1e9f, d2 = 1e9f;
                foreach (var p in pts)
                    for (int ox = -1; ox <= 1; ox++)
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            float dx = x - (p.x + ox * N), dy = y - (p.y + oy * N);
                            float d = dx * dx + dy * dy;
                            if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                        }
                float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
                float crack = Mathf.Clamp01(1f - edge / 2.6f);
                float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.35f + 0.65f;
                Color c = new Color(0.36f, 0.08f, 0.06f) * n;
                c = Color.Lerp(c, new Color(1f, 0.5f, 0.12f), crack);
                c.a = 1f;
                px[y * N + x] = c;
            }
        crackTex.SetPixels(px);
        crackTex.Apply(true);
        return crackTex;
    }

    static Material Lit(Color c, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        return m;
    }

    static Material Glow(Color c, float glow)
    {
        var m = Lit(c * 0.5f, 0.2f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * glow);
        return m;
    }

    public static Material SkinMaterial()
    {
        if (skinMat != null) return skinMat;
        var m = Lit(new Color(0.55f, 0.2f, 0.17f), 0.3f);
        var tex = CrackTexture();
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(2f, 2f)); }
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionMap")) { m.SetTexture("_EmissionMap", tex); m.SetTextureScale("_EmissionMap", new Vector2(2f, 2f)); }
        m.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.08f) * 0.9f);
        skinMat = m;
        return m;
    }

    public static Material ToothMaterial() => toothMat != null ? toothMat : (toothMat = Lit(new Color(0.9f, 0.84f, 0.7f), 0.55f));
    public static Material GumMaterial() => gumMat != null ? gumMat : (gumMat = Lit(new Color(0.32f, 0.04f, 0.03f), 0.75f));
    public static Material MouthMaterial() => mouthMat != null ? mouthMat : (mouthMat = Lit(new Color(0.07f, 0.01f, 0.01f), 0.6f));
    public static Material GlowMaterial() => glowMat != null ? glowMat : (glowMat = Glow(Ember, 4f));
    public static Material RockMaterial() => rockMat != null ? rockMat : (rockMat = Lit(new Color(0.2f, 0.12f, 0.1f), 0.15f));
    public static Material SandMaterial() => sandMat != null ? sandMat : (sandMat = Lit(Sand * 0.95f, 0.05f));
    public static Material LavaMaterial() => lavaMat != null ? lavaMat : (lavaMat = Glow(Lava, 3.2f));

    /// <summary>火山弾の、溶けた岩（ひび割れが強く光る）。</summary>
    public static Material MoltenMaterial()
    {
        if (moltenMat != null) return moltenMat;
        var m = Lit(new Color(0.4f, 0.15f, 0.1f), 0.2f);
        var tex = CrackTexture();
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", tex);
        m.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f) * 2.2f);
        moltenMat = m;
        return m;
    }

    /// <summary>炎の粒（燃えている物の周り）。</summary>
    public static ParticleSystem Flames(Transform parent, Vector3 local, float radius, float rate, float size = 0.5f)
    {
        return Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.55f, 0.12f), color2 = new Color(1f, 0.15f, 0.02f), count = 0, rate = rate, duration = 9999f, loop = true,
            life = 0.7f, speedMin = 0.5f, speedMax = 2.4f, sizeMin = size * 0.5f, sizeMax = size, radius = radius, gravity = -1.2f, local = false,
        }, local, parent);
    }

    /// <summary>砂煙と岩の破片（地面から飛び出す・潜るとき）。</summary>
    public static void GroundBurst(Vector3 p, float scale)
    {
        Fx.Dust(p, 2.2f * scale);
        Fx.Play(new Fx.Spec
        {
            color = Sand, color2 = new Color(0.5f, 0.38f, 0.25f), count = (int)(70 * scale), speedMin = 3f * scale, speedMax = 11f * scale, sizeMin = 0.3f, sizeMax = 1.1f * scale,
            life = 1.6f, radius = 2.4f * scale, gravity = 1.6f, additive = false, grow = true, shrink = false,
        }, p + Vector3.up * 0.5f, null, Quaternion.Euler(-90f, 0f, 0f));
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.6f, 0.15f), color2 = new Color(1f, 0.2f, 0.02f), count = (int)(40 * scale), speedMin = 4f, speedMax = 14f * scale, sizeMin = 0.08f, sizeMax = 0.25f,
            life = 1.1f, radius = 1.6f * scale, gravity = 2.5f,
        }, p + Vector3.up * 0.6f, null, Quaternion.Euler(-90f, 0f, 0f));
    }

    /// <summary>咆哮（専用の音が無いので、低く落とした音を重ねる）。</summary>
    public static void Roar(Vector3 p, float vol)
    {
        Sfx("slam_windup", p, vol, 0.3f);
        Sfx("charge", p, vol * 0.85f, 0.36f);
        Sfx("fire2", p, vol * 0.9f, 0.42f);
        Sfx("explosion2", p, vol * 0.6f, 0.3f);
        Sfx("explosion1", p, vol * 0.5f, 0.24f);   // 腹に響く、低いうなり
        Sfx("whoosh", p, vol * 0.5f, 0.35f);
        float d = Hz.Flat(Hz.PlayerPos, p);
        if (d < 90f) Quake(Mathf.Lerp(0.4f, 0.12f, d / 90f) * vol, 1.3f);
    }

    /// <summary>
    /// 揺れ（グランドプレデター用に、強め・長めの画面揺れ）。ゲームパッドがあれば、振動もさせる。
    /// </summary>
    public static void Quake(float amp, float dur)
    {
        CinematicDirector.Shake(Mathf.Min(1.5f, amp * 1.5f), dur * 1.3f);
        PredatorRumble.Pulse(Mathf.Clamp01(amp * 1.3f), dur * 1.3f);
    }

    /// <summary>
    /// 効果音。巨体の音は遠くまで届く（その場の 3D の音に、距離で弱まる 2D の音を重ねる）。
    /// </summary>
    public static void Sfx(string name, Vector3 p, float vol = 1f, float pitch = 1f)
    {
        AudioManager.PlaySfxAt(name, p, vol, pitch);
        float k = 1f - Mathf.Clamp01((Vector3.Distance(Hz.PlayerPos, p) - 12f) / 110f);
        if (k > 0.05f) AudioManager.PlaySfx(name, vol * 0.7f * k, pitch);
    }

    /// <summary>大爆発（炎の玉と、黒い煙）。</summary>
    public static void Blast(Vector3 p, float radius)
    {
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.75f, 0.3f), color2 = new Color(1f, 0.2f, 0.02f), count = (int)(60 + radius * 12f), speedMin = radius * 0.6f, speedMax = radius * 2.2f,
            sizeMin = 0.6f, sizeMax = 1.8f + radius * 0.1f, life = 0.9f, radius = radius * 0.3f, gravity = -0.4f,
        }, p + Vector3.up * 0.8f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.25f, 0.2f, 0.18f), color2 = new Color(0.08f, 0.06f, 0.05f), count = (int)(30 + radius * 6f), speedMin = 1f, speedMax = radius * 0.8f,
            sizeMin = 1.2f, sizeMax = 2.6f + radius * 0.15f, life = 2.4f, radius = radius * 0.4f, gravity = -0.35f, additive = false, grow = true, shrink = false,
        }, p + Vector3.up * 1.2f);
        MagicFx.FlashLight(p + Vector3.up * 2f, new Color(1f, 0.5f, 0.15f), radius * 4f, 30f, 0.5f);
    }
}

/// <summary>
/// グランドプレデターの、ボス本体ではない「狙える部分」（分身・火山弾）。プレイヤーの魔法・自動照準の的になる。
/// </summary>
public class PredatorPart : MonoBehaviour
{
    public Element element = Element.Fire;
    public bool targetable = true;   // 自動照準の的になる（分身）。火山弾は、狙えるが、自動照準の的にはしない
    public static readonly List<PredatorPart> Registry = new List<PredatorPart>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Registry.Clear();

    void OnEnable() => Registry.Add(this);
    void OnDisable() => Registry.Remove(this);
}

/// <summary>ゲームパッドの振動（グランドプレデターの揺れに合わせて）。パッドが無ければ、何もしない。</summary>
public class PredatorRumble : MonoBehaviour
{
    static PredatorRumble inst;
    float strength, start, until;

    public static void Pulse(float s, float dur)
    {
        var pad = UnityEngine.InputSystem.Gamepad.current;
        if (pad == null || s <= 0.03f) return;
        if (inst == null)
        {
            var go = new GameObject("PredatorRumble");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<PredatorRumble>();
        }
        float now = Time.unscaledTime;
        float cur = inst.Current(now);
        if (s < cur) return;
        inst.strength = s;
        inst.start = now;
        inst.until = now + Mathf.Max(0.05f, dur);
    }

    float Current(float now) => now >= until ? 0f : strength * (1f - (now - start) / Mathf.Max(0.01f, until - start));

    void Update()
    {
        var pad = UnityEngine.InputSystem.Gamepad.current;
        if (pad == null) return;
        float k = (Time.timeScale <= 0f && !CinematicDirector.IsPlaying) || AudioListener.pause ? 0f : Current(Time.unscaledTime);   // ポーズ中・時が止まる演出の間は止める
        pad.SetMotorSpeeds(k * 0.9f, k * 0.6f);
    }

    void OnDisable()
    {
        var pad = UnityEngine.InputSystem.Gamepad.current;
        if (pad != null) pad.SetMotorSpeeds(0f, 0f);
    }
}
