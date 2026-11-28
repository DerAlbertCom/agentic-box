using System.Text.Json;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;

namespace Abox.Features.Shared.Credentials;

/// <summary>
/// The inputs of secrets that are not secret, such as a user name, kept in plain text in
/// abox's state after a value was stored, so the next time offers them as the default.
/// Keyed by service and input name. A secret or fixed input is never kept here.
/// </summary>
internal sealed class RememberedInputs(AboxPaths paths, IConsole console)
{
    private string FilePath => Path.Combine(paths.StateDirectory, "secret-inputs.json");

    /// <summary>The remembered value of an input, or null.</summary>
    public string? Get(string service, string input) =>
        Load().GetValueOrDefault(service)?.GetValueOrDefault(input);

    /// <summary>Remembers the inputs of a service, replacing what it had.</summary>
    public void Save(string service, IReadOnlyDictionary<string, string> inputs)
    {
        var all = Load();
        if (inputs.Count == 0 && !all.ContainsKey(service))
        {
            return;
        }

        all.Remove(service);
        if (inputs.Count > 0)
        {
            all[service] = inputs.ToDictionary(StringComparer.Ordinal);
        }

        Write(all);
    }

    /// <summary>Forgets the inputs of a service, for example when its secret is removed.</summary>
    public void Forget(string service) => Save(service, new Dictionary<string, string>());

    /// <summary>
    /// The remembered inputs. An unreadable file is warned about and treated as empty: a
    /// lost default must not stop a command.
    /// </summary>
    private Dictionary<string, Dictionary<string, string>> Load()
    {
        if (!File.Exists(FilePath))
        {
            return new(StringComparer.Ordinal);
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                File.ReadAllText(FilePath)
            );
            return stored is null
                ? new(StringComparer.Ordinal)
                : new(stored, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            console.WriteError(
                $"Warning: the remembered inputs in '{FilePath}' are not readable and are ignored: {exception.Message}"
            );
            return new(StringComparer.Ordinal);
        }
    }

    private void Write(Dictionary<string, Dictionary<string, string>> all)
    {
        Directory.CreateDirectory(paths.StateDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
    }
}
