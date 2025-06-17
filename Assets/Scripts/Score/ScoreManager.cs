using Unity.Netcode;
using System;

public class ScoreManager : NetworkBehaviour
{
    public static ScoreManager Instance { get; private set; }

    public NetworkVariable<int> FinishScore = new NetworkVariable<int>();

    private const string GameModeKey = "GameMode";

    public struct PlayerStars : INetworkSerializable, IEquatable<PlayerStars>
    {
        public ulong clientId;
        public int stars;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref stars);
        }

        public bool Equals(PlayerStars other) =>
            clientId == other.clientId && stars == other.stars;

        public override bool Equals(object obj) =>
            obj is PlayerStars other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(clientId, stars);
    }
    
    public NetworkList<PlayerStars> Scores = new NetworkList<PlayerStars>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (IsServer)
        {
            if (LobbyManager.Instance.CurrentLobby.Data.TryGetValue(GameModeKey, out var entry))
                FinishScore.Value = int.Parse(entry.Value);
            else
                FinishScore.Value = 5;

            foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
                Scores.Add(new PlayerStars { clientId = c.ClientId, stars = 0 });

            // Subscribe to future client joins/leaves
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }
    
    // Called on the server whenever a new client connects
    private void OnClientConnected(ulong clientId)
    {
        Scores.Add(new PlayerStars { clientId = clientId, stars = 0 });
    }

    // Remove a player's row when they disconnect
    private void OnClientDisconnected(ulong clientId)
    {
        for (int i = 0; i < Scores.Count; i++)
        {
            if (Scores[i].clientId == clientId)
            {
                Scores.RemoveAt(i);
                break;
            }
        }
    }


    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void AwardStarServerRpc(ulong playerId, int count = 1)
    {
        for (int i = 0; i < Scores.Count; i++)
        {
            if (Scores[i].clientId == playerId)
            {
                var s = Scores[i];
                s.stars += count;
                Scores[i] = s;
                if (s.stars >= FinishScore.Value)
                {
                    // TODO: end‐game
                }

                break;
            }
        }
    }
}