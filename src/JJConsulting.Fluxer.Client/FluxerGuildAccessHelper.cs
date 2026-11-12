using JJConsulting.Fluxer.Client.Models;

namespace JJConsulting.Fluxer.Client;

internal static class FluxerGuildAccessHelper
{
    internal static FluxerResult<FluxerGuildAccess> Compute(
        FluxerGuildAccessResponse guild, 
        string userId, 
        HashSet<string> memberRoles)
    {
        var guildId = guild.Id;
        try
        {
            ulong permissions = 0;
            var everyoneFound = false;
            foreach (var role in guild.Roles)
            {
                var id = role.Id;
                if (id == guildId) everyoneFound = true;
                if (id == guildId || memberRoles.Contains(id))
                    permissions |= Mask(role.Permissions);
            }
            if (!everyoneFound) return InvalidResponse();
            var cutoff = guild.MessageHistoryCutoff;
            var result = new Dictionary<string, DateTimeOffset?>();
            foreach (var channel in guild.Channels)
            {
                var effective = permissions;
                if ((permissions & 8) == 0)
                {
                    var overwrites = channel.PermissionOverwrites;
                    Apply(overwrites.Where(o => o.Type == 0 && o.Id == guildId));
                    Apply(overwrites.Where(o => o.Type == 0 && memberRoles.Contains(o.Id)));
                    Apply(overwrites.Where(o => o.Type == 1 && o.Id == userId));
                }
                var administrator = (permissions & 8) != 0;
                if (!administrator && (effective & (1UL << 10)) == 0) continue;
                var history = administrator || (effective & (1UL << 16)) != 0;
                if (!history && cutoff is null) continue;
                var id = channel.Id;
                if (!FluxerAdminClient.IsId(id)) return InvalidResponse();
                result.Add(id, history ? null : cutoff);

                void Apply(IEnumerable<FluxerPermissionOverwrite> entries)
                {
                    ulong allow = 0, deny = 0;
                    foreach (var entry in entries)
                    {
                        allow |= Mask(entry.Allow);
                        deny |= Mask(entry.Deny);
                    }
                    effective = (effective & ~deny) | allow;
                }
            }
            return new FluxerGuildAccess { ReadableChannels = result };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return InvalidResponse();
        }
    }

    private static ulong Mask(string value) =>
        ulong.TryParse(value, out var mask) ? mask : throw new FormatException("Invalid permission mask.");
    private static FluxerError InvalidResponse() => new FluxerLocalError(FluxerFailure.InvalidResponse);
}
