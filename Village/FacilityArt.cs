using UnityEngine;

/// <summary>
/// 拠点の施設（装備屋・アイテム屋・鍛冶屋・鑑定屋・宿屋・素材屋）の見た目。
/// 元は「カウンターと店主だけ」の露店なので、その奥と左右に、壁・柱・屋根・棚・看板の灯り・のれんなどを足して、小さな店構えにする。
/// すべて、施設の向き（forward がお客さん側。店主はカウンターの奥 = -Z）に合わせた、ローカル座標で作る。
/// </summary>
public static class FacilityArt
{
    public enum Kind { Equipment, Item, Blacksmith, Appraiser, Inn, Material }

    const string Marker = "ShopFrame";

    static GameObject P(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Color color, bool collider = false, float glow = 0f)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var rend = go.GetComponent<Renderer>();
        if (glow <= 0f) { SharedMats.Apply(rend, color); return go; }   // 同じ色は、マテリアルを共有する
        var m = rend.material;
        m.color = color;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", color * glow);
        return go;
    }

    public static bool Has(Transform shop) => shop.Find(Marker) != null;

    /// <summary>shop の店構えを作る。すでに作ってあれば、何もしない。</summary>
    public static void Build(Transform shop, Kind kind)
    {
        if (Has(shop)) return;
        var f = new GameObject(Marker).transform;
        f.SetParent(shop, false);

        Color wall, roof, accent;
        float W;
        switch (kind)
        {
            case Kind.Equipment: wall = new Color(0.82f, 0.76f, 0.66f); roof = new Color(0.45f, 0.2f, 0.5f); accent = new Color(0.9f, 0.75f, 0.3f); W = 2.6f; break;
            case Kind.Item: wall = new Color(0.8f, 0.85f, 0.78f); roof = new Color(0.2f, 0.5f, 0.4f); accent = new Color(0.95f, 0.85f, 0.4f); W = 2.6f; break;
            case Kind.Blacksmith: wall = new Color(0.52f, 0.52f, 0.54f); roof = new Color(0.24f, 0.24f, 0.28f); accent = new Color(1f, 0.55f, 0.2f); W = 3.0f; break;
            case Kind.Appraiser: wall = new Color(0.32f, 0.3f, 0.52f); roof = new Color(0.18f, 0.16f, 0.36f); accent = new Color(0.8f, 0.65f, 1f); W = 2.6f; break;
            case Kind.Inn: wall = new Color(0.93f, 0.89f, 0.8f); roof = new Color(0.7f, 0.25f, 0.18f); accent = new Color(1f, 0.8f, 0.4f); W = 3.0f; break;
            default: wall = new Color(0.7f, 0.62f, 0.48f); roof = new Color(0.3f, 0.45f, 0.35f); accent = new Color(0.5f, 0.9f, 0.6f); W = 2.6f; break;
        }
        var timber = new Color(0.30f, 0.19f, 0.10f);
        var wood = new Color(0.45f, 0.30f, 0.16f);
        float zBack = -2.9f, zFront = 1.9f, eave = 3.7f;
        float zc = (zBack + zFront) * 0.5f;

        // 床板と、土台
        P(PrimitiveType.Cube, "Floor", f, new Vector3(0f, 0.03f, zc), new Vector3(W * 2f + 0.8f, 0.06f, zFront - zBack + 0.6f), wood * 0.9f);
        P(PrimitiveType.Cube, "Step", f, new Vector3(0f, 0.05f, zFront + 0.45f), new Vector3(W * 2f + 0.8f, 0.1f, 0.5f), new Color(0.5f, 0.48f, 0.45f));

        // 奥と左右の壁（見た目だけ。道をふさがないよう、当たり判定は無い）
        P(PrimitiveType.Cube, "BackWall", f, new Vector3(0f, eave * 0.5f, zBack), new Vector3(W * 2f, eave, 0.25f), wall);
        float sideLen = 0.9f - zBack;
        foreach (float sx in new[] { -1f, 1f })
        {
            P(PrimitiveType.Cube, "SideWall", f, new Vector3(sx * W, eave * 0.5f, zBack + sideLen * 0.5f), new Vector3(0.25f, eave, sideLen), wall);
            P(PrimitiveType.Cube, "SideBase", f, new Vector3(sx * W, 0.35f, zBack + sideLen * 0.5f), new Vector3(0.32f, 0.7f, sideLen + 0.1f), new Color(0.48f, 0.46f, 0.43f));
            // 前の柱
            P(PrimitiveType.Cylinder, "FrontPost", f, new Vector3(sx * W, eave * 0.5f, zFront), new Vector3(0.28f, eave * 0.5f, 0.28f), timber);
        }
        P(PrimitiveType.Cube, "FrontBeam", f, new Vector3(0f, eave - 0.1f, zFront), new Vector3(W * 2f + 0.4f, 0.28f, 0.28f), timber);
        P(PrimitiveType.Cube, "SideBeamL", f, new Vector3(-W, eave - 0.1f, zc), new Vector3(0.28f, 0.28f, zFront - zBack), timber);
        P(PrimitiveType.Cube, "SideBeamR", f, new Vector3(W, eave - 0.1f, zc), new Vector3(0.28f, 0.28f, zFront - zBack), timber);
        // 奥の壁の木組み
        for (float x = -W + 0.7f; x < W; x += 1.4f)
            P(PrimitiveType.Cube, "BackPost", f, new Vector3(x, eave * 0.5f, zBack + 0.14f), new Vector3(0.14f, eave, 0.06f), timber);
        P(PrimitiveType.Cube, "BackBeam", f, new Vector3(0f, 2.55f, zBack + 0.14f), new Vector3(W * 2f, 0.12f, 0.06f), timber);

        // 屋根（前後へ傾く切妻）と、棟、縁
        float ang = 24f, half = (zFront - zBack + 1f) * 0.5f;
        float rise = half * Mathf.Tan(ang * Mathf.Deg2Rad), len = half / Mathf.Cos(ang * Mathf.Deg2Rad);
        foreach (float sz in new[] { -1f, 1f })
        {
            var slab = P(PrimitiveType.Cube, "Roof", f, new Vector3(0f, eave + rise * 0.5f, zc + sz * half * 0.5f), new Vector3(W * 2f + 1.2f, 0.2f, len + 0.05f), roof);
            slab.transform.localRotation = Quaternion.Euler(sz * ang, 0f, 0f);
        }
        P(PrimitiveType.Cube, "Ridge", f, new Vector3(0f, eave + rise + 0.06f, zc), new Vector3(W * 2f + 1.3f, 0.2f, 0.3f), roof * 0.7f);
        for (int k = 0; k < 3; k++)
            foreach (float sx in new[] { -1f, 1f })
            {
                float hz = (zFront - zBack) * 0.5f * (1f - (k + 0.5f) / 3f);
                P(PrimitiveType.Cube, "Gable", f, new Vector3(sx * W, eave + (k + 0.5f) * rise / 3f, zc), new Vector3(0.25f, rise / 3f, hz * 2f), wall);
            }

        // 前の軒先に、のれん（色と模様がかわるひらひら）
        int flaps = Mathf.RoundToInt(W * 2f / 0.55f);
        for (int i = 0; i < flaps; i++)
        {
            float x = -W + 0.28f + i * (W * 2f - 0.56f) / Mathf.Max(1, flaps - 1);
            P(PrimitiveType.Cube, "Valance", f, new Vector3(x, eave - 0.55f, zFront + 0.18f), new Vector3(0.5f, 0.55f, 0.04f), i % 2 == 0 ? roof * 1.15f : accent);
        }

        // 奥の壁の棚と、商品
        for (int s = 0; s < 2; s++)
        {
            float y = 1.35f + s * 0.85f;
            P(PrimitiveType.Cube, "Shelf", f, new Vector3(0f, y, zBack + 0.38f), new Vector3(W * 2f - 0.6f, 0.07f, 0.4f), wood);
            for (float x = -W + 0.7f; x < W - 0.5f; x += 0.7f)
                Goods(f, kind, new Vector3(x + (s == 0 ? 0f : 0.35f), y + 0.04f, zBack + 0.38f), accent, s);
        }

        // 軒先のランタン（夜はほのかに光る）
        foreach (float sx in new[] { -1f, 1f })
        {
            P(PrimitiveType.Cylinder, "LanternChain", f, new Vector3(sx * (W - 0.5f), eave - 0.5f, zFront + 0.35f), new Vector3(0.03f, 0.35f, 0.03f), new Color(0.15f, 0.14f, 0.14f));
            P(PrimitiveType.Sphere, "Lantern", f, new Vector3(sx * (W - 0.5f), eave - 1.0f, zFront + 0.35f), new Vector3(0.32f, 0.42f, 0.32f), new Color(1f, 0.82f, 0.5f), false, 1.6f);
        }

        // 左右の飾り: 樽・木箱・のぼり旗
        P(PrimitiveType.Cylinder, "Barrel", f, new Vector3(-W - 0.8f, 0.45f, 0.2f), new Vector3(0.7f, 0.45f, 0.7f), new Color(0.45f, 0.3f, 0.15f));
        P(PrimitiveType.Cube, "Crate", f, new Vector3(W + 0.8f, 0.4f, 0.2f), new Vector3(0.8f, 0.8f, 0.8f), new Color(0.55f, 0.4f, 0.22f));
        P(PrimitiveType.Cube, "Crate2", f, new Vector3(W + 0.8f, 1.0f, 0.2f), new Vector3(0.55f, 0.4f, 0.55f), new Color(0.5f, 0.36f, 0.2f));
        P(PrimitiveType.Cylinder, "FlagPole", f, new Vector3(-W - 0.5f, 1.9f, zFront), new Vector3(0.07f, 1.9f, 0.07f), wood);
        P(PrimitiveType.Cube, "Flag", f, new Vector3(-W - 0.5f, 2.7f, zFront + 0.35f), new Vector3(0.04f, 1.1f, 0.65f), accent);
        P(PrimitiveType.Cube, "FlagEmblem", f, new Vector3(-W - 0.5f, 2.9f, zFront + 0.36f), new Vector3(0.05f, 0.3f, 0.3f), roof);

        // 店の前の敷物
        P(PrimitiveType.Cube, "Rug", f, new Vector3(0f, 0.08f, 1.5f), new Vector3(W * 1.1f, 0.03f, 1.1f), roof * 1.2f);
        P(PrimitiveType.Cube, "RugTrim", f, new Vector3(0f, 0.09f, 1.5f), new Vector3(W * 1.1f - 0.25f, 0.03f, 0.85f), accent);

        // 種類ごとの目印
        if (kind == Kind.Appraiser)
            for (int i = 0; i < 14; i++)
                P(PrimitiveType.Sphere, "Star", f, new Vector3(Random.Range(-W + 0.3f, W - 0.3f), Random.Range(3.0f, 3.5f), zBack + 0.2f + Random.Range(0f, 0.2f)), Vector3.one * Random.Range(0.06f, 0.12f), new Color(0.9f, 0.85f, 1f), false, 2f);
        if (kind == Kind.Inn)
        {
            P(PrimitiveType.Cube, "BedSign", f, new Vector3(W + 0.1f, 2.7f, 0.6f), new Vector3(0.06f, 0.7f, 0.9f), wood);
            P(PrimitiveType.Cube, "BedSignIcon", f, new Vector3(W + 0.14f, 2.7f, 0.6f), new Vector3(0.05f, 0.25f, 0.55f), new Color(0.9f, 0.9f, 0.95f));
        }
    }

    /// <summary>棚の上の商品（店の種類ごと）。</summary>
    static void Goods(Transform f, Kind kind, Vector3 p, Color accent, int row)
    {
        int v = Mathf.Abs((int)(p.x * 10f) + row * 3) % 3;
        switch (kind)
        {
            case Kind.Equipment:
                if (v == 0) { P(PrimitiveType.Cube, "Sword", f, p + Vector3.up * 0.3f, new Vector3(0.07f, 0.6f, 0.03f), new Color(0.8f, 0.85f, 0.9f)); P(PrimitiveType.Cube, "Guard", f, p + Vector3.up * 0.05f, new Vector3(0.22f, 0.04f, 0.05f), accent); }
                else if (v == 1) P(PrimitiveType.Cylinder, "Shield", f, p + Vector3.up * 0.28f, new Vector3(0.5f, 0.02f, 0.5f), new Color(0.55f, 0.2f, 0.2f)).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                else P(PrimitiveType.Sphere, "Helm", f, p + Vector3.up * 0.2f, new Vector3(0.34f, 0.34f, 0.34f), new Color(0.6f, 0.62f, 0.68f));
                break;
            case Kind.Item:
            {
                Color[] cs = { new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.85f, 0.35f), new Color(0.3f, 0.5f, 0.95f), new Color(0.95f, 0.8f, 0.2f) };
                P(PrimitiveType.Sphere, "Potion", f, p + Vector3.up * 0.14f, new Vector3(0.22f, 0.24f, 0.22f), cs[v + row], false, 0.6f);
                P(PrimitiveType.Cylinder, "Neck", f, p + Vector3.up * 0.32f, new Vector3(0.07f, 0.07f, 0.07f), new Color(0.8f, 0.8f, 0.85f));
                break;
            }
            case Kind.Blacksmith:
                if (v == 0) P(PrimitiveType.Cube, "Ingot", f, p + Vector3.up * 0.06f, new Vector3(0.4f, 0.12f, 0.2f), new Color(0.6f, 0.62f, 0.66f));
                else if (v == 1) { P(PrimitiveType.Cube, "Haft", f, p + Vector3.up * 0.3f, new Vector3(0.05f, 0.6f, 0.05f), new Color(0.4f, 0.27f, 0.14f)); P(PrimitiveType.Cube, "AxeHead", f, p + Vector3.up * 0.52f + Vector3.right * 0.1f, new Vector3(0.22f, 0.2f, 0.04f), new Color(0.6f, 0.62f, 0.66f)); }
                else P(PrimitiveType.Cube, "Ore", f, p + Vector3.up * 0.1f, new Vector3(0.26f, 0.2f, 0.24f), new Color(0.3f, 0.25f, 0.22f));
                break;
            case Kind.Appraiser:
                if (v == 0) P(PrimitiveType.Sphere, "Orb", f, p + Vector3.up * 0.16f, Vector3.one * 0.3f, new Color(0.7f, 0.5f, 1f), false, 1.5f);
                else if (v == 1) P(PrimitiveType.Cube, "Tome", f, p + Vector3.up * 0.18f, new Vector3(0.3f, 0.34f, 0.12f), new Color(0.4f, 0.2f, 0.5f));
                else P(PrimitiveType.Cube, "Gem", f, p + Vector3.up * 0.15f, Vector3.one * 0.2f, new Color(0.3f, 0.9f, 0.9f), false, 1.2f).transform.localRotation = Quaternion.Euler(30f, 20f, 15f);
                break;
            case Kind.Inn:
                if (v == 0) P(PrimitiveType.Cylinder, "Keg", f, p + Vector3.up * 0.2f, new Vector3(0.38f, 0.2f, 0.38f), new Color(0.45f, 0.3f, 0.15f));
                else if (v == 1) P(PrimitiveType.Cylinder, "Mug", f, p + Vector3.up * 0.1f, new Vector3(0.14f, 0.1f, 0.14f), new Color(0.9f, 0.85f, 0.7f));
                else P(PrimitiveType.Sphere, "Loaf", f, p + Vector3.up * 0.1f, new Vector3(0.3f, 0.18f, 0.2f), new Color(0.8f, 0.6f, 0.3f));
                break;
            default:
            {
                Color[] cs = { new Color(0.4f, 0.9f, 0.5f), new Color(0.4f, 0.6f, 1f), new Color(0.9f, 0.5f, 0.3f) };
                P(PrimitiveType.Cube, "Crate", f, p + Vector3.up * 0.12f, new Vector3(0.36f, 0.24f, 0.3f), new Color(0.5f, 0.37f, 0.2f));
                P(PrimitiveType.Cube, "Mat", f, p + Vector3.up * 0.28f, new Vector3(0.18f, 0.16f, 0.18f), cs[v], false, 0.8f).transform.localRotation = Quaternion.Euler(20f, 30f, 10f);
                break;
            }
        }
    }
}
