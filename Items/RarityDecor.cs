using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// エピック・レジェンダリー・ミシックの装備の見た目を、豪華にする飾り（武器のモデルの上に重ねる）。上のランクほど、下のランクの飾りも全部つく。
///  エピック: 刃に沿って光る筋、鍔（つば）の宝玉、刃からこぼれる光の粒
///  レジェンダリー: 金の翼飾り、刃のまわりを回る光の輪（ルーンの輪）、立ちのぼる金の粒、淡い光
///  ミシック: 赤と黒。刃のまわりを巡る黒い結晶、先端の赤い光輪、燃えるような赤い火の粉と黒い煙、脈打つ光、振ると赤い残光
/// 武器のモデルの大きさを測って、刃（先の方）と鍔（持ち手の少し先）の位置を決めるので、どの種類の武器にもつけられる。
/// 落ちている装備（DroppedItem）にも、光の柱などをつける（DropFx）。
/// </summary>
public static class RarityDecor
{
    public const string RootName = "RarityDecor";

    public static Color Main(Rarity r)
    {
        switch (r)
        {
            case Rarity.Mythic: return new Color(1f, 0.15f, 0.18f);
            case Rarity.Legendary: return new Color(1f, 0.82f, 0.25f);
            default: return new Color(0.75f, 0.4f, 1f);
        }
    }

    static Material ringMat;
    static Material RingMat()
    {
        if (ringMat == null) ringMat = DungeonMaterials.Ghost(Color.white);
        return ringMat;
    }

