using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DragonPet;

// Real WPF text in a safe rectangle inscribed within the ellipse.
public sealed class ThoughtBubble : Decorator
{
    public const double TailHeight = 29;
    const double TextWidthRatio = .76, TextHeightRatio = .58;
    static readonly Brush Fill = Ui.Brush("#FFFCFAFF");
    static readonly Pen Outline = new(Ui.Brush("#BBA9D3"), 1.4);
    readonly TextBlock caption = new()
    {
        FontSize = 12.5, FontWeight = FontWeights.Normal, Foreground = Ui.Brush("#8A729F"),
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8)
    };
    readonly TextBlock body = new()
    {
        FontSize = 16.5, FontWeight = FontWeights.Normal, LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        Foreground = Ui.Brush("#57416E"), TextWrapping = TextWrapping.Wrap
    };
    readonly ScrollViewer viewport;
    string message = "", label = "", fontId = "";
    bool detailed;
    double fontSize;
    public ThoughtBubble()
    {
        Width = 280; UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextElement.SetFontFamily(this, Ui.PetFont);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        var content = new StackPanel { UseLayoutRounding = true };
        viewport = new ScrollViewer
        {
            Content = body, MaxHeight = 144, Focusable = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        content.Children.Add(caption); content.Children.Add(viewport); Child = content;
        SetTypography(BubbleFonts.DefaultId, BubbleFonts.DefaultSize);
    }
    public void SetTypography(string font, double size)
    {
        size = double.IsFinite(size) ? Math.Clamp(size, 12, 28) : BubbleFonts.DefaultSize;
        if (fontId == font && fontSize == size) return;
        fontId = font; fontSize = size;
        TextElement.SetFontFamily(this, BubbleFonts.Resolve(font));
        body.FontSize = size; body.LineHeight = Math.Round(size * 1.45, 1);
        caption.FontSize = Math.Max(10.5, Math.Round(size * .76, 1));
        SetMessage(message, label, detailed);
    }
    public void SetMessage(string text, string title, bool details = false)
    {
        message = text; label = title; detailed = details;
        body.Inlines.Clear(); body.Text = text.Trim(); caption.Text = title.Trim();
        caption.Visibility = caption.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        body.TextAlignment = details || text.Split('\n').Length >= 3 || text.Length > 75 ? TextAlignment.Left : TextAlignment.Center;
        if (details)
        {
            body.Inlines.Clear();
            bool first = true;
            foreach (string line in text.Trim().Split('\n'))
            {
                if (!first) body.Inlines.Add(new LineBreak()); first = false;
                bool secondary = line.EndsWith("后重置") || line.StartsWith("更新于 ") || line.StartsWith("上次结果 · ");
                var run = new Run(line);
                if (secondary) { run.FontSize = Math.Max(10.5, Math.Round(fontSize * .73, 1)); run.FontWeight = FontWeights.Normal; run.Foreground = Ui.Brush("#8A729F"); }
                body.Inlines.Add(run);
            }
        }
        body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Width = details ? 360 : Math.Clamp(Math.Ceiling(Math.Max(body.DesiredSize.Width, caption.DesiredSize.Width) / TextWidthRatio) + 4, 252, 360);
        viewport.ScrollToTop(); InvalidateMeasure();
    }
    public void LimitHeight(double availableHeight)
    {
        caption.Measure(new Size((Width - 4) * TextWidthRatio, double.PositiveInfinity));
        double space = (availableHeight - TailHeight - 4) * TextHeightRatio - caption.DesiredSize.Height;
        double maximum = Math.Max(144, body.LineHeight * 4);
        viewport.MaxHeight = Math.Max(body.LineHeight, Math.Floor(Math.Clamp(space, body.LineHeight, maximum) / body.LineHeight) * body.LineHeight);
    }
    public Size MeasureMessage()
    {
        Child.Measure(new Size((Width - 4) * TextWidthRatio, double.PositiveInfinity));
        return new Size(Width, Math.Max(104, Child.DesiredSize.Height / TextHeightRatio + 4) + TailHeight);
    }
    protected override Size MeasureOverride(Size constraint)
    {
        double width = double.IsFinite(constraint.Width) ? constraint.Width : Width;
        Child.Measure(new Size(Math.Max(0, (width - 4) * TextWidthRatio), double.PositiveInfinity));
        return new Size(width, Math.Max(104, Child.DesiredSize.Height / TextHeightRatio + 4) + TailHeight);
    }
    protected override Size ArrangeOverride(Size size)
    {
        double bodyHeight = Math.Max(0, size.Height - TailHeight);
        double textWidth = Math.Max(0, (size.Width - 4) * TextWidthRatio);
        double textHeight = Math.Min(Child.DesiredSize.Height, Math.Max(0, (bodyHeight - 4) * TextHeightRatio));
        Child.Arrange(new Rect((size.Width - textWidth) / 2, (bodyHeight - textHeight) / 2, textWidth, textHeight));
        return size;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight - TailHeight;
        if (w <= 4 || h <= 4) return;
        dc.DrawEllipse(Fill, Outline, new Point(w / 2, h / 2), (w - 4) / 2, (h - 4) / 2);
        dc.DrawEllipse(Fill, Outline, new Point(w * .55, h + 8), 6.5, 5.5);
        dc.DrawEllipse(Fill, Outline, new Point(w * .58, h + 22), 3.5, 3);
    }
}
