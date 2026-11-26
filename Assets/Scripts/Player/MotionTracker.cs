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

    /// <summary>Size at a 1920 x 1080 reference; scaled like the HUD canvas.</summary>
    [SerializeField, Min(40f)]
    private float displaySize = 200f;

    /// <summary>
    /// Bottom-right placement in reference pixels. It sits directly
    /// above the HUD's tool box (40 px from the right, 36 + 214 px
    /// from the bottom) so the two never overlap.
    /// </summary>
    [SerializeField]
    private Vector2 displayOffset = new Vector2(40f, 264f);


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

        /* The HUD's full-screen cards (warning, caught, escape) freeze time; stay out of them. */
        if (Time.timeScale == 0f)
            return;

        EnsurePixel();

        /* Same scaling as the HUD's CanvasScaler (1920 x 1080, match 0.5). */
        float s = Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f));

        float size = displaySize * s;

        Rect area =
            new Rect(
                Screen.width - displayOffset.x * s - size,
                Screen.height - displayOffset.y * s - size,
                size,
                size
            );

        DrawBackground(area);

        Vector2 centre = area.center;

        float radius = size * 0.5f - 8f * s;

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
        /* The HUD's box style: near-black fill, thin light border. */
        GUI.color = new Color(0.025f, 0.03f, 0.028f, 0.84f);

        GUI.DrawTexture(area, pixel);

        GUI.color = new Color(0.86f, 0.84f, 0.78f, 0.55f);

        GUI.DrawTexture(new Rect(area.x, area.y, area.width, 1f), pixel);
        GUI.DrawTexture(new Rect(area.x, area.yMax - 1f, area.width, 1f), pixel);
        GUI.DrawTexture(new Rect(area.x, area.y, 1f, area.height), pixel);
        GUI.DrawTexture(new Rect(area.xMax - 1f, area.y, 1f, area.height), pixel);

        /* You are the centre. */
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

        GUI.color = new Color(0.86f, 0.84f, 0.78f, 0.05f + fade * 0.15f);

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
