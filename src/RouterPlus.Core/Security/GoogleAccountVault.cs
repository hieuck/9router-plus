namespace RouterPlus.Core.Security;

/// <summary>
/// Immutable collection of Google login credentials, one record per profile.
/// </summary>
public sealed class GoogleAccountVault
{
    private readonly Dictionary<string, GoogleLoginCredential> _recordsByProfileId;

    public GoogleAccountVault()
        : this(Array.Empty<GoogleLoginCredential>())
    {
    }

    public GoogleAccountVault(IEnumerable<GoogleLoginCredential> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        _recordsByProfileId = records
            .GroupBy(record => record.ProfileId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
    }

    public IReadOnlyList<GoogleLoginCredential> Records => _recordsByProfileId.Values.ToList();

    public GoogleLoginCredential? Find(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId, nameof(profileId));
        return _recordsByProfileId.GetValueOrDefault(profileId.Trim());
    }

    public GoogleAccountVault Upsert(GoogleLoginCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var updated = new Dictionary<string, GoogleLoginCredential>(_recordsByProfileId, StringComparer.Ordinal)
        {
            [credential.ProfileId] = credential
        };

        return new GoogleAccountVault(updated.Values);
    }
}
