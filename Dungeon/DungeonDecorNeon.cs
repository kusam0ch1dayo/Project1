using UnityEngine;

/// <summary>ネオンの内装の小物。壁を走るネオンライン・アーケード機体・バーカウンター・ビリヤード台・ネオン看板・浮かぶゲーム画面・スピーカー・ミラーボール。当たり判定は無い。</summary>
public partial class DungeonDecor
{
    static readonly Color NeonDark = new Color(0.07f, 0.06f, 0.12f);
    static readonly Color NeonBody = new Color(0.13f, 0.11f, 0.22f);

    Color NeonC() => NeonArt.Pick(rng);

    GameObject Glowy(PrimitiveType type, Vector3 pos, Vector3 scale, Color c, float k = 2.4f, Quaternion? rot = null)
        => Prim(type, pos, scale, c * 0.5f, 0.3f, rot, c * k);

    /// <summary>壁一面を走る、ネオンのライン（横線・縦線・折れ線）。</summary>
    void NeonCladding(Edge e)
    {
        var rot = Quaternion.LookRotation(e.inward);
        float w = cs - 0.3f;
        Color c = NeonC();
        Vector3 b = e.wall + e.inward * 0.05f;
        if (Chance(0.8f))
        {
            float y = R(0.45f, 3.4f);
            Glowy(PrimitiveType.Cube, b + Vector3.up * y, new Vector3(w, 0.07f, 0.05f), c, 2.6f, rot);
            if (Chance(0.5f)) Glowy(PrimitiveType.Cube, b + Vector3.up * (y + 0.28f), new Vector3(w * 0.7f, 0.04f, 0.04f), NeonC(), 2.2f, rot);
        }
        if (Chance(0.45f))
        {
            // 縦線から、ななめに折れる
            Vector3 p = b + e.side * R(-1.2f, 1.2f);
            Glowy(PrimitiveType.Cube, p + Vector3.up * 1.2f, new Vector3(0.06f, 2.3f, 0.05f), c, 2.6f, rot);
            Glowy(PrimitiveType.Cube, p + Vector3.up * 2.55f + e.side * 0.3f, new Vector3(0.06f, 0.7f, 0.05f), c, 2.6f, rot * Quaternion.Euler(0f, 0f, 45f));
        }
    }

