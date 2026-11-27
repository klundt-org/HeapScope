// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/klundt-org/HeapScope
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, version 3 only, with additional terms
// under Section 7 of that license.
//
// Additional terms, including attribution requirements, apply.
// See the ADDITIONAL-TERMS.md file distributed with this program.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program (see the LICENSE file). If not, see
// <https://www.gnu.org/licenses/>.
//
// Commercial licenses are available. See COMMERCIAL.md.

using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using HeapScope.Contracts;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.Controls;

/// <summary>
/// A tree's chart as a sunburst, from the same segments as <see cref="IcicleChart"/> (ADR-046): the focus as the center disc and each
/// level a ring around it, each arc's angle its share. A click selects an arc (and the path to it is highlighted); a double-click runs
/// <see cref="FocusCommand"/> with it. The rings carry no words; hovering names an arc, and the center names the focus. The geometry
/// is <see cref="SunburstGeometry"/>'s.
/// </summary>
public sealed class SunburstChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<IcicleSegmentDto>?> SegmentsProperty =
        AvaloniaProperty.Register<SunburstChart, IReadOnlyList<IcicleSegmentDto>?>(nameof(Segments));

    public static readonly StyledProperty<long> TotalProperty =
        AvaloniaProperty.Register<SunburstChart, long>(nameof(Total));

    public static readonly StyledProperty<IcicleSegmentDto?> SelectedSegmentProperty =
        AvaloniaProperty.Register<SunburstChart, IcicleSegmentDto?>(nameof(SelectedSegment), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> FocusCommandProperty =
        AvaloniaProperty.Register<SunburstChart, ICommand?>(nameof(FocusCommand));

    public static readonly StyledProperty<double> DiameterProperty =
        AvaloniaProperty.Register<SunburstChart, double>(nameof(Diameter), 300);

    public static readonly StyledProperty<ChartSubject> SubjectProperty =
        AvaloniaProperty.Register<SunburstChart, ChartSubject>(nameof(Subject));

    private IReadOnlyList<SunburstArc> _arcs = [];
    private StreamGeometry?[] _sectors = [];
    private IcicleSegmentDto? _hovered;

    // What the arcs were laid out for: a selection, hover or theme change draws the same arcs again with other fills.
    private IReadOnlyList<IcicleSegmentDto>? _laidOut;
    private long _laidOutTotal;
    private Size _laidOutSize;

    static SunburstChart()
    {
        AffectsRender<SunburstChart>(SegmentsProperty, TotalProperty, SelectedSegmentProperty, DiameterProperty);
        AffectsMeasure<SunburstChart>(SegmentsProperty, DiameterProperty);
    }

    public SunburstChart()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>The arcs as last drawn, relative to <see cref="Center"/> (tests and the screenshot run point at them).</summary>
    internal IReadOnlyList<SunburstArc> Arcs => _arcs;

    /// <summary>The chart's center in its own coordinates.</summary>
    internal Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

    public IReadOnlyList<IcicleSegmentDto>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <summary>The top segment's bytes: a full turn.</summary>
    public long Total
    {
        get => GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    public IcicleSegmentDto? SelectedSegment
    {
        get => GetValue(SelectedSegmentProperty);
        set => SetValue(SelectedSegmentProperty, value);
    }

    /// <summary>Run with the double-clicked segment.</summary>
    public ICommand? FocusCommand
    {
        get => GetValue(FocusCommandProperty);
        set => SetValue(FocusCommandProperty, value);
    }

    /// <summary>The largest the chart is drawn, in pixels; less when the space is narrower, or shorter than twice this (the rest is the tree's).</summary>
    public double Diameter
    {
        get => GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    /// <summary>What the widths measure, for the words on hover.</summary>
    public ChartSubject Subject
    {
        get => GetValue(SubjectProperty);
        set => SetValue(SubjectProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Segments is not { Count: > 0 })
        {
            return default;
        }

        // At most half the height it is given: the tree below the chart is where its rows are, and keeps the other half.
        var unbounded = double.IsInfinity(availableSize.Width);
        var side = unbounded ? Diameter : Math.Min(Diameter, availableSize.Width);
        if (!double.IsInfinity(availableSize.Height))
        {
            side = Math.Min(side, availableSize.Height / 2);
        }

        return new Size(unbounded ? side : availableSize.Width, side);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerMoved(e);
        var arc = ArcAt(e.GetPosition(this));
        if (arc?.Segment != _hovered)
        {
            _hovered = arc?.Segment;
            ToolTip.SetTip(this, arc is null ? null : ChartTexts.Describe(arc.Segment, arc.Count, Total, Subject, ChartShape.Sunburst));
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = null;
        ToolTip.SetTip(this, null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || ArcAt(e.GetPosition(this)) is not { IsGroup: false } arc)
        {
            return;
        }

        SelectedSegment = arc.Segment;
        if (e.ClickCount == 2 && FocusCommand is { } command && command.CanExecute(arc.Segment))
        {
            command.Execute(arc.Segment);
        }

        e.Handled = true;
    }

    private SunburstArc? ArcAt(Point point) => SunburstGeometry.ArcAt(_arcs, point.X - Center.X, point.Y - Center.Y);

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        LayOut();
        if (_arcs.Count == 0)
        {
            return;
        }

        var levels = new[] { Brush("Icicle.Bar1"), Brush("Icicle.Bar2") };
        var path = Brush("Icicle.Path") ?? Brushes.Orange;
        var border = new Pen(Brush("Brush.Surface") ?? Brushes.White, 1);
        var selected = SelectedSegment;
        var center = Center;
        for (var i = 0; i < _arcs.Count; i++)
        {
            var arc = _arcs[i];
            var onPath = IcicleGeometry.IsOnPath(arc.Segment, selected);
            var fill = onPath ? path : levels[arc.Segment.Level % 2] ?? Brushes.LightBlue;
            if (arc.IsCenter)
            {
                context.DrawEllipse(fill, border, center, arc.Outer, arc.Outer);
                DrawCenterLabel(context, arc, onPath);
            }
            else
            {
                context.DrawGeometry(fill, border, _sectors[i]!);
            }
        }
    }

    /// <summary>The arcs and their shapes, laid out again only when the segments, the total or the size change.</summary>
    private void LayOut()
    {
        var segments = Segments;
        if (ReferenceEquals(segments, _laidOut) && Total == _laidOutTotal && Bounds.Size == _laidOutSize)
        {
            return;
        }

        (_laidOut, _laidOutTotal, _laidOutSize) = (segments, Total, Bounds.Size);
        _arcs = segments is null ? [] : SunburstGeometry.Arcs(segments, Total, Math.Min(Bounds.Width, Bounds.Height));
        var center = Center;
        _sectors = [.. _arcs.Select(arc => arc.IsCenter ? null : Sector(center, arc))];
    }

    /// <summary>The focus's name and size inside the center disc, when it has room.</summary>
    private void DrawCenterLabel(DrawingContext context, SunburstArc arc, bool onPath)
    {
        var room = arc.Outer * 1.6;
        if (room < 48)
        {
            return;
        }

        var brush = (onPath ? Brush("Icicle.PathText") : Brush("Brush.Text")) ?? Brushes.Black;
        var words = string.Create(CultureInfo.CurrentCulture, $"{arc.Segment.Name}\n{Format.Bytes(arc.Segment.Width)}");
        var label = new FormattedText(words, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush)
        {
            MaxTextWidth = room,
            MaxLineCount = 2,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        context.DrawText(label, new Point(Center.X - (room / 2), Center.Y - (label.Height / 2)));
    }

    /// <summary>A ring's sector: a full turn is drawn as two halves, since an arc cannot end where it starts.</summary>
    private static StreamGeometry Sector(Point center, SunburstArc arc)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (arc.Sweep >= SunburstGeometry.Turn - 1e-9)
        {
            Add(context, center, arc.Inner, arc.Outer, arc.Start, Math.PI);
            Add(context, center, arc.Inner, arc.Outer, arc.Start + Math.PI, Math.PI);
        }
        else
        {
            Add(context, center, arc.Inner, arc.Outer, arc.Start, arc.Sweep);
        }

        return geometry;
    }

    private static void Add(StreamGeometryContext context, Point center, double inner, double outer, double start, double sweep)
    {
        Point At(double radius, double angle)
        {
            var (x, y) = SunburstGeometry.Point(radius, angle);
            return new Point(center.X + x, center.Y + y);
        }

        var large = sweep > Math.PI;
        context.BeginFigure(At(outer, start), isFilled: true);
        context.ArcTo(At(outer, start + sweep), new Size(outer, outer), 0, large, SweepDirection.Clockwise);
        context.LineTo(At(inner, start + sweep));
        context.ArcTo(At(inner, start), new Size(inner, inner), 0, large, SweepDirection.CounterClockwise);
        context.EndFigure(isClosed: true);
    }

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;
}
