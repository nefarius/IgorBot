using IgorBot.Core;

namespace IgorBot.Services;

/// <summary>
///     Validated bot-wide System One connection settings.
/// </summary>
internal sealed class SystemOneSettings
{
    private SystemOneSettings(Uri? baseUri, string model, string? apiToken, TimeSpan timeout)
    {
        BaseUri = baseUri;
        Model = model;
        ApiToken = apiToken;
        Timeout = timeout;
    }

    public Uri? BaseUri { get; }

    public string Model { get; }

    public string? ApiToken { get; }

    public TimeSpan Timeout { get; }

    public bool IsConfigured => BaseUri is not null;

    public static SystemOneSettings Create(SystemOneConfig? config)
    {
        config ??= new SystemOneConfig();
        if (config.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Bot:SystemOne:TimeoutSeconds must be greater than zero.");
        }

        string model = string.IsNullOrWhiteSpace(config.Model) ? "loaded" : config.Model.Trim();
        string? token = string.IsNullOrWhiteSpace(config.ApiToken) ? null : config.ApiToken.Trim();
        TimeSpan timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            return new SystemOneSettings(null, model, token, timeout);
        }

        if (!Uri.TryCreate(config.BaseUrl.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Bot:SystemOne:BaseUrl must be an absolute http or https URI.");
        }

        return new SystemOneSettings(uri, model, token, timeout);
    }
}
