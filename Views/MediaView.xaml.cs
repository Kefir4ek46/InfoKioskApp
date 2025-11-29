using InfoKioskApp.Converters;
using InfoKioskApp.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InfoKioskApp.Views
{
    public partial class MediaView : UserControl
    {
        public static string ApiBase = "http://localhost:8080";
        private static readonly HttpClient Http = new HttpClient() { Timeout = TimeSpan.FromSeconds(20) };

        private ObservableCollection<PostViewModel> _posts = new ObservableCollection<PostViewModel>();
        private int _page = 1;
        private int _pageSize = 9;
        private int _total = 0;
        private string _currentCategory = "";

        public MediaView()
        {
            InitializeComponent();
            PostsItemsControl.ItemsSource = _posts;

            // ensure small converter resource for null -> visible
            if (!this.Resources.Contains("NullToVisibilityConverter"))
                this.Resources.Add("NullToVisibilityConverter", new NullToVisibilityConverter());

            Loaded += async (s, e) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await LoadCategoriesAndBuildUI();
            if (!string.IsNullOrEmpty(_currentCategory))
                await LoadPostsAsync(_currentCategory, 1);
        }

        private async Task LoadCategoriesAndBuildUI()
        {
            try
            {
                var res = await Http.GetAsync($"{ApiBase}/media/categories");
                if (!res.IsSuccessStatusCode) return;

                var json = await res.Content.ReadAsStringAsync();
                var cats = JsonConvert.DeserializeObject<List<dynamic>>(json) ?? new List<dynamic>();

                CategoryPanel.Children.Clear();

                foreach (var c in cats)
                {
                    string id = (string)(c.id ?? "");
                    string name = (string)(c.name ?? id);

                    var btn = new Button
                    {
                        Content = name,
                        Tag = id,
                        Margin = new Thickness(4, 0, 4, 0),
                        Padding = new Thickness(10, 6, 10, 6),
                        Background = Brushes.Transparent,
                        Foreground = Brushes.White,
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(1),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    btn.Click += CategoryButton_Click;
                    CategoryPanel.Children.Add(btn);
                }

                if (cats.Count > 0)
                {
                    _currentCategory = (string)(cats[0].id ?? "");
                    HighlightCategory(_currentCategory);
                }
            }
            catch { /* silent */ }
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string id)
            {
                _currentCategory = id;
                _page = 1;
                HighlightCategory(id);
                _ = LoadPostsAsync(id, _page);
            }
        }

        private void HighlightCategory(string id)
        {
            foreach (var child in CategoryPanel.Children)
            {
                if (child is Button b)
                {
                    if ((b.Tag as string) == id)
                    {
                        b.Background = Brushes.DodgerBlue;
                        b.Foreground = Brushes.White;
                    }
                    else
                    {
                        b.Background = Brushes.Transparent;
                        b.Foreground = Brushes.White;
                    }
                }
            }
        }

        private async Task LoadPostsAsync(string category, int page = 1)
        {
            try
            {
                _page = Math.Max(1, page);
                string url = $"{ApiBase}/media/posts?category={Uri.EscapeDataString(category)}&page={_page}&pageSize={_pageSize}";
                var res = await Http.GetAsync(url);
                if (!res.IsSuccessStatusCode) return;

                var json = await res.Content.ReadAsStringAsync();
                var root = JsonConvert.DeserializeObject<dynamic>(json);
                _total = (int)(root.total ?? 0);

                var itemsJson = JsonConvert.SerializeObject(root.items ?? new object[] { });
                var previews = JsonConvert.DeserializeObject<List<MediaPostPreview>>(itemsJson) ?? new List<MediaPostPreview>();

                _posts.Clear();

                // create viewmodels and asynchronously load cover if missing
                var tasks = new List<Task>();
                foreach (var p in previews)
                {
                    var vm = new PostViewModel
                    {
                        Id = p.Id,
                        Title = p.Title,
                        Date = p.Date,
                        Cover = p.Cover,
                        ImagesCount = p.ImagesCount
                    };
                    _posts.Add(vm);

                    // if cover is empty try to fetch first image from full post
                    if (string.IsNullOrWhiteSpace(vm.Cover))
                    {
                        tasks.Add(FillCoverFromPostAsync(vm, category));
                    }
                    else
                    {
                        // try to load cover bitmap
                        tasks.Add(LoadCoverBitmapAsync(vm, category));
                    }
                }

                // run loaders concurrently
                await Task.WhenAll(tasks);

                UpdatePaginationInfo();
            }
            catch (Exception ex)
            {
                // show status if you want
            }
        }

        private async Task FillCoverFromPostAsync(PostViewModel vm, string category)
        {
            try
            {
                var res = await Http.GetAsync($"{ApiBase}/media/post?category={Uri.EscapeDataString(category)}&id={Uri.EscapeDataString(vm.Id)}");
                if (!res.IsSuccessStatusCode) return;

                var json = await res.Content.ReadAsStringAsync();
                dynamic obj = JsonConvert.DeserializeObject<dynamic>(json);
                var images = obj.images as JArray;

                if (images != null && images.Count > 0)
                {
                    string file = null;
                    var first = images[0] as JObject;

                    if (first.TryGetValue("file", StringComparison.OrdinalIgnoreCase, out var f1))
                        file = f1?.ToString();
                    else if (first.TryGetValue("File", StringComparison.OrdinalIgnoreCase, out var f2))
                        file = f2?.ToString();

                    if (!string.IsNullOrEmpty(file))
                    {
                        vm.Cover = file;
                        vm.ImagesCount = images.Count;

                        await LoadCoverBitmapAsync(vm, category);
                    }
                }
            }
            catch { }
        }


        private async Task LoadCoverBitmapAsync(PostViewModel vm, string category)
        {
            if (string.IsNullOrWhiteSpace(vm.Cover)) return;
            try
            {
                string url = $"{ApiBase}/download?target=media&name={Uri.EscapeDataString(vm.Cover)}&category={Uri.EscapeDataString(category)}&post={Uri.EscapeDataString(vm.Id)}";
                var bytes = await Http.GetByteArrayAsync(url);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();

                vm.CoverBitmap = bmp;
            }
            catch
            {
                // ignore (leave null)
            }
        }

        private void UpdatePaginationInfo()
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(_total / (double)_pageSize));
            PageInfo.Text = $"Страница {_page} / {totalPages}  (Всего: {_total})";
            PrevPageBtn.IsEnabled = _page > 1;
            NextPageBtn.IsEnabled = _page < totalPages;
        }

        private async void PrevPageBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_page > 1) { _page--; await LoadPostsAsync(_currentCategory, _page); }
        }

        private async void NextPageBtn_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(_total / (double)_pageSize));
            if (_page < totalPages) { _page++; await LoadPostsAsync(_currentCategory, _page); }
        }

        private void PostCard_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // DataContext of the clicked tile is PostViewModel
            if (sender is Grid g && g.DataContext is PostViewModel vm)
            {
                var viewer = new PostViewer(vm.Id, _currentCategory);
                viewer.Owner = Window.GetWindow(this);
                viewer.ShowDialog();

                // after viewer closed — refresh current page (in case images were added)
                _ = LoadPostsAsync(_currentCategory, _page);
            }
        }

        // simple viewmodel for binding
        private class PostViewModel : INotifyPropertyChanged
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Date { get; set; } = "";
            public string Cover { get; set; } = "";
            public int ImagesCount { get; set; } = 0;

            private BitmapImage _coverBitmap;
            public BitmapImage CoverBitmap
            {
                get => _coverBitmap;
                set { _coverBitmap = value; OnPropertyChanged(nameof(CoverBitmap)); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    
}
