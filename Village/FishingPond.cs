using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 村の北西の端の、大きな池（釣りができる）。桟橋と、桟橋の前でイスに座っている釣り人もいる。
/// 池の中は、ダンジョンの水と同じように泳げる（WaterZones.Ponds に登録する）。
/// 地面（500m 四方の板）に穴をあけるため、地面を、池のまわりの4枚に分け直す。
/// </summary>
public static class FishingPond
{
    // 池の範囲（x-z）と、深さ
    public static readonly Rect Area = new Rect(-73f, 11f, 24f, 14f);
    const float SurfaceY = -0.3f;
    const float FloorY = -3.4f;

    // 桟橋（池の南の岸から、北へ）
    const float PierX = -58f, PierZ0 = 8.5f, PierZ1 = 19.5f, PierW = 2.6f, PierTop = 0.25f;

    static readonly Color Bank = new Color(0.42f, 0.33f, 0.22f);
    static readonly Color Sand = new Color(0.82f, 0.74f, 0.52f);
    static readonly Color Mud = new Color(0.3f, 0.27f, 0.2f);
    static readonly Color Plank = new Color(0.55f, 0.4f, 0.24f);
    static readonly Color DarkWood = new Color(0.33f, 0.22f, 0.12f);
    static readonly Color Stone = new Color(0.55f, 0.54f, 0.5f);

    static Transform root;

    /// <summary>この位置（margin を足した範囲）は、池か（木・茂みを置かない）。</summary>
    public static bool Blocks(Vector3 p, float margin)
        => p.x > Area.xMin - margin && p.x < Area.xMax + margin && p.z > Area.yMin - margin - 3f && p.z < Area.yMax + margin;

    public static void Build(Transform parent)
    {
        root = new GameObject("FishingPond").transform;
        root.SetParent(parent, false);

        ClearDecor();
        CutGround();
        BuildBasin();
        BuildWater();
        BuildPier();
        BuildShore();
        Fisherman.Spawn(root, new Vector3(PierX - 2.6f, 0f, 9.9f));

        var zone = root.gameObject.AddComponent<PondRegistration>();
        zone.pond = new WaterZones.Pond { rect = Area, floorY = FloorY, surfaceY = SurfaceY, fishable = true };
    }

    // ---------------------------------------------------------------- 地面

