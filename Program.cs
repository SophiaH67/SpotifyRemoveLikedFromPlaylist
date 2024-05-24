// See https://aka.ms/new-console-template for more information
using SpotifyAPI.Web;
using Microsoft.Extensions.Logging;
using Swan;

#if DEBUG
LogLevel logLevel = LogLevel.Debug;
#else
LogLevel logLevel = LogLevel.Information;
#endif

using ILoggerFactory logging = LoggerFactory.Create(builder =>
  builder
    .AddConsole()
    .AddFilter(level => level >= logLevel)
);

// Get playlist ID from command line
string playlistId = Environment.GetCommandLineArgs()[1];

if (string.IsNullOrEmpty(playlistId))
{
  Console.WriteLine("Usage: SpotifyRemoveLikedFromPlaylist <playlistId>");
  return;
}

SpotifyAuth auth = new();
var token = await auth.GetAccessToken();

ILogger Logger = logging.CreateLogger("SpotifyRemoveLikedFromPlaylist");

Logger.LogDebug("Retrieved token " + token);

var config = SpotifyClientConfig
  .CreateDefault()
  .WithToken(token)
  .WithRetryHandler(new SimpleRetryHandler() { RetryAfter = TimeSpan.FromSeconds(1) });
var spotify = new SpotifyClient(config);


Logger.LogInformation("Grabbing playlist tracks");
var playlist = await spotify.Playlists.Get(playlistId);
var tracks = await spotify.PaginateAll(playlist.Tracks);

List<FullTrack> tracksToScan = [];

Logger.LogInformation("Parsing playlist tracks");
foreach (PlaylistTrack<IPlayableItem> unknownTrack in tracks)
{
  if (unknownTrack.Track is not FullTrack)
  {
    Logger.LogWarning("Found unsupported track in playlist");
    Logger.LogDebug(unknownTrack.ToJson());
    continue;
  }

  FullTrack track = unknownTrack.Track as FullTrack;

  tracksToScan.Add(track);
}

var toCheckIds = tracksToScan.Select(t => t.Id).ToArray();

// var checkedTracks = await spotify.Library.CheckTracks(new(toCheckIds));
// Check in batches of 20
Logger.LogInformation("Checking liked status on found tracks");
bool[] checkedTracks = new bool[toCheckIds.Length];
for (int j = 0; j < toCheckIds.Length; j += 20)
{
  var batch = toCheckIds.Skip(j).Take(20);
  var batchCheck = await spotify.Library.CheckTracks(new(batch.ToList()));
  for (int k = 0; k < batchCheck.Count; k++)
  {
    checkedTracks[j + k] = batchCheck[k];
  }
}

List<FullTrack> tracksToRemove = [];
var i = 0;
foreach (var track in tracksToScan)
{
  bool isLiked = checkedTracks[i];

  if (isLiked)
  {
    tracksToRemove.Add(track);
  }

  i++;
}


// Finally, remove the tracks
if (tracksToRemove.Count > 0)
{
  Logger.LogInformation("Removing " + tracksToRemove.Count + " tracks");
  var removeItemsTracks = new List<PlaylistRemoveItemsRequest.Item>();
  foreach (var track in tracksToRemove)
  {
    removeItemsTracks.Add(new PlaylistRemoveItemsRequest.Item { Uri = track.Uri });
  }

  PlaylistRemoveItemsRequest request = new()
  {
    Tracks = removeItemsTracks
  };
  await spotify.Playlists.RemoveItems(playlistId, request);
}
else
{
  Logger.LogInformation("No tracks to remove");
  return;
}
