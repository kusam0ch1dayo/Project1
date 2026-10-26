using UnityEngine;

/// <summary>Eキーで調べられるもの（宝箱・扉・祭壇など）。</summary>
public interface IInteractable
{
    string Prompt { get; }
    bool CanInteract { get; }
    void Interact(GameObject interactor);

    /// <summary>0 なら押した瞬間に実行。正の値なら、その秒数だけ E を長押ししている間、無防備になって実行される。</summary>
    float HoldDuration => 0f;
}
