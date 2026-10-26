using UnityEngine;

/// <summary>スチームパンク（銅の機械仕掛け）の内装の小物。歯車・電気の筒・ボイラー・銅管・蒸気。どれも当たり判定は無い。</summary>
public partial class DungeonDecor
{
    static readonly Color SteamCu = SteamArt.Copper, SteamCuDark = SteamArt.CopperDark, SteamBrass = SteamArt.Brass, SteamElec = SteamArt.Electric;
    int steamVents;

    void SteamGear(Vector3 pos, Quaternion frame, float r, float thick, float spin, float phase)
    {
        SteamArt.BuildGear(root, pos, frame, r, thick, SteamArt.TeethFor(r), spin, phase,
            Mat(SteamCu, 0.55f, null), Mat(SteamCuDark, 0.45f, null), Mat(SteamBrass, 0.65f, null), Mat(SteamElec, 0.5f, SteamElec * 3f));
    }

    /// <summary>壁を、磨かれた銅の板（真鍮の帯・青白い継ぎ目）と、真鍮の横走りの管でおおう。通路が、工場ではなく、銅の機械の中に見えるように。</summary>
    void SteamCladding(Edge e)
    {
        var rot = Quaternion.LookRotation(e.inward);
        float w = cs - 0.2f;
        if (Chance(0.8f))
        {
            Vector3 b = e.wall + e.inward * 0.03f;
            Prim(PrimitiveType.Cube, b + Vector3.up * 1.7f, new Vector3(w, 2.9f, 0.05f), new Color(0.86f, 0.5f, 0.27f), 0.7f, rot);
            Prim(PrimitiveType.Cube, b + Vector3.up * 0.22f + e.inward * 0.02f, new Vector3(w, 0.14f, 0.08f), SteamBrass, 0.75f, rot);
            Prim(PrimitiveType.Cube, b + Vector3.up * 3.18f + e.inward * 0.02f, new Vector3(w, 0.14f, 0.08f), SteamBrass, 0.75f, rot);
            if (Chance(0.35f))
                Prim(PrimitiveType.Cube, b + Vector3.up * 1.7f + e.inward * 0.03f, new Vector3(0.05f, 2.7f, 0.03f), SteamElec, 0.5f, rot, SteamElec * 1.6f, true);
        }
        if (Chance(0.3f))
        {
            var pr = Quaternion.LookRotation(e.side) * Quaternion.Euler(90f, 0f, 0f);
            Vector3 p = e.wall + e.inward * 0.2f + Vector3.up * R(0.5f, 3.3f);
            Prim(PrimitiveType.Cylinder, p, new Vector3(0.2f, cs * 0.5f, 0.2f), SteamCu, 0.7f, pr);
            Prim(PrimitiveType.Cylinder, p + e.side * (cs * 0.5f - 0.2f), new Vector3(0.3f, 0.06f, 0.3f), SteamBrass, 0.75f, pr);
            Prim(PrimitiveType.Cylinder, p - e.side * (cs * 0.5f - 0.2f), new Vector3(0.3f, 0.06f, 0.3f), SteamBrass, 0.75f, pr);
        }
    }

    /// <summary>壁に付く、噛み合う歯車（2つ、または1つ）。</summary>
    void SteamGearWall(Edge e)
    {
        var frame = Quaternion.LookRotation(e.inward, Vector3.up);
        float r1 = R(0.7f, 1.2f), thick = 0.24f;
        Vector3 c1 = e.wall + e.inward * (thick * 0.5f + 0.04f) + Vector3.up * R(1.5f, 2.4f) + e.side * R(-0.8f, 0.8f);
        float s1 = R(18f, 40f);
        float sign = Chance(0.5f) ? 1f : -1f;
        float toward = SteamArt.AngleToward(frame, e.side * sign);
        SteamGear(c1, frame, r1, thick, s1, toward);
        if (Chance(0.75f))
        {
            float r2 = r1 * R(0.55f, 0.85f);
            Vector3 c2 = c1 + e.side * ((r1 + r2) * sign);
            Vector3 dir = (c2 - c1).normalized;
            // 1つめの歯を、2つめの方向へ向け直す（見た目の位相合わせ）
            int n2 = SteamArt.TeethFor(r2), n1 = SteamArt.TeethFor(r1);
            float a2 = SteamArt.AngleToward(frame, -dir) + 180f / n2;
            SteamGear(c2, frame, r2, thick, -s1 * n1 / n2, a2);
        }
    }