    /// <summary>
    /// 武器の飾りをつける（前の飾りは消す）。holder = 武器の根元（+Z が刃の向き）、model = 測る対象（null なら holder 全体）。
    /// エピック未満なら、何もしない。
    /// </summary>
    /// <param name="geometry">宝玉・光る筋・翼などの「形の飾り」もつけるか（プレハブの武器だけ。標準のモデルは、エピック以上なら、モデル自体が豪華になっている）。</param>
    public static GameObject Apply(Transform holder, ItemData d, Transform model = null, bool geometry = true)
    {
        Remove(holder);
        if (holder == null || d == null || d.rarity < Rarity.Epic) return null;
        Rarity r = d.rarity;
        Color col = Main(r);
        Color elem = d.element != Element.None ? MagicFx.Main(d.element) : col;

        // モデルの大きさ（holder のローカル座標）
        var b = LocalBounds(holder, model != null ? model : holder);
        bool vertical = b.size.y > b.size.z * 1.4f;   // 弓など、上下に長いもの
        Vector3 axis = vertical ? Vector3.up : Vector3.forward;
        Vector3 ringAxis = vertical ? Vector3.forward : axis;   // 弓: 輪・模様は、縦に立てる（正面を向く）
        float lo = vertical ? b.min.y : Mathf.Max(b.min.z, -0.6f);
        float hi = vertical ? b.max.y : b.max.z;
        float len = Mathf.Max(0.3f, hi - lo);
        Vector3 basePt = vertical ? new Vector3(b.center.x, 0f, b.center.z) : new Vector3(b.center.x, b.center.y, 0f);
        Vector3 At(float t) => basePt + axis * Mathf.Lerp(lo, hi, t);
        float guardT = vertical ? 0.5f : Mathf.Clamp01((0.05f - lo) / len);   // 鍔（刃の始まり）
        float bladeT = vertical ? 0.5f : Mathf.Lerp(guardT, 1f, 0.55f);       // 刃の中ほど
        float thick = Mathf.Clamp(Mathf.Max(vertical ? b.size.z : b.size.x, vertical ? b.size.x : b.size.y), 0.06f, 0.5f);

        var root = new GameObject(RootName).transform;
        root.SetParent(holder, false);
        var anim = root.gameObject.AddComponent<RarityDecorAnim>();
        anim.rarity = r;
        anim.axis = ringAxis;

        // ---------------------------------------------------------------- エピック
        if (geometry) {
        // 刃に沿って光る筋（左右）
        if (!vertical)
        {
            float z0 = Mathf.Lerp(lo, hi, Mathf.Max(guardT, 0.05f) + 0.02f), z1 = hi - 0.02f;
            foreach (float s in new[] { -1f, 1f })
            {
                var strip = Glow(root, "EdgeGlow", new Vector3(basePt.x + s * thick * 0.45f, basePt.y, (z0 + z1) * 0.5f), new Vector3(0.012f, 0.012f, Mathf.Max(0.05f, z1 - z0)), Color.Lerp(col, Color.white, 0.35f), 3f);
                anim.pulses.Add(strip.transform);
            }
        }
        else
        {
            foreach (float s in new[] { -1f, 1f })
            {
                var strip = Glow(root, "EdgeGlow", basePt + Vector3.up * (s * len * 0.25f) + Vector3.forward * (b.center.z), new Vector3(0.012f, len * 0.42f, 0.012f), Color.Lerp(col, Color.white, 0.35f), 3f);
                anim.pulses.Add(strip.transform);
            }
        }
        // 鍔の宝玉
        var gem = Glow(root, "Gem", At(guardT) + (vertical ? Vector3.forward * 0.02f : Vector3.zero), Vector3.one * Mathf.Clamp(thick * 0.55f, 0.05f, 0.12f), elem, 4f, PrimitiveType.Sphere);
        anim.pulses.Add(gem.transform);
        }
        // こぼれる光の粒
        var sparkle = new GameObject("Sparkle").transform;
        sparkle.SetParent(root, false);
        sparkle.localPosition = At(bladeT);
        sparkle.localRotation = Quaternion.LookRotation(axis == Vector3.up ? Vector3.forward : Vector3.up, axis == Vector3.up ? Vector3.up : Vector3.forward);
        Fx.Play(new Fx.Spec
        {
            color = col, color2 = Color.white, count = 0, rate = r == Rarity.Epic ? 6f : r == Rarity.Legendary ? 10f : 14f, duration = 1f, loop = true, local = false,
            speedMin = 0.05f, speedMax = 0.25f, sizeMin = 0.015f, sizeMax = 0.04f, life = 0.8f, gravity = r == Rarity.Mythic ? -0.4f : 0.15f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(thick * 0.6f, thick * 0.6f, len * 0.5f),
        }, Vector3.zero, sparkle, Quaternion.LookRotation(axis));

        if (r == Rarity.Epic) return root.gameObject;

        // ---------------------------------------------------------------- レジェンダリー
        Color gold = r == Rarity.Mythic ? new Color(0.25f, 0.05f, 0.07f) : new Color(1f, 0.8f, 0.3f);
        Color wingGlow = r == Rarity.Mythic ? new Color(1f, 0.15f, 0.15f) : new Color(1f, 0.85f, 0.4f);
        // 翼飾り（鍔の左右）
        if (geometry) foreach (float s in new[] { -1f, 1f })
        {
            Vector3 side = vertical ? Vector3.right * s : Vector3.right * s;
            Vector3 p = At(guardT) + side * (thick * 0.6f + 0.06f) - axis * 0.02f;
            var wing = GeoMesh.Box(root, "Wing", p, new Vector3(0.025f, 0.05f, 0.16f), gold, false, wingGlow * 0.6f, PrimitiveType.Cube,
                                   Quaternion.LookRotation(axis + side * 0.9f - (vertical ? Vector3.forward : Vector3.up) * 0.1f, vertical ? Vector3.forward : Vector3.up));
            wing.transform.localPosition = p;
            GeoMesh.Box(root, "Wing2", p + side * 0.05f - axis * 0.04f, new Vector3(0.02f, 0.04f, 0.11f), gold, false, wingGlow * 0.6f, PrimitiveType.Cube,
                                    Quaternion.LookRotation(axis * 0.6f + side, vertical ? Vector3.forward : Vector3.up));
        }
        // 刃のまわりを回る、光の輪（2つ）
        for (int i = 0; i < 2; i++)
        {
            float t = Mathf.Lerp(bladeT, 0.97f, i * 0.6f);
            var ring = Ring(root, At(t), ringAxis, Mathf.Clamp(thick * (1.2f - i * 0.25f), 0.08f, 0.35f), r == Rarity.Mythic ? new Color(1f, 0.2f, 0.2f, 0.85f) : new Color(1f, 0.88f, 0.45f, 0.85f), i == 0 ? 6 : 0);
            anim.rings.Add(ring);
        }
        // 立ちのぼる粒
        var rise = new GameObject("Rise").transform;
        rise.SetParent(root, false);
        rise.localPosition = At(guardT);
        Fx.Play(new Fx.Spec
        {
            color = r == Rarity.Mythic ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.9f, 0.5f), color2 = Color.white, count = 0, rate = 8f, duration = 1f, loop = true,
            speedMin = 0.1f, speedMax = 0.35f, sizeMin = 0.02f, sizeMax = 0.05f, life = 1.1f, gravity = -0.3f, radius = thick * 0.6f,
        }, Vector3.zero, rise);
        // 淡い光
        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root, false);
        lightGo.transform.localPosition = At(bladeT);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = col;
        l.range = 1.6f;
        l.intensity = r == Rarity.Mythic ? 1.6f : 1.1f;
        l.shadows = LightShadows.None;
        anim.glowLight = l;

        if (r == Rarity.Legendary) return root.gameObject;

        // ---------------------------------------------------------------- ミシック
        // 刃のまわりを巡る、黒い結晶
        Color black = new Color(0.08f, 0.02f, 0.03f);
        for (int i = 0; i < 4; i++)
        {
            var orbit = new GameObject("Orbit").transform;
            orbit.SetParent(root, false);
            orbit.localPosition = At(Mathf.Lerp(bladeT, 0.95f, i / 3f));
            orbit.localRotation = Quaternion.AngleAxis(i * 90f, ringAxis);
            var shard = GeoMesh.Box(orbit, "Shard", Vector3.zero, new Vector3(0.035f, 0.035f, 0.09f), black, false, new Color(0.9f, 0.05f, 0.08f) * 0.8f, PrimitiveType.Cube,
                                    Quaternion.Euler(35f, 45f, 0f));
            Vector3 off = Vector3.right * Mathf.Clamp(thick * 1.1f + 0.06f, 0.12f, 0.45f);
            shard.transform.localPosition = off;
            anim.orbits.Add(orbit);
        }
        // 先端の赤い光輪
        var halo = Ring(root, At(1f) + axis * 0.04f, ringAxis, Mathf.Clamp(thick * 0.9f, 0.08f, 0.3f), new Color(1f, 0.1f, 0.1f, 0.95f), 0);
        anim.rings.Add(halo);
        var core = Glow(root, "Core", At(1f) + axis * 0.04f, Vector3.one * 0.05f, new Color(1f, 0.25f, 0.25f), 6f, PrimitiveType.Sphere);
        anim.pulses.Add(core.transform);
        // 火の粉と、黒い煙
        var ember = new GameObject("Ember").transform;
        ember.SetParent(root, false);
        ember.localPosition = At(bladeT);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.15f, 0.1f), color2 = new Color(1f, 0.6f, 0.2f), count = 0, rate = 18f, duration = 1f, loop = true,
            speedMin = 0.2f, speedMax = 0.6f, sizeMin = 0.02f, sizeMax = 0.06f, life = 0.7f, gravity = -0.9f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(thick * 0.8f, thick * 0.8f, len * 0.55f),
        }, Vector3.zero, ember, Quaternion.LookRotation(axis));
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.08f, 0.02f, 0.03f, 0.55f), color2 = new Color(0.3f, 0.02f, 0.05f, 0.35f), count = 0, rate = 5f, duration = 1f, loop = true, additive = false,
            speedMin = 0.05f, speedMax = 0.2f, sizeMin = 0.08f, sizeMax = 0.16f, life = 1.2f, gravity = -0.2f, grow = true,
            shape = ParticleSystemShapeType.Box, box = new Vector3(thick, thick, len * 0.5f),
        }, Vector3.zero, ember, Quaternion.LookRotation(axis));
        // 振ると、赤い残光
        var tip = new GameObject("Trail");
        tip.transform.SetParent(root, false);
        tip.transform.localPosition = At(1f);
        var tr = tip.AddComponent<TrailRenderer>();
        tr.time = 0.18f;
        tr.minVertexDistance = 0.02f;
        tr.widthMultiplier = Mathf.Clamp(thick * 0.8f, 0.04f, 0.2f);
        tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        tr.sharedMaterial = RingMat();
        tr.startColor = new Color(1f, 0.15f, 0.15f, 0.8f);
        tr.endColor = new Color(0.3f, 0f, 0f, 0f);
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return root.gameObject;
    }

    public static void Remove(Transform holder)
    {
        if (holder == null) return;
        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            var c = holder.GetChild(i);
            if (c.name == RootName) Object.Destroy(c.gameObject);
        }
    }

    /// <summary>落ちている装備・アイテムの飾り（エピック以上）: 光の柱・回る輪・粒。</summary>
    public static void DropFx(Transform root, Rarity r, float baseY)
    {
        if (root == null || r < Rarity.Epic) return;
        Color col = Main(r);
        var fx = new GameObject(RootName).transform;
        fx.SetParent(root, false);
        var anim = fx.gameObject.AddComponent<RarityDecorAnim>();
        anim.rarity = r;
        anim.axis = Vector3.up;
        // 光の柱
        var beam = new GameObject("Beam").AddComponent<LineRenderer>();
        beam.transform.SetParent(fx, false);
        beam.useWorldSpace = false;
        beam.positionCount = 2;
        beam.SetPosition(0, Vector3.up * 0.1f);
        beam.SetPosition(1, Vector3.up * (r == Rarity.Mythic ? 6f : r == Rarity.Legendary ? 4.5f : 3f));
        beam.widthMultiplier = r == Rarity.Mythic ? 0.32f : 0.22f;
        beam.sharedMaterial = RingMat();
        beam.startColor = new Color(col.r, col.g, col.b, 0.55f);
        beam.endColor = new Color(col.r, col.g, col.b, 0f);
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // 足元の輪
        anim.rings.Add(Ring(fx, Vector3.up * 0.05f, Vector3.up, 0.45f, new Color(col.r, col.g, col.b, 0.8f), r >= Rarity.Legendary ? 6 : 0));
        if (r >= Rarity.Legendary) anim.rings.Add(Ring(fx, Vector3.up * 0.4f, Vector3.up, 0.3f, new Color(col.r, col.g, col.b, 0.7f), 0));
        Fx.Play(new Fx.Spec
        {
            color = col, color2 = Color.white, count = 0, rate = r == Rarity.Mythic ? 16f : 9f, duration = 1f, loop = true,
            speedMin = 0.2f, speedMax = 0.6f, sizeMin = 0.03f, sizeMax = 0.07f, life = 1.4f, gravity = -0.4f, radius = 0.35f,
            shape = ParticleSystemShapeType.Circle,
        }, Vector3.up * 0.1f, fx, Quaternion.Euler(-90f, 0f, 0f));
        if (r == Rarity.Mythic)
        {
            for (int i = 0; i < 3; i++)
            {
                var orbit = new GameObject("Orbit").transform;
                orbit.SetParent(fx, false);
                orbit.localPosition = Vector3.up * (0.4f + i * 0.12f);
                orbit.localRotation = Quaternion.Euler(0f, i * 120f, 0f);
                var shard = GeoMesh.Box(orbit, "Shard", Vector3.right * 0.45f, new Vector3(0.06f, 0.06f, 0.14f), new Color(0.08f, 0.02f, 0.03f), false, new Color(0.9f, 0.05f, 0.08f) * 0.8f, PrimitiveType.Cube, Quaternion.Euler(30f, 0f, 45f));
                anim.orbits.Add(orbit);
            }
        }
    }

    // ---------------------------------------------------------------- 部品

    static GameObject Glow(Transform parent, string name, Vector3 pos, Vector3 scale, Color c, float glow, PrimitiveType type = PrimitiveType.Cube)
        => GeoMesh.Box(parent, name, pos, scale, c, false, c * glow, type);

    /// <summary>軸 axis のまわりの、光る輪（star = 星形の頂点の数。0 なら円だけ）。</summary>
    static Transform Ring(Transform parent, Vector3 pos, Vector3 axis, float radius, Color c, int star)
    {
        var t = new GameObject("Ring").transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = Quaternion.FromToRotation(Vector3.forward, axis);
        var lr = t.gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = star == 0;
        const int N = 40;
        var pts = new List<Vector3>();
        for (int i = 0; i < N; i++) { float a = i * Mathf.PI * 2f / N; pts.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius); }
        if (star >= 5)
        {
            pts.Add(pts[0]);
            for (int i = 0; i <= star; i++)
            {
                float a = (i * 2 % star) * Mathf.PI * 2f / star + Mathf.PI * 0.5f;
                pts.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius * 0.85f);
            }
        }
        lr.positionCount = pts.Count;
        lr.SetPositions(pts.ToArray());
        lr.widthMultiplier = Mathf.Clamp(radius * 0.06f, 0.006f, 0.03f);
        lr.sharedMaterial = RingMat();
        lr.startColor = lr.endColor = c;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.alignment = LineAlignment.TransformZ;
        return t;
    }

    /// <summary>parent のローカル座標での、model の見た目の大きさ（粒・線・文字は除く）。</summary>
    static Bounds LocalBounds(Transform parent, Transform model)
    {
        bool has = false;
        var b = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(0.1f, 0.1f, 1f));
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.GetComponentInParent<RarityDecorAnim>() != null) continue;
            var wb = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 lp = parent.InverseTransformPoint(c);
                if (!has) { b = new Bounds(lp, Vector3.zero); has = true; } else b.Encapsulate(lp);
            }
        }
        return b;
    }
}