    /// <summary>池の範囲にある、元の草・木・花・石などを片づける。</summary>
    static void ClearDecor()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || t.parent == null) continue;
            string n = t.name;
            if (n != "Tree" && n != "Flower" && n != "Stem" && n != "Bush" && n != "Stone" && n != "Patch") continue;
            if (!Blocks(t.position, 1.5f)) continue;
            t.gameObject.SetActive(false);
            Object.Destroy(t.gameObject);
        }
    }

    /// <summary>500m 四方の地面を、池の穴のまわりの4枚に分ける（元の地面は、見た目と当たり判定を切る）。</summary>
    static void CutGround()
    {
        Transform ground = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || t.name != "Ground" || t.GetComponent<BoxCollider>() == null) continue;
            if (t.lossyScale.x < 200f || t.lossyScale.z < 200f) continue;
            ground = t;
            break;
        }
        if (ground == null) { Debug.LogWarning("[池] 地面が見つからないので、池を掘れませんでした。"); return; }

        var rend = ground.GetComponent<Renderer>();
        var col = ground.GetComponent<BoxCollider>();
        Bounds b = col.bounds;
        Material mat = rend != null ? rend.sharedMaterial : null;
        if (rend != null) rend.enabled = false;
        col.enabled = false;

        float x0 = b.min.x, x1 = b.max.x, z0 = b.min.z, z1 = b.max.z, y0 = b.min.y, y1 = b.max.y;
        GroundPiece(mat, x0, x1, z0, Area.yMin, y0, y1);              // 南
        GroundPiece(mat, x0, x1, Area.yMax, z1, y0, y1);              // 北
        GroundPiece(mat, x0, Area.xMin, Area.yMin, Area.yMax, y0, y1); // 西
        GroundPiece(mat, Area.xMax, x1, Area.yMin, Area.yMax, y0, y1); // 東
    }

    static void GroundPiece(Material mat, float x0, float x1, float z0, float z1, float y0, float y1)
    {
        if (x1 - x0 < 0.01f || z1 - z0 < 0.01f) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Ground";
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        go.transform.localScale = new Vector3(x1 - x0, y1 - y0, z1 - z0);
        var r = go.GetComponent<Renderer>();
        if (mat != null) r.sharedMaterial = mat;
        go.isStatic = true;
    }

    // ---------------------------------------------------------------- 池

    static GameObject Box(string name, Vector3 center, Vector3 size, Color color, bool collider, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent != null ? parent : root, false);
        go.transform.position = center;
        go.transform.localScale = size;
        if (!collider) Object.Destroy(go.GetComponent<Collider>());
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }

    static GameObject Prim(PrimitiveType type, string name, Vector3 center, Vector3 size, Color color, Quaternion rot, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent != null ? parent : root, false);
        go.transform.position = center;
        go.transform.rotation = rot;
        go.transform.localScale = size;
        Object.Destroy(go.GetComponent<Collider>());
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }

    /// <summary>池の底と、まわりの土手（壁）。南西には、泳いで上がれる、なだらかな砂浜。</summary>
    static void BuildBasin()
    {
        float cx = Area.center.x, cz = Area.center.y, w = Area.width, d = Area.height;
        float h = -FloorY;
        Box("PondFloor", new Vector3(cx, FloorY - 0.25f, cz), new Vector3(w + 1f, 0.5f, d + 1f), Mud, true);
        // 土手（上の端は、地面より少し下。地面の縁と重ならないように）
        Box("PondBank", new Vector3(Area.xMin - 0.25f, FloorY * 0.5f - 0.01f, cz), new Vector3(0.5f, h - 0.02f, d + 1f), Bank, true);
        Box("PondBank", new Vector3(Area.xMax + 0.25f, FloorY * 0.5f - 0.01f, cz), new Vector3(0.5f, h - 0.02f, d + 1f), Bank, true);
        Box("PondBank", new Vector3(cx, FloorY * 0.5f - 0.01f, Area.yMin - 0.25f), new Vector3(w + 1f, h - 0.02f, 0.5f), Bank, true);
        Box("PondBank", new Vector3(cx, FloorY * 0.5f - 0.01f, Area.yMax + 0.25f), new Vector3(w + 1f, h - 0.02f, 0.5f), Bank, true);

        // 砂浜（南西の岸から、池の底へ下る坂）: 泳いで、ここから陸へ上がれる
        const float bx0 = -72.5f, bx1 = -64f;
        float run = 8f;
        float len = Mathf.Sqrt(run * run + h * h);
        float ang = Mathf.Atan2(h, run) * Mathf.Rad2Deg;
        var ramp = Box("PondBeach", new Vector3((bx0 + bx1) * 0.5f, FloorY * 0.5f - 0.18f, Area.yMin + run * 0.5f), new Vector3(bx1 - bx0, 0.4f, len), Sand, true);
        ramp.transform.rotation = Quaternion.Euler(ang, 0f, 0f);
        // 砂浜の両わきに、小石
        var rng = new System.Random(911);
        for (int i = 0; i < 14; i++)
        {
            float t = (float)rng.NextDouble();
            float z = Area.yMin + t * run;
            float y = -t * h + 0.05f;
            float x = i % 2 == 0 ? bx0 + 0.2f : bx1 - 0.2f;
            Prim(PrimitiveType.Sphere, "Pebble", new Vector3(x, y, z), new Vector3(0.5f, 0.3f, 0.45f) * (0.7f + (float)rng.NextDouble() * 0.6f), Stone, Quaternion.Euler(0f, i * 37f, 0f));
        }

        // 水底の水草
        for (int i = 0; i < 40; i++)
        {
            float x = Area.xMin + 1f + (float)rng.NextDouble() * (w - 2f);
            float z = Area.yMin + 4f + (float)rng.NextDouble() * (d - 5f);
            float hh = 0.6f + (float)rng.NextDouble() * 1.4f;
            Prim(PrimitiveType.Cube, "Weed", new Vector3(x, FloorY + hh * 0.5f, z), new Vector3(0.08f, hh, 0.08f), new Color(0.2f, 0.45f + (float)rng.NextDouble() * 0.2f, 0.25f),
                 Quaternion.Euler((float)rng.NextDouble() * 16f - 8f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 16f - 8f));
        }
    }

    /// <summary>水面（ダンジョンの水と同じ、2枚重ねの半透明の板）と、水底の光の網目。</summary>
    static void BuildWater()
    {
        float cx = Area.center.x, cz = Area.center.y, w = Area.width, d = Area.height;
        WaterSheet("PondWater", new Vector3(cx, SurfaceY, cz), w, d, DungeonGenerator.SharedWaterMaterial(0), new Vector2(0.018f, 0.011f), 1f,
                   new Color(0.38f, 0.7f, 1f, 0.3f), new Color(0.55f, 0.85f, 1f, 0.38f), 1.3f);
        WaterSheet("PondWater2", new Vector3(cx, SurfaceY + 0.025f, cz), w, d, DungeonGenerator.SharedWaterMaterial(1), new Vector2(-0.013f, 0.017f), 0.7f,
                   new Color(0.5f, 0.8f, 1f, 0.14f), new Color(0.8f, 0.95f, 1f, 0.22f), 0.9f);
        WaterSheet("PondCaustics", new Vector3(cx, FloorY + 0.04f, cz), w, d, DungeonGenerator.SharedWaterMaterial(2), new Vector2(0.03f, 0.02f), 1.1f,
                   new Color(0.55f, 0.85f, 1f, 1f), new Color(0.9f, 1f, 1f, 1f), 1.6f);
    }

    static void WaterSheet(string name, Vector3 center, float w, float d, Material mat, Vector2 scroll, float scale, Color a, Color b, float pulse)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = center;
        var mesh = new Mesh { name = name };
        float hw = w * 0.5f, hd = d * 0.5f;
        mesh.SetVertices(new List<Vector3> { new Vector3(-hw, 0f, -hd), new Vector3(hw, 0f, -hd), new Vector3(hw, 0f, hd), new Vector3(-hw, 0f, hd) });
        float u = w / 8f, v = d / 8f;
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(u, 0f), new Vector2(u, v), new Vector2(0f, v) });
        mesh.SetTriangles(new[] { 0, 3, 2, 0, 2, 1, 0, 2, 3, 0, 1, 2 }, 0);   // 上からも、水中からも見える
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var sh = go.AddComponent<WaterShimmer>();
        sh.scroll = scroll; sh.scale = scale; sh.colorA = a; sh.colorB = b; sh.pulse = pulse;
    }

    // ---------------------------------------------------------------- 桟橋・岸

    static void BuildPier()
    {
        var pier = new GameObject("Pier").transform;
        pier.SetParent(root, false);
        float len = PierZ1 - PierZ0;
        // 板（歩ける。当たり判定は、1枚の板）
        var deck = Box("PierDeck", new Vector3(PierX, PierTop - 0.08f, (PierZ0 + PierZ1) * 0.5f), new Vector3(PierW, 0.16f, len), Plank, true, pier);
        deck.GetComponent<Renderer>().enabled = false;   // 見た目は、細い板を並べる
        int n = Mathf.RoundToInt(len / 0.5f);
        for (int i = 0; i < n; i++)
        {
            float z = PierZ0 + (i + 0.5f) * len / n;
            Color c = i % 3 == 0 ? Plank * 0.92f : i % 3 == 1 ? Plank : Plank * 1.06f;
            c.a = 1f;
            Box("Plank", new Vector3(PierX, PierTop - 0.07f, z), new Vector3(PierW, 0.14f, len / n - 0.05f), c, false, pier);
        }
        // 支柱（水底まで）と、手すり
        for (float z = Area.yMin; z <= PierZ1 + 0.01f; z += 2.75f)
            foreach (float s in new[] { -1f, 1f })
            {
                float px = PierX + s * (PierW * 0.5f - 0.1f);
                Prim(PrimitiveType.Cylinder, "Post", new Vector3(px, (FloorY + PierTop + 0.6f) * 0.5f, z), new Vector3(0.22f, (PierTop + 0.6f - FloorY) * 0.5f, 0.22f), DarkWood, Quaternion.identity, pier);
            }
        foreach (float s in new[] { -1f, 1f })
            Box("Rail", new Vector3(PierX + s * (PierW * 0.5f - 0.1f), PierTop + 0.55f, (Area.yMin + PierZ1) * 0.5f), new Vector3(0.1f, 0.08f, PierZ1 - Area.yMin), DarkWood, false, pier);

        // 桟橋の先の、ランタン（夜に灯る）
        Vector3 lp = new Vector3(PierX + PierW * 0.5f - 0.1f, 0f, PierZ1 - 0.2f);
        Prim(PrimitiveType.Cylinder, "LampPost", lp + Vector3.up * (PierTop + 0.9f), new Vector3(0.1f, 0.9f, 0.1f), DarkWood, Quaternion.identity, pier);
        var lantern = Prim(PrimitiveType.Cube, "Lantern", lp + Vector3.up * (PierTop + 1.95f), new Vector3(0.3f, 0.38f, 0.3f), new Color(0.9f, 0.8f, 0.5f), Quaternion.identity, pier);
        var lg = new GameObject("LampLight");
        lg.transform.SetParent(lantern.transform, false);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.82f, 0.5f);
        l.range = 12f;
        l.intensity = 10f;
        l.enabled = false;
        var nl = lantern.AddComponent<NightLight>();
        nl.lampLight = l;
        nl.glow = lantern.GetComponent<Renderer>();
        nl.glowColor = new Color(1f, 0.85f, 0.5f);
        nl.dayColor = new Color(0.7f, 0.65f, 0.5f);
        nl.glowIntensity = 3f;
        nl.Set(TimeOfDay.IsNight);
    }

    /// <summary>岸の飾り: 石・葦・睡蓮の葉、看板、釣り人の道具。</summary>
    static void BuildShore()
    {
        var rng = new System.Random(4242);
        // 岸の石（砂浜と桟橋の所は避ける）
        for (int i = 0; i < 44; i++)
        {
            float t = (float)rng.NextDouble();
            Vector3 p;
            int side = i % 4;
            if (side == 0) p = new Vector3(Mathf.Lerp(Area.xMin, Area.xMax, t), 0f, Area.yMax + 0.4f);
            else if (side == 1) p = new Vector3(Area.xMin - 0.4f, 0f, Mathf.Lerp(Area.yMin, Area.yMax, t));
            else if (side == 2) p = new Vector3(Area.xMax + 0.4f, 0f, Mathf.Lerp(Area.yMin, Area.yMax, t));
            else p = new Vector3(Mathf.Lerp(Area.xMin, Area.xMax, t), 0f, Area.yMin - 0.4f);
            if (side == 3 && (p.x < -63.5f || Mathf.Abs(p.x - PierX) < 2.2f)) continue;
            float s = 0.5f + (float)rng.NextDouble() * 0.7f;
            Prim(PrimitiveType.Sphere, "ShoreStone", p + Vector3.up * 0.08f, new Vector3(s, s * 0.5f, s * 0.9f), Stone * (0.85f + (float)rng.NextDouble() * 0.25f), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
        }
        // 葦（北と東の岸）
        for (int i = 0; i < 70; i++)
        {
            bool north = i % 2 == 0;
            float t = (float)rng.NextDouble();
            Vector3 p = north ? new Vector3(Mathf.Lerp(Area.xMin + 0.5f, Area.xMax - 0.5f, t), 0f, Area.yMax - 0.4f - (float)rng.NextDouble() * 0.8f)
                              : new Vector3(Area.xMax - 0.4f - (float)rng.NextDouble() * 0.8f, 0f, Mathf.Lerp(Area.yMin + 2f, Area.yMax - 0.5f, t));
            float hh = 1.2f + (float)rng.NextDouble() * 1.0f;
            float bottom = SurfaceY - 0.4f;
            Prim(PrimitiveType.Cube, "Reed", new Vector3(p.x, bottom + hh * 0.5f, p.z), new Vector3(0.05f, hh, 0.05f), new Color(0.45f, 0.6f, 0.25f),
                 Quaternion.Euler((float)rng.NextDouble() * 12f - 6f, 0f, (float)rng.NextDouble() * 12f - 6f));
            if (i % 5 == 0) Prim(PrimitiveType.Capsule, "ReedHead", new Vector3(p.x, bottom + hh + 0.12f, p.z), new Vector3(0.09f, 0.16f, 0.09f), new Color(0.45f, 0.3f, 0.18f), Quaternion.identity);
        }
        // 睡蓮の葉と花
        for (int i = 0; i < 16; i++)
        {
            float x = Area.xMin + 2f + (float)rng.NextDouble() * (Area.width - 4f);
            float z = Area.yMin + 6f + (float)rng.NextDouble() * (Area.height - 7f);
            if (Mathf.Abs(x - PierX) < 2.5f) continue;
            float s = 0.7f + (float)rng.NextDouble() * 0.6f;
            Prim(PrimitiveType.Cylinder, "LilyPad", new Vector3(x, SurfaceY + 0.04f, z), new Vector3(s, 0.01f, s), new Color(0.25f, 0.55f, 0.25f), Quaternion.identity);
            if (i % 3 == 0) Prim(PrimitiveType.Sphere, "Lily", new Vector3(x + 0.1f, SurfaceY + 0.12f, z), new Vector3(0.22f, 0.14f, 0.22f), new Color(1f, 0.75f, 0.85f), Quaternion.identity);
        }
        // 看板
        var sign = new GameObject("PondSign").transform;
        sign.SetParent(root, false);
        sign.position = new Vector3(PierX + 2.4f, 0f, 9.4f);
        Prim(PrimitiveType.Cube, "SignPost", sign.position + Vector3.up * 0.6f, new Vector3(0.12f, 1.2f, 0.12f), DarkWood, Quaternion.identity, sign);
        Prim(PrimitiveType.Cube, "SignBoard", sign.position + Vector3.up * 1.25f, new Vector3(1.4f, 0.55f, 0.08f), Plank, Quaternion.identity, sign);
        var label = Fisherman.MakeText(sign, "SignText", "釣り池", sign.position + new Vector3(0f, 1.25f, -0.06f), 0.03f, new Color(0.15f, 0.1f, 0.05f));
        label.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        // 道具（バケツと、たる）
        Prim(PrimitiveType.Cylinder, "Bucket", new Vector3(PierX - 1.5f, 0.22f, 9.0f), new Vector3(0.4f, 0.22f, 0.4f), new Color(0.5f, 0.52f, 0.55f), Quaternion.identity);
        Prim(PrimitiveType.Cylinder, "BucketWater", new Vector3(PierX - 1.5f, 0.42f, 9.0f), new Vector3(0.34f, 0.02f, 0.34f), new Color(0.35f, 0.6f, 0.85f), Quaternion.identity);
        Prim(PrimitiveType.Cylinder, "Barrel", new Vector3(PierX - 3.8f, 0.45f, 8.6f), new Vector3(0.7f, 0.45f, 0.7f), DarkWood, Quaternion.identity);
    }
}

