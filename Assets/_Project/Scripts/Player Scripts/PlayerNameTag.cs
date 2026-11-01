using TMPro;
using UnityEngine;
using Unity.Netcode;

public class PlayerNameTag : NetworkBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Transform head;
    [SerializeField] private float heightAboveHead = 0.3f;

    public override void OnNetworkSpawn()
    {
        if (nameText == null)
            return;

        nameText.text = $"Player {OwnerClientId}";

        // Hide your own tag.
        nameText.gameObject.SetActive(!IsOwner);
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
        {
            tag.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }
}