using UnityEngine;

/// <summary>階層の判定。階は縦に離れている（20m）ので、高さの差が大きければ「別の階」とみなす。</summary>
public static class FloorUtil
{
    public const float Threshold = 6f;

    public static bool Different(Transform a, Transform b)
        => a != null && b != null && Mathf.Abs(a.position.y - b.position.y) > Threshold;

    public static bool Different(Vector3 a, Vector3 b) => Mathf.Abs(a.y - b.y) > Threshold;
}
