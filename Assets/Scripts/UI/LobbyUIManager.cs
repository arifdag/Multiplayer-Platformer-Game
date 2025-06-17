using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;

public class LobbyUIManager : MonoBehaviour
{
    public static LobbyUIManager Instance { get; private set; }

    [Header("Panels")] [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject createLobbyPanel;
    [SerializeField] private GameObject joinLobbyPanel;
    [SerializeField] private GameObject inLobbyPanel;

    [Header("Main Menu")] [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private Button createLobbyMenuButton;
    [SerializeField] private Button joinLobbyMenuButton; 

    [Header("Create Lobby Panel")] [SerializeField]
    private TMP_InputField lobbyNameInput;

    [SerializeField] private TMP_Dropdown maxPlayersDropdown;
    [SerializeField] private TMP_Dropdown finishPointDropdown;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button createLobbyButton; 
    [SerializeField] private Button createLobbyBackButton;

    [Header("Join Lobby Panel")] [SerializeField]
    private RectTransform lobbyListContainer;

    [SerializeField] private GameObject lobbyEntryPrefab;
    [SerializeField] private Button refreshLobbiesButton;
    [SerializeField] private Button joinLobbyBackButton; 

    [Header("In Lobby Panel")] [SerializeField]
    private RectTransform playerListContainer;

    [SerializeField] private GameObject playerEntryPrefab;
    [SerializeField] public Button[] colorButtons; // length = 8
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveLobbyButton;

    private Lobby currentLobby;
    private LobbyPlayerState localPlayerState;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // --- Main Menu: username + open-panel buttons ---
        usernameInput.onEndEdit.AddListener(OnUsernameChanged);
        RefreshUsernameDisplay();
        createLobbyMenuButton.onClick.AddListener(ShowCreateLobbyPanel);
        joinLobbyMenuButton.onClick.AddListener(ShowJoinLobbyPanel);

        // --- Create-Lobby panel buttons ---
        createLobbyButton.onClick.AddListener(OnCreateLobbyClicked);
        createLobbyBackButton.onClick.AddListener(ShowMainMenu);

        // --- Join-Lobby panel buttons ---
        refreshLobbiesButton.onClick.AddListener(OnRefreshLobbiesClicked);
        joinLobbyBackButton.onClick.AddListener(ShowMainMenu);

        // --- In-Lobby panel buttons ---
        leaveLobbyButton.onClick.AddListener(OnLeaveLobbyClicked);
        startGameButton.onClick.AddListener(OnStartGameClicked);
        for (int i = 0; i < colorButtons.Length; i++)
        {
            int idx = i;
            colorButtons[i].onClick.AddListener(() => OnColorButtonClicked(idx));
        }

        // Listen for when our Netcode client has connected & spawned its player


        // --- LobbyManager events ---
        LobbyManager.Instance.OnLobbyListChanged += UpdateLobbyListUI;
        LobbyManager.Instance.OnJoinedLobby += UpdateInLobbyUI;
        LobbyManager.Instance.OnLobbyUpdated += UpdateInLobbyUI;
        
        createLobbyButton.interactable = !string.IsNullOrWhiteSpace(PersistentPlayerData.Username);

        // Start on the main menu
        ShowMainMenu();
    }

    void OnDestroy()
    {
        usernameInput.onEndEdit.RemoveListener(OnUsernameChanged);
        LobbyManager.Instance.OnLobbyListChanged -= UpdateLobbyListUI;
        LobbyManager.Instance.OnJoinedLobby -= UpdateInLobbyUI;
        LobbyManager.Instance.OnLobbyUpdated -= UpdateInLobbyUI;
    }

    public void SetLocalPlayerState(LobbyPlayerState state)
    {
        localPlayerState = state;
    }

    #region Panel Switching

    public void ShowMainMenu()
    {
        mainMenuPanel.SetActive(true);
        createLobbyPanel.SetActive(false);
        joinLobbyPanel.SetActive(false);
        inLobbyPanel.SetActive(false);
    }

    public void ShowCreateLobbyPanel()
    {
        mainMenuPanel.SetActive(false);
        createLobbyPanel.SetActive(true);
    }

    public void ShowJoinLobbyPanel()
    {
        mainMenuPanel.SetActive(false);
        joinLobbyPanel.SetActive(true);
        OnRefreshLobbiesClicked();
    }

    public void ShowInLobbyPanel()
    {
        createLobbyPanel.SetActive(false);
        joinLobbyPanel.SetActive(false);
        inLobbyPanel.SetActive(true);
    }

    #endregion

    #region Main Menu: Username

    private void RefreshUsernameDisplay()
    {
        usernameInput.text = PersistentPlayerData.Username;
    }

    private void OnUsernameChanged(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            RefreshUsernameDisplay();
            return;
        }
        createLobbyButton.interactable = !string.IsNullOrWhiteSpace(PersistentPlayerData.Username);
        PersistentPlayerData.SetUsername(newName.Trim());
    }

