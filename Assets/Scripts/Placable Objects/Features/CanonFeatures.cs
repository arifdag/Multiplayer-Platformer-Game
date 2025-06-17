using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class CanonFeatures : NetworkBehaviour, IPlaceableFeature
{
    [Header("References")]
    [Tooltip("Local point to spawn the player in")]
    public Transform seatPoint;
    
    [Tooltip("The part of the canon that rotates")]
    public Transform canonRotatingPart;
    
    [Header("Settings")]
    [Tooltip("Launch velocity in local forward")]
    public float launchSpeed = 20f;
    
    [Tooltip("Size of the detection box")]
    public Vector3 detectionBoxSize = new Vector3(3f, 2f, 3f);
    
    [Tooltip("Rotation speed of the canon")]
    public float rotationSpeed = 90f;

    // Network variables
    private NetworkVariable<bool> networkOccupied = new NetworkVariable<bool>(false);
    private NetworkVariable<float> networkRotationAngle = new NetworkVariable<float>(0f);
    private NetworkVariable<ulong> networkRiderClientId = new NetworkVariable<ulong>(ulong.MaxValue);
    
    // Private variables
    private GameObject currentRider;
    private NetworkPlayerController networkRiderController;
    private PlayerController regularRiderController;
    private BoxCollider detectionCollider;
    private List<GameObject> playersInRange = new List<GameObject>();
    private bool localPlayerInRange = false;
    
    private void Awake()
    {
        // Create detection collider if it doesn't exist
        detectionCollider = GetComponent<BoxCollider>();
        if (detectionCollider == null)
        {
            detectionCollider = gameObject.AddComponent<BoxCollider>();
        }
        
        // Configure collider
        detectionCollider.isTrigger = true;
        detectionCollider.size = detectionBoxSize;
        detectionCollider.center = Vector3.zero;
        
        // If canonRotatingPart is not set, use this transform
        if (canonRotatingPart == null)
        {
            canonRotatingPart = transform;
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Subscribe to network variable changes
        networkOccupied.OnValueChanged += OnOccupiedStateChanged;
        networkRotationAngle.OnValueChanged += OnRotationAngleChanged;
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        // Unsubscribe from network variable changes
        networkOccupied.OnValueChanged -= OnOccupiedStateChanged;
        networkRotationAngle.OnValueChanged -= OnRotationAngleChanged;
    }
    
    private void OnOccupiedStateChanged(bool oldValue, bool newValue)
    {
        // TODO
        // Update visuals or effects when occupied state changes
    }
    
    private void OnRotationAngleChanged(float oldValue, float newValue)
    {
        // Update canon rotation for non-owners
        if (!IsOwner && !IsServer)
        {
            UpdateCanonRotation(newValue);
        }
    }

    void Update()
    {
        // Check for input to enter canon
        if (localPlayerInRange && Input.GetKeyDown(KeyCode.E) && !networkOccupied.Value)
        {
            // Find the local player in range
            GameObject localPlayer = null;
            foreach (var player in playersInRange)
            {
                NetworkPlayerController networkController = player.GetComponent<NetworkPlayerController>();
                if (networkController != null && networkController.IsLocalPlayer())
                {
                    localPlayer = player;
                    break;
                }
            }
            
            if (localPlayer != null)
            {
                EnterCanon(localPlayer);
            }
        }
        
        // If this client's player is in the canon
        if (networkOccupied.Value && currentRider != null)
        {
            bool isLocalRider = false;
            
            // Check if the current rider is controlled by this client
            NetworkPlayerController networkController = currentRider.GetComponent<NetworkPlayerController>();
            if (networkController != null && networkController.IsLocalPlayer())
            {
                isLocalRider = true;
            }
            
            if (isLocalRider)
            {
                // Rotate canon with input
                float turn = Input.GetAxis("Horizontal");
                float currentAngle = networkRotationAngle.Value;
                float newAngle = currentAngle + turn * rotationSpeed * Time.deltaTime;
                
                // Update rotation on server
                if (turn != 0)
                {
                    UpdateRotationServerRpc(newAngle);
                }
                
                // Check for shoot input
                if (Input.GetMouseButtonDown(0))
                {
                    ShootCanonServerRpc();
                }
            }
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void UpdateRotationServerRpc(float angle)
    {
        networkRotationAngle.Value = angle;
        UpdateCanonRotation(angle);
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void ShootCanonServerRpc()
    {
        if (!networkOccupied.Value) return;
        
        // Get the rider's client ID
        ulong riderId = networkRiderClientId.Value;
        
        // Find the rider GameObject
        GameObject rider = null;
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            if (client.Key == riderId && client.Value.PlayerObject != null)
            {
                rider = client.Value.PlayerObject.gameObject;
                break;
            }
        }
        
        if (rider != null)
        {
            // Exit and launch
            ExitCanonClientRpc(riderId, canonRotatingPart.forward * launchSpeed);
            
            // Reset canon state
            networkOccupied.Value = false;
            networkRiderClientId.Value = ulong.MaxValue;
        }
    }
    
    private void UpdateCanonRotation(float angle)
    {
        // Keep angle within reasonable bounds
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        
        // Determine if we should mirror the rotation based on angle
        bool facingRight = angle < 45 && angle > -45;
        
        // Create the rotation
        Quaternion targetRotation;
        
        if (facingRight)
        {
            // Normal rotation (0-45 degrees)
            targetRotation = Quaternion.Euler(0, angle, 0);
        }
        else
        {
            // Mirrored rotation (beyond 45 degrees)
            float mirroredAngle = angle > 0 ? 90 - angle : -90 - angle;
            targetRotation = Quaternion.Euler(0, mirroredAngle, 0);
            

            canonRotatingPart.localScale = new Vector3(
                Mathf.Abs(canonRotatingPart.localScale.x) * (angle > 0 ? -1 : 1),
                canonRotatingPart.localScale.y,
                canonRotatingPart.localScale.z
            );
        }
        
        // Apply rotation to the rotating part
        canonRotatingPart.localRotation = targetRotation;
    }
    
    private void EnterCanon(GameObject player)
    {
        // Check if already occupied
        if (networkOccupied.Value) return;
        
        // Check for network controller
        NetworkPlayerController networkController = player.GetComponent<NetworkPlayerController>();
        if (networkController != null)
        {
            EnterCanonServerRpc(networkController.OwnerClientId);
        }
        else
        {
            // Fallback for non-networked players (single player)
            PlayerController regularController = player.GetComponent<PlayerController>();
            if (regularController != null)
            {
                currentRider = player;
                regularRiderController = regularController;
                regularController.enabled = false;
                
                // Snap player into seat
                player.transform.position = seatPoint.position;
                player.transform.rotation = seatPoint.rotation;
            }
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void EnterCanonServerRpc(ulong clientId)
    {
        if (networkOccupied.Value) return;
        
        // Set network variables
        networkOccupied.Value = true;
        networkRiderClientId.Value = clientId;
        
        // Notify all clients
        EnterCanonClientRpc(clientId);
    }
    
    [ClientRpc]
    private void EnterCanonClientRpc(ulong clientId)
    {
        // Find the player object with this client ID
        GameObject player = null;
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            if (client.Key == clientId && client.Value.PlayerObject != null)
            {
                player = client.Value.PlayerObject.gameObject;
                break;
            }
        }
        
        if (player != null)
        {
            // Store references
            currentRider = player;
            networkRiderController = player.GetComponent<NetworkPlayerController>();
            
            if (networkRiderController != null)
            {
                // Disable player controller
                networkRiderController.enabled = false;
                
                // Snap player into seat
                player.transform.position = seatPoint.position;
                player.transform.rotation = seatPoint.rotation;
            }
        }
    }
    
    [ClientRpc]
    private void ExitCanonClientRpc(ulong clientId, Vector3 launchVelocity)
    {
        // Find the player object with this client ID
        GameObject player = null;
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            if (client.Key == clientId && client.Value.PlayerObject != null)
            {
                player = client.Value.PlayerObject.gameObject;
                break;
            }
        }
        
        if (player != null)
        {
            NetworkPlayerController controller = player.GetComponent<NetworkPlayerController>();
            if (controller != null)
            {
                // Re-enable controller and launch
                controller.enabled = true;
                controller.Launch(launchVelocity);
            }
        }
        
        // Clean up local references
        if (currentRider != null && networkRiderController != null)
        {
            currentRider = null;
            networkRiderController = null;
        }
    }
    
    public void OnPlayerEnter(GameObject player)
    {
        // Add player to the list of players in range
        if (!playersInRange.Contains(player))
        {
            playersInRange.Add(player);
            
            // Check if this is the local player
            NetworkPlayerController networkController = player.GetComponent<NetworkPlayerController>();
            if (networkController != null && networkController.IsLocalPlayer())
            {
                localPlayerInRange = true;
            }
        }
    }

    public void OnPlayerStay(GameObject player)
    {
        throw new System.NotImplementedException();
    }


    public void OnPlayerExit(GameObject player)
    {
        // Remove player from the list of players in range
        if (playersInRange.Contains(player))
        {
            playersInRange.Remove(player);
            
            // Check if this is the local player
            NetworkPlayerController networkController = player.GetComponent<NetworkPlayerController>();
            if (networkController != null && networkController.IsLocalPlayer())
            {
                localPlayerInRange = false;
            }
        }
        
        // If this was the rider, don't do anything - shooting handles the exit
    }
    
    // Trigger collider events for detecting nearby players
    private void OnTriggerEnter(Collider other)
    {
        // Check if this is a player
        NetworkPlayerController networkController = other.GetComponent<NetworkPlayerController>();
        PlayerController regularController = other.GetComponent<PlayerController>();
        
        if (networkController != null || regularController != null)
        {
            OnPlayerEnter(other.gameObject);
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        // Check if this is a player
        NetworkPlayerController networkController = other.GetComponent<NetworkPlayerController>();
        PlayerController regularController = other.GetComponent<PlayerController>();
        
        if (networkController != null || regularController != null)
        {
            OnPlayerExit(other.gameObject);
        }
    }
}
