using UnityEngine;

/// <summary>
/// 拠点の鑑定屋。Eで話しかけると、鑑定屋の画面（AppraiserUI）が開く。見た目は Start でコード生成する。
/// このオブジェクトの forward がお客さん側。店主はカウンターの奥に立つ。
/// </summary>
public class Appraiser : MonoBehaviour, IInteractable
{
    public string Prompt => "E : 鑑定屋に話しかける（魔晶覚醒・強化）";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<AppraiserUI>();
        if (ui == null) ui = interactor.AddComponent<AppraiserUI>();
        ui.Open();
    }

    void Start()
    {
        var wood = new Color(0.28f, 0.2f, 0.3f);
        var top = new Color(0.4f, 0.3f, 0.45f);
        var robe = new Color(0.3f, 0.2f, 0.5f);

        Part(PrimitiveType.Cube, "Counter", new Vector3(0f, 0.5f, 0f), new Vector3(3.2f, 1f, 0.8f), wood, true);
        Part(PrimitiveType.Cube, "CounterTop", new Vector3(0f, 1.03f, 0f), new Vector3(3.4f, 0.08f, 1.0f), top, false);

        // カウンターの上: 水晶玉・拡大鏡・魔法石
        var ball = Part(PrimitiveType.Sphere, "CrystalBall", new Vector3(0f, 1.3f, 0f), new Vector3(0.45f, 0.45f, 0.45f), new Color(0.75f, 0.55f, 1f), false);
        var mat = ball.GetComponent<Renderer>().material;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.6f, 0.3f, 1f) * 1.5f);
        Part(PrimitiveType.Cylinder, "BallStand", new Vector3(0f, 1.11f, 0f), new Vector3(0.3f, 0.03f, 0.3f), new Color(0.8f, 0.7f, 0.3f), false);
        Part(PrimitiveType.Cylinder, "Lens", new Vector3(-0.9f, 1.1f, 0.05f), new Vector3(0.28f, 0.015f, 0.28f), new Color(0.7f, 0.9f, 1f), false);
        Part(PrimitiveType.Cube, "Gem1", new Vector3(0.8f, 1.12f, 0.1f), new Vector3(0.14f, 0.14f, 0.14f), ElementUtil.Color(Element.Fire), false).transform.rotation = Quaternion.Euler(30f, 20f, 15f);
        Part(PrimitiveType.Cube, "Gem2", new Vector3(1.05f, 1.12f, -0.05f), new Vector3(0.14f, 0.14f, 0.14f), ElementUtil.Color(Element.Water), false).transform.rotation = Quaternion.Euler(10f, 50f, 40f);
        Part(PrimitiveType.Cube, "Gem3", new Vector3(0.95f, 1.12f, 0.25f), new Vector3(0.14f, 0.14f, 0.14f), ElementUtil.Color(Element.Wind), false).transform.rotation = Quaternion.Euler(45f, 10f, 5f);

        // 店主（ローブ・頭・とんがり帽子）
        Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.95f, -0.9f), new Vector3(0.75f, 0.95f, 0.75f), robe, false);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.0f, -0.9f), new Vector3(0.45f, 0.45f, 0.45f), new Color(0.9f, 0.75f, 0.6f), false);
        Part(PrimitiveType.Cylinder, "HatBrim", new Vector3(0f, 2.25f, -0.9f), new Vector3(0.7f, 0.04f, 0.7f), robe * 0.8f, false);
        Part(PrimitiveType.Cylinder, "HatTop", new Vector3(0f, 2.55f, -0.9f), new Vector3(0.32f, 0.3f, 0.32f), robe * 0.8f, false);

        // 看板
        Part(PrimitiveType.Cube, "Sign", new Vector3(0f, 3.0f, -1.0f), new Vector3(2.2f, 0.6f, 0.08f), wood, false);
        var textGo = new GameObject("SignText");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, 3.0f, -0.94f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = "鑑定屋";
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.9f, 0.7f, 1f);
        textGo.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;

        var lightGo = new GameObject("ShopLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.2f, 0.5f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.7f, 0.5f, 1f);
        light.range = 6f;
        light.intensity = 6f;
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
