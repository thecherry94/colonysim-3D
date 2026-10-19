namespace ColonySim.Core;

public enum JobStatus { Open, Unreachable, NeedsWayBack }

/// <summary>A queued job with identity, a colonist claim and work progress. Mutated only through <see cref="JobQueue"/>.</summary>
public sealed class Job
{
    internal Job(long id, JobSpec spec) { Id = id; Position = spec.Position; Kind = spec.Kind; Block = spec.Block; }

    public long Id { get; }
    public Int3 Position { get; }
    public JobKind Kind { get; }
    public ushort Block { get; }

    /// <summary>Colonist id holding the job; 0 = free.</summary>
    public int ClaimedBy { get; internal set; }

    /// <summary>Work ticks done so far.</summary>
    public float Progress { get; internal set; }

    public JobStatus Status { get; internal set; }

    /// <summary>The navigation version <see cref="Status"/> was found at.</summary>
    public int StatusVersion { get; internal set; }

    public JobSpec Spec => new(Position, Kind, Block);
}