    /// <summary>壁際に立つ、小さな電気の筒。青白い稲妻が、中を走る。</summary>
    void SteamTubeSmall(Edge e)
    {
        Vector3 b = e.wall + e.inward * 0.45f + e.side * R(-1.2f, 1.2f);
        float height = Mathf.Min(h - 0.4f, R(2.2f, 3.2f));
        float rad = R(0.22f, 0.34f);
        SteamArt.ElectricTube(root, b, height, rad, DungeonMaterials.Ghost(new Color(0.55f, 0.85f, 1f, 0.2f)),
            Mat(SteamCu, 0.55f, null), Mat(SteamBrass, 0.65f, null), Mat(SteamElec, 0.5f, SteamElec * 3f));
        Vector3 lp = b + Vector3.up * (height * 0.6f) + e.inward * 0.5f;
        if (LightOk(lp)) AddLight(null, lp, SteamElec, 3.2f, 8f);
    }

    /// <summary>壁に寄せた、銅のボイラー（横置きの円筒）。帯・リベット・煙突・圧力計。</summary>
    void SteamBoiler(Edge e)
    {
        float r = R(0.55f, 0.8f), len = R(1.6f, 2.4f);
        Vector3 p = e.wall + e.inward * (r + 0.1f) + e.side * R(-0.8f, 0.8f) + Vector3.up * (r + 0.1f);
        var along = Quaternion.LookRotation(e.side) * Quaternion.Euler(90f, 0f, 0f);
        Prim(PrimitiveType.Cylinder, p, new Vector3(r * 2f, len * 0.5f, r * 2f), SteamCu, 0.6f, along);
        for (int i = -1; i <= 1; i++)
            Prim(PrimitiveType.Cylinder, p + e.side * (i * len * 0.32f), new Vector3(r * 2.1f, 0.06f, r * 2.1f), SteamBrass, 0.7f, along);
        foreach (float s in new[] { -1f, 1f })
            Prim(PrimitiveType.Sphere, p + e.side * (s * len * 0.5f), new Vector3(r * 2f, r * 1.5f, r * 2f), SteamCu, 0.6f);
        Prim(PrimitiveType.Cylinder, p + Vector3.up * (r + 0.55f), new Vector3(0.22f, 0.55f, 0.22f), SteamCuDark, 0.4f);
        Prim(PrimitiveType.Cylinder, p + Vector3.up * (r + 1.1f), new Vector3(0.32f, 0.05f, 0.32f), SteamBrass, 0.6f);
        // 圧力計
        Prim(PrimitiveType.Cylinder, p + e.inward * (r + 0.02f) + Vector3.up * 0.1f, new Vector3(0.3f, 0.03f, 0.3f), SteamElec, 0.5f,
             Quaternion.LookRotation(e.inward) * Quaternion.Euler(90f, 0f, 0f), SteamElec * 2f, false);
    }

