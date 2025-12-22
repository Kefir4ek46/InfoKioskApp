using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace InfoKioskApp.Views
{
    public partial class PostViewer : Window
    {
        private readonly string _postId;
        private readonly string _category;
        private readonly bool _openToUpload;

        private static readonly string ApiBase = MediaView.ApiBase;
        private static readonly HttpClient Http = new HttpClient();

        private readonly List<BitmapImage> _gallery = new();
        private int _index = 0;

        public PostViewer(string postId, string category, bool openToUpload = false)
        {
            InitializeComponent();
            _postId = postId;
            _category = category;
            _openToUpload = openToUpload;
            UploadBtn.Visibility = openToUpload ? Visibility.Visible : Visibility.Collapsed;
            Loaded += PostViewer_Loaded;
        }

        private async void PostViewer_Loaded(object sender, RoutedEventArgs e)
            => await LoadPostAsync();


        private async Task LoadPostAsync()
        {
            try
            {
                var res = await Http.GetAsync(
                    $"{ApiBase}/media/post?category={Uri.EscapeDataString(_category)}&id={Uri.EscapeDataString(_postId)}");

                if (!res.IsSuccessStatusCode)
                {
                    MessageBox.Show("Пост не найден");
                    Close();
                    return;
                }

                dynamic obj = JsonConvert.DeserializeObject(await res.Content.ReadAsStringAsync());

                TitleText.Text = (string)(obj.Title ?? "");
                DateText.Text = (string)(obj.Date ?? "");
                DescriptionText.Text = (string)(obj.Description ?? "");

                _gallery.Clear();
                _index = 0;

                var images = obj.Images as Newtonsoft.Json.Linq.JArray;
                if (images != null)
                {
                    foreach (var im in images)
                    {
                        string file = (string)(im["File"] ?? im["file"]);
                        if (string.IsNullOrWhiteSpace(file)) continue;

                        string url =
                            $"{ApiBase}/download?target=media&category={Uri.EscapeDataString(_category)}&post={Uri.EscapeDataString(_postId)}&name={Uri.EscapeDataString(file)}";

                        var bmp = await LoadBitmap(url);
                        if (bmp != null)
                            _gallery.Add(bmp);
                    }
                }

                UpdateGallery();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message);
            }
        }


        private void UpdateGallery()
        {
            if (_gallery.Count == 0)
            {
                MainImage.Source = null;
                ImageCounter.Text = "";
                return;
            }

            MainImage.Source = _gallery[_index];
            ImageCounter.Text = $"{_index + 1} / {_gallery.Count}";
        }


        private void PrevImage_Click(object sender, RoutedEventArgs e)
        {
            if (_gallery.Count == 0) return;
            _index = (_index - 1 + _gallery.Count) % _gallery.Count;
            UpdateGallery();
        }


        private void NextImage_Click(object sender, RoutedEventArgs e)
        {
            if (_gallery.Count == 0) return;
            _index = (_index + 1) % _gallery.Count;
            UpdateGallery();
        }


        private void CloseBtn_Click(object sender, RoutedEventArgs e)
            => Close();


        private async Task<BitmapImage> LoadBitmap(string url)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }


        private async void UploadBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp"
            };

            if (dlg.ShowDialog() != true)
                return;

            foreach (var filePath in dlg.FileNames)
            {
                try
                {
                    using var fs = File.OpenRead(filePath);
                    var fileName = Path.GetFileName(filePath);
                    var nameB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileName));

                    var req = new HttpRequestMessage(
                        HttpMethod.Post,
                        $"{ApiBase}/upload?target=media&category={Uri.EscapeDataString(_category)}&post={Uri.EscapeDataString(_postId)}"
                    );

                    req.Headers.Add("X-Filename-Base64", nameB64);
                    req.Content = new StreamContent(fs);

                    var res = await Http.SendAsync(req);

                    if (!res.IsSuccessStatusCode)
                        MessageBox.Show("Ошибка загрузки: " + res.StatusCode);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка: " + ex.Message);
                }
            }

            await LoadPostAsync();
        }
    }
}
