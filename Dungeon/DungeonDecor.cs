using System.Collections.Generic;
using UnityEngine;

/// <summary>松明やランタンの炎のゆらぎ。</summary>
public class FlameFlicker : MonoBehaviour
{
    Light lamp;
    float baseIntensity, seed;

    void Start()
    {
        lamp = GetComponent<Light>();
        if (lamp != null) baseIntensity = lamp.intensity;
        seed = Random.value * 50f;
    }

    void Update()
    {
        if (lamp == null) return;
        lamp.intensity = baseIntensity * (0.78f + 0.44f * Mathf.PerlinNoise(Time.time * 7f, seed));
    }
}

/// <summary>
/// ダンジョンの内装。テーマ（洞窟・湿った坑道・鉱山・暗い回廊・水晶・闘技場）ごとに、壁際の小物・天井の飾り・光源・霧を置く。
/// 置くものには当たり判定を付けない（敵の移動や通行の邪魔にならない）。
/// </summary>
public partial class DungeonDecor
{
    struct Edge
    {
        public Vector3 cell;     // 床セルの中心
        public Vector3 wall;     // 壁の面の中心（床の高さ）
        public Vector3 inward;   // 壁から部屋の中へ向かう向き
        public Vector3 side;     // 壁に沿った向き
        public bool boss;
    }

    readonly DungeonGenerator gen;
    readonly DungeonStyle style;
    readonly System.Random rng;
    readonly float cs, h;
    Transform root;
    readonly List<Vector3> lights = new List<Vector3>();
    readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    Material baseMat;

    const int MaxLights = 45;
    const float LightSpacing = 9f;

    DungeonDecor(DungeonGenerator g, DungeonStyle s, System.Random r)
    {
        gen = g; style = s; rng = r;
        cs = g.cellSize; h = g.wallHeight;
        baseMat = g.wallMaterial != null ? g.wallMaterial : g.floorMaterial;
    }

    DungeonData data;
    bool dark;

    public static void Build(DungeonGenerator gen, Transform parent, DungeonData data, DungeonStyle style, int seed)
    {
        var d = new DungeonDecor(gen, style, new System.Random(seed * 31 + 7));
        d.data = data;
        var it = d.Run(parent);
        while (it.MoveNext()) { }
    }

    /// <summary>Build を、少しずつ進める版（ロード画面が止まらないように、時間を区切って、1フレームずつ待つ）。</summary>
    public static System.Collections.IEnumerator BuildRoutine(DungeonGenerator gen, Transform parent, DungeonData data, DungeonStyle style, int seed)
    {
        var d = new DungeonDecor(gen, style, new System.Random(seed * 31 + 7));
        d.data = data;
        return d.Run(parent);
    }

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    bool Chance(float p) => rng.NextDouble() < p;

    // ---------------------------------------------------------------- 全体

    System.Collections.IEnumerator Run(Transform parent)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = style.fog;
        RenderSettings.fogDensity = style.fogDensity;
        // 暗闇のダンジョン: 環境を真っ暗にする（壁の灯りや吊りランタンも、少なくなる）
        DarkDungeon.Begin(data);
        dark = DarkDungeon.Active;

        root = new GameObject("DungeonDecor").transform;
        root.SetParent(parent, false);

        var grid = gen.Grid;
        int w = gen.gridWidth, hh = gen.gridHeight;
        var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        bool hasBoss = gen.Rooms.Count > 0 && gen.BossRoomIndex >= 0 && gen.BossRoomIndex < gen.Rooms.Count;
        RectInt boss = hasBoss ? gen.Rooms[gen.BossRoomIndex] : default;

