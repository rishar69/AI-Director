using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Cameras and time controls for watching the creature think.
///
///   V          cycle view: Player -> Overhead (follow) -> Overhead (free) -> Chase
///   N          overhead follow target: creature / player
///   scroll     zoom the overhead views (PageUp / PageDown also work)
///   arrows     pan the free overhead view
///   P          pause / resume
///   - and =    slower / faster (0.1x .. 4x)
///   `          inspect mode: free the cursor (to click the tree viewer)
///              and freeze the player
///   H          show / hide this key list
///
/// The player stays controllable in every view, so you can play from
/// the overhead camera and watch the creature work out where you are.
/// </summary>
public class ObserverCamera : MonoBehaviour
{
    public enum View
    {
        Player = 0,
        OverheadFollow = 1,
        OverheadFree = 2,
        Chase = 3
    }


    [Header("Targets (found automatically when empty)")]
    [SerializeField]
    private Transform creature;

    [SerializeField]
    private PlayerController player;

    [Header("Start")]
    [SerializeField]
    private View startView = View.OverheadFollow;

    [Tooltip("Centre of the free overhead view.")]
    [SerializeField]
    private Vector3 freeCentre = Vector3.zero;

    [Header("Overhead")]
    [SerializeField, Min(4f)]
    private float followHeight = 16f;

    [SerializeField, Min(4f)]
    private float freeHeight = 34f;

    [SerializeField, Range(40f, 90f)]
    private float overheadPitch = 68f;

    [SerializeField, Min(1f)]
    private float panSpeed = 14f;

    [Header("Chase")]
    [SerializeField]
    private Vector3 chaseOffset = new Vector3(0f, 2.6f, -4.2f);

    [Header("Smoothing")]
    [SerializeField, Min(0.1f)]
    private float follow = 6f;


    /// <summary>The camera currently drawing the Game view, for overlays that project labels.</summary>
    public static Camera ActiveCamera { get; private set; }

    public View CurrentView => view;


    private View view;

    private Camera observer;

    private Camera playerCamera;

    private bool followPlayer;

    private bool paused;

    private float speed = 1f;

    private bool inspecting;

    private bool showHelp = true;

    private Vector3 freePan;

