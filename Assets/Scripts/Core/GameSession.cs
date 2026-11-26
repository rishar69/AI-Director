using UnityEngine;

/// <summary>
/// What survives a level restart within one play session.
///
/// Being caught reloads the whole museum scene, which wipes every
/// component. The few things that must outlive that - which attempt
/// this is, how often the player has been caught, and whether the
/// opening screens have already been shown - live here as statics,
/// cleared whenever Play mode (or the built game) starts fresh.
/// </summary>
public static class GameSession
{
    /// <summary>1 for the first attempt, 2 after the first catch, and so on.</summary>
    public static int Attempt { get; private set; } = 1;

    /// <summary>Times caught across every attempt this session.</summary>
    public static int TotalCaught { get; private set; }

    /// <summary>True when this scene load is a restart after being caught.</summary>
    public static bool RestartedAfterCatch { get; private set; }

    /// <summary>The content warning and intro are shown once per session.</summary>
    public static bool OpeningShown { get; set; }

    /// <summary>Seconds played across every attempt, for the escape screen.</summary>
    public static float EarlierAttemptsSeconds { get; private set; }


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        Attempt = 1;
        TotalCaught = 0;
        RestartedAfterCatch = false;
        OpeningShown = false;
        EarlierAttemptsSeconds = 0f;
    }


    /// <summary>Called just before the scene reloads after a catch.</summary>
    public static void NoteCaught(float secondsThisAttempt)
    {
        TotalCaught++;
        Attempt++;
        EarlierAttemptsSeconds += secondsThisAttempt;
        RestartedAfterCatch = true;
    }


    /// <summary>Called by the HUD once it has shown the "caught" card.</summary>
    public static void ConsumeRestartFlag()
    {
        RestartedAfterCatch = false;
    }


    /// <summary>Reloads the active scene, in the Editor and in a build.</summary>
    public static void ReloadLevel()
    {
        Time.timeScale = 1f;

        UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
            scene.path,
            new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
        UnityEngine.SceneManagement.SceneManager.LoadScene(scene.buildIndex);
#endif
    }


    /// <summary>A fresh start: used by "play again" after escaping.</summary>
    public static void ResetForNewRun()
    {
        ResetSession();
        OpeningShown = true;
    }
}
