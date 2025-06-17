using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }
    
    public event Action<GamePhase, GamePhase> GamePhaseChanged;

    public enum GamePhase
    {
        MainMenu,
        ItemSelection,
        ItemPlacement,
        RoundInProgress,
        RoundOver
    }

    [SerializeField]
    private NetworkVariable<GamePhase> currentPhaseNet = new NetworkVariable<GamePhase>(GamePhase.MainMenu);

    public GamePhase CurrentPhase => currentPhaseNet.Value;

    [Header("UI Canvases")] [SerializeField]
    private Canvas itemSelectionCanvas;

    [SerializeField] private GameObject scoreboardPanel;

    [Header("Game Elements")] [SerializeField]
    private GameObject networkPlayerPrefab;

    [SerializeField] private Transform[] playerStartPoints;

    // Add default spawn position fallback
    [SerializeField] private Vector3 defaultSpawnPosition = new Vector3(0, 1, 0);

    [Header("Camera Settings for Phases")] [SerializeField]
    private float cameraZForItemSelection = 0f;

    [SerializeField] private float cameraZForPlacementAndGame = -25f;
    
    [SerializeField] private float cameraZForRoundOver = 75f;

    [Header("Item Placement Settings")]
    private NetworkVariable<FixedString64Bytes> currentItemToPlaceNet = new NetworkVariable<FixedString64Bytes>();

    private GameObject _ghostItemInstance;
    private bool _isPlacingItem;
    private float _currentGhostRotationZ = 0f;

    [SerializeField] private float placementDepthZ = 0f;
    [SerializeField] private LayerMask placementCollisionMask;
    [SerializeField] private Material ghostMaterialValid;
    [SerializeField] private Material ghostMaterialInvalid;

    [Header("Player Identification")] [SerializeField]
    private Material[] playerGhostMaterials; // Different colors for each player

    [SerializeField] private Material otherPlayerGhostMaterial;
    [SerializeField] private string placedItemLayerName = "PlacedItems";

    private Dictionary<Renderer, Material[]> _originalGhostMaterials = new Dictionary<Renderer, Material[]>();
    private List<NetworkObject> _placedNetworkItems = new List<NetworkObject>();
    private Dictionary<ulong, GameObject> _playerGhostItems = new Dictionary<ulong, GameObject>();
    private Plane _placementPlane;
    private Vector3 _potentialPlacementPosition;
    private bool _isPotentialPlacementValid;
    private Camera _mainCamera;

    // Player Management
    private Dictionary<ulong, GameObject> _connectedPlayers = new Dictionary<ulong, GameObject>();
    private NetworkVariable<int> _playersFinishedCount = new NetworkVariable<int>(0);

    // Cache for ghost position optimization
    private Vector3 _lastSentGhostPosition = Vector3.zero;
    private float _lastSentGhostRotation = 0f;

    // Item Selection Management
    private NetworkVariable<ulong> _currentItemSelectingPlayer = new NetworkVariable<ulong>();

    // Track each player's selected item for simultaneous placement
    private NetworkList<PlayerItemSelection> _playerItemSelections = new NetworkList<PlayerItemSelection>();

    // Track ghost item positions for all players
    private NetworkList<PlayerGhostData> _playerGhostPositions = new NetworkList<PlayerGhostData>();

    // Track which players have placed their items
    private NetworkList<ulong> _playersWhoPlaced = new NetworkList<ulong>();

    [Serializable]
    public struct PlayerItemSelection : INetworkSerializable, IEquatable<PlayerItemSelection>
    {
        public ulong playerId;
        public FixedString64Bytes itemName;
        public bool hasSelected;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref playerId);
            serializer.SerializeValue(ref itemName);
            serializer.SerializeValue(ref hasSelected);
        }

        public bool Equals(PlayerItemSelection other)
        {
            return playerId == other.playerId && itemName.Equals(other.itemName) && hasSelected == other.hasSelected;
        }
    }

    [Serializable]
    public struct PlayerGhostData : INetworkSerializable, IEquatable<PlayerGhostData>
    {
        public ulong playerId;
        public Vector3 position;
        public float rotationZ;
        public bool isVisible;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref playerId);
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref rotationZ);
            serializer.SerializeValue(ref isVisible);
        }

        public bool Equals(PlayerGhostData other)
        {
            return playerId == other.playerId;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }

        _mainCamera = Camera.main;
        if (_mainCamera == null)
        {
            Debug.LogError("NetworkGameManager: Main Camera not found!");
        }

        _placementPlane = new Plane(Vector3.forward, placementDepthZ);
        
        // Log ghost materials
        if (ghostMaterialValid == null)
        {
            Debug.LogError("NetworkGameManager: ghostMaterialValid is not assigned!");
        }

        if (ghostMaterialInvalid == null)
        {
            Debug.LogError("NetworkGameManager: ghostMaterialInvalid is not assigned!");
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsHost)
        {
            // Handle players who join after game scene is loaded
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            
            currentPhaseNet.Value = GamePhase.ItemSelection;
            
            SpawnAllConnectedPlayers();
        }

        // Subscribe to network variable changes
        currentPhaseNet.OnValueChanged += OnGamePhaseChanged;
        currentItemToPlaceNet.OnValueChanged += OnCurrentItemToPlaceChanged;

        // Subscribe to selection list changes
        _playerItemSelections.OnListChanged += OnPlayerSelectionsChanged;

        // Trigger initial phase handling for all clients (including host)
        GamePhase currentPhase = currentPhaseNet.Value;
        
        if (currentPhase == GamePhase.ItemSelection)
        {
            HandleItemSelectionPhase();
        }
        else if (currentPhase == GamePhase.RoundInProgress)
        {
            HandleRoundInProgressPhase();
            // Manually trigger the event for any listeners that registered after phase was set
            GamePhaseChanged?.Invoke(GamePhase.MainMenu, GamePhase.RoundInProgress);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsHost)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        currentPhaseNet.OnValueChanged -= OnGamePhaseChanged;
        currentItemToPlaceNet.OnValueChanged -= OnCurrentItemToPlaceChanged;
    }

    void Update()
    {
        if (CurrentPhase == GamePhase.ItemPlacement && _isPlacingItem && _ghostItemInstance != null)
        {
            HandleGhostItemPlacement();

            if (Input.GetKeyDown(KeyCode.Q))
            {
                _currentGhostRotationZ -= 15f;
                if (_currentGhostRotationZ < 0) _currentGhostRotationZ += 360f;
                UpdatePlacementValidityAfterUserAction();
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                _currentGhostRotationZ += 15f;
                if (_currentGhostRotationZ >= 360f) _currentGhostRotationZ -= 360f;
                UpdatePlacementValidityAfterUserAction();
            }

            if (Input.GetMouseButtonDown(1)) // Right Click to CONFIRM PLACEMENT
            {
                if (_isPotentialPlacementValid)
                {
                    ulong localClientId = NetworkManager.Singleton.LocalClientId;
                    ConfirmPlacementServerRpc(_potentialPlacementPosition, _currentGhostRotationZ, localClientId);
                }
            }

            // Sync ghost position to other players (only if changed significantly)
            float positionDistance = Vector3.Distance(_potentialPlacementPosition, _lastSentGhostPosition);
            float rotationDifference = Mathf.Abs(_currentGhostRotationZ - _lastSentGhostRotation);

            if (positionDistance > 0.1f || rotationDifference > 5f)
            {
                UpdateGhostPositionServerRpc(NetworkManager.Singleton.LocalClientId, _potentialPlacementPosition,
                    _currentGhostRotationZ, true);
                _lastSentGhostPosition = _potentialPlacementPosition;
                _lastSentGhostRotation = _currentGhostRotationZ;
            }
        }

        // Update other players' ghost items (only during placement phase)
        if (CurrentPhase == GamePhase.ItemPlacement)
        {
            UpdateOtherPlayersGhosts();
        }
    }

    private void SpawnAllConnectedPlayers()
    {
        if (!IsHost) return;

        // Spawn players for all connected clients
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            // Only spawn if they don't already have a player object
            if (client.PlayerObject == null)
            {
                SpawnPlayerForClient(client.ClientId);
            }
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} disconnected from game");
        if (_connectedPlayers.ContainsKey(clientId))
        {
            if (_connectedPlayers[clientId] != null)
            {
                _connectedPlayers[clientId].GetComponent<NetworkObject>().Despawn();
            }

            _connectedPlayers.Remove(clientId);
        }
    }

    private void SpawnPlayerForClient(ulong clientId)
    {
        if (!IsHost) return;

        Vector3 spawnPosition;

        // Check if we have valid spawn points
        if (playerStartPoints != null && playerStartPoints.Length > 0)
        {
            // Find an available spawn point
            int spawnIndex = (int)(clientId % (ulong)playerStartPoints.Length);
            
            if (playerStartPoints[spawnIndex] != null)
            {
                spawnPosition = playerStartPoints[spawnIndex].position;
            }
            else
            {
                Debug.LogWarning($"NetworkGameManager: Spawn point {spawnIndex} is null, using default position");
                spawnPosition = defaultSpawnPosition;
            }
        }
        else
        {
            Debug.LogWarning("NetworkGameManager: No spawn points assigned, using default position");
            spawnPosition = defaultSpawnPosition;
        }

        // Spawn the network player
        GameObject playerInstance = Instantiate(networkPlayerPrefab, spawnPosition, Quaternion.Euler(0, 90, 0));
        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();

        networkObject.SpawnAsPlayerObject(clientId);
        _connectedPlayers[clientId] = playerInstance;

        // Ensure player is actually at the spawn position
        var characterController = playerInstance.GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.enabled = false;
            playerInstance.transform.position = spawnPosition;
            characterController.enabled = true;
        }

        // Set initial state
        var playerController = playerInstance.GetComponent<NetworkPlayerController>();
        if (playerController != null)
        {
            // Set initial state based on current phase
            switch (CurrentPhase)
            {
                case GamePhase.RoundInProgress:
                    playerController.enabled = true;
                    break;
                case GamePhase.ItemSelection:
                case GamePhase.ItemPlacement:
                case GamePhase.RoundOver:
                default:
                    playerController.enabled = false;
                    break;
            }
        }

        var fallDetector = playerInstance.GetComponent<PlayerFallDetector>();
        if (fallDetector != null)
        {
            // Activate detection only during round in progress
            if (CurrentPhase == GamePhase.RoundInProgress)
            {
                fallDetector.ActivateDetection();
            }
            else
            {
                fallDetector.DeactivateDetection();
            }
        }
    }

    private void OnGamePhaseChanged(GamePhase oldPhase, GamePhase newPhase)
    {
        Debug.Log($"Game phase changed from {oldPhase} to {newPhase}");

        // Invoke the event for external listeners
        if (GamePhaseChanged != null)
        {
            Debug.Log($"NetworkGameManager: Firing GamePhaseChanged event with {oldPhase} to {newPhase}");
            GamePhaseChanged.Invoke(oldPhase, newPhase);
        }
        else
        {
            Debug.LogWarning("NetworkGameManager: GamePhaseChanged event has no subscribers");
        }

        switch (newPhase)
        {
            case GamePhase.ItemSelection:
                HandleItemSelectionPhase();
                break;
            case GamePhase.ItemPlacement:
                HandleItemPlacementPhase();
                break;
            case GamePhase.RoundInProgress:
                HandleRoundInProgressPhase();
                break;
            case GamePhase.RoundOver:
                HandleRoundOverPhase();
                break;
        }
    }

    private void OnCurrentItemToPlaceChanged(FixedString64Bytes oldValue, FixedString64Bytes newValue)
    {
        string itemName = newValue.ToString();

        if (!string.IsNullOrEmpty(itemName))
        {
            // Only the player who is placing the item should get the ghost
            if (NetworkManager.Singleton.LocalClientId == _currentItemSelectingPlayer.Value)
            {
                // Find the item prefab by name
                GameObject itemPrefab = Resources.Load<GameObject>($"Prefabs/PlaceableItems/{itemName}");
                if (itemPrefab != null)
                {
                    InitiateItemPlacement(itemPrefab);
                }
                else
                {
                    Debug.LogError(
                        $"NetworkGameManager: Could not load item prefab at 'Resources/Prefabs/PlaceableItems/{itemName}'");
                }
            }
        }
    }

    public void PrepareForItemSelection()
    {
        if (IsHost)
        {
            currentPhaseNet.Value = GamePhase.ItemSelection;
        }
    }

    private void HandleItemSelectionPhase()
    {
        Time.timeScale = 0f;
        MoveCameraToZ(cameraZForItemSelection);

        // Clear previous selections for new round
        if (IsHost)
        {
            _playerItemSelections.Clear();
            _playerGhostPositions.Clear();
            _playersWhoPlaced.Clear();

            // Notify all clients to show UI
            ShowItemSelectionUIClientRpc();
        }


        ClearAllGhostItems();

        // Disable local player controls for all clients
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        if (_connectedPlayers.TryGetValue(localClientId, out GameObject localPlayer))
        {
            var controller = localPlayer.GetComponent<NetworkPlayerController>();
            if (controller != null) controller.enabled = false;

            var fallDetector = localPlayer.GetComponent<PlayerFallDetector>();
            if (fallDetector != null) fallDetector.DeactivateDetection();
        }


        ShowItemSelectionUI();
        _isPlacingItem = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void SelectItemServerRpc(string itemPrefabName, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        // Update or add this player's selection
        bool playerFound = false;
        for (int i = 0; i < _playerItemSelections.Count; i++)
        {
            if (_playerItemSelections[i].playerId == senderId)
            {
                var selection = _playerItemSelections[i];
                selection.itemName = new FixedString64Bytes(itemPrefabName);
                selection.hasSelected = true;
                _playerItemSelections[i] = selection;
                playerFound = true;
                break;
            }
        }

        if (!playerFound)
        {
            _playerItemSelections.Add(new PlayerItemSelection
            {
                playerId = senderId,
                itemName = new FixedString64Bytes(itemPrefabName),
                hasSelected = true
            });
        }
    }

    private void OnPlayerSelectionsChanged(NetworkListEvent<PlayerItemSelection> changeEvent)
    {
        if (!IsHost) return;

        int connectedPlayerCount = NetworkManager.Singleton.ConnectedClientsList.Count;
        if (connectedPlayerCount == 0) return;

        int playersWithSelections = 0;
        var connectedClientIds = new HashSet<ulong>();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            connectedClientIds.Add(client.ClientId);
        }

        foreach (var selection in _playerItemSelections)
        {
            if (selection.hasSelected && connectedClientIds.Contains(selection.playerId))
            {
                playersWithSelections++;
            }
        }
        

        if (playersWithSelections >= connectedPlayerCount)
        {
            foreach (var selection in _playerItemSelections)
            {
                if (selection.hasSelected && connectedClientIds.Contains(selection.playerId))
                {
                    AssignItemToPlayerClientRpc(selection.itemName.ToString(),
                        new ClientRpcParams
                            { Send = new ClientRpcSendParams { TargetClientIds = new[] { selection.playerId } } });
                }
            }

            currentPhaseNet.Value = GamePhase.ItemPlacement;
        }
    }

    [ClientRpc]
    private void AssignItemToPlayerClientRpc(string itemName, ClientRpcParams rpcParams = default)
    {
        ulong localClientId = NetworkManager.Singleton.LocalClientId;

        GameObject itemPrefab = Resources.Load<GameObject>($"Prefabs/PlaceableItems/{itemName}");
        if (itemPrefab != null)
        {
            InitiateItemPlacement(itemPrefab);
        }
        else
        {
            Debug.LogError(
                $"NetworkGameManager: Client {localClientId} could not load item prefab '{itemName}' received from host.");
        }
    }

    public void InitiateItemPlacement(GameObject itemPrefabToPlace)
    {
        if (itemPrefabToPlace == null)
        {
            Debug.LogError("NetworkGameManager: Cannot initiate placement - itemPrefabToPlace is null");
            return;
        }
        

        MoveCameraToZ(cameraZForPlacementAndGame);

        if (itemSelectionCanvas != null)
            itemSelectionCanvas.gameObject.SetActive(false);

        _isPlacingItem = true;
        _currentGhostRotationZ = 0f;
        InstantiateGhostItem(itemPrefabToPlace);
    }

    private void HandleItemPlacementPhase()
    {
        Time.timeScale = 0f; // Keep paused during placement
        MoveCameraToZ(cameraZForPlacementAndGame);

        if (itemSelectionCanvas != null)
            itemSelectionCanvas.gameObject.SetActive(false);
    }

    private void InstantiateGhostItem(GameObject itemPrefab)
    {
        ClearGhostItem();

        _ghostItemInstance = Instantiate(itemPrefab);
        _ghostItemInstance.name = itemPrefab.name + "_Ghost";

        // Disable any network components on ghost
        var networkComponents = _ghostItemInstance.GetComponentsInChildren<NetworkBehaviour>();
        foreach (var comp in networkComponents)
        {
            comp.enabled = false;
        }

        // Disable any rigidbodies to prevent physics interference
        var rigidbodies = _ghostItemInstance.GetComponentsInChildren<Rigidbody>();
        foreach (var rb in rigidbodies)
        {
            rb.isKinematic = true;
        }

        // Store original materials
        _originalGhostMaterials.Clear();
        Renderer[] renderers = _ghostItemInstance.GetComponentsInChildren<Renderer>();
        foreach (Renderer rend in renderers)
        {
            _originalGhostMaterials[rend] = rend.materials;
        }

        _ghostItemInstance.transform.rotation = Quaternion.Euler(0, 0, _currentGhostRotationZ);

        // Make sure the ghost is visible
        _ghostItemInstance.SetActive(true);
    }

    private void HandleGhostItemPlacement()
    {
        if (_ghostItemInstance == null)
        {
            _isPotentialPlacementValid = false;
            UpdateGhostMaterialVisuals(false);
            Debug.LogWarning("NetworkGameManager: Ghost item instance is null in HandleGhostItemPlacement");
            return;
        }

        if (_mainCamera == null)
        {
            Debug.LogError("NetworkGameManager: Main camera is null, cannot handle ghost placement");
            return;
        }

        Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);


        if (_placementPlane.Raycast(ray, out float enter))
        {
            Vector3 rawHitPoint = ray.GetPoint(enter);
            _potentialPlacementPosition = new Vector3(rawHitPoint.x, rawHitPoint.y, placementDepthZ);

            _ghostItemInstance.transform.position = _potentialPlacementPosition;
            _ghostItemInstance.transform.rotation = Quaternion.Euler(0, 0, _currentGhostRotationZ);

            _isPotentialPlacementValid =
                CheckPlacementValidity(_potentialPlacementPosition, _ghostItemInstance.transform.rotation);
            UpdateGhostMaterialVisuals(_isPotentialPlacementValid);
        }
        else
        {
            _isPotentialPlacementValid = false;
            UpdateGhostMaterialVisuals(false);
            Debug.LogWarning(
                $"NetworkGameManager: Ray did not hit placement plane. Plane: normal={_placementPlane.normal}, distance={_placementPlane.distance}");
        }
    }

    private void UpdatePlacementValidityAfterUserAction()
    {
        if (_ghostItemInstance == null) return;
        _ghostItemInstance.transform.rotation = Quaternion.Euler(0, 0, _currentGhostRotationZ);
        _isPotentialPlacementValid =
            CheckPlacementValidity(_potentialPlacementPosition, _ghostItemInstance.transform.rotation);
        UpdateGhostMaterialVisuals(_isPotentialPlacementValid);
    }

    private bool CheckPlacementValidity(Vector3 positionToCheck, Quaternion rotationToCheck)
    {
        if (_ghostItemInstance == null)
        {
            Debug.LogWarning("NetworkGameManager: Cannot check placement validity - ghost item is null");
            return false;
        }

        Collider primaryCollider = _ghostItemInstance.GetComponent<Collider>();
        Collider[] itemColliders = _ghostItemInstance.GetComponentsInChildren<Collider>(false);
        if (primaryCollider == null && itemColliders.Length > 0)
            primaryCollider = itemColliders[0];

        if (primaryCollider != null && primaryCollider.enabled)
        {
            Vector3 boxHalfExtents = GetColliderBounds(primaryCollider, _ghostItemInstance.transform);
            boxHalfExtents *= 0.95f;

            Collider[] overlaps = Physics.OverlapBox(positionToCheck, boxHalfExtents, rotationToCheck,
                placementCollisionMask, QueryTriggerInteraction.Ignore);

            if (overlaps.Length > 0)
            {
                foreach (var overlap in overlaps)
                {
                    if (overlap.transform == _ghostItemInstance.transform ||
                        overlap.transform.IsChildOf(_ghostItemInstance.transform))
                    {
                        continue;
                    }
                    
                    return false;
                }
            }


            return true;
        }
        else
        {
            Debug.LogWarning("NetworkGameManager: No valid collider found on ghost item, assuming placement is valid");
            return true;
        }
    }

    private Vector3 GetColliderBounds(Collider collider, Transform transform)
    {
        if (collider is BoxCollider boxCol)
            return Vector3.Scale(boxCol.size, transform.lossyScale) / 2f;
        else if (collider is SphereCollider sphereCol)
        {
            float r = sphereCol.radius *
                      Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
            return new Vector3(r, r, r);
        }
        else
            return collider.bounds.extents;
    }

    private void UpdateGhostMaterialVisuals(bool isValid)
    {
        if (_ghostItemInstance == null)
        {
            Debug.LogWarning("NetworkGameManager: Cannot update ghost visuals - ghost item is null");
            return;
        }

        Renderer[] renderers = _ghostItemInstance.GetComponentsInChildren<Renderer>();
        Material materialToApply = isValid ? ghostMaterialValid : ghostMaterialInvalid;

        if (materialToApply == null)
        {
            Debug.LogError(
                $"NetworkGameManager: Ghost material is null! Valid material: {ghostMaterialValid}, Invalid material: {ghostMaterialInvalid}");
            return;
        }


        foreach (Renderer rend in renderers)
        {
            if (rend == null) continue;

            Material[] newMaterials = new Material[rend.sharedMaterials.Length];
            for (int i = 0; i < newMaterials.Length; i++)
                newMaterials[i] = materialToApply;
            rend.materials = newMaterials;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConfirmPlacementServerRpc(Vector3 position, float rotationZ, ulong placerClientId,
        ServerRpcParams rpcParams = default)
    {
        // Find this player's selected item
        string itemName = "";
        foreach (var selection in _playerItemSelections)
        {
            if (selection.playerId == placerClientId && selection.hasSelected)
            {
                itemName = selection.itemName.ToString();
                break;
            }
        }

        if (string.IsNullOrEmpty(itemName))
        {
            Debug.LogError($"NetworkGameManager: No item selected for player {placerClientId}");
            return;
        }

        SpawnPlacedItemClientRpc(itemName, position, rotationZ);

        if (!_playersWhoPlaced.Contains(placerClientId))
        {
            _playersWhoPlaced.Add(placerClientId);
        }

        // Tell the placer client to clean up its ghost
        PlacerCleanUpGhostClientRpc(new ClientRpcParams
            { Send = new ClientRpcSendParams { TargetClientIds = new[] { placerClientId } } });

        CheckAllPlayersPlacedItems();
    }

    [ClientRpc]
    private void PlacerCleanUpGhostClientRpc(ClientRpcParams clientRpcParams = default)
    {
        // This RPC is only received by the client who placed the item.
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        ClearGhostItem();
        _isPlacingItem = false;
        
        UpdateGhostPositionServerRpc(localClientId, Vector3.zero, 0f, false);
    }

    [ClientRpc]
    private void SpawnPlacedItemClientRpc(string itemName, Vector3 position, float rotationZ)
    {
        GameObject itemPrefab = Resources.Load<GameObject>($"Prefabs/PlaceableItems/{itemName}");
        if (itemPrefab != null)
        {
            GameObject placedItem = Instantiate(itemPrefab, position, Quaternion.Euler(0, 0, rotationZ));
            placedItem.name = itemName + "_Placed";

            int layer = LayerMask.NameToLayer(placedItemLayerName);
            if (layer != -1) SetLayerRecursively(placedItem, layer);

            // Add to placed items list if it has a NetworkObject
            NetworkObject networkObj = placedItem.GetComponent<NetworkObject>();
            if (networkObj != null)
            {
                _placedNetworkItems.Add(networkObj);
            }
            
        }
        else
        {
            Debug.LogError($"NetworkGameManager: Failed to load item prefab '{itemName}' for placement");
        }
    }

    private void HandleRoundInProgressPhase()
    {
        Time.timeScale = 1f;
        MoveCameraToZ(cameraZForPlacementAndGame);

        // Reset all players
        foreach (var kvp in _connectedPlayers)
        {
            if (kvp.Value != null)
            {
                ResetPlayer(kvp.Value, kvp.Key);
            }
        }

        // Reset finished player count
        if (IsHost)
        {
            _playersFinishedCount.Value = 0;
        }

        ClearGhostItem();
        _isPlacingItem = false;

        if (itemSelectionCanvas != null)
            itemSelectionCanvas.gameObject.SetActive(false);
    }

    private void ResetPlayer(GameObject player, ulong clientId)
    {
        Vector3 spawnPosition;

        // Check if we have valid spawn points
        if (playerStartPoints != null && playerStartPoints.Length > 0)
        {
            // Reset position
            int spawnIndex = (int)(clientId % (ulong)playerStartPoints.Length);

            if (playerStartPoints[spawnIndex] != null)
            {
                spawnPosition = playerStartPoints[spawnIndex].position;
            }
            else
            {
                Debug.LogWarning($"NetworkGameManager: Spawn point {spawnIndex} is null, using default position");
                spawnPosition = defaultSpawnPosition;
            }
        }
        else
        {
            Debug.LogWarning("NetworkGameManager: No spawn points assigned, using default position");
            spawnPosition = defaultSpawnPosition;
        }

        var characterController = player.GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.enabled = false;
            player.transform.position = spawnPosition;
            characterController.enabled = true;
        }

        // Enable components
        var playerController = player.GetComponent<NetworkPlayerController>();
        if (playerController != null)
        {
            // Update server-side NetworkVariable so observers move instantly
            playerController.SetServerPosition(spawnPosition);

            // Tell the owning client to also teleport its local representation
            playerController.TeleportClientRpc(
                spawnPosition,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                });

            // Reset visibility to true when the round restarts
            playerController.SetVisibility(true);

            playerController.enabled = true;
        }

        var deathHandler = player.GetComponent<PlayerDeathHandler>();
        if (deathHandler != null) deathHandler.enabled = true;

        var fallDetector = player.GetComponent<PlayerFallDetector>();
        if (fallDetector != null) fallDetector.ActivateDetection();
    }

    private void HandleRoundOverPhase()
    {
        Time.timeScale = 0f;
        
        // Move camera far back to see the level
        MoveCameraToZ(cameraZForRoundOver);

        // Disable local player controls
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        if (_connectedPlayers.TryGetValue(localClientId, out var localPlayer))
            if (localPlayer.GetComponent<NetworkPlayerController>()
                is NetworkPlayerController controller)
            {
                controller.enabled = false;
            }

        // Show the scoreboard
        if (scoreboardPanel != null)
            scoreboardPanel.SetActive(true);

        // After 5 seconds, hide it and go to item selection
        StartCoroutine(ShowScoreboardThenNext());
    }

    private System.Collections.IEnumerator ShowScoreboardThenNext()
    {
        yield return new WaitForSecondsRealtime(5f);

        // Hide it
        if (scoreboardPanel != null)
            scoreboardPanel.SetActive(false);
        
        
        if (IsHost)
            currentPhaseNet.Value = GamePhase.ItemSelection;
    }

    [ServerRpc(RequireOwnership = false)]
    public void PlayerFinishedLevelServerRpc(ServerRpcParams rpcParams = default)
    {
        _playersFinishedCount.Value++;

        // Check if all players finished
        if (_playersFinishedCount.Value >= NetworkManager.Singleton.ConnectedClientsList.Count)
        {
            currentPhaseNet.Value = GamePhase.RoundOver;
        }
    }

    private void MoveCameraToZ(float targetZ)
    {
        if (_mainCamera == null) return;
        Vector3 currentCamPos = _mainCamera.transform.position;
        Vector3 targetCamPos = new Vector3(currentCamPos.x, currentCamPos.y, targetZ);
        _mainCamera.transform.position = targetCamPos;
    }

    private void ClearGhostItem()
    {
        if (_ghostItemInstance != null)
        {
            Destroy(_ghostItemInstance);
            _ghostItemInstance = null;
        }

        _originalGhostMaterials.Clear();
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
    
    public void SetPlayerSpawnPoints(Transform[] spawnPoints)
    {
        playerStartPoints = spawnPoints;
    }
    
    public void SetItemSelectionCanvas(Canvas canvas)
    {
        itemSelectionCanvas = canvas;
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} connected to game scene");

        // Only spawn if they don't already have a player object
        // This handles late joiners who connect after the game scene has loaded
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject == null)
            {
                SpawnPlayerForClient(clientId);
            }
            else
            {
                Debug.Log($"Client {clientId} already has a player object, skipping spawn");
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdateGhostPositionServerRpc(ulong clientId, Vector3 position, float rotationZ, bool isVisible,
        ServerRpcParams rpcParams = default)
    {
        // Update or add ghost data for this player
        bool playerFound = false;
        for (int i = 0; i < _playerGhostPositions.Count; i++)
        {
            if (_playerGhostPositions[i].playerId == clientId)
            {
                var ghostData = _playerGhostPositions[i];
                ghostData.position = position;
                ghostData.rotationZ = rotationZ;
                ghostData.isVisible = isVisible;
                _playerGhostPositions[i] = ghostData;
                playerFound = true;
                break;
            }
        }

        if (!playerFound)
        {
            _playerGhostPositions.Add(new PlayerGhostData
            {
                playerId = clientId,
                position = position,
                rotationZ = rotationZ,
                isVisible = isVisible
            });
        }
    }

    private void UpdateOtherPlayersGhosts()
    {
        if (_playerGhostPositions == null || _playerItemSelections == null || NetworkManager.Singleton == null)
        {
            return;
        }

        ulong localClientId = NetworkManager.Singleton.LocalClientId;

        foreach (var ghostData in _playerGhostPositions)
        {
            if (ghostData.playerId == localClientId) continue; // Skip local player

            // Find or create ghost for other player
            if (!_playerGhostItems.TryGetValue(ghostData.playerId, out GameObject otherPlayerGhost))
            {
                // Find this player's selected item
                string itemName = "";
                foreach (var selection in _playerItemSelections)
                {
                    if (selection.playerId == ghostData.playerId && selection.hasSelected)
                    {
                        itemName = selection.itemName.ToString();
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(itemName))
                {
                    GameObject itemPrefab = Resources.Load<GameObject>($"Prefabs/PlaceableItems/{itemName}");
                    if (itemPrefab != null)
                    {
                        otherPlayerGhost = Instantiate(itemPrefab);
                        otherPlayerGhost.name = $"Player{ghostData.playerId}_Ghost_{itemName}";

                        // Set up as non-interactive ghost
                        SetupOtherPlayerGhost(otherPlayerGhost, ghostData.playerId);
                        _playerGhostItems[ghostData.playerId] = otherPlayerGhost;
                    }
                }
            }

            // Update position and visibility
            if (otherPlayerGhost != null)
            {
                otherPlayerGhost.transform.position = ghostData.position;
                otherPlayerGhost.transform.rotation = Quaternion.Euler(0, 0, ghostData.rotationZ);
                otherPlayerGhost.SetActive(ghostData.isVisible);
            }
        }
    }

    private void SetupOtherPlayerGhost(GameObject ghost, ulong playerId)
    {
        // Disable network components
        var networkComponents = ghost.GetComponentsInChildren<NetworkBehaviour>();
        foreach (var comp in networkComponents)
        {
            comp.enabled = false;
        }

        // Make rigidbodies kinematic
        var rigidbodies = ghost.GetComponentsInChildren<Rigidbody>();
        foreach (var rb in rigidbodies)
        {
            rb.isKinematic = true;
        }

        // Set distinctive material for other players
        Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>();
        Material materialToUse = GetPlayerGhostMaterial(playerId);

        foreach (Renderer rend in renderers)
        {
            Material[] newMaterials = new Material[rend.sharedMaterials.Length];
            for (int i = 0; i < newMaterials.Length; i++)
                newMaterials[i] = materialToUse;
            rend.materials = newMaterials;
        }
    }

    private Material GetPlayerGhostMaterial(ulong playerId)
    {
        if (playerGhostMaterials != null && playerGhostMaterials.Length > 0)
        {
            int materialIndex = (int)(playerId % (ulong)playerGhostMaterials.Length);
            return playerGhostMaterials[materialIndex];
        }

        return otherPlayerGhostMaterial ?? ghostMaterialValid;
    }

    private void ClearAllGhostItems()
    {
        ClearGhostItem();

        // Clear other players' ghost items
        if (_playerGhostItems != null)
        {
            foreach (var kvp in _playerGhostItems)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }
            }

            _playerGhostItems.Clear();
        }
    }

    private void CheckAllPlayersPlacedItems()
    {
        if (!IsHost) return;

        int connectedPlayerCount = NetworkManager.Singleton.ConnectedClientsList.Count;
        int playersWhoPlacedCount = _playersWhoPlaced.Count;

        // If all players have placed their items, move to round in progress
        if (playersWhoPlacedCount >= connectedPlayerCount && connectedPlayerCount > 0)
        {
            currentPhaseNet.Value = GamePhase.RoundInProgress;
        }
    }

    private void ShowItemSelectionUI()
    {
        if (itemSelectionCanvas != null)
        {
            itemSelectionCanvas.gameObject.SetActive(true);
            ItemDisplayer displayer = itemSelectionCanvas.GetComponentInChildren<ItemDisplayer>();
            displayer?.DisplayNewItems();
        }
        else
        {
            Debug.LogWarning("NetworkGameManager: Item selection canvas is null!");
        }
    }

    [ClientRpc]
    private void ShowItemSelectionUIClientRpc()
    {
        ShowItemSelectionUI();
    }
    
    public void EndRoundDueToTimeout()
    {
        if (!IsHost) return;
        currentPhaseNet.Value = GamePhase.RoundOver;
    }
}