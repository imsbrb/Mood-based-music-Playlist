using System;
using System.Data;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using NAudio.Wave;
using System.Net;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using System.Threading.Tasks;
using System.Linq;
using NAudio.CoreAudioApi;
using System.Data.Common;
using Dapper;
using System.Data.SQLite;
using System.Reflection.Emit;
using System.Reflection;
using TagLib;  // Add this at the top with other using directives
using Newtonsoft.Json;  // You might need to add this NuGet package
using System.Net.Http;  // Add this for HttpClient and HttpResponseMessage



namespace melodicmusic_i1
{
    public class SearchResult
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string FilePath { get; set; }  // For local files
        public string Url { get; set; }       // For online sources
        public int Index { get; set; }
        public bool IsOnline { get; set; }

        public override string ToString()
        {
            return $"{Title} - {Artist} [{(IsOnline ? "Online" : "Local")}]";
        }
    }
    public partial class Form1 : Form
    {
        // Spotify credentials

        private static string clientId = "bbdc756e1ff74a02bdd32b416e0851a3"; // Replace with your actual Client ID
        private static string clientSecret = "2d55a9bb389c40c5a9dcf7059e15f37e"; // Replace with your actual Client Secret
        private static string redirectUri = "http://127.0.0.1:8888/callback";


        private WaveOutEvent waveOut;
        private AudioFileReader audioFile;
        private List<string> songFiles = new List<string>();
        private List<string> playlist = new List<string>();
        private List<string> favorites = new List<string>();
        private int currentIndex = 0;

        private Panel panelHome, panelSearch, panelLibrary, panelPlaylist, panelNowPlaying;
        private FlowLayoutPanel navBar;
        private System.Windows.Forms.Label songTitleLabel;
        private TrackBar playbackBar;
        private TrackBar volumeBar;
       private ComboBox playlistBox; 

        private System.Windows.Forms.Label volumePercentLabel;
        private System.Windows.Forms.Label playbackTimeLabel;
        private Timer timer;
        private bool isDragging = false;

        // Mood color dictionary
        private readonly Dictionary<string, Tuple<Color, Color, Color, Color>> moodColors = new Dictionary<string, Tuple<Color, Color, Color, Color>>()
        {
            { "Default", new Tuple<Color, Color, Color, Color>(Color.Gray, Color.Silver, Color.Black, Color.White) },
            { "Happy", new Tuple<Color, Color, Color, Color>(Color.DeepPink, Color.LightPink, Color.Black, Color.Black) },
            { "Sad", new Tuple<Color, Color, Color, Color>(Color.CornflowerBlue, Color.DodgerBlue, Color.White, Color.White) },
            { "Energetic", new Tuple<Color, Color, Color, Color>(Color.Red, Color.OrangeRed, Color.Black, Color.Black) },
            { "Angry", new Tuple<Color, Color, Color, Color>(Color.DarkRed, Color.Maroon, Color.White, Color.White) },
            { "Focus", new Tuple<Color, Color, Color, Color>(Color.Green, Color.LightGreen, Color.Black, Color.White) }
        };

        // Spotify API variables
        private string accessToken;
        private SpotifyClient spotifyClient;

        private TreeView playlistTree;
        private Dictionary<TreeNode, List<string>> playlistMap = new Dictionary<TreeNode, List<string>>();
        private System.Windows.Forms.Label statusBarLabel;
        private ProgressBar loadingIndicator;
        private System.Windows.Forms.TextBox searchBox;
       // private ListBox searchResults;
        // Add these declarations
        private List<string> localFiles = new List<string>();
        private List<string> spotifyUris = new List<string>();
        // Add these at class level
        private enum PlayerState { Stopped, Playing, Paused }
        private enum TrackType { Local, Spotify }
        private PlayerState currentPlayerState = PlayerState.Stopped;
        private TrackType currentTrackType = TrackType.Local;
        // Add these class-level declarations(missing in your Form1 class)
        private Panel panelFavorites;
        private ListBox favoritesListBox;  // Not present in your current code
        private ListBox libraryListBox;  // Add this with other declarations
                                  
        private ListBox searchResultsListBox;  // Renamed from searchResults
        private List<SearchResult> searchResults;  // Keep this as is
        private ListBox listBoxSearchResults;
        private Button searchButton;
        private ProgressBar searchProgressBar;
        private System.Windows.Forms.Label lblTrackDetails;
        private PictureBox picAlbumArt;
        // Update all references to searchResults (ListBox) to searchResultsListBox
        public Form1()
        {
            InitializeMyComponent();
            SwitchPanel(panelHome);
            ApplyMoodColors("Default"); // Set default mood (you can change to any of the moods defined)
            this.Load += Form1_Load;
        }
        private async void Form1_Load(object sender, EventArgs e)
        {
            DatabaseHelper.InitializeDatabase();  // Initialize DB first
            LoadSongsFromDatabase();
            LoadFavoritesFromDatabase();
            // Update UI with loaded songs
            UpdateLibraryUI();

            // Force TLS 1.2 and bypass SSL validation (for testing)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
           
            // If the database is empty, show a message
            if (songFiles.Count == 0)
            {
                MessageBox.Show("No songs in your library. Please add songs using the Library tab.",
                    "Library Empty", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            try
            {
                await ConnectToSpotifyAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Critical error: {ex.ToString()}"); // Show full exception details
            }
        }

      
        // Use this improved authentication method
        private async Task AuthenticateWithSpotifyAsync()
        {
            try
            {
                

                // Step 1: Create login request with proper scopes
                var loginRequest = new LoginRequest(
                    new Uri(redirectUri),
                    clientId,
                    LoginRequest.ResponseType.Code
                )
                {
                    Scope = new List<string>
            {
                "user-read-private",
                "user-read-email",
                "playlist-read-private",
                "playlist-read-collaborative",
                "streaming",
                "user-read-playback-state",     // Add this scope
                "user-modify-playback-state"    // Add this scope// Critical for Premium playback permissions

            },
                   
                };

                // Show info message to user
                MessageBox.Show("Launching browser for Spotify authentication.\nPlease log in and authorize the application.",
                    "Spotify Authentication", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Open the authorization URL in browser
                System.Diagnostics.Process.Start(loginRequest.ToUri().ToString());

                // Start HTTPS listener for the callback
                var authCode = await ListenForAuthorizationCodeAsync();

                if (string.IsNullOrEmpty(authCode))
                {
                    MessageBox.Show("Failed to receive authorization code.", "Authentication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                // Exchange code for access token
                var tokenRequest = new AuthorizationCodeTokenRequest(
                    clientId,
                    clientSecret,
                    authCode,
                    new Uri(redirectUri)
                );

                var response = await new OAuthClient().RequestToken(tokenRequest);
                // Save tokens
                accessToken = response.AccessToken;
                // Create Spotify client
                spotifyClient = new SpotifyClient(accessToken);

                // Load user playlists
               // await LoadUserPlaylistsAsync();

                MessageBox.Show("Successfully connected to Spotify!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Authentication failed: {ex.Message}\n\nDetails: {ex.StackTrace}",
                    "Authentication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }

        private async Task<string> ListenForAuthorizationCodeAsync()
        {
            // Create a simple HTTP listener
            var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:8888/");  // Note: just the base URL

            try
            {
                listener.Start();
                
                // Add a console message to debug
                MessageBox.Show("Waiting for Spotify callback...", "Debug", MessageBoxButtons.OK);

                // Set timeout for 2 minutes
                var context = await listener.GetContextAsync();

                // Check parameters
                var code = context.Request.QueryString["code"];
                var state = context.Request.QueryString["state"];
                var error = context.Request.QueryString["error"];

                // Send a response to the browser
                string responseString = "<html><body><h1>Authentication complete</h1><p>You can close this window now.</p></body></html>";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
                // Clean up
                listener.Stop();

                if (error != null)
                {
                    MessageBox.Show($"Authentication error: {error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }

               
                return code;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in HTTP listener: {ex.Message}\n\n{ex.StackTrace}",
                    "Critical Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            
        }

       

        // Call this method in your Form1_Load or from a button click
        private async Task ConnectToSpotifyAsync()
        {
            try
            {
                await AuthenticateWithSpotifyAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error connecting to Spotify: {ex.Message}",
                    "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void InitializeMyComponent()
        {
            this.Text = "Melodic Music Player";
            this.ClientSize = new Size(500, 700);
            this.BackColor = Color.FromArgb(40, 10, 50);
            this.Font = new Font("Segoe UI", 10, FontStyle.Bold);

            // Initialize all panels
            panelHome = CreatePanel();
            panelSearch = CreatePanel();
            panelLibrary = CreatePanel();
            panelPlaylist = CreatePanel();
            panelNowPlaying = CreatePanel();
            panelFavorites = CreatePanel();

            // Set initial visibility (all hidden except home)
            panelHome.Visible = true;
            panelSearch.Visible = panelLibrary.Visible = panelPlaylist.Visible =
                panelNowPlaying.Visible = panelFavorites.Visible = false;
            // Add mood buttons
            AddMoodButtons();

            // Initialize the library panel
            InitializeLibraryPanel();

            // Initialize the search panel
            InitializeSearchPanel();

            // Initialize the now playing panel
            InitializeNowPlayingPanel();

            // Initialize the favorites panel
            InitializeFavoritesPanel();

            // Initialize the playlist panel
            InitializePlaylistPanel();
            // Navigation Bar
            navBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.Black,
                FlowDirection = FlowDirection.LeftToRight
            };

            navBar.Controls.AddRange(new Control[]
            {
        CreateButton("Home", (s, e) => SwitchPanel(panelHome)),
        CreateButton("Search", (s, e) => SwitchPanel(panelSearch)),
        CreateButton("Library", (s, e) => SwitchPanel(panelLibrary)),
        CreateButton("Playlist", (s, e) => SwitchPanel(panelPlaylist)),
        CreateButton("Now", (s, e) => SwitchPanel(panelNowPlaying)),
        CreateButton("Favorites", (s, e) => SwitchPanel(panelFavorites))
            });

            // Add all components to form
            this.Controls.AddRange(new Control[] {
        panelHome,
        panelSearch,
        panelLibrary,
        panelPlaylist,
        panelNowPlaying,
        panelFavorites,
        navBar
    });
            

            // Other initializations
            timer = new Timer { Interval = 500 };
            timer.Tick += Timer_Tick;
            timer.Start();

            statusBarLabel = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Color.Black,
                Text = "Ready"
            };

            loadingIndicator = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 5,
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };

            this.Controls.Add(statusBarLabel);
            this.Controls.Add(loadingIndicator);

            // Initialize song lists
            if (songFiles == null)
                songFiles = new List<string>();

            if (playlist == null)
                playlist = new List<string>();

            if (favorites == null)
                favorites = new List<string>();
        }
        // Add new method to initialize the Search panel
        private void InitializeSearchPanel()
        {
            var searchLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3
            };

            searchLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // Search bar
            searchLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // Search button
            searchLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Results list

            searchBox = new System.Windows.Forms.TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12),
                Text = "Search songs...",
                ForeColor = Color.Gray
            };

            searchButton = new Button
            {
                Text = "Search",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            listBoxSearchResults = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10),
                BackColor = Color.WhiteSmoke,
                ForeColor = Color.Black,
                DisplayMember = "ToString"
            };

            searchProgressBar = new ProgressBar
            {
                Dock = DockStyle.Bottom,
                Height = 5,
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };

            searchLayout.Controls.Add(searchBox, 0, 0);
            searchLayout.Controls.Add(searchButton, 0, 1);
            searchLayout.Controls.Add(listBoxSearchResults, 0, 2);
            searchLayout.Controls.Add(searchProgressBar, 0, 2);

            panelSearch.Controls.Clear();
            panelSearch.Controls.Add(searchLayout);

            // Event handlers
            searchBox.GotFocus += (s, e) =>
            {
                if (searchBox.Text == "Search songs...")
                {
                    searchBox.Text = "";
                    searchBox.ForeColor = Color.Black;
                }
            };

            searchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(searchBox.Text))
                {
                    searchBox.Text = "Search songs...";
                    searchBox.ForeColor = Color.Gray;
                }
            };

            searchButton.Click += async (s, e) => await SearchButton_Click(s, e);

            listBoxSearchResults.DoubleClick += (s, e) =>
            {
                if (listBoxSearchResults.SelectedItem is SearchResult selected)
                {
                    if (selected.IsOnline)
                    {
                        PlaySpotifySong(selected.Url);
                    }
                    else
                    {
                        int index = songFiles.IndexOf(selected.FilePath);
                        if (index >= 0)
                        {
                            currentIndex = index;
                            PlaySong(currentIndex);
                        }
                    }
                }
            };
        }

        // Add new method to initialize the Now Playing panel
        private void InitializeNowPlayingPanel()
        {
            var albumArt = new PictureBox
            {
                Dock = DockStyle.Top,
                Height = 250,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            songTitleLabel = new System.Windows.Forms.Label
            {
                Text = "No song playing",
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };

            // Playback controls
            var playbackPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                ColumnCount = 2,
                RowCount = 1
            };

            playbackPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
            playbackPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

            playbackBar = new TrackBar
            {
                Dock = DockStyle.Fill,
                TickStyle = TickStyle.None,
                Enabled = false
            };

            playbackTimeLabel = new System.Windows.Forms.Label
            {
                Text = "00:00 / 00:00",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            playbackBar.MouseDown += playbackBar_MouseDown;
            playbackBar.MouseUp += playbackBar_MouseUp;

            playbackPanel.Controls.Add(playbackBar, 0, 0);
            playbackPanel.Controls.Add(playbackTimeLabel, 1, 0);

            // Player controls
            var controlsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Height = 60,
                AutoSize = true,
                Padding = new Padding(10),
                Margin = new Padding(10),
                WrapContents = false
            };

            controlsPanel.Controls.AddRange(new Control[]
            {
        //CreateButton("⏮", btnPrevious_Click),
        CreateButton("▶", btnPlay_Click),
        CreateButton("❚❚", btnPause_Click),
        CreateButton("■", btnStop_Click),
        //CreateButton("⏭", btnNext_Click)
            });

            // Volume controls
            var volumePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 60,
                ColumnCount = 2,
                RowCount = 1
            };

            volumePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
            volumePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

            volumeBar = new TrackBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                Value = 70,
                TickStyle = TickStyle.None
            };

            volumePercentLabel = new System.Windows.Forms.Label
            {
                Text = "70%",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            volumeBar.ValueChanged += VolumeBar_ValueChanged;

            volumePanel.Controls.Add(volumeBar, 0, 0);
            volumePanel.Controls.Add(volumePercentLabel, 1, 0);

            // Assemble Now Playing panel
            panelNowPlaying.Controls.Clear();
            panelNowPlaying.Controls.Add(volumePanel);
            panelNowPlaying.Controls.Add(controlsPanel);
            panelNowPlaying.Controls.Add(playbackPanel);
            panelNowPlaying.Controls.Add(songTitleLabel);
            panelNowPlaying.Controls.Add(albumArt);
        }

        // Add new method to initialize the Favorites panel
        private void InitializeFavoritesPanel()
        {
            var favoritesLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2
            };

            favoritesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            favoritesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var backButton = CreateButton("← Back to Library", (s, e) => SwitchPanel(panelLibrary));
            // Player controls
            var controlsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                Height = 60,
                AutoSize = true,
                Padding = new Padding(10),
                Margin = new Padding(10),
                WrapContents = false
            };
            controlsPanel.Controls.AddRange(new Control[]
            {
              CreateButton("★ Unmark", BtnUnmarkFavorite_Click)
            });
            favoritesListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = this.Font,
                BackColor = Color.WhiteSmoke,
                ForeColor = Color.Black
            };

            // Add double-click handler to play favorites
            favoritesListBox.DoubleClick += (s, e) =>
            {
                if (favoritesListBox.SelectedIndex >= 0 && favoritesListBox.SelectedIndex < favorites.Count)
                {
                    string favoritePath = favorites[favoritesListBox.SelectedIndex];
                    int mainIndex = songFiles.IndexOf(favoritePath);

                    if (mainIndex >= 0)
                    {
                        currentIndex = mainIndex;
                        PlaySong(currentIndex);
                    }
                }
            };

            favoritesLayout.Controls.Add(backButton, 0, 0);
            favoritesLayout.Controls.Add(favoritesListBox, 0, 1);

            panelFavorites.Controls.Clear();
            panelFavorites.Controls.Add(favoritesLayout);
        }

        // Add new method to initialize the Playlist panel
        private void InitializePlaylistPanel()
        {
            var playlistLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2
            };

            playlistLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            playlistLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            playlistBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            playlistTree = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12),
                BackColor = Color.WhiteSmoke,
                ShowPlusMinus = true,
                ShowRootLines = true
            };

            // Set up event handlers
            playlistBox.SelectedIndexChanged += PlaylistBox_SelectedIndexChanged;
            playlistTree.NodeMouseDoubleClick += PlaylistTree_NodeMouseDoubleClick;

            playlistLayout.Controls.Add(playlistBox, 0, 0);
            playlistLayout.Controls.Add(playlistTree, 0, 1);

            panelPlaylist.Controls.Clear();
            panelPlaylist.Controls.Add(playlistLayout);
        }
        // Add this method to correctly update the library UI
        private void UpdateLibraryUI()
        {
            // Find the ListBox in the library panel
            var libraryListBox = FindControlRecursive<ListBox>(panelLibrary);
            if (libraryListBox != null)
            {
                libraryListBox.Items.Clear();
                foreach (var songPath in songFiles)
                {
                    libraryListBox.Items.Add(Path.GetFileName(songPath));
                }

                // First remove any existing event handlers to avoid duplicates
                libraryListBox.DoubleClick -= LibraryListBox_DoubleClick;
                // Then add the event handler
                libraryListBox.DoubleClick += LibraryListBox_DoubleClick;
            }
            // Update the favorite songs list too
            RefreshFavoritesList();
        }

        // Define the event handler as a separate method
        private void LibraryListBox_DoubleClick(object sender, EventArgs e)
        {
            var libraryListBox = sender as ListBox;
            if (libraryListBox != null && libraryListBox.SelectedIndex >= 0)
            {
                currentIndex = libraryListBox.SelectedIndex;
                PlaySong(currentIndex);
            }
        }
        // Fix for library panel initialization
        private void InitializeLibraryPanel()
        {
            // Create a table layout for the library panel
            var libraryLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2
            };

            libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            libraryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Create a ListBox for the library
            libraryListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = this.Font,
                BackColor = Color.WhiteSmoke,
                ForeColor = Color.Black,
                Name = "libraryListBox"
            };

            // Add double-click event handler to play songs
            libraryListBox.DoubleClick += (s, e) =>
            {
                if (libraryListBox.SelectedIndex >= 0)
                {
                    currentIndex = libraryListBox.SelectedIndex;
                    PlaySong(currentIndex);
                }
            };

            // Create controls for the library
            var libraryControls = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(5)
            };

            libraryControls.Controls.AddRange(new Control[] {
        CreateButton("+ Add Songs", BtnAddSongs_Click),
        CreateButton("♥ Favorite", BtnMarkFavorite_Click),
        CreateButton("★ Unmark", BtnUnmarkFavorite_Click),
        CreateButton("Favorites", BtnViewFavorites_Click)
    });

            // Add controls to the layout
            libraryLayout.Controls.Add(libraryControls, 0, 0);
            libraryLayout.Controls.Add(libraryListBox, 0, 1);

            // Add the layout to the panel
            panelLibrary.Controls.Clear();
            panelLibrary.Controls.Add(libraryLayout);
        }

        // Add this method to fix the add songs functionality
        private void BtnAddSongs_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Multiselect = true;
                ofd.Filter = "Audio Files|*.mp3;*.wav;*.flac|All Files|*.*";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    int addedCount = 0;

                    foreach (var file in ofd.FileNames)
                    {
                        if (System.IO.File.Exists(file))
                        {
                            try
                            {
                                SaveSongToDatabase(file);
                                addedCount++;
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Error adding {Path.GetFileName(file)}: {ex.Message}",
                                    "Add Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }

                    // Reload songs from the database
                    LoadSongsFromDatabase();

                    // Update the UI
                    UpdateLibraryUI();

                    MessageBox.Show($"Added {addedCount} songs to your library.",
                        "Songs Added", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        // Fix the toggle favorite functionality
        private void ToggleFavorite(bool isFavorite)
        {
            try
            {
                if (currentIndex < 0 || currentIndex >= songFiles.Count)
                {
                    MessageBox.Show("Please select a song first.",
                        "No Song Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string songPath = songFiles[currentIndex];

                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    int songId = 0;

                    // Get the song ID, or insert it if it doesn't exist
                    var idResult = conn.ExecuteScalar<int?>(
                        "SELECT Id FROM Songs WHERE Path = @Path",
                        new { Path = songPath });

                    if (idResult.HasValue)
                    {
                        songId = idResult.Value;
                    }
                    else
                    {
                        // Song doesn't exist in database, insert it
                        SaveSongToDatabase(songPath);

                        // Now get the ID
                        songId = conn.ExecuteScalar<int>(
                            "SELECT Id FROM Songs WHERE Path = @Path",
                            new { Path = songPath });
                    }

                    if (isFavorite)
                    {
                        // Mark as favorite
                        conn.Execute(
                            "INSERT OR IGNORE INTO Favorites(SongId) VALUES(@SongId)",
                            new { SongId = songId });

                        MessageBox.Show("Song added to favorites.",
                            "Favorite Added", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        // Remove from favorites
                        var result = conn.Execute(
                            "DELETE FROM Favorites WHERE SongId = @SongId",
                            new { SongId = songId });

                        if (result > 0)
                        {
                            MessageBox.Show("Song removed from favorites.",
                                "Favorite Removed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("This song is not in your favorites.",
                                "Not a Favorite", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }

                    // Reload favorites from database
                    LoadFavoritesFromDatabase();

                    // Update UI
                    RefreshFavoritesList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error managing favorites: {ex.Message}",
                    "Favorites Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void AddMoodButtons()
        {
           
            var moodPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(10),
                WrapContents = false  // Ensure buttons don't wrap around
            };

            foreach (var mood in moodColors.Keys)
            {
                var moodButton = new Button
                {
                    Text = mood,
                    Width = 100,
                    Height = 40,
                    BackColor = Color.Gray, // Default button color
                    ForeColor = Color.White, // Default text color
                    FlatStyle = FlatStyle.Flat
                };

                moodButton.Click += async (sender, e) =>
                {
                    Button btn = sender as Button;
                    ApplyMoodColors(mood);
                    btn.BackColor = moodColors[mood].Item2;
                    btn.ForeColor = moodColors[mood].Item3;

                    if (spotifyClient == null)
                    {
                        MessageBox.Show("Authenticating now...");
                        await AuthenticateWithSpotifyAsync();
                    }

                    if (spotifyClient == null)
                    {
                        MessageBox.Show("Authentication failed. Please try again.");
                        return;
                    }

                    await SearchPlaylistsByMoodAsync(mood);
                    SwitchPanel(panelPlaylist);
                };
                // Add the button to the mood panel
                moodPanel.Controls.Add(moodButton);
            }

            // Add the mood panel to the home panel only if it's not already added
            panelHome.Controls.Add(moodPanel);
        }

        private void ApplyMoodColors(string mood)
        {
            // Check if the selected mood exists in the dictionary
            if (!moodColors.ContainsKey(mood)) return;

            // Retrieve the colors associated with the selected mood
            var colors = moodColors[mood];

            // Change the form's background color
            this.BackColor = colors.Item1;  // Form background color

            // Change colors for all buttons
            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is Button button)
                {
                    button.BackColor = colors.Item2;  // Button background color
                    button.ForeColor = colors.Item3;  // Button text color
                }
            }

            // Change colors for all labels
            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is System.Windows.Forms.Label label)
                {
                    label.ForeColor = colors.Item4;  // Label text color
                }
            }

            // Change colors for all panels
            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is Panel panel)
                {
                    panel.BackColor = colors.Item1;  // Panel background color
                }
            }
        }

        

        private Panel CreatePanel()
        {
            return new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        }

        private void SwitchPanel(Control panelToShow)
        {
            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is Panel && ctrl != navBar)
                    ctrl.Visible = false;
            }
            panelToShow.Visible = true;
            panelToShow.BringToFront();

        }
        // Should update both library and favorites listboxes
        private void RefreshFavoritesList()
        {
            // Update favorites panel
            favoritesListBox.Items.Clear();
            favoritesListBox.Items.AddRange(favorites
                .Select(Path.GetFileName)
                .Cast<object>()
                .ToArray());

            // Update library panel
            var libraryListBox = panelLibrary.Controls.OfType<ListBox>().FirstOrDefault();
            if (libraryListBox != null)
            {
                libraryListBox.Items.Clear();
                libraryListBox.Items.AddRange(songFiles
                    .Select(Path.GetFileName)
                    .Cast<object>()
                    .ToArray());
            }
        }
        private async void btnPlay_Click(object sender, EventArgs e)
        {
            if (playlistBox.Items.Count == 0 || currentIndex < 0) return;

            try
            {
                if (currentPlayerState == PlayerState.Paused)
                {
                    // Resume playback
                    if (currentTrackType == TrackType.Local)
                    {
                        waveOut?.Play();
                        currentPlayerState = PlayerState.Playing;
                    }
                    else
                    {
                        await spotifyClient.Player.ResumePlayback();
                        currentPlayerState = PlayerState.Playing;
                    }
                }
                else
                {
                    // Start new playback
                    PlaySong(currentIndex);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Play error: {ex.Message}");
            }
        }
        private async void btnResume_Click(object sender, EventArgs e)
        {
            try
            {
                if (currentTrackType == TrackType.Local)
                {
                    if (waveOut != null && waveOut.PlaybackState == PlaybackState.Paused)
                    {
                        waveOut.Play();
                    }
                }
                else
                {
                    await spotifyClient.Player.ResumePlayback();
                }
                currentPlayerState = PlayerState.Playing;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Resume error: {ex.Message}");
            }
        }
        private async void btnPause_Click(object sender, EventArgs e)
        {
            try
            {
                if (currentTrackType == TrackType.Local)
                {
                    waveOut?.Pause();
                }
                else
                {
                    await spotifyClient.Player.PausePlayback();
                }
                currentPlayerState = PlayerState.Paused;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Pause error: {ex.Message}");
            }
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            try
            {
                if (currentTrackType == TrackType.Local)
                {
                    waveOut?.Stop();
                    audioFile?.Dispose();
                    audioFile = null;
                }
                else
                {
                    await spotifyClient.Player.PausePlayback();
                }

                currentPlayerState = PlayerState.Stopped;
                playbackBar.Value = 0;
                playbackTimeLabel.Text = "00:00 / 00:00";
                songTitleLabel.Text = "Stopped";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Stop error: {ex.Message}");
            }
        }
        private void SaveSongToDatabase(string path)
        {
            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    // Explicitly use TagLib.File
                    using (var file = TagLib.File.Create(path))
                    {
                        conn.Execute(
                            @"INSERT OR IGNORE INTO Songs(Path, Title, Artist, Duration) 
                    VALUES(@Path, @Title, @Artist, @Duration)",
                            new
                            {
                                Path = path,
                                Title = file.Tag.Title ?? Path.GetFileNameWithoutExtension(path),
                                Artist = file.Tag.FirstPerformer ?? "Unknown Artist",
                                Duration = file.Properties.Duration.TotalSeconds
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving {System.IO.Path.GetFileName(path)}: {ex.Message}");
            }
        }

        // Update favorite management
        private void BtnMarkFavorite_Click(object sender, EventArgs e)
        {
            ToggleFavorite(true);
            LoadFavoritesFromDatabase();
            RefreshFavoritesList();
        }

        private void BtnUnmarkFavorite_Click(object sender, EventArgs e)
        {
            ToggleFavorite(false);
            LoadFavoritesFromDatabase();
            RefreshFavoritesList();
        }
        
        private void DisplayErrorMessage(string message)
        {
            // Instead of MessageBox, use a non-blocking notification
            statusBarLabel.Text = $"Error: {message}";
        }

        private void BtnViewFavorites_Click(object sender, EventArgs e)
        {
            if (favorites.Count == 0)
            {
                MessageBox.Show("No favorites yet.");
            }
            else
            {
                SwitchPanel(panelFavorites);
            }
        }

        private void PlaylistBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (playlistBox.SelectedIndex >= 0)
            {
                currentIndex = playlistBox.SelectedIndex;
                PlaySong(currentIndex);
            }
        }
        // Helper method to find a control by type
        private T FindControlRecursive<T>(Control container) where T : Control
        {
            foreach (Control ctrl in container.Controls)
            {
                if (ctrl is T)
                    return (T)ctrl;

                T result = FindControlRecursive<T>(ctrl);
                if (result != null)
                    return result;
            }

            return null;
        }

        // Fix for PlaySong to handle all cases properly
        private async void PlaySong(int index)
        {
            try
            {
                if (index < 0 || index >= songFiles.Count)
                {
                    MessageBox.Show("Invalid song index.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Update the current index
                currentIndex = index;
                var pathOrUri = songFiles[index];

                // Determine if local file or Spotify URI
                if (pathOrUri.StartsWith("spotify:track:"))
                {
                    currentTrackType = TrackType.Spotify;
                    await PlaySpotifySong(pathOrUri);
                }
                else if (System.IO.File.Exists(pathOrUri))
                {
                    currentTrackType = TrackType.Local;
                    PlayOfflineSong(pathOrUri);
                }
                else
                {
                    MessageBox.Show($"File not found: {pathOrUri}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    // Remove from database if the file doesn't exist
                    using (var conn = DatabaseHelper.GetConnection())
                    {
                        conn.Open();
                        conn.Execute("DELETE FROM Songs WHERE Path = @Path", new { Path = pathOrUri });
                    }

                    // Refresh lists
                    LoadSongsFromDatabase();
                    UpdateLibraryUI();
                    return;
                }

                // Update UI
                playbackBar.Enabled = true;
                currentPlayerState = PlayerState.Playing;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error playing song: {ex.Message}", "Playback Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void PlayOfflineSong(string path)
        {
            try
            {
                // Ensure the file exists before attempting to play it
                if (!System.IO.File.Exists(path))
                {
                    MessageBox.Show($"File not found: {path}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Properly dispose of previous resources
                if (waveOut != null)
                {
                    waveOut.Stop();
                    waveOut.Dispose();
                    waveOut = null;
                }

                if (audioFile != null)
                {
                    audioFile.Dispose();
                    audioFile = null;
                }

                // Initialize new audio playback
                audioFile = new AudioFileReader(path);
                waveOut = new WaveOutEvent();
                waveOut.Init(audioFile);

                // Set volume based on the volume bar
                audioFile.Volume = volumeBar.Value / 100f;

                // Update UI
                songTitleLabel.Text = "Playing: " + Path.GetFileName(path);
                playbackBar.Maximum = (int)audioFile.TotalTime.TotalSeconds;
                playbackBar.Enabled = true;

                // Start playback
                waveOut.Play();
                currentPlayerState = PlayerState.Playing;

                // Make sure timer is running
                timer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error playing song: {ex.Message}\n\nStack trace: {ex.StackTrace}", "Playback Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private async Task PlaySpotifySong(string uri)
        {
            try
            {
                ShowLoadingIndicator(true);

                // Ensure spotifyClient is initialized
                if (spotifyClient == null)
                {
                    MessageBox.Show("Not authenticated with Spotify. Attempting to log in...");
                    await AuthenticateWithSpotifyAsync();
                    if (spotifyClient == null)
                    {
                        MessageBox.Show("Authentication failed. Please try again.");
                        ShowLoadingIndicator(false);
                        return;
                    }
                }

                // Validate track URI
                if (string.IsNullOrEmpty(uri) || !uri.StartsWith("spotify:track:"))
                {
                    MessageBox.Show("Invalid Spotify track URI.");
                    ShowLoadingIndicator(false);
                    return;
                }

                // Check for available devices
                var devices = await spotifyClient.Player.GetAvailableDevices();
                if (devices.Devices == null || devices.Devices.Count == 0)
                {
                    MessageBox.Show("No active Spotify devices found. Please open Spotify on a device.");
                    ShowLoadingIndicator(false);
                    return;
                }

                // Select a device (prefer active one)
                var device = devices.Devices.FirstOrDefault(d => d.IsActive) ?? devices.Devices.First();

                // Transfer playback to selected device
                await spotifyClient.Player.TransferPlayback(
                    new PlayerTransferPlaybackRequest(new List<string> { device.Id }) { Play = true }
                );

                // Resume playback
                var playbackRequest = new PlayerResumePlaybackRequest
                {
                    Uris = new List<string> { uri },
                    DeviceId = device.Id
                };
                await spotifyClient.Player.ResumePlayback(playbackRequest);

                // Get track details for display
                var trackId = uri.Split(':').Last();
                var track = await spotifyClient.Tracks.Get(trackId);
                if (track != null)
                {
                    songTitleLabel.Text = $"Playing: {track.Name} by {string.Join(", ", track.Artists.Select(a => a.Name))} (on {device.Name})";
                }
                else
                {
                    songTitleLabel.Text = "Playing: Unknown Track";
                }
            }
            catch (SpotifyAPI.Web.APIException apiEx)
            {
                MessageBox.Show($"Spotify API error: {apiEx.Message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error playing Spotify track: {ex.Message}");
            }
            finally
            {
                ShowLoadingIndicator(false);
            }
        }


        private void VolumeBar_ValueChanged(object sender, EventArgs e)
        {
            if (audioFile != null)
            {
                audioFile.Volume = volumeBar.Value / 100f;
                volumePercentLabel.Text = $"{volumeBar.Value}%";
            }

            // For Spotify volume control (if needed)
            if (currentTrackType == TrackType.Spotify && spotifyClient != null)
            {
                _ = spotifyClient.Player.SetVolume(new PlayerVolumeRequest(volumeBar.Value));
            }
        }
        private void ShowLoadingIndicator(bool show)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => ShowLoadingIndicator(show)));
                return;
            }

            loadingIndicator.Visible = show;
            Cursor = show ? Cursors.WaitCursor : Cursors.Default;
        }


        private async void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (currentTrackType == TrackType.Local)
                {
                    if (audioFile != null && !isDragging)
                    {
                        playbackBar.Maximum = (int)audioFile.TotalTime.TotalSeconds;
                        playbackBar.Value = Math.Min(playbackBar.Maximum, (int)audioFile.CurrentTime.TotalSeconds);
                        playbackTimeLabel.Text = $"{audioFile.CurrentTime:mm\\:ss} / {audioFile.TotalTime:mm\\:ss}";
                    }
                }
                else
                {
                    var playback = await spotifyClient.Player.GetCurrentPlayback();
                    if (playback?.IsPlaying == true && playback.Item is FullTrack track)
                    {
                        playbackBar.Maximum = (int)track.DurationMs / 1000;
                        playbackBar.Value = (int)playback.ProgressMs / 1000;
                        playbackTimeLabel.Text =
                            $"{TimeSpan.FromMilliseconds(playback.ProgressMs):mm\\:ss} / " +
                            $"{TimeSpan.FromMilliseconds(track.DurationMs):mm\\:ss}";
                    }
                }
            }
            catch { /* Handle exceptions silently in timer */ }
        }


        private void playbackBar_MouseDown(object sender, MouseEventArgs e)
        {
            isDragging = true;
        }

        private async void playbackBar_MouseUp(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                isDragging = false;
                try
                {
                    if (currentTrackType == TrackType.Local && audioFile != null)
                    {
                        audioFile.CurrentTime = TimeSpan.FromSeconds(playbackBar.Value);
                    }
                    else if (currentTrackType == TrackType.Spotify)
                    {
                        // Convert seconds to milliseconds
                        long positionMs = (long)(playbackBar.Value * 1000);
                        await spotifyClient.Player.SeekTo(new PlayerSeekToRequest(positionMs));
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Seek error: {ex.Message}");
                }
            }
        }
        private async void btnNext_Click(object sender, EventArgs e)
        {
            try
            {
                if (currentTrackType == TrackType.Local)
                {
                    currentIndex = (currentIndex + 1) % playlistBox.Items.Count;
                    PlaySong(currentIndex);
                }
                else
                {
                    await spotifyClient.Player.SkipNext();
                    // Update UI after short delay
                    await Task.Delay(1000);
                    await UpdateSpotifyPlaybackInfo();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Next track error: {ex.Message}");
            }
        }

        private async void btnPrevious_Click(object sender, EventArgs e)
        {
            try
            {
                if (currentTrackType == TrackType.Local)
                {
                    currentIndex = (currentIndex - 1 + playlistBox.Items.Count) % playlistBox.Items.Count;
                    PlaySong(currentIndex);
                }
                else
                {
                    await spotifyClient.Player.SkipPrevious();
                    await Task.Delay(1000);
                    await UpdateSpotifyPlaybackInfo();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Previous track error: {ex.Message}");
            }
        }

        private async Task UpdateSpotifyPlaybackInfo()
        {
            try
            {
                var playback = await spotifyClient.Player.GetCurrentPlayback();
                if (playback?.Item is FullTrack track)
                {
                    songTitleLabel.Text = $"Playing: {track.Name} by {string.Join(", ", track.Artists.Select(a => a.Name))}";
                }
            }
            catch { /* Handle exceptions */ }
        }

        private Button CreateButton(string text, EventHandler handler)
        {
            var btn = new Button
            {
                Text = text,
                Width = 90,
                Height = 40,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.Transparent,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };

            // Hover effect: changes background color on hover
            btn.MouseEnter += (s, e) =>
            {
                btn.BackColor = Color.WhiteSmoke;  // Lighter shade on hover
                btn.ForeColor = Color.Black;
            };

            btn.MouseLeave += (s, e) =>
            {
                btn.BackColor = Color.Transparent;  // Default color
                btn.ForeColor = Color.Black;
            };

            btn.Click += handler;  // Attach the event handler for button click
            return btn;
        }


        private async Task SearchPlaylistsByMoodAsync(string mood)
        {
            // Show the loading indicator
            ShowLoadingIndicator(true);

            if (spotifyClient == null)
            {
                MessageBox.Show("Please log in to Spotify first.", "Not authenticated");
                ShowLoadingIndicator(false); // Hide loading indicator after message
                return;
            }

            string moodKeyword = mood.ToLower();
            try
            {
                var searchRequest = new SearchRequest(SearchRequest.Types.Playlist, moodKeyword);
                var searchResponse = await spotifyClient.Search.Item(searchRequest);

                var playlists = searchResponse.Playlists?.Items;
                if (playlists == null || playlists.Count == 0)
                {
                    DisplayErrorMessage($"No playlists found for mood '{moodKeyword}'.");
                    ShowLoadingIndicator(false);
                    return;
                }

                playlistTree.Nodes.Clear(); // Clear existing nodes
                //loadedPlaylistIds.Clear(); // Reset tracked playlists


                // Load only the top 10 playlists
                foreach (var playlist in playlists.Take(10))
                {
                    if (playlist == null)
                    {
                        continue; // Skip if playlist is null
                    }
                    // Check if playlist.Name is null or empty
                    if (string.IsNullOrEmpty(playlist.Name))
                    {
                        continue; // Skip if the playlist name is invalid
                    }
                    var playlistNode = new TreeNode(playlist.Name);
                    playlistTree.Nodes.Add(playlistNode);

                    try
                    {
                        // Fetch and add tracks for each playlist
                        var fullPlaylist = await spotifyClient.Playlists.Get(playlist.Id);
                        if (fullPlaylist?.Tracks?.Items != null)
                        {
                            var tracks = fullPlaylist.Tracks.Items
                                .Where(t => t?.Track is FullTrack)
                                .Select(t => ((FullTrack)t.Track).Uri) // Store URIs instead of names
                                .Where(uri => !string.IsNullOrEmpty(uri))
                                .Take(15)
                                .ToList();

                            AddPlaylistWithSongs(playlistNode, tracks); // Add songs to the node
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log or handle individual playlist errors but continue with others
                        playlistNode.Text += " (Error loading tracks)";
                        
                   }
                }
            }
            catch (SpotifyAPI.Web.APIException apiEx)
            {
                MessageBox.Show($"Spotify API error: {apiEx.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching playlists: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Hide the loading indicator once the process is complete
                ShowLoadingIndicator(false);
            }
        }



        private void AddPlaylistWithSongs(TreeNode playlistNode, List<string> trackUris)
        {
            playlistMap[playlistNode] = trackUris;

            // Clear existing nodes
            playlistNode.Nodes.Clear();

            foreach (var uri in trackUris.Take(25)) // Limit to top 25 tracks
{
    var trackName = GetTrackNameFromUri(uri); // Implement this method
    playlistNode.Nodes.Add(new TreeNode(trackName) { Tag = uri });
}

            //playlistTree.Nodes.Add(playlistNode);
        }
        private string GetTrackNameFromUri(string uri)
        {
            try
            {
                var trackId = uri.Split(':').Last();
                var track = spotifyClient.Tracks.Get(trackId).Result;
                return track.Name;
            }
            catch
            {
                return "Unknown Track";
            }
        }

        private void PlaylistTree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            // Add this to your playlistTree initialization
            playlistTree.NodeMouseDoubleClick += (sendr, se) =>
            {
                if (e.Node?.Tag != null && e.Node.Tag.ToString().StartsWith("spotify:track:"))
                {
                    PlaySong(songFiles.FindIndex(p => p == e.Node.Tag.ToString()));
                }
                else if (e.Node?.Tag != null)
                {
                    PlaySong(songFiles.FindIndex(p => p == e.Node.Tag.ToString()));
                }
            };
            if (e.Node.Tag == null || songFiles == null) return;

            // Prevent reloading the same playlist
            if (playlistMap.ContainsKey(e.Node))
            {
                // Existing logic for playlist nodes
                if (songFiles.SequenceEqual(playlistMap[e.Node]))
                    return;

                songFiles = playlistMap[e.Node];
                currentIndex = 0;
                PlaySong(currentIndex);
            }
            else if (e.Node.Parent != null && playlistMap.ContainsKey(e.Node.Parent))
            {
                // Handle SONG nodes by using their parent playlist node
                var parentPlaylist = e.Node.Parent;
                int selectedIndex = parentPlaylist.Nodes.IndexOf(e.Node);

                if (selectedIndex >= 0 && selectedIndex < playlistMap[parentPlaylist].Count)
                {
                    songFiles = playlistMap[parentPlaylist];
                    currentIndex = selectedIndex;
                    PlaySong(currentIndex);
                }
            }
            else
            {
                MessageBox.Show("Invalid selection.");
            }

            // Check if the clicked node is a playlist node
            if (e.Node.Tag?.ToString() == "PLAYLIST")
            {
                if (playlistMap.ContainsKey(e.Node))
                {
                    var songs = playlistMap[e.Node];
                    if (songs.Count > 0)
                    {
                        songFiles = songs;
                        currentIndex = 0; // Start playing the first song of the playlist
                        PlaySong(currentIndex); // Call the method to play the song
                    }
                    else
                    {
                        MessageBox.Show("This playlist is empty!"); // Handle empty playlist
                    }
                }
            }
            // Check if the clicked node is a song node
            else if (e.Node.Tag?.ToString() == "SONG")
            {
                // Find the parent playlist node (because the song is inside the playlist)
                var parentPlaylist = e.Node.Parent;
                if (parentPlaylist != null && playlistMap.ContainsKey(parentPlaylist))
                {
                    // Get the song index within the parent playlist
                    int selectedIndex = parentPlaylist.Nodes.IndexOf(e.Node);
                    if (selectedIndex >= 0 && selectedIndex < playlistMap[parentPlaylist].Count)
                    {
                        songFiles = playlistMap[parentPlaylist]; // Set song list to this playlist's songs
                        currentIndex = selectedIndex; // Set the current index to the selected song
                        PlaySong(currentIndex); // Call the method to play the song
                    }
                }
            }
        }


        private void UpdatePlaylistUI(Action updateAction)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdatePlaylistUI(updateAction)));
            }
            else
            {
                updateAction.Invoke();
            }
        }
      
        public class SongItem
        {
            public string DisplayText { get; set; }
            public int Index { get; set; }

            public override string ToString()
            {
                return DisplayText;
            }
        }

        // Updated SearchBox_TextChanged handler
        private async void SearchBox_TextChanged(object sender, EventArgs e)
        {
            var query = searchBox.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(query))
            {
                listBoxSearchResults.Items.Clear();
                return;
            }

            // Get both local and online results
            searchResults = await SearchSongsAsync(query);

            // Update the ListBox
            listBoxSearchResults.Items.Clear();
            listBoxSearchResults.Items.AddRange(searchResults.ToArray());
        }

        // Selection changed handler
        private void ListBoxSearchResults_SelectedIndexChanged(object sender, EventArgs e)
{
    // Clear previous details
    lblTrackDetails.Text = "";
    picAlbumArt.Image = SystemIcons.Information.ToBitmap(); // Default icon

    if (listBoxSearchResults.SelectedItem is SearchResult selected)
    {
        try
        {
            if (selected.IsOnline)
            {
                lblTrackDetails.Text = $"Online Track:\n{selected.Title}\nby {selected.Artist}";
                picAlbumArt.Image = SystemIcons.Shield.ToBitmap(); // Indicates online track
            }
            else
            {
                using (var file = TagLib.File.Create(selected.FilePath))
                {
                    string duration = file.Properties.Duration.ToString(@"mm\:ss");
                    lblTrackDetails.Text = $"Local Track:\n{file.Tag.Title}\nby {file.Tag.FirstPerformer}\nDuration: {duration}";
                    
                    
                }
            }
        }
        catch (FileNotFoundException)
        {
            lblTrackDetails.Text = "Error: File not found!";
            picAlbumArt.Image = SystemIcons.Error.ToBitmap();
        }
        catch (Exception ex)
        {
            lblTrackDetails.Text = $"Error: {ex.Message}";
            picAlbumArt.Image = SystemIcons.Error.ToBitmap();
        }
    }
}


        // Add these methods for database operations

        private void LoadSongsFromDatabase()
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                songFiles = conn.Query<string>("SELECT Path FROM Songs").ToList();
                playlistBox.Items.Clear();
                playlistBox.Items.AddRange(songFiles.Select(Path.GetFileName).ToArray());
            }
        }

    
        private void LoadFavoritesFromDatabase()
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                favorites = conn.Query<string>(
                    @"SELECT s.Path 
                  FROM Favorites f
                  JOIN Songs s ON f.SongId = s.Id").ToList();
            }
        }

        private async Task SearchButton_Click(object sender, EventArgs e)
        {
            string searchQuery = searchBox.Text.Trim();
            if (string.IsNullOrEmpty(searchQuery))
            {
                listBoxSearchResults.Items.Clear();
                return;
            }

            searchProgressBar.Visible = true;
            searchButton.Enabled = false;

            try
            {
                var results = await SearchSongsAsync(searchQuery);
                searchResults = results;
                listBoxSearchResults.Items.Clear();
                listBoxSearchResults.Items.AddRange(results.ToArray());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during search: {ex.Message}", "Search Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                searchProgressBar.Visible = false;
                searchButton.Enabled = true;
            }
        }

        private async Task<List<SearchResult>> SearchSongsAsync(string query)
        {
            var localSearchTask = Task.Run(() => SearchLocalSongs(query));
            var onlineSearchTask = SearchOnlineSongsAsync(query);

            await Task.WhenAll(localSearchTask, onlineSearchTask);

            return localSearchTask.Result.Concat(onlineSearchTask.Result).ToList();
        }


        private List<SearchResult> SearchLocalSongs(string query)
        {
            query = query.ToLower();
            return songFiles
                .Where(path => Path.GetFileNameWithoutExtension(path).ToLower().Contains(query))
                .Select(path => new SearchResult
                {
                    Title = Path.GetFileNameWithoutExtension(path),
                    Artist = "Local Artist", // Replace with actual metadata if available
                    FilePath = path,
                    IsOnline = false
                })
                .ToList();
        }


        private async Task<List<SearchResult>> SearchOnlineSongsAsync(string query)
        {
            if (spotifyClient == null)
            {
                return new List<SearchResult>();
            }

            try
            {
                var searchRequest = new SearchRequest(SearchRequest.Types.Track, query);
                var searchResponse = await spotifyClient.Search.Item(searchRequest);
                return searchResponse.Tracks?.Items
                    .Where(t => t is FullTrack)
                    .Select(t => new SearchResult
                    {
                        Title = ((FullTrack)t).Name,
                        Artist = string.Join(", ", ((FullTrack)t).Artists.Select(a => a.Name)),
                        Url = ((FullTrack)t).Uri,
                        IsOnline = true
                    })
                    .ToList() ?? new List<SearchResult>();
            }
            catch
            {
                return new List<SearchResult>();
            }
        }

        

        private void ListBoxSearchResults_DoubleClick(object sender, EventArgs e)
        {
            int selectedIndex = listBoxSearchResults.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < searchResults.Count)
            {
                var selectedSong = searchResults[selectedIndex];

                if (selectedSong.IsOnline)
                {
                    // Play or download the online song
                    PlaySpotifySong(selectedSong.Url);
                }
                else
                {
                    // Find the index in the original songFiles list
                    int songIndex = songFiles.IndexOf(selectedSong.FilePath);
                    if (songIndex >= 0)
                    {
                        currentIndex = songIndex;
                        PlaySong(currentIndex);
                    }
                }
            }
        }
       


    }
}