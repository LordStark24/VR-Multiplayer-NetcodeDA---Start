using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerNameTag : NetworkBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Transform head;
    [SerializeField] private float heightAboveHead = 0.3f;

    private NetworkVariable<FixedString64Bytes> displayName =
        new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

    public string DisplayName =>
        displayName.Value.Length > 0
            ? displayName.Value.ToString()
            : $"Player {OwnerClientId}";

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        displayName.OnValueChanged += OnDisplayNameChanged;

        if (IsOwner)
        {
            var connection = FindFirstObjectByType<ConnectionManager>();

            if (connection != null &&
                !string.IsNullOrWhiteSpace(connection.LocalDisplayName))
            {
                displayName.Value =
                    new FixedString64Bytes(connection.LocalDisplayName);
            }
        }

        if (nameText != null)
        {
            nameText.text = DisplayName;
            nameText.gameObject.SetActive(!IsOwner);
        }
    }

    public override void OnNetworkDespawn()
    {
        displayName.OnValueChanged -= OnDisplayNameChanged;
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
}