using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーの近くにある IInteractable を探し、E キー / ゲームパッド North で操作する。
/// HoldDuration が正のもの（宝箱）は長押しで解錠。解錠中は移動・攻撃ができない（視点のみ動かせる）。
/// E を離す・対象から離れる・インベントリを開く等で解錠は中断され、進行度はリセットされる。
/// </summary>
[RequireComponent(typeof(FirstPersonController))]
public class Interactor : MonoBehaviour
{
    public float range = 2.5f;

    FirstPersonController fpc;
    IInteractable holdTarget;
    float holdTimer;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    void OnDisable()
    {
        CancelHold();
    }

    void Update()
    {
        if (!fpc.inputEnabled)
        {
            CancelHold();
            SetPrompt(null);
            return;
        }

        IInteractable nearest = FindNearest();
        bool locked = FirstPersonController.CursorLocked;

        var kb = Keyboard.current;
        var pad = Gamepad.current;
        bool down = KeyBindings.WasPressed(GameAction.Interact) || PadKeys.Was(GameAction.Interact);
        bool held = KeyBindings.IsPressed(GameAction.Interact) || PadKeys.Held(GameAction.Interact);

        // 解錠中
        if (holdTarget != null)
        {
            if (!held || !locked || nearest != holdTarget)
            {
                CancelHold();
            }
            else
            {
                holdTimer += Time.deltaTime;
                float need = holdTarget.HoldDuration;
                SetHoldProgress(holdTimer / need);
                SetPrompt("解錠中…");

                if (holdTimer >= need)
                {
                    var done = holdTarget;
                    CancelHold();
                    done.Interact(gameObject);
                }
                return;
            }
        }

        SetPrompt(nearest != null ? nearest.Prompt : null);
        if (nearest == null || !locked || !down) return;
        if (nearest is Chest touchedChest) touchedChest.touched = true; // 一度でも E を押した宝箱

        if (nearest.HoldDuration > 0f)
        {
            holdTarget = nearest;
            holdTimer = 0f;
            fpc.actionsLocked = true;
            AudioManager.PlaySfx("beep", 0.6f);
        }
        else
        {
            nearest.Interact(gameObject);
        }
    }

    IInteractable FindNearest()
    {
        IInteractable nearest = null;
        float best = float.MaxValue;
        Vector3 origin = transform.position + Vector3.up;

        foreach (var col in Physics.OverlapSphere(origin, range, ~0, QueryTriggerInteraction.Ignore))
        {
            var it = col.GetComponentInParent<IInteractable>();
            if (it == null || !it.CanInteract) continue;

            float d = (col.ClosestPoint(origin) - origin).sqrMagnitude;
            if (d < best) { best = d; nearest = it; }
        }
        return nearest;
    }

    void CancelHold()
    {
        holdTarget = null;
        holdTimer = 0f;
        if (fpc != null) fpc.actionsLocked = false;
        SetHoldProgress(-1f);
    }

    static void SetPrompt(string text)
    {
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.SetPrompt(text);
    }

    static void SetHoldProgress(float ratio)
    {
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.SetHoldProgress(ratio);
    }
}
