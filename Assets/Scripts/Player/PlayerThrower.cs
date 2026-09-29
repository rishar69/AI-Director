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
    private static Noisemaker BuildPlaceholder()
    {
        GameObject obj =
            GameObject.CreatePrimitive(PrimitiveType.Sphere);

        obj.name = "Noisemaker (placeholder)";

        obj.transform.localScale = Vector3.one * 0.18f;

        Rigidbody body = obj.AddComponent<Rigidbody>();

        body.mass = 0.4f;

        return obj.AddComponent<Noisemaker>();
    }


    public void Resupply(int amount)
    {
        Remaining = Mathf.Max(0, Remaining + amount);
    }
}
