using UnityEngine;

/// <summary>
/// The world-space sign over anything that matters.
///
///   gold diamond + warm glow   - a task you can do now
///   red padlock                - a task you cannot do yet
///   pale blue eye (close only) - a place to hide
///
/// Scenery gets nothing. The rule is simple enough to learn from the
/// intro card and never needs repeating.
///
/// Markers are occluded by walls like anything else in the world; the
/// Tab overlay in the HUD is the through-walls version.
/// </summary>
public class InteractionMarker : MonoBehaviour
{
    public enum Kind
    {
        Task,
        Hide
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [SerializeField]
    private Kind kind = Kind.Task;

    [SerializeField]
    private Interactable target;

    /// <summary>Icon height above the focus point.</summary>
    [SerializeField]
    private float height = 0.55f;

    /// <summary>Hide markers only show this close, so rooms stay dark.</summary>
    [SerializeField, Min(1f)]
    private float hideVisibleDistance = 7f;

    [SerializeField, Min(1f)]
    private float taskVisibleDistance = 30f;


    private SpriteRenderer icon;

    private Light glow;

    private Transform view;

    private PlayerHiding hiding;

    private float phase;


    public void Setup(Kind markerKind, Interactable markerTarget, float iconHeight)
    {
        kind = markerKind;
        target = markerTarget;
        height = iconHeight;
    }


    // =========================================================
    // UNITY
    // =========================================================

    private void Start()
    {
        GameObject iconGo = new GameObject("Marker Icon");

        iconGo.transform.SetParent(transform, false);

        icon = iconGo.AddComponent<SpriteRenderer>();

        icon.sprite = kind == Kind.Hide ? GameArt.Eye : GameArt.Diamond;

        /*
         * The default sprite material is unlit, so the sign reads in the
         * darkest room without a light of its own.
         */

        icon.sortingOrder = 10;

        if (kind == Kind.Task)
        {
            GameObject glowGo = new GameObject("Marker Glow");

            glowGo.transform.SetParent(transform, false);

            glow = glowGo.AddComponent<Light>();

            glow.type = LightType.Point;
            glow.range = 2.5f;
            glow.shadows = LightShadows.None;
        }

        phase = Random.value * 10f;
    }


    private void LateUpdate()
    {
        if (view == null)
        {
            Camera main = Camera.main;

            if (main == null)
                return;

            view = main.transform;

            hiding = main.GetComponentInParent<PlayerHiding>();
        }

        Vector3 anchor = target != null ? target.FocusPoint : transform.position;

        float distance = Vector3.Distance(view.position, anchor);

        InteractState state = kind == Kind.Hide
            ? InteractState.Available
            : (target != null ? target.TaskState : InteractState.None);

        bool show = state != InteractState.None;

        if (kind == Kind.Hide)
        {
            show &= distance < hideVisibleDistance &&
                    (hiding == null || !hiding.IsHiding);
        }
        else
        {
            show &= distance < taskVisibleDistance;
        }

        icon.enabled = show;

        if (glow != null)
        {
            glow.enabled = show && state == InteractState.Available;
        }

        if (!show)
            return;

        phase += Time.deltaTime;

        float bob = Mathf.Sin(phase * 2.2f) * 0.05f;

        Vector3 position = anchor + Vector3.up * (height + bob);

        icon.transform.position = position;

        /* Billboard, and a constant-ish screen size. */

        icon.transform.rotation =
            Quaternion.LookRotation(icon.transform.position - view.position, Vector3.up);

        float size = Mathf.Clamp(distance * 0.045f, 0.18f, 0.9f);

        icon.transform.localScale = Vector3.one * size;

        Color color;

        if (kind == Kind.Hide)
        {
            color = GameArt.Hide;

            color.a = Mathf.Clamp01((hideVisibleDistance - distance) / 2.5f) * 0.8f;

            icon.sprite = GameArt.Eye;
        }
        else if (state == InteractState.Available)
        {
            color = GameArt.Brass;

            color.a = 0.75f + 0.25f * Mathf.Sin(phase * 3f);

            icon.sprite = GameArt.Diamond;
        }
        else
        {
            color = GameArt.Locked;

            color.a = 0.75f;

            icon.sprite = GameArt.Lock;
        }

        icon.color = color;

        if (glow != null && glow.enabled)
        {
            glow.transform.position = anchor + Vector3.up * 0.25f;

            glow.color = GameArt.Brass;

            glow.intensity = 0.55f + 0.25f * Mathf.Sin(phase * 3f);
        }
    }
}
