namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfToastType
{
    Default = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Success = 4
}

public class RgfToastEvent : EventArgs
{
    public RgfToastEvent(string title, string body, RgfToastType toastType = RgfToastType.Default, int? delay = null, string? status = null)
    {
        this.Title = title;
        this.Status = status;
        this.Body = body;
        this.ToastType = toastType;
        this.Delay = delay ?? toastType switch
        {
            RgfToastType.Error => 0,
            RgfToastType.Warning => 10000,
            _ => 5000,
        };
        this.TriggeredAt = DateTime.Now;
    }

    public string Title { get; init; }

    public string? Status { get; init; }

    public string Body { get; init; }

    public RgfToastType ToastType { get; init; }

    public int Delay { get; set; }

    public DateTime TriggeredAt { get; init; }

    public static RgfToastEvent CreateActionEvent(string status, string title, string action, RgfToastType toastType = RgfToastType.Default, int? delay = null)
       => new RgfToastEvent(title, ActionTemplate(action), toastType, delay, status);

    public static RgfToastEvent CreateActionEvent(string status, string title, string action, string message, RgfToastType toastType = RgfToastType.Default, int? delay = null)
       => new RgfToastEvent(title, ActionTemplate(action, message), toastType, delay, status);

    public static RgfToastEvent RecreateToastWithStatus(RgfToastEvent toast, string status, RgfToastType toastType = RgfToastType.Default, int? delay = null)
    {
        toast.Delay = -1;
        return new RgfToastEvent(toast.Title, toast.Body, toastType, delay, status);
    }

    public static RgfToastEvent RemoveToast(RgfToastEvent toast)
    {
        toast.Delay = -1;
        return toast;
    }

    public static string ActionTemplate(string action, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return $"<div><strong>{action}</strong></div>";
        }
        return $"<div><strong>{action}:</strong>&#32;<span>{message}</span></div>";
    }

    public static readonly string NotificationManagerScope = "RgfToastManager";
}