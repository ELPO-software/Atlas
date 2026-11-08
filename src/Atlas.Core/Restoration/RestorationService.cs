namespace Atlas.Core.Restoration;

/// <summary>
/// Opens the resources of a plan one after the other. A resource that fails never stops the others, and a
/// resource is reported as restored only when the system accepted it. Nothing is ever closed.
/// </summary>
public sealed class RestorationService(IResourceOpener opener, TimeProvider timeProvider)
{
    /// <summary>How long the system has to answer for one resource, such as a network folder that does not respond.</summary>
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(15);

    /// <param name="stop">
    /// Stops before the next resource. The one being opened is finished, so that its result is known and true.
    /// </param>
    public async Task<RestoreReport> RunAsync(RestorePlan plan, IProgress<RestoreProgress>? progress = null, CancellationToken stop = default)
    {
        var startedAt = timeProvider.GetUtcNow();
        var results = new RestoreStepResult[plan.Steps.Count];
        var pendingConfirmations = new List<(int Index, Task<OpenResult> Confirmation)>();

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];
            progress?.Report(new RestoreProgress(index, plan.Steps.Count, step.Resource));

            if (stop.IsCancellationRequested)
            {
                results[index] = Result(step, OpenResult.Skipped(RestoreReason.Cancelled));
                continue;
            }

            if (step.SkipReason is { } reason)
            {
                results[index] = Result(step, OpenResult.Skipped(reason));
                continue;
            }

            var opened = await OpenAsync(step);
            results[index] = Result(step, opened);
            if (opened.Confirmation is { } confirmation)
                pendingConfirmations.Add((index, confirmation));
        }

        // Everything has been asked for. Programs are given their chance to fail at the same time, not one after the other.
        progress?.Report(new RestoreProgress(plan.Steps.Count, plan.Steps.Count, null));
        foreach (var (index, confirmation) in pendingConfirmations)
            results[index] = Result(plan.Steps[index], await ConfirmAsync(confirmation, results[index]));

        return new RestoreReport(plan.WorkspaceId, startedAt, timeProvider.GetUtcNow(), results);
    }

    private async Task<OpenResult> OpenAsync(PlannedStep step)
    {
        try
        {
            // On the thread pool, so that a system call that blocks can be abandoned after the timeout.
            var opening = Task.Run(() => opener.OpenAsync(step.Resource.Kind, step.Resource.Target));
            return await opening.WaitAsync(StepTimeout, timeProvider);
        }
        catch (TimeoutException)
        {
            return OpenResult.Failed(RestoreReason.TimedOut);
        }
        catch (Exception ex)
        {
            return OpenResult.Failed(RestoreReason.SystemRefused, ex.Message);
        }
    }

    private async Task<OpenResult> ConfirmAsync(Task<OpenResult> confirmation, RestoreStepResult launched)
    {
        try
        {
            return await confirmation.WaitAsync(StepTimeout, timeProvider);
        }
        catch (Exception)
        {
            // The program started and nothing showed that it failed.
            return new OpenResult(launched.Status, launched.Reason, launched.SystemMessage);
        }
    }

    private static RestoreStepResult Result(PlannedStep step, OpenResult result) =>
        new(step.Resource, result.Status, result.Reason, result.SystemMessage);
}
