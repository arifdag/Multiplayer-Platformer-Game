using UnityEngine;
using Unity.Netcode;

public class LevelFinisher : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("The distance within which the player can interact with the finisher.")]
    [SerializeField]
    private float interactionDistance = 2.0f;

    [Tooltip("The key the player needs to press to finish the level.")] [SerializeField]
    private KeyCode interactionKey = KeyCode.E;

    [Tooltip("Tag of the Player GameObject.")] [SerializeField]
    private string playerTag = "Player";

    [Header("UI Feedback (Optional)")]
    [Tooltip("A UI element (e.g., Text or Image) to show when the player is in range. Assign if needed.")]
    [SerializeField]
    private GameObject interactionPromptUI;

    [Header("Network Settings")] [Tooltip("Visual effect to show when a player finishes")] [SerializeField]
    private GameObject finishEffect;

    private bool _playerInRange = false;
    private NetworkPlayerController _playerControllerInRange = null;

    void Start()
    {
        if (interactionPromptUI != null)
        {
            interactionPromptUI.SetActive(false); // Hide prompt initially
        }
    }

    void Update()
    {
        if (_playerInRange && _playerControllerInRange != null)
        {
            // Only allow local players to interact
            if (!_playerControllerInRange.IsLocalPlayer())
            {
                if (interactionPromptUI != null) interactionPromptUI.SetActive(false);
                return;
            }

            // Check if the player controller is enabled
            if (!_playerControllerInRange.enabled)
            {
                if (interactionPromptUI != null) interactionPromptUI.SetActive(false);
                return;
            }

            if (Input.GetKeyDown(interactionKey))
            {
                FinishLevel();
            }
        }
    }

    private void FinishLevel()
    {
        Debug.Log("Player finished level!");

        // Show visual effect
        if (finishEffect != null)
        {
            GameObject effect = Instantiate(finishEffect, transform.position, transform.rotation);
            Destroy(effect, 3f); // Clean up after 3 seconds
        }

        // Disable the player controller to prevent further movement
        if (_playerControllerInRange != null)
        {
            _playerControllerInRange.enabled = false;
        }

        // Hide interaction prompt
        if (interactionPromptUI != null)
        {
            interactionPromptUI.SetActive(false);
        }

        // Award a star via ScoreManager
        if (ScoreManager.Instance != null)
        {
            // LocalClientId is the player who just finished
            ulong clientId = NetworkManager.Singleton.LocalClientId;
            // default count = 1 star
            ScoreManager.Instance.AwardStarServerRpc(clientId, 1);
        }


        // Notify the NetworkGameManager that this player finished
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.PlayerFinishedLevelServerRpc();
        }
        else
        {
            Debug.LogError("LevelFinisher: NetworkGameManager not found!");
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            NetworkPlayerController playerController = other.GetComponent<NetworkPlayerController>();
            if (playerController != null)
            {
                _playerInRange = true;
                _playerControllerInRange = playerController;

                // Only show prompt for local player
                if (playerController.IsLocalPlayer() && interactionPromptUI != null)
                {
                    if (playerController.enabled)
                    {
                        interactionPromptUI.SetActive(true);
                    }
                }

                Debug.Log($"Player {playerController.OwnerClientId} entered finisher range.");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            NetworkPlayerController playerController = other.GetComponent<NetworkPlayerController>();
            if (playerController != null && playerController == _playerControllerInRange)
            {
                _playerInRange = false;
                _playerControllerInRange = null;

                if (interactionPromptUI != null)
                {
                    interactionPromptUI.SetActive(false);
                }

                Debug.Log($"Player {playerController.OwnerClientId} exited finisher range.");
            }
        }
    }


    // Draw a Gizmo in the editor to visualize the interaction range
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            // If it's a sphere collider, use its radius for a more accurate gizmo
            if (col is SphereCollider sphereCollider)
            {
                Gizmos.DrawWireSphere(transform.position + sphereCollider.center,
                    sphereCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y,
                        transform.lossyScale.z));
            }
            else // Draw a general sphere based on interactionDistance for other colliders
            {
                Gizmos.DrawWireSphere(transform.position, interactionDistance);
            }
        }
        else // Fallback if no collider found on this object
        {
            Gizmos.DrawWireSphere(transform.position, interactionDistance);
        }
    }
}