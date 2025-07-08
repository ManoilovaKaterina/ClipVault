using Microsoft.Data.Sqlite;
using ClipboardManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Linq;
using System.Collections.Concurrent;

namespace ClipboardManager.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly string _dbPath;
        private readonly ConcurrentDictionary<string, ClipboardItem> _recentItemsCache = new();
        private const int MAX_CACHE_SIZE = 100;

        public DatabaseService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClipboardManager");

            Directory.CreateDirectory(appDataPath);
            _dbPath = Path.Combine(appDataPath, "clipboard.db");
            _connectionString = $"Data Source={_dbPath};Cache=Shared;";
            //RecreateDatabase();
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Enable WAL mode for better concurrent performance
            var walCommand = connection.CreateCommand();
            walCommand.CommandText = "PRAGMA journal_mode=WAL;";
            walCommand.ExecuteNonQuery();

            // Optimize SQLite settings for performance
            var optimizeCommand = connection.CreateCommand();
            optimizeCommand.CommandText = @"
                PRAGMA synchronous=NORMAL;
                PRAGMA cache_size=10000;
                PRAGMA temp_store=MEMORY;
                PRAGMA mmap_size=268435456;";
            optimizeCommand.ExecuteNonQuery();

            var createTableCommand = connection.CreateCommand();
            createTableCommand.CommandText = @"
            CREATE TABLE IF NOT EXISTS ClipboardItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Content TEXT,
                Timestamp DATETIME NOT NULL,
                DataFormat TEXT NOT NULL,
                IsPinned BOOLEAN NOT NULL DEFAULT 0,
                ImageData BLOB,
                ImageFormat TEXT,
                FilePaths TEXT,
                ContentHash TEXT
            )";
            createTableCommand.ExecuteNonQuery();

            // Create indexes for better query performance
            var createIndexCommand = connection.CreateCommand();
            createIndexCommand.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_timestamp ON ClipboardItems(Timestamp DESC);
                CREATE INDEX IF NOT EXISTS idx_dataformat ON ClipboardItems(DataFormat);
                CREATE INDEX IF NOT EXISTS idx_content_hash ON ClipboardItems(ContentHash);
                CREATE INDEX IF NOT EXISTS idx_pinned_timestamp ON ClipboardItems(IsPinned DESC, Timestamp DESC);";
            createIndexCommand.ExecuteNonQuery();

            // Add ContentHash column if it doesn't exist (for existing databases)
            try
            {
                var addColumnCommand = connection.CreateCommand();
                addColumnCommand.CommandText = "ALTER TABLE ClipboardItems ADD COLUMN ContentHash TEXT;";
                addColumnCommand.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Column already exists, ignore
            }
        }

        public async Task<List<ClipboardItem>> GetAllItemsAsync()
        {
            var items = new List<ClipboardItem>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Content, Timestamp, DataFormat, IsPinned, ImageData, ImageFormat, FilePaths, ContentHash
                FROM ClipboardItems 
                ORDER BY IsPinned DESC, Timestamp DESC";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                try
                {
                    var item = new ClipboardItem
                    {
                        Id = reader.GetInt32("Id"),
                        Content = reader.GetString("Content"),
                        Timestamp = reader.GetDateTime("Timestamp"),
                        DataFormat = reader.GetString("DataFormat"),
                        IsPinned = reader.GetBoolean("IsPinned"),
                        ImageData = reader.IsDBNull("ImageData") ? null : (byte[])reader["ImageData"],
                        ImageFormat = reader.IsDBNull("ImageFormat") ? null : reader.GetString("ImageFormat"),
                        FilePaths = GetFilePathsFromReader(reader)
                    };

                    items.Add(item);

                    // Cache recent items for faster duplicate detection
                    if (items.Count <= MAX_CACHE_SIZE)
                    {
                        var cacheKey = GenerateCacheKey(item);
                        _recentItemsCache.TryAdd(cacheKey, item);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading ClipboardItem from DB: {ex.Message}");
                }
            }

            return items;
        }

        public async Task AddItemAsync(ClipboardItem item)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var contentHash = GenerateContentHash(item);
            var cacheKey = GenerateCacheKey(item);

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO ClipboardItems (Content, Timestamp, DataFormat, IsPinned, ImageData, ImageFormat, FilePaths, ContentHash)
                VALUES (@content, @timestamp, @dataFormat, @isPinned, @imageData, @imageFormat, @filePaths, @contentHash)";

            command.Parameters.AddWithValue("@content", item.Content);
            command.Parameters.AddWithValue("@timestamp", item.Timestamp);
            command.Parameters.AddWithValue("@dataFormat", item.DataFormat);
            command.Parameters.AddWithValue("@isPinned", item.IsPinned);
            command.Parameters.AddWithValue("@contentHash", contentHash);

            string filePathsJsonToSave = "[]";
            if (item.FilePaths != null && item.FilePaths.Any())
            {
                filePathsJsonToSave = JsonSerializer.Serialize(item.FilePaths);
            }

            command.Parameters.AddWithValue("@imageData", item.ImageData ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@imageFormat", item.ImageFormat ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@filePaths", filePathsJsonToSave);

            await command.ExecuteNonQueryAsync();

            // Update cache
            _recentItemsCache.TryAdd(cacheKey, item);

            // Limit cache size
            if (_recentItemsCache.Count > MAX_CACHE_SIZE)
            {
                var oldestKey = _recentItemsCache.Keys.FirstOrDefault();
                if (oldestKey != null)
                {
                    _recentItemsCache.TryRemove(oldestKey, out _);
                }
            }
        }

        public async Task<ClipboardItem> FindDuplicateItemAsync(ClipboardItem item)
        {
            var cacheKey = GenerateCacheKey(item);

            // Check cache first for very recent items
            if (_recentItemsCache.TryGetValue(cacheKey, out var cachedItem))
            {
                return cachedItem;
            }

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var contentHash = GenerateContentHash(item);
            var command = connection.CreateCommand();

            // Use content hash for faster lookups
            command.CommandText = @"
                SELECT Id, Content, Timestamp, DataFormat, IsPinned, ImageData, ImageFormat, FilePaths, ContentHash
                FROM ClipboardItems 
                WHERE ContentHash = @contentHash AND DataFormat = @dataFormat
                ORDER BY Timestamp DESC
                LIMIT 1";

            command.Parameters.AddWithValue("@contentHash", contentHash);
            command.Parameters.AddWithValue("@dataFormat", item.DataFormat);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var foundItem = new ClipboardItem
                {
                    Id = reader.GetInt32("Id"),
                    Content = reader.GetString("Content"),
                    Timestamp = reader.GetDateTime("Timestamp"),
                    DataFormat = reader.GetString("DataFormat"),
                    IsPinned = reader.GetBoolean("IsPinned"),
                    ImageData = reader.IsDBNull("ImageData") ? null : (byte[])reader["ImageData"],
                    ImageFormat = reader.IsDBNull("ImageFormat") ? null : reader.GetString("ImageFormat"),
                    FilePaths = GetFilePathsFromReader(reader)
                };

                // Additional verification for images (since hash might not be perfect)
                if (item.DataFormat == "Image" && foundItem.ImageData != null && item.ImageData != null)
                {
                    if (foundItem.ImageData.Length == item.ImageData.Length)
                    {
                        // Cache the result
                        _recentItemsCache.TryAdd(cacheKey, foundItem);
                        return foundItem;
                    }
                }
                else if (item.DataFormat != "Image")
                {
                    // Cache the result
                    _recentItemsCache.TryAdd(cacheKey, foundItem);
                    return foundItem;
                }
            }

            return null;
        }

        public async Task UpdateTimestampAsync(int id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE ClipboardItems 
                SET Timestamp = @timestamp 
                WHERE Id = @id";
            command.Parameters.AddWithValue("@timestamp", DateTime.Now);
            command.Parameters.AddWithValue("@id", id);

            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteItemAsync(int id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ClipboardItems WHERE Id = @id";
            command.Parameters.AddWithValue("@id", id);

            await command.ExecuteNonQueryAsync();

            // Remove from cache
            var itemToRemove = _recentItemsCache.FirstOrDefault(kvp => kvp.Value.Id == id);
            if (!itemToRemove.Equals(default(KeyValuePair<string, ClipboardItem>)))
            {
                _recentItemsCache.TryRemove(itemToRemove.Key, out _);
            }
        }

        public async Task TogglePinAsync(int id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE ClipboardItems 
                SET IsPinned = NOT IsPinned 
                WHERE Id = @id";
            command.Parameters.AddWithValue("@id", id);

            await command.ExecuteNonQueryAsync();
        }

        public async Task ClearHistoryAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ClipboardItems WHERE IsPinned = 0";
            await command.ExecuteNonQueryAsync();

            // Clear cache
            _recentItemsCache.Clear();
        }

        public async Task DeleteUnpinnedItemsAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ClipboardItems WHERE IsPinned = 0";
            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<ClipboardItem>> SearchItemsAsync(string searchTerm)
        {
            var items = new List<ClipboardItem>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Content, Timestamp, DataFormat, IsPinned, ImageData, ImageFormat, FilePaths, ContentHash
                FROM ClipboardItems 
                WHERE Content LIKE @searchTerm
                ORDER BY IsPinned DESC, Timestamp DESC";

            command.Parameters.AddWithValue("@searchTerm", $"%{searchTerm}%");

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(new ClipboardItem
                {
                    Id = reader.GetInt32("Id"),
                    Content = reader.GetString("Content"),
                    Timestamp = reader.GetDateTime("Timestamp"),
                    DataFormat = reader.GetString("DataFormat"),
                    IsPinned = reader.GetBoolean("IsPinned"),
                    ImageData = reader.IsDBNull("ImageData") ? null : (byte[])reader["ImageData"],
                    ImageFormat = reader.IsDBNull("ImageFormat") ? null : reader.GetString("ImageFormat"),
                    FilePaths = GetFilePathsFromReader(reader)
                });
            }

            return items;
        }

        public void RecreateDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = "DROP TABLE IF EXISTS ClipboardItems";
            dropCommand.ExecuteNonQuery();

            InitializeDatabase();
        }

        private List<string> GetFilePathsFromReader(SqliteDataReader reader)
        {
            if (reader.IsDBNull(reader.GetOrdinal("FilePaths")))
            {
                return new List<string>();
            }

            var filePathsJson = reader.GetString(reader.GetOrdinal("FilePaths"));
            if (string.IsNullOrEmpty(filePathsJson))
            {
                return new List<string>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(filePathsJson);
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Error deserializing FilePaths JSON: {ex.Message}");
                return new List<string>();
            }
        }

        private string GenerateContentHash(ClipboardItem item)
        {
            return item.DataFormat switch
            {
                "Text" => item.Content?.GetHashCode().ToString() ?? "0",
                "File" => string.Join("|", item.FilePaths ?? new List<string>()).GetHashCode().ToString(),
                "Image" => $"{item.Content}_{item.ImageData?.Length ?? 0}".GetHashCode().ToString(),
                _ => "0"
            };
        }

        private string GenerateCacheKey(ClipboardItem item)
        {
            return $"{item.DataFormat}_{GenerateContentHash(item)}";
        }
    }
}