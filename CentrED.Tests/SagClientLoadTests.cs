using ClassicUO.Assets;
using ClassicUO.IO;
using ClassicUO.Utility;

namespace CentrED.Tests;

/// <summary>
/// End-to-end check against a real UO-Sagas client installation: loads the
/// same set of loaders MapManager.Load uses, straight from the encrypted
/// .sag files. Runs only on machines that have the client installed.
/// </summary>
public class SagClientLoadTests
{
    [Fact]
    public void LoadsRealSagasClientInstall()
    {
        var clientPath = Constants.DEFAULT_CLIENT_PATH;
        if (!Directory.Exists(clientPath))
        {
            return; // client not installed on this machine
        }

        // Version detection from the encrypted tiledata, as MapManager does.
        var plaintextSize = SagCrypto.GetPlaintextSize(Path.Combine(clientPath, "tiledata.sag"));
        Assert.True(plaintextSize > 0, "tiledata.sag matched no known .sag format");
        var clientVersion = plaintextSize switch
        {
            >= 3188736 => ClientVersion.CV_7090,
            >= 1644544 => ClientVersion.CV_7000,
            _ => ClientVersion.CV_6000
        };

        // Structural sanity of the decrypted tiledata: CV_7090 tiledata is
        // 3188736 bytes and starts with a header flag dword — garbage output
        // would not parse into named land tiles (checked below via TileData).
        Assert.Equal(0, plaintextSize % 4);

        using var manager = new UOFileManager(clientVersion, clientPath);

        // .sag is preferred over plain .mul, matching the game client (the
        // install ships stale tiledata.mul/hues.mul next to the real .sag).
        Assert.EndsWith(".sag", manager.GetUOFilePath("art.mul"));
        Assert.EndsWith(".sag", manager.GetUOFilePath("texmaps.mul"));
        Assert.EndsWith(".sag", manager.GetUOFilePath("tiledata.mul"));

        // Same loader set as MapManager.Load.
        manager.Arts.Load();
        manager.Hues.Load();
        manager.TileData.Load();
        manager.Texmaps.Load();
        manager.AnimData.Load();
        manager.Lights.Load();
        manager.Multis.Load();

        Assert.True(manager.TileData.LandData.Length >= 0x4000, "land tiledata not loaded");
        Assert.True(manager.TileData.StaticData.Length > 0, "static tiledata not loaded");
        Assert.Contains(manager.TileData.LandData.Take(512), t => !string.IsNullOrEmpty(t.Name));

        Assert.True(manager.Arts.File.Entries.Length > 0x4000, "art entries not loaded");
        Assert.Contains(Enumerable.Range(0, 512), i => manager.Arts.File.GetValidRefEntry(i).Length > 0);

        Assert.True(manager.Texmaps.File.Entries.Length > 0, "texmap entries not loaded");
        Assert.True(manager.Hues.HuesCount > 0, "hues not loaded");
        Assert.True(manager.AnimData.AnimDataFile.Length > 0, "animdata not loaded");
    }
}
