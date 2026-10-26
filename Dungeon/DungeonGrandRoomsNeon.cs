using UnityEngine;

/// <summary>ネオンの大広間（ゲームセンター兼バー）。光る床のダンスフロア・回るミラーボールとレーザー・壁ぎわのアーケード機体の列・バーカウンター・ビリヤード台・宙に浮かぶゲーム画面。</summary>
public partial class GrandRoomBuilder
{
    static Color NeonPick(System.Random r) => NeonArt.Pick(r);

    void NeonHall()
    {
        var dark = Lit(new Color(0.07f, 0.06f, 0.12f), 0.4f);
        var body = Lit(new Color(0.13f, 0.11f, 0.22f), 0.4f);
        float minSide = Mathf.Min(Width, Depth);

        // 床: 色とりどりの光るタイルのダンスフロア
        int tiles = 8;
        float ts = minSide * 0.5f / tiles;
        for (int ix = 0; ix < tiles; ix++)
            for (int iz = 0; iz < tiles; iz++)
            {
                Color c = NeonPick(rng);
                float x = cx + (ix - (tiles - 1) * 0.5f) * ts, z = cz + (iz - (tiles - 1) * 0.5f) * ts;
                Prim(PrimitiveType.Cube, new Vector3(x, 0.025f, z), new Vector3(ts * 0.92f, 0.03f, ts * 0.92f), Lit(c, 0.5f, 1.6f));
            }

        // 天井: 回るミラーボールと、四方へ伸びるレーザー
        var ballPos = new Vector3(cx, H - 2.2f, cz);
        var ball = Group("MirrorBall", ballPos);
        ball.gameObject.AddComponent<TunnelSpin>().speed = new Vector3(0f, 40f, 0f);
        Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * Mathf.Clamp(minSide * 0.1f, 1.6f, 2.8f), Lit(new Color(0.75f, 0.75f, 0.85f), 0.95f, 0.4f), null, false, ball);
        Prim(PrimitiveType.Cylinder, new Vector3(cx, H - 1.0f, cz), new Vector3(0.05f, 1.1f, 0.05f), dark);
        Color[] beams = { NeonArt.Pink, NeonArt.Cyan, NeonArt.Yellow, NeonArt.Green };
        for (int i = 0; i < 6; i++)
        {
            var arm = Group("Laser", ballPos);
            arm.localRotation = Quaternion.Euler(0f, i * 60f, 0f);
            arm.gameObject.AddComponent<TunnelSpin>().speed = new Vector3(0f, (i % 2 == 0 ? 1f : -1f) * 35f, 0f);
            var beam = Prim(PrimitiveType.Cylinder, new Vector3(0f, -H * 0.2f, minSide * 0.28f), new Vector3(0.06f, minSide * 0.32f, 0.06f), Lit(beams[i % 4], 0.3f, 3f), Quaternion.Euler(70f, 0f, 0f), false, arm);
            beam.name = "LaserBeam";
        }
        Lamp(new Vector3(cx, H - 3f, cz), NeonArt.Violet, 6f, Mathf.Max(Width, Depth) * 1.2f);

