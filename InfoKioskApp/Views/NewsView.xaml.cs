using InfoKioskApp.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
                stack.Children.Add(new TextBlock { Text = post.Title, FontSize = 24, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
                stack.Children.Add(new TextBlock { Text = $"{post.CreatedAt:dd.MM.yyyy HH:mm} • {post.AuthorLogin}", Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 10) });
                stack.Children.Add(new TextBlock { Text = post.Content, FontSize = 18, TextWrapping = TextWrapping.Wrap });

                if (!string.IsNullOrWhiteSpace(post.VideoUrl))
                {
                    stack.Children.Add(new TextBlock { Text = $"Видео: {post.VideoUrl}", Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.LightBlue });
                }

                card.Child = stack;
                NewsPanel.Children.Add(card);
            }
        }
    }
}
