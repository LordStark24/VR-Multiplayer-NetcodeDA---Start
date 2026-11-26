using UnityEngine;
using Unity.Netcode;
using System.Linq;
using Unity.Collections;

[RequireComponent(typeof(NetworkObject))]
public class MansionMatchController : NetworkBehaviour
{
    public enum MatchPhase
    {
        WaitingForPlayers,
        Countdown,
        Playing,
        Finished
    }

    public MatchPhase CurrentPhase => phase.Value;

    [SerializeField, Min(1f)]
    private float matchDurationSeconds = 360f;

    [Header("Ammo Pickup Spots")]
    [SerializeField] private AmmoPickup[] ammoPickupSpots = new AmmoPickup[5];

    // -1 means the spots have not been selected yet.
    // Each bit represents one entry in ammoPickupSpots.
    private NetworkVariable<int> activeAmmoMask = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private NetworkVariable<MatchPhase> phase =
    new NetworkVariable<MatchPhase>(
        MatchPhase.WaitingForPlayers,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private NetworkVariable<double> phaseEndTime =
        new NetworkVariable<double>(
            0d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

    private NetworkVariable<FixedString4096Bytes> finalResults =
    new NetworkVariable<FixedString4096Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // Local copy that survives the scene change for this app run.
    public static string LastMatchResultsJson { get; private set; }
    private const string LastMatchResultsKey = "Gunner.LastMatchResults.v1";

    public static string LoadLastMatchResults()
    {
        if (string.IsNullOrEmpty(LastMatchResultsJson))
        {
            LastMatchResultsJson =
                PlayerPrefs.GetString(LastMatchResultsKey, string.Empty);
        }

        return LastMatchResultsJson;
    }

    public static int GetFinalPlacement(PlayerResult player, PlayerResult[] players)
    {
        return 1 + players.Count(other =>
            other.damageDealt > player.damageDealt ||
            (other.damageDealt == player.damageDealt && other.kills > player.kills));
    }

    [System.Serializable]
    public class MatchResults
    {
        public string matchId;
        public PlayerResult[] players;
    }

    [System.Serializable]
    public class PlayerResult
    {
        public string playerLabel;
        public string unityPlayerId;
        public int damageDealt;
        public int kills;
        public int deaths;
    }

    private ConnectionManager connectionManager;
    private bool finishAcknowledgementsConfirmed;
    private bool returningToHub;



    public float SecondsRemaining
    {
        get
        {
            if (!IsSpawned ||
                (phase.Value != MatchPhase.Countdown &&
                 phase.Value != MatchPhase.Playing))
            {
                return 0f;
            }

            return (float)System.Math.Max(
                0d,
                phaseEndTime.Value - NetworkManager.ServerTime.Time);
        }
    }

    public override void OnNetworkSpawn()
    {

        base.OnNetworkSpawn();

        phase.OnValueChanged += OnPhaseChanged;

        finalResults.OnValueChanged += OnFinalResultsChanged;

        Debug.Log(
            $"[Match] Controller spawned | " +
            $"Object: {NetworkObjectId} | " +
            $"Owner: {OwnerClientId} | " +
            $"Local owner: {IsOwner} | " +
            $"Phase: {phase.Value}");
    }

    [ContextMenu("Start Test Match")]
    private void StartTestMatch()
    {
        if (!Application.isPlaying || !IsSpawned || !IsOwner)
        {
            Debug.LogWarning(
                "[Match] Start the test on the controller's owning player.");
            return;
        }

        if (phase.Value != MatchPhase.WaitingForPlayers)
            return;

        phaseEndTime.Value = NetworkManager.ServerTime.Time + 5d;
        phase.Value = MatchPhase.Countdown;
    }

    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= OnPhaseChanged;
        finalResults.OnValueChanged -= OnFinalResultsChanged;
        base.OnNetworkDespawn();
    }


    private void Update()
    {

        if (!IsSpawned || !IsOwner)
            return;

        if (phase.Value == MatchPhase.Finished)
        {
            CheckFinishAcknowledgements();
            return;
        }

        if (phase.Value == MatchPhase.WaitingForPlayers)
        {
            TryStartWhenEveryoneReady();
            return;
        }

        double now = NetworkManager.ServerTime.Time;

        if (phase.Value == MatchPhase.Countdown &&
            now >= phaseEndTime.Value)
        {
            if (!TryConfigureAmmoPickups())
                return;

            phaseEndTime.Value = now + matchDurationSeconds;
            phase.Value = MatchPhase.Playing;

            Debug.Log("[Match] Playing.");
        }
        else if (phase.Value == MatchPhase.Playing &&
                 now >= phaseEndTime.Value)
        {
            phase.Value = MatchPhase.Finished;

            Debug.Log("[Match] Finished.");
        }

    }

    private async void OnPhaseChanged(
    MatchPhase previous,
    MatchPhase current)
    {
        Debug.Log($"[Match] Phase: {previous} -> {current}");

        if (current != MatchPhase.Finished)
            return;

        try
        {
            if (connectionManager == null)
            {
                connectionManager =
                    UnityEngine.Object.FindFirstObjectByType<ConnectionManager>();
            }

            if (connectionManager == null)
                throw new System.InvalidOperationException(
                    "ConnectionManager was not found.");

            await connectionManager.AcknowledgeMatchFinishedAsync();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private void TryStartWhenEveryoneReady()
    {
        if (connectionManager == null)
        {
            connectionManager =
                UnityEngine.Object.FindFirstObjectByType<ConnectionManager>();
        }

        if (connectionManager == null || connectionManager.IsBusy)
            return;

        var session = connectionManager.CurrentSession;
        var expectedIds = connectionManager.ExpectedMansionPlayerIds;

        if (session == null || expectedIds == null || expectedIds.Length == 0)
            return;

        bool everyoneReady = expectedIds.All(id =>
            session.Players.Any(player =>
                player.Id == id &&
                player.Properties.TryGetValue("mansion_ready", out var ready) &&
                ready.Value == session.Id));

        if (!everyoneReady)
            return;

        if (!TryConfigureAmmoPickups())
            return;

        phaseEndTime.Value =
            NetworkManager.ServerTime.Time + matchDurationSeconds;

        phase.Value = MatchPhase.Playing;
    }

    private void CheckFinishAcknowledgements()
    {
        if (finishAcknowledgementsConfirmed || connectionManager == null)
            return;

        var session = connectionManager.CurrentSession;
        var expectedIds = connectionManager.ExpectedMansionPlayerIds;

        if (session == null || expectedIds == null || expectedIds.Length == 0)
            return;

        bool everyoneAcknowledged = expectedIds.All(id =>
            session.Players.Any(player =>
                player.Id == id &&
                player.Properties.TryGetValue(
                    "match_finished_ack", out var ack) &&
                ack.Value == session.Id));

        if (!everyoneAcknowledged)
            return;

        var results = new MatchResults
        {
            matchId = session.Id,
            players = UnityEngine.Object
        .FindObjectsByType<NetworkPlayerHealth>(FindObjectsSortMode.None)
        .Where(player => player.IsSpawned)
        .OrderBy(player => player.OwnerClientId)
        .Select(player =>
        {
            var nameTag = player.GetComponent<PlayerNameTag>();

            return new PlayerResult
            {
                playerLabel = nameTag != null
                    ? nameTag.DisplayName
                    : $"Player {player.OwnerClientId}",
                unityPlayerId = nameTag != null
                    ? nameTag.UnityPlayerId
                    : string.Empty,
                damageDealt = player.DamageDealt,
                kills = player.Kills,
                deaths = player.Deaths
            };
        })
        .ToArray()
        };

        if (results.players.Length != expectedIds.Length)
            return;
        results.players = results.players
            .OrderByDescending(player => player.damageDealt)
            .ThenByDescending(player => player.kills)
            .ToArray();

        var payload = new FixedString4096Bytes(JsonUtility.ToJson(results));

        finishAcknowledgementsConfirmed = true;
        finalResults.Value = payload;
        Debug.Log(
            "[Match] Everyone acknowledged the finish. Ready to return to SocialHub.");
    }


    private async void OnFinalResultsChanged(
        FixedString4096Bytes previous, FixedString4096Bytes current)
    {
        if (current.Length == 0 || returningToHub)
            return;

        LastMatchResultsJson = current.ToString();
        PlayerPrefs.SetString(LastMatchResultsKey, LastMatchResultsJson);
        PlayerPrefs.Save();
        returningToHub = true;

        Debug.Log($"[Match] Final results saved: {LastMatchResultsJson}");

        var results = JsonUtility.FromJson<MatchResults>(LastMatchResultsJson);

        if (PlayerProgression.Local != null)
        {
            PlayerProgression.Local.AwardCompletedMatch(results);
        }
        else
        {
            Debug.LogError(
                "[Progression] Local progression was not ready; XP was not awarded.");
        }
        // Capture ownership before anyone starts leaving.
        bool leaveLast = IsOwner;

        try
        {
            if (connectionManager == null)
            {
                connectionManager =
                    UnityEngine.Object.FindFirstObjectByType<ConnectionManager>();
            }

            if (connectionManager == null)
            {
                throw new System.InvalidOperationException(
                    "ConnectionManager was not found.");
            }

            var manager = connectionManager;
            var mansionSession = manager.CurrentSession;

            if (mansionSession == null)
            {
                throw new System.InvalidOperationException(
                    "The mansion session is missing.");
            }

            Debug.Log("[Match] Returning to SocialHub.");

            if (leaveLast)
            {
                Debug.Log(
                    "[Match] Controller owner waiting for other players to leave.");

                float deadline = Time.realtimeSinceStartup + 30f;
                string localPlayerId = mansionSession.CurrentPlayer.Id;

                // Keep the controller available until the others leave.
                while (mansionSession.Players.Any(
                    player => player.Id != localPlayerId))
                {
                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        throw new System.TimeoutException(
                            "Timed out waiting for players to leave the mansion.");
                    }

                    await System.Threading.Tasks.Task.Delay(1500);

                    if (!Application.isPlaying || this == null ||
                        manager == null ||
                        manager.CurrentSession != mansionSession)
                    {
                        return;
                    }

                    await mansionSession.RefreshAsync();
                }
            }

            await manager.ReturnToSocialHubAsync();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public bool IsAmmoPickupEnabled(AmmoPickup pickup)
    {
        if (!IsSpawned ||
            phase.Value != MatchPhase.Playing ||
            activeAmmoMask.Value < 0 ||
            ammoPickupSpots == null)
        {
            return false;
        }

        for (int i = 0; i < ammoPickupSpots.Length && i < 5; i++)
        {
            if (ammoPickupSpots[i] == pickup)
                return (activeAmmoMask.Value & (1 << i)) != 0;
        }

        return false;
    }

    private bool TryConfigureAmmoPickups()
    {
        if (!IsOwner)
            return false;

        // Preserve the selection for the entire match,
        // including after controller ownership changes.
        if (activeAmmoMask.Value >= 0)
            return true;

        if (ammoPickupSpots == null ||
            ammoPickupSpots.Length != 5 ||
            ammoPickupSpots.Any(pickup => pickup == null) ||
            ammoPickupSpots.Distinct().Count() != 5)
        {
            Debug.LogError(
                "[Match] Assign five different ammo pickup spots.", this);
            return false;
        }

        // Wait until all scene pickups have spawned.
        if (ammoPickupSpots.Any(pickup => !pickup.IsSpawned))
            return false;

        if (connectionManager == null)
        {
            connectionManager =
                Object.FindFirstObjectByType<ConnectionManager>();
        }

        var expectedIds = connectionManager != null
            ? connectionManager.ExpectedMansionPlayerIds
            : null;

        int playerCount = expectedIds != null && expectedIds.Length > 0
            ? expectedIds.Length
            : Object.FindObjectsByType<NetworkPlayerHealth>(
                    FindObjectsSortMode.None)
                .Count(player => player.IsSpawned);

        if (playerCount == 0)
            return false;

        int activeCount = Mathf.Clamp(
            Mathf.Max(2, (playerCount + 1) / 2), 2, 5);

        int[] indices = { 0, 1, 2, 3, 4 };

        // Shuffle once on the controller owner.
        for (int i = indices.Length - 1; i > 0; i--)
        {
            int other = Random.Range(0, i + 1);
            int temporary = indices[i];
            indices[i] = indices[other];
            indices[other] = temporary;
        }

        int mask = 0;

        for (int i = 0; i < activeCount; i++)
            mask |= 1 << indices[i];

        activeAmmoMask.Value = mask;

        Debug.Log(
            $"[Match] Players: {playerCount} | " +
            $"Active ammo spots: {activeCount}/5", this);

        return true;
    }

}