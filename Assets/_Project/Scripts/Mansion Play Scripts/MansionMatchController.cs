using UnityEngine;
using Unity.Netcode;
using System.Linq;

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

    private NetworkVariable<bool> returnToHubReady =
    new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

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
        returnToHubReady.OnValueChanged -= OnReturnToHubReadyChanged;

        base.OnNetworkSpawn();
        phase.OnValueChanged += OnPhaseChanged;

        returnToHubReady.OnValueChanged += OnReturnToHubReadyChanged;

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

        if (session == null || expectedIds == null || expectedIds.Length < 2)
            return;

        bool everyoneReady = expectedIds.All(id =>
            session.Players.Any(player =>
                player.Id == id &&
                player.Properties.TryGetValue("mansion_ready", out var ready) &&
                ready.Value == session.Id));

        if (!everyoneReady)
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

        finishAcknowledgementsConfirmed = true;
        returnToHubReady.Value = true;

        Debug.Log(
            "[Match] Everyone acknowledged the finish. Ready to return to SocialHub.");
    }

    private async void OnReturnToHubReadyChanged(
    bool previous, bool current)
    {
        if (!current || returningToHub)
            return;

        returningToHub = true;

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

}