using UnityEngine;

[CreateAssetMenu(
    fileName = "PlayerProgressionDefinition",
    menuName = "Zombie Mansion/Player Progression Definition")]
public class PlayerProgressionDefinition : ScriptableObject
{
    [Tooltip("Total XP required for levels 1 through 6, in ascending order.")]
    public int[] totalXpRequired = { 0, 2000, 6000, 14000, 30000, 60000 };

    public int GetEarnedLevel(long totalXp)
    {
        if (totalXpRequired == null || totalXpRequired.Length != 6)
            return 1;

        int level = 1;

        for (int i = 1; i < totalXpRequired.Length; i++)
        {
            if (totalXp < totalXpRequired[i])
                break;

            level = i + 1;
        }

        return level;
    }
}