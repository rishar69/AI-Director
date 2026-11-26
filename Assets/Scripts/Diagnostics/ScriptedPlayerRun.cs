using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Test driver: moves the player along waypoints at a set speed, then
/// optionally hides in the nearest hiding spot. Used to play-test the
/// hunt repeatably ("seen running toward the offices, then hides").
///
/// Added at runtime by test scripts; not part of any scene. Disables
/// player input while it runs and gives it back when done.
/// </summary>
public class ScriptedPlayerRun : MonoBehaviour
{
    public List<Vector3> Waypoints = new List<Vector3>();

    public float Speed = 4.5f;

    public bool HideAtEnd = true;

    public bool Finished { get; private set; }

    public string Report { get; private set; } = "";

    private PlayerController player;

    private CharacterController body;

    private int index;


    private void Start()
    {
        player = GetComponent<PlayerController>();

        body = GetComponent<CharacterController>();

        if (player != null) player.enabled = false;
    }


    private void Update()
    {
        if (Finished)
            return;

        if (index >= Waypoints.Count)
        {
            Finish();
            return;
        }

        Vector3 target = Waypoints[index];

        target.y = transform.position.y;

        Vector3 to = target - transform.position;

        float step = Speed * Time.deltaTime;

        if (to.magnitude <= step)
        {
            Place(target);
            index++;
            return;
        }

        Vector3 dir = to.normalized;

        Place(transform.position + dir * step);

        if (player != null)
        {
            player.SetLook(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
        }

        /* Footsteps, so hearing behaves as it would for a real run. */
        if (Time.frameCount % 20 == 0)
        {
            NoiseBus.Emit(transform.position, 6f, NoiseType.Footstep, gameObject);
        }
    }


    private void Place(Vector3 p)
    {
        if (body != null) body.enabled = false;

        transform.position = p;

        if (body != null) body.enabled = true;
    }


    private void Finish()
    {
        Finished = true;

        if (player != null) player.enabled = true;

        if (!HideAtEnd)
        {
            Report = "run finished";
            return;
        }

        PlayerHiding hiding = GetComponent<PlayerHiding>();

        MethodInfo enter = typeof(PlayerHiding).GetMethod("TryEnter", BindingFlags.Instance | BindingFlags.NonPublic);

        enter?.Invoke(hiding, null);

        Report = hiding != null && hiding.IsHiding ? "hiding" : "could not hide here";
    }
}
