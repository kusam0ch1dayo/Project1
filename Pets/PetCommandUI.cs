using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 召喚獣への命令。召喚獣を見ながら命令キー（標準: F）を押すと、命令モードになる。
/// 1: ついてこい / 2: ここへ移動 / 3: この敵を攻撃 / 4: 宝箱を開けて / 5: 魔法 / 6: 撫でる。
/// 2〜5 は、場所・敵・宝箱を見ながら、もう一度 命令キーを押して決める。Esc で取りやめ。
/// </summary>
public class PetCommandUI : MonoBehaviour
{
    enum Mode { Off, Menu, PickMove, PickEnemy, PickChest, PickSpell, PickSpellTarget, PickSupport }

    public static bool Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active = false;

    FirstPersonController fpc;
    Mode mode;
    PetRuntime pet;
    EnemySpell spell;
    List<EnemySpell> spellChoices = new List<EnemySpell>();
    GameObject canvasGo;
    Text text;
    GameObject marker;

    static readonly Key[] Digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };

    void Awake() => fpc = GetComponent<FirstPersonController>();

    void OnDisable() { SetMode(Mode.Off); }

    Transform Cam => fpc != null && fpc.cameraPivot != null ? fpc.cameraPivot : Camera.main != null ? Camera.main.transform : transform;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || fpc == null) return;
        if (!fpc.inputEnabled || !FirstPersonController.CursorLocked || CinematicDirector.IsPlaying) { if (mode != Mode.Off) SetMode(Mode.Off); return; }

        bool cmdKey = KeyBindings.WasPressed(GameAction.PetCommand);
        // 0: 命令をやめる（命令モードを閉じる。命令モードでなければ、すべての召喚獣の命令を取り消して、ついてこさせる）
        if (kb.digit0Key.wasPressedThisFrame)
        {
            if (mode != Mode.Off) { SetMode(Mode.Off); return; }
            bool any = false;
            foreach (var p in PetSystem.Spawned) if (p != null && !p.Downed && p.order != PetRuntime.Order.Free) { p.Command(PetRuntime.Order.Free); any = true; }
            if (any) Hz.Toast("<color=#9fffb0>命令を取り消した</color>  <size=18>召喚獣が、ついてくる</size>");
            return;
        }
        if (mode == Mode.Off)
        {
            if (!cmdKey) return;
            // 乗っているときは、降りる
            if (PetRide.Active) { PetRide.Instance.End(); return; }
            var p = PetSystem.LookedPet(Cam, 14f);
            if (p == null)
            {
                // 動ける召喚獣が1匹だけなら、どこにいても命令できる（複数いるときは、見ている子に）
                PetRuntime only = null;
                int n = 0;
                foreach (var sp in PetSystem.Spawned) if (sp != null && !sp.Downed) { only = sp; n++; }
                if (n == 1) p = only;
                else if (n > 1) Hz.Toast("<color=#aaaaaa>召喚獣が複数いるときは、命令したい子を見ながら押す</color>");
            }
            if (p == null) return;
            pet = p;
            SetMode(Mode.Menu);
            return;
        }
        if (pet == null || pet.Downed) { SetMode(Mode.Off); return; }
        if (PadKeys.Esc(kb)) { PauseMenu.ConsumeEsc(); SetMode(Mode.Off); return; }

        switch (mode)
        {
            case Mode.Menu:
                if (cmdKey) { SetMode(Mode.Off); return; }
                // 好感度レベルが足りない命令は、聞いてくれない
                var pi = pet.inst;
                if (kb.digit1Key.wasPressedThisFrame) { if (PetAffinity.Can(pi, PetAffinity.LvFollow, "ついてこい")) { pet.Command(PetRuntime.Order.Free); SetMode(Mode.Off); } }
                else if (kb.digit2Key.wasPressedThisFrame) { if (PetAffinity.Can(pi, PetAffinity.LvMove, "ここへ移動")) SetMode(Mode.PickMove); }
                else if (kb.digit3Key.wasPressedThisFrame) { if (PetAffinity.Can(pi, PetAffinity.LvAttack, "攻撃")) SetMode(Mode.PickEnemy); }
                else if (kb.digit4Key.wasPressedThisFrame) { if (PetAffinity.Can(pi, PetAffinity.LvChest, "宝箱を開けて")) SetMode(Mode.PickChest); }
                else if (kb.digit5Key.wasPressedThisFrame)
                {
                    if (!PetAffinity.Can(pi, PetAffinity.LvSpell, "魔法")) break;
                    spellChoices = pet.AISpells(); SetMode(spellChoices.Count > 0 ? Mode.PickSpell : Mode.Menu); if (spellChoices.Count == 0) Hz.Toast("<color=#aaaaaa>使える魔法がない</color>");
                }
                else if (kb.digit6Key.wasPressedThisFrame) { if (PetAffinity.Can(pi, PetAffinity.LvPat, "撫でる")) { pet.Pet(); SetMode(Mode.Off); } }
                else if (kb.digit7Key.wasPressedThisFrame) { var rp = pet; SetMode(Mode.Off); PetRide.Begin(rp); }
                break;

            case Mode.PickSpell:
                for (int i = 0; i < Digits.Length && i < spellChoices.Count; i++)
                    if (kb[Digits[i]].wasPressedThisFrame) { spell = spellChoices[i]; SetMode(AllyAI.IsSupport(spell) ? Mode.PickSupport : Mode.PickSpellTarget); return; }
                break;

            case Mode.PickMove:
                UpdateMarker(GroundPoint(), true);
                if (cmdKey) { pet.Command(PetRuntime.Order.Move, GroundPoint()); SetMode(Mode.Off); }
                break;

            case Mode.PickEnemy:
            {
                var e = LookedEnemy();
                UpdateMarker(e != null ? MagicTargets.Center(e) + Vector3.up : Vector3.zero, e != null);
                if (!cmdKey) break;
                if (e == null) { Hz.Toast("<color=#aaaaaa>敵を見ながら、命令キーを押す</color>"); break; }
                pet.Command(PetRuntime.Order.Attack, default, e);
                SetMode(Mode.Off);
                break;
            }

            case Mode.PickSpellTarget:
            {
                // 敵を見ていれば、その敵へ。見ていなければ、見ている場所へ（敵でなくてもよい）
                var e = LookedEnemy();
                Vector3 gp = GroundPoint();
                UpdateMarker(e != null ? MagicTargets.Center(e) + Vector3.up : gp, true);
                if (!cmdKey) break;
                if (e != null) pet.Command(PetRuntime.Order.Spell, default, e, null, spell);
                else pet.Command(PetRuntime.Order.Spell, gp, null, null, spell, null, true);
                SetMode(Mode.Off);
                break;
            }

            case Mode.PickSupport:
            {
                // 回復・バリア: 見ている味方へ。誰も見ていなければ、プレイヤー（自分）へ
                var a = LookedAlly();
                UpdateMarker(a != null ? a.transform.position + Vector3.up * 2.4f : Vector3.zero, a != null);
                if (!cmdKey) break;
                var who = a != null ? a : Hz.PlayerHealth;
                pet.Command(PetRuntime.Order.Spell, default, null, null, spell, who);
                SetMode(Mode.Off);
                break;
            }

            case Mode.PickChest:
            {
                var c = LookedChest();
                UpdateMarker(c != null ? c.transform.position + Vector3.up * 1.2f : Vector3.zero, c != null);
                if (!cmdKey) break;
                if (c == null) { Hz.Toast("<color=#aaaaaa>開けていない宝箱を見ながら、命令キーを押す</color>"); break; }
                pet.Command(PetRuntime.Order.Chest, default, null, c);
                SetMode(Mode.Off);
                break;
            }
        }
    }

    void SetMode(Mode m)
    {
        mode = m;
        Active = m != Mode.Off;
        if (m == Mode.Off && marker != null) { Destroy(marker); marker = null; }
        if (canvasGo == null && m != Mode.Off) Build();
        if (canvasGo != null) canvasGo.SetActive(m != Mode.Off);
        if (m == Mode.Off) return;
        AudioManager.PlaySfx("cursor", 0.5f, 1.2f);
        string key = KeyBindings.Label(GameAction.PetCommand);
        string name = pet != null ? pet.inst.Name : "";
        var sb = new StringBuilder();
        sb.AppendLine($"<size=26><color=#9fffb0>{name}</color> に命令</size>  <size=18><color=#aaaaaa>MP {Mathf.RoundToInt(pet != null ? pet.Mp : 0)}/{Mathf.RoundToInt(pet != null ? pet.MpMax : 0)}</color></size>");
        switch (m)
        {
            case Mode.Menu:
            {
                var pi = pet.inst;
                string Opt(int n, string label, int lv) => pi.affinityLevel >= lv ? $"{n} {label}" : $"<color=#666666>{n} {label}<size=15>(好感度Lv{lv})</size></color>";
                sb.AppendLine($"<size=17><color=#ffb0c8>好感度 Lv{pi.affinityLevel}  {Mathf.RoundToInt(PetAffinity.Ratio(pi) * 100f)}%</color></size>");
                sb.Append($"{Opt(1, "ついてこい", PetAffinity.LvFollow)}　 {Opt(2, "ここへ移動", PetAffinity.LvMove)}　 {Opt(3, "攻撃", PetAffinity.LvAttack)}　 {Opt(4, "宝箱を開けて", PetAffinity.LvChest)}　 {Opt(5, "魔法", PetAffinity.LvSpell)}　 {Opt(6, "撫でる", PetAffinity.LvPat)}　 {Opt(7, "ライド", PetAffinity.LvRide)}　　<color=#aaaaaa>0・Esc 取りやめ</color>");
                break;
            }
            case Mode.PickMove: sb.Append("行かせたい場所を見て、<color=#ffd75e>命令キー</color>（先行させる。1「ついてこい」で戻る）"); break;
            case Mode.PickEnemy: sb.Append("狙わせたい敵を見て、<color=#ffd75e>命令キー</color>"); break;
            case Mode.PickChest: sb.Append("開けさせたい宝箱を見て、<color=#ffd75e>命令キー</color>（ミミックに注意）"); break;
            case Mode.PickSpell:
                for (int i = 0; i < spellChoices.Count && i < Digits.Length; i++)
                {
                    var s = spellChoices[i];
                    bool ok = pet.CanCast(s);
                    sb.Append($"{i + 1} <color={(ok ? "#" + ColorUtility.ToHtmlStringRGB(MagicFx.Main(s.element)) : "#666666")}>{s.name}</color><size=16><color=#888888>({PetRuntime.SpellCost(s):0})</color></size>　");
                }
                break;
            case Mode.PickSpellTarget: sb.Append($"<color=#9fd8ff>{spell?.name}</color> を撃つ敵・場所を見て、<color=#ffd75e>命令キー</color>"); break;
            case Mode.PickSupport: sb.Append($"<color=#9fffb0>{spell?.name}</color> をかける味方を見て、<color=#ffd75e>命令キー</color>（誰も見ていなければ、自分に）"); break;
        }
        text.text = sb.ToString();
    }

    void Build()
    {
        canvasGo = new GameObject("PetCommandCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 18;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var bg = UIKit.Panel(canvasGo.transform, "Bg", new Color(0f, 0f, 0f, 0.6f), new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.3f));
        bg.GetComponent<Image>().raycastTarget = false;
        text = UIKit.Label(bg, "Text", "", 22, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
        text.rectTransform.offsetMin = new Vector2(16f, 6f);
        text.rectTransform.offsetMax = new Vector2(-16f, -6f);
    }

    void OnDestroy() { if (canvasGo != null) Destroy(canvasGo); Active = false; }

    // ---------------------------------------------------------------- 見ているもの

    Vector3 GroundPoint()
    {
        var ray = new Ray(Cam.position, Cam.forward);
        foreach (var h in SortedHits(ray, 40f))
        {
            if (h.collider.transform.root == transform.root || h.collider.GetComponentInParent<Health>() != null) continue;
            return h.point;
        }
        return ray.GetPoint(10f);
    }

    static RaycastHit[] SortedHits(Ray ray, float dist)
    {
        var hits = Physics.RaycastAll(ray, dist, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    Health LookedEnemy()
    {
        Health best = null;
        float bestA = 10f;
        foreach (var h in MagicTargets.Alive)
        {
            if (h == null || h.IsDead) continue;
            Vector3 to = MagicTargets.Center(h) - Cam.position;
            if (to.magnitude > 40f) continue;
            float a = Vector3.Angle(Cam.forward, to);
            if (a < bestA && MagicTargets.Sight(Cam.position, h)) { bestA = a; best = h; }
        }
        return best;
    }

    /// <summary>見ている味方（蘇生した敵・召喚獣）。</summary>
    Health LookedAlly()
    {
        Health best = null;
        float bestA = 10f;
        foreach (var h in TrapAllies.Alive())
        {
            Vector3 to = MagicTargets.Center(h) - Cam.position;
            if (to.magnitude > 35f) continue;
            float a = Vector3.Angle(Cam.forward, to);
            if (a < bestA) { bestA = a; best = h; }
        }
        return best;
    }

    Chest LookedChest()
    {
        Chest best = null;
        float bestA = 12f;
        foreach (var c in Chest.All)
        {
            if (c == null || c.Opened) continue;
            Vector3 to = c.transform.position + Vector3.up * 0.4f - Cam.position;
            if (to.magnitude > 35f) continue;
            float a = Vector3.Angle(Cam.forward, to);
            if (a < bestA) { bestA = a; best = c; }
        }
        return best;
    }

    void UpdateMarker(Vector3 p, bool show)
    {
        if (!show) { if (marker != null) marker.SetActive(false); return; }
        if (marker == null)
        {
            marker = MagicFxExtra.Cylinder(new Color(0.5f, 1f, 0.6f), 3f);
            marker.name = "PetCommandMarker";
            marker.transform.localScale = new Vector3(0.6f, 0.03f, 0.6f);
        }
        marker.SetActive(true);
        marker.transform.position = p + Vector3.up * 0.05f;
    }
}
