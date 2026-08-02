internal sealed record BeatSaverPlaylist(
    string PlaylistTitle,
    string PlaylistAuthor,
    string PlaylistDescription,
    string Image,
    Dictionary<string, object?> CustomData,
    List<BeatSaverPlaylistSong> Songs);

internal sealed record BeatSaverPlaylistSong(
    string Key,
    string Hash,
    string SongName);
