namespace Storage.App.ViewModels;

/// <summary>
/// Runs a query once the user stops typing rather than once per keystroke: each
/// <see cref="Queue"/> cancels the previous pending run, so typing "hammer" costs one
/// query rather than six.
/// </summary>
public sealed class SearchDebounce
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _pending;

    public SearchDebounce(TimeSpan? delay = null) =>
        _delay = delay ?? TimeSpan.FromMilliseconds(250);

    public void Queue(Func<Task> run)
    {
        _pending?.Cancel();
        _pending?.Dispose();

        var cts = new CancellationTokenSource();
        _pending = cts;

        _ = WaitThenRun(cts.Token);

        async Task WaitThenRun(CancellationToken token)
        {
            try
            {
                await Task.Delay(_delay, token);
            }
            catch (TaskCanceledException)
            {
                // Superseded by a later keystroke; that one owns the query now.
                return;
            }

            await run();
        }
    }
}
