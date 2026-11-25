using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ValCC;

public class RiotApiException : Exception
{
    public RiotApiException(string message) : base(message)
    {
    }
}

public class RiotApi
{
    private const string SettingsType = "Ares.PlayerSettings";
    private const string UserAgent = "ShooterGame/13 Windows/10.0.19045.1.256.64bit";
    private const string ClientPlatform = "ew0KCSJwbGF0Zm9ybVR5cGUiOiAiUEMiLA0KCSJwbGF0Zm9ybU9TIjogIldpbmRvd3MiLA0KCSJwbGF0Zm9ybU9TVmVyc2lvbiI6ICIxMC4wLjE5MDQyLjEuMjU2LjY0Yml0IiwNCgkicGxhdGZvcm1DaGlwc2V0IjogIlVua25vd24iDQp9";
    private const string VersionUrl = "https://valorant-api.com/v1/version";
    private const string VersionLogMarker = "CI server version:";

    private static readonly string[] PreferenceHosts =
    {
        "https://player-preferences-euc1.pp.sgp.pvp.net",
        "https://player-preferences-usw2.pp.sgp.pvp.net"
    };

    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string LockfilePath = Path.Combine(LocalAppData, "Riot Games", "Riot Client", "Config", "lockfile");
    private static readonly string GameLogPath = Path.Combine(LocalAppData, "VALORANT", "Saved", "Logs", "ShooterGame.log");

    private readonly HttpClient localClient;
    private readonly HttpClient remoteClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private string accessToken = "";
    private string entitlementToken = "";
    private string clientVersion = "";
    private string? workingHost;

    public RiotApi()
    {
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = IsTrustedCertificate };
        localClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task ConnectAsync()
    {
        (string port, string password) = ReadLockfile();
        await LoadTokensAsync(port, password);
        if (clientVersion == "")
        {
            clientVersion = await FindClientVersionAsync();
        }
    }

    public async Task<JsonNode> FetchSettingsAsync()
    {
        var failures = new List<string>();
        foreach (string host in PreferenceHosts.OrderBy(host => host == workingHost ? 0 : 1))
        {
            using HttpRequestMessage request = CreateGameRequest(HttpMethod.Get, $"{host}/playerPref/v3/getPreference/{SettingsType}");
            (int status, string body) = await SendAsync(remoteClient, request);
            if (IsSuccess(status))
            {
                workingHost = host;
                return DecodeSettings(body);
            }

            failures.Add(DescribeFailure(host, status, body));
        }

        throw new RiotApiException("could not load your settings. " + string.Join(" / ", failures));
    }

    public async Task SaveSettingsAsync(JsonNode settings)
    {
        string host = workingHost ?? PreferenceHosts[0];
        var payload = new JsonObject
        {
            ["type"] = SettingsType,
            ["data"] = Convert.ToBase64String(Deflate(settings.ToJsonString(JsonHelper.CompactOptions)))
        };

        using HttpRequestMessage request = CreateGameRequest(HttpMethod.Put, $"{host}/playerPref/v3/savePreference");
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        (int status, string body) = await SendAsync(remoteClient, request);
        if (!IsSuccess(status))
        {
            throw new RiotApiException("the server refused the save. " + DescribeFailure(host, status, body));
        }
    }

    private static (string Port, string Password) ReadLockfile()
    {
        string content;
        try
        {
            using var stream = new FileStream(LockfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            content = reader.ReadToEnd();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new RiotApiException("the riot client is not running. start it, log in, then press refresh.");
        }
        catch (IOException)
        {
            throw new RiotApiException("could not read the riot client lockfile. press refresh to try again.");
        }

        string[] parts = content.Trim().Split(':');
        if (parts.Length < 5)
        {
            throw new RiotApiException("the riot client lockfile looks broken. restart the riot client.");
        }

        return (parts[^3], parts[^2]);
    }

    private async Task LoadTokensAsync(string port, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{port}/entitlements/v1/token");
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        (int status, string body) = await SendAsync(localClient, request);
        if (status == 0)
        {
            throw new RiotApiException("the riot client is not answering. restart it, then press refresh.");
        }

        if (!IsSuccess(status))
        {
            throw new RiotApiException($"the riot client refused the login ({status}). make sure you are logged in, then press refresh.");
        }

        JsonNode? response = JsonHelper.Parse(body);
        accessToken = JsonHelper.Text(response, "accessToken");
        entitlementToken = JsonHelper.Text(response, "token");
        if (accessToken == "" || entitlementToken == "")
        {
            throw new RiotApiException("the riot client did not hand out a login token. make sure you are logged in.");
        }
    }

    private async Task<string> FindClientVersionAsync()
    {
        string? version = await TryGetVersionFromApiAsync() ?? ReadVersionFromGameLog();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new RiotApiException("could not find the valorant client version. valorant-api.com is unreachable and no game log was found.");
        }

        return version;
    }

    private async Task<string?> TryGetVersionFromApiAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, VersionUrl);
        (int status, string body) = await SendAsync(remoteClient, request);
        if (!IsSuccess(status))
        {
            return null;
        }

        string version = JsonHelper.Text(JsonHelper.Child(JsonHelper.Parse(body), "data"), "riotClientVersion");
        return version == "" ? null : version;
    }

    private static string? ReadVersionFromGameLog()
    {
        try
        {
            using var stream = new FileStream(GameLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                int markerIndex = line.IndexOf(VersionLogMarker, StringComparison.Ordinal);
                if (markerIndex >= 0)
                {
                    return line[(markerIndex + VersionLogMarker.Length)..].Trim();
                }
            }
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }

    private HttpRequestMessage CreateGameRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
        request.Headers.TryAddWithoutValidation("X-Riot-Entitlements-JWT", entitlementToken);
        request.Headers.TryAddWithoutValidation("X-Riot-ClientVersion", clientVersion);
        request.Headers.TryAddWithoutValidation("X-Riot-ClientPlatform", ClientPlatform);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return request;
    }

    private static JsonNode DecodeSettings(string responseBody)
    {
        string data = JsonHelper.Text(JsonHelper.Parse(responseBody), "data");
        if (data == "")
        {
            throw new RiotApiException("the server has no saved settings for this account yet.");
        }

        try
        {
            string json = Inflate(Convert.FromBase64String(data));
            return JsonNode.Parse(json) ?? throw new RiotApiException("the server sent empty settings.");
        }
        catch (Exception error) when (error is FormatException or InvalidDataException or JsonException)
        {
            throw new RiotApiException("could not read the settings from the server: " + error.Message);
        }
    }

    private static string Inflate(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] Deflate(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
        {
            deflate.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    private static async Task<(int Status, string Body)> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            return ((int)response.StatusCode, body);
        }
        catch (HttpRequestException error)
        {
            return (0, error.Message);
        }
        catch (TaskCanceledException)
        {
            return (0, "timed out");
        }
    }

    private static bool IsSuccess(int status)
    {
        return status is >= 200 and <= 299;
    }

    private static string DescribeFailure(string host, int status, string body)
    {
        string region = new Uri(host).Host.Split('.')[0].Replace("player-preferences-", "");
        string detail = body.ReplaceLineEndings(" ").Trim();
        if (detail.Length > 60)
        {
            detail = detail[..60] + "…";
        }

        return status == 0 ? $"{region}: {detail}" : $"{region}: {status} {detail}";
    }

    private static bool IsTrustedCertificate(HttpRequestMessage request, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        return errors == SslPolicyErrors.None || request.RequestUri?.Host == "127.0.0.1";
    }
}
