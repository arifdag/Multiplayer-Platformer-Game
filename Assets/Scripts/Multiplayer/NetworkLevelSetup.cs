using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class NetworkLevelSetup : NetworkBehaviour
{
    [Header("Game Setup")]
    [SerializeField] private GameObject networkGameManagerPrefab;
    [SerializeField] private GameObject networkPlayerPrefab;
    [SerializeField] private Transform[] playerSpawnPoints;
    [SerializeField] private Canvas itemSelectionCanvas;
    
    [Header("Camera Setup")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Vector3 cameraInitialPosition = new Vector3(0, 0, -25);
    
    private bool _hasSetupCompleted = false;

    void Start()
    {
        // Only run setup once per scene load
        if (_hasSetupCompleted) return;
        
        SetupScene();
        _hasSetupCompleted = true;
    }

    private void SetupScene()
    {
        // Setup camera if not assigned
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                // Create a camera if none exists
                GameObject cameraGO = new GameObject("Main Camera");
                mainCamera = cameraGO.AddComponent<Camera>();
                cameraGO.tag = "MainCamera";
                AudioListener audioListener = cameraGO.AddComponent<AudioListener>();
            }
        }
        
 
        mainCamera.transform.position = cameraInitialPosition;
        
        // Only host should spawn the NetworkGameManager
        if (NetworkManager.Singleton.IsHost)
        {
            SpawnNetworkGameManager();
        }
    }

    private void SpawnNetworkGameManager()
    {
        if (NetworkGameManager.Instance != null)
        {
            return;
        }

        if (networkGameManagerPrefab == null)
        {
            Debug.LogError("NetworkLevelSetup: NetworkGameManager prefab not assigned!");
            return;
        }

        // Spawn the NetworkGameManager
        GameObject gameManagerGO = Instantiate(networkGameManagerPrefab);
        NetworkObject networkObject = gameManagerGO.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            networkObject.Spawn();
            
            // Configure the NetworkGameManager
            NetworkGameManager gameManager = gameManagerGO.GetComponent<NetworkGameManager>();
            if (gameManager != null)
            {
                SetupGameManager(gameManager);
            }
        }
        else
        {
            Debug.LogError("NetworkLevelSetup: NetworkGameManager prefab doesn't have a NetworkObject component!");
            Destroy(gameManagerGO);
        }
    }

    private void SetupGameManager(NetworkGameManager gameManager)
    {
        // Configure spawn points if available in this scene
        if (playerSpawnPoints != null && playerSpawnPoints.Length > 0)
        {
            gameManager.SetPlayerSpawnPoints(playerSpawnPoints);
        }
        else
        {
            Debug.LogWarning("NetworkLevelSetup: No player spawn points assigned in this scene!");
        }
        
        // Set up item selection canvas if available
        if (itemSelectionCanvas != null)
        {
            gameManager.SetItemSelectionCanvas(itemSelectionCanvas);
        }
        else
        {
            Debug.LogWarning("NetworkLevelSetup: No item selection canvas assigned in this scene!");
        }
        
    }

    // Called when all clients have loaded the scene
    [ClientRpc]
    public void OnAllClientsLoadedClientRpc()
    {
        SetupLocalPlayer();
    }

    private void SetupLocalPlayer()
    {
        // Find the local player and ensure it's properly configured
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null && client.PlayerObject.IsOwner)
            {
                var playerController = client.PlayerObject.GetComponent<NetworkPlayerController>();
                if (playerController != null)
                {
                    break;
                }
            }
        }
    }

    // Utility method to return to lobby
    public void ReturnToLobby()
    {
        if (NetworkManager.Singleton.IsHost)
        {
            // Load lobby scene for all clients
            NetworkManager.Singleton.SceneManager.LoadScene("LobbyScene", LoadSceneMode.Single);
        }
    }

    // Called when a client disconnects
    public void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} disconnected from game scene");
    }

    void OnDestroy()
    {
        // Cleanup when scene is unloaded
        _hasSetupCompleted = false;
    }

    // Gizmos for editor visualization
    void OnDrawGizmos()
    {
        if (playerSpawnPoints != null)
        {
            Gizmos.color = Color.blue;
            for (int i = 0; i < playerSpawnPoints.Length; i++)
            {
                if (playerSpawnPoints[i] != null)
                {
                    Gizmos.DrawWireSphere(playerSpawnPoints[i].position, 0.5f);
                    Gizmos.DrawLine(playerSpawnPoints[i].position, playerSpawnPoints[i].position + Vector3.up * 2f);
                }
            }
        }
    }
} 