/// <summary>池を、泳げる水として登録する（シーンを出ると、消える）。</summary>
public class PondRegistration : MonoBehaviour
{
    public WaterZones.Pond pond;

    void OnEnable() { if (pond != null && !WaterZones.Ponds.Contains(pond)) WaterZones.Ponds.Add(pond); }
    void Start() { if (pond != null && !WaterZones.Ponds.Contains(pond)) WaterZones.Ponds.Add(pond); }
    void OnDisable() { if (pond != null) WaterZones.Ponds.Remove(pond); }
}

/// <summary>
/// 桟橋の前で、イスに座って釣りをしている釣り人。話しかけると、釣り竿を貸してくれる（釣り竿を持っていないとき）。
/// </summary>
public class Fisherman : MonoBehaviour, IInteractable
{
    const string LendRodId = "wp_rod_bamboo";

    TextMesh label;
    Transform floatT;
    Vector3 floatBase;
    float talkCooldown;

    public string Prompt => "E : 釣り人に話しかける（魚図鑑）";
    public bool CanInteract => !FishBookUI.IsOpen;

    public void Interact(GameObject interactor)
    {
        if (Time.unscaledTime < talkCooldown) return;
        talkCooldown = Time.unscaledTime + 0.6f;
        AudioManager.PlaySfx("decide", 0.5f);

        if (!HasRod() && GameSession.FishingRodLent)
        {
            Say("竿をなくしちまったのかい？　もう貸せる竿はないんだ。大鍋で、竹の釣り竿を作れるはずだよ。骨片と、蜘蛛の糸があればね。");
            return;
        }
        if (!HasRod())
        {
            var db = ItemDatabase.Instance;
            var rod = db != null ? db.Find(LendRodId) : null;
            if (rod != null && GameSession.Add(rod) != null)
            {
                GameSession.FishingRodLent = true;   // 貸すのは、1回だけ（捨てて、また借りるのを防ぐ）
                SaveSystem.Save();
                Say("おや、釣りに興味があるのかい？　この竿を貸してあげよう。返さなくていいよ。");
                Hz.Toast($"<color=#{ColorUtility.ToHtmlStringRGB(rod.RarityColor)}>{rod.displayName}</color> を借りた！  <size=20><color=#aaaaaa>（装備して、池に向かって左クリック長押し → 離す）</color></size>");
                return;
            }
            Say("竿を貸してあげたいんだけど、荷物がいっぱいみたいだね。");
            return;
        }

        // 竿を持っていれば、魚図鑑を見せてくれる
        string[] tips =
        {
            "昼と夜で、釣れる魚が違うんだ。夜にしか顔を出さない大物もいる。",
            "同じ魚でも、大きさは毎回違う。ごくまれに、とんでもない大物が掛かるぞ。",
            "左クリックを長押しして、離すと糸を投げられる。長くためるほど、遠くへ飛ぶよ。",
            "浮きが激しく沈んだら、魚が食いついた合図だ。3秒以内に左クリック！",
            "掛かったら、あとは連打だ。攻撃力が高いほど、力強く引ける。",
            "いい竿ほど、珍しい魚が掛かりやすい。池のヌシを見た者もいるらしい……。",
            "釣った魚は、大鍋で料理にできる。料理には、いろんな効き目があるよ。",
            "たまに、魚じゃない物が掛かることもある。魔法石や、魔水晶なんかもね。",
        };
        var ui = interactor.GetComponent<FishBookUI>();
        if (ui == null) ui = interactor.AddComponent<FishBookUI>();
        ui.Open(tips[Random.Range(0, tips.Length)]);
    }

