using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InfoKioskApp.Plugins
{
    /// <summary>
    /// Сервис таблицы рекордов для игр-расширений.
    /// Хранит рекорды в data/leaderboards.json:
    /// {
    ///   "snake": [
    ///     { "name": "Иван", "score": 250, "date": "2026-06-23T12:34:56" },
    ///     { "name": "Мария", "score": 180, "date": "2026-06-23T11:00:00" }
    ///   ],
    ///   "game-2048": [ ... ],
    ///   "math-quiz": [ ... ]
    /// }
    ///
    /// Каждая игра имеет свой top-N (по умолчанию 10) — остальные записи
    /// автоматически удаляются при сохранении.
    /// </summary>
    public static class LeaderboardService
    {
        private static string FilePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "data", "leaderboards.json");

        private static readonly object _lock = new();
        private const int MaxScoresPerGame = 10;

        /// <summary>
        /// Загружает всю таблицу рекордов.
        /// </summary>
        public static Dictionary<string, List<LeaderboardEntry>> LoadAll()
        {
            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    if (!File.Exists(FilePath))
                        return new Dictionary<string, List<LeaderboardEntry>>();
                    var json = File.ReadAllText(FilePath);
                    return JsonConvert.DeserializeObject<Dictionary<string, List<LeaderboardEntry>>>(json)
                           ?? new Dictionary<string, List<LeaderboardEntry>>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Leaderboard] LoadAll failed: {ex.Message}");
                    return new Dictionary<string, List<LeaderboardEntry>>();
                }
            }
        }

        /// <summary>
        /// Возвращает топ-N рекордов для конкретной игры (отсортированы по убыванию очков).
        /// </summary>
        public static List<LeaderboardEntry> Get(string gameId)
        {
            var all = LoadAll();
            if (all.TryGetValue(gameId, out var list))
                return list.OrderByDescending(e => e.Score).Take(MaxScoresPerGame).ToList();
            return new List<LeaderboardEntry>();
        }

        /// <summary>
        /// Добавляет новый рекорд. Возвращает обновлённый топ-N.
        /// </summary>
        public static List<LeaderboardEntry> Submit(string gameId, string name, int score)
        {
            lock (_lock)
            {
                var all = LoadAll();
                if (!all.ContainsKey(gameId))
                    all[gameId] = new List<LeaderboardEntry>();

                all[gameId].Add(new LeaderboardEntry
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "Аноним" : name.Trim(),
                    Score = score,
                    Date = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
                });

                // Сортируем по убыванию очков, оставляем только топ-N.
                all[gameId] = all[gameId]
                    .OrderByDescending(e => e.Score)
                    .Take(MaxScoresPerGame)
                    .ToList();

                SaveAll(all);
                return all[gameId];
            }
        }

        /// <summary>
        /// Сбрасывает таблицу рекордов одной игры.
        /// </summary>
        public static bool Reset(string gameId)
        {
            lock (_lock)
            {
                var all = LoadAll();
                if (!all.ContainsKey(gameId)) return false;
                all[gameId].Clear();
                SaveAll(all);
                return true;
            }
        }

        /// <summary>
        /// Сбрасывает все таблицы рекордов (для всех игр).
        /// </summary>
        public static void ResetAll()
        {
            lock (_lock)
            {
                SaveAll(new Dictionary<string, List<LeaderboardEntry>>());
            }
        }

        private static void SaveAll(Dictionary<string, List<LeaderboardEntry>> data)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(FilePath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Leaderboard] SaveAll failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Одна запись в таблице рекордов.
    /// </summary>
    public sealed class LeaderboardEntry
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("score")]
        public int Score { get; set; }

        [JsonProperty("date")]
        public string Date { get; set; }
    }
}
