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
        private const int PageSize = 8;

        public HonorBoardView()
        {
            InitializeComponent();
            Loaded += (_, __) => ReloadAndRender();
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible)
                    ReloadAndRender();
            };
        }

        private void ReloadAndRender()
        {
            LoadData();
            RenderPage();
        }

        private void LoadData()
        {
            _items.Clear();
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "honor", "items.json");
            if (!File.Exists(path))
                return;

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
                CardsGrid.Children.Add(BuildEmptyCard());

            EmptyStateText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static Border BuildEmptyCard()
        {
            return new Border
            {
                Margin = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(29, 31, 39)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(54, 60, 75)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new TextBlock
                {
                    Text = "Пусто",
                    Foreground = new SolidColorBrush(Color.FromRgb(98, 106, 126)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 17
                }
            };
        }

        private UIElement BuildCard(HonorPerson item)
        {
            var border = new Border
            {
                Margin = new Thickness(8),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(35, 38, 48)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 70, 86)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var photoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "honor", "media", Path.GetFileName(item.PhotoFile ?? ""));

            var cardGrid = new Grid();
            var img = new Image { Stretch = Stretch.UniformToFill };
            if (File.Exists(photoPath))
                img.Source = LoadOrientedBitmap(photoPath);
            cardGrid.Children.Add(img);

            var overlayBg = new LinearGradientBrush();
            overlayBg.StartPoint = new Point(0.5, 0);
            overlayBg.EndPoint = new Point(0.5, 1);
            overlayBg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 6, 10, 18), 0));
            overlayBg.GradientStops.Add(new GradientStop(Color.FromArgb(185, 6, 10, 18), 1));

            var nameOverlay = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = overlayBg,
                Padding = new Thickness(10, 22, 10, 10)
            };

            nameOverlay.Child = new TextBlock
            {
                Text = item.FullName,
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            cardGrid.Children.Add(nameOverlay);
            border.Child = cardGrid;
            border.MouseLeftButtonUp += (_, __) => OpenDetails(item, photoPath);
            return border;
        }

        private void OpenDetails(HonorPerson item, string photoPath)
        {
            DetailName.Text = item.FullName;
            DetailDescription.Text = string.IsNullOrWhiteSpace(item.Description) ? "Описание пока не добавлено." : item.Description;
            DetailPhoto.Source = File.Exists(photoPath) ? LoadOrientedBitmap(photoPath) : null;
            DetailsOverlay.Visibility = Visibility.Visible;
        }

        private static ImageSource? LoadOrientedBitmap(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.FirstOrDefault();
                if (frame == null) return null;

                int orientation = 1;
                if (frame.Metadata is BitmapMetadata metadata)
                {
                    try
                    {
                        var query = metadata.GetQuery("/app1/ifd/{ushort=274}");
                        if (query is ushort o) orientation = o;
                    }
                    catch { }
                }

                double angle = orientation switch
                {
                    3 => 180,
                    6 => 90,
                    8 => 270,
                    _ => 0
                };

                if (Math.Abs(angle) < 0.1)
                    return frame;

                var rotated = new TransformedBitmap(frame, new RotateTransform(angle));
                rotated.Freeze();
                return rotated;
            }
            catch
            {
                try
                {
                    return new BitmapImage(new Uri(path, UriKind.Absolute));
                }
                catch
                {
                    return null;
                }
            }
        }

        private void CloseOverlay_Click(object sender, RoutedEventArgs e) => DetailsOverlay.Visibility = Visibility.Collapsed;

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            _page--;
            RenderPage();
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            _page++;
            RenderPage();
        }
    }
}
