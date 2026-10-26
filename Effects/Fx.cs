using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// コードだけで作るパーティクル演出（攻撃・被弾・回復・ボスの攻撃など）。
/// 使い捨てで、再生が終わると自動で消える。マテリアルは実行時に URP の Particles/Unlit から作る。
/// </summary>
public static class Fx
{
    public class Spec
    {
        public Color color = Color.white;
        public Color color2 = Color.white;
        public int count = 20;          // 開始時に一度に出す数
        public float rate = 0f;         // 継続して出す数（個/秒）
        public float duration = 0.1f;   // 継続して出す時間
        public float speedMin = 1f, speedMax = 3f;
        public float sizeMin = 0.1f, sizeMax = 0.2f;
        public float life = 0.6f;
        public float gravity = 0f;
        public ParticleSystemShapeType shape = ParticleSystemShapeType.Sphere;
        public float radius = 0.1f;
        public float angle = 25f;
        public bool edge = false;       // 形状の縁（表面）からだけ出す
        public Vector3 box = Vector3.one;
        public bool shrink = true;      // だんだん小さくなる
        public bool grow = false;       // だんだん大きくなる（煙）
        public bool additive = true;    // 加算合成（光）。false なら通常の半透明（煙）
        public bool scalable = true;    // 設定の「パーティクルの量」で増減する（星空など背景は false）
        public bool fade = true;       // 出現・消滅時にフェードする
        public bool loop = false;
        public bool local = false;      // 親に追従して動く
    }

    static Material additiveMat, alphaMat;
    static Texture2D softTex;

