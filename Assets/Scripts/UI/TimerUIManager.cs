using UnityEngine;
using TMPro;

public class TimerUIManager : MonoBehaviour
{
    [Header("Timer Settings")]
    [SerializeField] private float roundDuration = 60f;
    
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private GameObject timerContainer;
    
    private float _currentTime;
    private NetworkGameManager _gameManager;
    private bool _isTimerRunning;
    
    private void Awake()
    {
        Debug.Log("TimerUIManager: Awake");
        
        // Find the game manager
        _gameManager = FindObjectOfType<NetworkGameManager>();
        if (_gameManager == null)
            Debug.LogError("TimerUIManager: Could not find NetworkGameManager!");
        else
            Debug.Log("TimerUIManager: Found NetworkGameManager");
            
        // Hide timer initially
        if (timerContainer != null)
        {
            timerContainer.SetActive(false);
            Debug.Log("TimerUIManager: Timer container hidden initially");
        }
        else
        {
            Debug.LogError("TimerUIManager: Timer container reference is missing!");
        }
        
        if (timerText == null)
            Debug.LogError("TimerUIManager: Timer text reference is missing!");
            
        // Initialize timer
        _currentTime = roundDuration;
    }
    
    private void Start()
    {
        Debug.Log($"TimerUIManager: Start - Current game phase: {_gameManager?.CurrentPhase}");
        
        // Subscribe to game phase changes
        if (_gameManager != null)
        {
            _gameManager.GamePhaseChanged += HandleGamePhaseChanged;
            Debug.Log("TimerUIManager: Subscribed to GamePhaseChanged event");
            
            // Check current phase immediately
            if (_gameManager.CurrentPhase == NetworkGameManager.GamePhase.RoundInProgress)
            {
                Debug.Log("TimerUIManager: Game already in RoundInProgress, activating timer");
                HandleGamePhaseChanged(NetworkGameManager.GamePhase.ItemPlacement, NetworkGameManager.GamePhase.RoundInProgress);
            }
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (_gameManager != null)
        {
            _gameManager.GamePhaseChanged -= HandleGamePhaseChanged;
            Debug.Log("TimerUIManager: Unsubscribed from GamePhaseChanged event");
        }
    }
    
    private void Update()
    {
        if (!_isTimerRunning)
            return;
            
        // Update timer locally
        _currentTime -= Time.deltaTime;
        
        // Check for timer expiration
        if (_currentTime <= 0)
        {
            Debug.Log("TimerUIManager: Timer expired!");
            _currentTime = 0;
            _isTimerRunning = false;
            
            // End the round - only if we're on the host/server
            if (_gameManager != null && _gameManager.IsHost)
            {
                _gameManager.EndRoundDueToTimeout();
            }
        }
        
        UpdateTimerUI();
    }
    
    private void HandleGamePhaseChanged(NetworkGameManager.GamePhase oldPhase, NetworkGameManager.GamePhase newPhase)
    {
        Debug.Log($"TimerUIManager: Game phase changed from {oldPhase} to {newPhase}");
        
        if (newPhase == NetworkGameManager.GamePhase.RoundInProgress)
        {
            Debug.Log("TimerUIManager: Round started, showing timer");
            // Show timer and start it
            ShowTimer();
            ResetTimer();
            StartTimer();
        }
        else
        {
            Debug.Log($"TimerUIManager: Hiding timer for phase {newPhase}");
            // Hide timer for other phases
            HideTimer();
            StopTimer();
        }
    }
    
    private void ShowTimer()
    {
        if (timerContainer != null)
        {
            timerContainer.SetActive(true);
            Debug.Log("TimerUIManager: Timer container shown");
        }
        else
        {
            Debug.LogError("TimerUIManager: Cannot show timer - container is null");
        }
            
        UpdateTimerUI();
    }
    
    private void HideTimer()
    {
        if (timerContainer != null)
        {
            timerContainer.SetActive(false);
            Debug.Log("TimerUIManager: Timer container hidden");
        }
        else
        {
            Debug.LogError("TimerUIManager: Cannot hide timer - container is null");
        }
    }
    
    private void UpdateTimerUI()
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(_currentTime / 60);
            int seconds = Mathf.FloorToInt(_currentTime % 60);
            timerText.text = $"{minutes:00}:{seconds:00}";
        }
        else
        {
            Debug.LogError("TimerUIManager: Cannot update timer - timerText is null");
        }
    }
    
    private void ResetTimer()
    {
        _currentTime = roundDuration;
        Debug.Log($"TimerUIManager: Timer reset to {roundDuration} seconds");
    }
    
    private void StartTimer()
    {
        _isTimerRunning = true;
        Debug.Log("TimerUIManager: Timer started");
    }
    
    private void StopTimer()
    {
        _isTimerRunning = false;
        Debug.Log("TimerUIManager: Timer stopped");
    }
    
    // Public method to set the round duration
    public void SetRoundDuration(float duration)
    {
        Debug.Log($"TimerUIManager: Setting round duration to {duration} seconds");
        roundDuration = duration;
        _currentTime = duration;
    }
} 