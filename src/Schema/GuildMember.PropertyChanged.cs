using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using Serilog;

namespace IgorBot.Schema;

internal sealed partial class GuildMember
{
    public event PropertyChangedEventHandler PropertyChanged;

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public void OnPropertyChanged(string propertyName, object before, object after)
    {
#if DEBUG
        Log.Debug("{Property} changed from {Before} to {After}",
            propertyName, before, after);
#endif

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}