using TMPro;
using UnityEngine;

public class MansionMatchClockUI : MonoBehaviour
{
    [SerializeField]
    private MansionMatchController matchController;

    [SerializeField]
    private TMP_Text clockText;

    private void Update()
    {
        if (matchController == null || clockText == null)
            return;

        if (!matchController.IsSpawned)
        {
            clockText.text = "WAITING";
            return;
        }

        switch (matchController.CurrentPhase)
        {
            case MansionMatchController.MatchPhase.WaitingForPlayers:
                clockText.text = "WAITING";
                break;

            case MansionMatchController.MatchPhase.Countdown:
                clockText.text =
                    $"STARTS IN {Mathf.CeilToInt(matchController.SecondsRemaining)}";
                break;

            case MansionMatchController.MatchPhase.Playing:
                int seconds =
                    Mathf.CeilToInt(matchController.SecondsRemaining);

                clockText.text = $"{seconds / 60:00}:{seconds % 60:00}";
                break;

            case MansionMatchController.MatchPhase.Finished:
                clockText.text = "MATCH OVER";
                break;
        }
    }
}