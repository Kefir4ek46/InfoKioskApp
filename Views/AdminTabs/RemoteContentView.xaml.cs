using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using InfoKioskApp.Models;
using InfoKioskApp.Services;
using Newtonsoft.Json;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class RemoteContentView : UserControl
    {
        private readonly HttpClient _client = new();
        private readonly AppConfig _config;

        public RemoteContentView()
        {
            InitializeComponent();
            _config = ConfigService.LoadConfig();
        }

        // ---------------- ОБНОВЛЕНИЕ СПИСКОВ ----------------
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadList("docs", DocsList);
            await LoadList("media", MediaList);
            await LoadList("schedule", ScheduleList);
        }

        private async Task LoadList(string target, ListBox box)
        {
            try
            {
                string url = $"http://localhost:{_config.RemotePort}/list?target={target}";
                string json = await _client.GetStringAsync(url);
                dynamic data = JsonConvert.DeserializeObject(json);
                box.Items.Clear();
                if (data?.files != null)
                {
                    foreach (var f in data.files)
                        box.Items.Add((string)f.name);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки списка {target}: {ex.Message}");
            }
        }

        // ---------------- ЗАГРУЗКА ----------------
        private async void Upload_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                string target = GetActiveTarget();
                byte[] bytes;
                try
                {
                    // File.ReadAllBytes заменяет ReadAllBytesAsync для совместимости
                    bytes = File.ReadAllBytes(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка чтения файла: {ex.Message}");
                    return;
                }

                var content = new ByteArrayContent(bytes);
                string fileName = Path.GetFileName(dlg.FileName);

                try
                {
                    string url = $"http://localhost:{_config.RemotePort}/upload?target={target}&name={Uri.EscapeDataString(fileName)}";
                    var resp = await _client.PostAsync(url, content);
                    if (!resp.IsSuccessStatusCode)
                    {
                        MessageBox.Show($"Сервер вернул ошибку: {resp.StatusCode}");
                    }
                    await LoadList(target, GetActiveListBox());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки: {ex.Message}");
                }
            }
        }

        // ---------------- УДАЛЕНИЕ ----------------
        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var box = GetActiveListBox();
            if (box.SelectedItem is string name)
            {
                string target = GetActiveTarget();
                try
                {
                    string url = $"http://localhost:{_config.RemotePort}/delete?target={target}&name={Uri.EscapeDataString(name)}";
                    var resp = await _client.GetAsync(url);
                    if (!resp.IsSuccessStatusCode)
                        MessageBox.Show($"Сервер вернул код: {resp.StatusCode}");
                    await LoadList(target, box);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show("Выберите файл в списке для удаления.");
            }
        }

        // ---------------- КАЛЕНДАРЬ ----------------
        private async void AddEvent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ev = new CalendarEvent
                {
                    Title = EventTitle.Text,
                    Type = ((ComboBoxItem)EventType.SelectedItem)?.Content?.ToString() ?? "Другое",
                    StartDate = StartDate.SelectedDate ?? DateTime.Now,
                    EndDate = EndDate.SelectedDate ?? DateTime.Now
                };

                string json = JsonConvert.SerializeObject(ev);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string url = $"http://localhost:{_config.RemotePort}/calendar/add";
                var resp = await _client.PostAsync(url, content);
                if (!resp.IsSuccessStatusCode)
                    MessageBox.Show($"Сервер вернул код: {resp.StatusCode}");
                else
                {
                    MessageBox.Show("Событие добавлено!");
                    EventTitle.Text = "";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления события: {ex.Message}");
            }
        }

        // ---------------- ВСПОМОГАТЕЛЬНЫЕ ----------------
        private string GetActiveTarget()
        {
            if (Tabs.SelectedItem is TabItem tab)
            {
                string header = (tab.Header ?? "").ToString().ToLower();
                if (header.Contains("документ")) return "docs";
                if (header.Contains("медиа")) return "media";
                if (header.Contains("распис")) return "schedule";
            }
            return "media";
        }

        private ListBox GetActiveListBox()
        {
            if (Tabs.SelectedItem is TabItem tab)
            {
                string header = (tab.Header ?? "").ToString().ToLower();
                if (header.Contains("документ")) return DocsList;
                if (header.Contains("медиа")) return MediaList;
                if (header.Contains("распис")) return ScheduleList;
            }
            return DocsList;
        }
    }
}
