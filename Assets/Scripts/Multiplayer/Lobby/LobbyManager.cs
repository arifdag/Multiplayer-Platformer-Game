using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }
    public Lobby CurrentLobby { get; private set; }

    private float _heartbeatTimer;
    private CancellationTokenSource _refreshCts;

    private const string RelayJoinCodeKey = "RelayJoinCode";
    private const string GameModeKey = "GameMode";
    private const string InGameKey = "InGame";
    private const string PlayerNameKey = "PlayerName";
    private const string PlayerColorKey = "PlayerColor";

    public Action<List<Lobby>> OnLobbyListChanged;
    public Action<Lobby> OnJoinedLobby;
    public Action<Lobby> OnLobbyUpdated;
    public Action OnLeftLobby;

    private string _playerId;

    async void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Wait for auth
            await Task.Yield();
            while (UGS_AuthManager.Instance == null || string.IsNullOrEmpty(UGS_AuthManager.Instance.PlayerId))
                await Task.Yield();
            _playerId = UGS_AuthManager.Instance.PlayerId;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update() => HandleLobbyHeartbeat();

    private void HandleLobbyHeartbeat()
    {
        if (CurrentLobby == null || CurrentLobby.HostId != _playerId) return;

        _heartbeatTimer -= Time.deltaTime;
        if (_heartbeatTimer <= 0f)
        {
            _heartbeatTimer = 15f;
            LobbyService.Instance.SendHeartbeatPingAsync(CurrentLobby.Id)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted) Debug.LogError($"Heartbeat failed: {t.Exception}");
                });
        }
    }

    public async Task<bool> CreateLobby(string name, int maxPlayers, bool isPrivate, string password, string gameMode)
    {
        try
        {
            // Relay allocation
            var allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
            var relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var options = new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = GetNewPlayer(),
                Data = new Dictionary<string, DataObject>
                {
                    {
                        InGameKey,
                        new DataObject(
                            DataObject.VisibilityOptions.Public,
                            "false",
                            DataObject.IndexOptions.S1
                        )
                    },
                    { RelayJoinCodeKey, new DataObject(DataObject.VisibilityOptions.Member, relayCode) },
                    { GameModeKey, new DataObject(DataObject.VisibilityOptions.Public, gameMode) }
                }
            };

            CurrentLobby = await LobbyService.Instance.CreateLobbyAsync(name, maxPlayers, options);
            Debug.Log($"Created Lobby: {CurrentLobby.LobbyCode}");

            // Assign random color to the host
            await AssignRandomColorToCurrentPlayer();

            // NGO Host start
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));
            NetworkManager.Singleton.StartHost();

            // Init timers & refresh
            _heartbeatTimer = 15f;
            _refreshCts?.Cancel();
            _refreshCts = new CancellationTokenSource();
            _ = PeriodicLobbyRefreshAsync(CurrentLobby.Id, 1.1f, _refreshCts.Token);

            OnJoinedLobby?.Invoke(CurrentLobby);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"CreateLobby failed: {e}");
            CurrentLobby = null;
            return false;
        }
    }

    public async Task QueryLobbies()
    {
        try
        {
            var opts = new QueryLobbiesOptions
            {
                Count = 15,
                Filters = new List<QueryFilter>
                {
                    // Only lobbies with an open slot
                    new QueryFilter(
                        QueryFilter.FieldOptions.AvailableSlots,
                        "0",
                        QueryFilter.OpOptions.GT
                    ),
                    // Only lobbies where InGameKey was indexed to S1 == "false"
                    new QueryFilter(
                        QueryFilter.FieldOptions.S1,
                        "false",
                        QueryFilter.OpOptions.EQ
                    ),
                },
                Order = new List<QueryOrder>
                {
                    new QueryOrder(false, QueryOrder.FieldOptions.Created)
                }
            };

            var resp = await LobbyService.Instance.QueryLobbiesAsync(opts);
            OnLobbyListChanged?.Invoke(resp.Results);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"QueryLobbies failed: {e}");
        }
    }

    public async Task<bool> JoinLobbyByCode(string code)
    {
        try
        {
            CurrentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code.ToUpper(), new JoinLobbyByCodeOptions
            {
                Player = GetNewPlayer()
            });

            // Assign random color to the joining player
            await AssignRandomColorToCurrentPlayer();

            var relayCode = CurrentLobby.Data[RelayJoinCodeKey].Value;
            var allocation = await RelayService.Instance.JoinAllocationAsync(relayCode);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));
            NetworkManager.Singleton.StartClient();

            _heartbeatTimer = 15f;
            _refreshCts?.Cancel();
            _refreshCts = new CancellationTokenSource();
            _ = PeriodicLobbyRefreshAsync(CurrentLobby.Id, 1.1f, _refreshCts.Token);

            OnJoinedLobby?.Invoke(CurrentLobby);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"JoinLobby failed: {e}");
            CurrentLobby = null;
            return false;
        }
    }

    public async Task<bool> JoinLobbyById(string lobbyId)
    {
        try
        {
            CurrentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, new JoinLobbyByIdOptions
            {
                Player = GetNewPlayer()
            });

            // Assign random color to the joining player
            await AssignRandomColorToCurrentPlayer();

            var relayCode = CurrentLobby.Data[RelayJoinCodeKey].Value;
            var allocation = await RelayService.Instance.JoinAllocationAsync(relayCode);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));
            NetworkManager.Singleton.StartClient();

            _heartbeatTimer = 15f;
            _refreshCts?.Cancel();
            _refreshCts = new CancellationTokenSource();
            _ = PeriodicLobbyRefreshAsync(CurrentLobby.Id, 1.1f, _refreshCts.Token);

            OnJoinedLobby?.Invoke(CurrentLobby);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"JoinLobbyById failed: {e}");
            CurrentLobby = null;
            return false;
        }
    }

    public async Task LeaveLobby()
    {
        if (CurrentLobby == null) return;

        // Cancel refresh
        _refreshCts?.Cancel();

        try
        {
            await LobbyService.Instance.RemovePlayerAsync(CurrentLobby.Id, _playerId);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"LeaveLobby error: {e}");
        }
        finally
        {
            CurrentLobby = null;
            NetworkManager.Singleton.Shutdown();
            OnLeftLobby?.Invoke();
        }
    }


    public async void UpdateUGSPlayerColor(string ugsPlayerId, int colorIndex)
    {
        if (CurrentLobby == null) return;

        try
        {
            var opts = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {
                        PlayerColorKey,
                        new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, colorIndex.ToString())
                    }
                }
            };
            CurrentLobby = await LobbyService.Instance.UpdatePlayerAsync(CurrentLobby.Id, ugsPlayerId, opts);
            Debug.Log($"UGS player {ugsPlayerId} color updated to {colorIndex}");
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"UpdateUGSPlayerColor failed: {e}");
        }
    }

    public bool IsColorTakenByOther(ulong askerClientId, int colorIndex)
    {
        return NetworkManager.Singleton.ConnectedClientsList
            .Where(c => c.ClientId != askerClientId)
            .Any(c =>
            {
                var state = c.PlayerObject?.GetComponent<LobbyPlayerState>();
                return state != null && state.ColorIndexNet.Value == colorIndex;
            });
    }
    
    private async Task AssignRandomColorToCurrentPlayer()
    {
        if (CurrentLobby == null) return;

        // Get list of taken colors
        var takenColors = new HashSet<int>();
        foreach (var player in CurrentLobby.Players)
        {
            if (player.Data.TryGetValue(PlayerColorKey, out var value))
            {
                if (int.TryParse(value.Value, out int colorIndex) && colorIndex >= 0)
                {
                    takenColors.Add(colorIndex);
                }
            }
        }

        // Find available colors (assuming 8 colors available: 0-7)
        var availableColors = new List<int>();
        for (int i = 0; i < 8; i++)
        {
            if (!takenColors.Contains(i))
            {
                availableColors.Add(i);
            }
        }

        // Assign random color if available
        if (availableColors.Count > 0)
        {
            int randomColor = availableColors[UnityEngine.Random.Range(0, availableColors.Count)];
            
            try
            {
                var opts = new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        {
                            PlayerColorKey,
                            new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, randomColor.ToString())
                        }
                    }
                };
                CurrentLobby = await LobbyService.Instance.UpdatePlayerAsync(CurrentLobby.Id, _playerId, opts);
                Debug.Log($"Assigned random color {randomColor} to current player");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Failed to assign random color: {e}");
            }
        }
    }


    public bool IsColorTakenInUGS(int colorIndex)
    {
        if (CurrentLobby == null) return false;

        foreach (var player in CurrentLobby.Players)
        {
            if (player.Data.TryGetValue(PlayerColorKey, out var value))
            {
                if (int.TryParse(value.Value, out int playerColor) && playerColor == colorIndex)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private Player GetNewPlayer()
    {
        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                {
                    PlayerNameKey,
                    new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, PersistentPlayerData.Username)
                },
                { PlayerColorKey, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "-1") }
            }
        };
    }

    private async Task PeriodicLobbyRefreshAsync(string lobbyId, float interval, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && CurrentLobby != null && CurrentLobby.Id == lobbyId)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), ct);

                CurrentLobby = await LobbyService.Instance.GetLobbyAsync(lobbyId);
                OnLobbyUpdated?.Invoke(CurrentLobby);

                // Host side: push UGS data into NGO states
                if (NetworkManager.Singleton.IsHost)
                {
                    foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                    {
                        var state = client.PlayerObject?.GetComponent<LobbyPlayerState>();
                        if (state != null)
                        {
                            var ugsEntry =
                                CurrentLobby.Players.FirstOrDefault(p => p.Id == state.UgsPlayerIdNet.Value.ToString());
                            if (ugsEntry != null)
                            {
                                state.PlayerNameNet.Value = ugsEntry.Data[PlayerNameKey].Value;
                                state.ColorIndexNet.Value = int.Parse(ugsEntry.Data[PlayerColorKey].Value);
                            }
                        }
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            /* expected on cancellation */
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning($"Refresh error: {e}");
            if (e.Reason == LobbyExceptionReason.LobbyNotFound)
                await LeaveLobby();
        }
    }

    public void StartGame()
    {
        _ = StartGameAsync();
    }

    private async Task StartGameAsync()
    {
        if (CurrentLobby == null || CurrentLobby.HostId != _playerId) return;
        try
        {
            // Flip InGame flag
            var opts = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { InGameKey, new DataObject(DataObject.VisibilityOptions.Public, "true") }
                }
            };
            CurrentLobby = await LobbyService.Instance.UpdateLobbyAsync(CurrentLobby.Id, opts);

            // Load the scene
            NetworkManager.Singleton.SceneManager
                .LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Error starting game: {e}");
        }
    }
}