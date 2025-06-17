using UnityEngine;

public class SpringPadFeature : MonoBehaviour, IPlaceableFeature
{
    [Header("Settings")]
    [Tooltip("How long (seconds) for one full oscillation (stretch→compress→back)")]
    public float cycleTime = 2f;

    [Tooltip("Impulse strength applied upwards when fired")]
    public float springStrength = 12f;

    [Tooltip("Max Y-scale offset at peak (and compress depth at trough)")]
    public float scaleAmount = 0.5f;

    [Tooltip("How many oscillations per cycle (1 = one up+down)")]
    public float frequency = 1f;

    [Tooltip("Damping rate (0 = no decay)")]
    public float damping = 0f;

    // Runtime
    private float timer;
    private Vector3 baseScale;
    private GameObject currentPlayer;
    private bool hasLaunchedThisCycle;
    private float prevOsc;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void Update()
    {
        // Advance & wrap timer
        timer += Time.deltaTime;
        if (timer > cycleTime) timer -= cycleTime;

        // Compute raw sine oscillation
        float tNorm  = timer / cycleTime;
        float rawOsc = Mathf.Sin(tNorm * Mathf.PI * 2f * frequency);
        float oscillation = rawOsc * Mathf.Exp(-damping * timer);

        // Apply to visual scale
        float newY = baseScale.y + oscillation * scaleAmount;
        transform.localScale = new Vector3(baseScale.x, newY, baseScale.z);

        // Launch when the spring starts stretching
        if (currentPlayer != null && !hasLaunchedThisCycle)
        {
            // prevOsc ≤ 0 and rawOsc > 0 means we just crossed upward through the rest position
            if (prevOsc <= 0f && rawOsc > 0f)
            {
                LaunchPlayer(currentPlayer);
                hasLaunchedThisCycle = true;
            }
        }

        // Reset once it finishes compressing (so next up-stretch can fire)
        if (rawOsc < 0f)
            hasLaunchedThisCycle = false;

        prevOsc = rawOsc;
    }

    private void LaunchPlayer(GameObject player)
    {
        // Try to get NetworkPlayerController first (for multiplayer)
        NetworkPlayerController networkController = player.GetComponent<NetworkPlayerController>();
        if (networkController != null)
        {
            networkController.Launch(Vector3.up * springStrength);
            return;
        }

        // Fallback to regular PlayerController (for single player)
        PlayerController regularController = player.GetComponent<PlayerController>();
        if (regularController != null)
        {
            regularController.Launch(Vector3.up * springStrength);
        }
    }

    public void OnPlayerEnter(GameObject player)
    {
        currentPlayer = player;
        hasLaunchedThisCycle = false;   // allow firing on the next stretch
    }

    public void OnPlayerExit(GameObject player)
    {
        if (currentPlayer == player)
            currentPlayer = null;
    }

    public void OnPlayerStay(GameObject player) { }
}
