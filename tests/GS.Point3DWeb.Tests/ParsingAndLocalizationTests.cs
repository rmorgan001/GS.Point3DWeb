using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GS.Alpaca.Support;
using GS.Point3DWeb.Localization;
using GS.Point3DWeb.Support;
using GS.Point3DWeb.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GS.Point3DWeb.Tests;

public class TelescopeMappingsTests
{
    private static AlpacaResponse Parse(string json) => JsonConvert.DeserializeObject<AlpacaResponse>(json)!;

    private static string State(params string[] entries) =>
        $$"""{"ClientTransactionID":1,"ServerTransactionID":2,"ErrorNumber":0,"ErrorMessage":"","Value":[{{string.Join(",", entries)}}]}""";

    [Fact]
    public void A_full_state()
    {
        var state = TelescopeMappings.MapDeviceState(Parse(State(
            """{"Name":"Altitude","Value":30.5}""", """{"Name":"Azimuth","Value":120}""",
            """{"Name":"Declination","Value":-12.25}""", """{"Name":"RightAscension","Value":5.5}""",
            """{"Name":"SiderealTime","Value":7}""", """{"Name":"AtHome","Value":false}""",
            """{"Name":"AtPark","Value":true}""", """{"Name":"IsPulseGuiding","Value":false}""",
            """{"Name":"SideOfPier","Value":1}""", """{"Name":"Slewing","Value":false}""",
            """{"Name":"Tracking","Value":true}""", """{"Name":"UTCDate","Value":"2026-11-13T10:00:00.1234567Z"}""",
            """{"Name":"TimeStamp","Value":"2026-11-13T10:00:00"}""", """{"Name":"SomethingNew","Value":{"a":1}}""")));
        Assert.Equal((30.5, 120, -12.25, 5.5, 7), (state.Altitude, state.Azimuth, state.Declination, state.RightAscension, state.SiderealTime));
        Assert.Equal((false, true, false, 1, false, true), (state.AtHome!.Value, state.AtPark!.Value, state.IsPulseGuiding!.Value, state.SideOfPier!.Value, state.Slewing!.Value, state.Tracking!.Value));
        Assert.Equal(new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc).AddTicks(1234567), state.UTCDate!.Value.ToUniversalTime());
    }

    [Fact]
    public void Missing_or_odd_entries_are_skipped()
    {
        var state = TelescopeMappings.MapDeviceState(Parse(State(
            """{"Name":"RightAscension","Value":5.5}""", """{"Value":3}""", """{"Name":"Tracking"}""",
            """{"Name":"Slewing","Value":null}""", "42", "\"text\"", """{"Name":null,"Value":1}""",
            """{"Name":"SideOfPier","Value":"west"}""", """{"Name":"UTCDate","Value":"13/45/2026"}""",
            """{"Name":"AtHome","Value":"maybe"}""")));
        Assert.Equal(5.5, state.RightAscension);
        Assert.Null(state.Tracking);
        Assert.Null(state.Slewing);
        Assert.Null(state.SideOfPier);
        Assert.Null(state.UTCDate);
        Assert.Null(state.AtHome);
    }

    [Fact]
    public void An_unreadable_position_fails_the_whole_state()
        => Assert.ThrowsAny<Exception>(() => TelescopeMappings.MapDeviceState(Parse(State("""{"Name":"Declination","Value":"north"}"""))));

    [Fact]
    public void Booleans_as_numbers_and_numbers_as_strings()
    {
        var state = TelescopeMappings.MapDeviceState(Parse(State("""{"Name":"Tracking","Value":1}""", """{"Name":"RightAscension","Value":"5.5"}""")));
        Assert.True(state.Tracking);
        Assert.Equal(5.5, state.RightAscension);
    }

    [Fact]
    public void A_transaction_id_above_int_max_is_read()
    {
        var response = Parse("""{"ClientTransactionID":4294967295,"ServerTransactionID":4294967295,"ErrorNumber":0,"Value":true}""");
        Assert.Equal(4294967295L, response.ClientTransactionId);
    }

    [Fact]
    public void Mount_status_treats_a_time_without_a_zone_as_utc()
    {
        var unspecified = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Unspecified);
        var status = Model3DViewModel.MountStatus.From(new TelescopeDeviceState { Tracking = true, UTCDate = unspecified })!;
        Assert.Equal(DateTimeKind.Utc, status.UtcDate!.Value.Kind);
        Assert.Equal(10, status.UtcDate.Value.Hour);
        Assert.Null(Model3DViewModel.MountStatus.From(new TelescopeDeviceState { SideOfPier = 1 }));
    }
}