        // 壁ぎわ: アーケード機体の列（出入口の前は空ける）
        int cabinets = 0;
        for (int x = room.xMin; x < room.xMax; x++)
            for (int y = room.yMin; y < room.yMax; y++)
            {
                var cell = new Vector2Int(x, y);
                foreach (var d in new[] { new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(0, 1) })
                {
                    if (open(cell + d) || cabinets >= 16) continue;
                    Vector3 inward = new Vector3(-d.x, 0f, -d.y), side = new Vector3(-d.y, 0f, d.x);
                    Vector3 wall = new Vector3(x * cs, 0f, y * cs) + new Vector3(d.x, 0f, d.y) * (cs * 0.5f);
                    if (!Chance(0.7f)) continue;
                    Color c = NeonPick(rng);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Vector3 p = wall + inward * 0.6f + side * (k * 0.75f);
                        var rot = Quaternion.LookRotation(inward);
                        Prim(PrimitiveType.Cube, p + Vector3.up * 0.95f, new Vector3(0.84f, 1.9f, 0.92f), body, rot, true);
                        Prim(PrimitiveType.Cube, p + Vector3.up * 1.8f, new Vector3(0.88f, 0.32f, 0.96f), Lit(c, 0.3f, 2.4f), rot);
                        NeonArt.Screen(parent, p + Vector3.up * 1.45f + inward * 0.48f, inward, 0.64f, rng.Next(NeonArt.Kinds), c);
                    }
                    Lamp(wall + inward * 1.6f + Vector3.up * 1.8f, c, 2.4f, 7f);
                    cabinets++;
                }
            }

        // 四隅: バーカウンター（当たり判定あり）か、ビリヤード台
        int ci = 0;
        foreach (var c in FreeCorners())
        {
            Vector3 toCenter = new Vector3(cx - c.x, 0f, cz - c.y).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, toCenter);
            Vector3 p = new Vector3(c.x, 0f, c.y) + toCenter * 2.2f;
            Color nc = NeonPick(rng);
            var rot = Quaternion.LookRotation(side);
            if (ci++ % 2 == 0)
            {
                Prim(PrimitiveType.Cube, p + Vector3.up * 0.55f, new Vector3(0.8f, 1.1f, 3.4f), body, rot, true);
                Prim(PrimitiveType.Cube, p + Vector3.up * 1.14f, new Vector3(1.0f, 0.08f, 3.6f), Lit(new Color(0.35f, 0.22f, 0.45f), 0.8f), rot);
                Prim(PrimitiveType.Cube, p + toCenter * 0.41f + Vector3.up * 0.7f, new Vector3(0.03f, 0.06f, 3.3f), Lit(nc, 0.3f, 2.8f), rot);
                for (int i = 0; i < 8; i++)
                    Prim(PrimitiveType.Cylinder, p + side * (-1.5f + i * 0.42f) + Vector3.up * 1.32f, new Vector3(0.12f, R(0.12f, 0.2f), 0.12f), Lit(NeonPick(rng), 0.3f, 1.8f));
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 sp = p + toCenter * 1.0f + side * (i * 1.0f);
                    Prim(PrimitiveType.Cylinder, sp + Vector3.up * 0.35f, new Vector3(0.07f, 0.35f, 0.07f), dark);
                    Prim(PrimitiveType.Cylinder, sp + Vector3.up * 0.72f, new Vector3(0.42f, 0.05f, 0.42f), Lit(NeonPick(rng), 0.4f, 1.4f));
                }
            }
            else
            {
                Prim(PrimitiveType.Cube, p + Vector3.up * 0.8f, new Vector3(1.5f, 0.16f, 2.8f), Lit(new Color(0.25f, 0.14f, 0.1f), 0.5f), rot, true);
                Prim(PrimitiveType.Cube, p + Vector3.up * 0.9f, new Vector3(1.2f, 0.05f, 2.5f), Lit(new Color(0.03f, 0.35f, 0.3f), 0.2f, 0.3f), rot);
                foreach (float sx in new[] { -0.6f, 0.6f })
                    foreach (float sz in new[] { -1.2f, 1.2f })
                        Prim(PrimitiveType.Cylinder, p + side * sz + toCenter * sx + Vector3.up * 0.38f, new Vector3(0.16f, 0.38f, 0.16f), dark);
                for (int i = 0; i < 8; i++)
                    Prim(PrimitiveType.Sphere, p + side * R(-1f, 1f) + toCenter * R(-0.45f, 0.45f) + Vector3.up * 0.96f, Vector3.one * 0.1f, Lit(NeonPick(rng), 0.7f, 1.2f));
                Prim(PrimitiveType.Cube, p + Vector3.up * 2.6f, new Vector3(0.1f, 0.1f, 2.4f), Lit(nc, 0.3f, 3f), rot);
            }
            Lamp(p + Vector3.up * 2.4f, nc, 4f, 10f);
        }

        // 宙に浮かぶ、たくさんのゲーム画面（中央へ向く）
        int screens = Mathf.Clamp(Mathf.RoundToInt(minSide * 0.9f), 6, 18);
        for (int i = 0; i < screens; i++)
        {
            float a = i * Mathf.PI * 2f / screens + R(-0.2f, 0.2f);
            float rr = minSide * R(0.28f, 0.46f);
            Vector3 p = new Vector3(cx + Mathf.Cos(a) * rr, H * R(0.4f, 0.85f), cz + Mathf.Sin(a) * rr);
            Vector3 face = new Vector3(cx - p.x, 0f, cz - p.z);
            NeonArt.Screen(parent, p, face, R(2.2f, 4.2f), rng.Next(NeonArt.Kinds), NeonPick(rng), 0.5f, R(-6f, 6f));
        }

        // 天井の四方: ネオンの輪
        foreach (var c in new[] { new Vector2(minX + 2f, minZ + 2f), new Vector2(maxX - 2f, minZ + 2f), new Vector2(minX + 2f, maxZ - 2f), new Vector2(maxX - 2f, maxZ - 2f) })
        {
            Color rc = NeonPick(rng);
            var ring = Group("Ring", new Vector3(c.x, H - 1.5f, c.y));
            ring.gameObject.AddComponent<TunnelSpin>().speed = new Vector3(0f, 50f, 20f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                Prim(PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 1.4f, Mathf.Sin(a) * 1.4f, 0f), new Vector3(0.9f, 0.08f, 0.08f), Lit(rc, 0.3f, 3f), Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f), false, ring);
            }
        }
    }
}
