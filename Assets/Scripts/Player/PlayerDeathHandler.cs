using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerDeathHandler : MonoBehaviour
{
    [Tooltip("Which layers instantly kill the player")]
    public LayerMask lethalLayers;

    private bool _isDead = false;
    private CharacterController _cc;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    void OnEnable()
    {
        // Reset death state whenever this handler is re-enabled (e.g., at the start of a new round).
        _isDead = false;

        // Ensure the player is visible & interactive when re-enabled
        var networkController = GetComponent<NetworkPlayerController>();
        if (networkController != null)
        {
            networkController.SetVisibility(true);
        }
        else
        {
            // Fallback if NetworkPlayerController not found
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var rend in renderers) rend.enabled = true;

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                // CharacterController is handled separately by NetworkGameManager.ResetPlayer
                if (!(col is CharacterController)) col.enabled = true;
            }
        }
    }

    // For trigger‐based lethal objects
    void OnTriggerEnter(Collider other)
    {
        if (_isDead) return;
        if (((1 << other.gameObject.layer) & lethalLayers) != 0)
            Die();
    }

    // For CharacterController hits
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (_isDead) return;
        if (((1 << hit.gameObject.layer) & lethalLayers) != 0)
            Die();
    }

    public void Die()
    {
        _isDead = true;


        // Disable movement
        _cc.enabled = false;

        // In multiplayer we keep the object so that NetworkGameManager can reset it next round.
        if (NetworkGameManager.Instance != null)
        {
            // Notify the server that this player is effectively "done" for this round.
            NetworkGameManager.Instance.PlayerFinishedLevelServerRpc();

            // Disable this handler until next round; NetworkGameManager will re-enable it via ResetPlayer.
            enabled = false;

            // Use the NetworkPlayerController to set visibility across the network
            var networkController = GetComponent<NetworkPlayerController>();
            if (networkController != null)
            {
                networkController.SetVisibility(false);
            }
            else
            {
                // Fallback if NetworkPlayerController not found
                Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
                foreach (var rend in renderers) rend.enabled = false;
                
                // Disable colliders if NetworkPlayerController isn't available
                Collider[] colliders = GetComponentsInChildren<Collider>(true);
                foreach (var col in colliders)
                {
                    if (!(col is CharacterController)) col.enabled = false;
                }
            }
        }
    }
}