    static Texture2D SoftTexture()
    {
        if (softTex != null) return softTex;
        const int n = 32;
        softTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2((n - 1) / 2f, (n - 1) / 2f)) / (n / 2f);
            float a = Mathf.Clamp01(1f - d);
            a = a * a * (3f - 2f * a);
            softTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        softTex.Apply();
        return softTex;
    }

    static Material GetMaterial(bool additive)
    {
        var cached = additive ? additiveMat : alphaMat;
        if (cached != null) return cached;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        bool urp = shader != null;
        if (!urp) shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader) { name = additive ? "FxAdditive" : "FxAlpha" };
        mat.mainTexture = SoftTexture();
        if (urp)
        {
            mat.SetTexture("_BaseMap", SoftTexture());
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        if (additive) additiveMat = mat; else alphaMat = mat;
        return mat;
    }

    /// <summary>
    /// パーティクルを再生する。parent が無ければ pos/rot はワールド座標、あれば親からの相対。
    /// 発射方向は rot の forward（Cone など）。上向きにしたいときは Euler(-90,0,0)。
    /// </summary>
    public static ParticleSystem Play(Spec s, Vector3 pos, Transform parent = null, Quaternion? rot = null)
    {
        if (!Application.isPlaying) return null;

        // 設定の「パーティクルの量」を反映する（0 なら出さない）
        float scale = s.scalable ? GameSettings.ParticleScale : 1f;
        if (scale <= 0.01f) return null;
        int burstCount = s.count > 0 ? Mathf.Max(1, Mathf.RoundToInt(s.count * scale)) : 0;

        var go = new GameObject("Fx");
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            if (rot.HasValue) go.transform.localRotation = rot.Value;
        }
        else
        {
            go.transform.position = pos;
            if (rot.HasValue) go.transform.rotation = rot.Value;
        }

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = s.loop;
        main.duration = Mathf.Max(0.1f, s.duration);
        main.startLifetime = new ParticleSystem.MinMaxCurve(s.life * 0.7f, s.life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(s.speedMin, s.speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(s.sizeMin, s.sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(s.color, s.color2);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = s.gravity;
        main.simulationSpace = s.local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.maxParticles = 1500;
        main.stopAction = s.loop ? ParticleSystemStopAction.None : ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = s.rate * scale;
        if (burstCount > 0) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });
        else emission.SetBursts(new ParticleSystem.Burst[0]);

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = s.shape;
        shape.radius = s.radius;
        shape.angle = s.angle;
        shape.radiusThickness = s.edge ? 0f : 1f;
        if (s.shape == ParticleSystemShapeType.Box) shape.scale = s.box;

        if (s.fade)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
        }

        if (s.shrink || s.grow)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, s.grow
                ? new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 1f))
                : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
        }

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GetMaterial(s.additive);
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        ps.Play();
        return ps;
    }

    static readonly Quaternion Up = Quaternion.Euler(-90f, 0f, 0f);

    static Transform playerCache;
    static Transform Player
    {
        get
        {
            if (playerCache == null)
            {
                var p = Object.FindFirstObjectByType<FirstPersonController>();
                if (p != null) playerCache = p.transform;
            }
            return playerCache;
        }
    }

    // ---------------------------------------------------------------- 戦闘

    /// <summary>斬撃の軌跡（右から左へ流れる光の筋）。</summary>
    public static void Swing(Vector3 pos, Vector3 direction)
    {
        Play(new Spec
        {
            color = new Color(0.85f, 0.95f, 1f), color2 = Color.white,
            count = 18, speedMin = 5f, speedMax = 9f, sizeMin = 0.05f, sizeMax = 0.12f, life = 0.22f,
            shape = ParticleSystemShapeType.Cone, angle = 14f, radius = 0.04f,
        }, pos, null, Quaternion.LookRotation(direction));
    }

    /// <summary>命中の火花。</summary>
    public static void Hit(Vector3 pos)
    {
        Play(new Spec
        {
            color = new Color(1f, 0.92f, 0.6f), color2 = new Color(1f, 0.5f, 0.15f),
            count = 16, speedMin = 2.5f, speedMax = 7f, sizeMin = 0.06f, sizeMax = 0.16f, life = 0.4f,
            gravity = 0.6f, radius = 0.05f,
        }, pos);
    }

    /// <summary>回避の砂ぼこり。</summary>
    public static void Dust(Vector3 pos, float scale = 1f)
    {
        Play(new Spec
        {
            color = new Color(0.75f, 0.7f, 0.6f, 0.8f), color2 = new Color(0.6f, 0.55f, 0.45f, 0.8f),
            count = 6, speedMin = 0.5f, speedMax = 1.6f, sizeMin = 0.3f * scale, sizeMax = 0.6f * scale, life = 0.6f,
            gravity = -0.05f, radius = 0.3f * scale, additive = false, shrink = false, grow = true,
        }, pos);
    }

    public static void EnemyDeath(Vector3 pos)
    {
        pos += Vector3.up * 0.8f;
        Play(new Spec
        {
            color = new Color(0.5f, 0.45f, 0.45f, 0.7f), color2 = new Color(0.3f, 0.3f, 0.32f, 0.7f),
            count = 22, speedMin = 0.8f, speedMax = 2.2f, sizeMin = 0.4f, sizeMax = 0.9f, life = 1f,
            gravity = -0.1f, radius = 0.4f, additive = false, shrink = false, grow = true,
        }, pos);
        Play(new Spec
        {
            color = new Color(1f, 0.3f, 0.2f), color2 = new Color(1f, 0.75f, 0.3f),
            count = 22, speedMin = 2f, speedMax = 6f, sizeMin = 0.06f, sizeMax = 0.15f, life = 0.7f,
            gravity = 1f, radius = 0.3f,
        }, pos);
    }

    // ---------------------------------------------------------------- 回復・報酬

    public static void Rising(Transform target, Color a, Color b, float time = 1f)
    {
        if (target == null) return;
        Play(new Spec
        {
            color = a, color2 = b, count = 0, rate = 45f, duration = time, life = 1f,
            speedMin = 0.8f, speedMax = 1.8f, sizeMin = 0.08f, sizeMax = 0.18f,
            shape = ParticleSystemShapeType.Cone, angle = 4f, radius = 0.55f, local = true,
        }, new Vector3(0f, 0.1f, 0f), target, Up);
    }

    public static void Heal(Transform target) => Rising(target, new Color(0.4f, 1f, 0.5f), new Color(0.85f, 1f, 0.85f));
    public static void StaminaRestore(Transform target) => Rising(target, new Color(0.35f, 0.7f, 1f), new Color(0.8f, 0.95f, 1f));
    public static void GoldGain() => Rising(Player, new Color(1f, 0.85f, 0.25f), new Color(1f, 1f, 0.6f), 0.5f);

    public static void ChestOpen(Vector3 pos)
    {
        Play(new Spec
        {
            color = new Color(1f, 0.85f, 0.3f), color2 = new Color(1f, 1f, 0.7f),
            count = 50, speedMin = 3f, speedMax = 6.5f, sizeMin = 0.07f, sizeMax = 0.17f, life = 0.9f,
            gravity = 1.4f, shape = ParticleSystemShapeType.Cone, angle = 38f, radius = 0.3f,
        }, pos, null, Up);
    }

    // ---------------------------------------------------------------- ボス

    /// <summary>攻撃前の予備動作: 赤い光が体に集まる。</summary>
    public static void BossCharging(Transform boss, float time)
    {
        if (boss == null) return;
        Play(new Spec
        {
            color = new Color(1f, 0.2f, 0.1f), color2 = new Color(1f, 0.6f, 0.2f),
            count = 0, rate = 70f, duration = time, life = 0.6f,
            speedMin = -4.5f, speedMax = -3f, sizeMin = 0.12f, sizeMax = 0.3f,
            shape = ParticleSystemShapeType.Sphere, radius = 2.4f, edge = true, local = true,
        }, new Vector3(0f, 1.5f, 0f), boss);
    }

    /// <summary>薙ぎ払いの赤い火花。</summary>
    public static void BossSwing(Vector3 pos, Vector3 forward)
    {
        Play(new Spec
        {
            color = new Color(1f, 0.25f, 0.1f), color2 = new Color(1f, 0.7f, 0.2f),
            count = 45, speedMin = 6f, speedMax = 11f, sizeMin = 0.12f, sizeMax = 0.3f, life = 0.45f,
            shape = ParticleSystemShapeType.Cone, angle = 55f, radius = 0.5f,
        }, pos + Vector3.up * 1.4f + forward * 1.5f, null, Quaternion.LookRotation(forward));
    }

    /// <summary>衝撃波: 地面を走るリング、砂ぼこり、舞い上がる火花。</summary>
    public static void Slam(Vector3 pos, float radius)
    {
        pos += Vector3.up * 0.15f;
        Play(new Spec
        {
            color = new Color(1f, 0.45f, 0.1f), color2 = new Color(1f, 0.8f, 0.3f),
            count = 110, speedMin = radius / 0.5f, speedMax = radius / 0.5f, sizeMin = 0.35f, sizeMax = 0.6f, life = 0.5f,
            shape = ParticleSystemShapeType.Circle, radius = 0.5f, edge = true,
        }, pos, null, Up);
        Play(new Spec
        {
            color = new Color(0.65f, 0.6f, 0.5f, 0.8f), color2 = new Color(0.5f, 0.45f, 0.4f, 0.8f),
            count = 50, speedMin = radius / 0.9f, speedMax = radius / 0.7f, sizeMin = 0.8f, sizeMax = 1.5f, life = 0.9f,
            shape = ParticleSystemShapeType.Circle, radius = 0.5f, edge = true, additive = false, shrink = false, grow = true,
        }, pos, null, Up);
        Play(new Spec
        {
            color = new Color(1f, 0.5f, 0.15f), color2 = new Color(1f, 0.9f, 0.5f),
            count = 40, speedMin = 5f, speedMax = 12f, sizeMin = 0.1f, sizeMax = 0.25f, life = 0.9f,
            gravity = 2f, shape = ParticleSystemShapeType.Cone, angle = 35f, radius = 0.8f,
        }, pos, null, Up);
    }

    public static void Enrage(Vector3 pos)
    {
        pos += Vector3.up * 0.5f;
        Play(new Spec
        {
            color = new Color(1f, 0.15f, 0.05f), color2 = new Color(1f, 0.55f, 0.1f),
            count = 90, speedMin = 3f, speedMax = 8f, sizeMin = 0.25f, sizeMax = 0.6f, life = 1.1f,
            gravity = -0.4f, shape = ParticleSystemShapeType.Cone, angle = 55f, radius = 1.2f,
        }, pos, null, Up);
        Slam(pos, 5f);
    }

    public static void BossDeath(Vector3 pos)
    {
        pos += Vector3.up * 1.5f;
        Play(new Spec
        {
            color = new Color(1f, 0.5f, 0.1f), color2 = new Color(1f, 0.9f, 0.4f),
            count = 150, speedMin = 4f, speedMax = 13f, sizeMin = 0.4f, sizeMax = 1.1f, life = 1.3f,
            radius = 0.6f,
        }, pos);
        Play(new Spec
        {
            color = new Color(0.35f, 0.33f, 0.33f, 0.8f), color2 = new Color(0.2f, 0.2f, 0.2f, 0.8f),
            count = 50, speedMin = 1f, speedMax = 4f, sizeMin = 1f, sizeMax = 2.2f, life = 2f,
            gravity = -0.15f, radius = 1.2f, additive = false, shrink = false, grow = true,
        }, pos);
        Play(new Spec
        {
            color = new Color(1f, 0.85f, 0.3f), color2 = new Color(1f, 1f, 0.7f),
            count = 90, speedMin = 3f, speedMax = 9f, sizeMin = 0.08f, sizeMax = 0.2f, life = 1.6f,
            gravity = 1.5f, radius = 0.8f,
        }, pos);
    }

    // ---------------------------------------------------------------- 環境

    /// <summary>ふわふわ漂う粒（花粉・蛍・ほこり）。親の周囲の箱の範囲に出る。</summary>
    public static ParticleSystem Ambient(Transform parent, Vector3 localPos, Vector3 box, Color a, Color b,
                                         float size, float rate, float life, float speed)
    {
        return Play(new Spec
        {
            color = a, color2 = b, count = 0, rate = rate, loop = true, duration = 5f, life = life,
            speedMin = speed * 0.3f, speedMax = speed, sizeMin = size * 0.6f, sizeMax = size,
            shape = ParticleSystemShapeType.Box, box = box, shrink = false,
        }, localPos, parent);
    }

    /// <summary>ポータルのまわりに漂う光の粒。</summary>
    public static void PortalGlow(Transform portal, Color color)
    {
        Play(new Spec
        {
            color = color, color2 = Color.white, count = 0, rate = 18f, loop = true, duration = 5f, life = 2.2f,
            speedMin = 0.2f, speedMax = 0.7f, sizeMin = 0.06f, sizeMax = 0.14f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(2.4f, 3f, 0.4f), shrink = true, local = true,
        }, new Vector3(0f, 1.6f, 0f), portal);
    }
}