/// <summary>飾りの動き（輪が回る・結晶が巡る・光が脈打つ）。</summary>
public class RarityDecorAnim : MonoBehaviour
{
    public Rarity rarity;
    public Vector3 axis = Vector3.forward;
    public readonly List<Transform> rings = new List<Transform>();
    public readonly List<Transform> orbits = new List<Transform>();
    public readonly List<Transform> pulses = new List<Transform>();
    public Light glowLight;
    readonly Dictionary<Transform, Vector3> baseScale = new Dictionary<Transform, Vector3>();

    void Start()
    {
        foreach (var p in pulses) if (p != null) baseScale[p] = p.localScale;
    }

    void Update()
    {
        float t = Time.time;
        float dt = Time.deltaTime;
        for (int i = 0; i < rings.Count; i++)
            if (rings[i] != null) rings[i].Rotate(0f, 0f, (i % 2 == 0 ? 60f : -90f) * dt, Space.Self);
        foreach (var o in orbits) if (o != null) o.Rotate(axis, 140f * dt, Space.Self);
        float speed = rarity == Rarity.Mythic ? 5f : 3f;
        float k = 1f + (rarity == Rarity.Mythic ? 0.25f : 0.12f) * Mathf.Sin(t * speed);
        foreach (var p in pulses)
            if (p != null && baseScale.TryGetValue(p, out var s)) p.localScale = s * k;
        if (glowLight != null) glowLight.intensity = (rarity == Rarity.Mythic ? 1.6f : 1.1f) * (0.8f + 0.4f * Mathf.PerlinNoise(t * 2.5f, 0.3f));
    }
}
