using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 拠点シーン用の「澄んだ空の下の草原の村」を、プリミティブで生成する（Dungeon > Create Home Scene から呼ばれる）。
/// 地面・広場・崖（ダンジョン入口）・家・街灯・井戸・木・花・丘・雲・空・太陽と月。
/// </summary>
public static class HomeVillageBuilder
{
    static System.Random rng;
    // 木などを置かない範囲（x, z, 半径）
    static readonly List<Vector3> occupied = new List<Vector3>();

    static Material M(string name, Color c) => DungeonSceneSetup.GetOrCreateMaterial(name, c);

    static float R(float min, float max) => (float)(min + rng.NextDouble() * (max - min));

    public static void Build(Transform root)
    {
        rng = new System.Random(20240601);
        occupied.Clear();

        var grass = M("VGrass", new Color(0.30f, 0.56f, 0.20f));
        var grassDark = M("VGrassDark", new Color(0.23f, 0.46f, 0.17f));
        var grassLight = M("VGrassLight", new Color(0.42f, 0.63f, 0.25f));
        var plaza = M("VPlaza", new Color(0.66f, 0.62f, 0.54f));
        var path = M("VPath", new Color(0.70f, 0.60f, 0.42f));
        var rock = M("VRock", new Color(0.44f, 0.42f, 0.40f));
        var wood = M("VWood", new Color(0.40f, 0.26f, 0.12f));
        var metal = M("VMetal", new Color(0.15f, 0.15f, 0.17f));
        var leaf = M("VLeaf", new Color(0.20f, 0.50f, 0.18f));
        var leaf2 = M("VLeaf2", new Color(0.30f, 0.60f, 0.20f));
        var trunk = M("VTrunk", new Color(0.35f, 0.22f, 0.10f));
        var cloud = M("VCloud", new Color(1f, 1f, 1f));
        var door = M("VDoor", new Color(0.32f, 0.2f, 0.1f));
        var glass = M("VGlass", new Color(0.35f, 0.45f, 0.55f));
        var lampGlass = M("VLampGlass", new Color(0.9f, 0.8f, 0.5f));
        var flowerMats = new[]
        {
            M("VFlowerPink", new Color(0.95f, 0.5f, 0.7f)), M("VFlowerYellow", new Color(1f, 0.85f, 0.2f)),
            M("VFlowerWhite", new Color(0.97f, 0.97f, 0.97f)), M("VFlowerBlue", new Color(0.4f, 0.55f, 1f)),
            M("VFlowerRed", new Color(0.9f, 0.25f, 0.2f)),
        };
        var walls = new[] { M("VWallA", new Color(0.92f, 0.87f, 0.74f)), M("VWallB", new Color(0.86f, 0.72f, 0.62f)), M("VWallC", new Color(0.82f, 0.85f, 0.8f)) };
        var roofs = new[] { M("VRoofRed", new Color(0.70f, 0.25f, 0.18f)), M("VRoofBlue", new Color(0.25f, 0.36f, 0.62f)), M("VRoofBrown", new Color(0.45f, 0.30f, 0.20f)) };

        var env = new GameObject("Village").transform;
        env.SetParent(root, false);

        // ---- 地面
        Prim(PrimitiveType.Cube, "Ground", env, new Vector3(0f, -0.5f, 0f), new Vector3(500f, 1f, 500f), grass, true, true);
        for (int i = 0; i < 40; i++)
        {
            float s = R(8f, 22f);
            Prim(PrimitiveType.Cylinder, "Patch", env, new Vector3(R(-60f, 60f), 0.005f, R(-60f, 60f)),
                 new Vector3(s, 0.01f, s * R(0.6f, 1f)), i % 2 == 0 ? grassDark : grassLight, true, false);
        }

        // ---- 広場と道
        Prim(PrimitiveType.Cylinder, "Plaza", env, new Vector3(0f, 0.03f, 2f), new Vector3(26f, 0.06f, 26f), plaza, true, false);
        Prim(PrimitiveType.Cube, "PathSouth", env, new Vector3(0f, 0.02f, -22f), new Vector3(3.6f, 0.04f, 40f), path, true, false);
        Prim(PrimitiveType.Cube, "PathWest", env, new Vector3(-22f, 0.02f, -4f), new Vector3(30f, 0.04f, 3f), path, true, false);
        Prim(PrimitiveType.Cube, "PathEast", env, new Vector3(22f, 0.02f, -6f), new Vector3(30f, 0.04f, 3f), path, true, false);
        occupied.Add(new Vector3(0f, 2f, 15f));

        // ---- 崖（北側）: ダンジョンの入口はこの岩肌の前にある
        Prim(PrimitiveType.Cube, "Cliff", env, new Vector3(0f, 5f, 17f), new Vector3(90f, 10f, 8f), rock, true, true);
        for (int i = 0; i < 18; i++)
        {
            float s = R(2f, 5f);
            var go = Prim(PrimitiveType.Cube, "CliffRock", env, new Vector3(R(-42f, 42f), R(8f, 11f), R(14f, 20f)),
                          new Vector3(s, s * R(0.6f, 1.2f), s), rock, true, false);
            go.transform.rotation = Quaternion.Euler(R(-10f, 10f), R(0f, 90f), R(-10f, 10f));
        }
        Prim(PrimitiveType.Cube, "CliffTop", env, new Vector3(0f, 10.2f, 28f), new Vector3(90f, 0.4f, 24f), grass, true, true);

        // ---- 家
        House(env, new Vector3(-19f, 0f, -1f), 90f, 6f, 3f, 5f, walls[0], roofs[0], glass, door);
        House(env, new Vector3(-20f, 0f, -14f), 90f, 5f, 2.8f, 5f, walls[1], roofs[1], glass, door);
        House(env, new Vector3(18.5f, 0f, 3f), -90f, 6.5f, 3f, 5.5f, walls[2], roofs[2], glass, door);
        House(env, new Vector3(19f, 0f, -12f), -90f, 5.5f, 3f, 5f, walls[0], roofs[1], glass, door);
        House(env, new Vector3(-9f, 0f, -26f), 0f, 6f, 3f, 5f, walls[1], roofs[2], glass, door);
        House(env, new Vector3(9f, 0f, -27f), 0f, 5f, 2.8f, 5f, walls[2], roofs[0], glass, door);
        House(env, new Vector3(-27f, 0f, -26f), 30f, 5f, 2.8f, 5f, walls[0], roofs[2], glass, door);

        // ---- 街灯（夜に点く）
        foreach (var p in new[]
        {
            new Vector3(-4.5f, 0f, -3f), new Vector3(4.5f, 0f, -3f), new Vector3(-4.5f, 0f, 7f), new Vector3(4.5f, 0f, 7f),
            new Vector3(-11f, 0f, -6f), new Vector3(11f, 0f, -8f), new Vector3(2.5f, 0f, -17f), new Vector3(-2.5f, 0f, -22f),
        })
            Lamp(env, p, metal, lampGlass);

        // ---- 井戸
        Well(env, new Vector3(-10f, 0f, -12f), rock, wood, M("VWater", new Color(0.2f, 0.4f, 0.7f)));

        // ---- 柵（広場の西側と東側の端）
        for (int i = 0; i < 4; i++)
        {
            Fence(env, new Vector3(-12.5f, 0f, 6f + i * 1.6f), 90f, wood);
            Fence(env, new Vector3(12.5f, 0f, 9f + i * 1.6f), 90f, wood);
        }

        // ---- 木
        for (int i = 0; i < 70; i++)
        {
            Vector3 p = new Vector3(R(-42f, 42f), 0f, R(-42f, 11f));
            if (!IsFree(p, 3f)) continue;
            Tree(env, p, R(0.8f, 1.4f), trunk, i % 2 == 0 ? leaf : leaf2);
        }
        for (int i = 0; i < 90; i++) // 外の森
        {
            float a = R(0f, Mathf.PI * 2f), d = R(48f, 100f);
            Tree(env, new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), R(1.1f, 2f), trunk, i % 2 == 0 ? leaf : leaf2);
        }

