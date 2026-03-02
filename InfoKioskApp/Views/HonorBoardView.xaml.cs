using InfoKioskApp.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InfoKioskApp.Views
{
    public partial class HonorBoardView : UserControl
    {
        private readonly List<HonorPerson> _items = new();
        private int _page;
        private const int PageSize = 6;

        public HonorBoardView()
        {
            InitializeComponent();
            Loaded += (_, __) => { LoadData(); RenderPage(); };
        }

        private void LoadData()
        {
            _items.Clear();
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "honor", "items.json");
            if (!File.Exists(path)) return;
            var parsed = JsonConvert.DeserializeObject<List<HonorPerson>>(File.ReadAllText(path)) ?? [];
            _items.AddRange(parsed.OrderByDescending(x => x.CreatedAt));
        }

        private void RenderPage()
        {
            CardsGrid.Children.Clear();
            int totalPages = Math.Max(1, (int)Math.Ceiling(_items.Count / (double)PageSize));
            if (_page >= totalPages) _page = totalPages - 1;
            if (_page < 0) _page = 0;

            PageInfoText.Text = $"Страница {_page + 1} / {totalPages}";
            var pageItems = _items.Skip(_page * PageSize).Take(PageSize).ToList();
            foreach (var it in pageItems)
                CardsGrid.Children.Add(BuildCard(it));

            for (int i = pageItems.Count; i < PageSize; i++)
                CardsGrid.Children.Add(new Border { Margin = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)), CornerRadius = new CornerRadius(10) });
        }

        private UIElement BuildCard(HonorPerson item)
        {
            var border = new Border
            {
                Margin = new Thickness(8),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(35, 38, 48)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 70, 86)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var panel = new StackPanel();
            var img = new Image { Height = 190, Stretch = Stretch.Uniform };
            var photoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "honor", "media", Path.GetFileName(item.PhotoFile ?? ""));
            if (File.Exists(photoPath)) img.Source = new BitmapImage(new Uri(photoPath, UriKind.Absolute));
            panel.Children.Add(img);
            panel.Children.Add(new TextBlock
            {
                Text = item.FullName,
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(10, 8, 10, 10),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            border.Child = panel;
            border.MouseLeftButtonUp += (_, __) => OpenDetails(item, photoPath);
            return border;
        }

        private void OpenDetails(HonorPerson item, string photoPath)
        {
            DetailName.Text = item.FullName;
            DetailDescription.Text = item.Description;
            DetailPhoto.Source = File.Exists(photoPath) ? new BitmapImage(new Uri(photoPath, UriKind.Absolute)) : null;
            DetailsOverlay.Visibility = Visibility.Visible;
        }

        private void CloseOverlay_Click(object sender, RoutedEventArgs e) => DetailsOverlay.Visibility = Visibility.Collapsed;
        private void PrevPage_Click(object sender, RoutedEventArgs e) { _page--; RenderPage(); }
        private void NextPage_Click(object sender, RoutedEventArgs e) { _page++; RenderPage(); }
    }
}
