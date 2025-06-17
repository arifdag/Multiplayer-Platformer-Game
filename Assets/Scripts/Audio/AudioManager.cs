using System.Collections;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Music Settings")]
    [SerializeField] private AudioClip[] musicTracks;
    [SerializeField] private AudioSource musicSource;
    [SerializeField] [Range(0f, 1f)] private float musicVolume = 0.5f;
    
    private int _currentTrackIndex = 0;

    private void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Keep across scenes
        }


        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
        }

        // Configure audio source
        musicSource.playOnAwake = false;
        musicSource.loop = false; // Don't loop individual tracks
        musicSource.volume = musicVolume;
    }

    private void Start()
    {
        // Start playing the first track
        PlayCurrentTrack();
    }
    
    private void OnEnable()
    {
        // Subscribe to audio source completion event
        musicSource.loop = false;
        StartCoroutine(MonitorTrackProgress());
    }
    
    private void OnDisable()
    {
        StopAllCoroutines();
    }
    
    // Monitors the current track and plays the next one when it finishes
    private IEnumerator MonitorTrackProgress()
    {
        while (true)
        {
            if (musicSource.isPlaying)
            {
                // Wait until it stops playing (the current clip finishes) then play next track
                yield return new WaitUntil(() => !musicSource.isPlaying);
                PlayNextTrack();
            }
            yield return null;
        }
    }
    
    // Play the track at the current index
    private void PlayCurrentTrack()
    {
        if (musicTracks == null || musicTracks.Length == 0)
        {
            Debug.LogWarning("No music tracks assigned to AudioManager!");
            return;
        }

        AudioClip selectedTrack = musicTracks[_currentTrackIndex];
        
        if (selectedTrack != null)
        {
            musicSource.clip = selectedTrack;
            musicSource.Play();
        }
    }
    
    public void PlayNextTrack()
    {
        if (musicTracks == null || musicTracks.Length == 0) return;
        
        // Move to next track, wrapping around if we reached the end
        _currentTrackIndex = (_currentTrackIndex + 1) % musicTracks.Length;
        PlayCurrentTrack();
    }


    // Play a random music track from the array
    public void PlayRandomMusic()
    {
        if (musicTracks == null || musicTracks.Length == 0)
        {
            Debug.LogWarning("No music tracks assigned to AudioManager!");
            return;
        }
        
        int randomIndex = Random.Range(0, musicTracks.Length);
        _currentTrackIndex = randomIndex;
        
        PlayCurrentTrack();
    }

  
    public void StopMusic()
    {
        if (musicSource.isPlaying)
        {
            musicSource.Stop();
        }
    }
    
    // Set the music volume
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (musicSource != null)
        {
            musicSource.volume = musicVolume;
        }
    }
} 