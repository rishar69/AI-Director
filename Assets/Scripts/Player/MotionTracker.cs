using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tells you where it is, and tells it where you are.
///
/// The best piece of counterplay design in the genre, because it is not
/// really counterplay at all - it is a trade. Every ping is a real
/// NoiseEvent on the same bus as your footsteps, so the information you
/// gain costs you concealment, and the moment you most want to check is
/// exactly the moment using it is most dangerous.
///
/// Reading creature positions is legitimate here: this is a player-side
/// device, and it only sees things within its own range.
/// </summary>
public class MotionTracker : MonoBehaviour
{
    private readonly struct Contact
    {
        public readonly Vector3 Position;
        public readonly float Distance;

        public Contact(Vector3 position, float distance)
        {
            Position = position;
            Distance = distance;
        }
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Input")]
    [SerializeField]
    private Key toggleKey = Key.T;


    [Header("Scanning")]
    [SerializeField, Min(1f)]
    private float range = 30f;

    [SerializeField, Min(0.1f)]
    private float pingInterval = 1.2f;

    [SerializeField]
    private LayerMask creatureMask = ~0;


    [Header("Cost")]
    /// <summary>
    /// The noise each ping makes. Small compared to a footstep, but
    /// it repeats, and it repeats while you are standing still and
    /// otherwise silent - which is when it stands out most.
    /// </summary>
    [SerializeField, Min(0f)]
    private float pingNoiseRadius = 5f;


    [Header("Display")]
    [SerializeField]
    private bool showDisplay = true;

    [SerializeField, Min(40f)]
    private float displaySize = 150f;

    [SerializeField]
    private Vector2 displayMargin = new Vector2(18f, 18f);


    // =========================================================
    // STATE
    // =========================================================

    private readonly List<Contact> contacts = new List<Contact>();

    private readonly Collider[] overlapResults = new Collider[16];

    private Transform facingSource;

    private float nextPingTime;

    private float lastPingTime = -999f;

    private Texture2D pixel;


    public bool IsActive { get; private set; }

    public int ContactCount => contacts.Count;

    /// <summary>
    /// Distance to the closest contact, or -1 when nothing is on the
    /// screen. Only meaningful while active.
    /// </summary>
    public float NearestDistance { get; private set; } = -1f;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        Camera camera = GetComponentInChildren<Camera>();

        facingSource = camera != null ? camera.transform : transform;
    }


    private void OnDestroy()
    {
        if (pixel != null)
        {
            Destroy(pixel);
        }
    }


    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null &&
            keyboard[toggleKey].wasPressedThisFrame)
        {
            IsActive = !IsActive;

            if (!IsActive)
            {
                contacts.Clear();

                NearestDistance = -1f;
            }
        }

        if (!IsActive)
            return;

        if (Time.time < nextPingTime)
            return;

        Ping();
    }


    // =========================================================
    // PINGING
    // =========================================================

    private void Ping()
    {
        nextPingTime = Time.time + pingInterval;

        lastPingTime = Time.time;

        contacts.Clear();

        NearestDistance = -1f;

        int count =
            Physics.OverlapSphereNonAlloc(
                transform.position,
                range,
                overlapResults,
                creatureMask,
                QueryTriggerInteraction.Ignore
            );

        for (int i = 0; i < count; i++)
        {
            Collider collider = overlapResults[i];

            if (collider == null)
                continue;

            /*
             * Only things with a brain register. Scenery on the NPC
             * layer should not show up as a contact.
             */

            if (collider.GetComponentInParent<XenoBrain>() == null)
                continue;

            Vector3 position = collider.bounds.center;

            float distance =
                Vector3.Distance(transform.position, position);

            contacts.Add(new Contact(position, distance));

            if (NearestDistance < 0f || distance < NearestDistance)
            {
                NearestDistance = distance;
            }
        }

        /*
         * The cost. Emitted whether or not anything was found - the
         * device does not know in advance, and neither should the
         * player.
         */

        NoiseBus.Emit(
            transform.position,
            pingNoiseRadius,
            NoiseType.Device,
            gameObject
        );
    }


    // =========================================================
    // DISPLAY
    // =========================================================

    private void OnGUI()
    {
        if (!IsActive || !showDisplay)
            return;

        EnsurePixel();

        Rect area =
            new Rect(
                Screen.width - displaySize - displayMargin.x,
                Screen.height - displaySize - displayMargin.y,
                displaySize,
                displaySize
            );

        DrawBackground(area);

        Vector2 centre = area.center;

        float radius = displaySize * 0.5f - 8f;

        DrawSweep(centre, radius);

        DrawContacts(centre, radius);
    }


    private void EnsurePixel()
    {
        if (pixel != null)
            return;

        pixel = new Texture2D(1, 1);

        pixel.SetPixel(0, 0, Color.white);

        pixel.Apply();
    }


    private void DrawBackground(Rect area)
    {
        GUI.color = new Color(0f, 0.05f, 0.02f, 0.75f);

        GUI.DrawTexture(area, pixel);

        GUI.color = new Color(0.3f, 1f, 0.5f, 0.35f);

        DrawBox(area.center, 3f);

        GUI.color = Color.white;
    }


    /// <summary>
    /// A brightness pulse timed to the ping, so the display visibly
    /// costs something each time it refreshes.
    /// </summary>
    private void DrawSweep(
        Vector2 centre,
        float radius)
    {
        float age = Time.time - lastPingTime;

        float fade = Mathf.Clamp01(1f - age / pingInterval);

        GUI.color = new Color(0.3f, 1f, 0.5f, 0.10f + fade * 0.25f);

        GUI.DrawTexture(
            new Rect(
                centre.x - radius,
                centre.y - radius,
                radius * 2f,
                radius * 2f),
            pixel
        );

        GUI.color = Color.white;
    }


    private void DrawContacts(
        Vector2 centre,
        float radius)
    {
        Vector3 forward = facingSource.forward;

        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
            return;

        forward.Normalize();

        Vector3 right =
            Vector3.Cross(Vector3.up, forward);

        for (int i = 0; i < contacts.Count; i++)
        {
            Contact contact = contacts[i];

            Vector3 delta = contact.Position - transform.position;

            delta.y = 0f;

            /*
             * Rotated into the player's frame so the blip sits where
             * the thing actually is relative to where they are looking.
             */

            float forwardAmount = Vector3.Dot(delta, forward);

            float rightAmount = Vector3.Dot(delta, right);

            float scale = radius / range;

            Vector2 point =
                new Vector2(
                    centre.x + rightAmount * scale,
                    centre.y - forwardAmount * scale
                );

            float proximity =
                1f - Mathf.Clamp01(contact.Distance / range);

            GUI.color =
                new Color(1f, 0.3f - proximity * 0.2f, 0.2f, 0.95f);

            DrawBox(point, 4f + proximity * 4f);
        }

        GUI.color = Color.white;
    }


    private void DrawBox(
        Vector2 centre,
        float halfSize)
    {
        GUI.DrawTexture(
            new Rect(
                centre.x - halfSize,
                centre.y - halfSize,
                halfSize * 2f,
                halfSize * 2f),
            pixel
        );
    }
}
