using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Documents;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using System.Collections.Generic;
using CommandEditor.App.Commands;
using CommandEditor.App.Views;
using UpdateNotificationWidget = CommandEditor.App.Views.UpdateNotificationWidget;

namespace CommandEditor.App.ViewModels
{
    public class AboutViewModel : INotifyPropertyChanged
    {
        private string _currentVersion = "1.0";

        public string CurrentVersion
        {
            get => _currentVersion;
            set
            {
                _currentVersion = value;
                OnPropertyChanged();
            }
        }

        private bool _isCheckingForUpdates = false;

        public bool IsCheckingForUpdates
        {
            get => _isCheckingForUpdates;
            set
            {
                _isCheckingForUpdates = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CheckUpdatesButtonText));
            }
        }

        public string CheckUpdatesButtonText => IsCheckingForUpdates ? "Checking..." : "Check for Updates";

        private string _updateStatus = "Last checked: Never";
        
        public string UpdateStatus
        {
            get => _updateStatus;
            set
            {
                _updateStatus = value;
                OnPropertyChanged();
            }
        }

        private bool _isUpdateAvailable = false;
        private string? _lastDismissedVersion;
        
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set
            {
                _isUpdateAvailable = value;
                OnPropertyChanged();
            }
        }

        public string? LastDismissedVersion
        {
            get => _lastDismissedVersion;
            set
            {
                _lastDismissedVersion = value;
                OnPropertyChanged();
            }
        }

        private Window? _ownerWindow;
        private UpdateNotificationWidget? _notificationWidget;

        public Window? OwnerWindow
        {
            get => _ownerWindow;
            set
            {
                _ownerWindow = value;
                CreateNotificationWidget();
            }
        }

        private string _currentDate = DateTime.Now.ToString("yyyy-MM-dd");
        
        public string CurrentDate
        {
            get => _currentDate;
            set
            {
                // Always update the value - we'll make it work with WPF's binding requirements
                if (_currentDate != value)
                {
                    _currentDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string GitHubRepo { get; set; } = "colddoshirak/Command-Editor-DotNet";

        public string DeveloperBio { get; set; } = 
            "govnocoded by HotDoshirak\r\n\r\n" +
            "This is a rework of original Command Editor project on .NET 6." +
            "AI was used (again) when coding this, but it has less bugs than original version (hopefully).\r\n";

        public string DeveloperTwitchUrl { get; set; } = "https://twitch.tv/HotDoshirak";
        public string DeveloperGitHubUrl { get; set; } = "https://github.com/colddoshirak";
        public string DeveloperDonationUrl { get; set; } = "https://www.donationalerts.com/r/hotdoshirak1";

        public string InstructionsContent { get; set; } = 
            "<h2>Руководство по использованию</h2>\r\n\r\n" +
            "<h2>Высока вероятность, что инструкция переедет в Wiki на Github.</h2>\r\n\r\n" +
            "<h3>Как работать с этим ааа помогите</h3>\r\n" +
            "<p>Сейчас объясню</p>\r\n" +
            "<ul>\r\n" +
            "    <li><b>В Streamlabs Chatbot нужно экспортировать команды (правой кнопкой мыши по одной из команд - export by group, так удобнее)</li>\r\n" +
            "    <li><b>Если групп несколько... Ну это грустно, я не проверял, лучше одну группу.</li>\r\n" +
            "    <li><b>Load File</b> - Загрузить команды из бекапа команд. После этого создаётся новый файл с командами в новом формате.</li>\r\n" +
            "    <li><b>Save File</b> - Сохранить команды в файл. Также сохраняется раз в 300 секунд. Также сохраняется после закрытия программы.</li>\r\n" +
            "    <li><b>Save Legacy Format</b> - Сохранить в старом формате, хрень, нужно удалить. Вызовет жуткую несовместимость со Streamlabs Chatbot</li>\r\n" +
            "    <li><b>Auto-Assign Sounds</b> - Автоматически назначить звуковые файлы для команд. Нужно выбрать папку со звуковыми файлами. Работает только с одинаковыми названиями (!command -> !command.mp3/wav)</li>\r\n" +
            "    <li><b>Add/Remove Command</b> - Добавить, удалить выбранную команду. Ну это хотя бы работает.</li>\r\n" +
            "    <li><b>Play Sound</b> - Проигрывает/останавливает звуковую команду. Работает даже с ботом.</li>\r\n" +
            "    <li><b>Allow sounds to interrupt each other</b> - Позволяет перебивать другие звуки или запретить это делать. Да начнётся спам.</li>\r\n" +
            "    <li><b>Show message when sound blocked</b> - Отображает предупреждение о том, что звук ещё играет. Ну чтоб спамеры поняли, что это не баг, а фича.</li>\r\n" +
            "    <li><b>Search</b> - Да где же эта команда. Нашёл. Всего-то нужно в строку ввести название команды</li>\r\n" +
            "    <li><b>History</b> - Когда-то случилась ошибка ценой в 20 часов. <b>Я ОТКАЗЫВАЮСЬ ОТ ОШИБКИ. (поставь побольше бекапов)</b></li>\r\n" +
            "    <li><b>NEW Currency Settings</b> - Милорд, казна пустеет. Народ устроил анархию. В общем, тут все настройки для системы очков. (как обычно что-то не работает)</li>\r\n" +
            "    <li><b>NEW Currency Users</b> - Список гениев, миллиардеров, плейбоев, филантропов. Ну если что тут же можно лишить их этих достоинств.</li>\r\n" +
            "    <li><b>NEW Ranks - пока бесполезная функция, не работает, может в будущем починю. Заменяет ранг на нужном количестве очков.</li>\r\n" +
            "</ul>\r\n\r\n" +
            "<h3>А где бот аааа</h3>\r\n" +
            "<p>Сейчас объясню</p>\r\n" +
            "<ul>\r\n" +
            "    <li><b>Есть вкладка Twitch, там можно настроить подключение к каналу.</li>\r\n" +
            "    <li><b>Токен берём с Twitch Token Generator, я не богатый, чтобы свой хост поднимать, который сам будет всё делать.</li>\r\n" +
            "    <li><b>Сохранить не забудь и подключиться. Подключаться надо всегда при запуске программы.</li>\r\n" +
            "    <li><b>NEW Автоматическое переподключение при разрыве соединения.</li>\r\n" +
            "    <li><b>NEW Список зрителей, активных чаттеров, модераторов. Необходима повторная авторизация после обновления.</li>\r\n" +
            "    <li><b>NEW Состояние стрима. Необходима повторная авторизация после обновления.</li>\r\n" +
            "</ul>\r\n\r\n" +
            "<h3>Про остальное</h3>\r\n" +
            "<p>:)</p>\r\n" +
            "<ul>\r\n" +
            "    <li><b>Значения менять можно только вручную, в таблице. Под таблицей так, просто посмотреть, или скопировать значения.</li>\r\n" +
            "    <li><b>Менять обычно надо soundfile или cooldown(в минутах)</li>\r\n" +
            "    <li><b>Нормально работает только ползунок громкости. lolDu</li>\r\n" +
            "    <li><b>Как же он был направ, оказывается новые фичи работают как надо.</li>\r\n" +
            "    <li><b>Теперь программа безопасно сохраняет настройки при включенном боте.</li>\r\n" +
            "    <li><b>Работают как звуковые команды, так и ответы на команду через response. Для этого даже целый чат есть. Как будто второго чата нет</li>\r\n" +
            "    <li><b>Когда-нибудь может быть возможно маловероятно но вполне реально что скорее всего я вряд ли буду это обновлять, это же говнокод. (ага, так я тебе и поверил прошлый я)</li>\r\n" +
            "    <li><b>В currency settings не работает смена команды. Также 100% не работает награда за follow, sub, raid. Зато работает Regular и Mod bonus.</li>\r\n" +
            "    <li><b>Всё надо чинить, вообще всё , пока работает как-то, не понятно каким вообще образом.</li>\r\n" +
            "    <li><b>Надо сделать вики на гитхабе, а то как-то уже много копится</li>\r\n" +
            "</ul>" +
            "<h3>What about english tutorial?</h3>\r\n" +
            "<h2>Документация</h2>\r\n\r\n" +
            "<p>Вся документация теперь находится в Wiki на GitHub.</p>\r\n\r\n" +
            "<p><a href=\"https://github.com/ColdDoshirak/Command-Editor/wiki\">Перейти к Wiki</a></p>\r\n\r\n" +
            "<p>Там вы найдете полное руководство по использованию приложения, настройке и устранению неполадок.</p>";

        public ObservableCollection<string> MemeFilePaths { get; set; } = new ObservableCollection<string>();

        public ICommand OpenUrlCommand { get; private set; }
        public ICommand CheckForUpdatesCommand { get; private set; }
        public ICommand OpenGitHubCommand { get; private set; }

        public AboutViewModel()
        {
            _currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            OpenUrlCommand = new RelayCommand<string>(OpenUrl);
            CheckForUpdatesCommand = new RelayCommand(CheckForUpdates);
            OpenGitHubCommand = new RelayCommand(OpenGitHub);
            
            LoadMemes();
        }

        private void CreateNotificationWidget()
        {
            if (_ownerWindow == null) return;
            
            _notificationWidget = new UpdateNotificationWidget();
            
            // Find the notification canvas in the window and add the widget to it
            var notificationCanvas = FindVisualChild<Canvas>(_ownerWindow, "NotificationCanvas");
            if (notificationCanvas != null)
            {
                notificationCanvas.Children.Add(_notificationWidget);
                
                // Set initial position
                UpdateNotificationPosition();
                
                // Update position when window size changes
                _ownerWindow.SizeChanged += (s, e) => UpdateNotificationPosition();
            }
        }
        
        private void UpdateNotificationPosition()
        {
            if (_ownerWindow != null && _notificationWidget != null)
            {
                const int margin = 20;
                const int verticalOffset = 60; // Offset below the update indicator to prevent overlap
                
                // Position in top-right corner with margin and vertical offset
                Canvas.SetLeft(_notificationWidget, _ownerWindow.ActualWidth - _notificationWidget.ActualWidth - margin);
                Canvas.SetTop(_notificationWidget, margin + verticalOffset);
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent, string name) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T visualChild && child.GetValue(FrameworkElement.NameProperty) as string == name)
                {
                    return visualChild;
                }
                else
                {
                    var result = FindVisualChild<T>(child, name);
                    if (result != null)
                        return result;
                }
            }
            return null;
        }

        public void CheckForUpdatesOnStartup()
        {
            // Silent update check on startup (like the reference implementation)
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000); // Small delay to let UI load
                await CheckForUpdatesAsync(silent: true);
            });
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not open URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckForUpdates()
        {
            await CheckForUpdatesAsync(silent: false);
        }

        private async Task CheckForUpdatesAsync(bool silent = false)
        {
            if (!silent)
            {
                IsCheckingForUpdates = true;
            }
            
            try
            {
                var result = await PerformUpdateCheckAsync();
                var currentTime = DateTime.Now.ToString("HH:mm:ss");
                
                // Ensure UI updates happen on UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (result.IsUpdateAvailable)
                    {
                        UpdateStatus = $"Update available! {result.LatestVersion} ({currentTime})";
                        IsUpdateAvailable = true;  // Set the update availability property
                        
                        if (silent)
                        {
                            // Show a dialog box for silent startup checks to ensure visibility
                            // But only if this version hasn't been dismissed before
                            var shouldShowDialog = LastDismissedVersion != result.LatestVersion;
                            
                            if (shouldShowDialog)
                            {
                                var message = $"New version available: {result.LatestVersion}\\n\\n" +
                                             $"Current version: {CurrentVersion}\\n" +
                                             $"New version: {result.LatestVersion}\\n\\n" +
                                             $"Release Notes:\\n{result.ReleaseNotes}\\n\\n" +
                                             "Do you want to download the update?";
                                
                                // Show the dialog with the owner window to ensure proper focus
                                var resultDialog = _ownerWindow != null 
                                    ? System.Windows.MessageBox.Show(_ownerWindow, message, "Update Available", 
                                                    MessageBoxButton.YesNo, MessageBoxImage.Information)
                                    : System.Windows.MessageBox.Show(message, "Update Available", 
                                                    MessageBoxButton.YesNo, MessageBoxImage.Information);
                                
                                if (resultDialog == MessageBoxResult.Yes)
                                {
                                    OpenUrl(result.DownloadUrl);
                                }
                                else
                                {
                                    // User declined, remember this version to not show again until there's a newer version
                                    LastDismissedVersion = result.LatestVersion;
                                }
                            }
                        }
                        else if (!silent)
                        {
                            // Show full dialog for manual checks
                            var message = $"New version available: {result.LatestVersion}\n\n" +
                                         $"Current version: {CurrentVersion}\n" +
                                         $"New version: {result.LatestVersion}\n\n" +
                                         $"Release Notes:\n{result.ReleaseNotes}\n\n" +
                                         "Do you want to download the update?";
                            
                            var resultDialog = System.Windows.MessageBox.Show(message, "Update Available", 
                                                              MessageBoxButton.YesNo, 
                                                              MessageBoxImage.Information);
                            
                            if (resultDialog == MessageBoxResult.Yes)
                            {
                                OpenUrl(result.DownloadUrl);
                            }
                        }
                    }
                    else
                    {
                        // For silent checks, only show "Up to date" in status, don't show popup
                        if (silent)
                        {
                            UpdateStatus = $"Up to date ({currentTime})";
                        }
                        else
                        {
                            // For manual checks, show both status and popup
                            UpdateStatus = $"Up to date ({currentTime})";
                            // Show the dialog with the owner window to ensure proper focus
                            if (_ownerWindow != null)
                            {
                                System.Windows.MessageBox.Show(_ownerWindow, $"You are running the latest version ({CurrentVersion})", 
                                                   "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                System.Windows.MessageBox.Show($"You are running the latest version ({CurrentVersion})", 
                                                   "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                        IsUpdateAvailable = false;  // No update available
                    }
                });
            }
            catch (HttpRequestException ex)
            {
                var currentTime = DateTime.Now.ToString("HH:mm:ss");
                var message = ex.Message.Contains("403") || ex.Message.Contains("Forbidden") 
                    ? "GitHub API access forbidden. This may be due to rate limiting."
                    : $"Network error: {ex.Message}";
                
                // Ensure UI updates happen on UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateStatus = $"Error: {message.Substring(0, Math.Min(50, message.Length))}... ({currentTime})";
                    
                    // Only show error popup for manual checks
                    if (!silent)
                    {
                        if (_ownerWindow != null)
                        {
                            System.Windows.MessageBox.Show(_ownerWindow, message, 
                                           "Network Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(message, 
                                           "Network Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                });
            }
            catch (System.Text.Json.JsonException ex)
            {
                var currentTime = DateTime.Now.ToString("HH:mm:ss");
                var message = $"Failed to parse update information:\n{ex.Message}";
                
                // Ensure UI updates happen on UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateStatus = $"Parse Error ({currentTime})";
                    
                    // Only show parse error popup for manual checks
                    if (!silent)
                    {
                        if (_ownerWindow != null)
                        {
                            System.Windows.MessageBox.Show(_ownerWindow, message, 
                                           "Parse Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(message, 
                                           "Parse Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                });
            }
            catch (TaskCanceledException)
            {
                var currentTime = DateTime.Now.ToString("HH:mm:ss");
                var message = "Update check timed out. Please check your internet connection.";
                
                // Ensure UI updates happen on UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateStatus = $"Timeout ({currentTime})";
                    
                    // Only show timeout popup for manual checks
                    if (!silent)
                    {
                        if (_ownerWindow != null)
                        {
                            System.Windows.MessageBox.Show(_ownerWindow, message, 
                                           "Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(message, 
                                           "Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                var currentTime = DateTime.Now.ToString("HH:mm:ss");
                var message = $"Failed to check for updates:\n{ex.Message}";
                
                // Ensure UI updates happen on UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateStatus = $"Error: {ex.Message.Substring(0, Math.Min(30, ex.Message.Length))}... ({currentTime})";
                    
                    // Only show generic error popup for manual checks
                    if (!silent)
                    {
                        if (_ownerWindow != null)
                        {
                            System.Windows.MessageBox.Show(_ownerWindow, message, 
                                           "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(message, 
                                           "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                });
            }
            finally
            {
                if (!silent)
                {
                    IsCheckingForUpdates = false;
                }
            }
        }

        private async Task<UpdateCheckResult> PerformUpdateCheckAsync()
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            // Add User-Agent header (required by GitHub API)
            httpClient.DefaultRequestHeaders.Add("User-Agent", $"CommandEditor/{CurrentVersion}");

            var apiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            var response = await httpClient.GetStringAsync(apiUrl);

            using var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            var latestVersion = root.GetProperty("tag_name").GetString().TrimStart('v');
            var downloadUrl = root.GetProperty("html_url").GetString();
            var releaseNotes = root.GetProperty("body").GetString();

            var isNewer = IsNewerVersion(latestVersion, CurrentVersion);

            return new UpdateCheckResult
            {
                IsUpdateAvailable = isNewer,
                LatestVersion = latestVersion,
                DownloadUrl = downloadUrl,
                ReleaseNotes = releaseNotes ?? "No release notes available."
            };
        }

        private bool IsNewerVersion(string latest, string current)
        {
            try
            {
                var latestParts = Array.ConvertAll(latest.Split('.'), int.Parse);
                var currentParts = Array.ConvertAll(current.Split('.'), int.Parse);

                var maxLen = Math.Max(latestParts.Length, currentParts.Length);
                Array.Resize(ref latestParts, maxLen);
                Array.Resize(ref currentParts, maxLen);

                for (int i = 0; i < maxLen; i++)
                {
                    if (latestParts[i] > currentParts[i])
                        return true;
                    else if (latestParts[i] < currentParts[i])
                        return false;
                }

                return false;
            }
            catch
            {
                return latest != current;
            }
        }

        private void OpenGitHub()
        {
            OpenUrl("https://github.com/colddoshirak/Command-Editor-DotNet");
        }

        private void LoadMemes()
        {
            try
            {
                // Look for memes directory in the application folder
                var memesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memes");
                
                if (Directory.Exists(memesDir))
                {
                    var memeFiles = Directory.GetFiles(memesDir)
                        .Where(f => f.ToLower().EndsWith(".png") || 
                                   f.ToLower().EndsWith(".jpg") || 
                                   f.ToLower().EndsWith(".jpeg") || 
                                   f.ToLower().EndsWith(".gif") || 
                                   f.ToLower().EndsWith(".bmp"))
                        .ToArray();

                    foreach (var file in memeFiles)
                    {
                        MemeFilePaths.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently handle error, just don't load memes
                Debug.WriteLine($"Error loading memes: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string LatestVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
    }
}