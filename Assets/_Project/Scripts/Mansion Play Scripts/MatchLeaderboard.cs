using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

public class MatchLeaderboard : MonoBehaviour
{
    [SerializeField] private TMP_Text leaderboardText;

    private float nextRefreshTime;
    private readonly StringBuilder display = new StringBuilder();

    private void OnEnable()
    {
        nextRefreshTime = 0f;
    }

    private void Update()
    {
        if (leaderboardText == null ||
            Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + 0.5f;

        var players = Object.FindObjectsByType<NetworkPlayerHealth>(
                FindObjectsSortMode.None)
            .Where(player => player.IsSpawned)
            .OrderBy(player => player.OwnerClientId);

        display.Clear();
        display.AppendLine("MATCH LEADERBOARD");
        display.AppendLine();
        display.AppendLine(
            "PLAYER<pos=350>DAMAGE<pos=580>KILLS<pos=780>DEATHS");

        foreach (var player in players)
        {
            display.Append("Player ");
            display.Append(player.OwnerClientId);

            display.Append("<pos=350>");
            display.Append(player.DamageDealt);

            display.Append("<pos=580>");
            display.Append(player.Kills);

            display.Append("<pos=780>");
            display.AppendLine(player.Deaths.ToString());
        }

        leaderboardText.text = display.ToString();
    }
}