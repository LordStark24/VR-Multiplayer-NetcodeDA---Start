using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class PlayerStartingRifle : NetworkBehaviour
{
    [SerializeField] private GameObject riflePrefab;

    private MansionMatchController matchController;
    private bool rifleSpawned;

    public override void OnNetworkSpawn()
    {
        rifleSpawned = false;
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner || rifleSpawned)
            return;

        var rig = VRRigReferences.Instance;

        if (rig == null || rig.rifleHolster == null ||
            PlayerProgression.Local == null)
        {
            return;
        }

        if (matchController == null)
        {
            matchController =
                Object.FindFirstObjectByType<MansionMatchController>();
        }

        if (matchController == null ||
            !matchController.IsSpawned ||
            matchController.CurrentPhase !=
                MansionMatchController.MatchPhase.Playing)
        {
            return;
        }

        // Attempt only once, including if configuration is invalid.
        rifleSpawned = true;

        if (riflePrefab == null ||
            riflePrefab.GetComponent<NetworkObject>() == null ||
            riflePrefab.GetComponent<RifleWeapon>() == null ||
            riflePrefab.GetComponent<RifleChestHolster>() == null)
        {
            Debug.LogError(
                "[Loadout] Assign a rifle prefab containing " +
                "NetworkObject, RifleWeapon and RifleChestHolster.",
                this);
            return;
        }

        GameObject instance = Instantiate(
            riflePrefab,
            rig.rifleHolster.position,
            rig.rifleHolster.rotation);

        var networkObject = instance.GetComponent<NetworkObject>();

        // Distributed Authority: this client spawns its own rifle.
        networkObject.Spawn();

        if (!networkObject.IsOwner)
        {
            Debug.LogError(
                "[Loadout] Starting rifle did not spawn with local ownership.",
                instance);
            return;
        }

        instance.GetComponent<RifleChestHolster>()
            .PlaceInHolster(rig.rifleHolster);

        Debug.Log(
            $"[Loadout] Starting rifle spawned | " +
            $"Player: {OwnerClientId} | Ammo: " +
            $"{instance.GetComponent<RifleWeapon>().CurrentAmmo}",
            this);
    }
}