public class LocalizationTests
{
    private static readonly Func<string, IReadOnlyDictionary<string, string>?> TableFor =
        code => (IReadOnlyDictionary<string, string>?)typeof(L).Assembly.GetType("GS.Point3DWeb.Localization.Translations")!
            .GetMethod("For", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [code]);

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de", "de")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-Hans-CN", "zh-Hans")]
    [InlineData("FR-ca", "fr")]
    [InlineData("es-MX", "es")]
    [InlineData("it-CH", "it")]
    [InlineData("en-GB", "en")]
    public void Find_walks_up_to_a_supported_language(string culture, string expected) => Assert.Equal(expected, Languages.Find(culture)?.Code);

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("zh-TW")] // Traditional Chinese isn't offered: English, not Simplified
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a culture!!")]
    [InlineData("x-y-z-very-long-garbage-0000000000000000000000000000")]
    public void Unsupported_languages_fall_back(string? culture) => Assert.Null(Languages.Find(culture));

    [Fact]
    public void T_uses_the_current_ui_language_and_formats_with_it()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Hinzufügen", L.T("Add"));
            Assert.Equal("Bereits in der Liste als X", L.T("Already in the list as {0}", "X"));
            Assert.Equal("No translation for this", L.T("No translation for this"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    /// <summary>Every L.T("…") literal in the app's code and pages.</summary>
    public static IReadOnlyCollection<string> UsedKeys { get; } = FindKeys();

    private static IReadOnlyCollection<string> FindKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var call = new Regex("""L\.T\(\s*"((?:[^"\\]|\\.)*)"\s*[,)]""", RegexOptions.Compiled);
        foreach (var file in Directory.EnumerateFiles(Repo.WebProject, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".razor"))
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                 && !Path.GetFileName(f).StartsWith("Translations")))
        {
            foreach (Match m in call.Matches(File.ReadAllText(file)))
                keys.Add(Regex.Unescape(m.Groups[1].Value));
        }
        return keys;
    }

    public static TheoryData<string> Translated => new() { "de", "fr", "it", "es", "zh-Hans" };

    [Fact]
    public void The_source_scan_finds_the_keys() => Assert.True(UsedKeys.Count > 150, $"only {UsedKeys.Count} keys found");

    [Theory]
    [MemberData(nameof(Translated))]
    public void Every_used_text_is_translated(string code)
    {
        var table = TableFor(code)!;
        var missing = UsedKeys.Where(k => !table.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{code} is missing {missing.Count}:\n  " + string.Join("\n  ", missing));
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void Every_translation_keeps_the_placeholders(string code)
    {
        var placeholder = new Regex(@"\{(\d+)(?:[^}]*)\}");
        var problems = new List<string>();
        foreach (var (english, text) in TableFor(code)!)
        {
            // The address hint's literal "{n}" isn't a placeholder (that text is never formatted): it must come through.
            if (english.Contains("{n}"))
            {
                if (!text.Contains("{n}")) problems.Add($"lost {{n}}: {text}");
                continue;
            }
            var want = placeholder.Matches(english).Select(m => m.Groups[1].Value).Order().ToList();
            var got = placeholder.Matches(text).Select(m => m.Groups[1].Value).Order().ToList();
            if (!want.SequenceEqual(got)) problems.Add($"{english}  =>  {text}");
            try
            {
                _ = string.Format(CultureInfo.InvariantCulture, text, "a", "b", "c", "d");
            }
            catch (FormatException)
            {
                problems.Add($"bad format string: {text}");
            }
        }
        Assert.True(problems.Count == 0, $"{code}:\n  " + string.Join("\n  ", problems));
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void No_translation_is_blank_or_left_over(string code)
    {
        var table = TableFor(code)!;
        Assert.DoesNotContain(table, kv => string.IsNullOrWhiteSpace(kv.Value));
        var unused = table.Keys.Where(k => !UsedKeys.Contains(k)).ToList();
        // Texts reached without a literal L.T("…") call (enum display names, colour names…) are allowed; report the
        // rest so stale entries are noticed.
        Assert.True(unused.Count < 60, $"{code} has {unused.Count} entries no L.T(\"…\") uses:\n  " + string.Join("\n  ", unused));
    }

    [Fact]
    public void Every_language_has_a_help_page()
    {
        var help = Path.Combine(Repo.WebProject, "Components", "Help");
        foreach (var language in Languages.All)
        {
            var name = "Help" + string.Concat(language.Code.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant())) + ".razor";
            Assert.True(File.Exists(Path.Combine(help, name)), name);
        }
    }

    [Fact]
    public void Short_codes() => Assert.Equal(["EN", "DE", "FR", "IT", "ES", "ZH"], Languages.All.Select(l => l.ShortCode));
}

public class LogFilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gsp3w-log-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private FileInfo Write(string text)
    {
        var path = Path.Combine(_dir, "point3dweb-20261113.log");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return new FileInfo(path);
    }

    [Fact]
    public void First_read_returns_the_whole_lines()
    {
        var file = Write("﻿one\r\ntwo\nthree");
        var chunk = LogFiles.Read(file, -1, 1000);
        Assert.Equal(["one", "two"], chunk.Lines.Select(l => l.Text));
        Assert.True(chunk.FromStart);
        Assert.False(chunk.Truncated);
        Assert.Equal(0, chunk.Lines[0].Offset);
        Assert.Equal(Encoding.UTF8.GetByteCount("﻿one\r\n"), chunk.Lines[0].Bytes);
        Assert.Equal(chunk.Lines[1].Offset + chunk.Lines[1].Bytes, chunk.End);
    }

    [Fact]
    public void A_line_still_being_written_waits_for_its_end()
    {
        var file = Write("one\ntw");
        var first = LogFiles.Read(file, -1, 1000);
        Assert.Single(first.Lines);
        File.AppendAllText(file.FullName, "o\nthr");
        var next = LogFiles.Read(file, first.End, 1000);
        Assert.False(next.FromStart);
        Assert.Equal(["two"], next.Lines.Select(l => l.Text));
        var none = LogFiles.Read(file, next.End, 1000);
        Assert.Empty(none.Lines);
        Assert.Equal(next.End, none.End);
    }

    [Fact]
    public void Nothing_new_returns_nothing()
    {
        var file = Write("a\n");
        var chunk = LogFiles.Read(file, 2, 1000);
        Assert.Empty(chunk.Lines);
        Assert.Equal(2, chunk.End);
    }

    [Fact]
    public void A_big_file_starts_from_its_last_part_at_a_line_start()
    {
        var file = Write(string.Concat(Enumerable.Range(0, 1000).Select(i => $"line {i:0000}\n")));
        var chunk = LogFiles.Read(file, -1, 100);
        Assert.True(chunk.Truncated);
        Assert.All(chunk.Lines, l => Assert.Matches(@"^line \d{4}$", l.Text));
        Assert.Equal("line 0999", chunk.Lines[^1].Text);
        Assert.Equal(file.Length, chunk.End);
    }

    [Fact]
    public void A_replaced_file_starts_over()
    {
        var file = Write("short\n");
        var chunk = LogFiles.Read(file, 10_000, 1000);
        Assert.True(chunk.FromStart);
        Assert.Equal(["short"], chunk.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Falling_far_behind_starts_over()
    {
        var file = Write(new string('x', 5000) + "\nlast\n");
        var chunk = LogFiles.Read(file, 0, 100);
        Assert.True(chunk.FromStart && chunk.Truncated);
        Assert.Equal(["last"], chunk.Lines.Select(l => l.Text));
    }

    [Fact]
    public void A_single_line_longer_than_the_limit_is_not_shown_in_pieces()
    {
        var file = Write(new string('x', 5000));
        var chunk = LogFiles.Read(file, -1, 100);
        Assert.Empty(chunk.Lines);
        Assert.Equal(-1, chunk.End);
    }

    [Fact]
    public void Multi_byte_text_is_kept_whole()
    {
        var file = Write("中文 ünïcödé ✓\n");
        Assert.Equal("中文 ünïcödé ✓", LogFiles.Read(file, -1, 1000).Lines[0].Text);
    }

    [Fact]
    public void Can_read_while_another_writer_has_it_open()
    {
        var file = Write("");
        using var writer = new FileStream(file.FullName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        writer.Write("held\n"u8);
        writer.Flush();
        Assert.Equal(["held"], LogFiles.Read(file, -1, 1000).Lines.Select(l => l.Text));
    }
}
