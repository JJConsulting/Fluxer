namespace JJConsulting.Fluxer.Mcp.Models;

public sealed record DmContacts(IReadOnlyList<DmChannel> Contacts, int Total, bool Truncated);