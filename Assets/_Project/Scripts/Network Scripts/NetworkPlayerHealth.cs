using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerHealth : NetworkBehaviour
{
    private const int StartingHealth = 100;

    private NetworkVariable<int> health =
        new NetworkVariable<int>(
            StartingHealth,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

    private NetworkVariable<int> deaths = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public int Deaths => deaths.Value;

    private NetworkVariable<int> damageDealt = new(
    0,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Owner);

    private NetworkVariable<int> kills = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public int DamageDealt => damageDealt.Value;
    public int Kills => kills.Value;

    public int CurrentHealth => health.Value;
    public bool IsAlive => health.Value > 0;

    public override void OnNetworkSpawn()
    {
        health.OnValueChanged += OnHealthChanged;

        Debug.Log(
            $"[Health] Player spawned | Owner: {OwnerClientId} | " +
            $"Health: {CurrentHealth}");
    }

    public override void OnNetworkDespawn()
    {
        health.OnValueChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int previous, int current)
    {
        Debug.Log(
            $"[Health] Player {OwnerClientId}: {previous} -> {current}");

        if (previous > 0 && current == 0)
        {
            Debug.Log($"[Health] Player {OwnerClientId} died.");

            if (IsOwner)
            {
                deaths.Value++;

                Debug.Log(
                    $"[Score] Player {OwnerClientId} | Deaths: {Deaths}");

                MoveToPenaltyRoom();
            }
        }
    }
    [Rpc(SendTo.Owner, RequireOwnership = false)]
    public void RequestWeaponDamageRpc(
    NetworkObjectReference weaponReference,
    RpcParams rpcParams = default)
    {
        if (!IsSpawned || !IsOwner || !IsAlive)
            return;

        var match =
            UnityEngine.Object.FindFirstObjectByType<MansionMatchController>();

        // Only allow combat damage during active gameplay.
        if (match == null ||
            match.CurrentPhase != MansionMatchController.MatchPhase.Playing)
            return;

        if (!weaponReference.TryGet(out NetworkObject weaponObject))
            return;

        ulong shooterId = rpcParams.Receive.SenderClientId;

        // The request must come from the rifle's owner.
        if (weaponObject.OwnerClientId != shooterId)
            return;

        // No self-damage for this prototype.
        if (shooterId == OwnerClientId)
            return;

        var weapon = weaponObject.GetComponent<RifleWeapon>();

        if (weapon == null || weapon.Damage <= 0)
            return;

        int actualDamage = Mathf.Min(CurrentHealth, weapon.Damage);

        health.Value -= actualDamage;

        AwardCombatCreditRpc(
            shooterId,
            actualDamage,
            health.Value == 0);

        Debug.Log(
            $"[Health] Player {OwnerClientId} hit by {shooterId} | " +
            $"Damage: {weapon.Damage} | Health: {CurrentHealth}");
    }

    [Rpc(SendTo.Everyone, RequireOwnership = true)]
    private void AwardCombatCreditRpc(
    ulong shooterId,
    int actualDamage,
    bool killed,
    RpcParams rpcParams = default)
    {
        // Only accept credit reported by this victim's owner.
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (NetworkManager.LocalClientId != shooterId ||
            shooterId == OwnerClientId ||
            actualDamage <= 0 ||
            actualDamage > StartingHealth)
            return;

        var localPlayer = NetworkManager.LocalClient?.PlayerObject;

        if (localPlayer == null)
            return;

        var shooterHealth =
            localPlayer.GetComponent<NetworkPlayerHealth>();

        if (shooterHealth == null ||
            !shooterHealth.IsSpawned ||
            !shooterHealth.IsOwner)
            return;

        shooterHealth.damageDealt.Value += actualDamage;

        if (killed)
        {
            shooterHealth.kills.Value++;
        }

        Debug.Log(
            $"[Score] Player {shooterId} | " +
            $"Damage dealt: {shooterHealth.DamageDealt} | " +
            $"Kills: {shooterHealth.Kills} | " +
            $"Deaths: {shooterHealth.Deaths}");
    }

    [ContextMenu("Test / Take 25 Damage")]
    private void TestTakeDamage()
    {
        if (!Application.isPlaying || !IsSpawned || !IsOwner)
        {
            Debug.LogWarning(
                "[Health] Run this test on your locally owned network player.");
            return;
        }

        health.Value = Mathf.Max(0, health.Value - 25);
    }

    private void MoveToPenaltyRoom()
    {
        var penaltySpawn =
            UnityEngine.Object.FindFirstObjectByType<PenaltyRoomSpawn>();

        if (penaltySpawn == null)
        {
            Debug.LogError("[Health] PenaltyRoomSpawn was not found.");
            return;
        }

        if (VRRigReferences.Instance == null)
        {
            Debug.LogError("[Health] Local VR rig was not found.");
            return;
        }

        VRRigReferences.Instance.transform.root.SetPositionAndRotation(
            penaltySpawn.transform.position,
            penaltySpawn.transform.rotation);

        Debug.Log("[Health] Moved local player to the penalty room.");

        StartCoroutine(RespawnAfterPenalty());
    }

    private IEnumerator RespawnAfterPenalty()
    {
        yield return new WaitForSeconds(15f);

        // The avatar may have despawned while the player was waiting.
        if (!IsSpawned || !IsOwner || health.Value > 0)
            yield break;

        var match =
            UnityEngine.Object.FindFirstObjectByType<MansionMatchController>();

        // Don't respawn if the match has finished.
        if (match == null ||
            match.CurrentPhase != MansionMatchController.MatchPhase.Playing)
        {
            yield break;
        }

        var mansionSpawns =
            UnityEngine.Object.FindFirstObjectByType<MansionSpawnPoints>();

        if (mansionSpawns == null || mansionSpawns.spawnPoints == null)
        {
            Debug.LogError("[Health] Mansion spawn points were not found.");
            yield break;
        }

        var validSpawns = new List<Transform>();

        foreach (var spawn in mansionSpawns.spawnPoints)
        {
            if (spawn != null)
                validSpawns.Add(spawn);
        }

        if (validSpawns.Count == 0 || VRRigReferences.Instance == null)
        {
            Debug.LogError("[Health] No valid spawn point or local VR rig.");
            yield break;
        }

        Transform selectedSpawn =
            validSpawns[Random.Range(0, validSpawns.Count)];

        VRRigReferences.Instance.transform.root.SetPositionAndRotation(
            selectedSpawn.position,
            selectedSpawn.rotation);

        health.Value = StartingHealth;

        Debug.Log(
            $"[Health] Respawned at {selectedSpawn.name} with {health.Value} health.");
    }


}