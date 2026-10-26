using UnityEngine;

/// <summary>蒸気機関の大広間（スチームパンクの内装の、天井の高い部屋）。</summary>
public partial class GrandRoomBuilder
{
    void SteamHall()
    {
        var cu = Lit(SteamArt.Copper, 0.55f);
        var cuD = Lit(SteamArt.CopperDark, 0.45f);
        var brass = Lit(SteamArt.Brass, 0.65f);
        var elec = Lit(SteamArt.Electric, 0.5f, 3.2f);
        var glass = Ghost(new Color(0.55f, 0.85f, 1f, 0.16f));
        Color blue = SteamArt.Electric;

        float minSide = Mathf.Min(Width, Depth);

        // 床: 銅の円盤と、真鍮の輪
        float rad = minSide * 0.36f;
        Prim(PrimitiveType.Cylinder, new Vector3(cx, 0.03f, cz), new Vector3(rad * 2f, 0.01f, rad * 2f), cuD);
        Prim(PrimitiveType.Cylinder, new Vector3(cx, 0.04f, cz), new Vector3(rad * 1.7f, 0.01f, rad * 1.7f), cu);
        Prim(PrimitiveType.Cylinder, new Vector3(cx, 0.05f, cz), new Vector3(rad * 1.3f, 0.01f, rad * 1.3f), brass);

        // 中央: 天井まで届く、巨大な電気の筒（稲妻が走る。すり抜けられる）
        float tr = Mathf.Clamp(minSide * 0.07f, 1f, 1.8f);
        SteamArt.ElectricTube(parent, new Vector3(cx, 0f, cz), H - 0.2f, tr, glass, cu, brass, elec);
        Lamp(new Vector3(cx, H * 0.5f, cz), blue, 5.5f, Mathf.Max(Width, Depth) * 1.1f);

        // 天井: 噛み合う、水平の巨大な歯車（2組）
        float rA = Mathf.Clamp(minSide * 0.2f, 3f, 6.5f), rB = rA * 0.7f;
        for (int k = 0; k < 2; k++)
        {
            float sgn = k == 0 ? 1f : -1f;
            float zc = cz + sgn * Depth * 0.26f;
            float d = rA + rB;
            Vector3 a = new Vector3(cx - d * 0.5f * sgn, H - 1.1f - k * 0.5f, zc);
            Vector3 b = new Vector3(cx + d * 0.5f * sgn, H - 1.1f - k * 0.5f, zc);
            var frame = Quaternion.LookRotation(Vector3.up, Vector3.right);
            int nA = SteamArt.TeethFor(rA), nB = SteamArt.TeethFor(rB);
            float spin = 9f;
            SteamArt.BuildGear(parent, a, frame, rA, 0.9f, nA, spin * sgn, SteamArt.AngleToward(frame, b - a), cu, cuD, brass, elec);
            SteamArt.BuildGear(parent, b, frame, rB, 0.9f, nB, -spin * sgn * nA / nB, SteamArt.AngleToward(frame, a - b) + 180f / nB, cu, cuD, brass, elec);
        }

        // 天井を走る、銅の太い管（継ぎ目の輪つき）
        for (int i = 0; i < 3; i++)
        {
            float z = Mathf.Lerp(minZ + 1.5f, maxZ - 1.5f, (i + 0.5f) / 3f);
            Prim(PrimitiveType.Cylinder, new Vector3(cx, H - 0.45f, z), new Vector3(0.7f, Width * 0.5f, 0.7f), cu, Quaternion.Euler(0f, 0f, 90f));
            for (float x = minX + 2f; x < maxX - 1f; x += 4.5f)
                Prim(PrimitiveType.Cylinder, new Vector3(x, H - 0.45f, z), new Vector3(0.95f, 0.1f, 0.95f), brass, Quaternion.Euler(0f, 0f, 90f));
        }

        // 四隅: 銅の大きなボイラー（当たり判定あり）と、天井へ伸びる管、蒸気
        foreach (var c in FreeCorners())
        {
            float br = R(1.1f, 1.5f), bh = H * R(0.4f, 0.55f);
            Prim(PrimitiveType.Cylinder, new Vector3(c.x, bh * 0.5f, c.y), new Vector3(br * 2f, bh * 0.5f, br * 2f), cu, null, true);
            Prim(PrimitiveType.Sphere, new Vector3(c.x, bh, c.y), new Vector3(br * 2f, br * 1.1f, br * 2f), cu);
            for (int i = 1; i <= 3; i++)
                Prim(PrimitiveType.Cylinder, new Vector3(c.x, bh * i / 4f, c.y), new Vector3(br * 2.1f, 0.08f, br * 2.1f), brass);
            Prim(PrimitiveType.Cylinder, new Vector3(c.x, (bh + H) * 0.5f, c.y), new Vector3(0.4f, (H - bh) * 0.5f, 0.4f), cuD);
            Prim(PrimitiveType.Sphere, new Vector3(c.x, bh * 0.7f, c.y) + new Vector3(br * 0.9f, 0f, 0f), Vector3.one * 0.35f, elec);
            Particles(new Fx.Spec
            {
                color = new Color(0.92f, 0.94f, 0.97f, 0.5f), color2 = new Color(0.7f, 0.75f, 0.8f, 0.4f), count = 0, rate = 8f, duration = 30f, loop = true, life = 3f,
                speedMin = 0.6f, speedMax = 1.3f, sizeMin = 0.5f, sizeMax = 1.1f, gravity = -0.05f, shape = ParticleSystemShapeType.Cone, radius = 0.25f, angle = 14f,
                grow = true, additive = false, shrink = false,
            }, new Vector3(c.x, bh + br * 0.5f, c.y), Quaternion.Euler(-90f, 0f, 0f));
        }

        // 壁ぎわ: 壁に付く、噛み合う歯車の列（見た目だけ）
        float gy = Mathf.Min(H * 0.5f, 6f);
        float gr = Mathf.Clamp(H * 0.2f, 2.2f, 3.4f);
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? minX + 0.5f : maxX - 0.5f;
            Quaternion frame = Quaternion.LookRotation(side == 0 ? Vector3.right : Vector3.left, Vector3.up);
            float z = minZ + gr + 1.5f;
            float prevR = 0f; float spin = 8f; Vector3 prev = default; int prevN = 0; int idx = 0;
            while (z < maxZ - gr)
            {
                float r2 = idx % 2 == 0 ? gr : gr * 0.7f;
                if (idx > 0) z += (prevR + r2);
                if (z > maxZ - r2) break;
                Vector3 cpos = new Vector3(x, gy + (idx % 2 == 0 ? 0f : 0.4f), z);
                int n = SteamArt.TeethFor(r2);
                float phase = idx == 0 ? SteamArt.AngleToward(frame, Vector3.forward) : SteamArt.AngleToward(frame, prev - cpos) + 180f / n;
                SteamArt.BuildGear(parent, cpos, frame, r2, 0.7f, n, idx == 0 ? spin : -spin * prevN / n, phase, cu, cuD, brass, elec);
                spin = idx == 0 ? spin : -spin * prevN / n;
                prev = cpos; prevR = r2; prevN = n; idx++;
                if (idx > 6) break;
            }
        }
    }
}
