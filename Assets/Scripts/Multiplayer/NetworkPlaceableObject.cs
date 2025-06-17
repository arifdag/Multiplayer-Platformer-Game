using UnityEngine;
using Unity.Netcode;

public class NetworkPlaceableObject : NetworkBehaviour
{
    [Header("Network Sync")]
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<Quaternion> networkRotation = new NetworkVariable<Quaternion>();
    private NetworkVariable<bool> networkIsActive = new NetworkVariable<bool>(true);

    [Header("Object Settings")]
    [SerializeField] private bool syncTransformContinuously = false;
    [SerializeField] private float positionThreshold = 0.01f;
    [SerializeField] private float rotationThreshold = 1f;

    private Vector3 _lastSyncedPosition;
    private Quaternion _lastSyncedRotation;
    private float _lastSyncTime;
    private const float SYNC_RATE = 1f / 20f; // 20 updates per second

    public override void OnNetworkSpawn()
    {
        // Subscribe to network variable changes for non-owners
        if (!IsOwner)
        {
            networkPosition.OnValueChanged += OnNetworkPositionChanged;
            networkRotation.OnValueChanged += OnNetworkRotationChanged;
            networkIsActive.OnValueChanged += OnNetworkActiveChanged;
        }

        // Initialize position and rotation
        if (IsOwner)
        {
            networkPosition.Value = transform.position;
            networkRotation.Value = transform.rotation;
            _lastSyncedPosition = transform.position;
            _lastSyncedRotation = transform.rotation;
        }
        else
        {
            transform.position = networkPosition.Value;
            transform.rotation = networkRotation.Value;
            gameObject.SetActive(networkIsActive.Value);
        }
        
        SetPlacedItemLayer();
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner)
        {
            networkPosition.OnValueChanged -= OnNetworkPositionChanged;
            networkRotation.OnValueChanged -= OnNetworkRotationChanged;
            networkIsActive.OnValueChanged -= OnNetworkActiveChanged;
        }
    }

    void Update()
    {
        if (IsOwner && syncTransformContinuously)
        {
            // Check if we need to sync transform
            _lastSyncTime += Time.deltaTime;
            if (_lastSyncTime >= SYNC_RATE)
            {
                CheckAndSyncTransform();
                _lastSyncTime = 0f;
            }
        }
        else if (!IsOwner)
        {
            // Smooth interpolation for non-owners
            if (Vector3.Distance(transform.position, networkPosition.Value) > positionThreshold)
            {
                transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 10f);
            }
            
            if (Quaternion.Angle(transform.rotation, networkRotation.Value) > rotationThreshold)
            {
                float lerpValue = Mathf.Clamp01(Time.deltaTime * 10f);
                if (lerpValue > 0f && lerpValue < 1f)
                {
                    transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation.Value, lerpValue);
                }
                else if (lerpValue >= 1f)
                {
                    transform.rotation = networkRotation.Value;
                }
            }
        }
    }

    private void CheckAndSyncTransform()
    {
        bool needsSync = false;

        // Check position
        if (Vector3.Distance(transform.position, _lastSyncedPosition) > positionThreshold)
        {
            networkPosition.Value = transform.position;
            _lastSyncedPosition = transform.position;
            needsSync = true;
        }

        // Check rotation
        if (Quaternion.Angle(transform.rotation, _lastSyncedRotation) > rotationThreshold)
        {
            networkRotation.Value = transform.rotation;
            _lastSyncedRotation = transform.rotation;
            needsSync = true;
        }

        if (needsSync)
        {
            Debug.Log($"Synced transform for {gameObject.name}");
        }
    }

    private void OnNetworkPositionChanged(Vector3 oldPos, Vector3 newPos)
    {
        if (!IsOwner)
        {
            // If the change is too large, teleport immediately
            if (Vector3.Distance(transform.position, newPos) > 2f)
            {
                transform.position = newPos;
            }
            // Otherwise, smooth interpolation happens in Update()
        }
    }

    private void OnNetworkRotationChanged(Quaternion oldRot, Quaternion newRot)
    {
        if (!IsOwner)
        {
            // If the change is too large, snap immediately
            if (Quaternion.Angle(transform.rotation, newRot) > 45f)
            {
                transform.rotation = newRot;
            }
            // Otherwise, smooth interpolation happens in Update()
        }
    }

    private void OnNetworkActiveChanged(bool oldActive, bool newActive)
    {
        if (!IsOwner)
        {
            gameObject.SetActive(newActive);
        }
    }

    private void SetPlacedItemLayer()
    {
        // Set this object to the PlacedItems layer for collision detection
        int placedItemLayer = LayerMask.NameToLayer("PlacedItems");
        if (placedItemLayer != -1)
        {
            SetLayerRecursively(gameObject, placedItemLayer);
        }
        else
        {
            Debug.LogWarning($"NetworkPlaceableObject: 'PlacedItems' layer not found for {gameObject.name}");
        }
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    // Public method to enable/disable the object across the network
    [ServerRpc(RequireOwnership = false)]
    public void SetActiveServerRpc(bool active)
    {
        networkIsActive.Value = active;
        gameObject.SetActive(active);
    }

    // Public method to move the object across the network (for interactive objects)
    [ServerRpc(RequireOwnership = false)]
    public void SetTransformServerRpc(Vector3 position, Quaternion rotation)
    {
        networkPosition.Value = position;
        networkRotation.Value = rotation;
        transform.position = position;
        transform.rotation = rotation;
    }

    // Public method to destroy the object across the network
    [ServerRpc(RequireOwnership = false)]
    public void DestroyObjectServerRpc()
    {
        GetComponent<NetworkObject>().Despawn();
    }

    // Called when the object is hit by a player or other interaction
    public void OnPlayerInteraction(NetworkPlayerController player)
    {
        Debug.Log($"Player {player.OwnerClientId} interacted with {gameObject.name}");
        
        var placeableFeature = GetComponent<IPlaceableFeature>();
        if (placeableFeature != null)
        {
            // Trigger the feature on the server
            TriggerFeatureServerRpc(player.OwnerClientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void TriggerFeatureServerRpc(ulong playerClientId)
    {
        // Find the player object and trigger the feature
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(playerClientId, out var clientData))
        {
            if (clientData.PlayerObject != null)
            {
                var playerController = clientData.PlayerObject.GetComponent<NetworkPlayerController>();
                if (playerController != null)
                {
                    var placeableFeature = GetComponent<IPlaceableFeature>();
                    if (placeableFeature != null)
                    {
                        TriggerFeatureClientRpc(playerClientId);
                    }
                }
            }
        }
    }

    [ClientRpc]
    private void TriggerFeatureClientRpc(ulong playerClientId)
    {
        // Execute the feature effect on all clients
        var placeableFeature = GetComponent<IPlaceableFeature>();
        if (placeableFeature != null)
        {
            Debug.Log($"Triggering feature for player {playerClientId}");
        }
    }

    // Gizmos for debugging
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
        
        if (IsOwner)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.6f, 0.1f);
        }
    }
} 