using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Unity.Services.Authentication;

public class PlayerNameTag : NetworkBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Transform head;
    [SerializeField] private float heightAboveHead = 0.3f;
    [SerializeField] private string socialHubSceneName = "Start Scene";
    [SerializeField] private GameObject goldTrophy;
    [SerializeField] private GameObject silverTrophy;
    [SerializeField] private GameObject bronzeTrophy;

    private NetworkVariable<int> lastMatchPlace = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private NetworkVariable<FixedString64Bytes> displayName =
        new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

    public string DisplayName =>
        displayName.Value.Length > 0
            ? displayName.Value.ToString()
            : $"Player {OwnerClientId}";

    private NetworkVariable<FixedString64Bytes> unityPlayerId =
    new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public string UnityPlayerId => unityPlayerId.Value.ToString();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        displayName.OnValueChanged += OnDisplayNameChanged;

        if (IsOwner)
        {
            var authentication = AuthenticationService.Instance;

            if (authentication.IsSignedIn &&
                !string.IsNullOrWhiteSpace(authentication.PlayerId))
            {
                unityPlayerId.Value = new FixedString64Bytes(authentication.PlayerId);
            }
            var connection = FindFirstObjectByType<ConnectionManager>();

            if (connection != null &&
                !string.IsNullOrWhiteSpace(connection.LocalDisplayName))
            {
                displayName.Value =
                    new FixedString64Bytes(connection.LocalDisplayName);
            }
        }

        lastMatchPlace.OnValueChanged += OnLastMatchPlaceChanged;

        if (IsOwner)
            lastMatchPlace.Value = GetLocalLastMatchPlace();

        RefreshTrophy();

        if (nameText != null)
        {
            nameText.text = DisplayName;
            nameText.gameObject.SetActive(!IsOwner);
        }
    }

    public override void OnNetworkDespawn()
    {
        displayName.OnValueChanged -= OnDisplayNameChanged;
        lastMatchPlace.OnValueChanged -= OnLastMatchPlaceChanged;
        base.OnNetworkDespawn();
    }

    private void OnDisplayNameChanged(
        FixedString64Bytes previous, FixedString64Bytes current)
    {
        if (nameText != null)
            nameText.text = DisplayName;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || IsOwner ||
            nameText == null || head == null ||
            VRRigReferences.Instance == null ||
            VRRigReferences.Instance.head == null)
        {
            return;
        }

        Transform tag = nameText.transform;
        Transform viewer = VRRigReferences.Instance.head;

        tag.position = head.position + Vector3.up * heightAboveHead;

        Vector3 direction = tag.position - viewer.position;

        if (direction.sqrMagnitude > 0.001f)
            tag.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private bool IsInSocialHub() =>
    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        == socialHubSceneName;

    private int GetLocalLastMatchPlace()
    {
        if (!IsInSocialHub() || string.IsNullOrEmpty(UnityPlayerId))
            return 0;

        string json = MansionMatchController.LoadLastMatchResults();
        if (string.IsNullOrEmpty(json))
            return 0;

        var results =
            JsonUtility.FromJson<MansionMatchController.MatchResults>(json);

        if (results?.players == null)
            return 0;

        foreach (var player in results.players)
        {
            if (player != null && player.unityPlayerId == UnityPlayerId)
                return MansionMatchController.GetFinalPlacement(
                    player, results.players);
        }

        return 0;
    }

    private void OnLastMatchPlaceChanged(int previous, int current)
    {
        RefreshTrophy();
    }

    private void RefreshTrophy()
    {
        int place = IsInSocialHub() ? lastMatchPlace.Value : 0;

        if (goldTrophy != null) goldTrophy.SetActive(place == 1);
        if (silverTrophy != null) silverTrophy.SetActive(place == 2);
        if (bronzeTrophy != null) bronzeTrophy.SetActive(place == 3);
    }
}