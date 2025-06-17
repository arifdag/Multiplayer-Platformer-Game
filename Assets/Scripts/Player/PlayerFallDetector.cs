using UnityEngine;

[RequireComponent(typeof(CharacterController), typeof(PlayerDeathHandler))]
public class PlayerFallDetector : MonoBehaviour
{
    private Camera _mainCamera;
    private CharacterController _cc;
    private PlayerDeathHandler _deathHandler;
    private bool _isDetectionActive = false;

    void Awake()
    {
        _mainCamera = Camera.main;
        _cc = GetComponent<CharacterController>();
        _deathHandler = GetComponent<PlayerDeathHandler>();

        if (_mainCamera == null)
        {
            Debug.LogError("PlayerFallDetector: Main Camera not found! Please tag your main camera.", this);
            enabled = false;
            return;
        }
        if (_cc == null)
        {
            Debug.LogError("PlayerFallDetector: CharacterController not found!", this);
            enabled = false;
            return;
        }
        if (_deathHandler == null)
        {
            Debug.LogError("PlayerFallDetector: PlayerDeathHandler not found!", this);
            enabled = false;
            return;
        }
    }

    void Update()
    {
        if (!_isDetectionActive) return;

        float cameraBottomY;
        if (_mainCamera.orthographic)
        {
            cameraBottomY = _mainCamera.transform.position.y - _mainCamera.orthographicSize;
        }
        else
        {
            // Use viewport to world conversion at the player's depth
            float playerDepth = _mainCamera.WorldToViewportPoint(transform.position).z;
            Vector3 screenBottom = new Vector3(0.5f, 0f, playerDepth);
            cameraBottomY = _mainCamera.ViewportToWorldPoint(screenBottom).y;
        }

        // Compute the world‐space Y of the player's feet
        float playerBottomY = transform.position.y - (_cc.height / 2f) + _cc.center.y;

        if (playerBottomY < cameraBottomY)
        {
            // Trigger the same death flow as hitting a lethal object
            _deathHandler.Die();
            // Stop further detection until the next round
            DeactivateDetection();
        }
    }

    public void ActivateDetection()   => _isDetectionActive = true;
    public void DeactivateDetection() => _isDetectionActive = false;
}
