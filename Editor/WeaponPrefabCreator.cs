using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 武器プレハブの「参考用」サンプル（通常の剣）を Assets/Prefabs/Weapons に生成する。
/// 自作の武器プレハブは、これと同じ向き・基準点で作ると、位置合わせ無しでそのまま持たせられる。
/// </summary>
public static class WeaponPrefabCreator
{
    public const string PrefabDir = "Assets/Prefabs/Weapons";
    const string MaterialDir = "Assets/Materials/Weapons";
    public const string ReferenceSwordPath = PrefabDir + "/Sword_Reference.prefab";

    const string Readme =
@"■ 武器プレハブの作り方（Dungeon > Item Creator の「武器の見た目」で指定します）

【向き・基準点の決まり】Sword_Reference.prefab と同じにしてください。
  ・原点 (0,0,0)  : 手で握る位置（グリップの中心あたり）
  ・+Z 方向      : 刃先の向き（剣は前方へ伸びる）
  ・X 軸         : 刃の幅の方向。刃(エッジ)は左右(±X)に付く。
  ・Y 軸         : 刃の厚み方向。平らな面は上下(±Y)を向く。
  ・大きさ        : 1 ユニット = 1m。標準の剣は、刃の長さ約1.0m。

【ゲーム内での扱い】
  ・一人称の画面で、右下に構えた状態で表示され、攻撃時は右から左へ横薙ぎに振られます。
    横薙ぎのとき、刃の細い面(エッジ)が進行方向になる向きが正しい向きです。
  ・プレハブに付いている Collider / Rigidbody はゲーム内では自動で取り除かれます（見た目専用）。
  ・位置・回転・倍率がずれるときは、Item Creator の「位置 / 回転 / 倍率」で調整できます。
    （プレビューで、一人称視点での見え方を確認できます）
";

    [MenuItem("Dungeon/Create Reference Weapon Prefab")]
    static void MenuCreate()
    {
        var prefab = CreateReferenceSword(true);
        if (prefab != null)
        {
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
        }
    }

    /// <summary>参考用の剣プレハブを生成する。overwrite=false で既に存在すれば、そのまま返す。</summary>
    public static GameObject CreateReferenceSword(bool overwrite)
    {
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(MaterialDir);

        string readmePath = PrefabDir + "/README.txt";
        if (overwrite || !File.Exists(readmePath))
            File.WriteAllText(readmePath, Readme, new UTF8Encoding(true));

        if (!overwrite && File.Exists(ReferenceSwordPath))
            return AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceSwordPath);

        var blade = GetOrCreateMaterial("Sword_Blade", new Color(0.8f, 0.83f, 0.88f), 0.85f, 0.9f);
        var gold = GetOrCreateMaterial("Sword_Gold", new Color(0.85f, 0.68f, 0.2f), 0.7f, 0.8f);
        var leather = GetOrCreateMaterial("Sword_Grip", new Color(0.25f, 0.15f, 0.08f), 0f, 0.2f);

        var root = new GameObject("Sword_Reference");

        // 刃（幅0.14 x 厚み0.03 x 長さ1.0）。z = 0.08 〜 1.08
        Part(root, PrimitiveType.Cube, "Blade", new Vector3(0f, 0f, 0.58f), Vector3.zero, new Vector3(0.14f, 0.03f, 1.0f), blade);
        // 切っ先（Y軸45度回転した小さな立方体で、尖った先端にする）
        Part(root, PrimitiveType.Cube, "Tip", new Vector3(0f, 0f, 1.08f), new Vector3(0f, 45f, 0f), new Vector3(0.099f, 0.03f, 0.099f), blade);
        // 鍔
        Part(root, PrimitiveType.Cube, "Guard", new Vector3(0f, 0f, 0.04f), Vector3.zero, new Vector3(0.34f, 0.05f, 0.07f), gold);
        // 握り（円柱を寝かせる）
        Part(root, PrimitiveType.Cylinder, "Grip", new Vector3(0f, 0f, -0.15f), new Vector3(90f, 0f, 0f), new Vector3(0.055f, 0.14f, 0.055f), leather);
        // 柄頭
        Part(root, PrimitiveType.Sphere, "Pommel", new Vector3(0f, 0f, -0.32f), Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f), gold);

        var saved = PrefabUtility.SaveAsPrefabAsset(root, ReferenceSwordPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("参考用の剣プレハブを生成しました: " + ReferenceSwordPath);
        return saved;
    }

    static void Part(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static Material GetOrCreateMaterial(string name, Color color, float metallic, float smoothness)
    {
        string path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
