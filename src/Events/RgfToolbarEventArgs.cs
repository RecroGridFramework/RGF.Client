﻿using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Events;

public enum RgfToolbarEventKind
{
    Invalid,
    Refresh,
    ShowFilter,
    Add,
    Edit,
    Read,
    Delete,
    Select,
}

public class RgfToolbarEventArgs : EventArgs
{
    public RgfToolbarEventArgs(RgfToolbarEventKind eventKind, RgfDynamicDictionary? data = null)
    {
        EventKind = eventKind;
        Data = data;
    }

    public RgfToolbarEventKind EventKind { get; }

    public RgfDynamicDictionary? Data { get; }
}