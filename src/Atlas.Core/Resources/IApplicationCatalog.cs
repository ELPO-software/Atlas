namespace Atlas.Core.Resources;

/// <param name="Target">What to store as the target of an application resource to open this application.</param>
public sealed record InstalledApplication(string Name, string Target);

/// <summary>Lists the applications installed on this computer, as the system's application list shows them.</summary>
public interface IApplicationCatalog
{
    Task<IReadOnlyList<InstalledApplication>> ListAsync(CancellationToken cancellationToken = default);
}
