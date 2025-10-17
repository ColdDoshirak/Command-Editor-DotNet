using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using ScaleTransform = System.Windows.Media.ScaleTransform;
using Point = System.Windows.Point;

namespace CommandEditor.App.Views
{
    public partial class UpdateNotificationWidget : System.Windows.Controls.UserControl
    {
        private string _downloadUrl = "";
        private string _newVersion = "";
        private readonly DispatcherTimer _autoHideTimer;

        public string DownloadUrl
        {
            get => _downloadUrl;
            set
            {
                _downloadUrl = value;
                UpdateNotificationText();
            }
        }

        public string NewVersion
        {
            get => _newVersion;
            set
            {
                _newVersion = value;
                UpdateNotificationText();
                UpdateTooltip();
            }
        }

        public UpdateNotificationWidget()
        {
            InitializeComponent();
            Visibility = Visibility.Collapsed; // Initially hidden
            
            // Setup mouse events
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            
            // Setup auto-hide timer (hide after 10 seconds)
            _autoHideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _autoHideTimer.Tick += OnAutoHideTimerTick;
        }

        private void UpdateNotificationText()
        {
            var updateText = (System.Windows.Controls.TextBlock)FindName("UpdateTextBlock");
            if (updateText == null) return;
            
            if (string.IsNullOrEmpty(_newVersion))
            {
                updateText.Text = "Update available!";
            }
            else
            {
                updateText.Text = $"New version: {_newVersion}";
            }
        }

        private void UpdateTooltip()
        {
            var tooltip = string.IsNullOrEmpty(_newVersion) 
                ? "Click to open download page" 
                : $"New version {_newVersion} is available!\nClick to open download page";
            
            ToolTip = tooltip;
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            // Stop auto-hide timer when mouse is over the notification
            _autoHideTimer.Stop();
            
            // Add hover effect with animation
            var scaleTransform = new ScaleTransform
            {
                ScaleX = 1.05,
                ScaleY = 1.05
            };
            RenderTransform = scaleTransform;
            RenderTransformOrigin = new Point(0.5, 0.5);
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            // Reset scale and restart auto-hide timer
            var scaleAnimation = new ScaleTransform
            {
                ScaleX = 1.0,
                ScaleY = 1.0
            };
            RenderTransform = scaleAnimation;
            
            _autoHideTimer.Start();
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!string.IsNullOrEmpty(_downloadUrl))
            {
                try
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = _downloadUrl,
                            UseShellExecute = true
                        }
                    };
                    process.Start();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Could not open download URL: {ex.Message}", 
                                                   "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            
            HideNotification();
        }

        public void ShowNotification(string newVersion, string downloadUrl)
        {
            NewVersion = newVersion;
            DownloadUrl = downloadUrl;
            
            Visibility = Visibility.Visible;
            
            // Animate the notification appearance
            var fadeInAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(0.3)
            };
            
            Opacity = 0.0;
            BeginAnimation(OpacityProperty, fadeInAnimation);
            
            // Auto-hide after 10 seconds
            _autoHideTimer.Start();
        }

        public void HideNotification()
        {
            _autoHideTimer.Stop();
            
            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.3)
            };
            
            fadeOutAnimation.Completed += (s, e) => Visibility = Visibility.Collapsed;
            BeginAnimation(OpacityProperty, fadeOutAnimation);
        }

        private void OnAutoHideTimerTick(object? sender, EventArgs e)
        {
            HideNotification();
            _autoHideTimer.Stop();
        }

        public void UpdatePosition(Window parentWindow)
        {
            if (parentWindow == null) return;

            const int margin = 20;

            // When using Canvas, we use Canvas.Left and Canvas.Top instead of Margin
            Canvas.SetLeft(this, parentWindow.ActualWidth - this.ActualWidth - margin);
            Canvas.SetTop(this, margin);
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Find the parent window and position the notification
            if (Parent is FrameworkElement parent && parent.Parent is Window parentWindow)
            {
                UpdatePosition(parentWindow);
            }
        }
    }
}