    private static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };

    private GUIStyle style;


    private void Awake()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();

        if (creature == null)
        {
            XenoBehaviourTree tree = FindFirstObjectByType<XenoBehaviourTree>();

            if (tree != null) creature = tree.transform;
        }

        if (player != null)
        {
            playerCamera = player.GetComponentInChildren<Camera>(true);
        }

        GameObject go = new GameObject("Observer Camera View");

        go.transform.SetParent(transform, false);

        observer = go.AddComponent<Camera>();

        observer.nearClipPlane = 0.1f;

        observer.farClipPlane = 300f;

        observer.fieldOfView = 55f;

        if (playerCamera != null)
        {
            observer.clearFlags = playerCamera.clearFlags;
            observer.backgroundColor = playerCamera.backgroundColor;
        }

        observer.enabled = false;

        freePan = freeCentre;

        /* Time starts running here; nothing in this scene owns a start card. */
        Time.timeScale = 1f;

        SetView(startView, snap: true);
    }


    private void OnDisable()
    {
        Time.timeScale = 1f;
    }


    private void Update()
    {
        Keyboard kb = Keyboard.current;

        if (kb != null)
        {
            if (kb.vKey.wasPressedThisFrame) SetView((View)(((int)view + 1) % 4), snap: true);

            if (kb.nKey.wasPressedThisFrame) followPlayer = !followPlayer;

            if (kb.hKey.wasPressedThisFrame) showHelp = !showHelp;

            if (kb.pKey.wasPressedThisFrame)
            {
                paused = !paused;
                ApplyTime();
            }

            if (kb.minusKey.wasPressedThisFrame) StepSpeed(-1);

            if (kb.equalsKey.wasPressedThisFrame) StepSpeed(+1);

            if (kb.backquoteKey.wasPressedThisFrame) SetInspect(!inspecting);

            if (view == View.OverheadFree)
            {
                Vector3 pan = Vector3.zero;

                if (kb.leftArrowKey.isPressed) pan.x -= 1f;
                if (kb.rightArrowKey.isPressed) pan.x += 1f;
                if (kb.upArrowKey.isPressed) pan.z += 1f;
                if (kb.downArrowKey.isPressed) pan.z -= 1f;

                freePan += pan * panSpeed * Time.unscaledDeltaTime;
            }

            float zoom = 0f;

            if (kb.pageUpKey.isPressed) zoom -= 1f;
            if (kb.pageDownKey.isPressed) zoom += 1f;

            if (Mouse.current != null && !inspecting)
            {
                zoom -= Mathf.Clamp(Mouse.current.scroll.ReadValue().y, -1f, 1f) * 6f;
            }

            Zoom(zoom * 10f * Time.unscaledDeltaTime);
        }
    }


    private void LateUpdate()
    {
        if (view == View.Player)
            return;

        Place(false);
    }


    private void SetView(View next, bool snap)
    {
        view = next;

        bool usePlayer = view == View.Player || playerCamera == null;

        if (playerCamera != null) playerCamera.enabled = usePlayer;

        observer.enabled = !usePlayer;

        ActiveCamera = usePlayer ? playerCamera : observer;

        if (!usePlayer && snap) Place(true);
    }


    private void Place(bool snap)
    {
        Vector3 pos;
        Quaternion rot;

        Transform target = followPlayer && player != null ? player.transform : creature;

        switch (view)
        {
            case View.OverheadFollow:
            {
                Vector3 focus = target != null ? target.position : freePan;

                rot = Quaternion.Euler(overheadPitch, 0f, 0f);

                pos = focus - rot * Vector3.forward * followHeight;

                break;
            }

            case View.OverheadFree:
            {
                rot = Quaternion.Euler(overheadPitch, 0f, 0f);

                pos = freePan - rot * Vector3.forward * freeHeight;

                break;
            }

            default:
            {
                if (creature == null)
                    return;

                pos = creature.TransformPoint(chaseOffset);

                Vector3 look = creature.position + Vector3.up * 1.2f + creature.forward * 3f;

                rot = Quaternion.LookRotation(look - pos);

                break;
            }
        }

        if (snap)
        {
            observer.transform.SetPositionAndRotation(pos, rot);

            return;
        }

        float t = 1f - Mathf.Exp(-follow * Time.unscaledDeltaTime);

        observer.transform.SetPositionAndRotation(
            Vector3.Lerp(observer.transform.position, pos, t),
            Quaternion.Slerp(observer.transform.rotation, rot, t));
    }


    private void Zoom(float delta)
    {
        if (Mathf.Approximately(delta, 0f))
            return;

        if (view == View.OverheadFollow) followHeight = Mathf.Clamp(followHeight + delta, 4f, 60f);

        if (view == View.OverheadFree) freeHeight = Mathf.Clamp(freeHeight + delta, 6f, 90f);
    }


    private void StepSpeed(int dir)
    {
        int i = System.Array.IndexOf(Speeds, speed);

        if (i < 0) i = 3;

        speed = Speeds[Mathf.Clamp(i + dir, 0, Speeds.Length - 1)];

        ApplyTime();
    }


    private void ApplyTime()
    {
        Time.timeScale = paused ? 0f : speed;
    }


    private void SetInspect(bool on)
    {
        inspecting = on;

        if (player != null)
        {
            /* PlayerController frees the cursor when disabled and locks it again when enabled. */
            player.enabled = !on;
        }

        Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked;

        Cursor.visible = on;
    }


    private void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
        }

        string status =
            "view " + view + (view == View.OverheadFollow ? (followPlayer ? " (player)" : " (creature)") : "") +
            "    time " + (paused ? "PAUSED" : speed + "x") +
            (inspecting ? "    INSPECT (cursor free, player frozen)" : "");

        string help = showHelp
            ? "V view   N follow target   scroll zoom   arrows pan   P pause   -/= speed   ` inspect   " +
              "F1 old overlay   F2 tree   F3 creature   F4 markers   F5 tree mode   H hide keys"
            : "H keys";

        float w = Mathf.Min(Screen.width - 20f, 980f);

        Rect r = new Rect((Screen.width - w) * 0.5f, Screen.height - 46f, w, 40f);

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = paused ? new Color(1f, 0.72f, 0.2f) : Color.white;

        GUI.Label(new Rect(r.x, r.y + 2f, r.width, 18f), status, style);

        GUI.color = new Color(0.75f, 0.77f, 0.8f);

        GUI.Label(new Rect(r.x, r.y + 20f, r.width, 18f), help, style);

        GUI.color = Color.white;
    }
}
