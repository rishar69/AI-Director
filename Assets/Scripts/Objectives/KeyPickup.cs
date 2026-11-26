using UnityEngine;

/// <summary>
/// A gallery key lying somewhere in the museum.
///
/// Two kinds: one on a table you simply take, and one locked in a
/// display case you have to smash. Smashing is fast but very loud - it
/// is the first time the game asks the player to trade noise for
/// progress, and it asks early.
/// </summary>
public class KeyPickup : Interactable
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [SerializeField]
    private GalleryKey key;

    /// <summary>Locked in a case - hold to smash it.</summary>
    [SerializeField]
    private bool inCase;

    /// <summary>The case, whose glass disappears when smashed.</summary>
    [SerializeField]
    private GameObject caseRoot;

    [SerializeField]
    private Transform visual;


    [Header("Noise")]
    [SerializeField, Min(0f)]
    private float smashNoiseRadius = 26f;

    [SerializeField, Min(0f)]
    private float pickupNoiseRadius = 1.5f;


    private bool taken;

    private float spin;


    public GalleryKey Key => key;

    public override string Label => ObjectiveManager.KeyName(key);


    public void Setup(GalleryKey galleryKey, Transform keyVisual, GameObject lockedCase)
    {
        key = galleryKey;
        visual = keyVisual;
        caseRoot = lockedCase;
        inCase = lockedCase != null;
    }


    // =========================================================
    // PROMPT
    // =========================================================

    public override InteractPrompt GetPrompt()
    {
        if (taken)
            return InteractPrompt.None;

        if (inCase)
        {
            return new InteractPrompt
            {
                State = InteractState.Available,
                Action = "Smash the case - take the " + Label,
                Detail = "Breaking glass carries through the halls",
                Hold = true,
                Loud = true
            };
        }

        return new InteractPrompt
        {
            State = InteractState.Available,
            Action = "Take the " + Label,
            Detail = "Opens the " + ObjectiveManager.ClockName(key)
        };
    }


    // =========================================================
    // INTERACTION
    // =========================================================

    public override void Interact(GameObject who)
    {
        if (taken || inCase)
            return;

        Take(who);
    }


    protected override void OnHoldComplete(GameObject who)
    {
        if (taken)
            return;

        GameArt.Play(GameArt.Smash, FocusPoint, 1f);

        NoiseBus.Emit(FocusPoint, smashNoiseRadius, NoiseType.Object, gameObject);

        ShatterGlass();

        Take(who);
    }


    private void Take(GameObject who)
    {
        taken = true;

        GameArt.Play(GameArt.Pickup, FocusPoint, 0.8f);

        if (pickupNoiseRadius > 0f)
        {
            NoiseBus.Emit(FocusPoint, pickupNoiseRadius, NoiseType.Object, who);
        }

        if (visual != null)
        {
            visual.gameObject.SetActive(false);
        }

        ObjectiveManager.Instance?.GiveKey(key);
    }


    /// <summary>
    /// Hides every glass surface of the case. The case models carry
    /// their glass as a separate material, which is all there is to go
    /// on without real breakable geometry.
    /// </summary>
    private void ShatterGlass()
    {
        if (caseRoot == null)
            return;

        foreach (Renderer r in caseRoot.GetComponentsInChildren<Renderer>(true))
        {
            /*
             * Only renderers that are glass all the way through. A null
             * material slot would render as the magenta error shader,
             * so a renderer mixing glass and frame is left intact.
             */

            Material[] mats = r.sharedMaterials;

            bool allGlass = mats.Length > 0;

            foreach (Material m in mats)
            {
                allGlass &= m != null && m.name.ToLowerInvariant().Contains("glass");
            }

            if (allGlass)
            {
                r.enabled = false;
            }
        }
    }


    // =========================================================
    // PRESENTATION
    // =========================================================

    private void Update()
    {
        if (taken || visual == null)
            return;

        /* A slow turn, so it catches the light and reads as "loot". */

        spin += Time.deltaTime * 40f;

        visual.localRotation = Quaternion.Euler(0f, spin, 0f);
    }
}
