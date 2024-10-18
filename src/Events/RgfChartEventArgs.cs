namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfChartEventKind
{
    ShowChart
}

public class RgfChartEventArgs : EventArgs
{
    public RgfChartEventArgs(RgfChartEventKind eventKind)
    {
        EventKind = eventKind;
    }

    public RgfChartEventKind EventKind { get; }
}