    void NeonArcade(Edge e)
    {
        Color c = NeonC();
        int count = Chance(0.5f) ? 2 : 1;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = e.wall + e.inward * 0.6f + e.side * (R(-0.8f, 0.8f) + (i == 1 ? 1.0f : 0f));
            var rot = Quaternion.LookRotation(e.inward);
            Prim(PrimitiveType.Cube, p + Vector3.up * 0.9f, new Vector3(0.82f, 1.8f, 0.9f), NeonBody, 0.4f, rot);
            Glowy(PrimitiveType.Cube, p + Vector3.up * 1.72f, new Vector3(0.86f, 0.3f, 0.94f), c, 2.2f, rot);
            Prim(PrimitiveType.Cube, p + Vector3.up * 0.95f + e.inward * 0.38f, new Vector3(0.74f, 0.08f, 0.5f), NeonDark, 0.3f, rot);
            Glowy(PrimitiveType.Sphere, p + Vector3.up * 1.03f + e.inward * 0.45f + e.side * -0.18f, Vector3.one * 0.09f, NeonArt.Pink, 3f);
            Glowy(PrimitiveType.Sphere, p + Vector3.up * 1.03f + e.inward * 0.45f + e.side * 0.1f, Vector3.one * 0.07f, NeonArt.Yellow, 3f);
            Glowy(PrimitiveType.Sphere, p + Vector3.up * 1.03f + e.inward * 0.45f + e.side * 0.26f, Vector3.one * 0.07f, NeonArt.Cyan, 3f);
            Glowy(PrimitiveType.Cube, p + Vector3.up * 0.9f + e.side * 0.415f, new Vector3(0.02f, 1.7f, 0.9f), c, 1.8f, rot);
            Glowy(PrimitiveType.Cube, p + Vector3.up * 0.9f - e.side * 0.415f, new Vector3(0.02f, 1.7f, 0.9f), c, 1.8f, rot);
            NeonArt.Screen(root, p + Vector3.up * 1.38f + e.inward * 0.47f, e.inward, 0.62f, rng.Next(NeonArt.Kinds), NeonDark);
            Vector3 lp = p + Vector3.up * 1.5f + e.inward * 1.3f;
            if (i == 0 && LightOk(lp)) AddLight(null, lp, c, 2.2f, 6f);
        }
    }

    void NeonBar(Edge e)
    {
        Color c = NeonC();
        Vector3 p = e.wall + e.inward * 1.25f + e.side * R(-0.6f, 0.6f);
        var rot = Quaternion.LookRotation(e.side);
        Prim(PrimitiveType.Cube, p + Vector3.up * 0.52f, new Vector3(0.7f, 1.04f, 2.6f), NeonBody, 0.4f, rot);
        Prim(PrimitiveType.Cube, p + Vector3.up * 1.07f, new Vector3(0.9f, 0.08f, 2.8f), new Color(0.3f, 0.2f, 0.4f), 0.8f, rot);
        Glowy(PrimitiveType.Cube, p + e.inward * 0.36f + Vector3.up * 0.7f, new Vector3(0.03f, 0.05f, 2.5f), c, 2.8f, rot);
        Glowy(PrimitiveType.Cube, p + e.inward * 0.36f + Vector3.up * 0.2f, new Vector3(0.03f, 0.05f, 2.5f), NeonC(), 2.8f, rot);
        // 酒瓶
        for (int i = 0; i < 6; i++)
        {
            Color bc = NeonC();
            Glowy(PrimitiveType.Cylinder, p + e.side * (-1.1f + i * 0.44f + R(-0.08f, 0.08f)) + Vector3.up * 1.27f, new Vector3(0.12f, R(0.12f, 0.2f), 0.12f), bc, 1.6f);
        }
        // 背の高いスツール
        for (int i = 0; i < 3; i++)
        {
            Vector3 sp = p + e.inward * 0.95f + e.side * (-0.9f + i * 0.9f);
            Prim(PrimitiveType.Cylinder, sp + Vector3.up * 0.34f, new Vector3(0.07f, 0.34f, 0.07f), NeonDark, 0.6f);
            Glowy(PrimitiveType.Cylinder, sp + Vector3.up * 0.7f, new Vector3(0.42f, 0.05f, 0.42f), NeonC(), 1.4f);
        }
        Vector3 lp = p + Vector3.up * 2.3f;
        if (LightOk(lp)) AddLight(null, lp, c, 3f, 8f);
    }

    void NeonPool(Edge e)
    {
        Vector3 p = e.wall + e.inward * 1.9f + e.side * R(-0.4f, 0.4f);
        var rot = Quaternion.LookRotation(e.side);
        Prim(PrimitiveType.Cube, p + Vector3.up * 0.78f, new Vector3(1.4f, 0.14f, 2.6f), new Color(0.25f, 0.14f, 0.1f), 0.5f, rot);
        Prim(PrimitiveType.Cube, p + Vector3.up * 0.87f, new Vector3(1.14f, 0.05f, 2.34f), new Color(0.03f, 0.35f, 0.3f), 0.2f, rot, new Color(0.01f, 0.2f, 0.25f), true);
        foreach (float sx in new[] { -0.6f, 0.6f })
            foreach (float sz in new[] { -1.15f, 1.15f })
                Prim(PrimitiveType.Cylinder, p + e.side * sz + Vector3.Cross(Vector3.up, e.side).normalized * sx + Vector3.up * 0.36f, new Vector3(0.14f, 0.36f, 0.14f), NeonDark, 0.4f);
        for (int i = 0; i < 6; i++)
            Glowy(PrimitiveType.Sphere, p + e.side * R(-0.9f, 0.9f) + Vector3.Cross(Vector3.up, e.side).normalized * R(-0.4f, 0.4f) + Vector3.up * 0.93f, Vector3.one * 0.1f, NeonC(), 1.2f);
        var cue = Prim(PrimitiveType.Cylinder, p + Vector3.up * 0.95f + e.inward * 0.25f, new Vector3(0.025f, 0.7f, 0.025f), new Color(0.85f, 0.7f, 0.45f), 0.5f);
        cue.transform.rotation = Quaternion.LookRotation(e.side) * Quaternion.Euler(80f, 20f, 0f);
        Color c = NeonC();
        Glowy(PrimitiveType.Cube, p + Vector3.up * 2.3f, new Vector3(0.1f, 0.1f, 2.2f), c, 3f, rot);
        Vector3 lp = p + Vector3.up * 2.1f;
        if (LightOk(lp)) AddLight(null, lp, c, 3.5f, 8f);
    }

    void NeonSign(Edge e)
    {
        Color c = NeonC();
        var rot = Quaternion.LookRotation(e.inward);
        Vector3 p = e.wall + e.inward * 0.06f + Vector3.up * R(2.0f, 2.8f) + e.side * R(-0.8f, 0.8f);
        float w = R(1.0f, 1.7f), hh = R(0.5f, 0.8f);
        Prim(PrimitiveType.Cube, p, new Vector3(w, hh, 0.06f), NeonDark, 0.3f, rot);
        // 枠
        Glowy(PrimitiveType.Cube, p + Vector3.up * (hh * 0.5f), new Vector3(w, 0.05f, 0.08f), c, 3f, rot);
        Glowy(PrimitiveType.Cube, p - Vector3.up * (hh * 0.5f), new Vector3(w, 0.05f, 0.08f), c, 3f, rot);
        Glowy(PrimitiveType.Cube, p + e.side * (w * 0.5f), new Vector3(0.05f, hh, 0.08f), c, 3f, rot);
        Glowy(PrimitiveType.Cube, p - e.side * (w * 0.5f), new Vector3(0.05f, hh, 0.08f), c, 3f, rot);
        // 文字のようなドット
        Color c2 = NeonC();
        int n = rng.Next(3, 6);
        for (int i = 0; i < n; i++)
        {
            float x = (i - (n - 1) * 0.5f) * (w * 0.8f / n);
            float bh = hh * R(0.3f, 0.65f);
            Glowy(PrimitiveType.Cube, p + e.side * x + e.inward * 0.06f + Vector3.up * R(-0.05f, 0.05f), new Vector3(0.07f, bh, 0.03f), c2, 3f, rot);
            if (Chance(0.5f)) Glowy(PrimitiveType.Cube, p + e.side * (x + 0.06f) + e.inward * 0.06f + Vector3.up * (bh * 0.25f), new Vector3(0.12f, 0.06f, 0.03f), c2, 3f, rot);
        }
        Vector3 lp = p + e.inward * 0.8f;
        if (LightOk(lp)) AddLight(null, lp, c, 3f, 7f);
    }

    void NeonWallScreen(Edge e)
    {
        Vector3 p = e.wall + e.inward * 0.1f + Vector3.up * R(1.7f, 2.7f) + e.side * R(-1f, 1f);
        float w = R(1.2f, 2.2f);
        NeonArt.Screen(root, p, e.inward, w, rng.Next(NeonArt.Kinds), NeonC());
    }

    void NeonSpeaker(Edge e)
    {
        Vector3 p = e.wall + e.inward * 0.45f + e.side * R(-1.2f, 1.2f);
        var rot = Quaternion.LookRotation(e.inward);
        int n = rng.Next(2, 4);
        for (int i = 0; i < n; i++)
        {
            Vector3 b = p + Vector3.up * (0.45f + i * 0.9f);
            Prim(PrimitiveType.Cube, b, new Vector3(0.8f, 0.86f, 0.6f), NeonDark, 0.3f, rot);
            Glowy(PrimitiveType.Cylinder, b + e.inward * 0.31f, new Vector3(0.52f, 0.02f, 0.52f), NeonC(), 1.2f, rot * Quaternion.Euler(90f, 0f, 0f));
            Prim(PrimitiveType.Cylinder, b + e.inward * 0.32f, new Vector3(0.22f, 0.02f, 0.22f), new Color(0.02f, 0.02f, 0.03f), 0.2f, rot * Quaternion.Euler(90f, 0f, 0f));
        }
    }

    void NeonVending(Edge e)
    {
        Color c = NeonC();
        Vector3 p = e.wall + e.inward * 0.5f + e.side * R(-1.1f, 1.1f);
        var rot = Quaternion.LookRotation(e.inward);
        Prim(PrimitiveType.Cube, p + Vector3.up * 0.95f, new Vector3(0.95f, 1.9f, 0.8f), NeonBody, 0.4f, rot);
        Glowy(PrimitiveType.Cube, p + Vector3.up * 1.3f + e.inward * 0.405f, new Vector3(0.7f, 0.95f, 0.03f), c, 2f, rot);
        for (int i = 0; i < 6; i++)
            Glowy(PrimitiveType.Cube, p + Vector3.up * (1.6f - (i / 2) * 0.28f) + e.side * (-0.17f + (i % 2) * 0.34f) + e.inward * 0.43f, new Vector3(0.22f, 0.18f, 0.02f), NeonC(), 2.6f, rot);
        Prim(PrimitiveType.Cube, p + Vector3.up * 0.3f + e.inward * 0.41f, new Vector3(0.5f, 0.2f, 0.02f), Color.black, 0.2f, rot);
        Vector3 lp = p + Vector3.up * 1.4f + e.inward * 1.2f;
        if (LightOk(lp)) AddLight(null, lp, c, 2.4f, 6f);
    }

    void NeonWall(Edge e, double r)
    {
        if (r < 0.06) NeonArcade(e);
        else if (r < 0.095) NeonBar(e);
        else if (r < 0.12) NeonPool(e);
        else if (r < 0.17) NeonSign(e);
        else if (r < 0.25) NeonWallScreen(e);
        else if (r < 0.29) NeonSpeaker(e);
        else if (r < 0.32) NeonVending(e);
        else if (r < 0.34) Barrel(e.wall + e.inward * 0.55f + e.side * R(-1.2f, 1.2f), new Color(0.15f, 0.12f, 0.25f));
    }

    void NeonCorner(Vector3 pos, Vector3 corner, Vector3 diag, bool boss)
    {
        Color c = NeonC();
        if (boss) { Brazier(corner + diag * 0.9f, c); return; }
        // 床から天井へ立つ、ネオン管の柱
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.15f, new Vector3(0.5f, 0.15f, 0.5f), NeonDark, 0.4f);
        Glowy(PrimitiveType.Cylinder, pos + Vector3.up * (h * 0.5f), new Vector3(0.16f, h * 0.5f, 0.16f), c, 2.4f);
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * (h - 0.12f), new Vector3(0.5f, 0.12f, 0.5f), NeonDark, 0.4f);
        Vector3 lp = pos + Vector3.up * 1.6f + diag * 0.8f;
        if (LightOk(lp)) AddLight(null, lp, c, 2.4f, 7f);
    }

    void NeonArch(Vector3 cell, Vector3 axis)
    {
        float half = cs * 0.5f - 0.3f;
        Color c = NeonC();
        Glowy(PrimitiveType.Cylinder, cell + axis * half + Vector3.up * (h * 0.5f), new Vector3(0.14f, h * 0.5f, 0.14f), c, 2.6f);
        Glowy(PrimitiveType.Cylinder, cell - axis * half + Vector3.up * (h * 0.5f), new Vector3(0.14f, h * 0.5f, 0.14f), c, 2.6f);
        Glowy(PrimitiveType.Cube, cell + Vector3.up * (h - 0.35f), new Vector3(0.14f, 0.14f, cs - 0.6f), c, 2.6f, Quaternion.LookRotation(axis));
        Vector3 lp = cell + Vector3.up * (h - 0.8f);
        if (LightOk(lp)) AddLight(null, lp, c, 3f, 9f);
    }

    void NeonCeiling(Vector3 pos, bool boss)
    {
        double r = rng.NextDouble();
        Color c = NeonC();
        if (r < 0.32)
        {
            // 吊り下げのネオン管（ななめ）
            Prim(PrimitiveType.Cylinder, pos + Vector3.down * 0.3f, new Vector3(0.02f, 0.3f, 0.02f), NeonDark, 0.4f);
            Glowy(PrimitiveType.Cylinder, pos + Vector3.down * 0.65f, new Vector3(0.09f, R(0.9f, 1.6f), 0.09f), c, 2.8f, Quaternion.Euler(90f, R(0f, 180f), 0f));
            Vector3 lp = pos + Vector3.down * 0.9f;
            if (LightOk(lp)) AddLight(null, lp, c, 3.2f, 9f);
        }
        else if (r < 0.52)
        {
            // 光の輪
            float rad = R(0.5f, 0.9f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                Glowy(PrimitiveType.Cube, pos + Vector3.down * 0.5f + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad), new Vector3(rad * 0.65f, 0.05f, 0.06f), c, 2.8f, Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f));
            }
            Vector3 lp = pos + Vector3.down * 0.7f;
            if (LightOk(lp)) AddLight(null, lp, c, 3f, 8f);
        }
        else if (r < 0.7)
        {
            // 天井から吊るされた、ゲーム画面
            Vector3 face = Quaternion.Euler(0f, R(0f, 360f), 0f) * Vector3.forward;
            NeonArt.Screen(root, pos + Vector3.down * 1.1f, (face + Vector3.down * 0.5f).normalized, R(1.2f, 2f), rng.Next(NeonArt.Kinds), c, 0.1f, R(-8f, 8f));
        }
        else
        {
            // ミラーボール
            Prim(PrimitiveType.Cylinder, pos + Vector3.down * 0.3f, new Vector3(0.015f, 0.3f, 0.015f), NeonDark, 0.4f);
            Prim(PrimitiveType.Sphere, pos + Vector3.down * 0.85f, Vector3.one * 0.55f, new Color(0.7f, 0.7f, 0.8f), 0.95f, null, c * 0.7f, false);
            Vector3 lp = pos + Vector3.down * 1.1f;
            if (LightOk(lp)) AddLight(null, lp, c, 3.4f, 9f);
        }
    }

    void NeonFloor(Vector3 pos)
    {
        double r = rng.NextDouble();
        Color c = NeonC();
        if (r < 0.4)
        {
            // 床のネオンライン
            Glowy(PrimitiveType.Cube, pos + Vector3.up * 0.02f, new Vector3(R(1.2f, 2.8f), 0.02f, 0.07f), c, 2.4f, Quaternion.Euler(0f, R(0f, 180f), 0f));
        }
        else if (r < 0.7)
        {
            // ドットのダンスフロア
            for (int ix = -1; ix <= 1; ix++)
                for (int iz = -1; iz <= 1; iz++)
                    if (Chance(0.75f)) Glowy(PrimitiveType.Cube, pos + new Vector3(ix * 0.5f, 0.015f, iz * 0.5f), new Vector3(0.44f, 0.02f, 0.44f), NeonC(), 1.5f);
        }
        else
        {
            // ケーブルの束
            for (int i = 0; i < 3; i++)
                Prim(PrimitiveType.Cylinder, pos + new Vector3(R(-0.2f, 0.2f), 0.04f, R(-0.2f, 0.2f)), new Vector3(0.05f, R(0.5f, 1f), 0.05f), NeonDark, 0.3f, Quaternion.Euler(90f, R(0f, 180f), 0f));
        }
    }
}
