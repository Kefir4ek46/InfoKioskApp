using InfoKioskApp.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

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
                NewsPanel.Children.Add(new TextBlock { Text = "Пока нет опубликованных новостей.", Foreground = Brushes.Gray, FontSize = 18 });
                return;
            }

            var posts = JsonConvert.DeserializeObject<List<NewsPost>>(File.ReadAllText(path)) ?? [];
            foreach (var post in posts.OrderByDescending(p => p.CreatedAt))
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

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = post.Title, Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
                stack.Children.Add(new TextBlock { Text = $"{post.CreatedAt:dd.MM.yyyy HH:mm} • {post.AuthorLogin}", Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 10) });
                stack.Children.Add(new TextBlock { Text = post.Content, Foreground = Brushes.White, FontSize = 18, TextWrapping = TextWrapping.Wrap });

                AddPhotos(post, stack);
                AddVideo(post, stack);

                card.Child = stack;
                NewsPanel.Children.Add(card);
            }
        }

        private static void AddPhotos(NewsPost post, Panel stack)
        {
            if (post.PhotoFiles == null || post.PhotoFiles.Count == 0)
                return;

            var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var photo in post.PhotoFiles.Where(x => !string.IsNullOrWhiteSpace(x)).Take(6))
            {
                var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "news", "media", photo);
                if (!File.Exists(fullPath)) continue;

                var img = new Image
                {
                    Width = 220,
                    Height = 130,
                    Stretch = Stretch.UniformToFill,
                    Margin = new Thickness(0, 0, 8, 8),
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(fullPath, UriKind.Absolute))
                };
                wrap.Children.Add(img);
            }

            if (wrap.Children.Count > 0)
            {
                stack.Children.Add(new TextBlock { Text = "Фото:", Foreground = Brushes.LightGray, Margin = new Thickness(0, 10, 0, 6) });
                stack.Children.Add(wrap);
            }
        }

        private static void AddVideo(NewsPost post, Panel stack)
        {
            if (!string.IsNullOrWhiteSpace(post.VideoFile))
            {
                var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "news", "media", post.VideoFile);
                if (File.Exists(fullPath))
                {
                    stack.Children.Add(new TextBlock { Text = "Видео:", Foreground = Brushes.LightGray, Margin = new Thickness(0, 10, 0, 6) });
                    stack.Children.Add(new MediaElement
                    {
                        Source = new Uri(fullPath, UriKind.Absolute),
                        LoadedBehavior = MediaState.Play,
                        UnloadedBehavior = MediaState.Stop,
                        Stretch = Stretch.Uniform,
                        Height = 280,
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(post.VideoUrl))
            {
                var text = new TextBlock { Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.LightBlue };
                text.Inlines.Add("Видео: ");
                var link = new Hyperlink(new Run(post.VideoUrl));
                link.Click += (_, __) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(post.VideoUrl) { UseShellExecute = true });
                    }
                    catch { }
                };
                text.Inlines.Add(link);
                stack.Children.Add(text);
            }
        }
    }
}
