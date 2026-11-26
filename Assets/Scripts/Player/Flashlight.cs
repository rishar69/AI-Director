using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// The player's flashlight: a model held at the lower right of the view
/// and a warm spot beam, toggled with F.
///
/// It lags a touch behind the view so it reads as held rather than
/// bolted to the camera, and it is put away while hiding - under a desk
/// there is no room for it, and its beam would light the hiding place.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class Flashlight : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [SerializeField]
    private GameObject modelPrefab;

    [SerializeField]
    private Key toggleKey = Key.F;

    [SerializeField]
    private bool startOn = true;


    [Header("Hold")]
    /// <summary>Where the flashlight sits, relative to the camera.</summary>
    [SerializeField]
    private Vector3 holdOffset = new Vector3(0.17f, -0.14f, 0.38f);

    /// <summary>How quickly the flashlight catches up with the view.</summary>
    [SerializeField, Min(1f)]
    private float followSharpness = 14f;


    [Header("Beam")]
    [SerializeField]
    private Color beamColor = new Color(1f, 0.95f, 0.86f);

    [SerializeField, Min(0f)]
    private float beamIntensity = 150f;

    [SerializeField, Min(1f)]
    private float beamRange = 26f;

    [SerializeField, Range(10f, 120f)]
    private float beamAngle = 50f;

    /// <summary>The beam dips this far below the view, as a held torch does.</summary>
    [SerializeField, Range(0f, 20f)]
    private float beamDip = 9f;

    /// <summary>The faint spill the body of the torch catches from its own lens.</summary>
    [SerializeField, Min(0f)]
    private float bodyGlow = 0.04f;

    /// <summary>The lens, relative to the holder - where the beam starts.</summary>
    [SerializeField]
    private Vector3 lensOffset = new Vector3(0f, 0f, 0.13f);


    // =========================================================
    // STATE
    // =========================================================

    private Transform cameraTransform;

    private Transform holder;

    private GameObject model;

    private Light beam;

    private Light glow;

    private PlayerHiding hiding;

    private bool on;


    public bool IsOn => on;


    /// <summary>
    /// Every flashlight whose beam is lit right now. The caretaker's
    /// VisionSense reads this the way it reads colliders - as a thing in
    /// the world it may or may not see - never as the player's position.
    /// </summary>
    public static readonly List<Flashlight> Lit = new List<Flashlight>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLit()
    {
        Lit.Clear();
    }

    /// <summary>True while the beam is actually shining (on, and not put away).</summary>
    public bool IsBeaming => beam != null && beam.enabled;

    public Vector3 BeamOrigin => beam.transform.position;

    public Vector3 BeamDirection => beam.transform.forward;

    public float BeamRange => beamRange;

    public float BeamAngle => beamAngle;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        hiding = GetComponent<PlayerHiding>();

        Camera camera = GetComponentInChildren<Camera>();

        if (camera == null)
        {
            Debug.LogWarning("[Flashlight] No camera under the player.", this);

            enabled = false;

            return;
        }

        cameraTransform = camera.transform;

        holder = new GameObject("Flashlight").transform;

        holder.SetParent(cameraTransform, false);

        holder.localPosition = holdOffset;

        if (modelPrefab != null)
        {
            model = Instantiate(modelPrefab, holder);

            model.name = "Flashlight Model";

            foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
            {
                Destroy(c);
            }

            /* A held item never shades the world it is lighting. */

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        GameObject beamGo = new GameObject("Beam");

        beamGo.transform.SetParent(holder, false);

        beamGo.transform.localPosition = lensOffset;

        beam = beamGo.AddComponent<Light>();

        beam.type = LightType.Spot;
        beam.color = beamColor;
        beam.intensity = beamIntensity;
        beam.range = beamRange;
        beam.spotAngle = beamAngle;
        beam.innerSpotAngle = beamAngle * 0.45f;
        beam.shadows = LightShadows.Soft;

        beamGo.transform.localRotation = Quaternion.Euler(beamDip, -1f, 0f);

        GameObject glowGo = new GameObject("Body Glow");

        glowGo.transform.SetParent(holder, false);

        glowGo.transform.localPosition = new Vector3(-0.05f, 0.08f, 0.05f);

        glow = glowGo.AddComponent<Light>();

        glow.type = LightType.Point;
        glow.color = beamColor;
        glow.intensity = bodyGlow;
        glow.range = 0.5f;
        glow.shadows = LightShadows.None;

        on = startOn;
    }


    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame && !Hidden)
        {
            on = !on;

            AudioClip click = GameSound.Get(on ? "FlashlightOn" : "FlashlightOff") ?? GameArt.Tick;

            GameSound.Play(click, holder.position, 0.6f, GameSound.Player, 1f, 12f);
        }

        bool shown = !Hidden;

        if (model != null && model.activeSelf != shown)
        {
            model.SetActive(shown);
        }

        beam.enabled = on && shown;

        glow.enabled = on && shown;

        SetLit(beam.enabled);
    }


    private void OnDisable()
    {
        SetLit(false);
    }


    private void SetLit(bool lit)
    {
        bool listed = Lit.Contains(this);

        if (lit && !listed)
        {
            Lit.Add(this);
        }
        else if (!lit && listed)
        {
            Lit.Remove(this);
        }
    }


    /// <summary>For tests and scripted moments: switches the beam on or off.</summary>
    public void SetOn(bool value)
    {
        on = value;
    }


    private void LateUpdate()
    {
        /* Trail the view a little - a held light, not a painted-on one. */

        holder.rotation = Quaternion.Slerp(
            holder.rotation,
            cameraTransform.rotation,
            1f - Mathf.Exp(-followSharpness * Time.deltaTime));
    }


    private bool Hidden => hiding != null && hiding.IsHiding;


    /// <summary>For the editor setup: assigns the model to hold.</summary>
    public void SetModel(GameObject prefab)
    {
        modelPrefab = prefab;
    }
}