        for (int x = 0; x < w; x++)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            for (int y = 0; y < hh; y++)
            {
                if (!grid[x, y]) continue;
                if (gen.IsReserved(new Vector2Int(x, y))) continue; // 階段・梯子のセル
                var cell = new Vector3(x * cs, 0f, y * cs);
                bool inBoss = hasBoss && boss.Contains(new Vector2Int(x, y));
                var wallDirs = new List<Vector2Int>();

                foreach (var dir in dirs)
                {
                    int nx = x + dir.x, ny = y + dir.y;
                    bool isWall = nx < 0 || ny < 0 || nx >= w || ny >= hh || !grid[nx, ny];
                    if (!isWall) continue;
                    wallDirs.Add(dir);

                    var outward = new Vector3(dir.x, 0f, dir.y);
                    var e = new Edge
                    {
                        cell = cell, wall = cell + outward * (cs * 0.5f), inward = -outward,
                        side = new Vector3(-dir.y, 0f, dir.x), boss = inBoss,
                    };
                    OnWall(e);
                }

                // 角（隣り合う2方向が壁）
                if (wallDirs.Count >= 2)
                {
                    for (int i = 0; i < wallDirs.Count; i++)
                        for (int j = i + 1; j < wallDirs.Count; j++)
                        {
                            var a = wallDirs[i]; var b = wallDirs[j];
                            if (a.x * b.x + a.y * b.y != 0) continue; // 反対側どうしは角ではない
                            var diag = -new Vector3(a.x + b.x, 0f, a.y + b.y).normalized;
                            Vector3 corner = cell + new Vector3(a.x + b.x, 0f, a.y + b.y) * (cs * 0.5f);
                            OnCorner(corner, diag, inBoss);
                        }
                }

                // 通路（向かい合う壁）
                if (wallDirs.Count == 2 && wallDirs[0] == -wallDirs[1])
                    OnCorridor(cell, new Vector3(wallDirs[0].x, 0f, wallDirs[0].y), inBoss);

                // 天井
                OnCeiling(cell, inBoss);
                // 床
                OnFloor(cell, inBoss);
            }
        }

        // 描画負荷の軽減（光源・揺らぎのあるものは静的にしていない）
        var batch = ChunkBatcher.BatchRoutine(root, 40f); // 場所ごとの塊に分ける（画面に映らない・遠い塊は、描画しない）
        while (batch.MoveNext()) yield return null;
    }

    // ---------------------------------------------------------------- テーマごとの配置

    void OnWall(Edge e)
    {
        if (style.theme == DungeonTheme.Steampunk) SteamCladding(e); // 壁一面を、銅の板と管でおおう
        if (style.theme == DungeonTheme.Neon) NeonCladding(e); // 壁を走る、ネオンのライン
        double r = rng.NextDouble();
        float torch = (e.boss ? 0.22f : 0.065f) * (dark ? 0.3f : 1f);
        if (r < torch) { WallLight(e); return; }
        r -= torch;

        switch (style.theme)
        {
            case DungeonTheme.Cave:
                if (r < 0.10f) Stalagmite(e.wall + e.inward * R(0.3f, 0.9f) + e.side * R(-1.5f, 1.5f), R(0.25f, 0.55f), R(0.8f, 1.8f));
                else if (r < 0.19f) Rocks(e.wall + e.inward * 0.6f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.22f) Mushrooms(e.wall + e.inward * 0.4f + e.side * R(-1.4f, 1.4f), new Color(0.9f, 0.6f, 0.3f), false);
                else if (r < 0.24f) Bones(e.wall + e.inward * 0.6f + e.side * R(-1f, 1f));
                else if (r < 0.265f) Crate(e.wall + e.inward * 0.6f + e.side * R(-1.2f, 1.2f));
                break;

            case DungeonTheme.WetTunnel:
                if (r < 0.12f) Mushrooms(e.wall + e.inward * 0.35f + e.side * R(-1.5f, 1.5f), new Color(0.3f, 1f, 0.8f), true);
                else if (r < 0.2f) Puddle(e.wall + e.inward * R(0.8f, 1.5f) + e.side * R(-1.2f, 1.2f));
                else if (r < 0.27f) Pipe(e);
                else if (r < 0.31f) Barrel(e.wall + e.inward * 0.55f + e.side * R(-1.2f, 1.2f), new Color(0.3f, 0.35f, 0.3f));
                else if (r < 0.34f) Rocks(e.wall + e.inward * 0.6f + e.side * R(-1.4f, 1.4f));
                break;

            case DungeonTheme.Mine:
                if (r < 0.12f) OreVein(e);
                else if (r < 0.17f) Crate(e.wall + e.inward * 0.6f + e.side * R(-1.2f, 1.2f));
                else if (r < 0.22f) Barrel(e.wall + e.inward * 0.55f + e.side * R(-1.2f, 1.2f), new Color(0.4f, 0.27f, 0.15f));
                else if (r < 0.27f) Rocks(e.wall + e.inward * 0.6f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.30f) MineCart(e.wall + e.inward * 1.0f + e.side * R(-0.6f, 0.6f), e.side);
                else if (r < 0.34f) Pickaxe(e);
                break;

            case DungeonTheme.DarkCorridor:
                if (r < 0.08f) Skulls(e.wall + e.inward * 0.5f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.14f) Bones(e.wall + e.inward * 0.6f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.19f) Candles(e.wall + e.inward * 0.5f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.24f) Coffin(e);
                else if (r < 0.30f) Banner(e, new Color(0.28f, 0.08f, 0.35f));
                else if (r < 0.33f) Barrel(e.wall + e.inward * 0.55f + e.side * R(-1.2f, 1.2f), new Color(0.2f, 0.18f, 0.22f));
                break;

            case DungeonTheme.Crystal:
                if (r < 0.14f) CrystalCluster(e.wall + e.inward * R(0.3f, 0.8f) + e.side * R(-1.5f, 1.5f), false);
                else if (r < 0.21f) CrystalCluster(e.wall + e.inward * 0.1f + Vector3.up * R(0.8f, 2.8f) + e.side * R(-1.5f, 1.5f), true, e.inward);
                else if (r < 0.26f) Rocks(e.wall + e.inward * 0.6f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.29f) Stalagmite(e.wall + e.inward * 0.5f + e.side * R(-1.5f, 1.5f), R(0.2f, 0.4f), R(0.7f, 1.4f));
                break;

            case DungeonTheme.Arena:
                if (r < 0.14f) Banner(e, R(0f, 1f) < 0.5f ? new Color(0.65f, 0.1f, 0.1f) : new Color(0.15f, 0.2f, 0.55f));
                else if (r < 0.2f) Pillar(e.wall + e.inward * 0.55f + e.side * R(-1.2f, 1.2f), 0.4f);
                else if (r < 0.24f) Brazier(e.wall + e.inward * 0.7f + e.side * R(-1.2f, 1.2f));
                else if (r < 0.28f) Crate(e.wall + e.inward * 0.6f + e.side * R(-1.2f, 1.2f));
                else if (r < 0.31f) Weapons(e);
                break;

            case DungeonTheme.Ice:
                if (r < 0.13f) CrystalCluster(e.wall + e.inward * R(0.3f, 0.8f) + e.side * R(-1.5f, 1.5f), false);
                else if (r < 0.2f) Stalagmite(e.wall + e.inward * R(0.3f, 0.9f) + e.side * R(-1.5f, 1.5f), R(0.2f, 0.45f), R(0.8f, 1.9f));
                else if (r < 0.25f) Puddle(e.wall + e.inward * R(0.8f, 1.5f) + e.side * R(-1.2f, 1.2f));
                else if (r < 0.29f) Rocks(e.wall + e.inward * 0.6f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.31f) Bones(e.wall + e.inward * 0.6f + e.side * R(-1f, 1f));
                break;

            case DungeonTheme.Forest:
                if (r < 0.12f) Mushrooms(e.wall + e.inward * 0.35f + e.side * R(-1.5f, 1.5f), new Color(0.9f, 0.5f, 0.2f), false);
                else if (r < 0.21f) Mushrooms(e.wall + e.inward * 0.35f + e.side * R(-1.5f, 1.5f), new Color(0.5f, 1f, 0.5f), true);
                else if (r < 0.28f) Vines(e.wall + e.inward * 0.2f + e.side * R(-1.5f, 1.5f) + Vector3.up * h);
                else if (r < 0.34f) Rocks(e.wall + e.inward * 0.6f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.37f) Stalagmite(e.wall + e.inward * 0.5f + e.side * R(-1.5f, 1.5f), R(0.2f, 0.4f), R(0.7f, 1.4f));
                break;

            case DungeonTheme.Volcano:
                if (r < 0.14f) OreVein(e);
                else if (r < 0.21f) Rocks(e.wall + e.inward * 0.6f + e.side * R(-1.4f, 1.4f), new Color(0.16f, 0.1f, 0.09f));
                else if (r < 0.26f) Stalagmite(e.wall + e.inward * R(0.3f, 0.9f) + e.side * R(-1.5f, 1.5f), R(0.25f, 0.5f), R(0.8f, 1.8f));
                else if (r < 0.3f) Brazier(e.wall + e.inward * 0.7f + e.side * R(-1.2f, 1.2f), new Color(1f, 0.35f, 0.1f));
                else if (r < 0.33f) Bones(e.wall + e.inward * 0.6f + e.side * R(-1f, 1f));
                break;

            case DungeonTheme.Desert:
                if (r < 0.13f) Pillar(e.wall + e.inward * 0.55f + e.side * R(-1.2f, 1.2f), 0.4f);
                else if (r < 0.2f) Banner(e, R(0f, 1f) < 0.5f ? new Color(0.75f, 0.55f, 0.15f) : new Color(0.15f, 0.4f, 0.45f));
                else if (r < 0.25f) Rubble(e.wall + e.inward * 0.6f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.29f) Bones(e.wall + e.inward * 0.6f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.33f) Barrel(e.wall + e.inward * 0.55f + e.side * R(-1.2f, 1.2f), new Color(0.5f, 0.38f, 0.2f));
                else if (r < 0.36f) Coffin(e);
                break;

            case DungeonTheme.Library:
                if (r < 0.2f) Bookshelf(e);
                else if (r < 0.26f) Candles(e.wall + e.inward * 0.5f + e.side * R(-1.4f, 1.4f));
                else if (r < 0.31f) Banner(e, new Color(0.3f, 0.12f, 0.1f));
                else if (r < 0.35f) Crate(e.wall + e.inward * 0.6f + e.side * R(-1.2f, 1.2f));
                else if (r < 0.38f) Pillar(e.wall + e.inward * 0.55f + e.side * R(-1.2f, 1.2f), 0.35f);
                break;

            case DungeonTheme.Starlit:
                if (r < 0.14f) CrystalCluster(e.wall + e.inward * 0.1f + Vector3.up * R(0.8f, 2.8f) + e.side * R(-1.5f, 1.5f), true, e.inward);
                else if (r < 0.21f) CrystalCluster(e.wall + e.inward * R(0.3f, 0.8f) + e.side * R(-1.5f, 1.5f), false);
                else if (r < 0.26f) Pillar(e.wall + e.inward * 0.55f + e.side * R(-1.2f, 1.2f), 0.35f);
                else if (r < 0.3f) Brazier(e.wall + e.inward * 0.7f + e.side * R(-1.2f, 1.2f), new Color(0.5f, 0.6f, 1f));
                else if (r < 0.33f) Banner(e, new Color(0.12f, 0.12f, 0.4f));
                break;

            case DungeonTheme.Steampunk:
                SteamWall(e, r);
                break;

            case DungeonTheme.Neon:
                NeonWall(e, r);
                break;
        }
    }

    void OnCorner(Vector3 corner, Vector3 diag, bool boss)
    {
        float p = boss ? 0.9f : 0.3f;
        if (!Chance(p)) return;
        Vector3 pos = corner + diag * 0.45f;

        switch (style.theme)
        {
            case DungeonTheme.Arena:
                Pillar(pos, 0.45f);
                if (boss) Brazier(corner + diag * 1.3f);
                break;
            case DungeonTheme.Mine: Crate(pos); break;
            case DungeonTheme.WetTunnel: Vines(pos); break;
            case DungeonTheme.DarkCorridor: if (boss) Brazier(corner + diag * 0.9f, new Color(0.7f, 0.3f, 1f)); else Cobweb(pos, diag); break;
            case DungeonTheme.Crystal: CrystalCluster(pos, false); break;
            case DungeonTheme.Ice: CrystalCluster(pos, false); break;
            case DungeonTheme.Starlit: if (boss) Brazier(corner + diag * 0.9f, new Color(0.5f, 0.6f, 1f)); else CrystalCluster(pos, false); break;
            case DungeonTheme.Forest: Vines(pos + Vector3.up * h); break;
            case DungeonTheme.Volcano: Rocks(pos, new Color(0.16f, 0.1f, 0.09f)); break;
            case DungeonTheme.Desert: Pillar(pos, 0.45f); break;
            case DungeonTheme.Library: if (boss) Brazier(corner + diag * 1.0f, new Color(1f, 0.7f, 0.3f)); else Cobweb(pos, diag); break;
            case DungeonTheme.Steampunk: SteamCorner(pos, corner, diag, boss); break;
            case DungeonTheme.Neon: NeonCorner(pos, corner, diag, boss); break;
            default: Rocks(pos); break;
        }
    }

    void OnCorridor(Vector3 cell, Vector3 axis, bool boss)
    {
        // axis: 壁から壁へ向かう方向（通路をまたぐ向き）
        if (style.theme == DungeonTheme.Mine && Chance(0.16f)) MineSupport(cell, axis);
        else if (style.theme == DungeonTheme.Arena && Chance(0.08f)) Arch(cell, axis);
        else if (style.theme == DungeonTheme.DarkCorridor && Chance(0.07f)) Arch(cell, axis);
        else if ((style.theme == DungeonTheme.Desert || style.theme == DungeonTheme.Library) && Chance(0.08f)) Arch(cell, axis);
        else if (style.theme == DungeonTheme.Forest && Chance(0.07f)) MineSupport(cell, axis);
        else if (style.theme == DungeonTheme.Steampunk && Chance(0.3f)) SteamArch(cell, axis);
        else if (style.theme == DungeonTheme.Neon && Chance(0.2f)) NeonArch(cell, axis);
    }

    void OnCeiling(Vector3 cell, bool boss)
    {
        float p = 0.11f;
        float ch = gen.CeilingHeightAt(Mathf.RoundToInt(cell.x / cs), Mathf.RoundToInt(cell.z / cs)); // 大広間・ボス部屋は、天井が高い
        Vector3 pos = cell + new Vector3(R(-1.5f, 1.5f), ch, R(-1.5f, 1.5f));
        if (!Chance(p)) return;

        switch (style.theme)
        {
            case DungeonTheme.Cave: Stalactite(pos, R(0.2f, 0.45f), R(0.6f, 1.6f), new Color(0.3f, 0.27f, 0.24f)); break;
            case DungeonTheme.WetTunnel:
                if (Chance(0.5f)) Vines(pos); else Stalactite(pos, R(0.15f, 0.3f), R(0.5f, 1.2f), new Color(0.2f, 0.28f, 0.3f));
                break;
            case DungeonTheme.Mine: if (Chance(0.5f)) HangLantern(pos); else Stalactite(pos, R(0.2f, 0.35f), R(0.5f, 1.1f), new Color(0.3f, 0.2f, 0.14f)); break;
            case DungeonTheme.DarkCorridor: Chain(pos); break;
            case DungeonTheme.Crystal: CrystalCluster(pos, true, Vector3.down); break;
            case DungeonTheme.Arena: if (boss || Chance(0.4f)) HangLantern(pos, new Color(1f, 0.75f, 0.4f)); break;
            case DungeonTheme.Ice: Stalactite(pos, R(0.15f, 0.35f), R(0.6f, 1.8f), new Color(0.65f, 0.82f, 0.92f)); break;
            case DungeonTheme.Forest: Vines(pos); break;
            case DungeonTheme.Volcano: Stalactite(pos, R(0.2f, 0.4f), R(0.6f, 1.5f), new Color(0.18f, 0.1f, 0.09f)); break;
            case DungeonTheme.Desert: if (boss || Chance(0.3f)) HangLantern(pos, new Color(1f, 0.8f, 0.4f)); break;
            case DungeonTheme.Library: if (Chance(0.5f)) HangLantern(pos, new Color(1f, 0.75f, 0.4f)); else Chain(pos); break;
            case DungeonTheme.Starlit: CrystalCluster(pos, true, Vector3.down); break;
            case DungeonTheme.Steampunk: SteamCeiling(pos, boss); break;
            case DungeonTheme.Neon: NeonCeiling(pos, boss); break;
        }
    }

    void OnFloor(Vector3 cell, bool boss)
    {
        // 部屋の中・通路の真ん中にも、低い飾りを少し置く
        if (!Chance(0.05f)) return;
        Vector3 pos = cell + new Vector3(R(-1.4f, 1.4f), 0f, R(-1.4f, 1.4f));
        switch (style.theme)
        {
            case DungeonTheme.WetTunnel: Puddle(pos); break;
            case DungeonTheme.Cave: Rocks(pos); break;
            case DungeonTheme.Mine: Rocks(pos, new Color(0.32f, 0.22f, 0.15f)); break;
            case DungeonTheme.DarkCorridor: Bones(pos); break;
            case DungeonTheme.Crystal: CrystalCluster(pos, false); break;
            case DungeonTheme.Arena: Rubble(pos); break;
            case DungeonTheme.Ice: if (Chance(0.5f)) Puddle(pos); else CrystalCluster(pos, false); break;
            case DungeonTheme.Forest: if (Chance(0.5f)) Mushrooms(pos, new Color(0.5f, 1f, 0.5f), true); else Rocks(pos); break;
            case DungeonTheme.Volcano: Rocks(pos, new Color(0.16f, 0.1f, 0.09f)); break;
            case DungeonTheme.Desert: if (Chance(0.5f)) Rubble(pos); else Bones(pos); break;
            case DungeonTheme.Library: if (Chance(0.5f)) Candles(pos); else Rubble(pos); break;
            case DungeonTheme.Starlit: CrystalCluster(pos, false); break;
            case DungeonTheme.Steampunk: SteamFloor(pos); break;
            case DungeonTheme.Neon: NeonFloor(pos); break;
        }
    }

    // ---------------------------------------------------------------- 壁の灯り（テーマごとに形が違う）

    void WallLight(Edge e)
    {
        Color flame, glow;
        switch (style.theme)
        {
            case DungeonTheme.Crystal:
                CrystalCluster(e.wall + e.inward * 0.12f + Vector3.up * 2f + e.side * R(-0.4f, 0.4f), true, e.inward, true);
                return;
            case DungeonTheme.DarkCorridor: flame = new Color(0.7f, 0.35f, 1f); glow = style.lightColor; break;
            case DungeonTheme.WetTunnel: flame = new Color(0.35f, 0.95f, 1f); glow = style.lightColor; break;
            case DungeonTheme.Ice: flame = new Color(0.5f, 0.85f, 1f); glow = style.lightColor; break;
            case DungeonTheme.Forest: flame = new Color(0.7f, 1f, 0.4f); glow = style.lightColor; break;
            case DungeonTheme.Volcano: flame = new Color(1f, 0.3f, 0.08f); glow = style.lightColor; break;
            case DungeonTheme.Starlit: flame = new Color(0.55f, 0.65f, 1f); glow = style.lightColor; break;
            case DungeonTheme.Steampunk: flame = new Color(0.6f, 0.88f, 1f); glow = new Color(0.62f, 0.85f, 1f); break;
            case DungeonTheme.Neon: flame = NeonArt.Pick(rng); glow = flame; break;
            default: flame = new Color(1f, 0.6f, 0.2f); glow = style.lightColor; break;
        }

        Vector3 p = e.wall + e.inward * 0.14f + Vector3.up * 2.1f;
        var rot = Quaternion.LookRotation(e.inward);
        Prim(PrimitiveType.Cube, e.wall + e.inward * 0.04f + Vector3.up * 2.1f, new Vector3(0.32f, 0.5f, 0.06f), new Color(0.12f, 0.1f, 0.09f), 0.3f, rot);
        Prim(PrimitiveType.Cylinder, p, new Vector3(0.1f, 0.3f, 0.1f), new Color(0.35f, 0.22f, 0.1f), 0.1f);
        Prim(PrimitiveType.Cylinder, p + Vector3.up * 0.28f, new Vector3(0.18f, 0.05f, 0.18f), new Color(0.15f, 0.13f, 0.12f), 0.4f);
        var fl = Prim(PrimitiveType.Sphere, p + Vector3.up * 0.46f, new Vector3(0.2f, 0.3f, 0.2f), flame, 0.2f, null, flame * 2.2f, false);

        Vector3 lp = p + Vector3.up * 0.5f + e.inward * 0.3f;
        if (LightOk(lp)) AddLight(fl.transform, lp, glow, 4.5f, 9f);
    }

    bool LightOk(Vector3 p)
    {
        if (lights.Count >= (dark ? 14 : MaxLights)) return false;
        foreach (var l in lights) if ((l - p).sqrMagnitude < LightSpacing * LightSpacing) return false;
        return true;
    }

    void AddLight(Transform parent, Vector3 worldLocalPos, Color color, float intensity, float range)
    {
        lights.Add(worldLocalPos);
        var go = new GameObject("Light");
        go.transform.SetParent(root, false);
        go.transform.localPosition = worldLocalPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        go.AddComponent<FlameFlicker>();
    }

    // ---------------------------------------------------------------- 小物

    void Stalagmite(Vector3 pos, float radius, float height)
    {
        Color c = style.theme == DungeonTheme.Crystal ? new Color(0.25f, 0.25f, 0.45f) : Color.Lerp(style.wallA, style.wallB, 0.5f);
        var go = MeshProp(ConeMesh(), pos, new Vector3(radius * 2f, height, radius * 2f), c, 0.1f);
        go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
        if (Chance(0.5f))
            MeshProp(ConeMesh(), pos + new Vector3(R(-0.3f, 0.3f), 0f, R(-0.3f, 0.3f)), new Vector3(radius * 1.2f, height * 0.6f, radius * 1.2f), c * 0.9f, 0.1f);
    }

    void Stalactite(Vector3 pos, float radius, float length, Color c)
    {
        MeshProp(ConeMesh(), pos, new Vector3(radius * 2f, length, radius * 2f), c, 0.15f, Quaternion.Euler(180f, R(0f, 360f), 0f), true);
        // 先端が下を向くよう、根元（天井）を原点に長さぶん下へ伸ばす（上下反転して、根元の位置が天井になる）
    }

    void Rocks(Vector3 pos) => Rocks(pos, Color.Lerp(style.wallA, style.wallB, 0.4f));

    void Rocks(Vector3 pos, Color c)
    {
        int n = rng.Next(2, 5);
        for (int i = 0; i < n; i++)
        {
            float s = R(0.15f, 0.45f);
            var g = Prim(PrimitiveType.Sphere, pos + new Vector3(R(-0.4f, 0.4f), s * 0.25f, R(-0.4f, 0.4f)), new Vector3(s * 1.3f, s * 0.8f, s), c * R(0.8f, 1.15f), 0.1f);
            g.transform.rotation = Quaternion.Euler(R(-15f, 15f), R(0f, 360f), R(-15f, 15f));
        }
    }

    void Rubble(Vector3 pos) => Rocks(pos, Color.Lerp(style.floorA, style.floorB, 0.5f));

    void Mushrooms(Vector3 pos, Color cap, bool glow)
    {
        int n = rng.Next(2, 5);
        for (int i = 0; i < n; i++)
        {
            float s = R(0.15f, 0.4f);
            Vector3 p = pos + new Vector3(R(-0.3f, 0.3f), 0f, R(-0.3f, 0.3f));
            Prim(PrimitiveType.Cylinder, p + Vector3.up * s * 0.5f, new Vector3(s * 0.25f, s * 0.5f, s * 0.25f), new Color(0.8f, 0.78f, 0.7f), 0.1f);
            Prim(PrimitiveType.Sphere, p + Vector3.up * s, new Vector3(s * 1.2f, s * 0.6f, s * 1.2f), cap, 0.3f, null, glow ? cap * 1.6f : (Color?)null, true);
        }
    }

    void Bones(Vector3 pos)
    {
        var bone = new Color(0.85f, 0.82f, 0.72f);
        int n = rng.Next(2, 5);
        for (int i = 0; i < n; i++)
        {
            var g = Prim(PrimitiveType.Cylinder, pos + new Vector3(R(-0.4f, 0.4f), 0.05f, R(-0.4f, 0.4f)), new Vector3(0.06f, R(0.2f, 0.4f), 0.06f), bone, 0.1f);
            g.transform.rotation = Quaternion.Euler(90f, R(0f, 360f), 0f);
        }
        Prim(PrimitiveType.Sphere, pos + new Vector3(R(-0.2f, 0.2f), 0.12f, R(-0.2f, 0.2f)), new Vector3(0.22f, 0.24f, 0.22f), bone, 0.15f);
    }

    void Skulls(Vector3 pos)
    {
        var bone = new Color(0.82f, 0.8f, 0.7f);
        int n = rng.Next(3, 7);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = pos + new Vector3(R(-0.35f, 0.35f), 0.11f + (i > 3 ? 0.2f : 0f), R(-0.35f, 0.35f));
            Prim(PrimitiveType.Sphere, p, new Vector3(0.22f, 0.24f, 0.22f), bone, 0.15f);
            Prim(PrimitiveType.Cube, p + new Vector3(0f, -0.02f, 0.09f), new Vector3(0.12f, 0.05f, 0.05f), new Color(0.1f, 0.08f, 0.08f), 0.1f).transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
        }
    }

    void Candles(Vector3 pos)
    {
        int n = rng.Next(2, 5);
        for (int i = 0; i < n; i++)
        {
            float ht = R(0.1f, 0.3f);
            Vector3 p = pos + new Vector3(R(-0.3f, 0.3f), 0f, R(-0.3f, 0.3f));
            Prim(PrimitiveType.Cylinder, p + Vector3.up * ht * 0.5f, new Vector3(0.07f, ht * 0.5f, 0.07f), new Color(0.92f, 0.9f, 0.8f), 0.2f);
            Prim(PrimitiveType.Sphere, p + Vector3.up * (ht + 0.05f), new Vector3(0.06f, 0.1f, 0.06f), new Color(1f, 0.7f, 0.3f), 0.2f, null, new Color(1f, 0.6f, 0.2f) * 2.5f, false);
        }
    }

    void Crate(Vector3 pos)
    {
        float s = R(0.5f, 0.85f);
        var wood = new Color(0.4f, 0.27f, 0.14f) * R(0.8f, 1.1f);
        var g = Prim(PrimitiveType.Cube, pos + Vector3.up * s * 0.5f, new Vector3(s, s, s), wood, 0.1f);
        g.transform.rotation = Quaternion.Euler(0f, R(0f, 90f), 0f);
        // 帯
        Prim(PrimitiveType.Cube, pos + Vector3.up * s * 0.5f, new Vector3(s * 1.02f, s * 0.12f, s * 1.02f), wood * 0.6f, 0.1f).transform.rotation = g.transform.rotation;
        if (Chance(0.4f)) Prim(PrimitiveType.Cube, pos + new Vector3(0.1f, s + 0.2f, 0.05f), new Vector3(s * 0.6f, s * 0.4f, s * 0.6f), wood * 0.9f, 0.1f).transform.rotation = Quaternion.Euler(0f, R(0f, 90f), 0f);
    }

    void Barrel(Vector3 pos, Color c)
    {
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.45f, new Vector3(0.7f, 0.45f, 0.7f), c, 0.15f);
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.25f, new Vector3(0.74f, 0.03f, 0.74f), new Color(0.15f, 0.13f, 0.12f), 0.4f);
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.7f, new Vector3(0.74f, 0.03f, 0.74f), new Color(0.15f, 0.13f, 0.12f), 0.4f);
    }

    void Puddle(Vector3 pos)
    {
        float s = R(0.8f, 1.8f);
        var c = style.theme == DungeonTheme.WetTunnel ? new Color(0.1f, 0.25f, 0.3f)
                : style.theme == DungeonTheme.Ice ? new Color(0.6f, 0.82f, 0.95f) : new Color(0.15f, 0.2f, 0.25f);
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.015f, new Vector3(s, 0.01f, s * R(0.6f, 1f)), c, 0.95f);
    }

    /// <summary>壁際の本棚（木の棚に、色とりどりの本）。</summary>
    void Bookshelf(Edge e)
    {
        Vector3 b = e.wall + e.inward * 0.3f + e.side * R(-0.8f, 0.8f);
        var rot = Quaternion.LookRotation(e.inward);
        var wood = new Color(0.25f, 0.15f, 0.08f);
        Prim(PrimitiveType.Cube, b + Vector3.up * 1.4f, new Vector3(2.0f, 2.8f, 0.5f), wood, 0.2f, rot);
        for (int s = 0; s < 4; s++)
        {
            float y = 0.45f + s * 0.65f;
            Prim(PrimitiveType.Cube, b + Vector3.up * (y - 0.28f) + e.inward * 0.12f, new Vector3(1.9f, 0.05f, 0.4f), wood * 1.3f, 0.2f, rot);
            for (int i = 0; i < 7; i++)
            {
                float bh = R(0.32f, 0.52f);
                var c = Color.HSVToRGB(R(0f, 1f), R(0.3f, 0.6f), R(0.25f, 0.5f));
                Prim(PrimitiveType.Cube, b + e.side * (-0.78f + i * 0.26f) + Vector3.up * (y - 0.25f + bh * 0.5f) + e.inward * 0.28f, new Vector3(0.2f, bh, 0.28f), c, 0.2f, rot);
            }
        }
    }

    void Pipe(Edge e)
    {
        Vector3 p = e.wall + e.inward * 0.18f + Vector3.up * R(2.4f, 3.4f);
        var g = Prim(PrimitiveType.Cylinder, p, new Vector3(0.18f, 1.8f, 0.18f), new Color(0.3f, 0.28f, 0.24f), 0.5f);
        g.transform.rotation = Quaternion.LookRotation(e.side) * Quaternion.Euler(90f, 0f, 0f);
        Prim(PrimitiveType.Cylinder, p + Vector3.down * 0.5f + e.side * 0.8f, new Vector3(0.02f, 0.5f, 0.02f), new Color(0.35f, 0.6f, 0.75f), 0.9f);
    }

    void Vines(Vector3 pos)
    {
        int n = rng.Next(3, 6);
        for (int i = 0; i < n; i++)
        {
            float len = R(0.8f, 2.4f);
            var c = new Color(0.12f, R(0.3f, 0.45f), 0.12f);
            Prim(PrimitiveType.Cylinder, pos + new Vector3(R(-0.3f, 0.3f), -len * 0.5f + (pos.y > 1f ? 0f : len), R(-0.3f, 0.3f)),
                 new Vector3(0.04f, len * 0.5f, 0.04f), c, 0.2f);
        }
    }

    void Cobweb(Vector3 pos, Vector3 diag)
    {
        var c = new Color(0.8f, 0.8f, 0.85f);
        for (int i = 0; i < 3; i++)
        {
            var g = Prim(PrimitiveType.Cylinder, pos + Vector3.up * (h - 0.5f) + diag * 0.1f, new Vector3(0.012f, 0.7f, 0.012f), c, 0.1f);
            g.transform.rotation = Quaternion.LookRotation(diag) * Quaternion.Euler(R(40f, 140f), R(-50f, 50f), R(0f, 180f));
        }
    }

    void OreVein(Edge e)
    {
        var glow = style.vein.a > 0f ? style.vein : new Color(1f, 0.5f, 0.15f);
        Vector3 b = e.wall + e.inward * 0.06f + Vector3.up * R(0.8f, 2.8f) + e.side * R(-1.4f, 1.4f);
        int n = rng.Next(3, 7);
        for (int i = 0; i < n; i++)
        {
            float s = R(0.1f, 0.3f);
            var g = Prim(PrimitiveType.Cube, b + e.side * R(-0.4f, 0.4f) + Vector3.up * R(-0.3f, 0.3f), new Vector3(s, s * 1.4f, s * 0.6f), glow * 0.7f, 0.5f, null, glow * 2.6f, true);
            g.transform.rotation = Quaternion.LookRotation(e.inward) * Quaternion.Euler(R(-20f, 20f), R(-30f, 30f), R(0f, 90f));
        }
    }

    void Pickaxe(Edge e)
    {
        Vector3 p = e.wall + e.inward * 0.3f + e.side * R(-1.2f, 1.2f) + Vector3.up * 0.7f;
        var rot = Quaternion.LookRotation(e.inward) * Quaternion.Euler(-12f, 0f, R(-8f, 8f));
        Prim(PrimitiveType.Cylinder, p, new Vector3(0.06f, 0.7f, 0.06f), new Color(0.4f, 0.27f, 0.14f), 0.1f, rot * Quaternion.Euler(0f, 0f, 0f) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(-90f, 0f, 0f));
        Prim(PrimitiveType.Cube, p + Vector3.up * 0.68f, new Vector3(0.5f, 0.07f, 0.07f), new Color(0.5f, 0.5f, 0.55f), 0.6f, rot);
    }

    void MineCart(Vector3 pos, Vector3 side)
    {
        var rot = Quaternion.LookRotation(side);
        var iron = new Color(0.25f, 0.23f, 0.22f);
        Prim(PrimitiveType.Cube, pos + Vector3.up * 0.5f, new Vector3(1.1f, 0.6f, 0.8f), iron, 0.4f, rot);
        Prim(PrimitiveType.Cube, pos + Vector3.up * 0.82f, new Vector3(0.9f, 0.18f, 0.6f), new Color(0.8f, 0.5f, 0.15f), 0.4f, rot, new Color(0.6f, 0.3f, 0.05f), true);
        foreach (float sx in new[] { -0.4f, 0.4f })
            foreach (float sz in new[] { -0.4f, 0.4f })
            {
                var g = Prim(PrimitiveType.Cylinder, pos + rot * new Vector3(sz, 0.17f, sx), new Vector3(0.3f, 0.03f, 0.3f), new Color(0.12f, 0.12f, 0.12f), 0.4f);
                g.transform.rotation = rot * Quaternion.Euler(0f, 0f, 90f);
            }
    }

    void MineSupport(Vector3 cell, Vector3 axis)
    {
        var wood = new Color(0.36f, 0.24f, 0.12f);
        float half = cs * 0.5f - 0.15f;
        Prim(PrimitiveType.Cube, cell + axis * half + Vector3.up * h * 0.5f, new Vector3(0.28f, h, 0.28f), wood, 0.1f);
        Prim(PrimitiveType.Cube, cell - axis * half + Vector3.up * h * 0.5f, new Vector3(0.28f, h, 0.28f), wood, 0.1f);
        var beam = Prim(PrimitiveType.Cube, cell + Vector3.up * (h - 0.3f), new Vector3(0.3f, 0.3f, cs), wood * 0.9f, 0.1f);
        beam.transform.rotation = Quaternion.LookRotation(axis);
        if (Chance(0.6f)) HangLantern(cell + Vector3.up * (h - 0.45f));
    }

    void Arch(Vector3 cell, Vector3 axis)
    {
        var stone = Color.Lerp(style.wallA, style.wallB, 0.6f);
        float half = cs * 0.5f - 0.3f;
        Prim(PrimitiveType.Cube, cell + axis * half + Vector3.up * h * 0.5f, new Vector3(0.6f, h, 0.6f), stone, 0.2f);
        Prim(PrimitiveType.Cube, cell - axis * half + Vector3.up * h * 0.5f, new Vector3(0.6f, h, 0.6f), stone, 0.2f);
        var beam = Prim(PrimitiveType.Cube, cell + Vector3.up * (h - 0.35f), new Vector3(0.7f, 0.7f, cs), stone * 0.9f, 0.2f);
        beam.transform.rotation = Quaternion.LookRotation(axis);
    }

    void HangLantern(Vector3 ceilingPos, Color? color = null)
    {
        Color c = color ?? new Color(1f, 0.65f, 0.25f);
        Prim(PrimitiveType.Cylinder, ceilingPos + Vector3.down * 0.3f, new Vector3(0.025f, 0.3f, 0.025f), new Color(0.15f, 0.13f, 0.12f), 0.4f);
        Prim(PrimitiveType.Cube, ceilingPos + Vector3.down * 0.75f, new Vector3(0.28f, 0.32f, 0.28f), c, 0.3f, null, c * 2f, false);
        Prim(PrimitiveType.Cube, ceilingPos + Vector3.down * 0.55f, new Vector3(0.34f, 0.06f, 0.34f), new Color(0.12f, 0.1f, 0.09f), 0.4f);
        Vector3 lp = ceilingPos + Vector3.down * 0.8f;
        if (LightOk(lp)) AddLight(null, lp, c, 4f, 9f);
    }

    void Chain(Vector3 ceilingPos)
    {
        float len = R(0.8f, 2.2f);
        var iron = new Color(0.22f, 0.21f, 0.22f);
        Prim(PrimitiveType.Cylinder, ceilingPos + Vector3.down * len * 0.5f, new Vector3(0.04f, len * 0.5f, 0.04f), iron, 0.5f);
        if (Chance(0.4f))
            Prim(PrimitiveType.Cylinder, ceilingPos + Vector3.down * len, new Vector3(0.5f, 0.03f, 0.5f), iron, 0.5f);
        else
            Prim(PrimitiveType.Sphere, ceilingPos + Vector3.down * len, new Vector3(0.14f, 0.2f, 0.14f), iron, 0.5f);
    }

    void Coffin(Edge e)
    {
        var wood = new Color(0.2f, 0.12f, 0.08f);
        var rot = Quaternion.LookRotation(e.side);
        Vector3 p = e.wall + e.inward * 0.7f + e.side * R(-0.8f, 0.8f);
        Prim(PrimitiveType.Cube, p + Vector3.up * 0.3f, new Vector3(0.7f, 0.6f, 1.9f), wood, 0.2f, rot * Quaternion.Euler(0f, 90f, 0f));
        Prim(PrimitiveType.Cube, p + Vector3.up * 0.63f, new Vector3(0.78f, 0.1f, 2f), wood * 1.3f, 0.2f, rot * Quaternion.Euler(0f, 90f, 0f));
    }

    void Banner(Edge e, Color c)
    {
        Vector3 p = e.wall + e.inward * 0.06f + Vector3.up * 2.3f + e.side * R(-1.2f, 1.2f);
        var rot = Quaternion.LookRotation(e.inward);
        Prim(PrimitiveType.Cube, p + Vector3.up * 0.9f, new Vector3(0.9f, 0.06f, 0.08f), new Color(0.25f, 0.18f, 0.1f), 0.3f, rot);
        Prim(PrimitiveType.Cube, p, new Vector3(0.75f, 1.7f, 0.03f), c, 0.1f, rot);
        Prim(PrimitiveType.Cube, p + e.inward * 0.02f, new Vector3(0.22f, 0.5f, 0.03f), Color.Lerp(c, new Color(0.95f, 0.8f, 0.3f), 0.8f), 0.3f, rot);
    }

    void Pillar(Vector3 pos, float radius)
    {
        var stone = Color.Lerp(style.wallA, style.wallB, 0.7f);
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.2f, new Vector3(radius * 2.6f, 0.2f, radius * 2.6f), stone * 0.9f, 0.2f);
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * h * 0.5f, new Vector3(radius * 2f, h * 0.5f, radius * 2f), stone, 0.2f);
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * (h - 0.2f), new Vector3(radius * 2.6f, 0.2f, radius * 2.6f), stone * 0.9f, 0.2f);
    }

    void Brazier(Vector3 pos) => Brazier(pos, new Color(1f, 0.55f, 0.15f));

    void Brazier(Vector3 pos, Color flame)
    {
        var iron = new Color(0.14f, 0.13f, 0.13f);
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.45f, new Vector3(0.1f, 0.45f, 0.1f), iron, 0.5f);
        Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.95f, new Vector3(0.6f, 0.1f, 0.6f), iron, 0.5f);
        var f = Prim(PrimitiveType.Sphere, pos + Vector3.up * 1.15f, new Vector3(0.4f, 0.5f, 0.4f), flame, 0.2f, null, flame * 2.4f, false);
        Vector3 lp = pos + Vector3.up * 1.5f;
        if (LightOk(lp)) AddLight(f.transform, lp, Color.Lerp(flame, Color.white, 0.2f), 5f, 10f);
    }

    void Weapons(Edge e)
    {
        Vector3 p = e.wall + e.inward * 0.1f + Vector3.up * 2.0f + e.side * R(-1.2f, 1.2f);
        var rot = Quaternion.LookRotation(e.inward);
        var metal = new Color(0.55f, 0.57f, 0.6f);
        Prim(PrimitiveType.Cube, p, new Vector3(0.09f, 1.1f, 0.03f), metal, 0.7f, rot * Quaternion.Euler(0f, 0f, 35f));
        Prim(PrimitiveType.Cube, p, new Vector3(0.09f, 1.1f, 0.03f), metal, 0.7f, rot * Quaternion.Euler(0f, 0f, -35f));
        Prim(PrimitiveType.Cylinder, p + e.inward * 0.03f, new Vector3(0.6f, 0.02f, 0.6f), new Color(0.5f, 0.12f, 0.1f), 0.4f, rot * Quaternion.Euler(90f, 0f, 0f));
    }

    void CrystalCluster(Vector3 pos, bool onWallOrCeiling, Vector3? facing = null, bool small = false)
    {
        Color[] palette =
        {
            new Color(0.6f, 0.4f, 1f), new Color(0.35f, 0.8f, 1f), new Color(1f, 0.45f, 0.85f), new Color(0.45f, 1f, 0.8f),
        };
        Color c = palette[rng.Next(palette.Length)];
        int n = small ? rng.Next(2, 4) : rng.Next(3, 7);

        Quaternion baseRot = Quaternion.identity;
        if (onWallOrCeiling && facing.HasValue)
        {
            // 根元を面に向け、先端が部屋のほうへ伸びる
            Vector3 f = facing.Value;
            baseRot = Quaternion.FromToRotation(Vector3.up, f);
        }

        for (int i = 0; i < n; i++)
        {
            float s = (small ? 0.5f : 1f) * R(0.3f, 1.1f);
            Quaternion tilt = Quaternion.Euler(R(-25f, 25f), R(0f, 360f), R(-25f, 25f));
            Vector3 off = new Vector3(R(-0.3f, 0.3f), 0f, R(-0.3f, 0.3f));
            var g = MeshProp(CrystalMesh(), pos + baseRot * off, new Vector3(0.28f * s * 1.4f, 0.6f * s * 1.6f, 0.28f * s * 1.4f), c, 0.7f, baseRot * tilt, false, c * 2.4f);
        }
        Vector3 lp = pos + (facing.HasValue ? facing.Value * 0.6f : Vector3.up * 0.6f);
        if (!small && LightOk(lp)) AddLight(null, lp, c, 3.5f, 8f);
    }

    // ---------------------------------------------------------------- 生成の部品

    GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Color color, float smooth, Quaternion? rot = null, Color? emission = null, bool isStatic = true)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        go.GetComponent<Renderer>().sharedMaterial = Mat(color, smooth, emission);
        go.isStatic = isStatic;
        return go;
    }

    GameObject MeshProp(Mesh mesh, Vector3 pos, Vector3 scale, Color color, float smooth, Quaternion? rot = null, bool hanging = false, Color? emission = null)
    {
        var go = new GameObject("Prop");
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = Mat(color, smooth, emission);
        go.isStatic = true;
        return go;
    }

    Material Mat(Color color, float smooth, Color? emission)
    {
        string key = $"{color.r:0.00},{color.g:0.00},{color.b:0.00},{smooth:0.0},{(emission.HasValue ? emission.Value.r + emission.Value.g * 3f + emission.Value.b * 7f : 0f):0.00}";
        if (mats.TryGetValue(key, out var m)) return m;

        if (baseMat != null) m = new Material(baseMat);
        else m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        m.mainTexture = null;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
        }
        mats[key] = m;
        return m;
    }

    // ---------------------------------------------------------------- メッシュ（円錐・水晶）

    static Mesh cone, crystal;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cone = null; crystal = null; }

    static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        // 面の向きが外側になるように、巻き順を決める
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { var tmp = b; b = c; c = tmp; }
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c);
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }

    /// <summary>円錐（底面の中心が原点、先端が y=1、半径 0.5）。</summary>
    static Mesh ConeMesh()
    {
        if (cone != null) return cone;
        var v = new List<Vector3>(); var t = new List<int>();
        const int sides = 10;
        Vector3 apex = new Vector3(0f, 1f, 0f);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 b0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f);
            Vector3 b1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
            Vector3 mid = (b0 + b1) * 0.5f + Vector3.up * 0.3f;
            Tri(v, t, apex, b0, b1, mid);
        }
        cone = new Mesh { name = "DecorCone" };
        cone.SetVertices(v); cone.SetTriangles(t, 0);
        cone.RecalculateNormals(); cone.RecalculateBounds();
        return cone;
    }

    /// <summary>六角柱＋尖った先端の水晶（底面の中心が原点、先端が y=1、半径 0.5）。</summary>
    static Mesh CrystalMesh()
    {
        if (crystal != null) return crystal;
        var v = new List<Vector3>(); var t = new List<int>();
        const int sides = 6;
        float mid = 0.6f;
        Vector3 apex = new Vector3(0f, 1f, 0f);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 b0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f);
            Vector3 b1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
            Vector3 m0 = b0 + Vector3.up * mid, m1 = b1 + Vector3.up * mid;
            Vector3 outward = (b0 + b1) * 0.5f + Vector3.up * 0.2f;
            Tri(v, t, b0, b1, m1, outward);
            Tri(v, t, b0, m1, m0, outward);
            Tri(v, t, m0, m1, apex, (b0 + b1) * 0.5f + Vector3.up * 0.5f);
        }
        crystal = new Mesh { name = "DecorCrystal" };
        crystal.SetVertices(v); crystal.SetTriangles(t, 0);
        crystal.RecalculateNormals(); crystal.RecalculateBounds();
        return crystal;
    }
}
