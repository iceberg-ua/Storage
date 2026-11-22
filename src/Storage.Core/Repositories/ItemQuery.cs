namespace Storage.Core.Repositories;

/// <summary>What the Find page asks for. Every part that is set narrows the result.</summary>
/// <param name="Text">Matched against name, description and tag names; blank means no filter.</param>
/// <param name="WithinLocationId">This location and everything inside it, at any depth; null is everywhere.</param>
/// <param name="TagIds">Items carrying every one of these tags.</param>
public sealed record ItemQuery(string? Text, int? WithinLocationId, IReadOnlyCollection<int> TagIds);
