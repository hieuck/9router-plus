using RouterPlus.Core.Updates;

namespace RouterPlus.Infrastructure.Updates;

public interface IUpdateSignatureVerifier
{
    bool IsAvailable => true;

    bool VerifyManifest(string manifestPath, ReleaseManifest manifest, string expectedPublisher);

    bool VerifyExecutable(string executablePath, string expectedPublisher);
}
