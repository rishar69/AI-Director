using UnityEngine;

/// <summary>
/// Stands in for the death and respawn flow until Phase 5 builds one.
///
/// PLACEHOLDER. Replace this when player vitals, the death camera and
/// checkpoints exist.
///
/// It lives on the player rather than in the brain deliberately. The
/// brain announces "I struck at this point with this reach" and knows
/// nothing more; whether that connects is decided here, by the thing
/// that can legitimately know where the player is. That is the same
/// separation the whole two-brain design rests on, applied to damage.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerKillPlaceholder : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Hit Test")]
    /// <summary>
    /// Added to the attack's own reach when deciding whether it landed,
    /// to account for the player's body radius.
    /// </summary>
    [SerializeField, Min(0f)]
    private float bodyRadius = 0.5f;


    [Header("Response")]
    [SerializeField]
    private bool respawnAtStart = true;

    /// <summary>
    /// How long the creature is driven off after a kill, so the player
    /// is not immediately killed again on respawn.
    /// </summary>
    [SerializeField, Min(0f)]
    private float deterAfterKill = 20f;


    [Header("Debug")]
    [SerializeField]
    private bool logKills = true;


    // =========================================================
    // STATE
    // =========================================================

    private CharacterController controller;

    private XenoBrain brain;

    private Vector3 spawnPosition;

    private Quaternion spawnRotation;

    /// <summary>Times the player has been caught this session.</summary>
    public int Deaths { get; private set; }


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        spawnPosition = transform.position;

        spawnRotation = transform.rotation;
    }


    private void OnDisable()
    {
        Unsubscribe();
    }


    private void Update()
    {
        /*
         * Hooked up lazily rather than in Start, because the creature
         * may be built by the Phase 3 editor tool after the player is
         * already in the scene.
         */

        if (brain == null)
        {
            TrySubscribe();
        }
    }


    private void TrySubscribe()
    {
        XenoBrain found = FindFirstObjectByType<XenoBrain>();

        if (found == null)
            return;

        brain = found;

        brain.OnAttack += HandleAttack;
    }


    private void Unsubscribe()
    {
        if (brain == null)
            return;

        brain.OnAttack -= HandleAttack;

        brain = null;
    }


    // =========================================================
    // HIT TEST
    // =========================================================

    private void HandleAttack(
        Vector3 position,
        float reach)
    {
        Vector3 delta = transform.position - position;

        delta.y = 0f;

        if (delta.magnitude > reach + bodyRadius)
        {
            /*
             * It struck where it believed the player was, and the
             * player was not there. Perfectly good outcome.
             */

            if (logKills)
            {
                Debug.Log(
                    "[PlayerKill] Attack missed by " +
                    (delta.magnitude - reach - bodyRadius)
                        .ToString("F1") + "m",
                    this
                );
            }

            return;
        }

        Kill();
    }


    private void Kill()
    {
        Deaths++;

        if (logKills)
        {
            Debug.Log(
                "[PlayerKill] Caught. Death #" + Deaths,
                this
            );
        }

        if (respawnAtStart)
        {
            Teleport(spawnPosition, spawnRotation);
        }

        if (brain != null)
        {
            /*
             * Wipe what it knows and send it away, so respawning does
             * not drop the player straight back into its jaws.
             */

            brain.Perception?.ResetPerception();

            brain.ClearSearchMemory();

            brain.Deter(deterAfterKill);
        }
    }


    private void Teleport(
        Vector3 position,
        Quaternion rotation)
    {
        /*
         * CharacterController overwrites transform writes while it is
         * enabled, so it has to be switched off around the move.
         */

        bool wasEnabled = controller.enabled;

        controller.enabled = false;

        transform.SetPositionAndRotation(position, rotation);

        controller.enabled = wasEnabled;
    }
}
