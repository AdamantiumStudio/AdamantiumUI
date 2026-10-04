using Serilog.Core;
using Serilog.Events;

namespace Adamantium.UI.Automation;

internal sealed class LogJournalSink : ILogEventSink
{
    public void Emit(LogEvent logEvent) =>
        ErrorJournal.Add("Log", logEvent.Exception == null
            ? logEvent.RenderMessage()
            : $"{logEvent.RenderMessage()}: {logEvent.Exception.GetType().Name}: {logEvent.Exception.Message}");
}
