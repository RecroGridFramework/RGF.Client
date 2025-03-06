using Recrovit.RecroGridFramework.Abstraction.Models;
using Recrovit.RecroGridFramework.Client.Events;
using Recrovit.RecroGridFramework.Client.Services;

namespace Recrovit.RecroGridFramework.Client.Models;

public class RgfCustomFunctionContext
{
    public string? FunctionName { get; set; }

    public bool RequireQueryParams { get; set; }

    public Dictionary<string, object>? CustomParams { get; set; }

    public RgfEntityKey? EntityKey { get; set; }

    public RgfToastEventArgs? Toast { get; set; }

    public bool EnableProgressTracking { get; set; }

    public Action<IRgfProgressService, IRgfProgressArgs>? ProgressChanged;

    public Func<IRgfProgressService, IRgfProgressArgs, Task>? ProgressChangedAsync;
}