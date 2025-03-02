using Microsoft.AspNetCore.Components;
using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfToastType
{
    Default = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Success = 4
}

public class RgfToastEventArgs : EventArgs
{
    public RgfToastEventArgs(string title, string body, RgfToastType? toastType = null, int? delay = null, string? status = null, IRgfProgressArgs? progressArgs = null)
    {
        this.Title = title;
        this.Status = status;
        this.Body = body;
        this.ToastType = toastType ?? RgfToastEventArgsExtensions.ConvertToRgfToastType(progressArgs?.ProgressType) ?? RgfToastType.Default;
        this.Delay = delay ?? toastType switch
        {
            RgfToastType.Error => 0,
            RgfToastType.Warning => 10000,
            _ => 5000,
        };
        this.TriggeredAt = DateTime.Now;
        this.ProgressArgs = progressArgs;
    }

    public string Title { get; init; }

    public string? Status { get; init; }

    public string Body { get; init; }

    public MarkupString MarkupBody => OnRenderBody(this);

    public MarkupString MarkupHeader => OnRenderHeader(this);

    public RgfToastType ToastType { get; init; }

    public int Delay { get; set; }

    public DateTime TriggeredAt { get; init; }

    public IRgfProgressArgs? ProgressArgs { get; set; }

    public Func<RgfToastEventArgs, MarkupString> OnRenderBody { get; set; } = (toast)
        => new MarkupString(string.IsNullOrEmpty(toast.ProgressArgs?.Message) ? toast.Body : $"{toast.Body}<div class=\"progress-message\">{toast.ProgressArgs.Message}</div>");

    public Func<RgfToastEventArgs, MarkupString> OnRenderHeader { get; set; } = (toast) =>
    {
        var title = string.IsNullOrWhiteSpace(toast.Status) ? toast.Title : string.IsNullOrWhiteSpace(toast.Title) ? toast.Status : $"{toast.Status} - {toast.Title}";
        var progress = string.Empty;
        if (toast.ProgressArgs?.CurrentIteration != null)
        {
            if (toast.ProgressArgs.TotalIterations > 0)
            {
                progress = $" - {toast.ProgressArgs.CurrentIteration} / {toast.ProgressArgs.TotalIterations}";
            }
            else
            {
                progress = $" - {toast.ProgressArgs.CurrentIteration}";
            }
        }
        else if (toast.ProgressArgs?.Percentage != null)
        {
            progress = $" - {toast.ProgressArgs.Percentage}%";
        }
        return new MarkupString(title + progress);
    };

    public static RgfToastEventArgs CreateActionEvent(string? status, string title, string action, RgfToastType toastType = RgfToastType.Default, int? delay = null)
       => new RgfToastEventArgs(title, ActionTemplate(action), toastType, delay, status);

    public static RgfToastEventArgs CreateActionEvent(string? status, string title, string action, string message, RgfToastType toastType = RgfToastType.Default, int? delay = null)
       => new RgfToastEventArgs(title, ActionTemplate(action, message), toastType, delay, status);

    [Obsolete("Use Recreate method instead.")]
    public static RgfToastEventArgs RecreateToastWithStatus(RgfToastEventArgs toast, string status, RgfToastType toastType = RgfToastType.Default, int? delay = null)
        => toast.Recreate(status: status, toastType: toastType, delay: delay);

    [Obsolete("Use Remove method instead.")]
    public static RgfToastEventArgs RemoveToast(RgfToastEventArgs toast) => toast.Remove();

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

public static class RgfToastEventArgsExtensions
{
    public static RgfToastEventArgs Remove(this RgfToastEventArgs toast)
    {
        toast.Delay = -1;
        return toast;
    }

    public static RgfToastEventArgs Recreate(this RgfToastEventArgs toast, string? status = null, RgfToastType? toastType = null, string? title = null, string? body = null, int? delay = null, IRgfProgressArgs? progressArgs = null, RgfProgressType? progressType = null)
    {
        toast.Remove();
        return new RgfToastEventArgs(
            title ?? toast.Title,
            body ?? toast.Body,
            toastType ?? ConvertToRgfToastType(progressType) ?? toast.ToastType,
            delay, status ?? toast.Status,
            progressArgs);
    }

    public static RgfToastEventArgs Recreate(this RgfToastEventArgs toast, RgfToastType toastType, string? status = null, string? title = null, string? body = null, int? delay = null, IRgfProgressArgs? progressArgs = null)
        => toast.Recreate(status, toastType, title, body, delay, progressArgs);

    public static RgfToastEventArgs RecreateAsSuccess(this RgfToastEventArgs toast, string? status = null, string? title = null, string? body = null, int? delay = null, IRgfProgressArgs? progressArgs = null, RgfToastType toastType = RgfToastType.Success)
        => toast.Recreate(status, toastType, title, body, delay, progressArgs);

    public static RgfToastType? ConvertToRgfToastType(RgfProgressType? progressType) =>
        progressType switch
        {
            RgfProgressType.Default => RgfToastType.Default,
            RgfProgressType.Info => RgfToastType.Info,
            RgfProgressType.Warning => RgfToastType.Warning,
            RgfProgressType.Error => RgfToastType.Error,
            RgfProgressType.Success => RgfToastType.Success,
            _ => null
        };
}