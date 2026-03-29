using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TaskSplit.Models;
using TaskSplit.Services;
using TaskSplit.Win32;

namespace TaskSplit.Views;

public partial class TaskbarOverlay : Window
{
    private readonly TaskbarService _taskbarService;
    private AppConfig? _config;

    public TaskbarOverlay(TaskbarService taskbarService)
    {
        InitializeComponent();
        _taskbarService = taskbarService;
        Loaded += OnLoaded;
    }

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncToTaskbar();
    }

    public void SyncToTaskbar()
    {
        var rect = _taskbarService.GetTaskbarRect();
        if (rect.HasValue)
        {
            Left = rect.Value.Left;
            Top = rect.Value.Top;
            Width = rect.Value.Width;
            Height = rect.Value.Height;
        }
        Refresh();
    }

    public void Refresh()
    {
        OverlayCanvas.Children.Clear();
        if (_config == null) return;

        var buttons = _taskbarService.GetTaskbarButtons();
        if (buttons.Count == 0) return;

        // Simple grouping logic for visualization
        foreach (var group in _config.Groups)
        {
            // Find the last button in this group to place a divider after it
            var groupButtons = buttons
                .Where(b => group.ProcessNames.Contains(b.ProcessName))
                .OrderBy(b => b.Order)
                .ToList();

            if (groupButtons.Count > 0)
            {
                var lastBtn = groupButtons.Last();
                var btnRect = lastBtn.Rect;

                // Position relative to taskbar
                double x = btnRect.Right - Left + (group.GapAfter / 2.0);

                if (_config.ShowDividers)
                {
                    var line = new Line
                    {
                        X1 = x, Y1 = 8,
                        X2 = x, Y2 = Height - 8,
                        Stroke = (SolidColorBrush)new BrushConverter().ConvertFrom(group.Color)!,
                        StrokeThickness = 2,
                        Opacity = 0.8
                    };
                    OverlayCanvas.Children.Add(line);
                }

                if (_config.ShowGroupLabels)
                {
                    var label = new TextBlock
                    {
                        Text = group.Name.ToUpper(),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(group.Color)!,
                        Opacity = 0.7
                    };
                    Canvas.SetLeft(label, x + 4);
                    Canvas.SetTop(label, 2);
                    OverlayCanvas.Children.Add(label);
                }
            }
        }
    }
}
