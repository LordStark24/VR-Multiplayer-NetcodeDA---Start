using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class ConnectionManager : MonoBehaviour
{
    private const string SocialHubSessionId = "80af543c-3012-4b16-a403-a30571739581";

    private bool connectionBusy;
    public int maxPlayer = 10;
    private ISession currentSession;
    public ISession CurrentSession => currentSession;
    public bool IsBusy => connectionBusy;
    public string[] ExpectedMansionPlayerIds { get; private set; }

    public enum ConnectionState { Connecting, Failed, Success, Disconnecting, Disconnected }
    public UnityEvent<ConnectionState> OnConnectionEvent;

    public async void QuickJoinOrCreateSession()
    {
        // Prevent duplicate connection requests.
        if (connectionBusy || currentSession != null)
            return;

        connectionBusy = true;

        try
        {
            OnConnectionEvent?.Invoke(ConnectionState.Connecting);

            bool authenticated = await Authenticate();

            if (!authenticated)
            {
                OnConnectionEvent?.Invoke(ConnectionState.Failed);
                return;
            }

            var options = new SessionOptions
            {
                Name = "SocialHub",
                MaxPlayers = maxPlayer,
                IsPrivate = false
            }.WithDistributedAuthorityNetwork();

            Debug.Log($"[SocialHub-v2] Request | " + $"Target: {SocialHubSessionId} | " + $"Cloud project: {Application.cloudProjectId} | " + $"Player: {AuthenticationService.Instance.PlayerId}");

            var joinedSessionIds = await MultiplayerService.Instance.GetJoinedSessionIdsAsync();

            if (joinedSessionIds.Contains(SocialHubSessionId))
            {
                Debug.Log("[SocialHub] Reconnecting to existing membership.");

                currentSession =
                    await MultiplayerService.Instance.ReconnectToSessionAsync(
                        SocialHubSessionId);
            }
            else
            {
                currentSession =
                    await MultiplayerService.Instance.CreateOrJoinSessionAsync(
                        SocialHubSessionId,
                        options);
            }

            Debug.Log(
                $"[SocialHub-v2] Connected | " +
                $"Target: {SocialHubSessionId} | " +
                $"Actual session: {currentSession.Id} | " +
                $"Player: {AuthenticationService.Instance.PlayerId}");

            OnConnectionEvent?.Invoke(ConnectionState.Success);

            await Task.Delay(2000);

            var network = Unity.Netcode.NetworkManager.Singleton;

            Debug.Log(
                $"[SocialHub-NGO] " +
                $"Listening: {network != null && network.IsListening} | " +
                $"Connected: {network != null && network.IsConnectedClient}");
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            OnConnectionEvent?.Invoke(ConnectionState.Failed);
        }
        finally
        {
            connectionBusy = false;
        }
    }

    public async Task TravelToMansionAsync(
    string mansionSessionId,
    string sceneName,
    int spawnIndex,
    string[] expectedPlayerIds)
    {
        if (connectionBusy || currentSession == null)
        {
            throw new System.InvalidOperationException(
                "The connection is busy or no hub session is active.");
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            throw new System.InvalidOperationException(
                $"Scene '{sceneName}' is missing from the enabled Scene List.");
        }

        if (expectedPlayerIds == null || expectedPlayerIds.Length == 0)
        {
            throw new System.InvalidOperationException(
                "The mansion's expected player list is missing.");
        }

        ExpectedMansionPlayerIds = (string[])expectedPlayerIds.Clone();

        connectionBusy = true;

        try
        {
            Debug.Log("[Mansion] Leaving SocialHub.");

            OnConnectionEvent?.Invoke(ConnectionState.Disconnecting);

            await currentSession.LeaveAsync();
            currentSession = null;

            var network = Unity.Netcode.NetworkManager.Singleton;
            float deadline = Time.realtimeSinceStartup + 15f;

            // Wait for NGO to finish shutting down before connecting again.
            while (network != null &&
                   (network.IsListening || network.ShutdownInProgress))
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    throw new System.TimeoutException(
                        "NGO did not finish shutting down.");
                }

                await Task.Yield();
            }

            OnConnectionEvent?.Invoke(ConnectionState.Connecting);

            Debug.Log($"[Mansion] Loading scene: {sceneName}");

            var loading = SceneManager.LoadSceneAsync(
                sceneName, LoadSceneMode.Single);

            if (loading == null)
            {
                throw new System.InvalidOperationException(
                    $"Could not load scene '{sceneName}'.");
            }

            while (!loading.isDone)
            {
                await Task.Yield();
            }

            if (VRRigReferences.Instance == null)
            {
                throw new System.InvalidOperationException(
                    "The local VR rig did not survive the scene change.");
            }

            var mansionSpawns = UnityEngine.Object.FindFirstObjectByType<MansionSpawnPoints>();

            if (mansionSpawns == null ||
                mansionSpawns.spawnPoints == null ||
                spawnIndex < 0 ||
                spawnIndex >= mansionSpawns.spawnPoints.Length ||
                mansionSpawns.spawnPoints[spawnIndex] == null)
            {
                throw new System.InvalidOperationException(
                    $"Mansion spawn point {spawnIndex} is missing or invalid.");
            }

            Transform spawn = mansionSpawns.spawnPoints[spawnIndex];

            VRRigReferences.Instance.transform.root.SetPositionAndRotation(
                spawn.position,
                spawn.rotation);

            Debug.Log(
                $"[Mansion] Positioned at {spawn.name} | Index: {spawnIndex}");

            var options = new SessionOptions
            {
                Name = sceneName,
                MaxPlayers = 10,
                IsPrivate = false
            }.WithDistributedAuthorityNetwork();

            currentSession =
                await MultiplayerService.Instance.CreateOrJoinSessionAsync(
                    mansionSessionId,
                    options);

            await ReportMansionReadyAsync();

            Debug.Log(
                $"[Mansion] Connected | Scene: {sceneName} | " +
                $"Session: {currentSession.Id}");

            OnConnectionEvent?.Invoke(ConnectionState.Success);
        }
        catch
        {
            OnConnectionEvent?.Invoke(ConnectionState.Failed);
            throw;
        }
        finally
        {
            connectionBusy = false;
        }
    }

    private async Task ReportMansionReadyAsync()
    {
        var session = currentSession;

        if (session == null)
            throw new System.InvalidOperationException(
                "The mansion session is missing.");

        float deadline = Time.realtimeSinceStartup + 25f;

        while (true)
        {
            if (!Application.isPlaying || this == null)
                return;

            if (currentSession != session)
                return;

            var network = Unity.Netcode.NetworkManager.Singleton;

            if (network != null &&
                network.IsConnectedClient &&
                network.LocalClient != null &&
                network.LocalClient.PlayerObject != null &&
                network.LocalClient.PlayerObject.IsSpawned)
            {
                break;
            }

            if (Time.realtimeSinceStartup >= deadline)
            {
                throw new System.TimeoutException(
                    $"Mansion readiness timed out | " +
                    $"Session: {session.Id} | " +
                    $"Listening: {network != null && network.IsListening} | " +
                    $"Connected: {network != null && network.IsConnectedClient} | " +
                    $"Local client exists: {network != null && network.LocalClient != null} | " +
                    $"Player object exists: {network != null && network.LocalClient != null && network.LocalClient.PlayerObject != null} | " +
                    $"Player spawned: {network != null && network.LocalClient != null && network.LocalClient.PlayerObject != null && network.LocalClient.PlayerObject.IsSpawned}");
            }

            await Task.Yield();
        }

        session.CurrentPlayer.SetProperty(
            "mansion_ready",
            new PlayerProperty(
                session.Id,
                VisibilityPropertyOptions.Member));

        await session.SaveCurrentPlayerDataAsync();

        Debug.Log(
            $"[Match] Spawn ready | Player: {session.CurrentPlayer.Id} | " +
            $"Session: {session.Id}");
    }
    public async void Disconnect()
    {
        if (currentSession != null)
        {
            OnConnectionEvent.Invoke(ConnectionState.Disconnecting);

            await currentSession.LeaveAsync();
            currentSession = null;

            OnConnectionEvent.Invoke(ConnectionState.Disconnected);
        }
    }

    public async Task<bool> Authenticate()
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }
}