        // ---- 草むらと花
        for (int c = 0; c < 45; c++)
        {
            Vector3 center = new Vector3(R(-40f, 40f), 0f, R(-40f, 11f));
            if (!IsFree(center, 1.5f)) continue;
            var fm = flowerMats[rng.Next(flowerMats.Length)];
            for (int k = 0; k < 6; k++)
            {
                var f = Prim(PrimitiveType.Sphere, "Flower", env, center + new Vector3(R(-1.2f, 1.2f), 0.12f, R(-1.2f, 1.2f)),
                             Vector3.one * R(0.16f, 0.24f), fm, true, false);
                Prim(PrimitiveType.Cylinder, "Stem", env, f.transform.position - Vector3.up * 0.07f, new Vector3(0.03f, 0.07f, 0.03f), leaf, true, false);
            }
        }
        for (int i = 0; i < 40; i++) // 低木
        {
            Vector3 p = new Vector3(R(-40f, 40f), 0.35f, R(-40f, 11f));
            if (!IsFree(p, 1.5f)) continue;
            Prim(PrimitiveType.Sphere, "Bush", env, p, new Vector3(R(0.9f, 1.6f), R(0.6f, 1f), R(0.9f, 1.6f)), leaf, true, false);
        }
        for (int i = 0; i < 25; i++) // 岩
        {
            Vector3 p = new Vector3(R(-40f, 40f), 0.2f, R(-40f, 11f));
            if (!IsFree(p, 1.5f)) continue;
            var r = Prim(PrimitiveType.Sphere, "Stone", env, p, new Vector3(R(0.6f, 1.5f), R(0.4f, 0.8f), R(0.6f, 1.2f)), rock, true, true);
            r.transform.rotation = Quaternion.Euler(0f, R(0f, 180f), 0f);
        }