    static bool HasRod()
    {
        var eq = GameSession.GetEquipped(EquipSlot.Weapon);
        if (eq != null && eq.data != null && !eq.data.isStaff && eq.data.type == ItemType.Weapon && eq.data.weaponKind == WeaponKind.FishingRod) return true;
        foreach (var it in GameSession.Bag)
            if (it != null && it.data != null && it.data.type == ItemType.Weapon && !it.data.isStaff && it.data.weaponKind == WeaponKind.FishingRod) return true;
        return false;
    }

    static void Say(string text) => Hz.Toast($"<color=#9fd8ff>釣り人</color>「{text}」");

    void Update()
    {
        // 釣り人の浮きが、ゆらゆらする
        if (floatT != null) floatT.position = floatBase + Vector3.up * Mathf.Sin(Time.time * 2.2f) * 0.035f;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null && label != null) label.transform.rotation = cam.transform.rotation;
    }

    // ---------------------------------------------------------------- 見た目

    public static void Spawn(Transform parent, Vector3 pos)
    {
        var root = new GameObject("Fisherman").transform;
        root.SetParent(parent, false);
        root.position = pos;
        root.rotation = Quaternion.Euler(0f, 10f, 0f);   // 池の方（北）を向く
        var f = root.gameObject.AddComponent<Fisherman>();

        var col = root.gameObject.AddComponent<CapsuleCollider>();
        col.height = 1.6f;
        col.radius = 0.45f;
        col.center = new Vector3(0f, 0.8f, 0f);

        Color wood = new Color(0.45f, 0.3f, 0.16f), shirt = new Color(0.3f, 0.45f, 0.55f), pants = new Color(0.35f, 0.3f, 0.22f), skin = new Color(0.92f, 0.76f, 0.62f);
        Color vest = new Color(0.4f, 0.45f, 0.3f), boots = new Color(0.2f, 0.18f, 0.15f), straw = new Color(0.88f, 0.75f, 0.35f);

        // イス
        P(root, PrimitiveType.Cube, "Seat", new Vector3(0f, 0.5f, 0f), new Vector3(0.6f, 0.08f, 0.55f), wood);
        P(root, PrimitiveType.Cube, "Back", new Vector3(0f, 0.85f, -0.26f), new Vector3(0.6f, 0.65f, 0.06f), wood);
        foreach (var o in new[] { new Vector2(0.25f, 0.22f), new Vector2(-0.25f, 0.22f), new Vector2(0.25f, -0.22f), new Vector2(-0.25f, -0.22f) })
            P(root, PrimitiveType.Cube, "ChairLeg", new Vector3(o.x, 0.23f, o.y), new Vector3(0.06f, 0.46f, 0.06f), wood);

        // 座っている体
        foreach (float s in new[] { 1f, -1f })
        {
            P(root, PrimitiveType.Cube, "Thigh", new Vector3(s * 0.12f, 0.62f, 0.15f), new Vector3(0.17f, 0.17f, 0.45f), pants);
            P(root, PrimitiveType.Cube, "Shin", new Vector3(s * 0.12f, 0.33f, 0.36f), new Vector3(0.16f, 0.5f, 0.16f), pants);
            P(root, PrimitiveType.Cube, "Boot", new Vector3(s * 0.12f, 0.06f, 0.42f), new Vector3(0.19f, 0.12f, 0.3f), boots);
        }
        P(root, PrimitiveType.Cube, "Torso", new Vector3(0f, 0.97f, -0.04f), new Vector3(0.46f, 0.6f, 0.28f), shirt);
        P(root, PrimitiveType.Cube, "Vest", new Vector3(0f, 0.97f, -0.03f), new Vector3(0.48f, 0.5f, 0.3f), vest);
        foreach (float s in new[] { 1f, -1f })
        {
            var arm = P(root, PrimitiveType.Cube, "Arm", new Vector3(s * 0.28f, 0.98f, 0.14f), new Vector3(0.13f, 0.13f, 0.45f), shirt);
            arm.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            P(root, PrimitiveType.Sphere, "Hand", new Vector3(s * 0.18f, 0.88f, 0.38f), Vector3.one * 0.12f, skin);
        }
        P(root, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.47f, -0.02f), Vector3.one * 0.34f, skin);
        foreach (float s in new[] { 1f, -1f })
            P(root, PrimitiveType.Sphere, "Eye", new Vector3(s * 0.07f, 1.5f, 0.13f), Vector3.one * 0.04f, Color.black);
        P(root, PrimitiveType.Cube, "Beard", new Vector3(0f, 1.36f, 0.09f), new Vector3(0.26f, 0.16f, 0.1f), new Color(0.8f, 0.8f, 0.78f));
        P(root, PrimitiveType.Cylinder, "HatBrim", new Vector3(0f, 1.61f, -0.02f), new Vector3(0.66f, 0.015f, 0.66f), straw);
        P(root, PrimitiveType.Cylinder, "HatTop", new Vector3(0f, 1.69f, -0.02f), new Vector3(0.34f, 0.08f, 0.34f), straw);
        P(root, PrimitiveType.Cylinder, "HatBand", new Vector3(0f, 1.63f, -0.02f), new Vector3(0.35f, 0.015f, 0.35f), new Color(0.25f, 0.4f, 0.6f));

        // 釣り竿と糸と浮き
        Vector3 grip = new Vector3(0f, 0.88f, 0.4f);
        Vector3 tip = new Vector3(0.3f, 2.6f, 2.9f);
        Seg(root, grip, tip, 0.035f, new Color(0.65f, 0.5f, 0.3f));
        Vector3 worldTip = root.TransformPoint(tip);
        Vector3 worldFloat = new Vector3(worldTip.x - 0.6f, -0.25f, Mathf.Max(worldTip.z + 2.2f, 13f));
        var line = new GameObject("Line").AddComponent<LineRenderer>();
        line.transform.SetParent(root, false);
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, worldTip);
        line.SetPosition(1, worldFloat + Vector3.up * 0.15f);
        line.widthMultiplier = 0.01f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.sharedMaterial = EnemyJumpFx.UnlitShared(new Color(0.92f, 0.92f, 0.88f));
        var fl = new GameObject("Float").transform;
        fl.SetParent(root, true);
        fl.position = worldFloat;
        var top = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(top.GetComponent<Collider>());
        top.transform.SetParent(fl, false);
        top.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        top.transform.localScale = new Vector3(0.18f, 0.12f, 0.18f);
        SharedMats.Apply(top.GetComponent<Renderer>(), new Color(0.95f, 0.15f, 0.12f));
        f.floatT = fl;
        f.floatBase = worldFloat;

        f.label = MakeText(root, "Name", "釣り人", root.position + Vector3.up * 2.05f, 0.035f, new Color(0.75f, 0.92f, 1f));
    }

    static GameObject P(Transform parent, PrimitiveType type, string name, Vector3 local, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }

    static void Seg(Transform parent, Vector3 a, Vector3 b, float w, Color c)
    {
        Vector3 d = b - a;
        var go = P(parent, PrimitiveType.Cube, "Rod", (a + b) * 0.5f, new Vector3(w, w, d.magnitude), c);
        go.transform.localRotation = Quaternion.LookRotation(d.normalized, Vector3.up);
    }

    public static TextMesh MakeText(Transform parent, string name, string text, Vector3 worldPos, float size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = worldPos;
        var tm = go.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = size;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        go.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;
        return tm;
    }
}
