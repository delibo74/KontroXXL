using KontroXXL.Core.Configuration;
using Xunit;

namespace KontroXXL.Core.Tests.Configuration;

public class JsonFileStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "kx-cfg-" + Guid.NewGuid().ToString("N"));

    public JsonFileStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    string P(string name) => Path.Combine(_dir, name);

    [Fact]
    public void WriteAtomic_creates_the_file_with_the_given_content()
    {
        JsonFileStore.WriteAtomic(P("a.json"), "{\"x\":1}");
        Assert.Equal("{\"x\":1}", File.ReadAllText(P("a.json")));
    }

    [Fact]
    public void WriteAtomic_leaves_no_temp_file_behind()
    {
        JsonFileStore.WriteAtomic(P("a.json"), "{}");
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void WriteAtomic_replaces_existing_content_completely()
    {
        JsonFileStore.WriteAtomic(P("a.json"), "uzun-uzun-uzun-iceriik");
        JsonFileStore.WriteAtomic(P("a.json"), "kisa");
        Assert.Equal("kisa", File.ReadAllText(P("a.json")));
    }

    [Fact]
    public void WriteAtomic_creates_missing_directories()
    {
        string nested = Path.Combine(_dir, "x", "y", "a.json");
        JsonFileStore.WriteAtomic(nested, "{}");
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void ReadOrNull_returns_null_for_a_missing_file()
        => Assert.Null(JsonFileStore.ReadOrNull(P("yok.json")));

    [Fact]
    public void ReadOrNull_returns_content_for_an_existing_file()
    {
        File.WriteAllText(P("a.json"), "veri");
        Assert.Equal("veri", JsonFileStore.ReadOrNull(P("a.json")));
    }
}