    #endregion

    #region Create Lobby

    private async void OnCreateLobbyClicked()
    {
        var name = lobbyNameInput.text;
        var max = int.Parse(maxPlayersDropdown.options[maxPlayersDropdown.value].text);
        var finish = finishPointDropdown.options[finishPointDropdown.value].text;
        var pass = passwordInput.text;

        bool success = await LobbyManager.Instance
            .CreateLobby(name, max, !string.IsNullOrEmpty(pass), pass, finish);

        if (success)
        {
            currentLobby = LobbyManager.Instance.CurrentLobby;
            ShowInLobbyPanel();
        }
        else Debug.LogWarning("Create Lobby failed");
    }

    #endregion

    #region Join Lobby

    private void OnRefreshLobbiesClicked()
    {
        LobbyManager.Instance.QueryLobbies();
    }

    private void UpdateLobbyListUI(List<Lobby> list)
    {
        foreach (Transform t in lobbyListContainer)
            Destroy(t.gameObject);

        foreach (var l in list)
        {
            var entry = Instantiate(lobbyEntryPrefab, lobbyListContainer);
            entry.GetComponent<LobbyEntryUI>().Initialize(l,
                onJoin: () => StartCoroutine(JoinAndShow(l.Id))
            );
        }
    }

    private IEnumerator JoinAndShow(string lobbyId)
    {
        var joinTask = LobbyManager.Instance.JoinLobbyById(lobbyId);
        yield return new WaitUntil(() => joinTask.IsCompleted);

        if (joinTask.Result)
        {
            currentLobby = LobbyManager.Instance.CurrentLobby;
            ShowInLobbyPanel();
        }
        else Debug.LogWarning("Join Lobby failed");
    }

    #endregion

    #region In-Lobby

    private void UpdateInLobbyUI(Lobby lobby)
    {
        currentLobby = lobby;

        // Rebuild the player list
        foreach (Transform t in playerListContainer)
            Destroy(t.gameObject);

        foreach (var p in lobby.Players)
        {
            var entry = Instantiate(playerEntryPrefab, playerListContainer);
            entry.GetComponent<PlayerEntryUI>().Initialize(p);
        }

        // Only host sees Start
        startGameButton.gameObject
            .SetActive(lobby.HostId == UGS_AuthManager.Instance.PlayerId);
        
        // Disable taken color buttons —— 
        for (int i = 0; i < colorButtons.Length; i++)
        {
            bool available = IsColorAvailable(i);
            colorButtons[i].interactable = available;
        }
    }

    private void OnColorButtonClicked(int colorIndex)
    {
        if (IsColorAvailable(colorIndex))
            localPlayerState.RequestColorChange(colorIndex);
    }

    // Check both NGO states AND UGS lobby data for color availability
    public bool IsColorAvailable(int colorIndex)
    {
        // Check NGO states first
        foreach (var state in FindObjectsOfType<LobbyPlayerState>())
            if (state.ColorIndexNet.Value == colorIndex)
                return false;

        // Also check UGS lobby data as backup
        if (LobbyManager.Instance.IsColorTakenInUGS(colorIndex))
            return false;

        return true;
    }

    private void OnStartGameClicked()
    {
        LobbyManager.Instance.StartGame();
    }

    private void OnLeaveLobbyClicked()
    {
        LobbyManager.Instance.LeaveLobby();
        ShowMainMenu();
    }

    #endregion

    private LobbyPlayerState FindLocalPlayerState()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return null;

        // Iterate only if we have any connected clients
        foreach (var client in nm.ConnectedClientsList)
        {
            if (client.ClientId == nm.LocalClientId && client.PlayerObject != null)
            {
                return client.PlayerObject.GetComponent<LobbyPlayerState>();
            }
        }

        return null;
    }
}