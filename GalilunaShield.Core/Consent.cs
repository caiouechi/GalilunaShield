using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalilunaShield;

/// <summary>Current versions of the legal documents. Bump a version to force operators to re-accept.</summary>
public static class LegalVersions
{
    public const string Eula = "1.0";
    public const string Privacy = "1.0";
    public const string AcceptableUse = "1.0";

    /// <summary>A single string that changes whenever any document changes, stored in the consent record.</summary>
    public static string Combined => $"eula:{Eula};privacy:{Privacy};aup:{AcceptableUse}";
}

/// <summary>A timestamped record that the operator accepted the terms and affirmed their authority.</summary>
public sealed record ConsentRecord
{
    public string CombinedVersion { get; init; } = "";
    public string EulaVersion { get; init; } = "";
    public string PrivacyVersion { get; init; } = "";
    public string AcceptableUseVersion { get; init; } = "";
    public DateTimeOffset AcceptedAt { get; init; }
    public string WindowsUser { get; init; } = "";
    public string MachineName { get; init; } = "";
    public string AppVersion { get; init; } = "";
    /// <summary>The exact attestations the operator ticked (label -> true).</summary>
    public Dictionary<string, bool> Attestations { get; init; } = new();
    /// <summary>Optional child age band the operator selected, for their own record.</summary>
    public string? ChildAgeBand { get; init; }
}

/// <summary>Reads and writes the consent record, and decides whether the operator must (re-)consent.</summary>
public sealed class ConsentManager
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Path { get; }

    public ConsentManager(string? path = null)
    {
        Path = path ?? AppPaths.ConsentFile;
    }

    public ConsentRecord? Read()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            return JsonSerializer.Deserialize<ConsentRecord>(File.ReadAllText(Path), Options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when there is no consent on file, or it was for an older version of the documents.</summary>
    public bool NeedsConsent()
    {
        var record = Read();
        return record is null || record.CombinedVersion != LegalVersions.Combined;
    }

    public ConsentRecord Save(IReadOnlyDictionary<string, bool> attestations, string? childAgeBand, string appVersion)
    {
        var record = new ConsentRecord
        {
            CombinedVersion = LegalVersions.Combined,
            EulaVersion = LegalVersions.Eula,
            PrivacyVersion = LegalVersions.Privacy,
            AcceptableUseVersion = LegalVersions.AcceptableUse,
            AcceptedAt = DateTimeOffset.Now,
            WindowsUser = SafeUser(),
            MachineName = SafeMachine(),
            AppVersion = appVersion,
            Attestations = new Dictionary<string, bool>(attestations),
            ChildAgeBand = childAgeBand,
        };
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(record, Options));
        return record;
    }

    private static string SafeUser()
    {
        try { return Environment.UserName; } catch { return "unknown"; }
    }

    private static string SafeMachine()
    {
        try { return Environment.MachineName; } catch { return "unknown"; }
    }
}
