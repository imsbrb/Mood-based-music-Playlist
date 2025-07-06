using melodicmusic_i1;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.IO;
using System.Data;

namespace melodicmusic_i1  // Make sure this matches your project's namespace
{
    public static class DatabaseHelper
    {
        private static string DbFile = "musicDB.sqlite";
        private static string ConnectionString = $"Data Source={DbFile};Version=3;";

        public static void InitializeDatabase()
        {
            try
            {
                bool needsInit = !File.Exists(DbFile);

                // Create connection and database file if it doesn't exist
                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();

                    if (needsInit)
                    {
                        // Create Songs table
                        using (var cmd = new SQLiteCommand(
                            @"CREATE TABLE IF NOT EXISTS Songs (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Path TEXT UNIQUE NOT NULL,
                            Title TEXT,
                            Artist TEXT,
                            Duration REAL
                        )", conn))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        // Create Favorites table
                        using (var cmd = new SQLiteCommand(
                            @"CREATE TABLE IF NOT EXISTS Favorites (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            SongId INTEGER NOT NULL,
                            FOREIGN KEY (SongId) REFERENCES Songs(Id),
                            UNIQUE(SongId)
                        )", conn))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        // Create Playlists table
                        using (var cmd = new SQLiteCommand(
                            @"CREATE TABLE IF NOT EXISTS Playlists (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name TEXT UNIQUE NOT NULL
                        )", conn))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        // Create PlaylistSongs table
                        using (var cmd = new SQLiteCommand(
                            @"CREATE TABLE IF NOT EXISTS PlaylistSongs (
                            PlaylistId INTEGER NOT NULL,
                            SongId INTEGER NOT NULL,
                            Position INTEGER NOT NULL,
                            PRIMARY KEY (PlaylistId, SongId),
                            FOREIGN KEY (PlaylistId) REFERENCES Playlists(Id),
                            FOREIGN KEY (SongId) REFERENCES Songs(Id)
                        )", conn))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Database initialization error: {ex.Message}",
                    "Database Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }
        public static IDbConnection GetConnection()
        {
            return new SQLiteConnection(ConnectionString);
        }
    }
}
