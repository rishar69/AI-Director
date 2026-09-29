using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Raises and lowers motivations by hand.
///
/// The Director will own this once it is ported, but the whole point of
/// the motivation system is that the tree does not care who set the
/// flag - so toggling them from the keyboard is a complete test of the
/// arbitration, not a shortcut around it.
///
/// DELETE once AIDirector drives motivations.
/// </summary>
public class MotivationDebugKeys : MonoBehaviour
{
    [Serializable]
    public struct Binding
    {
        public Key key;

        public Motivation motivation;
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Target")]
    [SerializeField]
    private XenoBlackboard blackboard;


    [Header("Bindings")]
    [SerializeField]
    private Binding[] bindings = new Binding[0];


    [Header("Debug")]
    [SerializeField]
    private bool logToggles = true;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        if (blackboard == null)
        {
            blackboard = FindFirstObjectByType<XenoBlackboard>();
        }

        if (bindings == null || bindings.Length == 0)
        {
            BuildDefaultBindings();
        }
    }


    /// <summary>
    /// The motivations worth reaching for while the tree is still
    /// mostly stubs.
    /// </summary>
    private void BuildDefaultBindings()
    {
        bindings = new[]
        {
            new Binding { key = Key.Digit1, motivation = Motivation.Stalk },
            new Binding { key = Key.Digit2, motivation = Motivation.BackstageStalk },
            new Binding { key = Key.Digit3, motivation = Motivation.ThreatAware },
            new Binding { key = Key.Digit4, motivation = Motivation.Attack },
            new Binding { key = Key.Digit5, motivation = Motivation.SearchSystematic },
            new Binding { key = Key.Digit6, motivation = Motivation.SuspectTargetResponse }
        };
    }


    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null || blackboard == null)
            return;

        for (int i = 0; i < bindings.Length; i++)
        {
            Binding binding = bindings[i];

            if (!keyboard[binding.key].wasPressedThisFrame)
                continue;

            bool next =
                !blackboard.HasMotivation(binding.motivation);

            blackboard.SetMotivation(binding.motivation, next);

            if (logToggles)
            {
                Debug.Log(
                    "[MotivationKeys] " + binding.motivation +
                    " = " + next,
                    this
                );
            }
        }
    }


    /// <summary>
    /// Legend for the debug overlay.
    /// </summary>
    public string DescribeBindings()
    {
        if (bindings == null || bindings.Length == 0)
            return "none";

        string result = null;

        for (int i = 0; i < bindings.Length; i++)
        {
            string entry =
                bindings[i].key.ToString().Replace("Digit", "") +
                "=" +
                bindings[i].motivation;

            result = result == null ? entry : result + "  " + entry;
        }

        return result;
    }
}
