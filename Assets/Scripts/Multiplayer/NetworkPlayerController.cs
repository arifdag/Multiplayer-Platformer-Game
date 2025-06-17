using System;
using UnityEngine;
using Unity.Netcode;

public class NetworkPlayerController : NetworkBehaviour
{
    [Header("Movement Settings")] [SerializeField]
    private Vector2 speedInterval = new Vector2(5, 10);

    [SerializeField] private float jumpSpeed = 8f;
    [SerializeField, Min(0.01f)] private float runRampTime = 1f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField, Min(0.01f)] private float gravityMultiplier = 2f;

    [Header("Visual Settings")] [SerializeField]
    private Renderer playerRenderer;

    private CharacterController _characterController;
    private Animator _animator;

    // Network variables for synchronization
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<Vector3> networkVelocity = new NetworkVariable<Vector3>();
    private NetworkVariable<bool> networkIsGrounded = new NetworkVariable<bool>();
    private NetworkVariable<bool> networkFacingRight = new NetworkVariable<bool>(true);
    private NetworkVariable<bool> networkIsDancing = new NetworkVariable<bool>();
    private NetworkVariable<int> networkColorIndex = new NetworkVariable<int>(-1);
    private NetworkVariable<bool> networkIsVisible = new NetworkVariable<bool>(true);

    // Movement state
    private bool _isGrounded;
    private bool _lastFacingRight = true;
    private bool _isDancing = false;
    private bool _isLaunched = false;

    private float _horizontalInput;
    private float _targetHorizontalSpeed;
    private float _runTimer;
    private Vector3 _velocity;

    // Client-side prediction
    private Vector3 _clientPosition;
    private Vector3 _clientVelocity;
    private float _lastInputTime;

    // Color management
    public Color[] availableColors = new Color[8];

    // Animator hashes
    private static readonly int JumpAnimTrigger = Animator.StringToHash("Jump");
    private static readonly int IsWalking = Animator.StringToHash("IsWalking");
    private static readonly int IsRunning = Animator.StringToHash("IsRunning");
    private static readonly int IsDancing = Animator.StringToHash("IsDancing");

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _animator = GetComponent<Animator>();

        if (playerRenderer == null)
            playerRenderer = GetComponentInChildren<Renderer>();
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe to network variable changes for non-owners
        if (!IsOwner)
        {
            networkPosition.OnValueChanged += OnNetworkPositionChanged;
            networkVelocity.OnValueChanged += OnNetworkVelocityChanged;
            networkIsGrounded.OnValueChanged += OnNetworkGroundedChanged;
            networkFacingRight.OnValueChanged += OnNetworkFacingChanged;
            networkIsDancing.OnValueChanged += OnNetworkDancingChanged;
            networkIsVisible.OnValueChanged += OnNetworkVisibilityChanged;
        }

        networkColorIndex.OnValueChanged += OnNetworkColorChanged;

        // Initialize position
        if (IsOwner)
        {
            _clientPosition = transform.position;
            _clientVelocity = Vector3.zero;
        }
        else
        {
            transform.position = networkPosition.Value;
            _velocity = networkVelocity.Value;
        }

        // Get color from lobby state if available
        var lobbyState = GetComponent<LobbyPlayerState>();
        if (lobbyState != null)
        {
            UpdatePlayerColor(lobbyState.ColorIndexNet.Value);
            lobbyState.ColorIndexNet.OnValueChanged += (oldVal, newVal) => UpdatePlayerColor(newVal);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner)
        {
            networkPosition.OnValueChanged -= OnNetworkPositionChanged;
            networkVelocity.OnValueChanged -= OnNetworkVelocityChanged;
            networkIsGrounded.OnValueChanged -= OnNetworkGroundedChanged;
            networkFacingRight.OnValueChanged -= OnNetworkFacingChanged;
            networkIsDancing.OnValueChanged -= OnNetworkDancingChanged;
            networkIsVisible.OnValueChanged -= OnNetworkVisibilityChanged;
        }

        networkColorIndex.OnValueChanged -= OnNetworkColorChanged;
    }

    void Update()
    {
        if (IsOwner)
        {
            HandleOwnerUpdate();
        }
        else
        {
            HandleNonOwnerUpdate();
        }
    }

    private void HandleOwnerUpdate()
    {
        _isGrounded = _characterController.isGrounded;

        // If grounded and falling, reset vertical velocity
        if (_isGrounded && _velocity.y < 0)
        {
            _velocity.y = -2f;
        }

        ProcessDanceInput();

        if (!_isDancing)
        {
            ProcessMovementInput();
            ProcessJumpInput();

            _velocity.x = _targetHorizontalSpeed;

            // Animation & Direction based on input
            bool isMovingInput = Mathf.Abs(_horizontalInput) > 0.01f;
            bool isRunningInput = isMovingInput && Input.GetKey(KeyCode.LeftShift);
            UpdateMovementAnimation(isMovingInput, isRunningInput);

            if (isMovingInput)
            {
                if (_horizontalInput < 0 && _lastFacingRight)
                {
                    ChangeDirection();
                }
                else if (_horizontalInput > 0 && !_lastFacingRight)
                {
                    ChangeDirection();
                }
            }
        }
        else
        {
            _velocity.x = 0f;
            _horizontalInput = 0f;
            _targetHorizontalSpeed = 0f;
            UpdateMovementAnimation(false, false);
        }

        // Apply gravity
        _velocity.y += gravity * gravityMultiplier * Time.deltaTime;
        
        _characterController.Move(_velocity * Time.deltaTime);

        // Update client prediction position
        _clientPosition = transform.position;
        _clientVelocity = _velocity;

        // Send updates to server at regular intervals
        _lastInputTime += Time.deltaTime;
        if (_lastInputTime >= 1f / 30f) // 30 updates per second
        {
            SendPlayerUpdateServerRpc(transform.position, _velocity, _isGrounded, _lastFacingRight, _isDancing);
            _lastInputTime = 0f;
        }
    }

    private void HandleNonOwnerUpdate()
    {
        // Simple interpolation for non-owners
        if (Vector3.Distance(transform.position, networkPosition.Value) > 0.1f)
        {
            transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 10f);
        }

        // Update visual state
        _isGrounded = networkIsGrounded.Value;
        _lastFacingRight = networkFacingRight.Value;
        _isDancing = networkIsDancing.Value;
        _velocity = networkVelocity.Value;

        // Update animations based on network state
        bool isMoving = Mathf.Abs(_velocity.x) > 0.1f;
        bool isRunning = Mathf.Abs(_velocity.x) > speedInterval.x + 1f;
        UpdateMovementAnimation(isMoving && !_isDancing, isRunning && !_isDancing);
    }

    [ServerRpc]
    private void SendPlayerUpdateServerRpc(Vector3 position, Vector3 velocity, bool isGrounded, bool facingRight,
        bool isDancing)
    {
        // Server authoritative position validation
        float maxSpeed = speedInterval.y + 2f; // Allow some tolerance
        if (velocity.magnitude > maxSpeed)
        {
            velocity = velocity.normalized * maxSpeed;
        }

        // Update network variables
        networkPosition.Value = position;
        networkVelocity.Value = velocity;
        networkIsGrounded.Value = isGrounded;
        networkFacingRight.Value = facingRight;
        networkIsDancing.Value = isDancing;
    }

    private void OnNetworkPositionChanged(Vector3 oldPos, Vector3 newPos)
    {
        if (!IsOwner)
        {
            // Simple teleport if too far away, otherwise smooth interpolation happens in Update
            if (Vector3.Distance(transform.position, newPos) > 5f)
            {
                transform.position = newPos;
            }
        }
    }

    private void OnNetworkVelocityChanged(Vector3 oldVel, Vector3 newVel)
    {
        if (!IsOwner)
        {
            _velocity = newVel;
        }
    }

    private void OnNetworkGroundedChanged(bool oldGrounded, bool newGrounded)
    {
        if (!IsOwner)
        {
            _isGrounded = newGrounded;
        }
    }

    private void OnNetworkFacingChanged(bool oldFacing, bool newFacing)
    {
        if (!IsOwner && _lastFacingRight != newFacing)
        {
            _lastFacingRight = newFacing;
            // Apply rotation to match facing direction
            float targetY = newFacing ? 90f : 270f;
            transform.rotation = Quaternion.Euler(0f, targetY, 0f);
        }
    }

    private void OnNetworkDancingChanged(bool oldDancing, bool newDancing)
    {
        if (!IsOwner)
        {
            _isDancing = newDancing;
            if (_animator != null)
            {
                _animator.SetBool(IsDancing, newDancing);
            }
        }
    }

    private void OnNetworkColorChanged(int oldColor, int newColor)
    {
        UpdatePlayerColor(newColor);
    }

    private void UpdatePlayerColor(int colorIndex)
    {
        if (playerRenderer != null && colorIndex >= 0 && colorIndex < availableColors.Length)
        {
            playerRenderer.material.color = availableColors[colorIndex];
        }
    }

    private void ProcessMovementInput()
    {
        _horizontalInput = Input.GetAxisRaw("Horizontal");

        bool isTryingToMove = Mathf.Abs(_horizontalInput) > 0.01f;
        bool isRunning = isTryingToMove && Input.GetKey(KeyCode.LeftShift);

        _runTimer += (isRunning ? 1 : -1) * Time.deltaTime;
        _runTimer = Mathf.Clamp(_runTimer, 0f, runRampTime);

        float tNorm = (runRampTime > 0f) ? _runTimer / runRampTime : 1f;
        tNorm = Mathf.Clamp01(tNorm);
        float easedT = Mathf.SmoothStep(0f, 1f, tNorm);

        float currentSpeed = Mathf.Lerp(speedInterval.x, speedInterval.y, easedT);
        _targetHorizontalSpeed = _horizontalInput * currentSpeed;
    }

    private void UpdateMovementAnimation(bool isMoving, bool isRunning)
    {
        if (_animator != null)
        {
            if (!_isDancing)
            {
                _animator.SetBool(IsWalking, isMoving && !isRunning);
                _animator.SetBool(IsRunning, isRunning);
            }
            else
            {
                _animator.SetBool(IsWalking, false);
                _animator.SetBool(IsRunning, false);
            }
        }
    }

    private void ProcessJumpInput()
    {
        if (Input.GetButtonDown("Jump") && _isGrounded)
        {
            _velocity.y = jumpSpeed;
            if (_animator != null)
            {
                _animator.SetTrigger(JumpAnimTrigger);
            }
        }
    }

    private void ProcessDanceInput()
    {
        if (Input.GetKeyDown(KeyCode.H))
        {
            _isDancing = true;
            if (_animator != null)
            {
                _animator.SetBool(IsDancing, true);
            }
        }

        if (_isDancing)
        {
            if (Input.anyKeyDown && !Input.GetKeyDown(KeyCode.H))
            {
                _isDancing = false;
                if (_animator != null)
                {
                    _animator.SetBool(IsDancing, false);
                }
            }
        }
    }

    private void ChangeDirection()
    {
        if (_isDancing) return;
        transform.Rotate(0f, 180f, 0f, Space.World);
        _lastFacingRight = !_lastFacingRight;
    }

    public void Launch(Vector3 initialVelocity)
    {
        _velocity = initialVelocity;
        _isLaunched = true;

        if (IsOwner)
        {
            LaunchServerRpc(initialVelocity);
        }
    }

    [ServerRpc]
    private void LaunchServerRpc(Vector3 initialVelocity)
    {
        LaunchClientRpc(initialVelocity);
    }

    [ClientRpc]
    private void LaunchClientRpc(Vector3 initialVelocity)
    {
        if (!IsOwner)
        {
            _velocity = initialVelocity;
            _isLaunched = true;
        }
    }

    // Public method for external scripts to check if this is the local player
    public bool IsLocalPlayer()
    {
        return IsOwner;
    }

    // Public method to get the player's current color index
    public int GetColorIndex()
    {
        return networkColorIndex.Value;
    }

    // Method to request color change (for UI)
    public void RequestColorChange(int newColorIndex)
    {
        if (IsOwner)
        {
            var lobbyState = GetComponent<LobbyPlayerState>();
            if (lobbyState != null)
            {
                lobbyState.RequestColorChange(newColorIndex);
            }
        }
    }

    // Public helper to set player position on the server side and update network variables
    public void SetServerPosition(Vector3 newPosition)
    {
        if (!IsServer) return;
        networkPosition.Value = newPosition;
        networkVelocity.Value = Vector3.zero;
        transform.position = newPosition;
    }

    // Client-RPC used by the server to force the owning client to teleport its player
    [ClientRpc]
    public void TeleportClientRpc(Vector3 newPosition, ClientRpcParams rpcParams = default)
    {
        // Reset and teleport the CharacterController (as before)
        var cc = _characterController;
        if (cc != null)
        {
            cc.enabled = false;
            transform.position = newPosition;
            cc.enabled = true;
        }

        _velocity = Vector3.zero;
        _clientPosition = newPosition;

        // Re‐enable death & fall detectors (as before)
        var death = GetComponent<PlayerDeathHandler>();
        if (death != null) death.enabled = true;
        var fall = GetComponent<PlayerFallDetector>();
        if (fall != null) fall.ActivateDetection();

        // Re‐enable the input component on the owning client
        if (IsOwner)
        {
            this.enabled = true;
        }
    }

    private void UpdatePlayerVisibility(bool isVisible)
    {
        // Update renderers
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var rend in renderers) rend.enabled = isVisible;

        // Stop animations if invisible
        if (_animator != null && !isVisible)
        {
            _animator.SetBool(IsWalking, false);
            _animator.SetBool(IsRunning, false);
            _animator.SetBool(IsDancing, false);
        }

        // Update colliders (except the CharacterController which is needed for positioning)
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        foreach (var col in colliders)
        {
            if (!(col is CharacterController)) col.enabled = isVisible;
        }
    }

    // Public method to set visibility from other components like PlayerDeathHandler
    public void SetVisibility(bool isVisible)
    {
        // Apply visibility change locally first
        UpdatePlayerVisibility(isVisible);

        // Then sync to network
        if (IsServer)
        {
            networkIsVisible.Value = isVisible;
        }
        else
        {
            SetVisibilityServerRpc(isVisible);
        }
    }

    // Allow any client (not just the owner) to tell the server “hide me”
    [ServerRpc(RequireOwnership = false)]
    private void SetVisibilityServerRpc(bool isVisible)
    {
        networkIsVisible.Value = isVisible;
    }

    private void OnNetworkVisibilityChanged(bool oldVisible, bool newVisible)
    {
        // For non-owners, directly update the visibility
        if (!IsOwner)
        {
            UpdatePlayerVisibility(newVisible);
        }
    }
}