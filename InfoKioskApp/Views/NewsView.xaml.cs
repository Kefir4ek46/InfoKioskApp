using InfoKioskApp.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InfoKioskApp.Views
{
    public partial class NewsView : UserControl
    {
        public NewsView()
        {
            InitializeComponent();
            Loaded += (_, __) => LoadNews();
        }

        private void LoadNews()
        {
            NewsPanel.Children.Clear();

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "news", "published.json");
            if (!File.Exists(path))
            {
                NewsPanel.Children.Add(new TextBlock
                {
                    Text = "Пока нет опубликованных новостей.",
                    Foreground = Brushes.Gray,
                    FontSize = 18
                });
                return;
            }

            var posts = JsonConvert.DeserializeObject<List<NewsPost>>(File.ReadAllText(path)) ?? [];
            foreach (var post in posts.OrderByDescending(p => p.CreatedAt))
            {
                NewsPanel.Children.Add(BuildNewsCard(post));
            }
        }

        private static Border BuildNewsCard(NewsPost post)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 40)),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 70)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(14)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });

            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(post.Title) ? "Без названия" : post.Title,
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });
            left.Children.Add(new TextBlock
            {
                Text = $"{post.CreatedAt:dd.MM.yyyy HH:mm} • {post.AuthorLogin}",
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 10)
            });
            left.Children.Add(new TextBlock
            {
                Text = post.Content,
                Foreground = Brushes.White,
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap
            });

            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var right = BuildMediaPanel(post);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            card.Child = grid;
            return card;
        }

        private static UIElement BuildMediaPanel(NewsPost post)
        {
            var panel = new StackPanel();

            var photos = (post.PhotoFiles ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Path.GetFileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string? localVideoPath = null;
            if (!string.IsNullOrWhiteSpace(post.VideoFile))
            {
                var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "news", "media", Path.GetFileName(post.VideoFile));
                if (File.Exists(fullPath))
                    localVideoPath = fullPath;
            }

            if (!string.IsNullOrWhiteSpace(localVideoPath))
            {
                panel.Children.Add(new TextBlock { Text = "Видео", Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 6) });

                var mediaHost = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(18, 18, 24)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(56, 56, 66)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6)
                };

                var media = new MediaElement
                {
                    Source = new Uri(localVideoPath, UriKind.Absolute),
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Stop,
                    Stretch = Stretch.Uniform,
                    Height = 320,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                mediaHost.Child = media;
                panel.Children.Add(mediaHost);

                var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                controls.Children.Add(CreateControlButton("▶ Воспроизвести", (_, __) => media.Play()));
                controls.Children.Add(CreateControlButton("⏸ Пауза", (_, __) => media.Pause()));
                controls.Children.Add(CreateControlButton("⏹ Стоп", (_, __) => media.Stop()));
                panel.Children.Add(controls);
                return panel;
            }

            if (photos.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = photos.Count > 1 ? "Фото" : "Изображение", Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 6) });

                var firstPhoto = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "news", "media", photos[0]);
                if (File.Exists(firstPhoto))
                {
                    panel.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(18, 18, 24)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(56, 56, 66)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(6),
                        Child = new Image
                        {
                            Source = new BitmapImage(new Uri(firstPhoto, UriKind.Absolute)),
                            Height = 260,
                            Stretch = Stretch.Uniform
                        }
                    });
                }

                if (photos.Count > 1)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"+ ещё {photos.Count - 1} фото",
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(0, 8, 0, 0)
                    });
                }

                return panel;
            }

            if (!string.IsNullOrWhiteSpace(post.VideoUrl))
            {
                panel.Children.Add(new TextBlock { Text = "Видео по ссылке", Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 6) });
                panel.Children.Add(CreateControlButton("🎬 Открыть видео", (_, __) => OpenExternal(post.VideoUrl!)));
                return panel;
            }

            panel.Children.Add(new TextBlock { Text = "Медиа не прикреплено", Foreground = Brushes.Gray });
            return panel;
        }

        private static Button CreateControlButton(string text, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            b.Click += onClick;
            return b;
        }

        private static void OpenExternal(string pathOrUrl)
        {
            try
            {
                Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}
