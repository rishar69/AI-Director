using System.Collections;
using UnityEngine;

/// <summary>
/// The kill. Replaces PlayerKillPlaceholder.
///
/// Forcing the camera onto the creature is the entire point. Dying to
/// something you never saw is confusing rather than frightening, and a
/// horror game only gets to show its monster properly at the moment it
/// no longer matters that you have seen it.
///
/// Like the placeholder, this lives on the PLAYER. The brain announces
/// a strike at a position and never learns whether it connected -
/// deciding that is the job of the thing entitled to know where the
/// player is.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerController))]
public class PlayerDeathSequence : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Hit Test")]
    [SerializeField, Min(0f)]
    private float bodyRadius = 0.5f;


    [Header("Sequence")]
    /// <summary>How long the camera takes to snap onto it.</summary>
    [SerializeField, Min(0.01f)]
    private float turnDuration = 0.28f;

    /// <summary>Time spent looking at it before the fade starts.</summary>
    [SerializeField, Min(0f)]
    private float holdDuration = 1.1f;

    [SerializeField, Min(0.01f)]
    private float fadeOutDuration = 0.7f;

    [SerializeField, Min(0.01f)]
    private float fadeInDuration = 0.9f;

    [SerializeField, Min(0f)]
    private float blackHoldDuration = 0.6f;


    [Header("Restart")]
    /// <summary>
    /// Being caught restarts the whole level: the museum reloads and the
    /// night begins again. Off, the older behaviour applies - wake in the
    /// lobby with keys and clocks kept.
    /// </summary>
    [SerializeField]
    private bool restartLevelOnDeath = true;


    [Header("Respawn")]
    [SerializeField]
    private bool respawnAtStart = true;

    /// <summary>
    /// How long the creature is driven off afterwards, so respawning
    /// does not drop the player straight back into its jaws.
    /// </summary>
    [SerializeField, Min(0f)]
    private float deterAfterKill = 20f;


    [Header("Debug")]
    [SerializeField]
    private bool logDeaths = true;


    // =========================================================
    // STATE
    // =========================================================

    private CharacterController controller;

    private PlayerController player;

    private PlayerHiding hiding;

    private Transform cameraPivot;

    private XenoBrain brain;

    private XenoBehaviourTree tree;

    private Vector3 spawnPosition;

    private Quaternion spawnRotation;

    private float fadeAlpha;

    private Texture2D blackPixel;


    public int Deaths { get; private set; }

    public bool IsDying { get; private set; }


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        player = GetComponent<PlayerController>();

        hiding = GetComponent<PlayerHiding>();

        Camera camera = GetComponentInChildren<Camera>();

        if (camera != null)
        {
            cameraPivot = camera.transform;
        }

        spawnPosition = transform.position;

        spawnRotation = transform.rotation;
    }


    private void OnDestroy()
    {
        if (blackPixel != null)
        {
            Destroy(blackPixel);
        }
    }


    private void OnDisable()
    {
        Unsubscribe();
    }


    private void Update()
    {
        /*
         * Hooked lazily, because the creature may be built by an editor
         * tool after the player is already in the scene.
         */

        if (brain == null && tree == null)
        {
            TrySubscribe();
        }
    }


    /// <summary>
    /// Hooks whichever decision layer is present.
    ///
    /// The behaviour tree wins when both exist, because the tree
    /// disables the state machine on Awake and subscribing to both
    /// would kill the player twice from one strike.
    /// </summary>
    private void TrySubscribe()
    {
        XenoBehaviourTree foundTree =
            FindFirstObjectByType<XenoBehaviourTree>();

        /*
         * Only an ENABLED tree. In the FSM comparison version the tree
         * component is present but switched off, and subscribing to it
         * meant the FSM's strikes were never heard - the player could
         * not die.
         */
        if (foundTree != null && foundTree.enabled)
        {
            tree = foundTree;

            tree.OnAttack += HandleAttack;

            return;
        }

        XenoBrain found = FindFirstObjectByType<XenoBrain>();

        if (found == null)
            return;

        brain = found;

        brain.OnAttack += HandleAttack;
    }


    private void Unsubscribe()
    {
        if (tree != null)
        {
            tree.OnAttack -= HandleAttack;

            tree = null;
        }

        if (brain == null)
            return;

        brain.OnAttack -= HandleAttack;

        brain = null;
    }


    /// <summary>
    /// The creature, whichever decision layer is driving it. The camera
    /// needs something to look at and neither layer is guaranteed.
    /// </summary>
    private Transform CreatureRoot
    {
        get
        {
            if (tree != null) return tree.transform;

            if (brain != null) return brain.transform;

            return null;
        }
    }


    // =========================================================
    // HIT TEST
    // =========================================================

    private void HandleAttack(
        Vector3 position,
        float reach)
    {
        if (IsDying)
            return;

        Vector3 delta = transform.position - position;

        delta.y = 0f;

        if (delta.magnitude > reach + bodyRadius)
        {
            /*
             * It struck where it believed the player was, and they were
             * not there. A perfectly good outcome.
             */

            if (logDeaths)
            {
                Debug.Log(
                    "[PlayerDeath] Attack missed by " +
                    (delta.magnitude - reach - bodyRadius)
                        .ToString("F1") + "m",
                    this
                );
            }

            return;
        }

        StartCoroutine(RunSequence());
    }


    // =========================================================
    // SEQUENCE
    // =========================================================

    private IEnumerator RunSequence()
    {
        IsDying = true;

        Deaths++;

        if (logDeaths)
        {
            Debug.Log("[PlayerDeath] Caught. Death #" + Deaths, this);
        }

        player.MovementLocked = true;

        player.LookLocked = true;

        yield return TurnToFaceCreature();

        yield return new WaitForSeconds(holdDuration);

        yield return Fade(0f, 1f, fadeOutDuration);

        if (restartLevelOnDeath)
        {
            /* What the NPC learned about this player carries into the next attempt. */
            XenoLearning.CarryOver();

            GameSession.NoteCaught(ObjectiveManager.Instance != null ? ObjectiveManager.Instance.Elapsed : 0f);

            GameSession.ReloadLevel();

            yield break;
        }

        Respawn();

        yield return new WaitForSeconds(blackHoldDuration);

        yield return Fade(1f, 0f, fadeInDuration);

        player.MovementLocked = false;

        player.LookLocked = false;

        IsDying = false;
    }


    /// <summary>
    /// Swings the body and the camera onto the creature together.
    /// </summary>
    private IEnumerator TurnToFaceCreature()
    {
        Transform creature = CreatureRoot;

        if (creature == null || cameraPivot == null)
            yield break;

        Quaternion startBody = transform.rotation;

        Quaternion startPitch = cameraPivot.localRotation;

        float elapsed = 0f;

        while (elapsed < turnDuration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.SmoothStep(
                0f,
                1f,
                elapsed / turnDuration
            );

            Vector3 target =
                creature.position + Vector3.up * 1.2f;

            Vector3 toTarget = target - cameraPivot.position;

            if (toTarget.sqrMagnitude > 0.001f)
            {
                Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);

                if (flat.sqrMagnitude > 0.001f)
                {
                    transform.rotation =
                        Quaternion.Slerp(
                            startBody,
                            Quaternion.LookRotation(flat),
                            t
                        );
                }

                /*
                 * Pitch is applied on the pivot in local space, which
                 * is where PlayerController keeps it - writing a world
                 * rotation here would fight it on the frame control
                 * comes back.
                 */

                float pitch =
                    -Mathf.Asin(
                        Mathf.Clamp(
                            toTarget.normalized.y,
                            -1f,
                            1f)
                    ) * Mathf.Rad2Deg;

                cameraPivot.localRotation =
                    Quaternion.Slerp(
                        startPitch,
                        Quaternion.Euler(pitch, 0f, 0f),
                        t
                    );
            }

            yield return null;
        }
    }


    private IEnumerator Fade(
        float from,
        float to,
        float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            fadeAlpha =
                Mathf.Lerp(from, to, elapsed / duration);

            yield return null;
        }

        fadeAlpha = to;
    }


    // =========================================================
    // RESPAWN
    // =========================================================

    private void Respawn()
    {
        if (hiding != null && hiding.IsHiding)
        {
            hiding.enabled = false;

            hiding.enabled = true;
        }

        if (respawnAtStart)
        {
            Teleport(spawnPosition, spawnRotation);
        }

        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.identity;
        }

        /*
         * Wipe what the creature knows and send it away, so respawning
         * does not drop the player straight back into its jaws.
         */

        if (tree != null)
        {
            tree.GetComponent<PerceptionSystem>()?.ResetPerception();

            tree.GetComponent<XenoSenses>()?.ResetAll();

            tree.GetComponent<XenoEventLog>()?.ResetAll();

            tree.GetComponent<XenoSuspiciousItems>()?.Clear();

            XenoBlackboard board = tree.Blackboard;

            if (board != null)
            {
                board.ResetAll();

                /*
                 * A ban rather than a deter: every attack branch opens
                 * with HasTimerExpired(ATTACK_BAN_TIMER), so this is
                 * the tree's own way of saying "not for a while".
                 */

                board.StartTimer(
                    deterAfterKill,
                    TimerId.AttackBan
                );
            }

            /*
             * The designed tree's Director. Without this it never heard
             * about a death, the creature stayed beside the respawn
             * point, saw the player the moment they reappeared, and
             * killed them again as soon as the attack ban ran out.
             */
            FindFirstObjectByType<XenoDirector>()?.NotifyPlayerCaught();
        }

        if (brain == null)
            return;

        brain.Perception?.ResetPerception();

        brain.ClearSearchMemory();

        brain.Deter(deterAfterKill);

        AIDirector director =
            FindFirstObjectByType<AIDirector>();

        if (director != null)
        {
            director.ResetPacing();
        }
    }


    private void Teleport(
        Vector3 position,
        Quaternion rotation)
    {
        bool wasEnabled = controller.enabled;

        controller.enabled = false;

        transform.SetPositionAndRotation(position, rotation);

        controller.enabled = wasEnabled;
    }


    // =========================================================
    // FADE OVERLAY
    // =========================================================

    private void OnGUI()
    {
        if (fadeAlpha <= 0.001f)
            return;

        if (blackPixel == null)
        {
            blackPixel = new Texture2D(1, 1);

            blackPixel.SetPixel(0, 0, Color.black);

            blackPixel.Apply();
        }

        Color previous = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, fadeAlpha);

        GUI.DrawTexture(
            new Rect(0f, 0f, Screen.width, Screen.height),
            blackPixel
        );

        GUI.color = previous;
    }
}
