using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ItemData のインスペクター。種類に応じて必要な項目だけを表示し、武器は一人称視点のプレビューを出す。
/// Item Creator ウィンドウの詳細欄にも、これが使われる。
/// </summary>
[CustomEditor(typeof(ItemData))]
public class ItemDataEditor : Editor
{
    SerializedProperty id, displayName, description, type, rarity;
    SerializedProperty attackPower, weaponRange, swingTime, staminaCost, bladeColor, bladeLength;
    SerializedProperty weaponPrefab, prefabPosition, prefabEuler, prefabScale;
    SerializedProperty priceOverride, element;
    SerializedProperty bonusMoveSpeed, bonusAttackSpeed, bonusCritRate, bonusCritDamage, bonusStaminaRegen, bonusLifesteal, bonusDodge;
    SerializedProperty isStaff, spells, bonusMaxMana, bonusManaRegen, weaponKind;
    SerializedProperty liquidColor, useKind;
    SerializedProperty bonusAttack, defense, bonusMaxHealth, bonusMaxStamina, healAmount, staminaAmount, manaAmount, applyStatus, cureDebuffs;

    WeaponPreview preview;
    bool idCommitted;

    /// <summary>
    /// アセットのファイル名（= 他の画面のアイテム選択に出る名前）を、ID に合わせる。
    /// 重複するファイル名があるとき、IDが空のときは、何もしない。
    /// </summary>
    public static void SyncAssetName(ItemData item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.id)) return;
        string path = AssetDatabase.GetAssetPath(item);
        if (string.IsNullOrEmpty(path)) return;

        string safe = item.id.Trim();
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
        if (string.IsNullOrEmpty(safe) || System.IO.Path.GetFileNameWithoutExtension(path) == safe) return;

        string dir = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        if (AssetDatabase.LoadAssetAtPath<Object>($"{dir}/{safe}.asset") != null) return; // 同じ名前のファイルがある
        string error = AssetDatabase.RenameAsset(path, safe);
        if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"アイテムのファイル名を ID に合わせられませんでした: {error}");
    }

    /// <summary>すべてのアイテムの、ファイル名を ID に合わせる。</summary>
    public static void SyncAllAssetNames()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
            SyncAssetName(AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid)));
    }

    void OnEnable()
    {
        id = serializedObject.FindProperty("id");
        displayName = serializedObject.FindProperty("displayName");
        description = serializedObject.FindProperty("description");
        type = serializedObject.FindProperty("type");
        rarity = serializedObject.FindProperty("rarity");
        attackPower = serializedObject.FindProperty("attackPower");
        weaponRange = serializedObject.FindProperty("weaponRange");
        swingTime = serializedObject.FindProperty("swingTime");
        staminaCost = serializedObject.FindProperty("staminaCost");
        bladeColor = serializedObject.FindProperty("bladeColor");
        bladeLength = serializedObject.FindProperty("bladeLength");
        weaponPrefab = serializedObject.FindProperty("weaponPrefab");
        prefabPosition = serializedObject.FindProperty("prefabPosition");
        prefabEuler = serializedObject.FindProperty("prefabEuler");
        prefabScale = serializedObject.FindProperty("prefabScale");
        priceOverride = serializedObject.FindProperty("priceOverride");
        element = serializedObject.FindProperty("element");
        bonusMoveSpeed = serializedObject.FindProperty("bonusMoveSpeed");
        bonusAttackSpeed = serializedObject.FindProperty("bonusAttackSpeed");
        bonusCritRate = serializedObject.FindProperty("bonusCritRate");
        bonusCritDamage = serializedObject.FindProperty("bonusCritDamage");
        bonusStaminaRegen = serializedObject.FindProperty("bonusStaminaRegen");
        bonusLifesteal = serializedObject.FindProperty("bonusLifesteal");
        bonusDodge = serializedObject.FindProperty("bonusDodge");
        isStaff = serializedObject.FindProperty("isStaff");
        weaponKind = serializedObject.FindProperty("weaponKind");
        spells = serializedObject.FindProperty("spells");
        bonusMaxMana = serializedObject.FindProperty("bonusMaxMana");
        bonusManaRegen = serializedObject.FindProperty("bonusManaRegen");
        bonusAttack = serializedObject.FindProperty("bonusAttack");
        defense = serializedObject.FindProperty("defense");
        bonusMaxHealth = serializedObject.FindProperty("bonusMaxHealth");
        bonusMaxStamina = serializedObject.FindProperty("bonusMaxStamina");
        healAmount = serializedObject.FindProperty("healAmount");
        staminaAmount = serializedObject.FindProperty("staminaAmount");
        manaAmount = serializedObject.FindProperty("manaAmount");
        applyStatus = serializedObject.FindProperty("applyStatus");
        cureDebuffs = serializedObject.FindProperty("cureDebuffs");
        liquidColor = serializedObject.FindProperty("liquidColor");
        useKind = serializedObject.FindProperty("useKind");
    }

    void OnDisable()
    {
        if (preview != null)
        {
            preview.Dispose();
            preview = null;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ---- 基本
        EditorGUILayout.LabelField("基本", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(displayName, new GUIContent("名前"));
        EditorGUILayout.PropertyField(description, new GUIContent("説明"));
        EditorGUILayout.PropertyField(type, new GUIContent("種類"));
        EditorGUILayout.PropertyField(rarity, new GUIContent("レアリティ"));
        if (type.intValue != (int)ItemType.Consumable)
            EditorGUILayout.PropertyField(element, new GUIContent("属性", "敵との相性でダメージが変わる。魔法石の属性が一致するとエッセンスが2倍"));
        // Enter か、欄から離れたときに確定する。確定すると、アセットのファイル名（アイテム選択に出る名前）も ID に変わる
        string newId = EditorGUILayout.DelayedTextField(new GUIContent("ID", "セーブデータで使う識別子。他のアイテムと重複させないこと。確定すると、ファイル名も ID に変わる"), id.stringValue);
        if (newId != id.stringValue) { id.stringValue = newId; idCommitted = true; }
        DrawIdValidation();
        EditorGUILayout.PropertyField(priceOverride, new GUIContent("価格 (G)", "0 なら、性能とレアリティから自動計算"));
        var priced = (ItemData)target;
        EditorGUILayout.LabelField($"実際の価格: {priced.BuyPrice} G  /  買取: {priced.SellPrice} G", EditorStyles.miniLabel);

        var itemType = (ItemType)type.intValue;
        EditorGUILayout.Space();

        // ---- 武器
        if (itemType == ItemType.Weapon)
        {
            EditorGUILayout.LabelField("武器の性能", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(weaponKind, new GUIContent("武器の種類", "剣 / 槍 / 弓 / 双剣 / 斧 / モーニングスター / 鎌 / 釣り竿。種類ごとに、攻撃の仕方と標準の見た目が変わる（杖にチェックを入れたときは、杖として扱われる）"));
            var kind = (WeaponKind)weaponKind.enumValueIndex;
            string rangeLabel = "射程 (m)", rangeTip = "近接攻撃が届く距離", swingLabel = "振る時間 (秒)", swingTip = "短いほど素早い", costLabel = "スタミナ消費", costTip = "1回の攻撃ごと";
            string kindHelp = null;
            switch (kind)
            {
                case WeaponKind.Spear:
                    rangeLabel = "突きの射程 (m)"; rangeTip = "槍は射程が長い。2m 以内の近距離では威力が半分になり、向いている方向へ進んでいるほど威力が増す";
                    kindHelp = "槍: 中射程の突き。近づかれると弱い。向いている方向へ進むほど威力が上がる。"; break;
                case WeaponKind.Bow:
                    rangeLabel = "矢の飛距離 (m)"; swingLabel = "引き絞る時間 (秒)"; swingTip = "この時間まで引き絞ると、最大の威力と速さで射られる"; costLabel = "スタミナ消費（1射）";
                    kindHelp = "弓: 左クリック長押しで引き絞り、離して射る。矢を1本消耗する（素材屋で買える）。攻撃力は、最大まで引き絞ったときの威力。"; break;
                case WeaponKind.DualBlades:
                    swingTip = "2連撃ぶんの合計時間";
                    kindHelp = "双剣: 1回の攻撃で2連撃（攻撃力は1撃ぶん）。射程は短いが素早い。回避が身軽になる。"; break;
                case WeaponKind.Axe:
                    kindHelp = "斧: 重い振り下ろし。広い範囲をまとめて叩く。落下中に当てると、落下の速さで威力が増し、落下ダメージも無効になる。"; break;
                case WeaponKind.MorningStar:
                    rangeLabel = "鎖の長さ (m)"; rangeTip = "鎖の長さ。長いほど遠くへ届くが、振り回しにくい"; swingLabel = "（使いません）"; swingTip = "モーニングスターでは使われない"; costLabel = "スタミナ消費（振り回している間、毎秒）"; costTip = "左クリックを押して振り回している間、1秒あたりに消費する";
                    kindHelp = "モーニングスター: 左クリックを押しながら視点を振ると、鎖の先の鉄球が物理で振り回される。速いほど威力が大きい（攻撃力は、鉄球が速さ 9m/秒のときの威力）。"; break;
                case WeaponKind.Scythe:
                    kindHelp = "鎌: 素早い大振り。魔法を刻んだものが多い。左クリック長押しで、鎌を投げて戻ってくる。"; break;
                case WeaponKind.FishingRod:
                    rangeLabel = "最大の飛距離 (m)"; rangeTip = "最大までためて放ったときに、浮きが飛ぶ距離の目安"; swingLabel = "ためる時間 (秒)"; swingTip = "この時間まで長押しすると、最大の飛距離になる"; costLabel = "スタミナ消費（1投）";
                    kindHelp = "釣り竿: 左クリック長押しで力をため、離して釣り糸を放つ。敵に当たると、攻撃力ぶんのダメージを与えて、プレイヤーの方へ引き寄せる（通常のボスまで。大ボスは無理）。もう一度左クリックで巻き取る。侵食・無限ダンジョンのモディファイアで、使える武器には選ばれない。"; break;
            }
            EditorGUILayout.PropertyField(attackPower, new GUIContent("攻撃力"));
            EditorGUILayout.PropertyField(weaponRange, new GUIContent(rangeLabel, rangeTip));
            EditorGUILayout.PropertyField(swingTime, new GUIContent(swingLabel, swingTip));
            EditorGUILayout.PropertyField(staminaCost, new GUIContent(costLabel, costTip));
            if (kindHelp != null) EditorGUILayout.HelpBox(kindHelp, MessageType.None);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("魔法", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(isStaff, new GUIContent("杖", "左クリックで殴らず、MPを少し使って武器の属性の魔法弾を放つ"));
            EditorGUILayout.PropertyField(spells, new GUIContent("刻まれた魔法", "右クリック長押しで発動。複数ならマウスホイールで切り替え。空なら魔法は使えない"), true);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("武器の見た目", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(weaponPrefab, new GUIContent("プレハブ", "未設定なら標準の剣モデル"));

            if (weaponPrefab.objectReferenceValue != null)
            {
                EditorGUILayout.PropertyField(prefabPosition, new GUIContent("位置"));
                EditorGUILayout.PropertyField(prefabEuler, new GUIContent("回転"));
                EditorGUILayout.PropertyField(prefabScale, new GUIContent("倍率"));
                if (GUILayout.Button("位置・回転・倍率をリセット"))
                {
                    prefabPosition.vector3Value = Vector3.zero;
                    prefabEuler.vector3Value = Vector3.zero;
                    prefabScale.vector3Value = Vector3.one;
                }
                EditorGUILayout.HelpBox(
                    "向きの決まり: 原点=握る位置 / +Z=刃先 / 刃(エッジ)=左右(±X) / 平らな面=上下(±Y)。\n" +
                    "参考: Assets/Prefabs/Weapons/Sword_Reference.prefab",
                    MessageType.None);
                DrawPreview();
            }
            else
            {
                EditorGUILayout.PropertyField(bladeColor, new GUIContent("刃の色"));
                EditorGUILayout.PropertyField(bladeLength, new GUIContent("刃の長さ"));
                EditorGUILayout.HelpBox(kind == WeaponKind.Sword ? "プレハブが未設定です。標準の剣モデル（色と長さのみ変更可）が使われます。" : "プレハブが未設定です。この武器の種類の標準モデル（色と長さのみ変更可）が使われます。", MessageType.Info);
            }
            EditorGUILayout.Space();
        }

        // ---- 装備ボーナス / 消耗品
        if (itemType != ItemType.Consumable)
        {
            EditorGUILayout.LabelField(itemType == ItemType.Weapon ? "追加ボーナス" : "装備の効果", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(bonusAttack, new GUIContent("攻撃力 +"));
            EditorGUILayout.PropertyField(defense, new GUIContent("防御力 +"));
            EditorGUILayout.PropertyField(bonusMaxHealth, new GUIContent("最大HP +"));
            EditorGUILayout.PropertyField(bonusMaxStamina, new GUIContent("最大スタミナ +"));
            EditorGUILayout.PropertyField(bonusMoveSpeed, new GUIContent("移動速度 +%"));
            EditorGUILayout.PropertyField(bonusAttackSpeed, new GUIContent("攻撃速度 +%"));
            EditorGUILayout.PropertyField(bonusCritRate, new GUIContent("クリティカル率 +%"));
            EditorGUILayout.PropertyField(bonusCritDamage, new GUIContent("クリティカルダメージ +%"));
            EditorGUILayout.PropertyField(bonusStaminaRegen, new GUIContent("スタミナ回復速度 +%"));
            EditorGUILayout.PropertyField(bonusLifesteal, new GUIContent("吸血 %"));
            EditorGUILayout.PropertyField(bonusDodge, new GUIContent("回避率 +%"));
            EditorGUILayout.PropertyField(bonusMaxMana, new GUIContent("最大MP +"));
            EditorGUILayout.PropertyField(bonusManaRegen, new GUIContent("MP回復速度 +%"));

            // 防具・アクセサリーにも、魔法を刻める（着ていると、その魔法が使える。武器の魔法と、まとめて切り替えられる）
            if (itemType != ItemType.Weapon)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("刻まれた魔法", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(spells, new GUIContent("刻まれた魔法", "この装備を着ていると、使える魔法が増える（右クリック長押しで発動、ホイールで切り替え）。空なら、何も増えない"), true);
            }
        }
        else
        {
            EditorGUILayout.LabelField("消耗品の効果", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(healAmount, new GUIContent("HP回復量"));
            EditorGUILayout.PropertyField(staminaAmount, new GUIContent("スタミナ回復量"));
            EditorGUILayout.PropertyField(manaAmount, new GUIContent("MP回復量"));
            EditorGUILayout.PropertyField(applyStatus, new GUIContent("付くバフ（強度・時間）"), true);
            EditorGUILayout.PropertyField(cureDebuffs, new GUIContent("デバフをすべて消す（万能薬）"));
            EditorGUILayout.PropertyField(liquidColor, new GUIContent("ポーションの中身の色", "クイックスロットの瓶の色。アルファ 0 なら自動（HP=赤・MP=青・スタミナ=緑）。色を決めるときは、アルファを 255 にする"));
            if (liquidColor.colorValue.a <= 0.01f) EditorGUILayout.HelpBox("いまは自動の色です（HP=赤・MP=青・スタミナ=緑・両方=紫）。色を決めると、その色の瓶になります。", MessageType.None);
            EditorGUILayout.PropertyField(useKind, new GUIContent("特殊な使い方", "ふつうの薬は Normal。爆弾などの細かい設定は、インスペクターの下の項目で行う"));
        }

        serializedObject.ApplyModifiedProperties();
        if (idCommitted)
        {
            idCommitted = false;
            var renamed = (ItemData)target;
            EditorApplication.delayCall += () => SyncAssetName(renamed);
        }
    }

    void DrawIdValidation()
    {
        string current = id.stringValue;
        if (string.IsNullOrWhiteSpace(current))
        {
            EditorGUILayout.HelpBox("ID が空です。セーブデータで使うため、必ず入力してください。", MessageType.Error);
            return;
        }

        int count = AssetDatabase.FindAssets("t:ItemData")
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g)))
            .Count(a => a != null && a.id == current);
        if (count > 1)
            EditorGUILayout.HelpBox($"ID '{current}' が他のアイテムと重複しています（{count} 件）。", MessageType.Error);
    }

    void DrawPreview()
    {
        if (preview == null) preview = new WeaponPreview();

        EditorGUILayout.LabelField("プレビュー（左: 一人称で構えた状態 / 右: 真上から）", EditorStyles.miniBoldLabel);
        Rect area = GUILayoutUtility.GetRect(10f, 230f, GUILayout.ExpandWidth(true));
        var item = (ItemData)target;

        // プレビューは、編集中の値（SerializedObject の未反映分）ではなく、確定済みの値で描く
        Rect left = new Rect(area.x, area.y, area.width * 0.5f - 2f, area.height);
        Rect right = new Rect(area.x + area.width * 0.5f + 2f, area.y, area.width * 0.5f - 2f, area.height);

        if (Event.current.type == EventType.Repaint)
        {
            serializedObject.ApplyModifiedProperties();

            var a = preview.RenderFirstPerson(left, item);
            var b = preview.RenderTopDown(right, item);
            if (a != null) GUI.DrawTexture(left, a, ScaleMode.StretchToFill, false);
            if (b != null) GUI.DrawTexture(right, b, ScaleMode.StretchToFill, false);
        }
    }

    // ======================================================================== プレビュー描画

    /// <summary>武器プレハブを、ゲームと同じ構え（PlayerCombat の待機姿勢）で描画するプレビュー。</summary>
    class WeaponPreview : System.IDisposable
    {
        PreviewRenderUtility pru;
        GameObject root;
        GameObject instance;
        GameObject sourcePrefab;

        public WeaponPreview()
        {
            pru = new PreviewRenderUtility();
            var cam = pru.camera;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.13f, 0.17f);

            pru.lights[0].intensity = 1.4f;
            pru.lights[0].transform.rotation = Quaternion.Euler(40f, 30f, 0f);
            pru.lights[1].intensity = 0.7f;
            pru.ambientColor = new Color(0.4f, 0.4f, 0.45f);
        }

        void EnsureInstance(ItemData item)
        {
            if (instance != null && sourcePrefab == item.weaponPrefab) return;

            DestroyInstance();
            sourcePrefab = item.weaponPrefab;
            if (sourcePrefab == null) return;

            root = EditorUtility.CreateGameObjectWithHideFlags("WeaponPreviewRoot", HideFlags.HideAndDontSave);
            pru.AddSingleGO(root);
            instance = Object.Instantiate(sourcePrefab, root.transform);
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        void ApplyOffsets(ItemData item)
        {
            var t = instance.transform;
            t.localPosition = item.prefabPosition;
            t.localRotation = Quaternion.Euler(item.prefabEuler);
            t.localScale = Vector3.Scale(sourcePrefab.transform.localScale, item.prefabScale);
        }

        public Texture RenderFirstPerson(Rect rect, ItemData item)
        {
            EnsureInstance(item);
            if (instance == null) return null;
            ApplyOffsets(item);

            root.transform.localPosition = PlayerCombat.RestPos;
            root.transform.localRotation = Quaternion.Euler(PlayerCombat.RestEuler);

            var cam = pru.camera;
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity;
            cam.fieldOfView = 60f;

            pru.BeginPreview(rect, GUIStyle.none);
            cam.Render();
            return pru.EndPreview();
        }

        public Texture RenderTopDown(Rect rect, ItemData item)
        {
            EnsureInstance(item);
            if (instance == null) return null;
            ApplyOffsets(item);

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            var cam = pru.camera;
            cam.transform.position = new Vector3(0f, 3.2f, 0.45f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward); // 画面の上 = +Z（刃先）
            cam.fieldOfView = 30f;

            pru.BeginPreview(rect, GUIStyle.none);
            cam.Render();
            return pru.EndPreview();
        }

        void DestroyInstance()
        {
            if (root != null) Object.DestroyImmediate(root);
            root = null;
            instance = null;
            sourcePrefab = null;
        }

        public void Dispose()
        {
            DestroyInstance();
            if (pru != null) pru.Cleanup();
            pru = null;
        }
    }
}
