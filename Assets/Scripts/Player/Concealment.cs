using UnityEngine;

/// <summary>
/// Makes a thing harder to notice.
///
/// Deliberately generic rather than player-specific - the senses look
/// for this on whatever they find, so a crate, a corpse or a second
/// creature could carry one too.
///
/// This is the seam that lets hiding exist without the alien needing to
/// know what a locker is. VisionSense and ProximitySense scale their
/// confidence by Visibility; PlayerNoiseEmitter scales its footstep
/// radius by Noise. Nothing in the brain is involved.
/// </summary>
public class Concealment : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Multipliers")]
    /// <summary>
    /// How visible this is, 1 being fully exposed and 0 being
    /// undetectable by sight.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float visibility = 1f;

    /// <summary>
    /// Scales the radius of noises this thing makes.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float noise = 1f;


    public float Visibility => visibility;

    public float Noise => noise;

    /// <summary>True whenever anything is being suppressed.</summary>
    public bool IsConcealed => visibility < 0.999f;


    // =========================================================
    // API
    // =========================================================

    public void Set(
        float newVisibility,
        float newNoise)
    {
        visibility = Mathf.Clamp01(newVisibility);

        noise = Mathf.Clamp01(newNoise);
    }


    public void SetVisibility(float value)
    {
        visibility = Mathf.Clamp01(value);
    }


    /// <summary>Back to fully exposed.</summary>
    public void Clear()
    {
        visibility = 1f;

        noise = 1f;
    }


    // =========================================================
    // LOOKUP
    // =========================================================

    /// <summary>
    /// Visibility multiplier for whatever a sense just found, or 1 when
    /// the thing carries no Concealment at all.
    /// </summary>
    public static float VisibilityOf(Component found)
    {
        if (found == null)
            return 1f;

        Concealment concealment =
            found.GetComponentInParent<Concealment>();

        return concealment != null
            ? concealment.Visibility
            : 1f;
    }
}
