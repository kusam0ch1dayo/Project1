using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 拠点の素材屋。Eで話しかけると、素材の購入・売却の画面（MaterialShopUI）が開く。見た目は Start でコード生成する。
/// このオブジェクトの forward がお客さん側。店主はカウンターの奥に立つ。
/// ホームのシーンに置かれていなくても、ゲーム開始時に自動で建つ（MaterialShopSpawner）。
/// </summary>
public class MaterialShop : MonoBehaviour, IInteractable
{
    public string Prompt => "E : 素材屋に話しかける（素材の売買）";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<MaterialShopUI>();
        if (ui == null) ui = interactor.AddComponent<MaterialShopUI>();
        ui.Open();
    }

    void Start()
    {
        var wood = new Color(0.34f, 0.24f, 0.14f);
        var top = new Color(0.5f, 0.38f, 0.22f);
        var apron = new Color(0.25f, 0.45f, 0.38f);

        Part(PrimitiveType.Cube, "Counter", new Vector3(0f, 0.5f, 0f), new Vector3(3.2f, 1f, 0.8f), wood, true);
        Part(PrimitiveType.Cube, "CounterTop", new Vector3(0f, 1.03f, 0f), new Vector3(3.4f, 0.08f, 1.0f), top, false);

        // カウンターの上: 素材のサンプル（核・角・羽）と小さな秤
        foreach (var (name, x, color, s) in new[]
        {
            ("Core", -1.1f, new Color(0.4f, 0.9f, 0.5f), 0.22f),
            ("Core2", -0.8f, new Color(0.4f, 0.9f, 0.5f), 0.16f),
            ("Wing", 0.1f, new Color(0.55f, 0.3f, 0.5f), 0.3f),
            ("Gem", 1.0f, new Color(1f, 0.85f, 0.3f), 0.18f),
        })
        {
            var p = Part(name == "Wing" ? PrimitiveType.Cube : PrimitiveType.Sphere, name, new Vector3(x, 1.2f, 0.05f),
                name == "Wing" ? new Vector3(0.4f, 0.03f, 0.25f) : Vector3.one * s, color, false);
            var mat = p.GetComponent<Renderer>().material;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 0.5f);
        }
        Part(PrimitiveType.Cylinder, "ScalePost", new Vector3(0.55f, 1.2f, 0f), new Vector3(0.05f, 0.15f, 0.05f), new Color(0.7f, 0.65f, 0.3f), false);
        Part(PrimitiveType.Cube, "ScaleBeam", new Vector3(0.55f, 1.36f, 0f), new Vector3(0.5f, 0.03f, 0.03f), new Color(0.7f, 0.65f, 0.3f), false);

        // 店の脇: 素材を入れた樽と麻袋
        Part(PrimitiveType.Cylinder, "Barrel", new Vector3(-2.0f, 0.5f, 0.2f), new Vector3(0.8f, 0.5f, 0.8f), wood * 1.2f, true);
        Part(PrimitiveType.Sphere, "Sack1", new Vector3(2.0f, 0.4f, 0.1f), new Vector3(0.8f, 0.8f, 0.8f), new Color(0.65f, 0.55f, 0.35f), false);
        Part(PrimitiveType.Sphere, "Sack2", new Vector3(2.5f, 0.3f, 0.4f), new Vector3(0.6f, 0.6f, 0.6f), new Color(0.6f, 0.5f, 0.32f), false);
        // 壁に吊るした素材（角・骨）
        Part(PrimitiveType.Cube, "HangBar", new Vector3(0f, 2.55f, -1.0f), new Vector3(3.0f, 0.06f, 0.06f), wood, false);
        for (int i = 0; i < 5; i++)
        {
            float x = -1.2f + i * 0.6f;
            var color = i % 2 == 0 ? new Color(0.92f, 0.9f, 0.8f) : new Color(0.55f, 0.75f, 0.3f);
            Part(PrimitiveType.Cylinder, "Hang" + i, new Vector3(x, 2.25f, -1.0f), new Vector3(0.1f, 0.25f, 0.1f), color, false);
        }

        // 店主（エプロン・頭・ゴーグル）
        Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.95f, -0.9f), new Vector3(0.75f, 0.95f, 0.75f), apron, false);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.0f, -0.9f), new Vector3(0.45f, 0.45f, 0.45f), new Color(0.9f, 0.75f, 0.6f), false);
        Part(PrimitiveType.Cube, "Goggles", new Vector3(0f, 2.05f, -0.68f), new Vector3(0.42f, 0.1f, 0.06f), new Color(0.2f, 0.7f, 0.9f), false);
        Part(PrimitiveType.Cylinder, "Cap", new Vector3(0f, 2.27f, -0.9f), new Vector3(0.5f, 0.05f, 0.5f), apron * 0.7f, false);

        // 看板
        Part(PrimitiveType.Cube, "Sign", new Vector3(0f, 3.0f, -1.0f), new Vector3(2.2f, 0.6f, 0.08f), wood, false);
        var textGo = new GameObject("SignText");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, 3.0f, -0.94f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = "素材屋";
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.7f, 1f, 0.8f);
        textGo.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;

        var lightGo = new GameObject("ShopLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.6f, 0.5f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.8f, 1f, 0.85f);
        light.range = 7f;
        light.intensity = 7f;
    }

    GameObject Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }
}

/// <summary>ホームのシーンに、素材屋が無ければ建てる（シーンの作り直しは不要）。</summary>
public static class MaterialShopSpawner
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (SceneManager.GetActiveScene().name == SceneNames.Home) Spawn();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneNames.Home) Spawn();
    }

    /// <summary>東の壁、アイテムショップの南隣。</summary>
    public static readonly Vector3 Position = new Vector3(10.8f, 0f, -2.2f);
    public static readonly Quaternion Rotation = Quaternion.Euler(0f, -90f, 0f);

    static void Spawn()
    {
        if (Object.FindFirstObjectByType<MaterialShop>() != null) return;
        var go = new GameObject("MaterialShop");
        go.transform.SetPositionAndRotation(Position, Rotation);
        go.AddComponent<MaterialShop>();
    }
}
