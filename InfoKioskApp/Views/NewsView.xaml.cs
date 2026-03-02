using InfoKioskApp.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InfoKioskApp.Views
{
    public partial class NewsView : UserControl
    {
        private readonly List<NewsPost> _posts = new();
        private readonly List<NewsMediaItem> _overlayMedia = new();
        private int _overlayMediaIndex;
        private int _overlayRotation;
        private MediaElement? _activeOverlayMediaElement;
        private static readonly HttpClient _http = new();

        public NewsView()
        {
            InitializeComponent();
            Loaded += (_, __) => LoadNews();
        }

        private void LoadNews()
        {
            _posts.Clear();
            NewsListPanel.Children.Clear();

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "news", "published.json");
            if (!File.Exists(path))
            {
                NewsListPanel.Children.Add(new TextBlock
                {
                    Text = "Пока нет опубликованных новостей.",
                    Foreground = Brushes.Gray,
                    FontSize = 18
                });
                return;
            }

            var posts = JsonConvert.DeserializeObject<List<NewsPost>>(File.ReadAllText(path)) ?? [];
            _posts.AddRange(posts.OrderByDescending(p => p.CreatedAt));

            foreach (var post in _posts)
                NewsListPanel.Children.Add(BuildPreviewCard(post));
        }

        private UIElement BuildPreviewCard(NewsPost post)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 40)),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 70)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(12),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var preview = BuildPreviewMedia(post);
            Grid.SetColumn(preview, 0);
            grid.Children.Add(preview);

            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            text.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(post.Title) ? "Без названия" : post.Title,
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(new TextBlock
            {
                Text = $"{post.CreatedAt:dd.MM.yyyy HH:mm} • {post.AuthorLogin}",
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 8)
            });
            text.Children.Add(new TextBlock
            {
                Text = post.Content,
                Foreground = Brushes.White,
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 68
            });

            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            card.Child = grid;

            card.MouseLeftButtonUp += (_, __) => OpenOverlay(post);
            return card;
        }

        private UIElement BuildPreviewMedia(NewsPost post)
        {
            if (!string.IsNullOrWhiteSpace(post.VideoFile))
            {
                var v = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "news", "media", Path.GetFileName(post.VideoFile));
                if (File.Exists(v))
                {
                    return new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(10, 10, 12)),
                        CornerRadius = new CornerRadius(8),
                        Height = 92,
                        Child = new TextBlock
                        {
                            Text = "🎬 Видео",
                            Foreground = Brushes.LightGray,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = 18
                        }
                    };
                }
            }

            var photo = (post.PhotoFiles ?? [])
                .Select(Path.GetFileName)
                .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));
            if (!string.IsNullOrWhiteSpace(photo))
            {
                var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "news", "media", photo);
                if (File.Exists(p))
                {
                    return new Image
                    {
                        Source = new BitmapImage(new Uri(p, UriKind.Absolute)),
                        Height = 92,
                        Stretch = Stretch.UniformToFill
                    };
                }
            }

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 10, 12)),
                CornerRadius = new CornerRadius(8),
                Height = 92,
                Child = new TextBlock
                {
                    Text = "Нет медиа",
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private void OpenOverlay(NewsPost post)
        {
            OverlayTitleText.Text = string.IsNullOrWhiteSpace(post.Title) ? "Без названия" : post.Title;
            OverlayMetaText.Text = $"{post.CreatedAt:dd.MM.yyyy HH:mm} • {post.AuthorLogin}";
            OverlayContentText.Text = post.Content ?? "";

            BuildOverlayMedia(post);
            _overlayMediaIndex = 0;
            _overlayRotation = 0;
            RenderOverlayMedia();
            NewsOverlay.Visibility = Visibility.Visible;
        }

        private void BuildOverlayMedia(NewsPost post)
        {
            _overlayMedia.Clear();

            // Видео всегда первым
            if (!string.IsNullOrWhiteSpace(post.VideoFile))
            {
                var v = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "news", "media", Path.GetFileName(post.VideoFile));
                if (File.Exists(v))
                {
                    bool expectedPortrait = (post.VideoHeight ?? 0) > (post.VideoWidth ?? 0);
                    _overlayMedia.Add(new NewsMediaItem { Type = "video", Path = v, ExpectedPortrait = expectedPortrait });
                }
            }

            if (_overlayMedia.Count == 0 && !string.IsNullOrWhiteSpace(post.VideoUrl))
            {
                _overlayMedia.Add(new NewsMediaItem { Type = "video", Path = post.VideoUrl!, IsRemote = true, ExpectedPortrait = (post.VideoHeight ?? 0) > (post.VideoWidth ?? 0) });
            }

            foreach (var photo in (post.PhotoFiles ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(Path.GetFileName).Distinct())
            {
                var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "news", "media", photo);
                if (File.Exists(p)) _overlayMedia.Add(new NewsMediaItem { Type = "photo", Path = p });
            }

        }

        private void RenderLinkQr(string? linkUrl)
        {
            OverlayLinkText.Text = string.Empty;
            OverlayQrImage.Source = null;
            OverlayQrImage.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(linkUrl))
                return;

            OverlayLinkText.Text = $"Ссылка: {linkUrl}";
            try
            {
                var qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=220x220&data={Uri.EscapeDataString(linkUrl)}";
                OverlayQrImage.Source = new BitmapImage(new Uri(qrUrl));
                OverlayQrImage.Visibility = Visibility.Visible;
            }
            catch
            {
                OverlayLinkText.Text += " (QR недоступен)";
            }
        }

        private void StopActiveOverlayMedia()
        {
            try
            {
                _activeOverlayMediaElement?.Stop();
            }
            catch { }

            _activeOverlayMediaElement = null;
        }

        private static int CalculateAutoRotation(NewsMediaItem item, MediaElement media)
        {
            if (!item.IsVideo || !item.ExpectedPortrait)
                return 0;

            // Если редактор отправил видео как "вертикальное", а MediaElement отдает
            // горизонтальные NaturalVideoWidth/Height (типично при игнорировании rotation metadata),
            // поворачиваем автоматически на 90 градусов.
            if (media.NaturalVideoWidth > media.NaturalVideoHeight)
                return 90;

            return 0;
        }

        private void RenderOverlayMedia()
        {
            StopActiveOverlayMedia();

            if (_overlayMedia.Count == 0)
            {
                OverlayMediaHost.Content = new TextBlock { Text = "Нет медиа", Foreground = Brushes.Gray };
                OverlayMediaIndexText.Text = "0/0";
                return;
            }

            if (_overlayMediaIndex < 0) _overlayMediaIndex = _overlayMedia.Count - 1;
            if (_overlayMediaIndex >= _overlayMedia.Count) _overlayMediaIndex = 0;

            var item = _overlayMedia[_overlayMediaIndex];
            OverlayMediaIndexText.Text = $"{_overlayMediaIndex + 1}/{_overlayMedia.Count}";

            if (item.Type == "photo")
            {
                var img = new Image
                {
                    Source = new BitmapImage(new Uri(item.Path, UriKind.Absolute)),
                    Stretch = Stretch.Uniform,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(_overlayRotation)
                };
                OverlayMediaHost.Content = img;
                return;
            }

            if (item.Type == "video")
            {
                var wrap = new StackPanel();
                var media = new MediaElement
                {
                    Source = new Uri(item.Path, item.IsRemote ? UriKind.Absolute : UriKind.Absolute),
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Stop,
                    Stretch = Stretch.Uniform,
                    Height = 480,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };

                int autoRotation = 0;
                media.MediaOpened += (_, __) =>
                {
                    autoRotation = CalculateAutoRotation(item, media);
                    media.RenderTransform = new RotateTransform((_overlayRotation + autoRotation) % 360);
                };

                media.RenderTransform = new RotateTransform((_overlayRotation + autoRotation) % 360);
                _activeOverlayMediaElement = media;
                wrap.Children.Add(media);
                var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
                controls.Children.Add(CreateControlButton("▶", (_, __) => media.Play()));
                controls.Children.Add(CreateControlButton("⏸", (_, __) => media.Pause()));
                controls.Children.Add(CreateControlButton("⏹", (_, __) => media.Stop()));
                wrap.Children.Add(controls);

                OverlayMediaHost.Content = wrap;
                return;
            }

            OverlayMediaHost.Content = new TextBlock
            {
                Text = "Неподдерживаемый формат медиа",
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Button CreateControlButton(string text, RoutedEventHandler handler)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 10, 4) };
            b.Click += handler;
            return b;
        }


        private void CloseOverlay_Click(object sender, RoutedEventArgs e)
        {
            StopActiveOverlayMedia();
            NewsOverlay.Visibility = Visibility.Collapsed;
        }
        private void PrevMedia_Click(object sender, RoutedEventArgs e) { _overlayMediaIndex--; RenderOverlayMedia(); }
        private void NextMedia_Click(object sender, RoutedEventArgs e) { _overlayMediaIndex++; RenderOverlayMedia(); }
        private void RotateMedia_Click(object sender, RoutedEventArgs e) { _overlayRotation = (_overlayRotation + 90) % 360; RenderOverlayMedia(); }

        private sealed class NewsMediaItem
        {
            public string Type { get; set; } = "photo";
            public string Path { get; set; } = "";
            public bool ExpectedPortrait { get; set; }
            public bool IsRemote { get; set; }
            public bool IsVideo => Type == "video";
        }
    }
}