        // ---- 遠景の丘と雲
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.PI * 2f + R(-0.2f, 0.2f), d = R(130f, 190f), h = R(25f, 60f);
            Prim(PrimitiveType.Sphere, "Hill", env, new Vector3(Mathf.Cos(a) * d, -h * 0.35f, Mathf.Sin(a) * d),
                 new Vector3(R(80f, 130f), h, R(80f, 130f)), i % 3 == 0 ? grassDark : grass, true, false);
        }
        var clouds = new GameObject("Clouds").transform; // 夜は HomeEnvironment が非表示にする
        clouds.SetParent(env, false);
        for (int i = 0; i < 16; i++)
        {
            float a = R(0f, Mathf.PI * 2f), d = R(40f, 150f), y = R(55f, 90f);
            for (int k = 0; k < 4; k++)
            {
                var cl = Prim(PrimitiveType.Sphere, "Cloud", clouds,
                              new Vector3(Mathf.Cos(a) * d + R(-12f, 12f), y + R(-2f, 3f), Mathf.Sin(a) * d + R(-8f, 8f)),
                              new Vector3(R(14f, 26f), R(5f, 9f), R(10f, 18f)), cloud, true, false);
                cl.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ---- 行動範囲の制限（見えない壁）
        var bounds = new GameObject("Bounds").transform;
        bounds.SetParent(root, false);
        Wall(bounds, new Vector3(0f, 10f, 28f), new Vector3(100f, 30f, 2f));  // 北（崖の上へ出さない）
        Wall(bounds, new Vector3(0f, 10f, -44f), new Vector3(100f, 30f, 2f));
        Wall(bounds, new Vector3(44f, 10f, 0f), new Vector3(2f, 30f, 100f));
        Wall(bounds, new Vector3(-44f, 10f, 0f), new Vector3(2f, 30f, 100f));
        // 崖の前（z>12.6）にも入れないよう、崖本体のコライダーで塞がれている

        // ---- 空と光
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/HomeSky.mat");
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.04f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            AssetDatabase.CreateAsset(sky, "Assets/Materials/HomeSky.mat");
        }
        RenderSettings.skybox = sky;

        var sunGo = new GameObject("Sun");
        sunGo.transform.SetParent(root, false);
        sunGo.transform.rotation = Quaternion.Euler(48f, -30f, 0f);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.intensity = 1.25f;
        sun.shadows = LightShadows.Soft;
        RenderSettings.sun = sun;

        var moonGo = new GameObject("Moon");
        moonGo.transform.SetParent(root, false);
        moonGo.transform.rotation = Quaternion.Euler(50f, 150f, 0f);
        var moon = moonGo.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.55f, 0.65f, 1f);
        moon.intensity = 0.45f;
        moon.shadows = LightShadows.Soft;
        moon.enabled = false;

        var envGo = new GameObject("HomeEnvironment");
        envGo.transform.SetParent(root, false);
        var home = envGo.AddComponent<HomeEnvironment>();
        home.sun = sun;
        home.moon = moon;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.78f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.6f, 0.65f, 0.6f);
        RenderSettings.ambientGroundColor = new Color(0.35f, 0.32f, 0.25f);
    }

    // ---------------------------------------------------------------- 部品

    static bool IsFree(Vector3 p, float margin)
    {
        // 広場・道・入口まわりは空けておく
        if (new Vector2(p.x, p.z - 2f).magnitude < 15f) return false;
        if (Mathf.Abs(p.x) < 3.5f && p.z < 12f) return false;
        if (Mathf.Abs(p.z + 5f) < 3f && Mathf.Abs(p.x) < 40f && p.z < 0f) return false;
        foreach (var o in occupied)
            if (new Vector2(p.x - o.x, p.z - o.y).magnitude < o.z + margin) return false;
        return true;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale,
                           Material mat, bool isStatic, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.isStatic = isStatic;
        return go;
    }

    static void Wall(Transform parent, Vector3 pos, Vector3 size)
    {
        var go = new GameObject("Wall");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.AddComponent<BoxCollider>().size = size;
    }

    /// <summary>家。front が +Z（ドア側）。w=幅, h=壁の高さ, d=奥行き。</summary>
    static void House(Transform parent, Vector3 pos, float yaw, float w, float h, float d,
                      Material wall, Material roof, Material glass, Material door)
    {
        var root = new GameObject("House").transform;
        root.SetParent(parent, false);
        root.position = pos;
        root.rotation = Quaternion.Euler(0f, yaw, 0f);
        occupied.Add(new Vector3(pos.x, pos.z, Mathf.Max(w, d) * 0.5f + 2.5f));

        Prim(PrimitiveType.Cube, "Body", root, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), wall, true, true);
        GableRoof(root, h, w, d, roof, wall);
        Prim(PrimitiveType.Cube, "Door", root, new Vector3(0f, 1f, d * 0.5f + 0.03f), new Vector3(1.1f, 2f, 0.08f), door, true, false);
        Prim(PrimitiveType.Cube, "Step", root, new Vector3(0f, 0.08f, d * 0.5f + 0.5f), new Vector3(1.8f, 0.16f, 0.9f),
             DungeonSceneSetup.GetOrCreateMaterial("VRock", Color.gray), true, false);
        // 煙突（屋根の面より少し高く突き出す）
        float surface = h + (w * 0.5f + 0.45f - w * 0.28f) * Mathf.Tan(RoofAngle * Mathf.Deg2Rad);
        float chimneyBottom = h - 0.2f, chimneyTop = surface + 0.7f;
        Prim(PrimitiveType.Cube, "Chimney", root, new Vector3(w * 0.28f, (chimneyBottom + chimneyTop) * 0.5f, -d * 0.2f),
             new Vector3(0.6f, chimneyTop - chimneyBottom, 0.6f),
             DungeonSceneSetup.GetOrCreateMaterial("VRock", Color.gray), true, false);

        // 窓（夜に明かりが灯る）
        foreach (float sx in new[] { -1f, 1f })
        {
            var win = Prim(PrimitiveType.Cube, "Window", root, new Vector3(sx * w * 0.3f, h * 0.58f, d * 0.5f + 0.03f),
                           new Vector3(0.9f, 0.9f, 0.08f), glass, false, false);
            var nl = win.AddComponent<NightLight>();
            nl.glow = win.GetComponent<Renderer>();
            nl.glowColor = new Color(1f, 0.8f, 0.45f);
            nl.glowIntensity = 2.2f;
            // 窓の外の淡い光
            var lg = new GameObject("WindowLight");
            lg.transform.SetParent(win.transform, false);
            lg.transform.localPosition = new Vector3(0f, 0f, 3f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.78f, 0.45f);
            l.range = 6f;
            l.intensity = 3f;
            l.enabled = false;
            nl.lampLight = l;
        }
    }

    static void Lamp(Transform parent, Vector3 pos, Material metal, Material glassMat)
    {
        var root = new GameObject("StreetLamp").transform;
        root.SetParent(parent, false);
        root.position = pos;
        occupied.Add(new Vector3(pos.x, pos.z, 0.5f));

        Prim(PrimitiveType.Cylinder, "Post", root, new Vector3(0f, 1.25f, 0f), new Vector3(0.14f, 1.25f, 0.14f), metal, true, true);
        Prim(PrimitiveType.Cube, "Cap", root, new Vector3(0f, 2.95f, 0f), new Vector3(0.55f, 0.1f, 0.55f), metal, true, false);
        var lantern = Prim(PrimitiveType.Cube, "Lantern", root, new Vector3(0f, 2.65f, 0f), new Vector3(0.38f, 0.5f, 0.38f), glassMat, false, false);

        var lg = new GameObject("LampLight");
        lg.transform.SetParent(root, false);
        lg.transform.localPosition = new Vector3(0f, 2.6f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.82f, 0.5f);
        l.range = 13f;
        l.intensity = 14f;
        l.enabled = false;

        var nl = lantern.AddComponent<NightLight>();
        nl.lampLight = l;
        nl.glow = lantern.GetComponent<Renderer>();
        nl.glowColor = new Color(1f, 0.85f, 0.5f);
        nl.dayColor = new Color(0.7f, 0.65f, 0.5f);
        nl.glowIntensity = 3f;
    }

    static void Well(Transform parent, Vector3 pos, Material stone, Material wood, Material water)
    {
        var root = new GameObject("Well").transform;
        root.SetParent(parent, false);
        root.position = pos;
        occupied.Add(new Vector3(pos.x, pos.z, 2.5f));

        Prim(PrimitiveType.Cylinder, "Ring", root, new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.45f, 1.8f), stone, true, true);
        Prim(PrimitiveType.Cylinder, "Water", root, new Vector3(0f, 0.7f, 0f), new Vector3(1.4f, 0.02f, 1.4f), water, true, false);
        Prim(PrimitiveType.Cube, "PostL", root, new Vector3(-0.85f, 1.4f, 0f), new Vector3(0.12f, 1.9f, 0.12f), wood, true, false);
        Prim(PrimitiveType.Cube, "PostR", root, new Vector3(0.85f, 1.4f, 0f), new Vector3(0.12f, 1.9f, 0.12f), wood, true, false);
        GableRoof(root, 2.35f, 1.7f, 1.6f, DungeonSceneSetup.GetOrCreateMaterial("VRoofBrown", Color.gray), null);
    }

    const float RoofAngle = 32f;

    /// <summary>
    /// 切妻屋根。棟は Z 方向。左右 2 枚の斜めの板と、gableMat があれば妻側（前後の三角部分）を階段状の壁で埋める。
    /// baseY は壁の上端の高さ、w は壁の幅、d は奥行き。
    /// </summary>
    static void GableRoof(Transform parent, float baseY, float w, float d, Material roofMat, Material gableMat)
    {
        float rad = RoofAngle * Mathf.Deg2Rad;
        float half = w * 0.5f + 0.45f;              // 棟から軒先までの水平距離（軒の出を含む）
        float rise = half * Mathf.Tan(rad);         // 棟の高さ
        float len = half / Mathf.Cos(rad);          // 板の長さ

        foreach (float side in new[] { -1f, 1f })
        {
            var slab = Prim(PrimitiveType.Cube, "Roof", parent,
                            new Vector3(side * half * 0.5f, baseY + rise * 0.5f, 0f),
                            new Vector3(len + 0.05f, 0.18f, d + 0.9f), roofMat, true, false);
            slab.transform.localRotation = Quaternion.Euler(0f, 0f, -side * RoofAngle);
        }

        if (gableMat == null) return;
        const int steps = 3;
        float wallRise = (w * 0.5f) * Mathf.Tan(rad);
        for (int k = 0; k < steps; k++)
        {
            float width = w * (1f - (k + 0.5f) / steps);
            float hgt = wallRise / steps;
            Prim(PrimitiveType.Cube, "Gable", parent, new Vector3(0f, baseY + (k + 0.5f) * hgt, 0f),
                 new Vector3(width, hgt, d), gableMat, true, false);
        }
    }

    static void Fence(Transform parent, Vector3 pos, float yaw, Material wood)
    {
        var root = new GameObject("Fence").transform;
        root.SetParent(parent, false);
        root.position = pos;
        root.rotation = Quaternion.Euler(0f, yaw, 0f);
        Prim(PrimitiveType.Cube, "Post", root, new Vector3(0f, 0.5f, 0f), new Vector3(0.12f, 1f, 0.12f), wood, true, true);
        Prim(PrimitiveType.Cube, "Rail1", root, new Vector3(0.8f, 0.8f, 0f), new Vector3(1.6f, 0.08f, 0.06f), wood, true, false);
        Prim(PrimitiveType.Cube, "Rail2", root, new Vector3(0.8f, 0.45f, 0f), new Vector3(1.6f, 0.08f, 0.06f), wood, true, false);
    }

    static void Tree(Transform parent, Vector3 pos, float scale, Material trunk, Material leaf)
    {
        var root = new GameObject("Tree").transform;
        root.SetParent(parent, false);
        root.position = pos;
        occupied.Add(new Vector3(pos.x, pos.z, 1.5f * scale));

        float h = 2.6f * scale;
        Prim(PrimitiveType.Cylinder, "Trunk", root, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.45f * scale, h * 0.5f, 0.45f * scale), trunk, true, true);
        Prim(PrimitiveType.Sphere, "Canopy", root, new Vector3(0f, h + 0.9f * scale, 0f), Vector3.one * 3.4f * scale, leaf, true, false);
        Prim(PrimitiveType.Sphere, "Canopy2", root, new Vector3(0.7f * scale, h + 0.2f * scale, 0.4f * scale), Vector3.one * 2.4f * scale, leaf, true, false);
    }
}
