using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Unity.Netcode;
using Unity.Services.Authentication;

[RequireComponent(typeof(NetworkObject))]
public class PlayerProgression : NetworkBehaviour
{
    [SerializeField]
    private PlayerProgressionDefinition definition;

    private NetworkVariable<int> earnedLevel = new(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    [Serializable]
    private class ProgressionSave
    {
        public long totalXp;
        public List<string> awardedMatchIds = new();
    }

    private ProgressionSave save;
    private string playerId;
    private string saveKey;
    private string savePath;

    public int Level => earnedLevel.Value;
    public long TotalXp => save != null ? save.totalXp : 0L;

    public static PlayerProgression Local { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
            return;

        if (definition == null)
        {
            Debug.LogError(
                "[Progression] Assign the progression definition.", this);
            return;
        }

        var authentication = AuthenticationService.Instance;

        if (!authentication.IsSignedIn)
        {
            Debug.LogError(
                "[Progression] Player must be signed in before spawning.",
                this);
            return;
        }

        playerId = authentication.PlayerId;
        saveKey = $"WCG.Progression.v1.{playerId}";

        string directory = Path.Combine(
            Application.persistentDataPath, "Progression");

        Directory.CreateDirectory(directory);

        savePath = Path.Combine(directory, $"{playerId}.json");

        string json = File.Exists(savePath)
            ? File.ReadAllText(savePath)
            : PlayerPrefs.GetString(saveKey, string.Empty);

        Debug.Log(
            $"[Progression LOAD] File: {savePath} | JSON: {json}",
            this);
        save = string.IsNullOrEmpty(json)
            ? new ProgressionSave()
            : JsonUtility.FromJson<ProgressionSave>(json);

        if (save == null)
        {
            Debug.LogError(
                "[Progression] Could not load saved progression.", this);
            return;
        }

        if (save.awardedMatchIds == null)
            save.awardedMatchIds = new List<string>();

        save.totalXp = Math.Max(0L, save.totalXp);
        earnedLevel.Value = definition.GetEarnedLevel(TotalXp);

        Local = this;

        Debug.Log(
            $"[Progression] Player: {playerId} | " +
            $"XP: {TotalXp} | Level: {Level}", this);
    }

    public void AwardCompletedMatch(
        MansionMatchController.MatchResults results)
    {
        if (!IsSpawned || !IsOwner || save == null ||
            results == null || results.players == null ||
            string.IsNullOrWhiteSpace(results.matchId))
        {
            return;
        }

        if (save.awardedMatchIds.Contains(results.matchId))
        {
            Debug.Log(
                $"[Progression] Match already awarded: {results.matchId}",
                this);
            return;
        }

        MansionMatchController.PlayerResult localResult = null;

        foreach (var result in results.players)
        {
            if (result != null && result.unityPlayerId == playerId)
            {
                localResult = result;
                break;
            }
        }

        if (localResult == null)
        {
            Debug.LogError(
                "[Progression] No match result matched the local player ID.",
                this);
            return;
        }

        long earnedXp = Math.Max(0, localResult.damageDealt);
        int previousLevel = Level;

        save.totalXp += Math.Min(earnedXp, long.MaxValue - save.totalXp);
        save.awardedMatchIds.Add(results.matchId);

        // Save the total and duplicate-award protection together.
        string savedJson = JsonUtility.ToJson(save);

        File.WriteAllText(savePath, savedJson);

        Debug.Log(
            $"[Progression SAVE] File: {savePath} | " +
            $"JSON: {File.ReadAllText(savePath)}",
            this);

        earnedLevel.Value = definition.GetEarnedLevel(TotalXp);

        Debug.Log(
            $"[Progression] Earned: {earnedXp} XP | " +
            $"Total: {TotalXp} | Level: {previousLevel} -> {Level}",
            this);
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this)
            Local = null;

        base.OnNetworkDespawn();
    }
}