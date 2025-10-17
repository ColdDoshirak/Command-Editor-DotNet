using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using CommandEditor.App.ViewModels;

namespace CommandEditor.App.Views
{
    /// <summary>
    /// Interaction logic for AboutTabView.xaml
    /// </summary>
    public partial class AboutTabView : System.Windows.Controls.UserControl
    {
        public AboutTabView()
        {
            InitializeComponent();
            
            // Set initial active tab button style
            AboutMeButton.Style = (Style)FindResource("ActiveTabButtonStyle");
        }

        private void AboutMeButton_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("AboutMe");
            UpdateButtonStyles(sender as System.Windows.Controls.Button);
        }

        private void InstructionsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("Instructions");
            UpdateButtonStyles(sender as System.Windows.Controls.Button);
        }

        private void VersionInfoButton_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("VersionInfo");
            UpdateButtonStyles(sender as System.Windows.Controls.Button);
        }

        private void ShowTab(string tabName)
        {
            // Hide all content
            AboutMeContent.Visibility = Visibility.Collapsed;
            InstructionsContent.Visibility = Visibility.Collapsed;
            VersionInfoContent.Visibility = Visibility.Collapsed;

            // Show selected content
            switch (tabName)
            {
                case "AboutMe":
                    AboutMeContent.Visibility = Visibility.Visible;
                    break;
                case "Instructions":
                    InstructionsContent.Visibility = Visibility.Visible;
                    break;
                case "VersionInfo":
                    VersionInfoContent.Visibility = Visibility.Visible;
                    break;
            }
        }

        // This method is no longer used since we replaced the WebBrowser with a simple hyperlink
        /*
        private string ConvertTextToHtml(string rawHtml)
        {
            // Process the rawHtml string to properly convert HTML tags to rendered HTML
            // Replace <h2> tags with proper heading formatting
            string processedHtml = rawHtml
                .Replace("<h2>Руководство по использованию</h2>\r\n\r\n<h2>Высока вероятность, что инструкция переедет в Wiki на Github.</h2>", 
                         "<h2>Руководство по использованию</h2><h2>Высока вероятность, что инструкция переедет в Wiki на Github.</h2>")
                .Replace("<h3>Как работать с этим ааа помогите</h3>\r\n<p>Сейчас объясню</p>", 
                         "<h3>Как работать с этим ааа помогите</h3><p>Сейчас объясню</p>")
                .Replace("<h3>А где бот аааа</h3>\r\n<p>Сейчас объясню</p>", 
                         "<h3>А где бот аааа</h3><p>Сейчас объясню</p>")
                .Replace("<h3>Про остальное</h3>\r\n<p>:)</p>", 
                         "<h3>Про остальное</h3><p>:)</p>")
                .Replace("<h3>What about english tutorial?</h3>\r\n<p>Use google translate, i'm too lazy to make buttons for that. Or check readme.md in repository.</p>", 
                         "<h3>What about english tutorial?</h3><p>Use google translate, i'm too lazy to make buttons for that. Or check readme.md in repository.</p>")
                // Replace ul/li tags with proper list formatting
                .Replace("<ul>\r\n", "<ul>")
                .Replace("\r\n</ul>", "</ul>")
                .Replace("\r\n    <li>", "<li>")
                .Replace("</li>\r\n    <li>", "</li><li>")
                .Replace("</li>\r\n    </ul>", "</li></ul>")
                .Replace("<b>", "<strong>")
                .Replace("</b>", "</strong>")
                // Replace paragraph tags
                .Replace("<p>", "<p>")
                .Replace("</p>", "</p>")
                // Clean up any remaining line breaks that might interfere with HTML structure
                .Replace("\r\n", " ");

            // Construct the full HTML page with proper styling
            string html = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='UTF-8'>
                    <style>
                        body {{ 
                            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; 
                            margin: 10px; 
                            line-height: 1.5;
                        }}
                        h2 {{ 
                            color: #2196F3; 
                            font-size: 1.5em; 
                            margin-top: 20px; 
                            margin-bottom: 10px; 
                        }}
                        h3 {{ 
                            color: #424242; 
                            font-size: 1.2em; 
                            margin-top: 15px; 
                            margin-bottom: 8px; 
                        }}
                        ul {{ 
                            margin: 5px 0; 
                            padding-left: 20px; 
                        }}
                        li {{ 
                            margin: 3px 0; 
                        }}
                        p {{ 
                            margin: 8px 0; 
                        }}
                        strong, b {{ 
                            font-weight: bold; 
                        }}
                    </style>
                </head>
                <body>
                    {processedHtml}
                </body>
                </html>";
            return html;
        }
        */

        private void UpdateButtonStyles(System.Windows.Controls.Button activeButton)
        {
            // Reset all buttons to normal style
            AboutMeButton.Style = (Style)FindResource("TabButtonStyle");
            InstructionsButton.Style = (Style)FindResource("TabButtonStyle");
            VersionInfoButton.Style = (Style)FindResource("TabButtonStyle");

            // Set active button style
            if (activeButton != null)
            {
                activeButton.Style = (Style)FindResource("ActiveTabButtonStyle");
            }
        }
        
        private void GitHubWikiLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/ColdDoshirak/Command-Editor-DotNet/wiki",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось открыть ссылку: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}