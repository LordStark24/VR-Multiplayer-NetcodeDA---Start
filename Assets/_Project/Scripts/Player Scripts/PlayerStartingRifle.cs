using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class PlayerStartingRifle : NetworkBehaviour
{
    [Tooltip("Six rifle prefabs, ordered from level 1 to level 6.")]
    [SerializeField]
    private GameObject[] levelRiflePrefabs =
        new GameObject[6];

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
        var progression = PlayerProgression.Local;

        if (rig == null || rig.rifleHolster == null ||
            progression == null)
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

        rifleSpawned = true;

        int level = Mathf.Clamp(progression.Level, 1, 6);

        if (levelRiflePrefabs == null ||
            levelRiflePrefabs.Length != 6)
        {
            Debug.LogError(
                "[Loadout] Assign six rifle prefab entries.", this);
            return;
        }

        GameObject prefab = levelRiflePrefabs[level - 1];

        if (prefab == null ||
            prefab.GetComponent<NetworkObject>() == null ||
            prefab.GetComponent<RifleWeapon>() == null ||
            prefab.GetComponent<RifleChestHolster>() == null)
        {
            Debug.LogError(
                $"[Loadout] Level {level} needs a rifle prefab with " +
                "NetworkObject, RifleWeapon and RifleChestHolster.",
                this);
            return;
        }

        GameObject instance = Instantiate(
            prefab,
            rig.rifleHolster.position,
            rig.rifleHolster.rotation);

        var networkObject = instance.GetComponent<NetworkObject>();
        networkObject.Spawn();

        if (!networkObject.IsOwner)
        {
            Debug.LogError(
                "[Loadout] Starting rifle did not spawn with local ownership.",
                instance);
            return;
        }

        var chestHolster = instance.GetComponent<RifleChestHolster>();

        chestHolster.AssignStartingPlayer(OwnerClientId);
        chestHolster.PlaceInHolster(rig.rifleHolster);

        Debug.Log(
            $"[Loadout] Starting rifle | Player: {OwnerClientId} | " +
            $"Level: {level} | Prefab: {prefab.name} | " +
            $"Ammo: {instance.GetComponent<RifleWeapon>().CurrentAmmo}",
            this);
    }
}