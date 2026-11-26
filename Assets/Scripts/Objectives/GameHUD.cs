using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The player-facing interface.
///
/// Visual language: near-black boxes with a thin light border, serif
/// type, wide-spaced capitals for titles, and every key shown in its own
/// small key box. Nothing on screen that the player does not need now.
///
///   - Objective (top left): ONE line - what to do now - and a short
///     hint of where. When it is done, the next one replaces it.
///   - Tools (bottom right): the four tools, their keys and their state
///     (on/off, fuel, pebbles left).
///   - Movement keys (bottom left).
///   - Prompt (centre): what E does right now - or, in red, why it cannot.
///   - Hold ring, noise vignette, Tab markers for what can be done now.
///   - Messages (top centre): what just happened.
///   - Content warning and title card once per session, a "caught" card
///     when the level restarts, and the escape screen.
///
/// Built entirely in code at runtime, so there is no prefab to wire.
/// </summary>
public class GameHUD : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [SerializeField]
    private bool showIntro = true;

    [SerializeField]
    private Key tasksKey = Key.Tab;

    [SerializeField, Min(1f)]
    private float toastSeconds = 5f;

    [SerializeField, Min(0.5f)]
    private float caughtCardSeconds = 3f;


    // =========================================================
    // PALETTE
    // =========================================================

    private static readonly Color BoxFill = new Color(0.025f, 0.03f, 0.028f, 0.84f);
    private static readonly Color BoxLine = new Color(0.86f, 0.84f, 0.78f, 0.55f);
    private static readonly Color Text1 = new Color(0.93f, 0.91f, 0.86f);
    private static readonly Color Text2 = new Color(0.62f, 0.60f, 0.56f);
    private static readonly Color Off = new Color(0.45f, 0.44f, 0.42f);


    // =========================================================
    // REFERENCES
    // =========================================================

    private ObjectiveManager objectives;

    private PlayerInteractor interactor;

    private PlayerHiding hiding;

    private PlayerController player;

    private Flashlight flashlight;

    private PlayerDeterrent deterrent;

    private PlayerThrower thrower;

    private MotionTracker tracker;

    private Camera view;


    // =========================================================
    // WIDGETS
    // =========================================================

    private RectTransform root;

    private GameObject playLayer;

    private Text objectiveStep;

    private Text objectiveMain;

    private Text objectiveHint;

    private Image crosshair;

    private Image holdTrack;

    private Image holdFill;

    private RectTransform promptBox;

    private Text promptAction;

    private Text promptDetail;

    private Text promptLoud;

    private Image loudVignette;

    private RectTransform toastBox;

    private Text toastText;

    private float toastBorn = -100f;

    private GameObject warningCard;

    private GameObject introCard;

    private Text introContinue;

    private Text warningContinue;

    private GameObject caughtCard;

    private Image caughtFill;

    private Text caughtTitle;

    private Text caughtLine;

    private GameObject winCard;

    private Text winStats;

    private readonly List<(Image icon, Text label)> tabMarkers = new List<(Image, Text)>();

    private RectTransform tabLayer;

    private Text flashlightState;

    private Image flashFuel;

    private Text pebbleState;

    private Text watchState;

    private Text flashlightKey;


    // =========================================================
    // STATE
    // =========================================================

    private enum HudScreen { Warning, Intro, Caught, Playing, Won }

    private HudScreen screen = HudScreen.Playing;

    private bool tasksDirty = true;

    private float caughtStarted;

    private string lastObjective;


    // =========================================================
    // UNITY
    // =========================================================

    private void Start()
    {
        objectives = ObjectiveManager.Instance;

        interactor = FindFirstObjectByType<PlayerInteractor>();

        if (interactor != null)
        {
            hiding = interactor.GetComponent<PlayerHiding>();
            player = interactor.GetComponent<PlayerController>();
            flashlight = interactor.GetComponent<Flashlight>();
            deterrent = interactor.GetComponent<PlayerDeterrent>();
            thrower = interactor.GetComponent<PlayerThrower>();
            tracker = interactor.GetComponent<MotionTracker>();
            view = interactor.GetComponentInChildren<Camera>();
        }

        if (view == null)
        {
            view = Camera.main;
        }

        BuildCanvas();

        if (objectives != null)
        {
            objectives.Changed += MarkDirty;
            objectives.Message += ShowToast;
        }

        if (GameSession.RestartedAfterCatch)
        {
            EnterCaught();
        }
        else if (showIntro && !GameSession.OpeningShown)
        {
            EnterWarning();
        }
        else
        {
            StartPlaying(false);
        }
    }


    private void OnDestroy()
    {
        if (objectives != null)
        {
            objectives.Changed -= MarkDirty;
            objectives.Message -= ShowToast;
        }

        Time.timeScale = 1f;
    }


    private void MarkDirty()
    {
        tasksDirty = true;

        if (objectives != null && objectives.Escaped && screen != HudScreen.Won)
        {
            EnterWin();
        }
    }


    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        switch (screen)
        {
            case HudScreen.Warning:
                TickWarning(keyboard);
                return;

            case HudScreen.Intro:
                TickIntro(keyboard);
                return;

            case HudScreen.Caught:
                TickCaught();
                return;

            case HudScreen.Won:
                TickWin(keyboard);
                return;
        }

        if (tasksDirty)
        {
            tasksDirty = false;
            RebuildObjective();
        }

        TickPrompt();
        TickTabMarkers(keyboard);
        TickToast();
        TickTools();
    }


    // =========================================================
    // SCREENS
    // =========================================================

    private void EnterWarning()
    {
        screen = HudScreen.Warning;

        warningCard.SetActive(true);
        playLayer.SetActive(false);

        Freeze(true);
    }


    private void TickWarning(Keyboard keyboard)
    {
        warningContinue.color = WithAlpha(Text2, 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f));

        if (keyboard == null || !(keyboard.eKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
            return;

        warningCard.SetActive(false);

        screen = HudScreen.Intro;

        introCard.SetActive(true);
    }


    private void TickIntro(Keyboard keyboard)
    {
        introContinue.color = WithAlpha(Text1, 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f));

        if (keyboard == null || !keyboard.eKey.wasPressedThisFrame)
            return;

        DismissIntro();
    }


    /// <summary>Starts the night. Public so tests and menus can skip the cards.</summary>
    public void DismissIntro()
    {
        if (screen != HudScreen.Intro && screen != HudScreen.Warning)
            return;

        warningCard.SetActive(false);
        introCard.SetActive(false);

        GameSession.OpeningShown = true;

        StartPlaying(true);
    }


    private void StartPlaying(bool firstTime)
    {
        playLayer.SetActive(true);

        Freeze(false);

        screen = HudScreen.Playing;

        objectives?.Begin();

        tasksDirty = true;

        if (firstTime)
        {
            ShowToast("Hold Tab to see where to go. Your tools are in the bottom right corner.", MessageTone.Info);
        }
    }


    private void EnterCaught()
    {
        screen = HudScreen.Caught;

        caughtStarted = Time.unscaledTime;

        caughtTitle.text = Spaced("YOU WERE CAUGHT");
        caughtLine.text = "The night begins again.   Attempt " + GameSession.Attempt;

        caughtCard.SetActive(true);
        playLayer.SetActive(false);

        Freeze(true);

        GameSession.ConsumeRestartFlag();
    }


    private void TickCaught()
    {
        float t = (Time.unscaledTime - caughtStarted) / caughtCardSeconds;

        /* Hold, then fade the black away over the restarted museum. */
        float alpha = t < 0.6f ? 1f : Mathf.Clamp01(1f - (t - 0.6f) / 0.4f);

        caughtFill.color = new Color(0f, 0f, 0f, alpha);
        caughtTitle.color = WithAlpha(new Color(0.75f, 0.18f, 0.15f), alpha);
        caughtLine.color = WithAlpha(Text1, alpha);

        if (t < 1f)
            return;

        caughtCard.SetActive(false);

        StartPlaying(false);
    }


    private void EnterWin()
    {
        screen = HudScreen.Won;

        playLayer.SetActive(false);
        winCard.SetActive(true);

        float t = GameSession.EarlierAttemptsSeconds + (objectives != null ? objectives.Elapsed : 0f);

        int caught = GameSession.TotalCaught;

        winStats.text =
            "Time in the museum   " + Mathf.FloorToInt(t / 60f) + ":" + Mathf.FloorToInt(t % 60f).ToString("00") + "\n" +
            (caught == 0
                ? "He never laid a hand on you."
                : "Caught " + caught + (caught == 1 ? " time" : " times") + "   -   " + GameSession.Attempt + " attempts");

        Freeze(true);
    }


    private void TickWin(Keyboard keyboard)
    {
        if (keyboard == null || !keyboard.rKey.wasPressedThisFrame)
            return;

        GameSession.ResetForNewRun();
        GameSession.ReloadLevel();
    }


    /// <summary>
    /// Stops the world behind a card. The player's look is locked too,
    /// because mouse look does not scale with time.
    /// </summary>
    private void Freeze(bool frozen)
    {
        Time.timeScale = frozen ? 0f : 1f;

        if (interactor != null)
        {
            interactor.Suspended = frozen;
        }

        if (player != null)
        {
            player.LookLocked = frozen;
            player.MovementLocked = frozen;
        }
    }


    // =========================================================
    // OBJECTIVE
    // =========================================================

    /// <summary>
    /// The single thing to do now. Objectives are taken in a fixed order;
    /// any already done (even out of order) are skipped.
    /// </summary>
    private void RebuildObjective()
    {
        if (objectives == null || objectiveMain == null)
            return;

        GalleryKey west = GalleryKey.NaturalHistory;
        GalleryKey east = GalleryKey.Egyptian;
        GalleryKey vault = GalleryKey.FineArt;

        var steps = new (bool done, string main, string hint)[]
        {
            (objectives.HasKey(west), "Find the " + ObjectiveManager.KeyName(west), Capital(ObjectiveManager.KeyHint(west))),
            (objectives.IsClockWound(west), "Wind the " + ObjectiveManager.ClockName(west), Capital(ObjectiveManager.ClockHint(west)) + "  -  count the chimes"),
            (objectives.HasKey(east), "Find the " + ObjectiveManager.KeyName(east), Capital(ObjectiveManager.KeyHint(east))),
            (objectives.IsClockWound(east), "Wind the " + ObjectiveManager.ClockName(east), Capital(ObjectiveManager.ClockHint(east)) + "  -  count the chimes"),
            (objectives.GateOpen, "Unlock the Special Collections gate", "East corridor  -  needs both gallery keys"),
            (objectives.IsClockWound(vault), "Wind the " + ObjectiveManager.ClockName(vault), Capital(ObjectiveManager.ClockHint(vault)) + "  -  count the chimes"),
            (objectives.VaultOpen, "Open the strongroom", "Dial the code  " + objectives.CodeDisplay),
            (objectives.HasKey(vault), "Take the master key", Capital(ObjectiveManager.KeyHint(vault))),
            (objectives.Escaped, "Open the front doors and leave", "Hold E at the doors  -  do not stop"),
        };

        int index = 0;

        while (index < steps.Length && steps[index].done)
        {
            index++;
        }

        if (index >= steps.Length)
        {
            objectiveMain.text = "";
            objectiveHint.text = "";
            return;
        }

        objectiveStep.text = Spaced("OBJECTIVE") + "     " + (index + 1) + " / " + steps.Length;
        objectiveMain.text = steps[index].main;
        objectiveHint.text = steps[index].hint;

        if (lastObjective != null && lastObjective != steps[index].main)
        {
            ShowToast("New objective:  " + steps[index].main, MessageTone.Success);
        }

        lastObjective = steps[index].main;
    }


    private static string Capital(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);


    // =========================================================
    // TOOLS
    // =========================================================

    private void TickTools()
    {
        if (flashlightState != null)
        {
            bool on = flashlight != null && flashlight.IsOn;

            flashlightState.text = on ? "ON" : "OFF";
            flashlightState.color = on ? Text1 : Off;
        }

        if (flashFuel != null)
        {
            float fuel = deterrent != null ? deterrent.FuelFraction : 0f;

            flashFuel.rectTransform.localScale = new Vector3(Mathf.Clamp01(fuel), 1f, 1f);
            flashFuel.color = fuel > 0.25f ? Text1 : new Color(0.8f, 0.3f, 0.25f);
        }

        if (pebbleState != null)
        {
            int left = thrower != null ? thrower.Remaining : 0;

            pebbleState.text = "x " + left;
            pebbleState.color = left > 0 ? Text1 : Off;
        }

        if (watchState != null)
        {
            bool on = tracker != null && tracker.IsActive;

            watchState.text = on ? "ON" : "OFF";
            watchState.color = on ? Text1 : Off;
        }
    }


    // =========================================================
    // PROMPT & CROSSHAIR
    // =========================================================

    private void TickPrompt()
    {
        InteractPrompt prompt = interactor != null ? interactor.Prompt : InteractPrompt.None;

        bool holding = interactor != null && interactor.IsHolding;

        string action = null, detail = null;

        Color actionColor = Text1;

        bool loud = false;

        if (prompt.State == InteractState.Available)
        {
            action = (prompt.Hold ? "Hold  [ E ]   " : "[ E ]   ") + prompt.Action;
            detail = prompt.Detail;
            loud = prompt.Loud;
        }
        else if (prompt.State == InteractState.Blocked)
        {
            action = prompt.Action;
            detail = prompt.Detail;
            actionColor = GameArt.Locked;
        }
        else if (hiding != null && hiding.IsHiding)
        {
            action = "[ E ]   Leave hiding place        [ W ]   Peek";
            detail = "He can still find you if he looks here";
            actionColor = GameArt.Hide;
        }
        else if (interactor != null && HidingSpot.TryGetInteractable(interactor.transform.position, out HidingSpot _))
        {
            action = "[ E ]   Hide";
            actionColor = GameArt.Hide;
        }

        promptAction.text = action ?? "";
        promptAction.color = actionColor;
        promptDetail.text = detail ?? "";
        promptLoud.text = loud
            ? (holding ? "MAKING NOISE  -  he can hear this" : "LOUD  -  he will hear this")
            : "";

        bool show = !string.IsNullOrEmpty(action);

        promptBox.gameObject.SetActive(show);

        if (show)
        {
            float w = Mathf.Max(promptAction.preferredWidth, promptDetail.preferredWidth, promptLoud.preferredWidth) + 70f;

            float h = 56f + (string.IsNullOrEmpty(detail) ? 0f : 30f) + (loud ? 30f : 0f);

            promptBox.sizeDelta = new Vector2(Mathf.Max(260f, w), h);
        }

        /* Crosshair: a dot, light on a task, red on a lock. */

        Color cross = WithAlpha(Text1, 0.55f);

        float size = 7f;

        if (prompt.State == InteractState.Available)
        {
            cross = Text1;
            size = 11f;
        }
        else if (prompt.State == InteractState.Blocked)
        {
            cross = GameArt.Locked;
            size = 11f;
        }

        crosshair.color = cross;
        crosshair.rectTransform.sizeDelta = Vector2.one * size;

        /* Hold ring. */

        bool showRing = prompt.State == InteractState.Available && prompt.Hold &&
                        interactor.Focus != null &&
                        (holding || interactor.Focus.HoldProgress > 0f);

        holdTrack.enabled = showRing;
        holdFill.enabled = showRing;

        if (showRing)
        {
            holdFill.fillAmount = interactor.Focus.HoldProgress;
        }

        /* Red edges while noisy. */

        float target = holding && loud ? 0.22f + 0.1f * Mathf.Sin(Time.unscaledTime * 8f) : 0f;

        Color v = loudVignette.color;

        v.a = Mathf.MoveTowards(v.a, target, Time.unscaledDeltaTime * 1.5f);

        loudVignette.color = v;
    }


    // =========================================================
    // TAB MARKERS (only what can be done now)
    // =========================================================

    private void TickTabMarkers(Keyboard keyboard)
    {
        bool show = keyboard != null && keyboard[tasksKey].isPressed && view != null;

        int used = 0;

        if (show)
        {
            foreach (Interactable target in Interactable.Active)
            {
                if (target == null || target.TaskState != InteractState.Available)
                    continue;

                PlaceTabMarker(used++, target);
            }
        }

        for (int i = used; i < tabMarkers.Count; i++)
        {
            tabMarkers[i].icon.enabled = false;
            tabMarkers[i].label.enabled = false;
        }
    }


    private void PlaceTabMarker(int index, Interactable target)
    {
        while (tabMarkers.Count <= index)
        {
            Image icon = NewImage(tabLayer, "Tab Marker", GameArt.Diamond, Color.white);

            icon.rectTransform.sizeDelta = new Vector2(30f, 30f);

            Text label = NewText(tabLayer, "Tab Label", 18, TextAnchor.UpperCenter);

            label.rectTransform.sizeDelta = new Vector2(320f, 50f);

            tabMarkers.Add((icon, label));
        }

        (Image icon, Text label) marker = tabMarkers[index];

        Vector3 world = target.FocusPoint;

        Vector3 screenPoint = view.WorldToScreenPoint(world);

        bool behind = screenPoint.z < 0f;

        if (behind)
        {
            screenPoint = -screenPoint;
        }

        Vector2 canvasSize = root.rect.size;

        Vector2 p = new Vector2(
            screenPoint.x / UnityEngine.Screen.width * canvasSize.x,
            screenPoint.y / UnityEngine.Screen.height * canvasSize.y);

        float margin = 60f;

        bool offScreen = behind || p.x < margin || p.x > canvasSize.x - margin ||
                         p.y < margin || p.y > canvasSize.y - margin;

        if (offScreen)
        {
            Vector2 centre = canvasSize * 0.5f;

            Vector2 dir = p - centre;

            if (dir.sqrMagnitude < 1f) dir = Vector2.down;

            float scale = Mathf.Min(
                (centre.x - margin) / Mathf.Abs(dir.x + 0.0001f),
                (centre.y - margin) / Mathf.Abs(dir.y + 0.0001f));

            p = centre + dir * scale;
        }

        marker.icon.enabled = true;
        marker.icon.color = Text1;
        marker.icon.rectTransform.anchoredPosition = p;

        float metres = Vector3.Distance(view.transform.position, world);

        marker.label.enabled = true;
        marker.label.color = Text1;
        marker.label.text = target.Label + "\n<size=15>" + Mathf.RoundToInt(metres) + " m</size>";
        marker.label.rectTransform.anchoredPosition = p + new Vector2(0f, -22f);
    }


    // =========================================================
    // MESSAGES
    // =========================================================

    private void ShowToast(string text, MessageTone tone)
    {
        if (toastText == null)
            return;

        toastText.text = text;

        toastText.color = tone == MessageTone.Warning ? new Color(0.9f, 0.45f, 0.38f) : Text1;

        toastBorn = Time.unscaledTime;
    }


    private void TickToast()
    {
        float age = Time.unscaledTime - toastBorn;

        float alpha = Mathf.Clamp01(age * 4f) * Mathf.Clamp01((toastSeconds - age) / 1f);

        bool show = alpha > 0.01f && !string.IsNullOrEmpty(toastText.text);

        toastBox.gameObject.SetActive(show);

        if (!show)
            return;

        toastBox.sizeDelta = new Vector2(toastText.preferredWidth + 80f, 62f);

        foreach (Graphic g in toastBox.GetComponentsInChildren<Graphic>())
        {
            Color c = g.color;

            if (g is Text)
            {
                c.a = alpha;
            }
            else
            {
                c.a = (g.name == "Line" ? BoxLine.a : BoxFill.a) * alpha;
            }

            g.color = c;
        }
    }


    // =========================================================
    // CANVAS
    // =========================================================

    private void BuildCanvas()
    {
        GameObject canvasGo = new GameObject("HUD Canvas");

        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root = canvasGo.GetComponent<RectTransform>();

        playLayer = Stretch(root, "Play Layer");

        loudVignette = NewImage(playLayer.transform, "Noise Vignette", EdgeSprite(), new Color(0.6f, 0.05f, 0.03f, 0f));

        StretchRect(loudVignette.rectTransform);

        BuildObjective(playLayer.transform);
        BuildCentre(playLayer.transform);
        BuildToast(playLayer.transform);
        BuildTools(playLayer.transform);
        BuildMovementKeys(playLayer.transform);

        tabLayer = Stretch(playLayer.transform, "Tab Layer").GetComponent<RectTransform>();
        tabLayer.anchorMin = Vector2.zero;
        tabLayer.anchorMax = Vector2.zero;
        tabLayer.pivot = Vector2.zero;
        tabLayer.sizeDelta = Vector2.zero;

        warningCard = BuildWarning(root);
        introCard = BuildIntro(root);
        caughtCard = BuildCaught(root);
        winCard = BuildWin(root);

        warningCard.SetActive(false);
        introCard.SetActive(false);
        caughtCard.SetActive(false);
        winCard.SetActive(false);
    }


    private void BuildObjective(Transform parent)
    {
        RectTransform box = Box(parent, "Objective", new Vector2(0f, 1f), new Vector2(40f, -36f), new Vector2(620f, 118f), new Vector2(0f, 1f));

        objectiveStep = NewText(box, "Step", 15, TextAnchor.UpperLeft);
        objectiveStep.color = Text2;
        Place(objectiveStep.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(560f, 24f));

        objectiveMain = NewText(box, "Main", 25, TextAnchor.UpperLeft);
        objectiveMain.color = Text1;
        Place(objectiveMain.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -44f), new Vector2(580f, 34f));

        objectiveHint = NewText(box, "Hint", 18, TextAnchor.UpperLeft);
        objectiveHint.color = Text2;
        objectiveHint.fontStyle = FontStyle.Italic;
        Place(objectiveHint.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -80f), new Vector2(580f, 26f));
    }


    private void BuildCentre(Transform parent)
    {
        crosshair = NewImage(parent, "Crosshair", GameArt.Dot, Text1);
        crosshair.rectTransform.sizeDelta = new Vector2(7f, 7f);

        holdTrack = NewImage(parent, "Hold Track", GameArt.Ring, WithAlpha(Text1, 0.18f));
        holdTrack.rectTransform.sizeDelta = new Vector2(72f, 72f);

        holdFill = NewImage(parent, "Hold Fill", GameArt.Ring, Text1);
        holdFill.rectTransform.sizeDelta = new Vector2(72f, 72f);
        holdFill.type = Image.Type.Filled;
        holdFill.fillMethod = Image.FillMethod.Radial360;
        holdFill.fillOrigin = (int)Image.Origin360.Top;
        holdFill.fillClockwise = true;

        promptBox = Box(parent, "Prompt", new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(600f, 90f), new Vector2(0.5f, 1f));

        promptAction = NewText(promptBox, "Action", 25, TextAnchor.UpperCenter);
        Place(promptAction.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -15f), new Vector2(1200f, 34f));

        promptDetail = NewText(promptBox, "Detail", 18, TextAnchor.UpperCenter);
        promptDetail.color = Text2;
        promptDetail.fontStyle = FontStyle.Italic;
        Place(promptDetail.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1200f, 28f));

        promptLoud = NewText(promptBox, "Loud", 17, TextAnchor.UpperCenter);
        promptLoud.color = GameArt.Locked;
        promptLoud.fontStyle = FontStyle.Bold;
        Place(promptLoud.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(1200f, 26f));
        promptLoud.rectTransform.pivot = new Vector2(0.5f, 0f);
    }


    private void BuildToast(Transform parent)
    {
        toastBox = Box(parent, "Message", new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(700f, 62f), new Vector2(0.5f, 1f));

        toastText = NewText(toastBox, "Text", 22, TextAnchor.MiddleCenter);
        StretchRect(toastText.rectTransform);
        toastText.text = "";
    }


    /// <summary>The four tools, bottom right: key, name, state.</summary>
    private void BuildTools(Transform parent)
    {
        RectTransform box = Box(parent, "Tools", new Vector2(1f, 0f), new Vector2(-40f, 36f), new Vector2(330f, 214f), new Vector2(1f, 0f));

        Text title = NewText(box, "Title", 15, TextAnchor.UpperLeft);
        title.color = Text2;
        title.text = Spaced("TOOLS");
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -14f), new Vector2(280f, 22f));

        flashlightState = ToolRow(box, 0, "F", "Flashlight");
        ToolRow(box, 1, "Q", "Camera flash");
        pebbleState = ToolRow(box, 2, "G", "Pebbles");
        watchState = ToolRow(box, 3, "T", "Pocket watch");

        /* The flash shows its fuel as a bar instead of a word. */
        RectTransform track = new GameObject("Fuel Track", typeof(RectTransform)).GetComponent<RectTransform>();
        track.SetParent(box, false);
        PlaceRow(track, new Vector2(0f, 1f), new Vector2(222f, -86f), new Vector2(84f, 8f));
        Image trackImg = track.gameObject.AddComponent<Image>();
        trackImg.sprite = GameArt.White;
        trackImg.color = WithAlpha(Text1, 0.18f);
        trackImg.raycastTarget = false;

        flashFuel = NewImage(track, "Fuel", GameArt.White, Text1);
        flashFuel.rectTransform.anchorMin = new Vector2(0f, 0f);
        flashFuel.rectTransform.anchorMax = new Vector2(1f, 1f);
        flashFuel.rectTransform.pivot = new Vector2(0f, 0.5f);
        flashFuel.rectTransform.offsetMin = Vector2.zero;
        flashFuel.rectTransform.offsetMax = Vector2.zero;
    }


    private Text ToolRow(RectTransform box, int row, string key, string name)
    {
        float y = -46f - row * 40f;

        Keycap(box, key, new Vector2(22f, y), new Vector2(0f, 1f));

        Text label = NewText(box, name, 20, TextAnchor.MiddleLeft);
        label.text = name;
        PlaceRow(label.rectTransform, new Vector2(0f, 1f), new Vector2(72f, y), new Vector2(160f, 32f));

        Text state = NewText(box, name + " State", 17, TextAnchor.MiddleRight);
        state.color = Text2;
        PlaceRow(state.rectTransform, new Vector2(0f, 1f), new Vector2(226f, y), new Vector2(80f, 32f));

        return state;
    }


    private void BuildMovementKeys(Transform parent)
    {
        RectTransform box = Box(parent, "Keys", new Vector2(0f, 0f), new Vector2(40f, 36f), new Vector2(760f, 58f), new Vector2(0f, 0f));

        float x = 18f;

        foreach ((string key, string label) in new[] { ("WASD", "Move"), ("Shift", "Run"), ("C", "Crouch"), ("E", "Interact"), ("Tab", "Where to go") })
        {
            RectTransform cap = Keycap(box, key, new Vector2(x, -29f), new Vector2(0f, 1f));

            x += cap.sizeDelta.x + 10f;

            Text t = NewText(box, label, 19, TextAnchor.MiddleLeft);
            t.text = label;
            t.color = Text2;
            PlaceRow(t.rectTransform, new Vector2(0f, 1f), new Vector2(x, -29f), new Vector2(160f, 32f));

            x += t.preferredWidth + 26f;
        }

        box.sizeDelta = new Vector2(x, 58f);
    }


    // =========================================================
    // CARDS
    // =========================================================

    private GameObject BuildWarning(RectTransform parent)
    {
        GameObject card = Stretch(parent, "Content Warning");

        Image bg = card.AddComponent<Image>();
        bg.sprite = GameArt.White;
        bg.color = Color.black;

        Transform c = card.transform;

        Text title = NewText(c, "Title", 46, TextAnchor.MiddleCenter);
        title.text = Spaced("CONTENT WARNING");
        title.color = Text1;
        Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1400f, 70f));

        Rule(c, new Vector2(0f, 102f), 560f);

        Text body = NewText(c, "Body", 27, TextAnchor.UpperCenter);
        body.color = Text1;
        body.lineSpacing = 1.3f;
        body.text =
            "This game includes darkness, sudden scares and an unseen pursuer.\n" +
            "It is meant to be tense and may be unsettling for some players.\n\n" +
            "Play with headphones, and take a break whenever you need one.";
        Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(1400f, 200f));

        warningContinue = NewText(c, "Continue", 22, TextAnchor.MiddleCenter);
        warningContinue.text = "Press  [ E ]  to continue";
        Place(warningContinue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(800f, 40f));

        return card;
    }


    private GameObject BuildIntro(RectTransform parent)
    {
        GameObject card = Stretch(parent, "Intro Card");

        Image bg = card.AddComponent<Image>();
        bg.sprite = GameArt.White;
        bg.color = new Color(0.01f, 0.012f, 0.011f, 0.97f);

        Transform c = card.transform;

        Text title = NewText(c, "Title", 70, TextAnchor.MiddleCenter);
        title.text = Spaced("THE CARETAKER");
        title.color = Text1;
        Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 360f), new Vector2(1600f, 90f));

        Rule(c, new Vector2(0f, 312f), 560f);

        Text story = NewText(c, "Story", 24, TextAnchor.UpperCenter);
        story.fontStyle = FontStyle.Italic;
        story.color = Text2;
        story.lineSpacing = 1.2f;
        story.text =
            "You stayed late restoring a painting, and the museum sealed itself for the night.\n" +
            "Something is walking the halls. It hunts by sound and by light.";
        Place(story.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(1500f, 80f));

        /* What to do, in one line. */
        RectTransform goal = Box(c, "Goal", new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(1120f, 66f), new Vector2(0.5f, 0.5f));
        Text goalText = NewText(goal, "Text", 23, TextAnchor.MiddleCenter);
        StretchRect(goalText.rectTransform);
        goalText.text = "Find the keys and wind the clocks.  Learn the strongroom code.  Escape through the front doors.";

        /* The controls, two columns of key boxes. */
        (string key, string what)[] left =
        {
            ("WASD", "Move"), ("Shift", "Run  (loud)"), ("C", "Crouch  (quiet)"), ("E", "Interact, hide under desks"), ("Tab", "Show where to go"),
        };

        (string key, string what)[] right =
        {
            ("F", "Flashlight  -  he can see its light"), ("Q", "Camera flash  -  drives him back"), ("G", "Throw a pebble  -  a noise elsewhere"), ("T", "Pocket watch  -  shows movement nearby"),
        };

        ControlColumn(c, left, new Vector2(-540f, 70f));
        ControlColumn(c, right, new Vector2(40f, 70f));

        Text warn = NewText(c, "Warning", 21, TextAnchor.UpperCenter);
        warn.color = new Color(0.9f, 0.45f, 0.38f);
        warn.text = "Smashing, winding and opening doors are loud. Let go of E to stop.";
        Place(warn.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(1400f, 34f));

        introContinue = NewText(c, "Continue", 26, TextAnchor.MiddleCenter);
        introContinue.text = "Press  [ E ]  to begin";
        Place(introContinue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -340f), new Vector2(800f, 44f));

        return card;
    }


    private void ControlColumn(Transform parent, (string key, string what)[] rows, Vector2 topLeft)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            Vector2 at = topLeft + new Vector2(0f, -i * 54f);

            RectTransform cap = Keycap(parent, rows[i].key, at, new Vector2(0.5f, 0.5f));

            Text t = NewText(parent, rows[i].what, 22, TextAnchor.MiddleLeft);
            t.text = rows[i].what;
            t.color = Text1;
            PlaceRow(t.rectTransform, new Vector2(0.5f, 0.5f), at + new Vector2(cap.sizeDelta.x + 18f, 0f), new Vector2(520f, 32f));
        }
    }


    private GameObject BuildCaught(RectTransform parent)
    {
        GameObject card = Stretch(parent, "Caught Card");

        caughtFill = card.AddComponent<Image>();
        caughtFill.sprite = GameArt.White;
        caughtFill.color = Color.black;

        Transform c = card.transform;

        caughtTitle = NewText(c, "Title", 58, TextAnchor.MiddleCenter);
        Place(caughtTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1500f, 80f));

        caughtLine = NewText(c, "Line", 24, TextAnchor.MiddleCenter);
        caughtLine.fontStyle = FontStyle.Italic;
        Place(caughtLine.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1400f, 40f));

        return card;
    }


    private GameObject BuildWin(RectTransform parent)
    {
        GameObject card = Stretch(parent, "Escape Card");

        Image bg = card.AddComponent<Image>();
        bg.sprite = GameArt.White;
        bg.color = new Color(0f, 0f, 0f, 0.94f);

        Transform c = card.transform;

        Text title = NewText(c, "Title", 66, TextAnchor.MiddleCenter);
        title.text = Spaced("YOU ESCAPED");
        title.color = Text1;
        Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1500f, 90f));

        Rule(c, new Vector2(0f, 70f), 460f);

        winStats = NewText(c, "Stats", 24, TextAnchor.UpperCenter);
        winStats.color = Text2;
        winStats.lineSpacing = 1.3f;
        Place(winStats.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1200f, 90f));

        Text again = NewText(c, "Again", 24, TextAnchor.MiddleCenter);
        again.text = "Press  [ R ]  to play again";
        Place(again.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(800f, 40f));

        return card;
    }


    private void Rule(Transform parent, Vector2 at, float width)
    {
        Image rule = NewImage(parent, "Rule", GameArt.White, WithAlpha(Text1, 0.55f));

        rule.rectTransform.sizeDelta = new Vector2(width, 1.5f);
        rule.rectTransform.anchoredPosition = at;
    }


    // =========================================================
    // WIDGET HELPERS
    // =========================================================

    /// <summary>A dark box with a thin light border.</summary>
    private static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));

        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();

        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        Image fill = go.AddComponent<Image>();
        fill.sprite = GameArt.White;
        fill.color = BoxFill;
        fill.raycastTarget = false;

        Image line = NewImage(rt, "Line", FrameSprite(), BoxLine);
        line.type = Image.Type.Sliced;
        StretchRect(line.rectTransform);

        return rt;
    }


    /// <summary>A key drawn in a small box, sized to its label.</summary>
    private static RectTransform Keycap(Transform parent, string key, Vector2 position, Vector2 anchor)
    {
        Text probe = NewText(parent, "Key " + key, 18, TextAnchor.MiddleCenter);

        probe.text = key;

        float w = Mathf.Max(34f, probe.preferredWidth + 20f);

        Object.Destroy(probe.gameObject);

        RectTransform cap = Box(parent, "Key " + key, anchor, position, new Vector2(w, 32f), new Vector2(0f, 0.5f));

        cap.GetComponent<Image>().color = new Color(0.08f, 0.085f, 0.08f, 0.95f);

        Text t = NewText(cap, "Label", 18, TextAnchor.MiddleCenter);

        t.text = key;
        t.color = Text1;

        StretchRect(t.rectTransform);

        return cap;
    }


    /// <summary>Places a row label: left edge at x, vertical centre at y (matches Keycap).</summary>
    private static void PlaceRow(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }


    private static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x == 0.5f ? 0.5f : anchor.x, anchor.y == 0.5f ? 0.5f : 1f);

        if (anchor.x == 0f) rt.pivot = new Vector2(0f, rt.pivot.y);

        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }


    /// <summary>Wide-spaced capitals for titles.</summary>
    private static string Spaced(string s) => string.Join(" ", s.ToCharArray());


    private static GameObject Stretch(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));

        go.transform.SetParent(parent, false);

        StretchRect(go.GetComponent<RectTransform>());

        return go;
    }


    private static void StretchRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }


    private static Image NewImage(Transform parent, string name, Sprite sprite, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));

        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();

        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;

        return image;
    }


    private static Text NewText(Transform parent, string name, int size, TextAnchor anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));

        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();

        text.font = GameArt.Font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Text1;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        Shadow shadow = go.AddComponent<Shadow>();

        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1.2f, -1.2f);

        return text;
    }


    private static Color WithAlpha(Color c, float a)
    {
        c.a = a;

        return c;
    }


    private static Sprite frameSprite;

    /// <summary>A 1-pixel border, nine-sliced so it stays thin at any size.</summary>
    private static Sprite FrameSprite()
    {
        if (frameSprite != null)
            return frameSprite;

        const int size = 8;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.DontSave
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, edge ? 1f : 0f));
            }
        }

        tex.Apply();

        frameSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(2f, 2f, 2f, 2f));

        frameSprite.hideFlags = HideFlags.DontSave;

        return frameSprite;
    }


    private static Sprite edgeSprite;

    /// <summary>Transparent centre, opaque edges - for the noise pulse.</summary>
    private static Sprite EdgeSprite()
    {
        if (edgeSprite != null)
            return edgeSprite;

        const int size = 64;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size - 0.5f;
                float v = (y + 0.5f) / size - 0.5f;

                float d = Mathf.Sqrt(u * u + v * v) * 2f;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, (d - 0.55f) / 0.5f)));
            }
        }

        tex.Apply();

        edgeSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        edgeSprite.hideFlags = HideFlags.DontSave;

        return edgeSprite;
    }
}
