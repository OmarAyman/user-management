using System.Reflection;
using System.Xml.Linq;
using UserManagement.Domain.Constants;

namespace UserManagement.UnitTests.Localization;

/// <summary>
/// Keeps the resource files honest.
/// </summary>
/// <remarks>
/// A missing Arabic string does not fail at runtime - it falls back to English, silently. That is the most
/// common i18n defect and the one most likely to survive a demo, because the page still renders. So it fails
/// the build here instead.
/// </remarks>
public sealed class ResourceParityTests
{
    private const string EnglishFile = "ErrorMessages.resx";
    private const string ArabicFile = "ErrorMessages.ar.resx";

    [Fact]
    public void Both_resource_files_declare_exactly_the_same_keys()
    {
        var english = ReadKeys(EnglishFile);
        var arabic = ReadKeys(ArabicFile);

        var missingFromArabic = english.Except(arabic, StringComparer.Ordinal).Order().ToList();
        var extraInArabic = arabic.Except(english, StringComparer.Ordinal).Order().ToList();

        Assert.Empty(missingFromArabic);
        Assert.Empty(extraInArabic);
    }

    [Fact]
    public void Every_error_code_has_a_title_and_a_detail_entry_in_both_files()
    {
        var english = ReadKeys(EnglishFile);
        var arabic = ReadKeys(ArabicFile);

        foreach (var code in ErrorCodeValues())
        {
            Assert.Contains($"Title.{code}", english);
            Assert.Contains($"Title.{code}", arabic);
            Assert.Contains($"Detail.{code}", english);
            Assert.Contains($"Detail.{code}", arabic);
        }
    }

    [Fact]
    public void Every_message_key_the_application_uses_exists_in_both_files()
    {
        var english = ReadKeys(EnglishFile);
        var arabic = ReadKeys(ArabicFile);

        foreach (var key in MessageKeys.All)
        {
            Assert.Contains(key, english);
            Assert.Contains(key, arabic);
        }
    }

    [Fact]
    public void No_arabic_value_is_left_as_english_text()
    {
        var arabic = ReadEntries(ArabicFile);

        foreach (var (key, value) in arabic)
        {
            if (string.IsNullOrEmpty(value))
            {
                // The 500 detail is deliberately empty in both files: an unexpected failure must not describe
                // itself. An empty value is a decision, not an untranslated string.
                continue;
            }

            // Arabic text contains characters in the Arabic Unicode block. A value with none is a copy of the
            // English that somebody forgot to translate - which the key-parity test cannot catch.
            Assert.True(
                value.Any(character => character >= '\u0600' && character <= '\u06FF'),
                $"'{key}' has no Arabic characters: \"{value}\"");
        }
    }

    private static IEnumerable<string> ErrorCodeValues() =>
        typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    private static HashSet<string> ReadKeys(string fileName) =>
        [.. ReadEntries(fileName).Select(entry => entry.Key)];

    /// <summary>
    /// Reads the resx from source rather than from the compiled assembly, so the test checks the file a
    /// developer edits.
    /// </summary>
    private static List<KeyValuePair<string, string>> ReadEntries(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "UserManagement.Api", "Resources", fileName);

        Assert.True(File.Exists(path), $"Resource file not found: {path}");

        return
        [
            .. XDocument.Load(path).Root!
                .Elements("data")
                .Select(element => new KeyValuePair<string, string>(
                    element.Attribute("name")!.Value,
                    element.Element("value")?.Value ?? string.Empty)),
        ];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UserManagement.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
