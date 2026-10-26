using UnityEngine;

/// <summary>
/// 拠点（草原の村）の昼夜。TimeOfDay.IsNight に応じて、空・太陽/月・環境光・霧・星・街灯を切り替える。
/// 昼: 澄んだ青空と太陽。夜: 星空と月、街灯と窓に明かりが灯る。
/// </summary>
public class HomeEnvironment : MonoBehaviour
{
    public Light sun;
    public Light moon;

    Material sky;
    Transform stars, moonVisual, cam;
    ParticleSystem pollen, fireflies;

    static readonly Vector3 MoonDir = new Vector3(0.45f, 0.55f, 0.7f);

    void Start()
    {
        GameSession.EnsureInitialized(); // セーブされた昼夜を読み込む

        var player = FindFirstObjectByType<FirstPersonController>();
        var camera = Camera.main;
        cam = camera != null ? camera.transform : null;

        if (RenderSettings.skybox != null)
        {
            sky = new Material(RenderSettings.skybox); // 元のアセットは書き換えない
            RenderSettings.skybox = sky;
        }

        // 昼夜はシーンに入った時点で決まる（ダンジョンから戻るたびにシーンが読み込まれ直す）ので、必要な方だけ作る
        bool night = TimeOfDay.IsNight;
        if (night) BuildNightSky();
        if (player != null) BuildAmbientParticles(player.transform, night);

        Apply(night, player);
    }

    void LateUpdate()
    {
        if (cam == null) return;
        if (stars != null) stars.position = cam.position;
        if (moonVisual != null) moonVisual.position = cam.position + MoonDir.normalized * 150f;
    }

    void BuildNightSky()
    {
        // 星: 上半球の殻に小さな光の点を置く（カメラに追従して無限遠のように見せる）
        var ps = Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 1f, 1f), color2 = new Color(0.8f, 0.9f, 1f), count = 900, life = 100000f,
            speedMin = 0f, speedMax = 0f, sizeMin = 0.7f, sizeMax = 2.2f, shape = ParticleSystemShapeType.Hemisphere,
            radius = 150f, edge = true, shrink = false, fade = false, scalable = false, local = true,
        }, Vector3.zero, null, Quaternion.Euler(-90f, 0f, 0f));
        if (ps != null) stars = ps.transform;

        // 月
        var moonGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        moonGo.name = "MoonVisual";
        Destroy(moonGo.GetComponent<Collider>());
        moonGo.transform.localScale = Vector3.one * 14f;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.95f, 0.97f, 1f));
            moonGo.GetComponent<Renderer>().sharedMaterial = mat;
        }
        moonGo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        moonVisual = moonGo.transform;
    }

    void BuildAmbientParticles(Transform player, bool night)
    {
        // 昼: 花粉のような白い粒 / 夜: 蛍。プレイヤーに付いて来るが、粒自体は世界に残る
        if (night)
            fireflies = Fx.Ambient(player, new Vector3(0f, 1.5f, 0f), new Vector3(36f, 3f, 36f),
                                   new Color(0.7f, 1f, 0.35f), new Color(1f, 1f, 0.5f), 0.22f, 10f, 6f, 0.4f);
        else
            pollen = Fx.Ambient(player, new Vector3(0f, 2f, 0f), new Vector3(40f, 5f, 40f),
                                new Color(1f, 1f, 0.8f, 0.7f), new Color(1f, 0.95f, 0.7f, 0.7f), 0.08f, 22f, 8f, 0.35f);
    }

    void Apply(bool night, FirstPersonController player)
    {
        if (sun != null)
        {
            RenderSettings.sun = sun;
            // 夜は太陽を地平線の下へ（空が暗くなる）
            sun.transform.rotation = night ? Quaternion.Euler(-35f, -30f, 0f) : Quaternion.Euler(48f, -30f, 0f);
            sun.intensity = night ? 0f : 1.25f;
            sun.enabled = !night;
        }
        if (moon != null)
        {
            moon.transform.rotation = Quaternion.LookRotation(-MoonDir.normalized);
            moon.intensity = 0.45f;
            moon.enabled = night;
        }

        if (sky != null)
        {
            sky.SetFloat("_Exposure", night ? 0.5f : 1.3f);
            sky.SetFloat("_AtmosphereThickness", night ? 0.7f : 0.9f);
            sky.SetColor("_SkyTint", night ? new Color(0.35f, 0.4f, 0.6f) : new Color(0.5f, 0.55f, 0.65f));
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        if (night)
        {
            RenderSettings.ambientSkyColor = new Color(0.10f, 0.13f, 0.25f);
            RenderSettings.ambientEquatorColor = new Color(0.07f, 0.09f, 0.16f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.05f, 0.08f);
        }
        else
        {
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.78f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.6f, 0.65f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.32f, 0.25f);
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = night ? 0.0022f : 0.0016f;
        RenderSettings.fogColor = night ? new Color(0.03f, 0.04f, 0.09f) : new Color(0.75f, 0.86f, 0.97f);

        var clouds = GameObject.Find("Clouds");
        if (clouds != null) clouds.SetActive(!night);

        foreach (var l in FindObjectsByType<NightLight>(FindObjectsSortMode.None)) l.Set(night);

        // プレイヤーが持つ明かり: 昼は消し、夜はランタン程度
        if (player != null)
        {
            var torch = player.GetComponentInChildren<Light>();
            if (torch != null)
            {
                torch.enabled = night;
                torch.intensity = 6f;
                torch.range = 9f;
            }
        }
    }
}
