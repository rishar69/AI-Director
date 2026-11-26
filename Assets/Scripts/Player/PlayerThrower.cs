using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Throws noisemakers.
///
/// Builds a plain sphere at runtime when no prefab is assigned, so the
/// mechanic is testable with zero authoring.
/// </summary>
public class PlayerThrower : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Input")]
    [SerializeField]
    private Key throwKey = Key.G;


    [Header("Projectile")]
    /// <summary>
    /// Optional. Must carry a Rigidbody and a Noisemaker. Left empty,
    /// a placeholder sphere is generated instead.
    /// </summary>
    [SerializeField]
    private Noisemaker projectilePrefab;

    [SerializeField, Min(1f)]
    private float throwSpeed = 12f;

    /// <summary>
    /// Lobbed slightly upward so it arcs rather than firing flat.
    /// </summary>
    [SerializeField, Range(0f, 45f)]
    private float throwArcDegrees = 12f;


    [Header("Supply")]
    [SerializeField, Min(0)]
    private int startingCount = 5;

    [SerializeField, Min(0f)]
    private float cooldown = 0.6f;


    [Header("Debug")]
    [SerializeField]
    private bool logThrows = true;


    // =========================================================
    // STATE
    // =========================================================

    private Transform origin;

    private PlayerHiding hiding;

    private float nextThrowTime;


    public int Remaining { get; private set; }


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        Remaining = startingCount;

        hiding = GetComponent<PlayerHiding>();

        Camera camera = GetComponentInChildren<Camera>();

        origin = camera != null ? camera.transform : transform;
    }


    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (!keyboard[throwKey].wasPressedThisFrame)
            return;

        TryThrow();
    }


    // =========================================================
    // THROWING
    // =========================================================

    public bool TryThrow()
    {
        if (Remaining <= 0)
            return false;

        if (Time.time < nextThrowTime)
            return false;

        /*
         * Throwing from inside a locker would put the object inside the
         * geometry, and hurling something across the room while
         * supposedly hiding is not a mechanic worth having.
         */

        if (hiding != null && hiding.IsHiding)
            return false;

        Vector3 direction =
            Quaternion.AngleAxis(
                -throwArcDegrees,
                origin.right) *
            origin.forward;

        Noisemaker projectile = Spawn();

        projectile.transform.position =
            origin.position + origin.forward * 0.6f;

        Rigidbody body = projectile.GetComponent<Rigidbody>();

        body.linearVelocity = direction * throwSpeed;

        Remaining--;

        nextThrowTime = Time.time + cooldown;

        if (logThrows)
        {
            Debug.Log(
                "[PlayerThrower] Threw noisemaker. " +
                Remaining + " left.",
                this
            );
        }

        return true;
    }


    private Noisemaker Spawn()
    {
        if (projectilePrefab != null)
        {
            return Instantiate(projectilePrefab);
        }

        return BuildPlaceholder();
    }


    /// <summary>
    /// A small sphere with the right components on it, so the mechanic
    /// works before any art exists.
    /// </summary>
    private static Mesh pebbleMesh;

    private static Material pebbleMaterial;

    private static Material trailMaterial;

    /// <summary>
    /// A small grey stone, made in code: a sphere pushed in and out by a
    /// smooth noise so it reads as a rock, not a ball. It leaves a faint
    /// trail so the throw can be followed in the dark.
    /// </summary>
    private static Noisemaker BuildPlaceholder()
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);

        obj.name = "Pebble";

        if (pebbleMesh == null)
        {
            Mesh source = obj.GetComponent<MeshFilter>().sharedMesh;

            pebbleMesh = Object.Instantiate(source);

            Vector3[] v = pebbleMesh.vertices;

            for (int i = 0; i < v.Length; i++)
            {
                Vector3 d = v[i].normalized;

                float bump =
                    0.18f * Mathf.Sin(d.x * 5.1f + 1.3f) * Mathf.Cos(d.y * 4.3f) +
                    0.12f * Mathf.Sin(d.z * 7.7f + d.x * 2.1f) +
                    0.08f * Mathf.Cos(d.y * 9.4f + 0.7f);

                v[i] = Vector3.Scale(d * 0.5f * (1f + bump), new Vector3(1.15f, 0.8f, 1f));
            }

            pebbleMesh.vertices = v;
            pebbleMesh.RecalculateNormals();
            pebbleMesh.RecalculateBounds();
        }

        obj.GetComponent<MeshFilter>().sharedMesh = pebbleMesh;

        if (pebbleMaterial == null)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");

            pebbleMaterial = new Material(lit != null ? lit : Shader.Find("Standard"));
            pebbleMaterial.color = new Color(0.62f, 0.60f, 0.55f);

            if (pebbleMaterial.HasProperty("_Smoothness"))
            {
                pebbleMaterial.SetFloat("_Smoothness", 0.25f);
            }

            /* A faint glint, so it can be followed in a dark room. */
            if (pebbleMaterial.HasProperty("_EmissionColor"))
            {
                pebbleMaterial.EnableKeyword("_EMISSION");
                pebbleMaterial.SetColor("_EmissionColor", new Color(0.16f, 0.155f, 0.14f));
            }
        }

        obj.GetComponent<MeshRenderer>().sharedMaterial = pebbleMaterial;

        obj.transform.localScale = Vector3.one * 0.14f;

        TrailRenderer trail = obj.AddComponent<TrailRenderer>();

        if (trailMaterial == null)
        {
            trailMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        trail.sharedMaterial = trailMaterial;
        trail.time = 0.5f;
        trail.startWidth = 0.05f;
        trail.endWidth = 0f;
        trail.startColor = new Color(1f, 0.97f, 0.9f, 0.6f);
        trail.endColor = new Color(1f, 0.97f, 0.9f, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Rigidbody body = obj.AddComponent<Rigidbody>();

        body.mass = 0.4f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        return obj.AddComponent<Noisemaker>();
    }


    public void Resupply(int amount)
    {
        Remaining = Mathf.Max(0, Remaining + amount);
    }
}
