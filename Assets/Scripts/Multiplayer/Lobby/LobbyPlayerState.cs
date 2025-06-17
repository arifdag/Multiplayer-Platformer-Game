using UnityEngine;
using Unity.Netcode;
using TMPro;
using Unity.Collections;

public class LobbyPlayerState : NetworkBehaviour
{
    public NetworkVariable<FixedString64Bytes> PlayerNameNet =
        new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);



    public NetworkVariable<int> ColorIndexNet = new NetworkVariable<int>(-1);
    public NetworkVariable<FixedString128Bytes> UgsPlayerIdNet = new NetworkVariable<FixedString128Bytes>();

    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private UnityEngine.UI.Image playerColorDisplay;
    public Color[] availableColors = new Color[8];
    
    // Client→server RPC so owners can request their name be set
    [ServerRpc]
    private void SetPlayerNameServerRpc(FixedString64Bytes name, ServerRpcParams rpcParams = default)
    {
        PlayerNameNet.Value = name;
    }

    public override void OnNetworkSpawn()
    {
        // Register callbacks
        PlayerNameNet.OnValueChanged += (_, newVal) => UpdateName(newVal.ToString());
        ColorIndexNet.OnValueChanged += (_, newVal) => UpdateColorDisplay(newVal);

        // Initial UI
        UpdateName(PlayerNameNet.Value.ToString());
        UpdateColorDisplay(ColorIndexNet.Value);

        if (IsOwner)
        {
            // Ask the server to write username into the net-var
            SetPlayerNameServerRpc(new FixedString64Bytes(PersistentPlayerData.Username));
            // Send UGS PlayerId up to the host
            var id = UGS_AuthManager.Instance.PlayerId;
            SetUgsPlayerIdServerRpc(new FixedString128Bytes(id));
            // Tell the UI manager "here I am!"
            if (LobbyUIManager.Instance != null)
                LobbyUIManager.Instance.SetLocalPlayerState(this);
        }
    }

    public override void OnNetworkDespawn()
    {
        PlayerNameNet.OnValueChanged -= (_, newVal) => UpdateName(newVal.ToString());
        ColorIndexNet.OnValueChanged -= (_, newVal) => UpdateColorDisplay(newVal);
    }

    private void UpdateName(string name)
    {
        if (playerNameText != null)
            playerNameText.text = name;
    }

    private void UpdateColorDisplay(int colorIndex)
    {
        if (playerColorDisplay == null) return;

        if (colorIndex >= 0 && colorIndex < availableColors.Length)
        {
            playerColorDisplay.color = availableColors[colorIndex];
            playerColorDisplay.gameObject.SetActive(true);
        }
        else
        {
            playerColorDisplay.gameObject.SetActive(false);
        }
    }

    public void RequestColorChange(int newColorIndex)
    {
        if (!IsOwner) return;
        if (LobbyUIManager.Instance.IsColorAvailable(newColorIndex))
            ChangeColorServerRpc(newColorIndex);
        else
            Debug.Log("Client: Color already taken or invalid.");
    }

    [ServerRpc(RequireOwnership = false)]
    private void ChangeColorServerRpc(int newColorIndex, ServerRpcParams rpcParams = default)
    {
        var senderId = rpcParams.Receive.SenderClientId;
        if (LobbyManager.Instance.IsColorTakenByOther(senderId, newColorIndex))
        {
            Debug.Log($"Server: Client {senderId} tried to pick taken color {newColorIndex}.");
            return;
        }

        // Update across NGO
        ColorIndexNet.Value = newColorIndex;

        // And persist in UGS
        var ugsId = UgsPlayerIdNet.Value.ToString();
        LobbyManager.Instance.UpdateUGSPlayerColor(ugsId, newColorIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetUgsPlayerIdServerRpc(FixedString128Bytes id, ServerRpcParams rpcParams = default)
    {
        UgsPlayerIdNet.Value = id;
    }
}