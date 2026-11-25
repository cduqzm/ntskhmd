using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Media;

namespace ValCC;

public enum CrosshairPart
{
    Primary,
    Ads
}

public record OutlineChange(int ProfileIndex, string ProfileName, CrosshairPart Part, Color Color)
{
    public string Description => $"{ProfileName} ({(Part == CrosshairPart.Primary ? "primary" : "ads")})";
}

public class CrosshairProfile
{
    private readonly Color originalPrimaryOutline;
    private readonly Color originalAdsOutline;

    public CrosshairProfile(int index, JsonObject node)
    {
        Index = index;
        Node = node;
        Primary = JsonHelper.Child(node, "primary") as JsonObject;
        Ads = JsonHelper.Child(node, "aDS") as JsonObject;
        originalPrimaryOutline = GetOutline(CrosshairPart.Primary);
        originalAdsOutline = GetOutline(CrosshairPart.Ads);
    }

    public int Index { get; }

    public JsonObject Node { get; }

    public JsonObject? Primary { get; }

    public JsonObject? Ads { get; }

    public string Name
    {
        get
        {
            string name = JsonHelper.Text(Node, "profileName");
            return name.Trim() == "" ? $"profile {Index + 1}" : name;
        }
    }

    public bool UsesPrimaryForAds => JsonHelper.Flag(Node, "bUsePrimaryCrosshairForADS");

    public JsonObject? GetCrosshair(CrosshairPart part)
    {
        return part == CrosshairPart.Primary ? Primary : Ads;
    }

    public Color GetOutline(CrosshairPart part)
    {
        return JsonHelper.ReadColor(GetCrosshair(part), "outlineColor");
    }

    public void SetOutline(CrosshairPart part, Color color)
    {
        JsonHelper.WriteColor(GetCrosshair(part), "outlineColor", color);
    }

    public Color GetOriginalOutline(CrosshairPart part)
    {
        return part == CrosshairPart.Primary ? originalPrimaryOutline : originalAdsOutline;
    }
}

public class CrosshairData
{
    private const string ProfileSettingName = "EAresStringSettingName::SavedCrosshairProfileData";

    private readonly JsonObject? profileSetting;
    private readonly JsonNode? profileData;

    private CrosshairData(JsonObject? profileSetting, JsonNode? profileData, List<CrosshairProfile> profiles)
    {
        this.profileSetting = profileSetting;
        this.profileData = profileData;
        Profiles = profiles;
    }

    public List<CrosshairProfile> Profiles { get; }

    public static CrosshairData Read(JsonNode settings)
    {
        JsonObject? profileSetting = FindProfileSetting(settings);
        string profileJson = JsonHelper.Text(profileSetting, "value");
        if (profileSetting == null || profileJson == "")
        {
            return new CrosshairData(null, null, new List<CrosshairProfile>());
        }

        JsonNode profileData = JsonHelper.Parse(profileJson)
            ?? throw new InvalidDataException("the crosshair profile data in your settings is not valid json.");

        var profiles = new List<CrosshairProfile>();
        if (JsonHelper.Child(profileData, "profiles") is JsonArray profileArray)
        {
            for (int index = 0; index < profileArray.Count; index++)
            {
                if (profileArray[index] is JsonObject profile)
                {
                    profiles.Add(new CrosshairProfile(index, profile));
                }
            }
        }

        return new CrosshairData(profileSetting, profileData, profiles);
    }

    public List<OutlineChange> GetChanges()
    {
        var changes = new List<OutlineChange>();
        foreach (CrosshairProfile profile in Profiles)
        {
            foreach (CrosshairPart part in Enum.GetValues<CrosshairPart>())
            {
                Color color = profile.GetOutline(part);
                if (color != profile.GetOriginalOutline(part))
                {
                    changes.Add(new OutlineChange(profile.Index, profile.Name, part, color));
                }
            }
        }

        return changes;
    }

    public void Apply(List<OutlineChange> changes)
    {
        foreach (OutlineChange change in changes)
        {
            CrosshairProfile profile = FindProfile(change)
                ?? throw new InvalidOperationException($"the profile \"{change.ProfileName}\" changed in game since it was loaded. press refresh and try again.");
            profile.SetOutline(change.Part, change.Color);
        }

        if (profileSetting != null && profileData != null)
        {
            JsonHelper.SetChild(profileSetting, "value", profileData.ToJsonString(JsonHelper.CompactOptions));
        }
    }

    public List<OutlineChange> FindMissingChanges(List<OutlineChange> changes)
    {
        return changes.Where(change => FindProfile(change)?.GetOutline(change.Part) != change.Color).ToList();
    }

    private CrosshairProfile? FindProfile(OutlineChange change)
    {
        CrosshairProfile? profile = Profiles.FirstOrDefault(candidate => candidate.Index == change.ProfileIndex);
        return profile != null && profile.Name == change.ProfileName ? profile : null;
    }

    private static JsonObject? FindProfileSetting(JsonNode settings)
    {
        if (JsonHelper.Child(settings, "stringSettings") is not JsonArray stringSettings)
        {
            return null;
        }

        foreach (JsonNode? entry in stringSettings)
        {
            if (string.Equals(JsonHelper.Text(entry, "settingEnum"), ProfileSettingName, StringComparison.OrdinalIgnoreCase))
            {
                return entry as JsonObject;
            }
        }

        return null;
    }
}
