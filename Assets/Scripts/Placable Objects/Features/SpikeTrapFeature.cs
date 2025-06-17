using System.Collections;
using UnityEngine;

public class SpikeTrapFeature : MonoBehaviour, IPlaceableFeature
{
    [Header("Spike Trap Settings")]
    [Tooltip("Time in seconds between spike rises")]
    public float riseInterval = 2.0f;
    
    [Tooltip("How long the spikes stay up before retracting")]
    public float stayUpDuration = 1.0f;
    
    [Tooltip("How fast the spikes rise and retract")]
    public float movementSpeed = 2.0f;
    
    [Tooltip("The maximum height the spikes will rise to")]
    public float maxHeight = 0.5f;
    
    [Header("References")]
    [Tooltip("The spikes GameObject that will move up and down")]
    public Transform spikesTransform;
    
    // Internal variables
    private Vector3 _retractedPosition;
    private Vector3 _extendedPosition;
    private bool _isActive = false;
    private bool _isMoving = false;
    
    // Audio
    public AudioSource audioSource;
    public AudioClip riseSound;
    public AudioClip retractSound;
    
    void Start()
    {
        if (spikesTransform == null)
        {
            // Try to find a child object that might be the spikes
            foreach (Transform child in transform)
            {
                if (child.name.ToLower().Contains("spike"))
                {
                    spikesTransform = child;
                    break;
                }
            }
            
            // If still null, use this object's transform
            if (spikesTransform == null)
            {
                spikesTransform = transform;
                Debug.LogWarning("SpikeTrapFeature: No spikes transform assigned, using this GameObject");
            }
        }
        
        // Store the initial position as retracted position
        _retractedPosition = spikesTransform.localPosition;
        // Calculate the extended position based on the max height
        _extendedPosition = _retractedPosition + Vector3.up * maxHeight;
        
        StartCoroutine(SpikeCycle());
    }
    
    IEnumerator SpikeCycle()
    {
        while (true)
        {
            // Wait for the interval duration
            yield return new WaitForSeconds(riseInterval);
            
            // Extend spikes
            _isActive = true;
            _isMoving = true;
            
            if (audioSource != null && riseSound != null)
            {
                audioSource.PlayOneShot(riseSound);
            }
            
            float elapsedTime = 0f;
            while (elapsedTime < (maxHeight / movementSpeed))
            {
                spikesTransform.localPosition = Vector3.Lerp(_retractedPosition, _extendedPosition, elapsedTime / (maxHeight / movementSpeed));
                elapsedTime += Time.deltaTime;
                yield return null;
            }
            
            // Ensure spikes are fully extended
            spikesTransform.localPosition = _extendedPosition;
            _isMoving = false;
            
            // Keep spikes extended for the stay duration
            yield return new WaitForSeconds(stayUpDuration);
            
            // Retract spikes
            _isMoving = true;
            
            if (audioSource != null && retractSound != null)
            {
                audioSource.PlayOneShot(retractSound);
            }
            
            elapsedTime = 0f;
            while (elapsedTime < (maxHeight / movementSpeed))
            {
                spikesTransform.localPosition = Vector3.Lerp(_extendedPosition, _retractedPosition, elapsedTime / (maxHeight / movementSpeed));
                elapsedTime += Time.deltaTime;
                yield return null;
            }
            
            // Ensure spikes are fully retracted
            spikesTransform.localPosition = _retractedPosition;
            _isMoving = false;
            _isActive = false;
        }
    }
    
    public void OnPlayerEnter(GameObject player)
    {
        if (_isActive)
        {
            // Only process if spikes are extended or extending
            var net = player.GetComponent<NetworkPlayerController>();
            // Only the local-owner should run Die()
            if (net == null || !net.IsLocalPlayer()) return;
            
            var death = player.GetComponent<PlayerDeathHandler>();
            if (death != null) death.Die();
        }
    }
    
    public void OnPlayerStay(GameObject player)
    {
        // If player is still on the trap when spikes become active
        if (_isActive)
        {
            var net = player.GetComponent<NetworkPlayerController>();
            // Only the local-owner should run Die()
            if (net == null || !net.IsLocalPlayer()) return;
            
            var death = player.GetComponent<PlayerDeathHandler>();
            if (death != null) death.Die();
        }
    }
    
    public void OnPlayerExit(GameObject player)
    {
        // Nothing to do when player exits
    }
    
#if UNITY_EDITOR
    // Visual debugging in the Unity Editor
    private void OnDrawGizmosSelected()
    {
        if (spikesTransform == null) return;
        
        Vector3 startPos = Application.isPlaying ? _retractedPosition : spikesTransform.localPosition;
        Vector3 endPos = startPos + Vector3.up * maxHeight;
        
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.TransformPoint(startPos), transform.TransformPoint(endPos));
        Gizmos.DrawWireSphere(transform.TransformPoint(endPos), 0.1f);
    }
#endif
} 