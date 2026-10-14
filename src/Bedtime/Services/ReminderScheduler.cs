using Bedtime.Core;
using Bedtime.Interop;
using Microsoft.UI.Dispatching;

namespace Bedtime.Services;

/// <summary>Runs the <see cref="ReminderEngine"/> on a timer, using wall-clock time and the user's idle time.</summary>
internal sealed class ReminderScheduler
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    private readonly DispatcherQueueTimer _timer;
    private readonly Func<BedtimeSettings> _settings;

    public ReminderScheduler(DispatcherQueue dispatcher, Func<BedtimeSettings> settings)
    {
        _settings = settings;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = Interval;
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Evaluate();
    }

    public ReminderEngine Engine { get; } = new();

    public EngineResult? Last { get; private set; }

    public event EventHandler<EngineResult>? Evaluated;

    public void Start()
    {
        Evaluate();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public EngineResult Evaluate()
    {
        var result = Engine.Evaluate(DateTimeOffset.Now, _settings(), TimeZoneInfo.Local, NativeMethods.GetIdleTime());
        Last = result;
        Evaluated?.Invoke(this, result);
        return result;
    }
}
