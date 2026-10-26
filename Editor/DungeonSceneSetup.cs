using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// メニュー「Dungeon」から、拠点(Home)とダンジョン(Dungeon)のシーン、マテリアル、アイテムを自動作成する。
/// </summary>
public static class DungeonSceneSetup
{
    const string MaterialDir = "Assets/Materials";
    const string SceneDir = "Assets/Scenes";
    static string HomePath => $"{SceneDir}/{SceneNames.Home}.unity";
    static string DungeonPath => $"{SceneDir}/{SceneNames.Dungeon}.unity";

    // ---------------------------------------------------------------- メニュー

    [MenuItem("Dungeon/Create All Scenes")]
    public static void CreateAllScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        CreateDungeonSceneInternal();
        CreateHomeSceneInternal();
        UpdateBuildSettings();
        EditorSceneManager.OpenScene(HomePath);
        Debug.Log("Home / Dungeon シーンを作成しました。Home を開いて ▶ で遊べます。");
    }

    [MenuItem("Dungeon/Create Dungeon Scene")]
    public static void CreateDungeonScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        CreateDungeonSceneInternal();
        UpdateBuildSettings();
    }

    [MenuItem("Dungeon/Create Home Scene")]
    public static void CreateHomeScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        CreateHomeSceneInternal();
        UpdateBuildSettings();
    }

    [MenuItem("Dungeon/Delete Save Data")]
    public static void DeleteSave()
    {
        SaveSystem.Delete();
        Debug.Log("セーブデータを削除しました: " + SaveSystem.FilePath);
    }

    // ---------------------------------------------------------------- ダンジョン

    static void CreateDungeonSceneInternal()
    {
        ItemAssetCreator.CreateAll();
        EnemyAssetCreator.CreateAll(); // サンプルの敵・ボス・ダンジョン（すでにあるものは上書きしない）

        var floorMat = GetOrCreateMaterial("DungeonFloor", new Color(0.35f, 0.32f, 0.30f));
        var wallMat = GetOrCreateMaterial("DungeonWall", new Color(0.45f, 0.42f, 0.40f));
        var ceilMat = GetOrCreateMaterial("DungeonCeiling", new Color(0.25f, 0.23f, 0.22f));

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.12f, 0.15f);

        var player = BuildPlayer();

        var dungeon = new GameObject("DungeonGenerator");
        var gen = dungeon.AddComponent<DungeonGenerator>();
        gen.floorMaterial = floorMat;
        gen.wallMaterial = wallMat;
        gen.ceilingMaterial = ceilMat;
        gen.player = player.transform;

        new GameObject("EnemySpawner").AddComponent<EnemySpawner>().dungeon = gen;
        new GameObject("BossSpawner").AddComponent<BossSpawner>().dungeon = gen;
        new GameObject("ChestSpawner").AddComponent<ChestSpawner>().dungeon = gen;
        new GameObject("ExitPortalSpawner").AddComponent<ExitPortalSpawner>().dungeon = gen;

        Directory.CreateDirectory(SceneDir);
        EditorSceneManager.SaveScene(scene, DungeonPath);
        Selection.activeGameObject = dungeon;
        Debug.Log("Dungeon シーンを作成しました: " + DungeonPath);
    }

    // ---------------------------------------------------------------- 拠点

    static void CreateHomeSceneInternal()
    {
        ItemAssetCreator.CreateAll();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 澄んだ空の下の草原の村（昼夜は HomeEnvironment が実行時に切り替える）
        var root = new GameObject("HomeVillage").transform;
        HomeVillageBuilder.Build(root);
        var player = BuildPlayer();
        player.transform.position = new Vector3(0f, 0.1f, -7f);

        // ダンジョン入口（崖の前）と装備台
        var gate = new GameObject("DungeonGate");
        gate.transform.SetPositionAndRotation(new Vector3(0f, 0f, 11.5f), Quaternion.Euler(0f, 180f, 0f));
        gate.AddComponent<Portal>().kind = PortalKind.DungeonEntrance;

        var stand = new GameObject("EquipmentStand");
        stand.transform.SetPositionAndRotation(new Vector3(-7f, 0f, 11f), Quaternion.Euler(0f, 180f, 0f));
        stand.AddComponent<EquipmentStand>();

        // ショップ（店主に話しかけて売買する）: 装備ショップは北壁の右側、アイテムショップは東の壁
        var equipShop = new GameObject("EquipmentShop");
        equipShop.transform.SetPositionAndRotation(new Vector3(6.5f, 0f, 10.8f), Quaternion.Euler(0f, 180f, 0f));
        equipShop.AddComponent<Shopkeeper>().kind = ShopKind.Equipment;

        var itemShop = new GameObject("ItemShop");
        itemShop.transform.SetPositionAndRotation(new Vector3(10.8f, 0f, 3f), Quaternion.Euler(0f, -90f, 0f));
        itemShop.AddComponent<Shopkeeper>().kind = ShopKind.Item;

        // 鑑定屋（魔晶覚醒・魔法石での強化）: アイテムショップの向かい側、西の広場
        var appraiser = new GameObject("Appraiser");
        appraiser.transform.SetPositionAndRotation(new Vector3(-10.8f, 0f, 3f), Quaternion.Euler(0f, 90f, 0f));
        appraiser.AddComponent<Appraiser>();

        // 鍛冶屋（装備の強化）: 鑑定屋の南隣、西の広場
        var smith = new GameObject("Blacksmith");
        smith.transform.SetPositionAndRotation(new Vector3(-10.8f, 0f, -1.5f), Quaternion.Euler(0f, 90f, 0f));
        smith.AddComponent<Blacksmith>();

        // 宿屋（減っているHPの分だけ G を払って全回復）: 初期位置の近く、手前の広場
        var inn = new GameObject("Inn");
        inn.transform.SetPositionAndRotation(new Vector3(5.5f, 0f, -10.5f), Quaternion.Euler(0f, 0f, 0f));
        inn.AddComponent<Innkeeper>();

        Directory.CreateDirectory(SceneDir);
        EditorSceneManager.SaveScene(scene, HomePath);
        Debug.Log("Home シーンを作成しました: " + HomePath);
    }

    static void Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.isStatic = true;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // ---------------------------------------------------------------- 共通

    /// <summary>プレイヤー一式（移動・戦闘・HUD・インベントリ）を生成する。</summary>
    static GameObject BuildPlayer()
    {
        var player = new GameObject("Player");
        player.tag = "Player";
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.3f;

        var pivot = new GameObject("CameraPivot");
        pivot.transform.SetParent(player.transform, false);
        pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(pivot.transform, false);
        camGo.AddComponent<Camera>().nearClipPlane = 0.05f;
        camGo.AddComponent<AudioListener>();

        var lightGo = new GameObject("Torch");
        lightGo.transform.SetParent(pivot.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 14f;
        light.intensity = 25f;
        light.color = new Color(1f, 0.85f, 0.65f);

        var fpc = player.AddComponent<FirstPersonController>();
        fpc.cameraPivot = pivot.transform;

        player.AddComponent<Health>().maxHealth = 100f;
        player.AddComponent<Stamina>();
        player.AddComponent<PlayerCombat>().cameraPivot = pivot.transform;
        player.AddComponent<PlayerStats>();
        player.AddComponent<Interactor>();
        player.AddComponent<PlayerHUD>();
        player.AddComponent<InventoryUI>();
        player.AddComponent<MapUI>();
        return player;
    }

    internal static Material GetOrCreateMaterial(string name, Color color)
    {
        Directory.CreateDirectory(MaterialDir);
        string path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", 0.1f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>Build Settings を Home(0) → Dungeon(1) の順にする（他のシーンはその後ろに残す）。</summary>
    static void UpdateBuildSettings()
    {
        var list = new List<EditorBuildSettingsScene>();
        if (File.Exists(HomePath)) list.Add(new EditorBuildSettingsScene(HomePath, true));
        if (File.Exists(DungeonPath)) list.Add(new EditorBuildSettingsScene(DungeonPath, true));

        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != HomePath && s.path != DungeonPath) list.Add(s);

        EditorBuildSettings.scenes = list.ToArray();
    }
}