    /// <summary>床から天井へ伸びる、2本の銅の管。節の輪と、青白く光る継ぎ目。</summary>
    void SteamPipes(Edge e)
    {
        for (int k = 0; k < 2; k++)
        {
            Vector3 b = e.wall + e.inward * (0.2f + 0.3f * k) + e.side * (R(-1.3f, 1.3f));
            float rad = k == 0 ? 0.16f : 0.1f;
            Prim(PrimitiveType.Cylinder, b + Vector3.up * (h * 0.5f), new Vector3(rad * 2f, h * 0.5f, rad * 2f), SteamCu, 0.6f);
            int rings = Mathf.Max(2, Mathf.RoundToInt(h / 1.4f));
            for (int i = 0; i <= rings; i++)
                Prim(PrimitiveType.Cylinder, b + Vector3.up * (h * i / rings), new Vector3(rad * 2.6f, 0.05f, rad * 2.6f), SteamBrass, 0.7f);
            if (k == 0)
                Prim(PrimitiveType.Sphere, b + Vector3.up * R(0.8f, h - 0.8f), new Vector3(rad * 3f, rad * 3f, rad * 3f), SteamElec, 0.5f, null, SteamElec * 2.4f, false);
        }
    }

    void SteamGauge(Edge e)
    {
        Vector3 p = e.wall + e.inward * 0.05f + Vector3.up * R(1.4f, 2.3f) + e.side * R(-1.2f, 1.2f);
        var rot = Quaternion.LookRotation(e.inward);
        Prim(PrimitiveType.Cube, p, new Vector3(0.8f, 0.6f, 0.08f), SteamCuDark, 0.4f, rot);
        Prim(PrimitiveType.Cylinder, p + e.inward * 0.05f, new Vector3(0.45f, 0.03f, 0.45f), SteamBrass, 0.7f, rot * Quaternion.Euler(90f, 0f, 0f));
        Prim(PrimitiveType.Cylinder, p + e.inward * 0.075f, new Vector3(0.36f, 0.02f, 0.36f), new Color(0.85f, 0.95f, 1f), 0.4f, rot * Quaternion.Euler(90f, 0f, 0f), SteamElec * 1.4f, false);
        Prim(PrimitiveType.Cube, p + e.inward * 0.1f, new Vector3(0.04f, 0.17f, 0.01f), new Color(0.15f, 0.08f, 0.05f), 0.3f, rot * Quaternion.Euler(0f, 0f, R(-60f, 60f)));
    }

