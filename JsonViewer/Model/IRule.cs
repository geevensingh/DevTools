﻿namespace JsonViewer.Model
{
    using System.Windows.Media;

    public interface IRule
    {
        Brush ForegroundBrush { get; }

        Brush BackgroundBrush { get; }

        double? FontSize { get; }

        int? ExpandChildren { get; }

        string WarningMessage { get; }

        bool Matches(JsonObject obj);
    }
}
