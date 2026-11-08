using Atlas.Core.Exchange;
using Atlas.Core.Resources;
using Atlas.Core.Validation;

namespace Atlas.Core.Workspaces;

/// <summary>
/// A saved working environment. Every change goes through a method that checks the rules first,
/// so an instance is valid at all times and a refused change leaves it untouched.
/// </summary>
public sealed class Workspace
{
    private readonly List<Resource> _resources;

    private Workspace(
        Guid id,
        string name,
        string description,
        string notes,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? lastUsedAt,
        IEnumerable<Resource> resources)
    {
        Id = id;
        Name = name;
        Description = description;
        Notes = notes;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        LastUsedAt = lastUsedAt;
        _resources = resources.OrderBy(r => r.SortOrder).ToList();
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public string Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the workspace was last restored, or null if it never was.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    public IReadOnlyList<Resource> Resources => _resources;

    public static Workspace Create(string? name, string? description, DateTimeOffset now)
    {
        ThrowIfInvalid(WorkspaceRules.ValidateDetails(name, description));
        return new Workspace(Guid.NewGuid(), name!.Trim(), description ?? string.Empty, string.Empty, now, now, null, []);
    }

    /// <summary>Rebuilds a workspace from storage. The stored values were validated when they were saved.</summary>
    public static Workspace Restore(
        Guid id,
        string name,
        string description,
        string notes,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? lastUsedAt,
        IEnumerable<Resource> resources) =>
        new(id, name, description, notes, createdAt, updatedAt, lastUsedAt, resources);

    /// <summary>
    /// Creates a new workspace from an imported file. Everything gets new identifiers and today's date, and resources
    /// the plan refused are left out.
    /// </summary>
    public static Workspace Import(ImportPlan plan, DateTimeOffset now)
    {
        ThrowIfInvalid(plan.WorkspaceErrors);

        var resources = plan.Resources
            .Where(r => r.Note != ImportNote.Refused)
            .Select((r, index) => new Resource(
                Guid.NewGuid(),
                r.Kind!.Value,
                r.Source.Name.Trim(),
                r.Source.Target.Trim(),
                index,
                RequiresReview: r.Note != ImportNote.None,
                now,
                now));

        var content = plan.Content;
        return new Workspace(Guid.NewGuid(), content.Name.Trim(), content.Description, content.Notes, now, now, null, resources);
    }

    public void UpdateDetails(string? name, string? description, DateTimeOffset now)
    {
        ThrowIfInvalid(WorkspaceRules.ValidateDetails(name, description));
        Name = name!.Trim();
        Description = description ?? string.Empty;
        UpdatedAt = now;
    }

    public void UpdateNotes(string? notes, DateTimeOffset now)
    {
        ThrowIfInvalid(WorkspaceRules.ValidateNotes(notes));
        Notes = notes ?? string.Empty;
        UpdatedAt = now;
    }

    /// <summary>Records a restoration. This is not an edit, so the modification date does not move.</summary>
    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;

    public Resource AddResource(ResourceKind kind, string? displayName, string? target, DateTimeOffset now)
    {
        if (kind == default)
            throw new ArgumentException("A resource must have a type.", nameof(kind));

        ThrowIfInvalid(ResourceRules.Validate(kind, displayName, target));

        var sortOrder = _resources.Count == 0 ? 0 : _resources.Max(r => r.SortOrder) + 1;
        var resource = new Resource(Guid.NewGuid(), kind, displayName!.Trim(), target!.Trim(), sortOrder, false, now, now);
        _resources.Add(resource);
        UpdatedAt = now;
        return resource;
    }

    public Resource UpdateResource(Guid resourceId, string? displayName, string? target, DateTimeOffset now)
    {
        var index = IndexOfResource(resourceId);
        ThrowIfInvalid(ResourceRules.Validate(_resources[index].Kind, displayName, target));

        var updated = _resources[index] with
        {
            DisplayName = displayName!.Trim(),
            Target = target!.Trim(),
            UpdatedAt = now,
        };
        _resources[index] = updated;
        UpdatedAt = now;
        return updated;
    }

    public Resource ApproveResource(Guid resourceId, DateTimeOffset now)
    {
        var index = IndexOfResource(resourceId);
        var approved = _resources[index] with { RequiresReview = false, UpdatedAt = now };
        _resources[index] = approved;
        UpdatedAt = now;
        return approved;
    }

    /// <summary>
    /// Goes back to a saved version: its details, notes and resources replace the current ones. Resources keep the
    /// approval state they had in that version.
    /// </summary>
    public void RevertTo(DocumentContent version, DateTimeOffset now)
    {
        ThrowIfInvalid(WorkspaceRules.ValidateDetails(version.Name, version.Description));
        ThrowIfInvalid(WorkspaceRules.ValidateNotes(version.Notes));

        var resources = new List<Resource>();
        foreach (var saved in version.Resources)
        {
            // A version holds Atlas's own data; a type it can no longer name is kept out rather than invented.
            if (ResourceKind.TryParse(saved.Kind, out var kind))
                resources.Add(new Resource(Guid.NewGuid(), kind, saved.Name, saved.Target, resources.Count, saved.RequiresReview, now, now));
        }

        Name = version.Name.Trim();
        Description = version.Description;
        Notes = version.Notes;
        _resources.Clear();
        _resources.AddRange(resources);
        UpdatedAt = now;
    }

    public void RemoveResource(Guid resourceId, DateTimeOffset now)
    {
        _resources.RemoveAt(IndexOfResource(resourceId));
        UpdatedAt = now;
    }

    /// <summary>
    /// Copies the details, notes and resources into an independent workspace with new identifiers.
    /// The copy has its own history: it starts today and has never been restored.
    /// </summary>
    public Workspace Duplicate(string? newName, DateTimeOffset now)
    {
        ThrowIfInvalid(WorkspaceRules.ValidateDetails(newName, Description));

        var resources = _resources.Select(r => r with { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now });
        return new Workspace(Guid.NewGuid(), newName!.Trim(), Description, Notes, now, now, null, resources);
    }

    private int IndexOfResource(Guid resourceId)
    {
        var index = _resources.FindIndex(r => r.Id == resourceId);
        return index >= 0 ? index : throw new ResourceNotFoundException(Id, resourceId);
    }

    private static void ThrowIfInvalid(IReadOnlyList<ValidationError> errors)
    {
        if (errors.Count > 0)
            throw new AtlasValidationException(errors);
    }
}
