using System.Diagnostics.CodeAnalysis;

using Nefarius.DSharpPlus.Extensions.Hosting;

namespace IgorBot.Core;

/// <summary>
///     This is a terrible pattern but it simplifies certain things...
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
internal class Global
{
    public Global(IServiceProvider serviceProvider)
    {
        ClientService = serviceProvider.GetRequiredService<IDiscordClientService>();
    }

    internal static IDiscordClientService ClientService { get; private set; }
}