    void SteamVentAt(Vector3 pos)
    {
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.06f, new Vector3(0.7f, 0.06f, 0.7f), SteamCuDark, 0.5f);
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.14f, new Vector3(0.5f, 0.03f, 0.5f), new Color(0.05f, 0.04f, 0.04f), 0.2f);
        if (steamVents >= 14) return;
        steamVents++;
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.9f, 0.92f, 0.95f, 0.5f), color2 = new Color(0.7f, 0.75f, 0.8f, 0.4f), count = 0, rate = 7f, duration = 30f, loop = true, life = 2.6f,
            speedMin = 0.5f, speedMax = 1.1f, sizeMin = 0.35f, sizeMax = 0.8f, gravity = -0.05f, shape = ParticleSystemShapeType.Cone, radius = 0.15f, angle = 12f,
            grow = true, additive = false, shrink = false,
        }, pos + Vector3.up * 0.2f, root, Quaternion.Euler(-90f, 0f, 0f));
    }

    void SteamWall(Edge e, double r)
    {
        if (r < 0.035) SteamGearWall(e);
        else if (r < 0.085) SteamTubeSmall(e);
        else if (r < 0.14) SteamBoiler(e);
        else if (r < 0.2) SteamPipes(e);
        else if (r < 0.235) SteamGauge(e);
        else if (r < 0.275) Barrel(e.wall + e.inward * 0.55f + e.side * R(-1.2f, 1.2f), new Color(0.55f, 0.32f, 0.18f));
        else if (r < 0.305) Crate(e.wall + e.inward * 0.6f + e.side * R(-1.2f, 1.2f));
        else if (r < 0.33) SteamVentAt(e.wall + e.inward * R(0.6f, 1.2f) + e.side * R(-1.2f, 1.2f));
    }

    void SteamCorner(Vector3 pos, Vector3 corner, Vector3 diag, bool boss)
    {
        if (boss) { Brazier(corner + diag * 0.9f, new Color(0.55f, 0.85f, 1f)); return; }
        // 床から天井へ立つ、太い銅の柱と、節の輪
        float rad = 0.28f;
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * (h * 0.5f), new Vector3(rad * 2f, h * 0.5f, rad * 2f), SteamCu, 0.6f);
        int rings = Mathf.Max(2, Mathf.RoundToInt(h / 1.2f));
        for (int i = 0; i <= rings; i++)
            Prim(PrimitiveType.Cylinder, pos + Vector3.up * (h * i / rings), new Vector3(rad * 2.5f, 0.07f, rad * 2.5f), i % 2 == 0 ? SteamBrass : SteamCuDark, 0.7f);
    }

    void SteamArch(Vector3 cell, Vector3 axis)
    {
        float half = cs * 0.5f - 0.3f;
        Prim(PrimitiveType.Cylinder, cell + axis * half + Vector3.up * (h * 0.5f), new Vector3(0.5f, h * 0.5f, 0.5f), SteamCu, 0.6f);
        Prim(PrimitiveType.Cylinder, cell - axis * half + Vector3.up * (h * 0.5f), new Vector3(0.5f, h * 0.5f, 0.5f), SteamCu, 0.6f);
        var beam = Prim(PrimitiveType.Cylinder, cell + Vector3.up * (h - 0.4f), new Vector3(0.5f, cs * 0.5f, 0.5f), SteamCu, 0.6f);
        beam.transform.rotation = Quaternion.LookRotation(axis) * Quaternion.Euler(90f, 0f, 0f);
        for (int i = -1; i <= 1; i++)
        {
            var ring = Prim(PrimitiveType.Cylinder, cell + axis * (i * half * 0.7f) + Vector3.up * (h - 0.4f), new Vector3(0.62f, 0.05f, 0.62f), SteamBrass, 0.7f);
            ring.transform.rotation = Quaternion.LookRotation(axis) * Quaternion.Euler(90f, 0f, 0f);
        }
        if (Chance(0.6f)) HangLantern(cell + Vector3.up * (h - 0.7f), new Color(0.6f, 0.88f, 1f));
    }

    void SteamCeiling(Vector3 pos, bool boss)
    {
        double r = rng.NextDouble();
        if (r < 0.3 || boss) HangLantern(pos, new Color(0.6f, 0.88f, 1f));
        else if (r < 0.55)
        {
            // 天井の歯車（水平に回る）
            float rad = R(0.8f, 1.7f);
            SteamGear(pos + Vector3.down * 0.25f, Quaternion.LookRotation(Vector3.up, Vector3.right), rad, 0.25f, R(15f, 40f) * (Chance(0.5f) ? 1f : -1f), R(0f, 40f));
        }
        else if (r < 0.75) Chain(pos);
        else
        {
            float len = R(0.6f, 1.6f);
            Prim(PrimitiveType.Cylinder, pos + Vector3.down * (len * 0.5f), new Vector3(0.24f, len * 0.5f, 0.24f), SteamCu, 0.6f);
            Prim(PrimitiveType.Cylinder, pos + Vector3.down * 0.15f, new Vector3(0.4f, 0.05f, 0.4f), SteamBrass, 0.7f);
            Prim(PrimitiveType.Cylinder, pos + Vector3.down * len, new Vector3(0.34f, 0.05f, 0.34f), SteamBrass, 0.7f);
        }
    }

    void SteamFloor(Vector3 pos)
    {
        double r = rng.NextDouble();
        if (r < 0.45)
        {
            float rad = R(0.4f, 0.85f);
            SteamGear(pos + Vector3.up * 0.12f, Quaternion.LookRotation(Vector3.up, Vector3.right), rad, 0.14f, R(20f, 45f) * (Chance(0.5f) ? 1f : -1f), R(0f, 40f));
        }
        else if (r < 0.75) SteamVentAt(pos);
        else Rubble(pos);
    }
}
