public static class PlayerDisplayName
{
    public static string Resolve(string unityPlayerId, string metaName)
    {
        if (!string.IsNullOrWhiteSpace(metaName))
            return metaName.Trim();

        if (string.IsNullOrWhiteSpace(unityPlayerId))
            return "WCG";

        int start = System.Math.Max(0, unityPlayerId.Length - 6);
        return "WCG-" + unityPlayerId.Substring(start);
    }
}