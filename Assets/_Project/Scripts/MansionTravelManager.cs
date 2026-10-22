using System;
using System.Linq;
using UnityEngine;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;

public class MansionTravelManager : MonoBehaviour
{
    public ConnectionManager connectionManager;

    private const string DepartureKey = "mansion_departure";
    private bool publishing;

    private string lastDepartureJson;
    private string travelStartedFor;

    [Serializable]
    public class MansionDeparture
    {
        public string sessionId;
        public string sceneName;
        public bool readyToTravel;
        public string[] playerIds;
        public int[] spawnIndices;
    }

    public async void StartMansion()
    {
        if (publishing)
            return;

        if (connectionManager == null)
        {
            Debug.LogWarning("[Mansion] ConnectionManager is not assigned.");
            return;
        }

        var session = connectionManager.CurrentSession;

        if (session == null)
        {
            Debug.LogWarning("[Mansion] Connect to the SocialHub first.");
            return;
        }

        if (!session.IsHost)
        {
            Debug.LogWarning(
                "[Mansion] Only the hub coordinator can start this departure.");
            return;
        }

        publishing = true;

        try
        {
            // Prevent selecting players already reserved for a departure.
            if (session.Properties.TryGetValue(
                DepartureKey, out var existingProperty) &&
                !string.IsNullOrEmpty(existingProperty.Value))
            {
                var existing = JsonUtility.FromJson<MansionDeparture>(
                    existingProperty.Value);

                if (existing != null && existing.playerIds != null &&
                    session.Players.Any(
                        player => existing.playerIds.Contains(player.Id)))
                {
                    Debug.LogWarning(
                        "[Mansion] A departure is already prepared.");
                    return;
                }
            }

            var selectedIds = session.Players
                .OrderBy(player => player.Joined)
                .ThenBy(player => player.Id, StringComparer.Ordinal)
                .Take(2)
                .Select(player => player.Id)
                .ToArray();

            if (selectedIds.Length < 2)
            {
                Debug.LogWarning(
                    "[Mansion] At least two players are required.");
                return;
            }

            // Matches the 10 spawn markers in the mansion scene.
            int[] shuffledSpawns = Enumerable.Range(0, 10).ToArray();

            // Shuffle the indices so no two selected players get the same marker.
            var random = new System.Random();

            for (int i = shuffledSpawns.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);

                int temporary = shuffledSpawns[i];
                shuffledSpawns[i] = shuffledSpawns[j];
                shuffledSpawns[j] = temporary;
            }

            var departure = new MansionDeparture
            {
                sessionId = Guid.NewGuid().ToString(),
                sceneName = "ZombieMansionLevel01",
                playerIds = selectedIds,
                spawnIndices = shuffledSpawns.Take(selectedIds.Length).ToArray()
            };

            var hostSession = session.AsHost();

            hostSession.SetProperty(
                DepartureKey,
                new SessionProperty(
                    JsonUtility.ToJson(departure),
                    VisibilityPropertyOptions.Member));

            await hostSession.SavePropertiesAsync();

            Debug.Log(
                $"[Mansion] Destination published: {departure.sessionId} | " +
                $"Scene: {departure.sceneName} | " +
                $"Players: {departure.playerIds.Length}");

            await WaitForAcknowledgementsAsync(departure);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            publishing = false;
        }
    }

    private async void Update()
    {

        if (connectionManager == null)
            return;

        var session = connectionManager.CurrentSession;

        if (session == null)
        {
            lastDepartureJson = null;
            return;
        }

        if (!session.Properties.TryGetValue(
            DepartureKey, out var property))
        {
            lastDepartureJson = null;
            return;
        }

        string json = property.Value;

        if (string.IsNullOrEmpty(json) || json == lastDepartureJson)
            return;

        bool firstDepartureSeen = lastDepartureJson == null;
        lastDepartureJson = json;

        try
        {
            var departure =
                JsonUtility.FromJson<MansionDeparture>(json);

            if (departure == null || departure.playerIds == null)
                return;

            // Ignore a confirmed departure already present on connection.
            if (firstDepartureSeen && departure.readyToTravel)
            {
                Debug.Log("[Mansion] Ignoring an existing confirmed departure.");
                return;
            }


            bool selected = departure.playerIds.Contains(
                session.CurrentPlayer.Id);

            Debug.Log(
                $"[Mansion] Destination received: {departure.sessionId} | " +
                $"Scene: {departure.sceneName} | " +
                $"Selected: {selected}");

            if (selected)
            {
                if (departure.readyToTravel)
                {
                    Debug.Log(
                        $"[Mansion] Travel instruction received: " +
                        $"{departure.sessionId}");

                    await FollowDepartureAsync(departure);
                }
                else
                {
                    await AcknowledgeDepartureAsync(departure);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private async Task AcknowledgeDepartureAsync(
    MansionDeparture departure)
    {
        var session = connectionManager.CurrentSession;

        if (session == null)
            return;

        session.CurrentPlayer.SetProperty(
            "mansion_ack",
            new PlayerProperty(
                departure.sessionId,
                VisibilityPropertyOptions.Member));

        await session.SaveCurrentPlayerDataAsync();

        Debug.Log(
            $"[Mansion] Destination acknowledged: {departure.sessionId} | " +
            $"Player: {session.CurrentPlayer.Id}");
    }

    private async Task WaitForAcknowledgementsAsync(
    MansionDeparture departure)
    {
        var session = connectionManager.CurrentSession;

        if (session == null || !session.IsHost)
            return;

        float deadline = Time.realtimeSinceStartup + 25f;

        while (Application.isPlaying)
        {
            if (connectionManager.CurrentSession != session)
                return;

            if (!session.IsHost)
            {
                Debug.LogWarning("[Mansion] The hub coordinator changed.");
                return;
            }

            bool everyoneAcknowledged = departure.playerIds.All(id =>
                session.Players.Any(player =>
                    player.Id == id &&
                    player.Properties.TryGetValue("mansion_ack", out var ack) &&
                    ack.Value == departure.sessionId));

            if (everyoneAcknowledged)
            {
                departure.readyToTravel = true;

                var hostSession = session.AsHost();

                hostSession.SetProperty(
                    DepartureKey,
                    new SessionProperty(
                        JsonUtility.ToJson(departure),
                        VisibilityPropertyOptions.Member));

                await hostSession.SavePropertiesAsync();

                Debug.Log(
                    $"[Mansion] Travel confirmed: {departure.sessionId}");

                return;
            }

            if (Time.realtimeSinceStartup >= deadline)
            {
                Debug.LogWarning(
                    "[Mansion] Timed out waiting for acknowledgements. " +
                    "Everyone stays in the hub.");

                return;
            }

            await Task.Delay(1100);

            if (!Application.isPlaying || this == null ||
                connectionManager.CurrentSession != session)
                return;

            await session.RefreshAsync();
        }
    }

    private async Task FollowDepartureAsync(
    MansionDeparture departure)
    {
        // Prevent processing the same travel instruction twice.
        if (travelStartedFor == departure.sessionId)
            return;

        travelStartedFor = departure.sessionId;

        // Wait for local connection work and destination publishing to finish.
        float deadline = Time.realtimeSinceStartup + 25f;

        while (publishing || connectionManager.IsBusy)
        {
            if (!Application.isPlaying || this == null)
                return;

            if (Time.realtimeSinceStartup >= deadline)
                throw new TimeoutException(
                    "Timed out waiting for the connection to become ready.");

            await Task.Yield();
        }

        var hub = connectionManager.CurrentSession;

        if (hub == null)
            throw new InvalidOperationException("The hub session is missing.");

        if (hub.IsHost)
        {
            // Space this request apart from the acknowledgement refresh.
            await Task.Delay(1500);

            // Confirm the travel signal was successfully saved.
            await hub.RefreshAsync();

            if (!hub.Properties.TryGetValue(
                DepartureKey, out var property))
            {
                throw new InvalidOperationException(
                    "The departure instruction is missing.");
            }

            var savedDeparture =
                JsonUtility.FromJson<MansionDeparture>(property.Value);

            if (savedDeparture == null ||
                !savedDeparture.readyToTravel ||
                savedDeparture.sessionId != departure.sessionId)
            {
                throw new InvalidOperationException(
                    "The departure has not been confirmed.");
            }

            Debug.Log(
                "[Mansion] Coordinator waiting for selected players to leave.");

            deadline = Time.realtimeSinceStartup + 25f;

            while (hub.Players.Any(player =>
                player.Id != hub.CurrentPlayer.Id &&
                departure.playerIds.Contains(player.Id)))
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    throw new TimeoutException(
                        "A selected player has not left. Coordinator stayed in hub.");
                }

                await Task.Delay(1100);

                if (!Application.isPlaying || this == null)
                    return;

                await hub.RefreshAsync();
            }
        }

        int playerIndex = Array.IndexOf(
    departure.playerIds,
    hub.CurrentPlayer.Id);

        if (playerIndex < 0 ||
            departure.spawnIndices == null ||
            playerIndex >= departure.spawnIndices.Length)
        {
            throw new InvalidOperationException(
                "This player has no assigned mansion spawn point.");
        }

        int spawnIndex = departure.spawnIndices[playerIndex];

        Debug.Log($"[Mansion] Assigned spawn index: {spawnIndex}");

        await connectionManager.TravelToMansionAsync(
            departure.sessionId,
            departure.sceneName,
            spawnIndex);
    }

}