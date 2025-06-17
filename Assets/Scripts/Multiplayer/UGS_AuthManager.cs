using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using System.Threading.Tasks;

public class UGS_AuthManager : MonoBehaviour
{
    public static UGS_AuthManager Instance { get; private set; }
    public string PlayerId { get; private set; }

    async void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            await InitializeAndSignInAsync();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private async Task InitializeAndSignInAsync()
    {
        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            // Set PlayerId and load your username
            PlayerId = AuthenticationService.Instance.PlayerId;
            Debug.Log($"UGS Signed in. Player ID: {PlayerId}");
            
            PersistentPlayerData.LoadUsername();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error during UGS init/sign-in: {e}");
        }
    }
}