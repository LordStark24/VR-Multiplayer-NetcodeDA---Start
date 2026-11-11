using System.Text;
using TMPro;
using UnityEngine;

public class LastMatchResultsUI : MonoBehaviour
{
    [SerializeField] private TMP_Text resultsText;

    private void OnEnable()
    {
        if (resultsText == null)
            return;

        const string title = "LAST MATCH RESULTS\n\n";
        string json = MansionMatchController.LoadLastMatchResults();

        if (string.IsNullOrEmpty(json))
        {
            resultsText.text = title + "No match completed yet.";
            return;
        }

        var results =
            JsonUtility.FromJson<MansionMatchController.MatchResults>(json);

        if (results == null || results.players == null)
        {
            resultsText.text = title + "Match results unavailable.";
            return;
        }

        var display = new StringBuilder(title);
        display.AppendLine(
            "PLAYER<pos=32%>DAMAGE<pos=58%>KILLS<pos=75%>DEATHS");

        int place = 1;

        for (int i = 0; i < results.players.Length; i++)
        {
            var player = results.players[i];

            if (i > 0)
            {
                var previous = results.players[i - 1];

                if (player.damageDealt != previous.damageDealt ||
                    player.kills != previous.kills)
                {
                    place = i + 1;
                }
            }

            display.AppendLine(
                $"{place}. {player.playerLabel}<pos=32%>{player.damageDealt}" +
                $"<pos=58%>{player.kills}<pos=75%>{player.deaths}");
        }

        resultsText.text = display.ToString();
    }
}